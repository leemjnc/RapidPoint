using System.Text.Json;

namespace RapidPoint;

internal static class PauseSkillTests
{
    internal static void Run()
    {
        var settings = new RapidMacroSettings
        {
            PauseSkillSequence = true, PauseAfterClick = true,
            SequenceHoldMs = 7, BeforeClickMs = 11, BeforePauseMs = 13
        };
        var restored = JsonSerializer.Deserialize<RapidMacroSettings>(
            JsonSerializer.Serialize(settings.Clone(), SettingsStore.JsonOptions), SettingsStore.JsonOptions)!;
        if (!restored.PauseSkillSequence || !restored.PauseAfterClick ||
            restored.SequenceHoldMs != 7 || restored.BeforeClickMs != 11 || restored.BeforePauseMs != 13)
            throw new Exception("Sequence settings round trip failed.");
        var legacy = JsonSerializer.Deserialize<RapidMacroSettings>("{}", SettingsStore.JsonOptions)!;
        if (legacy.PauseSkillSequence || legacy.PauseSequenceKind != PauseSequenceKind.Skill)
            throw new Exception("Legacy macros must keep their existing modes.");
        var previousSkill = JsonSerializer.Deserialize<RapidMacroSettings>("{\"PauseSkillSequence\":true}", SettingsStore.JsonOptions)!;
        if (!previousSkill.PauseSkillSequence || previousSkill.PauseSequenceKind != PauseSequenceKind.Skill)
            throw new Exception("Existing pause skills changed mode during upgrade.");
        foreach (var kind in Enum.GetValues<PauseSequenceKind>())
        {
            settings.PauseSequenceKind = kind;
            var copy = JsonSerializer.Deserialize<RapidMacroSettings>(
                JsonSerializer.Serialize(settings.Clone(), SettingsStore.JsonOptions), SettingsStore.JsonOptions)!;
            if (copy.PauseSequenceKind != kind) throw new Exception("Sequence kind was not preserved.");
            TestEditor(kind);
        }

        var config = new MacroConfiguration(100, 200, CoordinateSpace.Screen, IntPtr.Zero,
            1280, 720, true, false, MouseButtonKind.Left, true, InputActionKind.MouseClick,
            MouseButtonKind.Left, Keys.D2, 20, RepeatMode.Count, 1)
        {
            PauseSkillSequence = true, PauseAfterClick = true,
            SequenceHoldMs = 7, BeforeClickMs = 11, BeforePauseMs = 13
        };
        List<string> RunTrace(MacroConfiguration c, int cancelAtWait = 0, int inactiveAt = 0)
        {
            var trace = new List<string>();
            using var cancellation = new CancellationTokenSource();
            var waitCount = 0;
            var checks = 0;
            try
            {
                PauseSkillRunner.Execute(c, cancellation.Token,
                    (keys, down) => trace.Add(string.Join("+", keys) + (down ? ":down" : ":up")),
                    (_, down) => trace.Add(down ? "mouse:down" : "mouse:up"),
                    () => trace.Add("move"),
                    () => ++checks != inactiveAt,
                    ms =>
                    {
                        trace.Add("wait:" + ms);
                        if (++waitCount == cancelAtWait)
                        {
                            cancellation.Cancel();
                            cancellation.Token.ThrowIfCancellationRequested();
                        }
                    });
                if (cancelAtWait > 0 || inactiveAt > 0) throw new Exception("Cancellation was not observed.");
            }
            catch (OperationCanceledException) when (cancelAtWait > 0 || inactiveAt > 0) { }
            return trace;
        }
        var full = RunTrace(config);
        string[] expected =
        [
            "Escape+D2:down", "wait:7", "D2+Escape:up", "wait:11",
            "move", "mouse:down", "wait:7", "mouse:up", "wait:13",
            "Escape:down", "wait:7", "Escape:up"
        ];
        if (!full.SequenceEqual(expected)) throw new Exception("Sequence order mismatch.");
        if (!RunTrace(config with { PauseAfterClick = false }).SequenceEqual(expected.Take(8)))
            throw new Exception("Optional repause mismatch.");
        var canceledChord = RunTrace(config, cancelAtWait: 1);
        if (canceledChord[^1] != "D2+Escape:up" || canceledChord.Contains("mouse:down"))
            throw new Exception("Chord cancellation did not release keys.");
        var canceledMouse = RunTrace(config, cancelAtWait: 3);
        if (canceledMouse[^1] != "mouse:up" || canceledMouse.Contains("Escape:down"))
            throw new Exception("Mouse cancellation did not release button.");
        var focusLost = RunTrace(config, inactiveAt: 3);
        if (focusLost.Contains("move") || focusLost.Contains("mouse:down"))
            throw new Exception("Sequence continued after losing target focus.");

        var clickFirst = config with { PauseSequenceKind = PauseSequenceKind.ClickThenEscape };
        string[] clickFirstExpected = ["move", "mouse:down", "wait:7", "mouse:up", "wait:13",
            "Escape:down", "wait:7", "Escape:up"];
        if (!RunTrace(clickFirst).SequenceEqual(clickFirstExpected)) throw new Exception("Click-ESC order mismatch.");
        var escFirst = config with { PauseSequenceKind = PauseSequenceKind.EscapeThenClick };
        string[] escFirstExpected = ["Escape:down", "wait:7", "Escape:up", "wait:11",
            "move", "mouse:down", "wait:7", "mouse:up"];
        if (!RunTrace(escFirst).SequenceEqual(escFirstExpected)) throw new Exception("ESC-click order mismatch.");
        if (!RunTrace(config with { PauseSequenceKind = PauseSequenceKind.EscapeWithKey }).SequenceEqual(expected.Take(3)))
            throw new Exception("Keys-only mode emitted a click.");
        foreach (var cycle in new[] { clickFirst, escFirst })
        {
            for (var at = 1; at <= 3; at++)
            {
                var canceled = RunTrace(cycle, cancelAtWait: at);
                if (canceled.Count(s => s.EndsWith(":down")) != canceled.Count(s => s.EndsWith(":up")))
                    throw new Exception("Cycle cancellation left an input pressed.");
            }
            if (RunTrace(cycle, inactiveAt: 1).Any()) throw new Exception("Inactive window received input.");
            var interrupted = RunTrace(cycle, inactiveAt: 4);
            if (interrupted.Count(s => s.EndsWith(":down")) != 1 ||
                interrupted.Count(s => s.EndsWith(":up")) != 1)
                throw new Exception("Focus loss did not stop the next input in the pair.");
        }
        TestRepeatingEngine(clickFirst);
    }

    private static void TestEditor(PauseSequenceKind kind)
    {
        var settings = new RapidMacroSettings
        {
            PauseSkillSequence = true, PauseSequenceKind = kind, RepeatMode = RepeatMode.Hold,
            KeyboardKey = (int)Keys.D2, BindingId = RapidMacroSettings.CurrentCursorBindingId
        };
        using var dialog = new MacroEditorDialog(settings, [], []);
        typeof(MacroEditorDialog).GetMethod("SaveAndClose",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(dialog, null);
        var saved = dialog.Result ?? throw new Exception("Editor did not save.");
        var cycle = kind is PauseSequenceKind.ClickThenEscape or PauseSequenceKind.EscapeThenClick;
        if (!saved.PauseSkillSequence || saved.PauseSequenceKind != kind ||
            saved.RepeatMode != (cycle ? RepeatMode.Hold : RepeatMode.Count) ||
            saved.KeyboardEnabled == cycle || saved.MouseEnabled == (kind == PauseSequenceKind.EscapeWithKey))
            throw new Exception("Editor mode controls did not preserve the intended sequence.");
    }

    private static void TestRepeatingEngine(MacroConfiguration config)
    {
        using var completed = new ManualResetEventSlim();
        var executions = 0;
        string? error = null;
        var lastFinished = 0L;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        using var engine = new MacroEngine(_ => throw new Exception("Wrong runner."), (_, token) =>
        {
            if (executions > 0 && clock.ElapsedMilliseconds - lastFinished < 15)
                throw new Exception("Timed cycle ran a catch-up burst.");
            if (token.WaitHandle.WaitOne(30)) token.ThrowIfCancellationRequested();
            Interlocked.Increment(ref executions);
            lastFinished = clock.ElapsedMilliseconds;
        });
        engine.Completed += (_, result) => { error = result.ErrorMessage; completed.Set(); };
        engine.Start(config with { RepeatMode = RepeatMode.Count, RepeatCount = 3, IntervalMs = 20 });
        if (!completed.Wait(3000) || error != null || executions != 3 || engine.ClickCount != 3)
            throw new Exception("Counted pause cycles failed: " + error);

        using var entered = new ManualResetEventSlim();
        using var stopped = new ManualResetEventSlim();
        var released = false;
        using var hold = new MacroEngine(_ => { }, (_, token) =>
        {
            try { entered.Set(); token.WaitHandle.WaitOne(); token.ThrowIfCancellationRequested(); }
            finally { released = true; }
        });
        hold.Completed += (_, _) => stopped.Set();
        hold.Start(config with { RepeatMode = RepeatMode.Hold });
        if (!entered.Wait(3000)) throw new Exception("Held cycle did not start.");
        hold.Stop();
        if (!stopped.Wait(3000) || !released || hold.IsRunning) throw new Exception("Held cycle did not stop.");
    }
}
