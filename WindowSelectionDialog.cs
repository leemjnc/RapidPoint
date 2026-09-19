using System.Diagnostics;

namespace RapidPoint;

internal sealed class WindowSelectionDialog : Form
{
    private static readonly Color Background = Color.FromArgb(10, 15, 29);
    private static readonly Color Surface = Color.FromArgb(20, 28, 48);
    private static readonly Color Primary = Color.FromArgb(241, 245, 249);
    private static readonly Color Secondary = Color.FromArgb(148, 163, 184);
    private static readonly Color Accent = Color.FromArgb(34, 211, 238);

    private readonly IntPtr _currentWindow;
    private ComboBox _windowInput = null!;
    private Label _countLabel = null!;

    public IntPtr SelectedWindow { get; private set; }

    public WindowSelectionDialog(IntPtr currentWindow)
    {
        _currentWindow = currentWindow;
        ConfigureWindow();
        BuildInterface();
        RefreshWindowList();
    }

    private void ConfigureWindow()
    {
        Text = "대상 창 선택";
        BackColor = Background;
        ForeColor = Primary;
        Font = new Font("Segoe UI", 9.5F);
        ClientSize = new Size(650, 245);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
    }

    private void BuildInterface()
    {
        Controls.Add(new Label
        {
            Text = "OBS 방식으로 대상 창 선택",
            ForeColor = Primary,
            Font = new Font("Segoe UI Semibold", 17F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(24, 20)
        });
        Controls.Add(new Label
        {
            Text = "현재 열려 있는 창에서 키 바인딩과 매크로를 사용할 창을 선택하세요.",
            ForeColor = Secondary,
            AutoSize = true,
            Location = new Point(26, 57)
        });
        Controls.Add(new Label
        {
            Text = "윈도우",
            ForeColor = Secondary,
            Font = new Font("Segoe UI Semibold", 8.5F),
            AutoSize = true,
            Location = new Point(26, 93)
        });

        _windowInput = new ComboBox
        {
            Location = new Point(26, 115),
            Size = new Size(487, 36),
            BackColor = Surface,
            ForeColor = Primary,
            FlatStyle = FlatStyle.Flat,
            DropDownStyle = ComboBoxStyle.DropDownList,
            DropDownHeight = 270,
            Font = new Font("Segoe UI", 9.2F)
        };
        _windowInput.DoubleClick += (_, _) => SelectAndClose();
        Controls.Add(_windowInput);

        var refreshButton = CreateButton("새로 고침", 525, 114, 101, 37);
        refreshButton.Click += (_, _) => RefreshWindowList();
        Controls.Add(refreshButton);

        _countLabel = new Label
        {
            ForeColor = Secondary,
            AutoSize = true,
            Location = new Point(27, 162),
            Font = new Font("Segoe UI", 8.5F)
        };
        Controls.Add(_countLabel);

        var cancelButton = CreateButton("취소", 412, 184, 102, 40);
        cancelButton.Click += (_, _) => DialogResult = DialogResult.Cancel;
        Controls.Add(cancelButton);
        var selectButton = CreateButton("선택", 524, 184, 102, 40, true);
        selectButton.Click += (_, _) => SelectAndClose();
        Controls.Add(selectButton);
        AcceptButton = selectButton;
        CancelButton = cancelButton;
    }

    private void RefreshWindowList()
    {
        var previouslySelected = (_windowInput.SelectedItem as WindowChoice)?.Handle ?? _currentWindow;
        var choices = EnumerateWindows();
        _windowInput.BeginUpdate();
        _windowInput.Items.Clear();
        _windowInput.Items.AddRange(choices.Cast<object>().ToArray());
        _windowInput.EndUpdate();

        var selectedIndex = choices.FindIndex(choice => choice.Handle == previouslySelected);
        _windowInput.SelectedIndex = selectedIndex >= 0 ? selectedIndex : (choices.Count > 0 ? 0 : -1);
        _countLabel.Text = choices.Count > 0
            ? $"선택 가능한 창 {choices.Count}개"
            : "선택할 수 있는 창이 없습니다.";
    }

    private static List<WindowChoice> EnumerateWindows()
    {
        var choices = new List<WindowChoice>();
        NativeMethods.EnumWindows((window, _) =>
        {
            if (!NativeMethods.IsWindowVisible(window) || NativeMethods.GetWindowTextLength(window) <= 0)
            {
                return true;
            }
            NativeMethods.GetWindowThreadProcessId(window, out var processId);
            if (processId == 0 || processId == Environment.ProcessId)
            {
                return true;
            }
            try
            {
                using var process = Process.GetProcessById((int)processId);
                var processName = process.ProcessName;
                var title = NativeMethods.GetWindowTitle(window);
                if (!string.IsNullOrWhiteSpace(processName) && !string.IsNullOrWhiteSpace(title))
                {
                    choices.Add(new WindowChoice(window, processName, title));
                }
            }
            catch
            {
                // 종료 중이거나 접근할 수 없는 창은 목록에서 제외합니다.
            }
            return true;
        }, IntPtr.Zero);

        return choices
            .OrderBy(choice => choice.ProcessName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(choice => choice.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private void SelectAndClose()
    {
        if (_windowInput.SelectedItem is not WindowChoice choice || !NativeMethods.IsWindow(choice.Handle))
        {
            MessageBox.Show("사용할 창을 선택하세요.", "대상 창 선택", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        SelectedWindow = choice.Handle;
        DialogResult = DialogResult.OK;
    }

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

    private sealed record WindowChoice(IntPtr Handle, string ProcessName, string Title)
    {
        public override string ToString() => $"[{ProcessName}.exe]: {Title}";
    }
}
