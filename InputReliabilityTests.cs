using System.Diagnostics;
using System.Text.Json;

namespace RapidPoint;

internal static class InputReliabilityTests
{
    private static MacroConfiguration Configuration => new(0, 0, CoordinateSpace.CurrentCursor, IntPtr.Zero,
        1280, 720, true, false, MouseButtonKind.Left, true, InputActionKind.MouseClick,
        MouseButtonKind.Left, Keys.D2, 5, RepeatMode.Count, 1)
    { SequenceHoldMs = 7, BeforeClickMs = 11, StableInput = true };

    internal static void Run()
    {
        TestStableOrder();
        MappedMacroTests.Run();
        MouseAndMonitorTests.Run();
        MonitorVisualTests.Run();
        TestPauseStress();
        TestPackets();
        TestNoBurstAndStop();
        TestCycleSerialization();
        TestTimer();
        var legacy = JsonSerializer.Deserialize<RapidMacroSettings>("{}", SettingsStore.JsonOptions)!;
        if (!legacy.StableInput) throw new Exception("Stable input should default on.");
        legacy.StableInput = false;
        if (legacy.Clone().StableInput) throw new Exception("Fast mode choice was not cloned.");
        using var dialog = new MacroEditorDialog(new RapidMacroSettings
            { StableInput = true, SequenceHoldMs = 17, BeforeClickMs = 9 }, [], []);
        typeof(MacroEditorDialog).GetMethod("SaveAndClose",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(dialog, null);
        if (dialog.Result is not { StableInput: true, PauseSkillSequence: false, SequenceHoldMs: 17, BeforeClickMs: 9 })
            throw new Exception("Standard timing settings were not saved.");
    }

    private static void TestStableOrder()
    {
        foreach (var mouseFirst in new[] { false, true })
        {
            var config = Configuration with { KeyboardStepEnabled = !mouseFirst, FirstStepMouseEnabled = mouseFirst };
            for (var cancel = 0; cancel <= 3; cancel++)
            {
                var trace = new List<string>();
                using var source = new CancellationTokenSource();
                var waits = 0;
                try
                {
                    StableInputRunner.Execute(config, source.Token,
                        (_, down) => trace.Add(down ? "key+" : "key-"),
                        (_, down) => trace.Add(down ? "mouse+" : "mouse-"),
                        () => trace.Add("move"), () => true,
                        ms => { trace.Add("wait:" + ms); if (++waits == cancel) { source.Cancel(); source.Token.ThrowIfCancellationRequested(); } });
                    if (cancel != 0) throw new Exception("Expected cancellation.");
                }
                catch (OperationCanceledException) when (cancel != 0) { }
                if (trace.Count(s => s.EndsWith('+')) != trace.Count(s => s.EndsWith('-')))
                    throw new Exception("Stable mode left a key/button pressed.");
                string first = mouseFirst ? "mouse" : "key";
                if (cancel == 0 && !trace.SequenceEqual(new[] { first + "+", "wait:" + config.EffectiveHoldMs, first + "-", "wait:" + config.EffectiveGapMs, "move", "mouse+", "wait:" + config.EffectiveHoldMs, "mouse-" }))
                    throw new Exception("Stable sequence order mismatch.");
            }
        }
        // Exercise 10,000 complete cycles through the real sequence logic with a fake sink.
        var downs = 0;
        var ups = 0;
        for (var index = 0; index < 10000; index++)
            StableInputRunner.Execute(Configuration, CancellationToken.None,
                (_, down) => { if (down) downs++; else ups++; },
                (_, down) => { if (down) downs++; else ups++; }, () => { }, () => true, _ => { });
        if (downs != 20000 || ups != 20000) throw new Exception("10,000-cycle input accounting mismatch.");

        var checks = 0;
        var mouseInputs = 0;
        try
        {
            StableInputRunner.Execute(Configuration, CancellationToken.None, (_, _) => { },
                (_, _) => mouseInputs++, () => { }, () => ++checks < 3, _ => { });
            throw new Exception("Focus loss was ignored.");
        }
        catch (OperationCanceledException) { }
        if (mouseInputs != 0) throw new Exception("Mouse input escaped to an inactive window.");
    }

    private static void TestPackets()
    {
        var chord = PauseSkillRunner.SingleKey(Keys.D2);
        var packet = InputPackets.KeyChange(chord, true);
        if (!ReferenceEquals(packet, InputPackets.KeyChange(chord, true))) throw new Exception("Packets not cached.");
        var combined = InputPackets.TapAndClick(Keys.D2, MouseButtonKind.Left);
        if (combined.Length != 4 || combined[0].Union.Keyboard.Flags != 0 ||
            combined[1].Union.Keyboard.Flags != NativeMethods.KeyEventKeyUp ||
            combined[2].Union.Mouse.Flags != NativeMethods.MouseEventLeftDown ||
            combined[3].Union.Mouse.Flags != NativeMethods.MouseEventLeftUp)
            throw new Exception("Fast packet order mismatch.");
        if ((InputPackets.Tap(Keys.Right)[0].Union.Keyboard.Flags & NativeMethods.KeyEventExtendedKey) == 0)
            throw new Exception("Extended key flag lost.");
        for (uint accepted = 0; accepted < 4; accepted++)
        {
            var calls = 0;
            try
            {
                InputPackets.Send(combined, inputs =>
                {
                    if (++calls == 1) return accepted;
                    if (inputs.Any(input => input.Type == NativeMethods.InputKeyboard
                        ? (input.Union.Keyboard.Flags & NativeMethods.KeyEventKeyUp) == 0
                        : input.Union.Mouse.Flags != NativeMethods.MouseEventLeftUp))
                        throw new Exception("Cleanup replayed an input down.");
                    return (uint)inputs.Length;
                });
                throw new Exception("Partial failure not reported.");
            }
            catch (System.ComponentModel.Win32Exception) { }
            if (calls != 2) throw new Exception("Partial failure did not attempt release.");
        }
    }

    private static void TestPauseStress()
    {
        var config = Configuration with { PauseSkillSequence = true, PauseAfterClick = true };
        var downs = 0;
        var ups = 0;
        for (var i = 0; i < 10000; i++)
            PauseSkillRunner.Execute(config, CancellationToken.None,
                (keys, down) => { if (down) downs += keys.Length; else ups += keys.Length; },
                (_, down) => { if (down) downs++; else ups++; }, () => { }, () => true, _ => { });
        if (downs != 40000 || ups != 40000) throw new Exception("Pause stress test lost an input edge.");
    }

    private static void TestNoBurstAndStop()
    {
        using var done = new ManualResetEventSlim();
        var clock = Stopwatch.StartNew();
        var previousEnd = 0d;
        var calls = 0;
        string? error = null;
        using var engine = new MacroEngine(_ =>
        {
            if (calls > 0 && clock.Elapsed.TotalMilliseconds - previousEnd < 4)
                throw new Exception("Catch-up burst.");
            Thread.Sleep(15);
            calls++;
            previousEnd = clock.Elapsed.TotalMilliseconds;
        });
        engine.Completed += (_, result) => { error = result.ErrorMessage; done.Set(); };
        engine.Start(Configuration with { StableInput = false, RepeatCount = 5 });
        if (!done.Wait(4000) || calls != 5 || error != null) throw new Exception("No-burst test: " + error);

        using var entered = new ManualResetEventSlim();
        using var finish = new ManualResetEventSlim();
        using var stopped = new ManualResetEventSlim();
        var completedCycle = false;
        using var graceful = new MacroEngine(_ => { }, (_, token) =>
        {
            entered.Set();
            if (!finish.Wait(3000)) throw new Exception("Test timeout.");
            token.ThrowIfCancellationRequested();
            completedCycle = true;
        });
        graceful.Completed += (_, _) => stopped.Set();
        graceful.Start(Configuration with { PauseSkillSequence = true, PauseSequenceKind = PauseSequenceKind.ClickThenEscape, RepeatMode = RepeatMode.Hold });
        if (!entered.Wait(3000)) throw new Exception("Graceful cycle never started.");
        graceful.Stop(finishCurrentCycle: true);
        finish.Set();
        if (!stopped.Wait(3000) || !completedCycle || graceful.ClickCount != 1)
            throw new Exception("Normal stop interrupted a committed cycle.");
    }

    private static void TestCycleSerialization()
    {
        using var firstDone = new ManualResetEventSlim();
        using var secondDone = new ManualResetEventSlim();
        var active = 0;
        var overlap = 0;
        Action<MacroConfiguration> action = _ =>
        {
            if (Interlocked.Increment(ref active) != 1) Interlocked.Increment(ref overlap);
            Thread.Sleep(2);
            Interlocked.Decrement(ref active);
        };
        using var first = new MacroEngine(action);
        using var second = new MacroEngine(action);
        first.Completed += (_, _) => firstDone.Set();
        second.Completed += (_, _) => secondDone.Set();
        var config = Configuration with { StableInput = false, RepeatCount = 20, IntervalMs = 1 };
        first.Start(config);
        second.Start(config);
        if (!firstDone.Wait(4000) || !secondDone.Wait(4000) || overlap != 0 || first.ClickCount != 20 || second.ClickCount != 20)
            throw new Exception("Concurrent macro cycles interleaved or were lost.");
    }

    private static void TestTimer()
    {
        foreach (var fallback in new[] { false, true })
        {
            using var timer = new PrecisionInputWait(fallback);
            var clock = Stopwatch.StartNew();
            timer.Delay(7, CancellationToken.None);
            if (clock.Elapsed.TotalMilliseconds < 7) throw new Exception("Timer shortened a hold.");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            try { timer.Delay(1000, cancellation.Token); throw new Exception("Timer ignored cancellation."); }
            catch (OperationCanceledException) { }
            using var duringWait = new CancellationTokenSource();
            duringWait.CancelAfter(10);
            clock.Restart();
            try { timer.Delay(1000, duringWait.Token); throw new Exception("Timer ignored in-flight cancellation."); }
            catch (OperationCanceledException) { }
            if (clock.ElapsedMilliseconds > 800) throw new Exception("Timer cancellation was not responsive.");
        }
    }

    internal static int WriteReport(string path)
    {
        try
        {
            Run();
            PauseSkillTests.Run();
            using var timer = new PrecisionInputWait();
            NativeMethods.timeBeginPeriod(1);
            try
            {
                // Alternating samples reduce bias from changes in system load; no real input sent.
                var legacy = new List<double>();
                var current = new List<double>();
                using var source = new CancellationTokenSource();
                timer.Delay(10, source.Token);
                for (var i = 0; i < 200; i++)
                {
                    var start = Stopwatch.GetTimestamp();
                    source.Token.WaitHandle.WaitOne(10);
                    legacy.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                    start = Stopwatch.GetTimestamp();
                    timer.Delay(10, source.Token);
                    current.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                }
                string Stats(List<double> values)
                {
                    values.Sort();
                    return $"median={values[100]:F3}ms; p95={values[189]:F3}ms; max={values[^1]:F3}ms";
                }
                File.WriteAllText(path, "RapidPoint 1.11.5 input validation\nPASS\n" +
                    "PASS: one-pixel crosshair, single latest click replaces prior value, click-time coordinates retained on movement/resize\n" +
                    "PASS: readable HUD coordinates, cursor-centered crosshair, red click pixels, transparent background, bounded click history/expiry\n" +
                    "PASS: mouse-first cycles return to original cursor, updated aim per cycle, no return on hard cancellation/focus loss\n" +
                    "PASS: mouse-first fast setting protected, holds/gap before move, cancellation/focus cleanup\n" +
                    "PASS: HUD/picker shared client coordinates, reference scaling, transparent/no-activate styles\n" +
                    "PASS: mapped first-key action then second action, fresh cursor aim per cycle, raw-key opt-out\n" +
                    "10,000 simulated standard cycles: 20,000 downs / 20,000 ups\n" +
                    "10,000 simulated pause-skill cycles: 40,000 downs / 40,000 ups\n" +
                    "PASS: pause order, settings, canceled-key/mouse release, focus loss, partial send cleanup\n" +
                    "PASS: normal stop completes cycle, hard cancellation, concurrent cycles serialized, no catch-up bursts\n" +
                    $"High-resolution timer available: {timer.HighResolutionAvailable}\n" +
                    "10ms waits, 200 samples each (local synthetic measurement, not game latency):\n" +
                    "Previous WaitOne: " + Stats(legacy) + "\n" +
                    "New timer: " + Stats(current) + "\n" +
                    "No actual keyboard/mouse events were emitted. Game receipt/skill activation NOT verified.\n");
            }
            finally { NativeMethods.timeEndPeriod(1); }
            return 0;
        }
        catch (Exception error) { File.WriteAllText(path, "FAIL\n" + error); return 1; }
    }
}
