namespace RapidPoint;

internal static class StableInputRunner
{
    internal static void Execute(MacroConfiguration config, CancellationToken token, Action<int> wait) =>
        Prepare(config, token, wait)();

    internal static Action Prepare(MacroConfiguration config, CancellationToken token, Action<int> wait)
    {
        var secondDestination = config;
        Action captureSecond = () =>
        {
            secondDestination = CaptureSecondDestination(config, static () =>
            {
                if (!NativeMethods.GetCursorPos(out var point))
                    throw new InvalidOperationException("2단계에 사용할 마우스 위치를 읽지 못했습니다.");
                return point;
            });
        };
        Action move = () => MacroEngine.MovePointer(secondDestination);
        Func<bool> active = () => MacroEngine.IsTargetActive(config);
        return () => Execute(config, token, NativeMethods.SetKeys, NativeMethods.SetMouseButton,
            move, active, wait, MacroEngine.MovePointer, captureSecond,
            static () =>
            {
                if (!NativeMethods.GetCursorPos(out var point))
                    throw new InvalidOperationException("복귀할 원래 마우스 위치를 읽지 못했습니다.");
                return point;
            },
            static point =>
            {
                if (!NativeMethods.SetCursorPos(point.X, point.Y))
                    throw new InvalidOperationException("원래 마우스 위치로 돌아갈 수 없습니다.");
            });
    }

    internal static MacroConfiguration CaptureSecondDestination(MacroConfiguration config,
        Func<NativeMethods.Point> getCursor)
    {
        if (config.FirstKeyBinding == null || !config.MouseStepEnabled ||
            config.CoordinateSpace != CoordinateSpace.CurrentCursor) return config;
        var point = getCursor();
        return config with { X = point.X, Y = point.Y, CoordinateSpace = CoordinateSpace.Screen };
    }

    internal static void Execute(MacroConfiguration config, CancellationToken token,
        Action<Keys[], bool> keys, Action<MouseButtonKind, bool> mouse,
        Action move, Func<bool> active, Action<int> wait,
        Action<MacroConfiguration>? moveFirst = null, Action? captureSecond = null,
        Func<NativeMethods.Point>? readCursor = null, Action<NativeMethods.Point>? restoreCursor = null)
    {
        void Check()
        {
            token.ThrowIfCancellationRequested();
            if (!active()) throw new OperationCanceledException("대상 창이 비활성화되었습니다.");
        }
        void TapMouse(MouseButtonKind button)
        {
            Check();
            try
            {
                mouse(button, true);
                if (config.UsesTimedInput) wait(config.EffectiveHoldMs);
            }
            finally { mouse(button, false); }
        }
        Check();
        var originalCursor = default(NativeMethods.Point);
        if (config.ReturnsToFirstCursor)
        {
            if (readCursor == null || restoreCursor == null)
                throw new InvalidOperationException("원래 마우스 위치 복귀 처리가 없습니다.");
            // Sample once per cycle, before any click or movement. A user's new aim
            // between cycles becomes the next origin instead of the saved binding point.
            originalCursor = readCursor();
        }
        captureSecond?.Invoke();
        if (config.FirstStepMouseEnabled) TapMouse(config.FirstStepMouseButton);
        if (config.FirstKeyBinding is { } binding)
        {
            // Execute the mapped action directly: reinjecting its hotkey is deliberately
            // ignored by the global hook, and re-enabling that hook would recurse.
            Check();
            (moveFirst ?? throw new InvalidOperationException("1단계 바인딩 이동 처리가 없습니다."))(binding);
            if (binding.MouseAction == InputActionKind.MouseClick) TapMouse(binding.MouseButton);
        }
        else if (config.KeyboardStepEnabled)
        {
            var chord = PauseSkillRunner.SingleKey(config.KeyboardKey);
            Check();
            try
            {
                keys(chord, true);
                if (config.UsesTimedInput) wait(config.EffectiveHoldMs);
            }
            finally { keys(chord, false); }
        }
        if (config.MouseStepEnabled)
        {
            if (config.UsesTimedInput && (config.KeyboardStepEnabled || config.FirstStepMouseEnabled))
                wait(config.EffectiveGapMs);
            Check();
            move();
            if (config.MouseAction == InputActionKind.MouseClick) TapMouse(config.MouseButton);
        }
        if (config.ReturnsToFirstCursor)
        {
            // Keep the second action's release gap before moving away. Normal stop
            // completes this return; hard cancellation/focus loss must not move the
            // pointer back over another application.
            wait(config.EffectiveGapMs);
            Check();
            restoreCursor!(originalCursor);
        }
    }
}
