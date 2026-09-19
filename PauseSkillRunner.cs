using System.Collections.Concurrent;

namespace RapidPoint;

// Receives replaceable output functions so order and cancellation can be tested without game input.
internal static class PauseSkillRunner
{
    private static readonly ConcurrentDictionary<Keys, Keys[]> SingleKeys = new();
    private static readonly ConcurrentDictionary<Keys, Keys[]> Chords = new();
    private static readonly ConcurrentDictionary<Keys, Keys[]> ReleasedChords = new();
    internal static Keys[] SingleKey(Keys key) => SingleKeys.GetOrAdd(key, static key => [key]);
    internal static void Execute(MacroConfiguration config, CancellationToken token)
    {
        using var delay = new PrecisionInputWait();
        ExecuteTimed(config, token, ms => delay.Delay(ms, token));
    }

    internal static void ExecuteTimed(MacroConfiguration config, CancellationToken token, Action<int> wait) =>
        Prepare(config, token, wait)();

    internal static Action Prepare(MacroConfiguration config, CancellationToken token, Action<int> wait)
    {
        Action move = () => MacroEngine.MovePointer(config);
        Func<bool> active = () => MacroEngine.IsTargetActive(config);
        return () => Execute(config, token, NativeMethods.SetKeys, NativeMethods.SetMouseButton, move, active, wait);
    }

    internal static void Execute(
        MacroConfiguration config, CancellationToken token,
        Action<Keys[], bool> keys, Action<MouseButtonKind, bool> mouse,
        Action move, Func<bool> active, Action<int> wait)
    {
        void Check()
        {
            token.ThrowIfCancellationRequested();
            if (!active()) throw new OperationCanceledException("대상 창이 비활성화되었습니다.");
        }
        void TapKeys(Keys[] chord)
        {
            Check();
            try
            {
                keys(chord, true);
                wait(Math.Clamp(config.SequenceHoldMs, 1, 1000));
            }
            finally
            {
                // Also release after cancellation or a partially accepted SendInput request.
                keys(chord.Length == 1 ? chord :
                    ReleasedChords.GetOrAdd(config.KeyboardKey, static key => [key, Keys.Escape]), false);
            }
        }
        void TapMouse()
        {
            Check();
            move();
            Check();
            try
            {
                mouse(MouseButtonKind.Left, true);
                wait(Math.Clamp(config.SequenceHoldMs, 1, 1000));
            }
            finally
            {
                mouse(MouseButtonKind.Left, false);
            }
        }
        Check();
        switch (config.PauseSequenceKind)
        {
            case PauseSequenceKind.ClickThenEscape:
                TapMouse();
                wait(Math.Clamp(config.BeforePauseMs, 0, 1000));
                TapKeys(SingleKey(Keys.Escape));
                return;
            case PauseSequenceKind.EscapeThenClick:
                TapKeys(SingleKey(Keys.Escape));
                wait(Math.Clamp(config.BeforeClickMs, 0, 1000));
                TapMouse();
                return;
            case PauseSequenceKind.EscapeWithKey:
                TapKeys(Chords.GetOrAdd(config.KeyboardKey, static key => [Keys.Escape, key]));
                return;
            case PauseSequenceKind.Skill:
                break;
            default:
                throw new InvalidOperationException("지원하지 않는 퍼즈 입력 모드입니다.");
        }
        TapKeys(Chords.GetOrAdd(config.KeyboardKey, static key => [Keys.Escape, key]));
        wait(Math.Clamp(config.BeforeClickMs, 0, 1000));
        TapMouse();
        if (config.PauseAfterClick)
        {
            wait(Math.Clamp(config.BeforePauseMs, 0, 1000));
            TapKeys(SingleKey(Keys.Escape));
        }
    }
}
