using System.Text.Json;
using System.Drawing.Imaging;

namespace RapidPoint;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var validationArgument = Array.IndexOf(args, "--input-validation");
        if (validationArgument >= 0 && validationArgument + 1 < args.Length)
            return InputReliabilityTests.WriteReport(args[validationArgument + 1]);

        var monitorPreview = Array.IndexOf(args, "--render-monitor-preview");
        if (monitorPreview >= 0 && monitorPreview + 1 < args.Length)
        {
            using var monitor = new CoordinateMonitorOverlay();
            monitor.SetDisplay(CoordinateReadout.Describe(new NativeMethods.Point { X = 820, Y = 500 },
                new NativeMethods.Point { X = 720, Y = 450 }, 1600, 900,
                new CoordinateBindingSettings { Name = "바인딩 1", CoordinateSpace = CoordinateSpace.TargetWindow }), new Point(720, 450));
            monitor.AddPreviewClick(new Point(570, 320));
            _ = monitor.Handle;
            using var bitmap = new Bitmap(monitor.Width, monitor.Height);
            monitor.DrawToBitmap(bitmap, monitor.ClientRectangle);
            bitmap.MakeTransparent(Color.Magenta);
            bitmap.Save(args[monitorPreview + 1], ImageFormat.Png);
            return 0;
        }

        if (args.Any(arg => string.Equals(arg, "--smoke-test", StringComparison.OrdinalIgnoreCase)))
        {
            return RunSmokeTest();
        }

        if (args.Any(arg => string.Equals(arg, "--capture-flow-test", StringComparison.OrdinalIgnoreCase)))
        {
            return RunCoordinateCaptureFlowTest();
        }

        var previewArgument = Array.FindIndex(args, arg =>
            string.Equals(arg, "--render-preview", StringComparison.OrdinalIgnoreCase));
        if (previewArgument >= 0 && previewArgument + 1 < args.Length)
        {
            return RenderPreview(args[previewArgument + 1]);
        }

        var bindingPreviewArgument = Array.FindIndex(args, arg =>
            string.Equals(arg, "--render-binding-preview", StringComparison.OrdinalIgnoreCase));
        if (bindingPreviewArgument >= 0 && bindingPreviewArgument + 1 < args.Length)
        {
            return RenderBindingPreview(args[bindingPreviewArgument + 1]);
        }

        var macroPreviewArgument = Array.FindIndex(args, arg =>
            string.Equals(arg, "--render-macro-preview", StringComparison.OrdinalIgnoreCase));
        if (macroPreviewArgument >= 0 && macroPreviewArgument + 1 < args.Length)
        {
            var kind = macroPreviewArgument + 2 < args.Length &&
                Enum.TryParse<PauseSequenceKind>(args[macroPreviewArgument + 2], out var parsed)
                ? parsed : PauseSequenceKind.Skill;
            return RenderMacroPreview(args[macroPreviewArgument + 1],
                macroPreviewArgument + 2 < args.Length && args[macroPreviewArgument + 2] is "Standard" or "MouseFirst" ? null : kind,
                macroPreviewArgument + 2 < args.Length && args[macroPreviewArgument + 2] == "MouseFirst");
        }

        var windowPreviewArgument = Array.FindIndex(args, arg =>
            string.Equals(arg, "--render-window-preview", StringComparison.OrdinalIgnoreCase));
        if (windowPreviewArgument >= 0 && windowPreviewArgument + 1 < args.Length)
        {
            return RenderWindowPreview(args[windowPreviewArgument + 1]);
        }

        var overlayPreviewArgument = Array.FindIndex(args, arg =>
            string.Equals(arg, "--render-overlay-preview", StringComparison.OrdinalIgnoreCase));
        if (overlayPreviewArgument >= 0 && overlayPreviewArgument + 1 < args.Length)
        {
            return RenderOverlayPreview(args[overlayPreviewArgument + 1]);
        }

        Application.ThreadException += (_, eventArgs) =>
            MessageBox.Show(eventArgs.Exception.Message, "RapidPoint 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);

        using var form = new MainForm();
        Application.Run(form);
        return 0;
    }

    private static int RenderPreview(string outputPath)
    {
        try
        {
            // Isolated empty-state preview: never read or write personal settings.
            using var form = new MainForm(new AppSettings { MultipleMacrosInitialized = true });
            form.ShowInTaskbar = false;
            form.Opacity = 0;
            form.Show();
            Application.DoEvents();
            form.PerformLayout();
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(outputPath, ImageFormat.Png);
            form.Close();
            return 0;
        }
        catch
        {
            return 1;
        }
    }

    private static int RenderBindingPreview(string outputPath)
    {
        try
        {
            using var dialog = new BindingEditorDialog(
                new CoordinateBindingSettings
                {
                    Name = "창 비례 좌표 예시",
                    Activation = BindingActivation.ReleaseClick,
                    CoordinateSpace = CoordinateSpace.TargetWindow,
                    MouseAction = InputActionKind.MouseClick,
                    X = 640,
                    Y = 360,
                    ReferenceWidth = 1280,
                    ReferenceHeight = 720
                },
                IntPtr.Zero,
                []);
            dialog.ShowInTaskbar = false;
            dialog.Opacity = 0;
            dialog.Show();
            Application.DoEvents();
            dialog.PerformLayout();
            using var bitmap = new Bitmap(dialog.Width, dialog.Height);
            dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size));
            bitmap.Save(outputPath, ImageFormat.Png);
            dialog.Close();
            return 0;
        }
        catch
        {
            return 1;
        }
    }

    private static int RenderMacroPreview(string outputPath, PauseSequenceKind? kind, bool mouseFirst = false)
    {
        try
        {
            var previewBinding = new CoordinateBindingSettings
            {
                Name = "공격 버튼",
                TriggerKey = (int)Keys.D2,
                CoordinateSpace = CoordinateSpace.TargetWindow
            };
            using var dialog = new MacroEditorDialog(
                new RapidMacroSettings
                {
                    Name = kind == null ? "연타 매크로 2" : "퍼즈 스킬 2",
                    PauseSkillSequence = kind != null,
                    PauseSequenceKind = kind ?? PauseSequenceKind.Skill,
                    PauseAfterClick = true,
                    TriggerKey = (int)Keys.X,
                    FirstStepKind = mouseFirst ? MacroFirstStepKind.MouseClick : MacroFirstStepKind.KeyboardKey,
                    StableInput = false,
                    FirstStepMouseButton = MouseButtonKind.Left,
                    KeyboardKey = (int)Keys.D2,
                    BindingId = previewBinding.Id
                },
                [previewBinding],
                [Keys.D1, Keys.D2]);
            dialog.ShowInTaskbar = false;
            dialog.Opacity = 0;
            dialog.Show();
            Application.DoEvents();
            dialog.PerformLayout();
            using var bitmap = new Bitmap(dialog.Width, dialog.Height);
            dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size));
            bitmap.Save(outputPath, ImageFormat.Png);
            dialog.Close();
            return 0;
        }
        catch
        {
            return 1;
        }
    }

    private static int RenderWindowPreview(string outputPath)
    {
        try
        {
            using var dialog = new WindowSelectionDialog(IntPtr.Zero);
            dialog.ShowInTaskbar = false;
            dialog.Opacity = 0;
            dialog.Show();
            Application.DoEvents();
            dialog.PerformLayout();
            using var bitmap = new Bitmap(dialog.Width, dialog.Height);
            dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size));
            bitmap.Save(outputPath, ImageFormat.Png);
            dialog.Close();
            return 0;
        }
        catch
        {
            return 1;
        }
    }

    private static int RenderOverlayPreview(string outputPath)
    {
        try
        {
            using var target = new Form
            {
                ClientSize = new Size(800, 450),
                StartPosition = FormStartPosition.Manual,
                Location = new Point(80, 80),
                ShowInTaskbar = false,
                Opacity = 0
            };
            target.Show();
            using var overlay = new CoordinateCaptureOverlay(target.Handle)
            {
                Opacity = 1
            };
            overlay.Show();
            Application.DoEvents();
            overlay.PerformLayout();
            using var bitmap = new Bitmap(overlay.Width, overlay.Height);
            overlay.DrawToBitmap(bitmap, new Rectangle(Point.Empty, overlay.Size));
            bitmap.Save(outputPath, ImageFormat.Png);
            overlay.Close();
            target.Close();
            return 0;
        }
        catch
        {
            return 1;
        }
    }

    private static int RunCoordinateCaptureFlowTest()
    {
        try
        {
            using var owner = new Form
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-22000, -22000),
                Size = new Size(400, 300),
                ShowInTaskbar = false,
                Opacity = 0
            };
            using var target = new Form
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-20000, -20000),
                ClientSize = new Size(800, 450),
                ShowInTaskbar = false,
                Opacity = 0
            };
            owner.Show();
            target.Show();
            using var dialog = new BindingEditorDialog(
                new CoordinateBindingSettings
                {
                    Name = "좌표 흐름 테스트",
                    TriggerKey = (int)Keys.D1,
                    CoordinateSpace = CoordinateSpace.TargetWindow
                },
                target.Handle,
                []);
            dialog.Opacity = 0;

            var phase = 0;
            var resultCode = 9;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            using var timer = new System.Windows.Forms.Timer { Interval = 20 };
            timer.Tick += (_, _) =>
            {
                if (stopwatch.Elapsed > TimeSpan.FromSeconds(5))
                {
                    resultCode = 8;
                    dialog.DialogResult = DialogResult.Cancel;
                    timer.Stop();
                    return;
                }

                if (phase == 0 && dialog.Visible)
                {
                    var captureButton = FindControl<Button>(dialog, button => button.Name == "CaptureCoordinates");
                    if (captureButton is null)
                    {
                        resultCode = 7;
                        dialog.DialogResult = DialogResult.Cancel;
                        timer.Stop();
                        return;
                    }
                    phase = 1;
                    captureButton.PerformClick();
                    return;
                }

                var overlay = Application.OpenForms.OfType<CoordinateCaptureOverlay>().FirstOrDefault();
                if (phase == 1 && overlay is not null)
                {
                    phase = 2;
                    overlay.CaptureAtClientPoint(new Point(400, 225));
                    return;
                }

                if (phase == 2 && overlay is null && dialog.Visible)
                {
                    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                    var xInput = typeof(BindingEditorDialog).GetField("_xInput", flags)?.GetValue(dialog) as NumericUpDown;
                    var yInput = typeof(BindingEditorDialog).GetField("_yInput", flags)?.GetValue(dialog) as NumericUpDown;
                    var widthInput = typeof(BindingEditorDialog).GetField("_referenceWidthInput", flags)?.GetValue(dialog) as NumericUpDown;
                    var heightInput = typeof(BindingEditorDialog).GetField("_referenceHeightInput", flags)?.GetValue(dialog) as NumericUpDown;
                    resultCode = dialog.DialogResult == DialogResult.None &&
                                 xInput?.Value == 400 && yInput?.Value == 225 &&
                                 widthInput?.Value == 800 && heightInput?.Value == 450
                        ? 0
                        : 6;
                    dialog.DialogResult = DialogResult.Cancel;
                    timer.Stop();
                }
            };
            timer.Start();
            dialog.ShowDialog(owner);
            timer.Stop();
            target.Close();
            owner.Close();
            return resultCode;
        }
        catch
        {
            return 5;
        }
    }

    private static T? FindControl<T>(Control root, Func<T, bool> predicate) where T : Control
    {
        foreach (Control control in root.Controls)
        {
            if (control is T typed && predicate(typed))
            {
                return typed;
            }
            var nested = FindControl(control, predicate);
            if (nested is not null)
            {
                return nested;
            }
        }
        return null;
    }

    private static int RunSmokeTest()
    {
        try
        {
            PauseSkillTests.Run();
            InputReliabilityTests.Run();
            var original = new AppSettings
            {
                X = -25,
                Y = 480,
                CoordinateSpace = CoordinateSpace.Screen,
                KeyboardKey = (int)Keys.A,
                IntervalMs = 3,
                RepeatMode = RepeatMode.Count,
                RepeatCount = 5,
                MultipleMacrosInitialized = true,
                Bindings =
                [
                    new CoordinateBindingSettings
                    {
                        Name = "테스트 바인딩",
                        TriggerKey = (int)Keys.D1,
                        X = 321,
                        Y = 654,
                        Activation = BindingActivation.RepeatWhileHeld
                    },
                    new CoordinateBindingSettings
                    {
                        Name = "릴리스 클릭",
                        TriggerKey = (int)Keys.D2,
                        X = 777,
                        Y = 333,
                        CoordinateSpace = CoordinateSpace.TargetWindow,
                        Activation = BindingActivation.ReleaseClick
                    },
                    new CoordinateBindingSettings
                    {
                        Name = "현재 위치 클릭",
                        TriggerKey = (int)Keys.D3,
                        CoordinateSpace = CoordinateSpace.CurrentCursor,
                        MouseAction = InputActionKind.MouseClick,
                        Activation = BindingActivation.SinglePress
                    }
                ],
                Macros =
                [
                    new RapidMacroSettings
                    {
                        Name = "매크로 X",
                        TriggerKey = (int)Keys.X,
                        FirstStepKind = MacroFirstStepKind.MouseClick,
                        FirstStepMouseButton = MouseButtonKind.Right,
                        KeyboardKey = (int)Keys.D1,
                        BindingId = RapidMacroSettings.CurrentCursorBindingId,
                        RepeatMode = RepeatMode.Hold
                    },
                    new RapidMacroSettings
                    {
                        Name = "매크로 Z",
                        TriggerKey = (int)Keys.Z,
                        KeyboardKey = (int)Keys.D2,
                        BindingId = "saved-binding",
                        RepeatMode = RepeatMode.Toggle
                    }
                ]
            };
            var json = JsonSerializer.Serialize(original, SettingsStore.JsonOptions);
            var restored = JsonSerializer.Deserialize<AppSettings>(json, SettingsStore.JsonOptions);
            if (restored is null || restored.X != -25 || restored.RepeatCount != 5 ||
                restored.Bindings.Count != 3 || restored.Bindings[0].X != 321 ||
                restored.Bindings[0].Activation != BindingActivation.RepeatWhileHeld ||
                restored.Bindings[1].CoordinateSpace != CoordinateSpace.TargetWindow ||
                restored.Bindings[1].X != 777 ||
                restored.Bindings[1].Activation != BindingActivation.ReleaseClick ||
                restored.Bindings[2].CoordinateSpace != CoordinateSpace.CurrentCursor ||
                restored.Bindings[2].MouseAction != InputActionKind.MouseClick ||
                restored.Macros.Count != 2 || restored.Macros[0].TriggerKey != (int)Keys.X ||
                restored.Macros[0].FirstStepKind != MacroFirstStepKind.MouseClick ||
                restored.Macros[0].FirstStepMouseButton != MouseButtonKind.Right ||
                restored.Macros[1].RepeatMode != RepeatMode.Toggle ||
                restored.Macros[0].BindingId != RapidMacroSettings.CurrentCursorBindingId)
            {
                return 2;
            }

            var mappedPoint = MacroEngine.ScaleMappedPoint(640, 360, 1920, 1080, 1280, 720);
            if (mappedPoint.X != 960 || mappedPoint.Y != 540)
            {
                return 4;
            }

            using var completed = new ManualResetEventSlim(false);
            using var completedSecond = new ManualResetEventSlim(false);
            using var engine = new MacroEngine(_ => { });
            using var secondEngine = new MacroEngine(_ => { });
            engine.Completed += (_, _) => completed.Set();
            secondEngine.Completed += (_, _) => completedSecond.Set();
            engine.Start(new MacroConfiguration(
                -25,
                480,
                CoordinateSpace.Screen,
                IntPtr.Zero,
                1280,
                720,
                true,
                false,
                MouseButtonKind.Left,
                false,
                InputActionKind.MouseClick,
                MouseButtonKind.Left,
                Keys.A,
                1,
                RepeatMode.Count,
                5));
            secondEngine.Start(new MacroConfiguration(
                0,
                0,
                CoordinateSpace.CurrentCursor,
                IntPtr.Zero,
                1280,
                720,
                true,
                false,
                MouseButtonKind.Left,
                true,
                InputActionKind.MouseClick,
                MouseButtonKind.Left,
                Keys.B,
                1,
                RepeatMode.Count,
                7));
            if (!completed.Wait(TimeSpan.FromSeconds(3)) ||
                !completedSecond.Wait(TimeSpan.FromSeconds(3)) ||
                engine.ClickCount != 5 || secondEngine.ClickCount != 7)
            {
                return 3;
            }

            var immediateExecutions = 0;
            using var immediateEngine = new MacroEngine(_ => Interlocked.Increment(ref immediateExecutions));
            immediateEngine.Start(new MacroConfiguration(
                0,
                0,
                CoordinateSpace.CurrentCursor,
                IntPtr.Zero,
                1280,
                720,
                true,
                false,
                MouseButtonKind.Left,
                false,
                InputActionKind.MouseClick,
                MouseButtonKind.Left,
                Keys.C,
                100,
                RepeatMode.Count,
                2));
            if (Volatile.Read(ref immediateExecutions) != 1)
            {
                return 6;
            }
            immediateEngine.Stop();

            var privateInstance = System.Reflection.BindingFlags.Instance |
                                  System.Reflection.BindingFlags.NonPublic;
            using var bindingDialog = new BindingEditorDialog(
                new CoordinateBindingSettings { TriggerKey = (int)Keys.D1 },
                IntPtr.Zero,
                []);
            typeof(BindingEditorDialog).GetField("_capturingKey", privateInstance)!
                .SetValue(bindingDialog, true);
            typeof(BindingEditorDialog).GetMethod("ProcessDialogKey", privateInstance)!
                .Invoke(bindingDialog, [Keys.Escape]);
            var capturedBindingKey = (Keys)typeof(BindingEditorDialog)
                .GetField("_triggerKey", privateInstance)!.GetValue(bindingDialog)!;
            if (capturedBindingKey != Keys.Escape || bindingDialog.DialogResult != DialogResult.None)
            {
                return 7;
            }

            using var macroDialog = new MacroEditorDialog(
                new RapidMacroSettings { TriggerKey = (int)Keys.X, KeyboardKey = (int)Keys.D1 },
                [],
                []);
            var keyCaptureType = typeof(MacroEditorDialog)
                .GetNestedType("KeyCaptureTarget", System.Reflection.BindingFlags.NonPublic)!;
            var beginKeyCapture = typeof(MacroEditorDialog).GetMethod("BeginKeyCapture", privateInstance)!;
            var macroDialogKey = typeof(MacroEditorDialog).GetMethod("ProcessDialogKey", privateInstance)!;
            beginKeyCapture.Invoke(macroDialog, [Enum.Parse(keyCaptureType, "Trigger")]);
            macroDialogKey.Invoke(macroDialog, [Keys.Escape]);
            beginKeyCapture.Invoke(macroDialog, [Enum.Parse(keyCaptureType, "KeyboardOutput")]);
            macroDialogKey.Invoke(macroDialog, [Keys.Escape]);
            var capturedMacroTrigger = (Keys)typeof(MacroEditorDialog)
                .GetField("_triggerKey", privateInstance)!.GetValue(macroDialog)!;
            var capturedMacroOutput = (Keys)typeof(MacroEditorDialog)
                .GetField("_keyboardKey", privateInstance)!.GetValue(macroDialog)!;
            if (capturedMacroTrigger != Keys.Escape) return 81;
            if (capturedMacroOutput != Keys.Escape) return 82;
            if (macroDialog.DialogResult != DialogResult.None) return 83;

            var storedCoordinate = new CoordinateBindingSettings
            {
                Name = "저장 좌표",
                CoordinateSpace = CoordinateSpace.TargetWindow
            };
            var currentCursorBinding = new CoordinateBindingSettings
            {
                Name = "현재 위치",
                CoordinateSpace = CoordinateSpace.CurrentCursor
            };
            using var restrictedMacroDialog = new MacroEditorDialog(
                new RapidMacroSettings
                {
                    FirstStepKind = MacroFirstStepKind.MouseClick,
                    MouseEnabled = true,
                    BindingId = RapidMacroSettings.CurrentCursorBindingId
                },
                [storedCoordinate, currentCursorBinding],
                []);
            var restrictedChoices = (ComboBox)typeof(MacroEditorDialog)
                .GetField("_bindingInput", privateInstance)!.GetValue(restrictedMacroDialog)!;
            var remainingChoiceId = restrictedChoices.Items.Count == 1
                ? restrictedChoices.Items[0]!.GetType().GetProperty("Id")?.GetValue(restrictedChoices.Items[0]) as string
                : null;
            if (remainingChoiceId != storedCoordinate.Id)
            {
                return 11;
            }

            var dispatchedInputs = new System.Collections.Concurrent.ConcurrentQueue<(Keys Key, bool Pressed)>();
            var dispatchErrors = 0;
            using var dispatchCompleted = new ManualResetEventSlim(false);
            using (var dispatcher = new LowLatencyInputDispatcher(
                       (key, pressed) =>
                       {
                           if (key == Keys.F1) throw new InvalidOperationException("복구 검사");
                           dispatchedInputs.Enqueue((key, pressed));
                           if (dispatchedInputs.Count == 2) dispatchCompleted.Set();
                       },
                       _ => Interlocked.Increment(ref dispatchErrors)))
            {
                dispatcher.Enqueue(Keys.F1, true);
                dispatcher.Enqueue(Keys.D1, true);
                dispatcher.Enqueue(Keys.D1, false);
                if (!dispatchCompleted.Wait(TimeSpan.FromSeconds(2)))
                {
                    return 9;
                }
            }
            var dispatched = dispatchedInputs.ToArray();
            if (dispatchErrors != 1 || dispatched.Length != 2 ||
                dispatched[0] != (Keys.D1, true) || dispatched[1] != (Keys.D1, false))
            {
                return 10;
            }

            return 0;
        }
        catch
        {
            return 1;
        }
    }
}
