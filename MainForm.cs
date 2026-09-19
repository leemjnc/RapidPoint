using System.Drawing.Drawing2D;

namespace RapidPoint;

internal sealed class MainForm : Form
{
    private static readonly Color AppBackground = Color.FromArgb(10, 15, 29);
    private static readonly Color CardBackground = Color.FromArgb(20, 28, 48);
    private static readonly Color CardBorder = Color.FromArgb(42, 54, 82);
    private static readonly Color TextPrimary = Color.FromArgb(241, 245, 249);
    private static readonly Color TextSecondary = Color.FromArgb(148, 163, 184);
    private static readonly Color Accent = Color.FromArgb(34, 211, 238);
    private static readonly Color AccentDark = Color.FromArgb(8, 145, 178);
    private static readonly Color Success = Color.FromArgb(52, 211, 153);
    private static readonly Color Danger = Color.FromArgb(251, 113, 133);

    private readonly AppSettings _settings;
    private readonly Dictionary<string, MacroEngine> _macroEngines = [];
    private MacroEngine? _pauseSkillEngine;
    private readonly Dictionary<string, MouseButtonKind> _heldBindingButtons = [];
    private readonly object _macroEnginesSync = new();
    private readonly object _heldBindingButtonsSync = new();
    private readonly GlobalKeyboardHook _keyboardHook = new();
    private readonly LowLatencyInputDispatcher _inputDispatcher;
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 100 };
    private readonly System.Windows.Forms.Timer _coordinateTimer = new() { Interval = 33 };
    private CoordinateMonitorOverlay? _coordinateMonitor;

    private ListView _bindingList = null!;
    private ListView _macroList = null!;
    private Label _targetWindowLabel = null!;
    private Label _statusBadge = null!;
    private Button _startButton = null!;

    private IntPtr _targetWindow;
    private volatile bool _bindingDialogOpen;
    private volatile bool _profileActive;
    private volatile bool _closing;

    public MainForm()
    {
        _ = DedicatedPauseWorker.Shared;
        _inputDispatcher = new LowLatencyInputDispatcher(
            ProcessHotkeyChanged,
            exception => SetStatus(exception.Message, StatusKind.Error));
        _settings = SettingsStore.Load();
        NormalizeSettings();
        ConfigureWindow();
        BuildInterface();
        ApplySettings();
        WireEvents();

        _keyboardHook.SuppressHotkeys = true;
        _keyboardHook.IsTrackedKey = IsTrackedKey;
        if (!_keyboardHook.IsInstalled)
        {
            SetStatus("전역 키 연결 실패", StatusKind.Error);
        }

        _refreshTimer.Start();
    }

    private void NormalizeSettings()
    {
        _settings.Bindings ??= [];
        _settings.Macros ??= [];
        _settings.SuppressHotkeys = true;
        _settings.AlwaysOnTop = false;
        if (_settings.Bindings.Count == 0)
        {
            _settings.Bindings.Add(new CoordinateBindingSettings
            {
                X = _settings.X,
                Y = _settings.Y,
                CoordinateSpace = _settings.CoordinateSpace,
                ReferenceWidth = _settings.ReferenceWidth,
                ReferenceHeight = _settings.ReferenceHeight,
                TriggerKey = _settings.CoordinateMappingKey > 0 ? _settings.CoordinateMappingKey : (int)Keys.D1
            });
        }
        foreach (var binding in _settings.Bindings.Where(binding =>
                     binding.Activation == BindingActivation.ReleaseClick ||
                     binding.CoordinateSpace == CoordinateSpace.CurrentCursor))
        {
            binding.MouseAction = InputActionKind.MouseClick;
        }
        if (!_settings.MultipleMacrosInitialized)
        {
            _settings.Macros.Add(new RapidMacroSettings
            {
                Name = "연타 매크로 1",
                TriggerKey = _settings.MacroTriggerKey > 0 ? _settings.MacroTriggerKey : (int)Keys.X,
                KeyboardEnabled = _settings.MacroKeyboardEnabled,
                KeyboardKey = _settings.KeyboardKey > 0 ? _settings.KeyboardKey : (int)Keys.D1,
                MouseEnabled = _settings.MacroMouseEnabled,
                BindingId = string.IsNullOrWhiteSpace(_settings.MacroBindingId)
                    ? RapidMacroSettings.CurrentCursorBindingId
                    : _settings.MacroBindingId,
                IntervalMs = _settings.IntervalMs,
                RepeatMode = _settings.RepeatMode,
                RepeatCount = _settings.RepeatCount
            });
            _settings.MultipleMacrosInitialized = true;
        }
        NormalizeTriggerConflicts();
    }

    private void ConfigureWindow()
    {
        Text = "RapidPoint — 키 바인딩 매크로";
        BackColor = AppBackground;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 10F);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(620, 835);
        MinimumSize = new Size(620, 865);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    protected override void OnHandleCreated(EventArgs eventArgs)
    {
        base.OnHandleCreated(eventArgs);
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            var enabled = 1;
            NativeMethods.DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int));
        }
    }

    private void BuildInterface()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = AppBackground,
            Padding = new Padding(26, 18, 26, 18),
            ColumnCount = 1,
            RowCount = 3
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        Controls.Add(root);
        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildBody(), 0, 1);
        root.Controls.Add(BuildFooter(), 0, 2);
    }

    private Control BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Fill, BackColor = AppBackground };
        header.Controls.Add(new GradientLogo { Location = new Point(0, 5), Size = new Size(54, 54) });
        header.Controls.Add(new Label
        {
            Text = "RapidPoint",
            ForeColor = TextPrimary,
            Font = new Font("Segoe UI Semibold", 20F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(70, 2)
        });
        header.Controls.Add(new Label
        {
            Text = "여러 좌표 키 바인딩 · 복합 연타 매크로 · 창 비례 좌표",
            ForeColor = TextSecondary,
            Font = new Font("Segoe UI", 9.5F),
            AutoSize = true,
            Location = new Point(73, 43)
        });
        _statusBadge = new Label
        {
            Text = "●  대기 중",
            ForeColor = TextSecondary,
            BackColor = Color.FromArgb(27, 37, 58),
            Font = new Font("Segoe UI Semibold", 9.5F),
            TextAlign = ContentAlignment.MiddleCenter,
            Size = new Size(150, 34),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(732, 14)
        };
        header.Resize += (_, _) => _statusBadge.Left = header.ClientSize.Width - _statusBadge.Width;
        header.Controls.Add(_statusBadge);
        return header;
    }

    private Control BuildBody()
    {
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = AppBackground,
            ColumnCount = 1,
            RowCount = 1,
            Margin = Padding.Empty
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var left = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = AppBackground,
            RowCount = 2,
            ColumnCount = 1,
            Margin = Padding.Empty
        };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 365));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.Controls.Add(BuildBindingsCard(), 0, 0);
        left.Controls.Add(BuildMacroCard(), 0, 1);

        body.Controls.Add(left, 0, 0);
        return body;
    }

    private Control BuildBindingsCard()
    {
        var card = CreateCard(new Padding(0, 0, 0, 8));
        AddCardTitle(card, "키 바인딩", "새 바인딩을 추가하고 키마다 서로 다른 좌표 동작을 지정합니다.");

        _targetWindowLabel = new Label
        {
            Text = "연결된 대상 창 없음",
            ForeColor = TextSecondary,
            AutoEllipsis = true,
            Location = new Point(23, 64),
            Size = new Size(330, 33),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 8.8F)
        };
        card.Controls.Add(_targetWindowLabel);
        var targetButton = CreateSecondaryButton("창 목록에서 선택", new Point(365, 62), new Size(168, 35));
        targetButton.Click += (_, _) => OpenTargetWindowSelector();
        card.Controls.Add(targetButton);

        _bindingList = new ListView
        {
            Location = new Point(22, 107),
            Size = new Size(511, 133),
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            GridLines = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            BackColor = Color.FromArgb(11, 18, 32),
            ForeColor = TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            HideSelection = false,
            Font = new Font("Segoe UI", 8.7F)
        };
        _bindingList.Columns.Add("이름", 130);
        _bindingList.Columns.Add("키", 58);
        _bindingList.Columns.Add("좌표", 120);
        _bindingList.Columns.Add("동작", 92);
        _bindingList.Columns.Add("방식", 88);
        _bindingList.DoubleClick += (_, _) => EditSelectedBinding();
        card.Controls.Add(_bindingList);

        var addButton = CreateSecondaryButton("＋ 새 바인딩", new Point(22, 252), new Size(160, 38));
        addButton.Click += (_, _) => AddBinding();
        card.Controls.Add(addButton);
        var editButton = CreateSecondaryButton("좌표·설정 편집", new Point(194, 252), new Size(168, 38));
        editButton.Click += (_, _) => EditSelectedBinding();
        card.Controls.Add(editButton);
        var deleteButton = CreateSecondaryButton("삭제", new Point(374, 252), new Size(159, 38));
        deleteButton.Click += (_, _) => DeleteSelectedBinding();
        card.Controls.Add(deleteButton);
        var coordinateView = new CheckBox
        {
            Text = "게임 위 좌표 보기 · 선택한 바인딩 기준도 표시",
            Location = new Point(22, 303), AutoSize = true,
            ForeColor = TextSecondary, Checked = _settings.ShowCoordinateMonitor,
            Font = new Font("Segoe UI", 9F)
        };
        coordinateView.CheckedChanged += (_, _) =>
        {
            _settings.ShowCoordinateMonitor = coordinateView.Checked;
            UpdateCoordinateMonitor();
            SaveSettings();
        };
        card.Controls.Add(coordinateView);
        return card;
    }

    private Control BuildMacroCard()
    {
        var card = CreateCard(new Padding(0, 8, 0, 0));
        AddCardTitle(card, "연타 매크로", "여러 실행 키에 서로 다른 복합 연타 동작을 지정합니다.");

        _macroList = new ListView
        {
            Location = new Point(22, 68),
            Size = new Size(511, 137),
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            GridLines = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            BackColor = Color.FromArgb(11, 18, 32),
            ForeColor = TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            HideSelection = false,
            Font = new Font("Segoe UI", 8.7F)
        };
        _macroList.Columns.Add("이름", 126);
        _macroList.Columns.Add("실행 키", 68);
        _macroList.Columns.Add("반복 동작", 190);
        _macroList.Columns.Add("반복", 123);
        _macroList.DoubleClick += (_, _) => EditSelectedMacro();
        card.Controls.Add(_macroList);

        var addButton = CreateSecondaryButton("＋ 새 매크로", new Point(22, 217), new Size(160, 38));
        addButton.Click += (_, _) => AddMacro();
        card.Controls.Add(addButton);
        var editButton = CreateSecondaryButton("매크로 설정 편집", new Point(194, 217), new Size(168, 38));
        editButton.Click += (_, _) => EditSelectedMacro();
        card.Controls.Add(editButton);
        var deleteButton = CreateSecondaryButton("삭제", new Point(374, 217), new Size(159, 38));
        deleteButton.Click += (_, _) => DeleteSelectedMacro();
        card.Controls.Add(deleteButton);
        return card;
    }

    private Control BuildFooter()
    {
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = AppBackground,
            ColumnCount = 1,
            RowCount = 1,
            Padding = new Padding(0, 18, 0, 0),
            Margin = Padding.Empty
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _startButton = CreatePrimaryButton("◎   대상 창이 활성화되면 자동 준비");
        _startButton.Dock = DockStyle.Fill;
        _startButton.Margin = Padding.Empty;
        _startButton.Enabled = false;
        footer.Controls.Add(_startButton, 0, 0);
        return footer;
    }

    private void ApplySettings()
    {
        TopMost = false;
        TryRestoreTargetWindow();
        RefreshAutomaticActivation();
        UpdateAutomaticActivationUi();
        RefreshBindingList();
        RefreshMacroList();
    }

    private void WireEvents()
    {
        _keyboardHook.HotkeyChanged += OnHotkeyChanged;
        _refreshTimer.Tick += (_, _) => RefreshAutomaticActivation();
        _coordinateTimer.Tick += (_, _) => UpdateCoordinateMonitor();
        _coordinateTimer.Start();
        FormClosing += OnFormClosing;
    }

    private void UpdateCoordinateMonitor()
    {
        var enabled = _settings.ShowCoordinateMonitor && !_bindingDialogOpen && !_closing;
        if (enabled) _coordinateMonitor ??= new CoordinateMonitorOverlay();
        _coordinateMonitor?.RefreshTarget(_targetWindow, SelectedListBinding, enabled);
    }

    private void RefreshAutomaticActivation()
    {
        if (_targetWindow != IntPtr.Zero && !NativeMethods.IsWindow(_targetWindow))
        {
            _targetWindow = IntPtr.Zero;
            _profileActive = false;
            StopEverything(false);
            UpdateTargetWindowLabel();
            UpdateAutomaticActivationUi();
            return;
        }
        var foreground = NativeMethods.GetForegroundWindow();
        var foregroundRoot = foreground == IntPtr.Zero
            ? IntPtr.Zero
            : NativeMethods.GetAncestor(foreground, NativeMethods.GaRoot);
        var active = _targetWindow != IntPtr.Zero && NativeMethods.IsWindow(_targetWindow) &&
                     foregroundRoot == _targetWindow;
        if (!active) StopEverything(false);
        if (active == _profileActive)
        {
            return;
        }

        _profileActive = active;
        UpdateAutomaticActivationUi();
    }

    private void UpdateAutomaticActivationUi()
    {
        if (_profileActive)
        {
            _startButton.Text = "●   대상 창 활성 — 바인딩 자동 작동 중";
            _startButton.BackColor = Color.FromArgb(16, 113, 99);
            SetStatus("자동 활성화", StatusKind.Success);
        }
        else if (_targetWindow == IntPtr.Zero || !NativeMethods.IsWindow(_targetWindow))
        {
            _startButton.Text = "◎   창 목록에서 대상 창을 먼저 선택하세요";
            _startButton.BackColor = Color.FromArgb(51, 65, 85);
            SetStatus("대상 창 없음", StatusKind.Idle);
        }
        else
        {
            _startButton.Text = "○   대상 창 비활성 — 입력 감지 안 함";
            _startButton.BackColor = Color.FromArgb(51, 65, 85);
            SetStatus("자동 비활성화", StatusKind.Idle);
        }
    }

    private bool TargetIsForeground() => _targetWindow != IntPtr.Zero &&
        NativeMethods.IsWindow(_targetWindow) &&
        NativeMethods.GetAncestor(NativeMethods.GetForegroundWindow(), NativeMethods.GaRoot) == _targetWindow;

    private bool IsTrackedKey(Keys key)
    {
        if (_bindingDialogOpen)
        {
            return false;
        }
        // Do not wait for the 100ms UI refresh after returning to the target window.
        if (!TargetIsForeground())
        {
            return false;
        }
        return _settings.Macros.Any(macro => macro.Enabled && (Keys)macro.TriggerKey == key) ||
                _settings.Bindings.Any(binding => binding.Enabled && (Keys)binding.TriggerKey == key);
    }

    private void OnHotkeyChanged(Keys key, bool pressed)
    {
        if (_closing || IsDisposed || !IsHandleCreated)
        {
            return;
        }
        _inputDispatcher.Enqueue(key, pressed);
    }

    private void ProcessHotkeyChanged(Keys key, bool pressed)
    {
        if (_closing || _bindingDialogOpen || !TargetIsForeground())
        {
            return;
        }
        var macro = _settings.Macros.FirstOrDefault(item => item.Enabled && (Keys)item.TriggerKey == key);
        if (macro is not null)
        {
            HandleMacroTrigger(macro, pressed);
            return;
        }
        var binding = _settings.Bindings.FirstOrDefault(item => item.Enabled && (Keys)item.TriggerKey == key);
        if (binding is not null)
        {
            HandleBindingTrigger(binding, pressed);
        }
    }

    private void HandleMacroTrigger(RapidMacroSettings macro, bool pressed)
    {
        if (macro.PauseSkillSequence &&
            macro.PauseSequenceKind is PauseSequenceKind.Skill or PauseSequenceKind.EscapeWithKey)
        {
            if (pressed) StartMacro(macro);
            return;
        }
        if (macro.RepeatMode == RepeatMode.Hold)
        {
            if (pressed) StartMacro(macro); else StopMacro(macro.Id);
        }
        else if (pressed)
        {
            ToggleMacro(macro);
        }
    }

    private void HandleBindingTrigger(CoordinateBindingSettings binding, bool pressed)
    {
        lock (_macroEnginesSync)
        {
            if (_macroEngines.Count != 0) return;
        }
        if (binding.Activation == BindingActivation.ReleaseClick)
        {
            if (pressed) MoveBindingPointer(binding); else ClickBindingPointer(binding);
        }
        else if (binding.Activation == BindingActivation.SinglePress)
        {
            if (pressed) ExecuteBindingOnce(binding);
        }
        else
        {
            if (pressed) HoldBindingPointer(binding); else ReleaseBindingPointer(binding.Id);
        }
    }

    private void ToggleMacro(RapidMacroSettings macro)
    {
        bool running;
        lock (_macroEnginesSync)
        {
            running = _macroEngines.ContainsKey(macro.Id);
        }
        if (running) StopMacro(macro.Id); else StartMacro(macro);
    }

    private void StartMacro(RapidMacroSettings macro)
    {
        if (!ValidateMacro(macro, out var error))
        {
            SetStatus(error, StatusKind.Error);
            System.Media.SystemSounds.Exclamation.Play();
            return;
        }
        var engine = new MacroEngine();
        engine.Completed += (_, args) => OnMacroCompleted(macro.Id, macro.Name, engine, args);
        lock (_macroEnginesSync)
        {
            if (!TargetIsForeground() || _closing || _macroEngines.ContainsKey(macro.Id))
            {
                engine.Dispose();
                return;
            }
            if (_pauseSkillEngine is not null || (macro.PauseSkillSequence && !AllInputsIdle()))
            {
                engine.Dispose();
                SetStatus("진행 중인 입력이 끝난 뒤 실행하세요", StatusKind.Idle);
                return;
            }
            lock (_heldBindingButtonsSync)
            {
                if (_heldBindingButtons.Count != 0)
                {
                    engine.Dispose();
                    SetStatus("누름 유지 바인딩을 놓은 뒤 실행하세요", StatusKind.Idle);
                    return;
                }
            }
            _macroEngines[macro.Id] = engine;
            if (macro.PauseSkillSequence) _pauseSkillEngine = engine;
        }
        engine.Start(BuildMacroConfiguration(macro));
        if (engine.IsRunning)
        {
            SetStatus($"{macro.Name} 실행 중", StatusKind.Running);
        }
    }

    private void StopMacro(string macroId)
    {
        MacroEngine? engine;
        lock (_macroEnginesSync)
        {
            _macroEngines.TryGetValue(macroId, out engine);
        }
        if (engine is not null)
        {
            engine.Stop(finishCurrentCycle: true);
        }
    }

    private void StopEverything(bool showManualStatus = true)
    {
        _keyboardHook.ResetHeldKeys();
        MacroEngine[] engines;
        lock (_macroEnginesSync)
        {
            engines = _macroEngines.Values.ToArray();
        }
        foreach (var engine in engines)
        {
            engine.Stop();
        }
        ReleaseAllBindingButtons();
        if (showManualStatus)
        {
            SetStatus("전체 중지", StatusKind.Idle);
        }
    }

    private bool ValidateMacro(RapidMacroSettings macro, out string error)
    {
        error = string.Empty;
        if (macro.PauseSkillSequence)
        {
            if (macro.PauseSequenceKind is PauseSequenceKind.Skill or PauseSequenceKind.EscapeWithKey &&
                (Keys)macro.KeyboardKey is Keys.None or Keys.Escape)
            {
                error = "ESC와 함께 누를 스킬 키를 선택하세요";
                return false;
            }
            if (macro.PauseSequenceKind == PauseSequenceKind.EscapeWithKey) return true;
            if (macro.BindingId == RapidMacroSettings.CurrentCursorBindingId) return true;
            var destination = FindMacroBinding(macro);
            if (destination is null)
            {
                error = "클릭할 좌표를 선택하세요";
                return false;
            }
            return ValidateBindingCoordinate(destination, out error);
        }
        if (!macro.KeyboardEnabled && !macro.MouseEnabled)
        {
            error = "매크로 단계를 하나 이상 켜세요";
            return false;
        }
        var firstKeyBinding = MacroBindingResolver.FindFirstKeyBinding(macro, _settings.Bindings);
        if (firstKeyBinding != null && !ValidateBindingCoordinate(firstKeyBinding, out error)) return false;
        if (macro.KeyboardEnabled &&
            macro.FirstStepKind == MacroFirstStepKind.MouseClick &&
            macro.MouseEnabled &&
            macro.BindingId == RapidMacroSettings.CurrentCursorBindingId)
        {
            error = "마우스 입력 다음에 현재 위치 마우스 입력을 연속으로 사용할 수 없습니다";
            return false;
        }
        if (macro.MouseEnabled)
        {
            if (macro.BindingId == RapidMacroSettings.CurrentCursorBindingId)
            {
                return true;
            }
            var binding = FindMacroBinding(macro);
            if (binding is null)
            {
                error = "2단계에서 사용할 좌표 바인딩을 선택하세요";
                return false;
            }
            return ValidateBindingCoordinate(binding, out error);
        }
        return true;
    }

    private MacroConfiguration BuildMacroConfiguration(RapidMacroSettings macro)
    {
        var binding = macro.BindingId == RapidMacroSettings.CurrentCursorBindingId
            ? new CoordinateBindingSettings
            {
                Name = "현재 마우스 위치",
                CoordinateSpace = CoordinateSpace.CurrentCursor,
                Activation = BindingActivation.ReleaseClick,
                MouseAction = InputActionKind.MouseClick,
                MouseButton = MouseButtonKind.Left
            }
            : FindMacroBinding(macro) ?? new CoordinateBindingSettings { X = 0, Y = 0 };
        var configuration = BuildConfiguration(
            binding,
            macro.KeyboardEnabled && macro.FirstStepKind == MacroFirstStepKind.KeyboardKey,
            macro.KeyboardEnabled && macro.FirstStepKind == MacroFirstStepKind.MouseClick,
            macro.FirstStepMouseButton,
            macro.MouseEnabled,
            macro.IntervalMs,
            macro.RepeatMode,
            macro.RepeatCount,
            (Keys)macro.KeyboardKey);
        configuration = configuration with
        {
            StableInput = macro.StableInput,
            SequenceHoldMs = macro.SequenceHoldMs,
            BeforeClickMs = macro.BeforeClickMs
        };
        var firstKeyBinding = MacroBindingResolver.FindFirstKeyBinding(macro, _settings.Bindings);
        if (firstKeyBinding != null)
        {
            var firstAction = BuildBindingConfiguration(firstKeyBinding, RepeatMode.Count, 1);
            // A complete macro key pulse includes its release; ReleaseClick bindings
            // therefore click once here, instead of waiting for a physical key-up.
            if (firstKeyBinding.Activation == BindingActivation.ReleaseClick)
                firstAction = firstAction with { MouseAction = InputActionKind.MouseClick };
            configuration = configuration with { FirstKeyBinding = firstAction };
        }
        if (!macro.PauseSkillSequence) return configuration;
        // Keep the point the user aimed at when pressing the trigger.
        if (configuration.CoordinateSpace == CoordinateSpace.CurrentCursor &&
            NativeMethods.GetCursorPos(out var cursor))
            configuration = configuration with { X = cursor.X, Y = cursor.Y, CoordinateSpace = CoordinateSpace.Screen };
        return configuration with
        {
            PauseSkillSequence = true,
            PauseSequenceKind = macro.PauseSequenceKind,
            PauseAfterClick = macro.PauseAfterClick,
            SequenceHoldMs = macro.SequenceHoldMs,
            BeforeClickMs = macro.BeforeClickMs,
            BeforePauseMs = macro.BeforePauseMs,
            RepeatMode = macro.PauseSequenceKind is PauseSequenceKind.ClickThenEscape or PauseSequenceKind.EscapeThenClick
                ? macro.RepeatMode : RepeatMode.Count,
            RepeatCount = macro.PauseSequenceKind is PauseSequenceKind.ClickThenEscape or PauseSequenceKind.EscapeThenClick
                ? macro.RepeatCount : 1
        };
    }

    private MacroConfiguration BuildBindingConfiguration(CoordinateBindingSettings binding, RepeatMode repeatMode, int count) =>
        BuildConfiguration(binding, false, false, MouseButtonKind.Left, true,
            binding.IntervalMs, repeatMode, count, Keys.None);

    private MacroConfiguration BuildConfiguration(
        CoordinateBindingSettings binding,
        bool keyboardStep,
        bool firstStepMouse,
        MouseButtonKind firstStepMouseButton,
        bool mouseStep,
        int interval,
        RepeatMode repeatMode,
        int count,
        Keys keyboardKey)
    {
        var useCurrentCursor = binding.CoordinateSpace == CoordinateSpace.CurrentCursor;
        return new MacroConfiguration(
            binding.X,
            binding.Y,
            useCurrentCursor ? CoordinateSpace.CurrentCursor : binding.CoordinateSpace,
            _targetWindow,
            binding.ReferenceWidth,
            binding.ReferenceHeight,
            keyboardStep,
            firstStepMouse,
            firstStepMouseButton,
            mouseStep,
            useCurrentCursor ? InputActionKind.MouseClick : binding.MouseAction,
            binding.MouseButton,
            keyboardKey,
            interval,
            repeatMode,
            count);
    }

    private void ExecuteBindingOnce(CoordinateBindingSettings binding)
    {
        if (!ValidateBindingCoordinate(binding, out var error))
        {
            SetStatus(error, StatusKind.Error);
            return;
        }
        var configuration = BuildBindingConfiguration(binding, RepeatMode.Count, 1);
        try
        {
            MacroEngine.ExecuteOnce(configuration);
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, StatusKind.Error);
        }
    }

    private void MoveBindingPointer(CoordinateBindingSettings binding)
    {
        if (!ValidateBindingCoordinate(binding, out var error))
        {
            SetStatus(error, StatusKind.Error);
            return;
        }
        try
        {
            MacroEngine.MovePointer(BuildBindingConfiguration(binding, RepeatMode.Count, 1));
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, StatusKind.Error);
        }
    }

    private void ClickBindingPointer(CoordinateBindingSettings binding)
    {
        if (!ValidateBindingCoordinate(binding, out var error))
        {
            SetStatus(error, StatusKind.Error);
            return;
        }
        try
        {
            var clickBinding = binding.Clone();
            clickBinding.MouseAction = InputActionKind.MouseClick;
            MacroEngine.ExecuteOnce(BuildBindingConfiguration(clickBinding, RepeatMode.Count, 1));
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, StatusKind.Error);
        }
    }

    private void HoldBindingPointer(CoordinateBindingSettings binding)
    {
        if (!ValidateBindingCoordinate(binding, out var error))
        {
            SetStatus(error, StatusKind.Error);
            return;
        }
        try
        {
            lock (_heldBindingButtonsSync)
            {
                if (!TargetIsForeground() || _closing || _heldBindingButtons.ContainsKey(binding.Id))
                {
                    return;
                }
                MacroEngine.MovePointer(BuildBindingConfiguration(binding, RepeatMode.Count, 1));
                if (!_heldBindingButtons.Values.Contains(binding.MouseButton))
                {
                    NativeMethods.SetMouseButton(binding.MouseButton, true);
                }
                _heldBindingButtons[binding.Id] = binding.MouseButton;
            }
            SetStatus($"{binding.Name} 버튼 누름 유지", StatusKind.Running);
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, StatusKind.Error);
        }
    }

    private void ReleaseBindingPointer(string bindingId)
    {
        try
        {
            var released = false;
            lock (_heldBindingButtonsSync)
            {
                if (_heldBindingButtons.Remove(bindingId, out var button))
                {
                    if (!_heldBindingButtons.Values.Contains(button))
                    {
                        NativeMethods.SetMouseButton(button, false);
                    }
                    released = true;
                }
            }
            if (!released) return;
            bool macrosIdle;
            lock (_macroEnginesSync)
            {
                macrosIdle = _macroEngines.Count == 0;
            }
            if (macrosIdle)
            {
                SetStatus(_profileActive ? "자동 활성화" : "자동 비활성화",
                    _profileActive ? StatusKind.Success : StatusKind.Idle);
            }
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, StatusKind.Error);
        }
    }

    private bool AllInputsIdle()
    {
        lock (_macroEnginesSync)
        lock (_heldBindingButtonsSync)
        {
            return _macroEngines.Count == 0 && _heldBindingButtons.Count == 0;
        }
    }

    private void ReleaseAllBindingButtons()
    {
        lock (_heldBindingButtonsSync)
        {
            var buttons = _heldBindingButtons.Values.Distinct().ToArray();
            _heldBindingButtons.Clear();
            foreach (var button in buttons)
            {
                try
                {
                    NativeMethods.SetMouseButton(button, false);
                }
                catch
                {
                    // 비활성화·종료 중에는 가능한 모든 버튼의 해제를 계속 시도합니다.
                }
            }
        }
    }

    private void OnMacroCompleted(string macroId, string macroName, MacroEngine engine, MacroCompletedEventArgs args)
    {
        lock (_macroEnginesSync)
        {
            if (ReferenceEquals(_pauseSkillEngine, engine)) _pauseSkillEngine = null;
            if (_macroEngines.TryGetValue(macroId, out var current) && ReferenceEquals(current, engine))
            {
                _macroEngines.Remove(macroId);
            }
        }
        engine.Dispose();
        if (_closing || !IsHandleCreated)
        {
            return;
        }
        BeginInvoke(() =>
        {
            if (!string.IsNullOrWhiteSpace(args.ErrorMessage))
            {
                SetStatus("입력 실행 오류", StatusKind.Error);
                MessageBox.Show(args.ErrorMessage, "입력을 실행하지 못했습니다", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else if (args.ReachedCount)
            {
                SetStatus($"{macroName} 완료", StatusKind.Success);
            }
            else if (AllInputsIdle())
            {
                SetStatus(_profileActive ? "자동 활성화" : "자동 비활성화",
                    _profileActive ? StatusKind.Success : StatusKind.Idle);
            }
        });
    }

    private bool ValidateBindingCoordinate(CoordinateBindingSettings binding, out string error)
    {
        error = string.Empty;
        if (binding.CoordinateSpace == CoordinateSpace.CurrentCursor)
        {
            return true;
        }
        if (binding.CoordinateSpace == CoordinateSpace.Screen)
        {
            if (!SystemInformation.VirtualScreen.Contains(binding.X, binding.Y))
            {
                error = $"{binding.Name}: 화면 밖 좌표입니다";
                return false;
            }
            return true;
        }
        if (!EnsureTargetWindow())
        {
            error = "먼저 창 목록에서 대상 창을 선택하세요";
            return false;
        }
        if (binding.X < 0 || binding.Y < 0 || binding.X >= binding.ReferenceWidth || binding.Y >= binding.ReferenceHeight)
        {
            error = $"{binding.Name}: 기준 해상도 밖 좌표입니다";
            return false;
        }
        return true;
    }

    private void AddBinding()
    {
        var binding = new CoordinateBindingSettings
        {
            Name = $"바인딩 {_settings.Bindings.Count + 1}",
            TriggerKey = (int)FindAvailableTriggerKey([Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5, Keys.D6, Keys.D7, Keys.D8, Keys.D9]),
            CoordinateSpace = _targetWindow != IntPtr.Zero ? CoordinateSpace.TargetWindow : CoordinateSpace.Screen
        };
        if (NativeMethods.GetCursorPos(out var point))
        {
            if (binding.CoordinateSpace == CoordinateSpace.TargetWindow &&
                NativeMethods.GetClientRect(_targetWindow, out var rectangle) &&
                rectangle.Width > 0 && rectangle.Height > 0 &&
                NativeMethods.ScreenToClient(_targetWindow, ref point))
            {
                binding.ReferenceWidth = rectangle.Width;
                binding.ReferenceHeight = rectangle.Height;
            }
            binding.X = point.X;
            binding.Y = point.Y;
        }
        OpenBindingEditor(binding, true);
    }

    private void EditSelectedBinding()
    {
        var binding = SelectedListBinding;
        if (binding is null)
        {
            SetStatus("편집할 바인딩을 선택하세요", StatusKind.Error);
            return;
        }
        OpenBindingEditor(binding, false);
    }

    private void OpenBindingEditor(CoordinateBindingSettings binding, bool isNew)
    {
        var reserved = _settings.Bindings
            .Where(item => item.Id != binding.Id)
            .Select(item => (Keys)item.TriggerKey)
            .Concat(_settings.Macros.Select(macro => (Keys)macro.TriggerKey));
        _bindingDialogOpen = true;
        try
        {
            using var dialog = new BindingEditorDialog(binding, _targetWindow, reserved);
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null)
            {
                return;
            }
            if (isNew)
            {
                _settings.Bindings.Add(dialog.Result);
            }
            else
            {
                var index = _settings.Bindings.FindIndex(item => item.Id == binding.Id);
                if (index >= 0) _settings.Bindings[index] = dialog.Result;
            }
            RefreshBindingList(dialog.Result.Id);
            RefreshMacroList();
            SaveSettings();
            SetStatus("바인딩 저장 완료", StatusKind.Success);
        }
        finally
        {
            _bindingDialogOpen = false;
        }
    }

    private void DeleteSelectedBinding()
    {
        var binding = SelectedListBinding;
        if (binding is null)
        {
            return;
        }
        if (MessageBox.Show($"'{binding.Name}' 바인딩을 삭제할까요?", "바인딩 삭제",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }
        ReleaseBindingPointer(binding.Id);
        _settings.Bindings.RemoveAll(item => item.Id == binding.Id);
        foreach (var macro in _settings.Macros.Where(macro => macro.BindingId == binding.Id))
        {
            macro.BindingId = RapidMacroSettings.CurrentCursorBindingId;
        }
        RefreshBindingList();
        RefreshMacroList();
        SaveSettings();
    }

    private void AddMacro()
    {
        var macro = new RapidMacroSettings
        {
            Name = $"연타 매크로 {_settings.Macros.Count + 1}",
            TriggerKey = (int)FindAvailableTriggerKey(
                [Keys.X, Keys.Z, Keys.C, Keys.V, Keys.A, Keys.S, Keys.D, Keys.F]),
            BindingId = RapidMacroSettings.CurrentCursorBindingId
        };
        OpenMacroEditor(macro, true);
    }

    private void EditSelectedMacro()
    {
        var macro = SelectedListMacro;
        if (macro is null)
        {
            SetStatus("편집할 매크로를 선택하세요", StatusKind.Error);
            return;
        }
        OpenMacroEditor(macro, false);
    }

    private void OpenMacroEditor(RapidMacroSettings macro, bool isNew)
    {
        var reserved = _settings.Bindings.Select(binding => (Keys)binding.TriggerKey)
            .Concat(_settings.Macros.Where(item => item.Id != macro.Id).Select(item => (Keys)item.TriggerKey));
        _bindingDialogOpen = true;
        try
        {
            using var dialog = new MacroEditorDialog(macro, _settings.Bindings, reserved);
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null)
            {
                return;
            }
            if (isNew)
            {
                _settings.Macros.Add(dialog.Result);
            }
            else
            {
                StopMacro(macro.Id);
                var index = _settings.Macros.FindIndex(item => item.Id == macro.Id);
                if (index >= 0) _settings.Macros[index] = dialog.Result;
            }
            RefreshMacroList(dialog.Result.Id);
            SaveSettings();
            SetStatus("매크로 저장 완료", StatusKind.Success);
        }
        finally
        {
            _bindingDialogOpen = false;
        }
    }

    private void DeleteSelectedMacro()
    {
        var macro = SelectedListMacro;
        if (macro is null)
        {
            return;
        }
        if (MessageBox.Show($"'{macro.Name}' 매크로를 삭제할까요?", "매크로 삭제",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }
        StopMacro(macro.Id);
        _settings.Macros.RemoveAll(item => item.Id == macro.Id);
        RefreshMacroList();
        SaveSettings();
    }

    private CoordinateBindingSettings? SelectedListBinding
    {
        get
        {
            if (_bindingList.SelectedItems.Count == 0) return null;
            var id = _bindingList.SelectedItems[0].Tag as string;
            return _settings.Bindings.FirstOrDefault(binding => binding.Id == id);
        }
    }

    private RapidMacroSettings? SelectedListMacro
    {
        get
        {
            if (_macroList.SelectedItems.Count == 0) return null;
            var id = _macroList.SelectedItems[0].Tag as string;
            return _settings.Macros.FirstOrDefault(macro => macro.Id == id);
        }
    }

    private CoordinateBindingSettings? FindMacroBinding(RapidMacroSettings macro) =>
        _settings.Bindings.FirstOrDefault(binding => binding.Id == macro.BindingId);

    private void RefreshBindingList(string? selectId = null)
    {
        selectId ??= SelectedListBinding?.Id;
        _bindingList.BeginUpdate();
        _bindingList.Items.Clear();
        foreach (var binding in _settings.Bindings)
        {
            var coordinate = binding.CoordinateSpace == CoordinateSpace.CurrentCursor
                ? "현재 커서"
                : binding.CoordinateSpace == CoordinateSpace.TargetWindow
                    ? $"창 {binding.X}, {binding.Y}"
                    : $"{binding.X}, {binding.Y}";
            var action = binding.Activation == BindingActivation.ReleaseClick
                ? "재이동→클릭"
                : binding.Activation == BindingActivation.RepeatWhileHeld
                    ? binding.MouseButton switch
                    {
                        MouseButtonKind.Right => "우클릭 유지",
                        MouseButtonKind.Middle => "중클릭 유지",
                        _ => "좌클릭 유지"
                    }
                : binding.MouseAction == InputActionKind.MouseMove
                ? "이동"
                : binding.MouseButton switch
                {
                    MouseButtonKind.Right => "우클릭",
                    MouseButtonKind.Middle => "중클릭",
                    _ => "좌클릭"
                };
            var item = new ListViewItem(binding.Enabled ? binding.Name : $"(끔) {binding.Name}") { Tag = binding.Id };
            item.SubItems.Add(KeyFormatter.Format((Keys)binding.TriggerKey));
            item.SubItems.Add(coordinate);
            item.SubItems.Add(action);
            item.SubItems.Add(binding.Activation switch
            {
                BindingActivation.RepeatWhileHeld => "누름 유지",
                BindingActivation.ReleaseClick => "놓을 때",
                _ => "한 번"
            });
            _bindingList.Items.Add(item);
            if (binding.Id == selectId) item.Selected = true;
        }
        _bindingList.EndUpdate();
        if (_bindingList.SelectedItems.Count == 0 && _bindingList.Items.Count > 0)
        {
            _bindingList.Items[0].Selected = true;
        }
    }

    private void RefreshMacroList(string? selectId = null)
    {
        selectId ??= SelectedListMacro?.Id;
        _macroList.BeginUpdate();
        _macroList.Items.Clear();
        foreach (var macro in _settings.Macros)
        {
            var steps = new List<string>();
            var firstKeyBinding = MacroBindingResolver.FindFirstKeyBinding(macro, _settings.Bindings);
            if (macro.KeyboardEnabled)
            {
                steps.Add(macro.FirstStepKind == MacroFirstStepKind.MouseClick
                    ? macro.FirstStepMouseButton switch
                    {
                        MouseButtonKind.Right => "현재 커서 우클릭",
                        MouseButtonKind.Middle => "현재 커서 중클릭",
                        _ => "현재 커서 좌클릭"
                    }
                    : firstKeyBinding != null
                        ? $"{KeyFormatter.Format((Keys)macro.KeyboardKey)}({firstKeyBinding.Name})"
                        : KeyFormatter.Format((Keys)macro.KeyboardKey));
            }
            if (macro.MouseEnabled)
            {
                steps.Add(macro.BindingId == RapidMacroSettings.CurrentCursorBindingId
                    ? "현재 커서 클릭"
                    : FindMacroBinding(macro)?.Name ?? "좌표 없음");
            }
            var repeat = macro.RepeatMode switch
            {
                RepeatMode.Toggle => "토글 무제한",
                RepeatMode.Count => $"{macro.RepeatCount:N0}회",
                _ => "누르는 동안"
            };
            if (macro.PauseSkillSequence)
            {
                steps = macro.PauseSequenceKind switch
                {
                    PauseSequenceKind.ClickThenEscape => ["좌클릭", "ESC"],
                    PauseSequenceKind.EscapeThenClick => ["ESC", "좌클릭"],
                    PauseSequenceKind.EscapeWithKey => [$"ESC+{KeyFormatter.Format((Keys)macro.KeyboardKey)}"],
                    _ => [$"ESC+{KeyFormatter.Format((Keys)macro.KeyboardKey)}", "좌클릭"]
                };
                if (macro.PauseSequenceKind == PauseSequenceKind.Skill && macro.PauseAfterClick) steps.Add("ESC");
                if (macro.PauseSequenceKind is PauseSequenceKind.Skill or PauseSequenceKind.EscapeWithKey)
                    repeat = "한 번 실행";
            }
            var item = new ListViewItem(macro.Enabled ? macro.Name : $"(끔) {macro.Name}") { Tag = macro.Id };
            item.SubItems.Add(KeyFormatter.Format((Keys)macro.TriggerKey));
            item.SubItems.Add(string.Join(" → ", steps));
            item.SubItems.Add(repeat);
            _macroList.Items.Add(item);
            if (macro.Id == selectId) item.Selected = true;
        }
        _macroList.EndUpdate();
        if (_macroList.SelectedItems.Count == 0 && _macroList.Items.Count > 0)
        {
            _macroList.Items[0].Selected = true;
        }
    }

    private void OpenTargetWindowSelector()
    {
        _bindingDialogOpen = true;
        try
        {
            using var dialog = new WindowSelectionDialog(_targetWindow);
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                ConnectTargetWindow(dialog.SelectedWindow);
            }
        }
        finally
        {
            _bindingDialogOpen = false;
        }
    }

    private void ConnectTargetWindow(IntPtr candidate)
    {
        if (candidate == IntPtr.Zero || !NativeMethods.IsWindow(candidate))
        {
            SetStatus("대상 창을 찾지 못했습니다", StatusKind.Error);
            return;
        }
        NativeMethods.GetWindowThreadProcessId(candidate, out var processId);
        if (processId == Environment.ProcessId)
        {
            SetStatus("RapidPoint가 아닌 창을 가리켜 주세요", StatusKind.Error);
            return;
        }
        StopEverything(false);
        _profileActive = false;
        _targetWindow = candidate;
        _settings.TargetWindowTitle = NativeMethods.GetWindowTitle(candidate);
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)processId);
            _settings.TargetProcessName = process.ProcessName;
        }
        catch
        {
            _settings.TargetProcessName = string.Empty;
        }
        UpdateTargetWindowLabel();
        RefreshAutomaticActivation();
        UpdateAutomaticActivationUi();
        SaveSettings();
        SetStatus("대상 창 연결 완료", StatusKind.Success);
    }

    private bool EnsureTargetWindow() =>
        (_targetWindow != IntPtr.Zero && NativeMethods.IsWindow(_targetWindow)) || TryRestoreTargetWindow();

    private bool TryRestoreTargetWindow()
    {
        _targetWindow = IntPtr.Zero;
        if (!string.IsNullOrWhiteSpace(_settings.TargetProcessName))
        {
            try
            {
                _targetWindow = FindSavedTopLevelWindow();
                if (_targetWindow == IntPtr.Zero)
                {
                    var processes = System.Diagnostics.Process.GetProcessesByName(_settings.TargetProcessName);
                    try
                    {
                        var match = processes.FirstOrDefault(process => process.MainWindowHandle != IntPtr.Zero &&
                                                 process.MainWindowTitle == _settings.TargetWindowTitle)
                                    ?? processes.FirstOrDefault(process => process.MainWindowHandle != IntPtr.Zero);
                        if (match is not null) _targetWindow = match.MainWindowHandle;
                    }
                    finally
                    {
                        foreach (var process in processes) process.Dispose();
                    }
                }
            }
            catch
            {
                _targetWindow = IntPtr.Zero;
            }
        }
        UpdateTargetWindowLabel();
        return _targetWindow != IntPtr.Zero;
    }

    private IntPtr FindSavedTopLevelWindow()
    {
        var found = IntPtr.Zero;
        NativeMethods.EnumWindows((window, _) =>
        {
            if (!NativeMethods.IsWindowVisible(window) ||
                !string.Equals(NativeMethods.GetWindowTitle(window), _settings.TargetWindowTitle, StringComparison.Ordinal))
            {
                return true;
            }
            NativeMethods.GetWindowThreadProcessId(window, out var processId);
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById((int)processId);
                if (!string.Equals(process.ProcessName, _settings.TargetProcessName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch
            {
                return true;
            }
            found = window;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private void UpdateTargetWindowLabel()
    {
        if (_targetWindow != IntPtr.Zero && NativeMethods.IsWindow(_targetWindow))
        {
            _targetWindowLabel.Text = "연결: " + NativeMethods.GetWindowTitle(_targetWindow);
            _targetWindowLabel.ForeColor = Success;
        }
        else
        {
            _targetWindowLabel.Text = string.IsNullOrWhiteSpace(_settings.TargetWindowTitle)
                ? "연결된 대상 창 없음"
                : "대상 창 꺼짐: " + _settings.TargetWindowTitle;
            _targetWindowLabel.ForeColor = TextSecondary;
        }
    }

    private Keys FindAvailableTriggerKey(IEnumerable<Keys> candidates)
    {
        var used = _settings.Bindings.Select(binding => (Keys)binding.TriggerKey)
            .Concat(_settings.Macros.Select(macro => (Keys)macro.TriggerKey))
            .ToHashSet();
        return candidates.FirstOrDefault(key => !used.Contains(key), Keys.F10);
    }

    private void NormalizeTriggerConflicts()
    {
        var used = _settings.Bindings.Select(binding => (Keys)binding.TriggerKey).ToHashSet();
        var candidates = new[]
        {
            Keys.X, Keys.Z, Keys.C, Keys.V, Keys.A, Keys.S, Keys.D, Keys.F,
            Keys.G, Keys.H, Keys.J, Keys.K, Keys.F10, Keys.F11, Keys.F12
        };
        foreach (var macro in _settings.Macros)
        {
            var trigger = (Keys)macro.TriggerKey;
            if (trigger == Keys.None || !used.Add(trigger))
            {
                trigger = candidates.FirstOrDefault(candidate => !used.Contains(candidate), Keys.F10);
                macro.TriggerKey = (int)trigger;
                used.Add(trigger);
            }
            if (macro.BindingId != RapidMacroSettings.CurrentCursorBindingId &&
                _settings.Bindings.All(binding => binding.Id != macro.BindingId))
            {
                macro.BindingId = RapidMacroSettings.CurrentCursorBindingId;
            }
        }
    }

    private void SetStatus(string text, StatusKind kind)
    {
        if (_closing || IsDisposed)
        {
            return;
        }
        if (IsHandleCreated && InvokeRequired)
        {
            BeginInvoke(() => SetStatus(text, kind));
            return;
        }
        _statusBadge.Text = kind switch
        {
            StatusKind.Running => $"●  {text}",
            StatusKind.Success => $"✓  {text}",
            StatusKind.Error => $"!  {text}",
            _ => $"●  {text}"
        };
        _statusBadge.ForeColor = kind switch
        {
            StatusKind.Running => Accent,
            StatusKind.Success => Success,
            StatusKind.Error => Danger,
            _ => TextSecondary
        };
    }

    private void SaveSettings()
    {
        _settings.SuppressHotkeys = true;
        _settings.AlwaysOnTop = false;
        SettingsStore.Save(_settings);
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        _closing = true;
        SaveSettings();
        _refreshTimer.Stop();
        _coordinateTimer.Stop();
        _coordinateTimer.Dispose();
        _coordinateMonitor?.Dispose();
        _keyboardHook.Dispose();
        _inputDispatcher.Dispose();
        ReleaseAllBindingButtons();
        MacroEngine[] engines;
        lock (_macroEnginesSync)
        {
            engines = _macroEngines.Values.ToArray();
            _macroEngines.Clear();
        }
        foreach (var engine in engines) engine.Dispose();
    }

    private static Panel CreateCard(Padding margin)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = CardBackground, Margin = margin };
        panel.Paint += (_, eventArgs) =>
        {
            using var pen = new Pen(CardBorder);
            var rectangle = panel.ClientRectangle;
            rectangle.Width -= 1;
            rectangle.Height -= 1;
            eventArgs.Graphics.DrawRectangle(pen, rectangle);
        };
        return panel;
    }

    private static void AddCardTitle(Control card, string title, string subtitle)
    {
        card.Controls.Add(new Label
        {
            Text = title,
            ForeColor = TextPrimary,
            Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(22, 16)
        });
        card.Controls.Add(new Label
        {
            Text = subtitle,
            ForeColor = TextSecondary,
            Font = new Font("Segoe UI", 8.5F),
            AutoSize = true,
            Location = new Point(23, 43)
        });
    }

    private static Label CreateFieldLabel(string text, Point location) => new()
    {
        Text = text,
        ForeColor = TextSecondary,
        Font = new Font("Segoe UI Semibold", 8.5F),
        AutoSize = true,
        Location = location
    };

    private static Label CreateSmallLabel(string text, Point location) => new()
    {
        Text = text,
        ForeColor = TextSecondary,
        Font = new Font("Segoe UI Semibold", 9F),
        AutoSize = true,
        Location = location
    };

    private static NumericUpDown CreateNumberInput(Point location, int width, decimal minimum, decimal maximum) => new()
    {
        Location = location,
        Size = new Size(width, 35),
        Minimum = minimum,
        Maximum = maximum,
        BackColor = Color.FromArgb(11, 18, 32),
        ForeColor = TextPrimary,
        BorderStyle = BorderStyle.FixedSingle,
        TextAlign = HorizontalAlignment.Right,
        ThousandsSeparator = true
    };

    private static ComboBox CreateComboBox(Point location, int width, string[] choices)
    {
        var combo = new ComboBox
        {
            Location = location,
            Size = new Size(width, 35),
            BackColor = Color.FromArgb(11, 18, 32),
            ForeColor = TextPrimary,
            FlatStyle = FlatStyle.Flat,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 9F)
        };
        combo.Items.AddRange(choices);
        return combo;
    }

    private static Button CreateSecondaryButton(string text, Point location, Size size)
    {
        var button = new Button
        {
            Text = text,
            Location = location,
            Size = size,
            BackColor = Color.FromArgb(27, 39, 61),
            ForeColor = TextPrimary,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI Semibold", 8.5F)
        };
        button.FlatAppearance.BorderColor = CardBorder;
        return button;
    }

    private static Button CreatePrimaryButton(string text)
    {
        var button = new Button
        {
            Text = text,
            BackColor = Accent,
            ForeColor = Color.FromArgb(6, 23, 31),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold)
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    private static decimal Clamp(int value, decimal minimum, decimal maximum) =>
        Math.Min(maximum, Math.Max(minimum, value));

    private enum StatusKind { Idle, Running, Success, Error }
}

internal sealed class GradientLogo : Control
{
    public GradientLogo() =>
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = new GraphicsPath();
        path.AddRoundedRectangle(new Rectangle(1, 1, Width - 3, Height - 3), 12);
        using var brush = new LinearGradientBrush(ClientRectangle, Color.FromArgb(34, 211, 238), Color.FromArgb(99, 102, 241), 45F);
        eventArgs.Graphics.FillPath(brush, path);
        TextRenderer.DrawText(eventArgs.Graphics, "RP", new Font("Segoe UI Semibold", 14F, FontStyle.Bold),
            ClientRectangle, Color.FromArgb(3, 16, 24), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

internal static class GraphicsPathExtensions
{
    public static void AddRoundedRectangle(this GraphicsPath path, Rectangle rectangle, int radius)
    {
        var diameter = radius * 2;
        var arc = new Rectangle(rectangle.Location, new Size(diameter, diameter));
        path.AddArc(arc, 180, 90);
        arc.X = rectangle.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = rectangle.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = rectangle.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
    }
}
