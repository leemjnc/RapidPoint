namespace RapidPoint;

internal sealed class MacroEditorDialog : Form
{
    private static readonly Color Background = Color.FromArgb(10, 15, 29);
    private static readonly Color Surface = Color.FromArgb(20, 28, 48);
    private static readonly Color Primary = Color.FromArgb(241, 245, 249);
    private static readonly Color Secondary = Color.FromArgb(148, 163, 184);
    private static readonly Color Accent = Color.FromArgb(34, 211, 238);

    private readonly RapidMacroSettings _working;
    private readonly IReadOnlyList<CoordinateBindingSettings> _bindings;
    private readonly HashSet<Keys> _reservedTriggerKeys;
    private TextBox _nameInput = null!;
    private CheckBox _enabledInput = null!;
    private Button _triggerKeyButton = null!;
    private CheckBox _keyboardEnabledInput = null!;
    private ComboBox _firstStepTypeInput = null!;
    private Button _keyboardKeyButton = null!;
    private ComboBox _firstStepMouseButtonInput = null!;
    private CheckBox _mouseEnabledInput = null!;
    private ComboBox _bindingInput = null!;
    private Label _combinationNoteLabel = null!;
    private CheckBox _firstKeyBindingInput = null!;
    private NumericUpDown _intervalInput = null!;
    private ComboBox _repeatModeInput = null!;
    private NumericUpDown _countInput = null!;
    private Label _speedLabel = null!;
    private Keys _triggerKey;
    private Keys _keyboardKey;
    private KeyCaptureTarget _captureTarget;
    private bool _applyingValues;
    private ComboBox _sequenceModeInput = null!;
    private Label _sequenceDescription = null!;
    private Label _sequenceNote = null!;
    private CheckBox _pauseAfterInput = null!;
    private CheckBox _stableInput = null!;
    private Label _clickProtection = null!;
    private Label _gapLabel = null!;
    private Label _holdLabel = null!;
    private Label _pauseDelayLabel = null!;
    private NumericUpDown _sequenceHoldInput = null!;
    private NumericUpDown _beforeClickInput = null!;
    private NumericUpDown _beforePauseInput = null!;

    public RapidMacroSettings? Result { get; private set; }

    public MacroEditorDialog(
        RapidMacroSettings macro,
        IReadOnlyList<CoordinateBindingSettings> bindings,
        IEnumerable<Keys> reservedTriggerKeys)
    {
        _working = macro.Clone();
        _bindings = bindings;
        _reservedTriggerKeys = reservedTriggerKeys.ToHashSet();
        _triggerKey = (Keys)_working.TriggerKey;
        _keyboardKey = (Keys)_working.KeyboardKey;
        ConfigureWindow();
        BuildInterface();
        ApplyValues();
    }

    private void ConfigureWindow()
    {
        Text = "연타 매크로 설정";
        BackColor = Background;
        ForeColor = Primary;
        Font = new Font("Segoe UI", 9.5F);
        ClientSize = new Size(590, 785);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;
        KeyDown += OnDialogKeyDown;
    }

    private void BuildInterface()
    {
        Controls.Add(new Label
        {
            Text = "연타 매크로 추가·편집",
            Font = new Font("Segoe UI Semibold", 17F, FontStyle.Bold),
            ForeColor = Primary,
            AutoSize = true,
            Location = new Point(24, 20)
        });
        Controls.Add(new Label
        {
            Text = "실행 키를 정하고, 아래 순서대로 반복할 동작을 선택하세요.",
            ForeColor = Secondary,
            AutoSize = true,
            Location = new Point(26, 55)
        });

        AddLabel("이름", 26, 91);
        _nameInput = CreateTextInput(26, 113, 252);
        Controls.Add(_nameInput);
        AddLabel("실행 키 · 눌러서 변경", 296, 91);
        _triggerKeyButton = CreateButton("키 선택", 296, 112, 160, 35);
        _triggerKeyButton.Click += (_, _) => BeginKeyCapture(KeyCaptureTarget.Trigger);
        Controls.Add(_triggerKeyButton);
        _enabledInput = new CheckBox
        {
            Text = "사용",
            ForeColor = Secondary,
            BackColor = Background,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            Location = new Point(486, 119)
        };
        Controls.Add(_enabledInput);

        AddLabel("실행 모드", 26, 165);
        _sequenceModeInput = CreateCombo(126, 160, 440,
            ["일반 연타", "퍼즈 스킬 · ESC+키 → 클릭", "퍼즈 반복 · 클릭 → ESC", "퍼즈 반복 · ESC → 클릭", "ESC+지정 키 · 동시 입력만"]);
        _sequenceModeInput.SelectedIndexChanged += (_, _) => UpdateControls();
        Controls.Add(_sequenceModeInput);

        var stepsPanel = new Panel
        {
            BackColor = Surface,
            Location = new Point(24, 208),
            Size = new Size(542, 188)
        };
        Controls.Add(stepsPanel);
        stepsPanel.Controls.Add(new Label
        {
            Text = "실행 순서 · 1단계 → 2단계",
            ForeColor = Primary,
            Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(18, 14)
        });
        _keyboardEnabledInput = CreateCheckBox("1단계 입력", 20, 52);
        _keyboardEnabledInput.CheckedChanged += (_, _) => UpdateControls();
        stepsPanel.Controls.Add(_keyboardEnabledInput);
        _firstStepTypeInput = CreateCombo(127, 45, 155, ["키보드 키", "마우스 클릭"]);
        _firstStepTypeInput.SelectedIndexChanged += (_, _) => UpdateControls();
        stepsPanel.Controls.Add(_firstStepTypeInput);
        _keyboardKeyButton = CreateButton("키 선택", 292, 45, 224, 35);
        _keyboardKeyButton.Click += (_, _) => BeginKeyCapture(KeyCaptureTarget.KeyboardOutput);
        stepsPanel.Controls.Add(_keyboardKeyButton);
        _firstStepMouseButtonInput = CreateCombo(292, 45, 224, ["좌클릭", "우클릭", "중클릭"]);
        stepsPanel.Controls.Add(_firstStepMouseButtonInput);
        _mouseEnabledInput = CreateCheckBox("2단계 · 좌표 동작", 20, 99);
        _mouseEnabledInput.CheckedChanged += (_, _) => UpdateControls();
        stepsPanel.Controls.Add(_mouseEnabledInput);
        _bindingInput = CreateCombo(194, 92, 322, []);
        stepsPanel.Controls.Add(_bindingInput);
        _firstKeyBindingInput = CreateCheckBox("1단계 키에 연결된 바인딩 실행", 20, 130);
        _firstKeyBindingInput.CheckedChanged += (_, _) => UpdateControls();
        stepsPanel.Controls.Add(_firstKeyBindingInput);
        _combinationNoteLabel = new Label
        {
            Text = "마우스 1단계에서는 저장된 좌표 동작만 사용할 수 있습니다.",
            ForeColor = Secondary,
            AutoSize = false,
            AutoEllipsis = true,
            Size = new Size(500, 22),
            Location = new Point(20, 159),
            Font = new Font("Segoe UI", 8.2F)
        };
        stepsPanel.Controls.Add(_combinationNoteLabel);

        var repeatPanel = new Panel
        {
            BackColor = Surface,
            Location = new Point(24, 410),
            Size = new Size(542, 126)
        };
        Controls.Add(repeatPanel);
        repeatPanel.Controls.Add(CreateLabel("반복 사이 대기", 18, 16));
        _intervalInput = CreateNumber(18, 39, 105, 1, 60000);
        _intervalInput.ValueChanged += (_, _) => UpdateSpeedLabel();
        repeatPanel.Controls.Add(_intervalInput);
        repeatPanel.Controls.Add(CreateLabel("ms", 128, 47));
        repeatPanel.Controls.Add(CreateLabel("반복 방식", 165, 16));
        _repeatModeInput = CreateCombo(165, 39, 205, ["누르고 있는 동안", "한 번 눌러 시작 / 다시 중지", "정해진 횟수만"]);
        _repeatModeInput.SelectedIndexChanged += (_, _) => UpdateControls();
        repeatPanel.Controls.Add(_repeatModeInput);
        repeatPanel.Controls.Add(CreateLabel("반복 횟수", 388, 16));
        _countInput = CreateNumber(388, 39, 128, 1, 100000000);
        repeatPanel.Controls.Add(_countInput);
        _speedLabel = new Label
        {
            ForeColor = Accent,
            Font = new Font("Segoe UI Semibold", 8.8F),
            AutoSize = true,
            Location = new Point(18, 88)
        };
        repeatPanel.Controls.Add(_speedLabel);

        var sequencePanel = new Panel
        {
            BackColor = Surface, Location = new Point(24, 550), Size = new Size(542, 163)
        };
        Controls.Add(sequencePanel);
        var timingTitle = CreateLabel("입력 시간 설정", 18, 12);
        timingTitle.ForeColor = Primary;
        sequencePanel.Controls.Add(timingTitle);
        _pauseAfterInput = CreateCheckBox("클릭 후 ESC 재정지", 330, 12);
        sequencePanel.Controls.Add(_pauseAfterInput);
        _stableInput = CreateCheckBox("안전 모드 · 안정 입력", 330, 12);
        _stableInput.CheckedChanged += (_, _) => UpdateControls();
        sequencePanel.Controls.Add(_stableInput);
        _clickProtection = CreateLabel("클릭 보호 · 자동 적용", 330, 12);
        _clickProtection.ForeColor = Accent;
        sequencePanel.Controls.Add(_clickProtection);
        _sequenceDescription = CreateLabel("", 18, 42);
        sequencePanel.Controls.Add(_sequenceDescription);
        _holdLabel = CreateLabel("누름 유지 (ms)", 18, 69);
        sequencePanel.Controls.Add(_holdLabel);
        _gapLabel = CreateLabel("클릭 전 대기 (ms)", 190, 69);
        sequencePanel.Controls.Add(_gapLabel);
        _pauseDelayLabel = CreateLabel("재정지 전 대기 (ms)", 362, 69);
        sequencePanel.Controls.Add(_pauseDelayLabel);
        _sequenceHoldInput = CreateNumber(18, 90, 145, 1, 1000);
        _beforeClickInput = CreateNumber(190, 90, 145, 0, 1000);
        _beforePauseInput = CreateNumber(362, 90, 154, 0, 1000);
        sequencePanel.Controls.AddRange([_sequenceHoldInput, _beforeClickInput, _beforePauseInput]);
        _sequenceNote = CreateLabel("", 18, 133);
        sequencePanel.Controls.Add(_sequenceNote);

        var cancelButton = CreateButton("취소", 350, 731, 102, 40);
        cancelButton.Click += (_, _) => DialogResult = DialogResult.Cancel;
        Controls.Add(cancelButton);
        var saveButton = CreateButton("설정 저장", 464, 731, 102, 40, true);
        saveButton.Click += (_, _) => SaveAndClose();
        Controls.Add(saveButton);
        AcceptButton = saveButton;
        CancelButton = cancelButton;
    }

    private void ApplyValues()
    {
        _applyingValues = true;
        _nameInput.Text = _working.Name;
        _enabledInput.Checked = _working.Enabled;
        _triggerKeyButton.Text = KeyFormatter.Format(_triggerKey);
        _keyboardEnabledInput.Checked = _working.KeyboardEnabled;
        _firstKeyBindingInput.Checked = _working.UseFirstKeyBinding;
        _firstStepTypeInput.SelectedIndex = _working.FirstStepKind == MacroFirstStepKind.MouseClick ? 1 : 0;
        _keyboardKeyButton.Text = KeyFormatter.Format(_keyboardKey);
        _firstStepMouseButtonInput.SelectedIndex = (int)_working.FirstStepMouseButton;
        _mouseEnabledInput.Checked = _working.MouseEnabled;
        RefreshBindingChoices(_working.BindingId);
        _intervalInput.Value = Clamp(_working.IntervalMs, 1, 60000);
        _repeatModeInput.SelectedIndex = _working.RepeatMode switch
        {
            RepeatMode.Toggle => 1,
            RepeatMode.Count => 2,
            _ => 0
        };
        _countInput.Value = Clamp(_working.RepeatCount, 1, 100000000);
        _sequenceModeInput.SelectedIndex = _working.PauseSkillSequence
            ? Math.Clamp((int)_working.PauseSequenceKind + 1, 1, 4) : 0;
        _pauseAfterInput.Checked = _working.PauseAfterClick;
        _stableInput.Checked = _working.StableInput;
        _sequenceHoldInput.Value = Clamp(_working.SequenceHoldMs, 1, 1000);
        _beforeClickInput.Value = Clamp(_working.BeforeClickMs, 0, 1000);
        _beforePauseInput.Value = Clamp(_working.BeforePauseMs, 0, 1000);
        _applyingValues = false;
        UpdateControls();
    }

    private void BeginKeyCapture(KeyCaptureTarget target)
    {
        _captureTarget = target;
        if (target == KeyCaptureTarget.Trigger)
        {
            _triggerKeyButton.Text = "키를 누르세요…";
            _triggerKeyButton.Focus();
        }
        else
        {
            _keyboardKeyButton.Text = "키를 누르세요…";
            _keyboardKeyButton.Focus();
        }
    }

    private void OnDialogKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (_captureTarget == KeyCaptureTarget.None)
        {
            return;
        }
        eventArgs.Handled = true;
        eventArgs.SuppressKeyPress = true;
        if (_captureTarget == KeyCaptureTarget.Trigger && _reservedTriggerKeys.Contains(eventArgs.KeyCode))
        {
            MessageBox.Show("다른 바인딩이나 매크로에서 이미 사용 중인 실행 키입니다.", "키 충돌", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_captureTarget == KeyCaptureTarget.Trigger)
        {
            _triggerKey = eventArgs.KeyCode;
        }
        else
        {
            _keyboardKey = eventArgs.KeyCode;
        }
        _captureTarget = KeyCaptureTarget.None;
        RestoreKeyLabels();
    }

    private void RestoreKeyLabels()
    {
        _triggerKeyButton.Text = KeyFormatter.Format(_triggerKey);
        _keyboardKeyButton.Text = KeyFormatter.Format(_keyboardKey);
        UpdateControls();
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (_captureTarget != KeyCaptureTarget.None && (keyData & Keys.KeyCode) == Keys.Escape)
        {
            if (_captureTarget == KeyCaptureTarget.Trigger)
            {
                _triggerKey = Keys.Escape;
            }
            else
            {
                _keyboardKey = Keys.Escape;
            }
            _captureTarget = KeyCaptureTarget.None;
            RestoreKeyLabels();
            return true;
        }
        return base.ProcessDialogKey(keyData);
    }

    private void UpdateControls()
    {
        if (_applyingValues)
        {
            return;
        }
        var firstStepEnabled = _keyboardEnabledInput.Checked;
        var sequence = SequenceEnabled;
        var cycle = RepeatingPause;
        var keysOnly = SelectedSequenceKind == PauseSequenceKind.EscapeWithKey;
        if (sequence)
        {
            _applyingValues = true;
            _keyboardEnabledInput.Checked = !cycle;
            _mouseEnabledInput.Checked = !keysOnly;
            _firstStepTypeInput.SelectedIndex = 0;
            if (!cycle)
            {
                _repeatModeInput.SelectedIndex = 2;
                _countInput.Value = 1;
            }
            _applyingValues = false;
            firstStepEnabled = !cycle;
        }
        _keyboardEnabledInput.Enabled = !sequence;
        _mouseEnabledInput.Enabled = !sequence;
        var mouseFirst = SelectedFirstStepKind == MacroFirstStepKind.MouseClick;
        _firstStepTypeInput.Enabled = firstStepEnabled && !sequence;
        _keyboardKeyButton.Visible = !mouseFirst;
        _keyboardKeyButton.Enabled = firstStepEnabled && !mouseFirst;
        _firstStepMouseButtonInput.Visible = mouseFirst;
        _firstStepMouseButtonInput.Enabled = firstStepEnabled && mouseFirst;
        RefreshBindingChoices();
        _bindingInput.Enabled = _mouseEnabledInput.Checked;
        _firstKeyBindingInput.Visible = !sequence && !mouseFirst;
        _firstKeyBindingInput.Enabled = firstStepEnabled;
        var mappedKey = _bindings.FirstOrDefault(binding => binding.Enabled && (Keys)binding.TriggerKey == _keyboardKey);
        _combinationNoteLabel.Visible = !sequence && firstStepEnabled;
        _combinationNoteLabel.Text = mouseFirst
            ? "현재 위치 클릭 → 저장 좌표 동작 → 원래 위치 복귀"
            : _firstKeyBindingInput.Checked && mappedKey != null
                ? $"{KeyFormatter.Format(_keyboardKey)} → {mappedKey.Name} 실행 후 2단계"
                : "바인딩 대신 키 자체를 대상 프로그램에 전송";
        _countInput.Enabled = SelectedRepeatMode == RepeatMode.Count;
        _repeatModeInput.Enabled = !sequence || cycle;
        _intervalInput.Enabled = !sequence || cycle;
        if (sequence && !cycle) _countInput.Enabled = false;
        _pauseAfterInput.Enabled = sequence && SelectedSequenceKind == PauseSequenceKind.Skill;
        _pauseAfterInput.Visible = sequence;
        _stableInput.Visible = !sequence;
        var protectedMouseSequence = !sequence && firstStepEnabled && mouseFirst && _mouseEnabledInput.Checked;
        _stableInput.Visible = !sequence && !protectedMouseSequence;
        _clickProtection.Visible = protectedMouseSequence;
        // Mouse-click protection is enforced by the engine independently. Do not
        // overwrite the user's standard-mode preference when switching step types.
        _stableInput.Enabled = !protectedMouseSequence;
        _sequenceHoldInput.Minimum = protectedMouseSequence ? 20 : 1;
        _beforeClickInput.Minimum = protectedMouseSequence ? 20 : 0;
        _sequenceHoldInput.Enabled = sequence || _stableInput.Checked || protectedMouseSequence;
        _beforeClickInput.Enabled = (!sequence && (_stableInput.Checked || protectedMouseSequence)) ||
            (sequence && SelectedSequenceKind is PauseSequenceKind.Skill or PauseSequenceKind.EscapeThenClick);
        _gapLabel.Text = sequence ? "클릭 전 대기 (ms)" : "1→2단계 대기 (ms)";
        _beforePauseInput.Enabled = sequence && SelectedSequenceKind is PauseSequenceKind.Skill or PauseSequenceKind.ClickThenEscape;
        _beforePauseInput.Visible = _pauseDelayLabel.Visible = sequence &&
            SelectedSequenceKind is PauseSequenceKind.Skill or PauseSequenceKind.ClickThenEscape;
        _holdLabel.ForeColor = _sequenceHoldInput.Enabled ? Secondary : Color.DimGray;
        _gapLabel.ForeColor = _beforeClickInput.Enabled ? Secondary : Color.DimGray;
        _sequenceDescription.Text = !sequence
            ? protectedMouseSequence ? "클릭 보호 자동 적용 · 클릭을 해제한 뒤 다음 위치로 이동합니다."
                : _stableInput.Checked ? "안전 모드 켜짐 · 누름 → 유지 → 해제 후 다음 단계 실행"
                : "안전 모드 꺼짐 · 추가 누름 유지 없이 순서대로 전송합니다."
            : SelectedSequenceKind switch
        {
            PauseSequenceKind.ClickThenEscape => "2단계 위치 좌클릭 → 대기 → ESC · 한 묶음씩 반복",
            PauseSequenceKind.EscapeThenClick => "ESC → 대기 → 2단계 위치 좌클릭 · 한 묶음씩 반복",
            PauseSequenceKind.EscapeWithKey => "ESC + 1단계 키 동시 입력 · 클릭 없이 한 번 실행",
            _ => "ESC + 1단계 키 → 2단계 위치 좌클릭 → 선택적으로 ESC"
        };
        _sequenceNote.Text = protectedMouseSequence ? "매 반복 후 원래 위치 복귀 · 유지/이동 전 간격 최소 20ms"
            : !sequence ? "입력이 누락되면 안전 모드를 켜고 시간을 늘려 확인하세요." : cycle
            ? "퍼즈 메뉴의 복귀 위치 지정 · 중단 후 게임의 정지 상태를 확인하세요."
            : "게임이 일시정지된 상태에서 실행 · 시간은 환경에 맞춰 조정";
        UpdateSpeedLabel();
    }

    private void RefreshBindingChoices(string? preferredId = null)
    {
        preferredId ??= (_bindingInput.SelectedItem as BindingChoice)?.Id;
        var mouseFirst = _firstStepTypeInput.SelectedIndex == 1;
        _bindingInput.BeginUpdate();
        _bindingInput.Items.Clear();
        if (!mouseFirst)
        {
            _bindingInput.Items.Add(new BindingChoice(
                RapidMacroSettings.CurrentCursorBindingId,
                "현재 마우스 위치 (좌클릭)"));
        }
        foreach (var binding in _bindings.Where(binding =>
                     !mouseFirst || binding.CoordinateSpace != CoordinateSpace.CurrentCursor))
        {
            _bindingInput.Items.Add(new BindingChoice(
                binding.Id,
                $"{binding.Name} ({KeyFormatter.Format((Keys)binding.TriggerKey)})"));
        }
        var selectedIndex = -1;
        for (var index = 0; index < _bindingInput.Items.Count; index++)
        {
            if (_bindingInput.Items[index] is BindingChoice choice && choice.Id == preferredId)
            {
                selectedIndex = index;
                break;
            }
        }
        _bindingInput.SelectedIndex = selectedIndex >= 0 ? selectedIndex :
            _bindingInput.Items.Count > 0 ? 0 : -1;
        _bindingInput.EndUpdate();
    }

    private void UpdateSpeedLabel()
    {
        if (_speedLabel is null || _intervalInput is null)
        {
            return;
        }
        _speedLabel.Text = $"1·2단계 완료 → {_intervalInput.Value:N0}ms 대기 → 다음 반복";
        if (SequenceEnabled)
            _speedLabel.Text = RepeatingPause
                ? "입력 간격 = 한 묶음 완료 후 다음 묶음까지의 대기 (ms)"
                : "키를 한 번 누를 때마다 1회 실행 (연타 안 함)";
    }

    private bool SequenceEnabled => _sequenceModeInput is not null && _sequenceModeInput.SelectedIndex > 0;
    private PauseSequenceKind SelectedSequenceKind =>
        (PauseSequenceKind)Math.Max(0, (_sequenceModeInput?.SelectedIndex ?? 0) - 1);
    private bool RepeatingPause => SequenceEnabled &&
        SelectedSequenceKind is PauseSequenceKind.ClickThenEscape or PauseSequenceKind.EscapeThenClick;

    private RepeatMode SelectedRepeatMode => _repeatModeInput.SelectedIndex switch
    {
        1 => RepeatMode.Toggle,
        2 => RepeatMode.Count,
        _ => RepeatMode.Hold
    };

    private MacroFirstStepKind SelectedFirstStepKind =>
        _firstStepTypeInput.SelectedIndex == 1 ? MacroFirstStepKind.MouseClick : MacroFirstStepKind.KeyboardKey;

    private void SaveAndClose()
    {
        if (string.IsNullOrWhiteSpace(_nameInput.Text))
        {
            MessageBox.Show("매크로 이름을 입력하세요.", "입력 확인", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (SequenceEnabled && !RepeatingPause && _keyboardKey is Keys.None or Keys.Escape)
        {
            MessageBox.Show("1단계에 ESC와 함께 누를 스킬 키(예: 1, 2, 3)를 지정하세요.",
                "스킬 키 선택", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!_keyboardEnabledInput.Checked && !_mouseEnabledInput.Checked)
        {
            MessageBox.Show("반복할 동작을 하나 이상 켜세요.", "입력 확인", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_mouseEnabledInput.Checked && _bindingInput.SelectedItem is not BindingChoice)
        {
            MessageBox.Show("2단계에서 사용할 좌표 동작을 선택하세요.", "입력 확인", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_keyboardEnabledInput.Checked &&
            SelectedFirstStepKind == MacroFirstStepKind.MouseClick &&
            _mouseEnabledInput.Checked &&
            _bindingInput.SelectedItem is BindingChoice selectedChoice &&
            (selectedChoice.Id == RapidMacroSettings.CurrentCursorBindingId ||
             _bindings.FirstOrDefault(binding => binding.Id == selectedChoice.Id)?.CoordinateSpace == CoordinateSpace.CurrentCursor))
        {
            MessageBox.Show("마우스 입력 다음에 현재 위치 마우스 입력을 연속으로 사용할 수 없습니다.",
                "입력 조합 확인", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _working.Name = _nameInput.Text.Trim();
        _working.PauseSkillSequence = SequenceEnabled;
        _working.StableInput = _stableInput.Checked;
        _working.PauseSequenceKind = SelectedSequenceKind;
        _working.PauseAfterClick = _pauseAfterInput.Checked;
        _working.SequenceHoldMs = (int)_sequenceHoldInput.Value;
        _working.BeforeClickMs = (int)_beforeClickInput.Value;
        _working.BeforePauseMs = (int)_beforePauseInput.Value;
        _working.Enabled = _enabledInput.Checked;
        _working.TriggerKey = (int)_triggerKey;
        _working.KeyboardEnabled = _keyboardEnabledInput.Checked;
        _working.UseFirstKeyBinding = _firstKeyBindingInput.Checked;
        _working.FirstStepKind = SelectedFirstStepKind;
        _working.KeyboardKey = (int)_keyboardKey;
        _working.FirstStepMouseButton = (MouseButtonKind)Math.Max(0, _firstStepMouseButtonInput.SelectedIndex);
        _working.MouseEnabled = _mouseEnabledInput.Checked;
        _working.BindingId = (_bindingInput.SelectedItem as BindingChoice)?.Id
                             ?? RapidMacroSettings.CurrentCursorBindingId;
        _working.IntervalMs = (int)_intervalInput.Value;
        _working.RepeatMode = SelectedRepeatMode;
        _working.RepeatCount = (int)_countInput.Value;
        Result = _working;
        DialogResult = DialogResult.OK;
    }

    private void AddLabel(string text, int x, int y) => Controls.Add(CreateLabel(text, x, y));

    private static Label CreateLabel(string text, int x, int y) => new()
    {
        Text = text,
        ForeColor = Secondary,
        AutoSize = true,
        Location = new Point(x, y),
        Font = new Font("Segoe UI Semibold", 8.5F)
    };

    private static TextBox CreateTextInput(int x, int y, int width) => new()
    {
        Location = new Point(x, y),
        Size = new Size(width, 34),
        BackColor = Color.FromArgb(11, 18, 32),
        ForeColor = Primary,
        BorderStyle = BorderStyle.FixedSingle
    };

    private static NumericUpDown CreateNumber(int x, int y, int width, decimal minimum, decimal maximum) => new()
    {
        Location = new Point(x, y),
        Size = new Size(width, 34),
        Minimum = minimum,
        Maximum = maximum,
        BackColor = Color.FromArgb(11, 18, 32),
        ForeColor = Primary,
        BorderStyle = BorderStyle.FixedSingle,
        TextAlign = HorizontalAlignment.Right,
        ThousandsSeparator = true
    };

    private static ComboBox CreateCombo(int x, int y, int width, string[] items)
    {
        var combo = new ComboBox
        {
            Location = new Point(x, y),
            Size = new Size(width, 34),
            BackColor = Color.FromArgb(11, 18, 32),
            ForeColor = Primary,
            FlatStyle = FlatStyle.Flat,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        combo.Items.AddRange(items);
        return combo;
    }

    private static CheckBox CreateCheckBox(string text, int x, int y) => new()
    {
        Text = text,
        ForeColor = Secondary,
        BackColor = Surface,
        AutoSize = true,
        FlatStyle = FlatStyle.Flat,
        Location = new Point(x, y)
    };

    private static Button CreateButton(string text, int x, int y, int width, int height, bool primary = false)
    {
        var button = new Button
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, height),
            BackColor = primary ? Accent : Color.FromArgb(27, 39, 61),
            ForeColor = primary ? Color.FromArgb(6, 23, 31) : Primary,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI Semibold", 9F)
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(42, 54, 82);
        return button;
    }

    private static decimal Clamp(int value, decimal minimum, decimal maximum) =>
        Math.Min(maximum, Math.Max(minimum, value));

    private sealed record BindingChoice(string Id, string Label)
    {
        public override string ToString() => Label;
    }

    private enum KeyCaptureTarget
    {
        None,
        Trigger,
        KeyboardOutput
    }
}
