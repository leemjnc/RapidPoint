using System.Reflection;

namespace RapidPoint;

internal static class MouseAndMonitorTests
{
    internal static void Run()
    {
        var config = new MacroConfiguration(123, 234, CoordinateSpace.Screen, IntPtr.Zero,
            1280, 720, false, true, MouseButtonKind.Left, true, InputActionKind.MouseClick,
            MouseButtonKind.Left, Keys.None, 10, RepeatMode.Hold, 1)
            { StableInput = false, SequenceHoldMs = 10, BeforeClickMs = 0 };
        if (!config.UsesTimedInput || config.EffectiveHoldMs != 20 || config.EffectiveGapMs != 20)
            throw new Exception("Existing fast mouse-first macro bypasses click protection.");
        for (var canceledWait = 0; canceledWait <= 4; canceledWait++)
        {
            var trace = new List<string>();
            using var cancel = new CancellationTokenSource();
            var waitNumber = 0;
            try
            {
                StableInputRunner.Execute(config, cancel.Token, (_, _) => throw new Exception("Unexpected key"),
                    (_, down) => trace.Add(down ? "down" : "up"), () => trace.Add("move"), () => true,
                    ms => { trace.Add("wait:" + ms); if (++waitNumber == canceledWait) { cancel.Cancel(); cancel.Token.ThrowIfCancellationRequested(); } },
                    readCursor: () => new NativeMethods.Point { X = 500, Y = 600 },
                    restoreCursor: point => trace.Add($"return:{point.X},{point.Y}"));
                if (canceledWait != 0) throw new Exception("Cancellation ignored.");
            }
            catch (OperationCanceledException) when (canceledWait != 0) { }
            if (trace.Count(s => s == "down") != trace.Count(s => s == "up"))
                throw new Exception("Mouse-first cancellation left mouse held.");
            if (canceledWait == 0 && !trace.SequenceEqual(new[] { "down", "wait:20", "up", "wait:20", "move", "down", "wait:20", "up", "wait:20", "return:500,600" }))
                throw new Exception("First click moved before its hold and release gap.");
            if (canceledWait <= 2 && canceledWait != 0 && trace.Contains("move"))
                throw new Exception("Canceled first stage moved to second coordinate.");
            if (canceledWait != 0 && trace.Any(s => s.StartsWith("return:")))
                throw new Exception("Hard cancellation moved the mouse back.");
        }
        var focus = true;
        var moves = 0;
        var releases = 0;
        try
        {
            StableInputRunner.Execute(config, CancellationToken.None, (_, _) => { },
                (_, down) => { if (!down) releases++; }, () => moves++, () => focus, _ => focus = false,
                readCursor: () => default, restoreCursor: _ => throw new Exception("Returned after focus loss"));
            throw new Exception("Focus loss ignored.");
        }
        catch (OperationCanceledException) { }
        if (moves != 0 || releases != 1) throw new Exception("Focus-loss cleanup failed.");
        var slower = config with { SequenceHoldMs = 40, BeforeClickMs = 50 };
        if (slower.EffectiveHoldMs != 40 || slower.EffectiveGapMs != 50)
            throw new Exception("Configured longer timings were shortened.");
        TestReturnCycles(config);

        var binding = new CoordinateBindingSettings { CoordinateSpace = CoordinateSpace.TargetWindow };
        using var editor = new MacroEditorDialog(new RapidMacroSettings
        {
            FirstStepKind = MacroFirstStepKind.MouseClick, StableInput = false,
            BindingId = binding.Id, SequenceHoldMs = 10, BeforeClickMs = 0
        }, [binding], []);
        typeof(MacroEditorDialog).GetMethod("SaveAndClose", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(editor, null);
        if (editor.Result is not { StableInput: false, SequenceHoldMs: 20, BeforeClickMs: 20 })
            throw new Exception("Editor timing does not match actual mouse protection.");

        using var target = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(200, 100), ClientSize = new Size(1280, 720) };
        _ = target.Handle;
        foreach (var size in new[] { new Size(1280, 720), new Size(1600, 900), new Size(960, 540) })
        {
            target.ClientSize = size;
            target.Location = new Point(target.Left - 70, target.Top + 23);
            var local = new NativeMethods.Point { X = size.Width / 2, Y = size.Height / 2 };
            var screen = local;
            if (!NativeMethods.ClientToScreen(target.Handle, ref screen) ||
                !CoordinateReadout.TryClientPoint(target.Handle, screen, out var read, out var bounds) ||
                read.X != local.X || read.Y != local.Y || bounds.Width != size.Width || bounds.Height != size.Height)
                throw new Exception($"HUD/picker coordinate mismatch after move/resize: requested {size}, actual {target.ClientSize}, screen {screen.X},{screen.Y}.");
            var text = CoordinateReadout.Format(screen, read, bounds.Width, bounds.Height, binding);
            if (!text.Contains($"창 X: {local.X}  Y: {local.Y}") || !text.Contains("X: 640  Y: 360"))
                throw new Exception("HUD reference coordinate scaling mismatch.");
        }
        using var overlay = new CoordinateMonitorOverlay();
        var parameters = (CreateParams)typeof(CoordinateMonitorOverlay).GetProperty("CreateParams", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(overlay)!;
        if ((parameters.ExStyle & CoordinateMonitorOverlay.MonitorStyles) != CoordinateMonitorOverlay.MonitorStyles ||
            overlay.TransparencyKey != overlay.BackColor || overlay.Opacity != 1 || overlay.ShowInTaskbar)
            throw new Exception("HUD may intercept input, tint the game or take focus.");
        var noActivation = (bool)typeof(CoordinateMonitorOverlay).GetProperty("ShowWithoutActivation", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(overlay)!;
        if (!noActivation) throw new Exception("HUD activation not disabled.");
    }

    private static void TestReturnCycles(MacroConfiguration config)
    {
        foreach (var action in new[] { InputActionKind.MouseClick, InputActionKind.MouseMove })
        {
            var cursor = new NativeMethods.Point { X = -400, Y = 500 };
            var origin = cursor;
            var snapshots = 0;
            var returns = 0;
            var clicks = new List<NativeMethods.Point>();
            for (var cycle = 0; cycle < 10000; cycle++)
            {
                // A new manual aim between cycles must not be overwritten by an old session snapshot.
                if (cycle == 5000) cursor = origin = new NativeMethods.Point { X = 800, Y = 600 };
                clicks.Clear();
                StableInputRunner.Execute(config with { MouseAction = action }, CancellationToken.None,
                    (_, _) => throw new Exception("Unexpected key"),
                    (_, down) => { if (down) clicks.Add(cursor); },
                    () => cursor = new NativeMethods.Point { X = config.X, Y = config.Y },
                    () => true, _ => { }, readCursor: () => { snapshots++; return cursor; },
                    restoreCursor: point => { returns++; cursor = point; });
                if (cursor.X != origin.X || cursor.Y != origin.Y ||
                    clicks[0].X != origin.X || clicks[0].Y != origin.Y ||
                    clicks.Count != (action == InputActionKind.MouseClick ? 2 : 1))
                    throw new Exception("Repeated first click drifted or return emitted an extra click.");
                if (action == InputActionKind.MouseClick && (clicks[1].X != config.X || clicks[1].Y != config.Y))
                    throw new Exception("Second binding action changed.");
            }
            if (returns != 10000 || snapshots != 10000) throw new Exception("Missing per-cycle cursor restore.");
        }

        var focused = true;
        var waits = 0;
        var restored = false;
        try
        {
            StableInputRunner.Execute(config, CancellationToken.None, (_, _) => { }, (_, _) => { },
                () => { }, () => focused, _ => { if (++waits == 4) focused = false; },
                readCursor: () => default, restoreCursor: _ => restored = true);
            throw new Exception("Return ignored last-moment focus loss.");
        }
        catch (OperationCanceledException) { }
        if (restored) throw new Exception("Mouse returned over another application.");

        var edges = 0;
        try
        {
            StableInputRunner.Execute(config, CancellationToken.None, (_, _) => { }, (_, _) => edges++,
                () => { }, () => true, _ => { },
                readCursor: () => throw new InvalidOperationException("test read failure"), restoreCursor: _ => { });
            throw new Exception("Missing cursor snapshot was ignored.");
        }
        catch (InvalidOperationException ex) when (ex.Message == "test read failure") { }
        if (edges != 0) throw new Exception("Clicked before obtaining a return location.");
    }
}
