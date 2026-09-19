using System.Drawing.Drawing2D;

namespace RapidPoint;

internal sealed class CoordinateCaptureOverlay : Form
{
    private readonly IntPtr _targetWindow;
    private readonly System.Windows.Forms.Timer _syncTimer = new() { Interval = 50 };
    private Point _cursorPoint;

    public NativeMethods.Point? CapturedScreenPoint { get; private set; }
    public string? FailureMessage { get; private set; }

    public CoordinateCaptureOverlay(IntPtr targetWindow)
    {
        _targetWindow = targetWindow;
        ConfigureWindow();
        _syncTimer.Tick += (_, _) => SyncToTargetWindow();
        MouseMove += (_, eventArgs) =>
        {
            _cursorPoint = eventArgs.Location;
            Invalidate();
        };
        MouseDown += OnOverlayMouseDown;
        KeyDown += OnOverlayKeyDown;
        Shown += (_, _) =>
        {
            if (SyncToTargetWindow())
            {
                _syncTimer.Start();
                Activate();
            }
        };
        FormClosed += (_, _) =>
        {
            _syncTimer.Stop();
            _syncTimer.Dispose();
        };
    }

    private void ConfigureWindow()
    {
        Text = "좌표 선택";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        Cursor = Cursors.Cross;
        BackColor = Color.FromArgb(8, 145, 178);
        Opacity = 0.32;
        DoubleBuffered = true;
    }

    private bool SyncToTargetWindow()
    {
        if (_targetWindow == IntPtr.Zero || !NativeMethods.IsWindow(_targetWindow) ||
            !NativeMethods.GetClientRect(_targetWindow, out var clientRectangle) ||
            clientRectangle.Width <= 0 || clientRectangle.Height <= 0)
        {
            FailureMessage = "대상 창을 찾지 못했거나 창 크기를 읽을 수 없습니다.";
            DialogResult = DialogResult.Abort;
            Close();
            return false;
        }

        var clientOrigin = new NativeMethods.Point { X = 0, Y = 0 };
        if (!NativeMethods.ClientToScreen(_targetWindow, ref clientOrigin))
        {
            FailureMessage = "대상 창의 화면 위치를 읽을 수 없습니다.";
            DialogResult = DialogResult.Abort;
            Close();
            return false;
        }

        var targetBounds = new Rectangle(
            clientOrigin.X,
            clientOrigin.Y,
            clientRectangle.Width,
            clientRectangle.Height);
        if (Bounds != targetBounds)
        {
            Bounds = targetBounds;
            if (_cursorPoint == Point.Empty)
            {
                _cursorPoint = new Point(ClientSize.Width / 2, ClientSize.Height / 2);
            }
        }
        return true;
    }

    private void OnOverlayMouseDown(object? sender, MouseEventArgs eventArgs)
    {
        if (eventArgs.Button != MouseButtons.Left)
        {
            return;
        }
        CaptureAtClientPoint(eventArgs.Location);
    }

    internal void CaptureAtClientPoint(Point clientPoint)
    {
        var screenPoint = PointToScreen(clientPoint);
        CapturedScreenPoint = new NativeMethods.Point { X = screenPoint.X, Y = screenPoint.Y };
        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnOverlayKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.KeyCode != Keys.Escape)
        {
            return;
        }
        eventArgs.Handled = true;
        eventArgs.SuppressKeyPress = true;
        DialogResult = DialogResult.Cancel;
        Close();
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var gridPen = new Pen(Color.FromArgb(170, 255, 255, 255), 1F)
        {
            DashStyle = DashStyle.Dash
        };
        eventArgs.Graphics.DrawLine(gridPen, _cursorPoint.X, 0, _cursorPoint.X, ClientSize.Height);
        eventArgs.Graphics.DrawLine(gridPen, 0, _cursorPoint.Y, ClientSize.Width, _cursorPoint.Y);

        var instruction = "원하는 위치를 좌클릭하세요  ·  Esc 취소";
        var instructionRectangle = new Rectangle(0, 18, ClientSize.Width, 42);
        using var instructionFont = new Font("Segoe UI Semibold", 13F, FontStyle.Bold);
        TextRenderer.DrawText(
            eventArgs.Graphics,
            instruction,
            instructionFont,
            instructionRectangle,
            Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}
