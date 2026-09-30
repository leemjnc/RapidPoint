namespace RapidPoint;

internal sealed class BindingEditorDialog : Form
{
    private static readonly Color Background = Color.FromArgb(10, 15, 29);
    private static readonly Color Surface = Color.FromArgb(20, 28, 48);
    private static readonly Color Primary = Color.FromArgb(241, 245, 249);
    private static readonly Color Secondary = Color.FromArgb(148, 163, 184);
    private static readonly Color Accent = Color.FromArgb(34, 211, 238);

    private readonly IntPtr _targetWindow;
    private readonly HashSet<Keys> _reservedKeys;
    private readonly CoordinateBindingSettings _working;
    private TextBox _nameInput = null!;
    private Button _keyButton = null!;
    private ComboBox _actionInput = null!;
    private ComboBox _mouseButtonInput = null!;
    private ComboBox _activationInput = null!;
    private RadioButton _screenOption = null!;
    private RadioButton _windowOption = null!;
    private RadioButton _currentCursorOption = null!;
    private NumericUpDown _xInput = null!;
    private NumericUpDown _yInput = null!;
    private NumericUpDown _referenceWidthInput = null!;
    private NumericUpDown _referenceHeightInput = null!;
    private CheckBox _enabledInput = null!;
    private Button _captureButton = null!;
    private Label _coordinateTitleLabel = null!;
    private Label _coordinateHelp = null!;
    private bool _capturingKey;
    private Keys _triggerKey;

    public CoordinateBindingSettings? Result { get; private set; }

    public BindingEditorDialog(
        CoordinateBindingSettings binding,
        IntPtr targetWindow,
        IEnumerable<Keys> reservedKeys)
    {
        _working = binding.Clone();
        _targetWindow = targetWindow;
        _reservedKeys = reservedKeys.ToHashSet();
        _triggerKey = (Keys)_working.TriggerKey;
        ConfigureWindow();
        BuildInterface();
        ApplyValues();
    }

    private void ConfigureWindow()
    {
        Text = "좌표 바인딩 설정";
        BackColor = Background;
        ForeColor = Primary;
        Font = new Font("Segoe UI", 9.5F);
        ClientSize = new Size(560, 550);
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
            Text = "키 바인딩 설정",
            Font = new Font("Segoe UI Semibold", 17F, FontStyle.Bold),
            ForeColor = Primary,
            AutoSize = true,
            Location = new Point(24, 20)
        });
        Controls.Add(new Label
        {
            Text = "키를 누르면 지정 좌표에서 선택한 마우스 동작을 실행합니다.",
            ForeColor = Secondary,
            AutoSize = true,
            Location = new Point(26, 55)
        });

        AddLabel("이름", 26, 91);
        _nameInput = CreateTextInput(26, 113, 244);
        Controls.Add(_nameInput);
        AddLabel("할당 키 · 눌러서 변경", 292, 91);
        _keyButton = CreateButton("키 선택", 292, 112, 240, 35);
        _keyButton.Click += (_, _) =>
        {
            _capturingKey = true;
            _keyButton.Text = "키를 누르세요…";
            _keyButton.Focus();
        };
        Controls.Add(_keyButton);

        AddLabel("마우스 동작", 26, 163);
        _actionInput = CreateCombo(26, 185, 150, ["클릭", "마우스 이동"]);
        _actionInput.SelectedIndexChanged += (_, _) => UpdateActivationMode();
        Controls.Add(_actionInput);
        AddLabel("클릭 버튼", 190, 163);
        _mouseButtonInput = CreateCombo(190, 185, 132, ["좌클릭", "우클릭", "중클릭"]);
        Controls.Add(_mouseButtonInput);
        AddLabel("실행 방식", 336, 163);
        _activationInput = CreateCombo(336, 185, 196, ["한 번 실행", "누르는 동안 누름 유지", "키를 놓을 때 클릭"]);
        _activationInput.SelectedIndexChanged += (_, _) => UpdateActivationMode();
        Controls.Add(_activationInput);

        var coordinatePanel = new Panel
        {
            BackColor = Surface,
            Location = new Point(24, 235),
            Size = new Size(508, 190)
        };
        Controls.Add(coordinatePanel);
        _coordinateTitleLabel = new Label
        {
            Text = "좌표 설정",
            ForeColor = Primary,
            Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(18, 15)
        };
        coordinatePanel.Controls.Add(_coordinateTitleLabel);
        _screenOption = CreateRadio("화면 절대 좌표", 20, 51);
        _windowOption = CreateRadio("대상 창 내부", 156, 51);
        _currentCursorOption = CreateRadio("현재 위치 (이동 없음)", 282, 51);
        _screenOption.CheckedChanged += (_, _) => UpdateCoordinateMode();
        _windowOption.CheckedChanged += (_, _) => UpdateCoordinateMode();
        _currentCursorOption.CheckedChanged += (_, _) => UpdateActivationMode();
        coordinatePanel.Controls.Add(_screenOption);
        coordinatePanel.Controls.Add(_windowOption);
        coordinatePanel.Controls.Add(_currentCursorOption);

        coordinatePanel.Controls.Add(CreateLabel("X", 20, 92));
        _xInput = CreateNumber(43, 85, 118, -100000, 100000);
        coordinatePanel.Controls.Add(_xInput);
        coordinatePanel.Controls.Add(CreateLabel("Y", 177, 92));
        _yInput = CreateNumber(200, 85, 118, -100000, 100000);
        coordinatePanel.Controls.Add(_yInput);
        _captureButton = CreateButton("게임에서 좌표 찍기", 327, 83, 161, 37);
        _captureButton.Name = "CaptureCoordinates";
        _captureButton.Click += (_, _) => CapturePositionFromTargetWindow();
        coordinatePanel.Controls.Add(_captureButton);

        coordinatePanel.Controls.Add(CreateLabel("기준 해상도", 20, 143));
        _referenceWidthInput = CreateNumber(110, 136, 104, 1, 32768);
        coordinatePanel.Controls.Add(_referenceWidthInput);
        coordinatePanel.Controls.Add(CreateLabel("×", 223, 143));
        _referenceHeightInput = CreateNumber(243, 136, 104, 1, 32768);
        coordinatePanel.Controls.Add(_referenceHeightInput);

        _enabledInput = new CheckBox
        {
            Text = "이 바인딩 사용",
            ForeColor = Secondary,
            BackColor = Surface,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            Location = new Point(356, 142)
        };
        coordinatePanel.Controls.Add(_enabledInput);

        _coordinateHelp = new Label
        {
            Location = new Point(26, 439), Size = new Size(506, 38),
            ForeColor = Secondary, Font = new Font("Segoe UI", 9F)
        };
        Controls.Add(_coordinateHelp);
        var cancelButton = CreateButton("취소", 318, 492, 102, 40);
        cancelButton.Click += (_, _) => DialogResult = DialogResult.Cancel;
        Controls.Add(cancelButton);
        var saveButton = CreateButton("설정 저장", 430, 492, 102, 40, true);
        saveButton.Click += (_, _) => SaveAndClose();
        Controls.Add(saveButton);
        AcceptButton = saveButton;
        CancelButton = cancelButton;
    }

    private void ApplyValues()
    {
        _nameInput.Text = _working.Name;
        _keyButton.Text = KeyFormatter.Format(_triggerKey);
        _actionInput.SelectedIndex = _working.MouseAction == InputActionKind.MouseMove ? 1 : 0;
        _mouseButtonInput.SelectedIndex = (int)_working.MouseButton;
        _activationInput.SelectedIndex = _working.Activation switch
        {
            BindingActivation.RepeatWhileHeld => 1,
            BindingActivation.ReleaseClick => 2,
            _ => 0
        };
        _screenOption.Checked = _working.CoordinateSpace == CoordinateSpace.Screen;
        _windowOption.Checked = _working.CoordinateSpace == CoordinateSpace.TargetWindow;
        _currentCursorOption.Checked = _working.CoordinateSpace == CoordinateSpace.CurrentCursor;
        _xInput.Value = Clamp(_working.X, _xInput.Minimum, _xInput.Maximum);
        _yInput.Value = Clamp(_working.Y, _yInput.Minimum, _yInput.Maximum);
        _referenceWidthInput.Value = Clamp(_working.ReferenceWidth, 1, 32768);
        _referenceHeightInput.Value = Clamp(_working.ReferenceHeight, 1, 32768);
        _enabledInput.Checked = _working.Enabled;
        UpdateActivationMode();
    }

    private void OnDialogKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (!_capturingKey)
        {
            return;
        }

        eventArgs.Handled = true;
        eventArgs.SuppressKeyPress = true;
        if (_reservedKeys.Contains(eventArgs.KeyCode))
        {
            MessageBox.Show("이미 사용 중이거나 제어용으로 예약된 키입니다.", "키 충돌", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _triggerKey = eventArgs.KeyCode;
        _capturingKey = false;
        _keyButton.Text = KeyFormatter.Format(_triggerKey);
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (_capturingKey && (keyData & Keys.KeyCode) == Keys.Escape)
        {
            _triggerKey = Keys.Escape;
            _capturingKey = false;
            _keyButton.Text = KeyFormatter.Format(_triggerKey);
            return true;
        }
        return base.ProcessDialogKey(keyData);
    }

    private bool CaptureTargetRelativePosition(NativeMethods.Point point)
    {
        if (!CoordinateReadout.TryClientPoint(_targetWindow, point, out point, out var rectangle))
        {
            return false;
        }

        _referenceWidthInput.Value = Clamp(rectangle.Width, 1, 32768);
        _referenceHeightInput.Value = Clamp(rectangle.Height, 1, 32768);
        _xInput.Value = Clamp(point.X, _xInput.Minimum, _xInput.Maximum);
        _yInput.Value = Clamp(point.Y, _yInput.Minimum, _yInput.Maximum);
        _coordinateTitleLabel.Text = "좌표 설정 완료 · 창 이동·크기 자동 보정";
        return true;
    }

    private async void CapturePositionFromTargetWindow()
    {
        if (_targetWindow == IntPtr.Zero || !NativeMethods.IsWindow(_targetWindow))
        {
            MessageBox.Show("먼저 메인 화면에서 대상 창을 지정하세요.", "대상 창 필요", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        _windowOption.Checked = true;
        var dialogOpacity = Opacity;
        var ownerOpacity = Owner?.Opacity ?? 1D;
        DialogResult captureResult;
        NativeMethods.Point? capturedPoint;
        string? failureMessage;
        var coordinateConversionFailed = false;
        try
        {
            _captureButton.Enabled = false;
            Opacity = 0;
            if (Owner is not null)
            {
                Owner.Opacity = 0;
            }
            NativeMethods.ShowWindowAsync(_targetWindow, NativeMethods.SwRestore);
            NativeMethods.SetForegroundWindow(_targetWindow);
            await Task.Delay(250);
            using var overlay = new CoordinateCaptureOverlay(_targetWindow);
            captureResult = overlay.ShowDialog();
            capturedPoint = overlay.CapturedScreenPoint;
            failureMessage = overlay.FailureMessage;
            if (captureResult == DialogResult.OK && capturedPoint.HasValue)
            {
                coordinateConversionFailed = !CaptureTargetRelativePosition(capturedPoint.Value);
            }
        }
        finally
        {
            if (Owner is not null)
            {
                Owner.Opacity = ownerOpacity;
            }
            if (!IsDisposed)
            {
                DialogResult = DialogResult.None;
                Opacity = dialogOpacity;
                _captureButton.Enabled = true;
                Activate();
                BringToFront();
            }
        }
        if (captureResult == DialogResult.OK && capturedPoint.HasValue)
        {
            if (coordinateConversionFailed)
            {
                MessageBox.Show("선택한 위치를 대상 창 좌표로 변환하지 못했습니다.", "좌표 입력", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            return;
        }
        if (captureResult == DialogResult.Abort && !string.IsNullOrWhiteSpace(failureMessage))
        {
            MessageBox.Show(failureMessage, "좌표 입력", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void UpdateCoordinateMode()
    {
        var currentCursorMode = _currentCursorOption.Checked;
        var windowMode = !currentCursorMode && _windowOption.Checked;
        _screenOption.Enabled = true;
        _windowOption.Enabled = true;
        _currentCursorOption.Enabled = true;
        _xInput.Enabled = !currentCursorMode;
        _yInput.Enabled = !currentCursorMode;
        _captureButton.Enabled = !currentCursorMode;
        _referenceWidthInput.Enabled = windowMode;
        _referenceHeightInput.Enabled = windowMode;
        _coordinateHelp.Text = currentCursorMode
            ? "마우스를 움직이지 않고, 현재 포인터 위치에서 클릭합니다."
            : windowMode
                ? "‘게임에서 좌표 찍기’ → 원하는 위치 클릭 → 설정 저장\n창을 이동하거나 크기를 바꿔도 기준 해상도에 맞춰 보정합니다."
                : "모니터 전체를 기준으로 한 좌표입니다. 창 이동·크기는 보정하지 않습니다.\n게임 창을 움직이며 사용한다면 ‘대상 창 내부’를 선택하세요.";
    }

    private void UpdateActivationMode()
    {
        var holdMode = _activationInput.SelectedIndex == 1;
        var releaseMode = _activationInput.SelectedIndex == 2;
        var currentCursorMode = _currentCursorOption.Checked;
        _actionInput.Enabled = !holdMode && !releaseMode && !currentCursorMode;
        if (holdMode || releaseMode || currentCursorMode)
        {
            _actionInput.SelectedIndex = 0;
        }
        _coordinateTitleLabel.Text = currentCursorMode
            ? holdMode
                ? "현재 위치 · 누르는 동안 버튼 누름 유지"
                : releaseMode
                    ? "이동 없이 대기 · 놓으면 현재 위치 클릭"
                    : "현재 위치에서 클릭 · 마우스 이동 없음"
            : holdMode
                ? "지정 좌표로 이동 · 누르는 동안 버튼 누름 유지"
                : releaseMode
                    ? "누르면 지정 좌표로 이동 · 놓을 때 다시 이동 후 클릭"
                    : "좌표 설정";
        _mouseButtonInput.Enabled = holdMode || releaseMode || currentCursorMode || _actionInput.SelectedIndex == 0;
        UpdateCoordinateMode();
    }

    private void SaveAndClose()
    {
        if (string.IsNullOrWhiteSpace(_nameInput.Text))
        {
            MessageBox.Show("바인딩 이름을 입력하세요.", "입력 확인", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var holdMode = _activationInput.SelectedIndex == 1;
        var releaseMode = _activationInput.SelectedIndex == 2;
        var currentCursorMode = _currentCursorOption.Checked;
        if (_windowOption.Checked && (_targetWindow == IntPtr.Zero || !NativeMethods.IsWindow(_targetWindow)))
        {
            MessageBox.Show("대상 창 내부 좌표를 사용하려면 먼저 메인 화면의 창 목록에서 대상을 선택하세요.", "대상 창 필요", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var x = (int)_xInput.Value;
        var y = (int)_yInput.Value;
        if (_windowOption.Checked &&
            (x < 0 || y < 0 || x >= _referenceWidthInput.Value || y >= _referenceHeightInput.Value))
        {
            MessageBox.Show("X/Y 좌표가 기준 해상도 범위 밖입니다.", "좌표 확인", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_screenOption.Checked && !SystemInformation.VirtualScreen.Contains(x, y))
        {
            MessageBox.Show("X/Y 좌표가 현재 화면 범위 밖입니다.", "좌표 확인", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _working.Name = _nameInput.Text.Trim();
        _working.TriggerKey = (int)_triggerKey;
        _working.MouseAction = holdMode || releaseMode || currentCursorMode || _actionInput.SelectedIndex == 0
            ? InputActionKind.MouseClick
            : InputActionKind.MouseMove;
        _working.MouseButton = (MouseButtonKind)Math.Max(0, _mouseButtonInput.SelectedIndex);
        _working.Activation = _activationInput.SelectedIndex switch
        {
            1 => BindingActivation.RepeatWhileHeld,
            2 => BindingActivation.ReleaseClick,
            _ => BindingActivation.SinglePress
        };
        _working.CoordinateSpace = currentCursorMode
            ? CoordinateSpace.CurrentCursor
            : _windowOption.Checked ? CoordinateSpace.TargetWindow : CoordinateSpace.Screen;
        _working.X = x;
        _working.Y = y;
        _working.ReferenceWidth = (int)_referenceWidthInput.Value;
        _working.ReferenceHeight = (int)_referenceHeightInput.Value;
        _working.Enabled = _enabledInput.Checked;
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

    private static RadioButton CreateRadio(string text, int x, int y) => new()
    {
        Text = text,
        ForeColor = Primary,
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
}
