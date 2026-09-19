using System.Diagnostics;

namespace RapidPoint;

internal sealed record MacroConfiguration(
    int X,
    int Y,
    CoordinateSpace CoordinateSpace,
    IntPtr TargetWindow,
    int ReferenceWidth,
    int ReferenceHeight,
    bool KeyboardStepEnabled,
    bool FirstStepMouseEnabled,
    MouseButtonKind FirstStepMouseButton,
    bool MouseStepEnabled,
    InputActionKind MouseAction,
    MouseButtonKind MouseButton,
    Keys KeyboardKey,
    int IntervalMs,
    RepeatMode RepeatMode,
    int RepeatCount)
{
    public bool PauseSkillSequence { get; init; }
    public bool StableInput { get; init; }
    public PauseSequenceKind PauseSequenceKind { get; init; }
    public bool RepeatingPause => PauseSkillSequence &&
        PauseSequenceKind is PauseSequenceKind.ClickThenEscape or PauseSequenceKind.EscapeThenClick;
    public bool PauseAfterClick { get; init; }
    public int SequenceHoldMs { get; init; } = 10;
    public int BeforeClickMs { get; init; } = 10;
    public int BeforePauseMs { get; init; } = 10;
}

internal sealed class MacroCompletedEventArgs(string? errorMessage, bool reachedCount) : EventArgs
{
    public string? ErrorMessage { get; } = errorMessage;
    public bool ReachedCount { get; } = reachedCount;
}

internal sealed class MacroEngine : IDisposable
{
    private readonly Action<MacroConfiguration> _clickAction;
    private readonly Action<MacroConfiguration, CancellationToken> _sequenceAction;
    private readonly bool _nativeExecution;
    private readonly bool _nativeSequence;
    private readonly object _sync = new();
    private static readonly object InputCycleSync = new();
    private CancellationTokenSource? _cancellation;
    private CancellationTokenSource? _betweenCycles;
    private volatile bool _stopRequested;
    private Task? _worker;
    private long _clickCount;
    private bool _running;

    public event EventHandler<MacroCompletedEventArgs>? Completed;

    public bool IsRunning
    {
        get { lock (_sync) return _running; }
    }

    public long ClickCount => Interlocked.Read(ref _clickCount);

    public MacroEngine() : this(ExecuteConfiguration)
    {
    }

    internal static void ExecuteOnce(MacroConfiguration configuration)
    {
        lock (InputCycleSync) ExecuteConfiguration(configuration);
    }

    internal static bool IsTargetActive(MacroConfiguration config) => config.TargetWindow == IntPtr.Zero ||
        (NativeMethods.IsWindow(config.TargetWindow) &&
         NativeMethods.GetAncestor(NativeMethods.GetForegroundWindow(), NativeMethods.GaRoot) == config.TargetWindow);

    internal static void MovePointer(MacroConfiguration configuration)
    {
        if (configuration.CoordinateSpace == CoordinateSpace.CurrentCursor)
        {
            return;
        }

        var point = ResolveScreenPoint(configuration);
        if (!NativeMethods.SetCursorPos(point.X, point.Y))
        {
            throw new InvalidOperationException("지정한 좌표로 마우스를 이동할 수 없습니다.");
        }
    }

    private static void ExecuteConfiguration(MacroConfiguration configuration)
    {
        if (!IsTargetActive(configuration)) throw new OperationCanceledException();
        if (configuration.FirstStepMouseEnabled)
        {
            NativeMethods.Click(configuration.FirstStepMouseButton);
        }

        if (configuration.KeyboardStepEnabled &&
            configuration.MouseStepEnabled &&
            configuration.MouseAction == InputActionKind.MouseClick)
        {
            MovePointer(configuration);
            NativeMethods.PressKeyAndClick(configuration.KeyboardKey, configuration.MouseButton);
            return;
        }

        if (configuration.KeyboardStepEnabled)
        {
            NativeMethods.PressKey(configuration.KeyboardKey);
        }

        if (configuration.MouseStepEnabled)
        {
            MovePointer(configuration);
            if (configuration.MouseAction == InputActionKind.MouseClick)
            {
                NativeMethods.Click(configuration.MouseButton);
            }
        }
    }

    internal MacroEngine(Action<MacroConfiguration> clickAction,
        Action<MacroConfiguration, CancellationToken>? sequenceAction = null)
    {
        _clickAction = clickAction;
        _nativeExecution = clickAction == ExecuteConfiguration;
        _nativeSequence = sequenceAction == null;
        _sequenceAction = sequenceAction ?? PauseSkillRunner.Execute;
    }

    public void Start(MacroConfiguration configuration)
    {
        CancellationToken token;
        lock (_sync)
        {
            if (_running)
            {
                return;
            }

            _running = true;
            _stopRequested = false;
            Interlocked.Exchange(ref _clickCount, 0);
            _cancellation = new CancellationTokenSource();
            _betweenCycles = new CancellationTokenSource();
            token = _cancellation.Token;
            if (configuration.PauseSkillSequence || configuration.StableInput || _nativeExecution)
            {
                if (configuration.PauseSkillSequence && !configuration.RepeatingPause)
                    configuration = configuration with { RepeatMode = RepeatMode.Count, RepeatCount = 1 };
                _worker = configuration.PauseSkillSequence
                    ? DedicatedPauseWorker.Shared.Enqueue(() => Run(configuration, token, false))
                    : Task.Run(() => Run(configuration, token, false));
                return;
            }
        }

        try
        {
            lock (InputCycleSync) _clickAction(configuration);
            var count = Interlocked.Increment(ref _clickCount);
            if (configuration.RepeatMode == RepeatMode.Count && count >= configuration.RepeatCount)
            {
                CompleteBeforeWorker(null, true);
                return;
            }
        }
        catch (Exception exception)
        {
            CompleteBeforeWorker(exception.Message, false);
            return;
        }

        if (token.IsCancellationRequested)
        {
            CompleteBeforeWorker(null, false);
            return;
        }

        lock (_sync)
        {
            if (!_running)
            {
                return;
            }
            _worker = Task.Run(() => Run(configuration, token, true));
        }
    }

    public void Stop(bool finishCurrentCycle = false)
    {
        lock (_sync)
        {
            _stopRequested = true;
            _betweenCycles?.Cancel();
            if (!finishCurrentCycle) _cancellation?.Cancel();
        }
    }

    private void CompleteBeforeWorker(string? error, bool reachedCount)
    {
        CancellationTokenSource? cancellation;
        lock (_sync)
        {
            _running = false;
            cancellation = _cancellation;
            _cancellation = null;
            _betweenCycles?.Dispose();
            _betweenCycles = null;
            _worker = null;
        }
        cancellation?.Dispose();
        Completed?.Invoke(this, new MacroCompletedEventArgs(error, reachedCount));
    }

    private void Run(MacroConfiguration configuration, CancellationToken token, bool firstExecutionCompleted)
    {
        string? error = null;
        var reachedCount = false;
        var originalPriority = Thread.CurrentThread.Priority;
        NativeMethods.timeBeginPeriod(1);

        try
        {
            try
            {
                Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
            }
            catch
            {
                // 일부 환경에서 우선순위 변경이 거부되어도 입력은 계속합니다.
            }

            using var delay = new PrecisionInputWait();
            var interval = Math.Max(1, configuration.IntervalMs);
            var waitBeforeCycle = firstExecutionCompleted;
            var betweenToken = _betweenCycles!.Token;
            Action<int> wait = ms => delay.Delay(ms, token);
            var prepared = configuration.PauseSkillSequence && _nativeSequence
                ? PauseSkillRunner.Prepare(configuration, token, wait)
                : !configuration.PauseSkillSequence && configuration.StableInput
                    ? StableInputRunner.Prepare(configuration, token, wait) : null;

            while (!token.IsCancellationRequested && !_stopRequested)
            {
                if (waitBeforeCycle) delay.Delay(interval, betweenToken);
                token.ThrowIfCancellationRequested();
                while (!Monitor.TryEnter(InputCycleSync, 2))
                {
                    token.ThrowIfCancellationRequested();
                    if (_stopRequested) return;
                }
                try
                {
                    token.ThrowIfCancellationRequested();
                    if (_stopRequested) break;
                    if (!IsTargetActive(configuration)) throw new OperationCanceledException();
                    if (prepared != null) prepared();
                    else if (configuration.PauseSkillSequence)
                        _sequenceAction(configuration, token);
                    else
                        _clickAction(configuration);
                }
                finally { Monitor.Exit(InputCycleSync); }
                var count = Interlocked.Increment(ref _clickCount);

                if (configuration.RepeatMode == RepeatMode.Count && count >= configuration.RepeatCount)
                {
                    reachedCount = true;
                    break;
                }

                // Every mode preserves a release gap; never send overdue cycles in a burst.
                waitBeforeCycle = true;
            }
        }
        catch (OperationCanceledException)
        {
            // 정상 중지
        }
        catch (Exception exception)
        {
            error = exception.Message;
        }
        finally
        {
            NativeMethods.timeEndPeriod(1);
            try
            {
                Thread.CurrentThread.Priority = originalPriority;
            }
            catch
            {
                // 종료 과정에서는 우선순위 복원 실패를 무시합니다.
            }
            lock (_sync)
            {
                _running = false;
                _cancellation?.Dispose();
                _cancellation = null;
                _betweenCycles?.Dispose();
                _betweenCycles = null;
                _worker = null;
            }

            Completed?.Invoke(this, new MacroCompletedEventArgs(error, reachedCount));
        }
    }

    internal static NativeMethods.Point ResolveScreenPoint(MacroConfiguration configuration)
    {
        if (configuration.CoordinateSpace == CoordinateSpace.Screen)
        {
            return new NativeMethods.Point { X = configuration.X, Y = configuration.Y };
        }

        if (configuration.TargetWindow == IntPtr.Zero || !NativeMethods.IsWindow(configuration.TargetWindow))
        {
            throw new InvalidOperationException("연결한 대상 창을 찾을 수 없습니다. 창 목록에서 대상을 다시 선택하세요.");
        }

        if (!NativeMethods.GetClientRect(configuration.TargetWindow, out var clientRectangle) ||
            clientRectangle.Width <= 0 || clientRectangle.Height <= 0)
        {
            throw new InvalidOperationException("대상 창의 크기를 읽을 수 없습니다.");
        }

        var referenceWidth = Math.Max(1, configuration.ReferenceWidth);
        var referenceHeight = Math.Max(1, configuration.ReferenceHeight);
        var point = ScaleMappedPoint(
            configuration.X,
            configuration.Y,
            clientRectangle.Width,
            clientRectangle.Height,
            referenceWidth,
            referenceHeight);
        if (!NativeMethods.ClientToScreen(configuration.TargetWindow, ref point))
        {
            throw new InvalidOperationException("창 내부 좌표를 화면 좌표로 변환하지 못했습니다.");
        }

        return point;
    }

    internal static NativeMethods.Point ScaleMappedPoint(
        int x,
        int y,
        int clientWidth,
        int clientHeight,
        int referenceWidth,
        int referenceHeight) => new()
    {
        X = (int)Math.Round(x * clientWidth / (double)Math.Max(1, referenceWidth)),
        Y = (int)Math.Round(y * clientHeight / (double)Math.Max(1, referenceHeight))
    };

    public void Dispose()
    {
        Task? worker;
        lock (_sync)
        {
            _stopRequested = true;
            _betweenCycles?.Cancel();
            _cancellation?.Cancel();
            worker = _worker;
        }

        try
        {
            worker?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // 종료 중 취소 예외는 무시합니다.
        }
    }
}
