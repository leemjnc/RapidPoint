namespace RapidPoint;

internal static class StableInputRunner
{
    internal static void Execute(MacroConfiguration config, CancellationToken token, Action<int> wait) =>
        Prepare(config, token, wait)();

    internal static Action Prepare(MacroConfiguration config, CancellationToken token, Action<int> wait)
    {
        Action move = () => MacroEngine.MovePointer(config);
        Func<bool> active = () => MacroEngine.IsTargetActive(config);
        return () => Execute(config, token, NativeMethods.SetKeys, NativeMethods.SetMouseButton, move, active, wait);
    }

    internal static void Execute(MacroConfiguration config, CancellationToken token,
        Action<Keys[], bool> keys, Action<MouseButtonKind, bool> mouse,
        Action move, Func<bool> active, Action<int> wait)
    {
        void Check()
        {
            token.ThrowIfCancellationRequested();
            if (!active()) throw new OperationCanceledException("대상 창이 비활성화되었습니다.");
        }
        void TapMouse(MouseButtonKind button)
        {
            Check();
            try { mouse(button, true); wait(Math.Clamp(config.SequenceHoldMs, 1, 1000)); }
            finally { mouse(button, false); }
        }
        Check();
        if (config.FirstStepMouseEnabled) TapMouse(config.FirstStepMouseButton);
        if (config.KeyboardStepEnabled)
        {
            var chord = PauseSkillRunner.SingleKey(config.KeyboardKey);
            Check();
            try { keys(chord, true); wait(Math.Clamp(config.SequenceHoldMs, 1, 1000)); }
            finally { keys(chord, false); }
        }
        if (config.MouseStepEnabled)
        {
            if (config.KeyboardStepEnabled || config.FirstStepMouseEnabled)
                wait(Math.Clamp(config.BeforeClickMs, 0, 1000));
            Check();
            move();
            if (config.MouseAction == InputActionKind.MouseClick) TapMouse(config.MouseButton);
        }
    }
}
