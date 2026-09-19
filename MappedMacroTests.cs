using System.Text.Json;

namespace RapidPoint;

internal static class MappedMacroTests
{
    internal static void Run()
    {
        var binding = new CoordinateBindingSettings
        {
            TriggerKey = (int)Keys.Q, Activation = BindingActivation.ReleaseClick,
            Name = "Q 좌표", X = 100, Y = 200, CoordinateSpace = CoordinateSpace.TargetWindow
        };
        var macro = JsonSerializer.Deserialize<RapidMacroSettings>(
            "{\"KeyboardKey\":81,\"StableInput\":false}", SettingsStore.JsonOptions)!;
        if (MacroBindingResolver.FindFirstKeyBinding(macro, [binding]) != binding)
            throw new Exception("Legacy Q macro did not resolve its enabled coordinate binding.");
        macro.UseFirstKeyBinding = false;
        if (MacroBindingResolver.FindFirstKeyBinding(macro, [binding]) != null || macro.Clone().UseFirstKeyBinding)
            throw new Exception("Raw-key opt-out ignored.");
        macro.UseFirstKeyBinding = true;
        macro.PauseSkillSequence = true;
        if (MacroBindingResolver.FindFirstKeyBinding(macro, [binding]) != null)
            throw new Exception("Pause skill must send the actual skill key.");
        macro.PauseSkillSequence = false;
        binding.Enabled = false;
        if (MacroBindingResolver.FindFirstKeyBinding(macro, [binding]) != null)
            throw new Exception("Disabled binding was executed.");
        binding.Enabled = true;

        var first = new MacroConfiguration(100, 200, CoordinateSpace.Screen, IntPtr.Zero, 1280, 720,
            false, false, MouseButtonKind.Left, true, InputActionKind.MouseClick, MouseButtonKind.Left,
            Keys.None, 10, RepeatMode.Count, 1);
        foreach (var stable in new[] { false, true })
        {
            var config = first with
            {
                KeyboardStepEnabled = true, KeyboardKey = Keys.Q, FirstKeyBinding = first,
                CoordinateSpace = CoordinateSpace.CurrentCursor, StableInput = stable,
                SequenceHoldMs = 7, BeforeClickMs = 11
            };
            var cursor = new NativeMethods.Point { X = 800, Y = 600 };
            var destination = config;
            var trace = new List<string>();
            void RunCycle() => StableInputRunner.Execute(config, CancellationToken.None,
                (_, _) => throw new Exception("Mapped Q was incorrectly reinjected as a keyboard event."),
                (_, down) => trace.Add($"{(down ? "down" : "up")}@{cursor.X},{cursor.Y}"),
                () => { cursor = new NativeMethods.Point { X = destination.X, Y = destination.Y }; trace.Add("second"); },
                () => true, ms => trace.Add("wait:" + ms),
                action => { cursor = new NativeMethods.Point { X = action.X, Y = action.Y }; trace.Add("first"); },
                () => { destination = StableInputRunner.CaptureSecondDestination(config, () => cursor); trace.Add("capture"); });
            RunCycle();
            var expected = new List<string> { "capture", "first", "down@100,200" };
            if (stable) expected.Add("wait:7");
            expected.Add("up@100,200");
            if (stable) expected.Add("wait:11");
            expected.AddRange(["second", "down@800,600"]);
            if (stable) expected.Add("wait:7");
            expected.Add("up@800,600");
            if (!trace.SequenceEqual(expected)) throw new Exception("Mapped first/second sequence mismatch.");

            // Each repetition samples a fresh user aim, not the prior binding destination.
            trace.Clear();
            cursor = new NativeMethods.Point { X = 900, Y = 700 };
            RunCycle();
            if (cursor.X != 900 || cursor.Y != 700 || trace.Count(s => s.StartsWith("down@")) != 2)
                throw new Exception("Second cycle reused stale cursor coordinates.");
            var saved = config with { CoordinateSpace = CoordinateSpace.TargetWindow, X = 300, Y = 400 };
            if (StableInputRunner.CaptureSecondDestination(saved, () => throw new Exception("Unneeded cursor snapshot")) != saved)
                throw new Exception("Saved second coordinate was changed.");

            using var cancellation = new CancellationTokenSource();
            var firstReleased = false;
            var secondRan = false;
            try
            {
                StableInputRunner.Execute(config, cancellation.Token,
                    (_, _) => { }, (_, down) => { if (down) cancellation.Cancel(); else firstReleased = true; },
                    () => secondRan = true, () => true, _ => cancellation.Token.ThrowIfCancellationRequested(),
                    _ => { });
                throw new Exception("Mapped cycle ignored cancellation.");
            }
            catch (OperationCanceledException) { }
            if (!firstReleased || secondRan) throw new Exception("Mapped cancellation did not release/stop correctly.");
        }

        using var editor = new MacroEditorDialog(macro, [binding], []);
        typeof(MacroEditorDialog).GetMethod("SaveAndClose",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(editor, null);
        if (editor.Result is not { UseFirstKeyBinding: true, KeyboardEnabled: true, KeyboardKey: (int)Keys.Q, MouseEnabled: true })
            throw new Exception("Editor did not retain both steps and binding resolution.");
    }
}
