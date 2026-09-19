using System.Drawing.Drawing2D;

namespace RapidPoint;

internal sealed class CoordinateMonitorOverlay : Form
{
    internal const int MonitorStyles = 0x00080000 | 0x00000020 | 0x08000000 | 0x00000080;
    private static readonly Rectangle ReadoutBounds = new(8, 8, 500, 185);
    private readonly ClickMarkerTrail _clicks = new();
    private readonly List<ClickMarkerTrail.Marker> _paintedMarkers = new(ClickMarkerTrail.MaximumMarkers);
    private CoordinateReadout.Display _display = new("", "", "", "", "");
    private MouseClickObserver? _observer;
    private IntPtr _target;
    private Point? _cursor;
    private CoordinateReadout.Display? _recentClick;
    private IntPtr _recentTarget;
    private bool _recentClickDirty;
    internal CoordinateReadout.Display? RecentClick => _recentClick;
    private Rectangle RecentClickBounds => new(8, Math.Max(0, ClientSize.Height - 92), 500, 92);

    internal CoordinateMonitorOverlay()
    {
        Text = "RapidPoint 좌표 보기";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
        DoubleBuffered = true;
        ClientSize = new Size(960, 540);
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            // Layered, click-through, no activation, hidden from the taskbar.
            parameters.ExStyle |= MonitorStyles;
            return parameters;
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0021) { message.Result = new IntPtr(3); return; }
        if (message.Msg == 0x0084) { message.Result = new IntPtr(-1); return; }
        base.WndProc(ref message);
    }

    internal void RefreshTarget(IntPtr target, CoordinateBindingSettings? binding, bool enabled)
    {
        // Keep the last click while temporarily switching to another application,
        // but never show another target's history or persist it after disabling.
        if (!enabled || _recentTarget != target)
        {
            _recentClick = null;
            _recentTarget = target;
            _recentClickDirty = true;
        }
        if (!enabled || target == IntPtr.Zero || !NativeMethods.IsWindow(target) ||
            NativeMethods.GetAncestor(NativeMethods.GetForegroundWindow(), NativeMethods.GaRoot) != target ||
            !NativeMethods.GetCursorPos(out var screen) ||
            !CoordinateReadout.TryClientPoint(target, screen, out var point, out var bounds))
        {
            Suspend();
            return;
        }
        var origin = new NativeMethods.Point();
        if (!NativeMethods.ClientToScreen(target, ref origin)) { Suspend(); return; }
        if (_target != target) { _clicks.Clear(); _target = target; Invalidate(); }
        var windowBounds = new Rectangle(origin.X, origin.Y, bounds.Width, bounds.Height);
        if (Bounds != windowBounds) { Bounds = windowBounds; Invalidate(); }
        _observer ??= new MouseClickObserver(ObserveClick);
        SetDisplay(CoordinateReadout.Describe(screen, point, bounds.Width, bounds.Height, binding), new Point(point.X, point.Y));
        // Repaint only small old/new marker regions, including evicted/expired ones.
        foreach (var marker in _paintedMarkers) InvalidateMarker(marker);
        _clicks.Expire(Environment.TickCount64);
        foreach (var marker in _clicks.Markers) InvalidateMarker(marker);
        _paintedMarkers.Clear();
        _paintedMarkers.AddRange(_clicks.Markers);
        if (_recentClickDirty) { Invalidate(RecentClickBounds); _recentClickDirty = false; }
        if (!Visible) Show();
    }

    private void ObserveClick(NativeMethods.Point screen)
    {
        // Hook and timer run on the UI message thread. Store bounded visual state
        // only; rendering is deferred to the 33ms timer and never blocks this hook.
        if (_target == IntPtr.Zero || !Visible ||
            NativeMethods.GetAncestor(NativeMethods.GetForegroundWindow(), NativeMethods.GaRoot) != _target ||
            !CoordinateReadout.TryClientPoint(_target, screen, out var point, out var bounds)) return;
        RecordClick(screen, point, bounds.Width, bounds.Height);
    }

    private void Suspend()
    {
        _target = IntPtr.Zero;
        _observer?.Dispose();
        _observer = null;
        _clicks.Clear();
        _cursor = null;
        _paintedMarkers.Clear();
        if (Visible) Hide();
    }

    internal void SetDisplay(CoordinateReadout.Display display, Point? cursor)
    {
        var next = cursor is { } p && ClientRectangle.Contains(p) ? cursor : null;
        if (_cursor != next)
        {
            InvalidateCrosshair(_cursor);
            _cursor = next;
            InvalidateCrosshair(_cursor);
        }
        if (_display != display) { _display = display; Invalidate(ReadoutBounds); }
    }

    internal void AddPreviewClick(Point point)
    {
        var position = new NativeMethods.Point { X = point.X, Y = point.Y };
        RecordClick(position, position, ClientSize.Width, ClientSize.Height);
    }

    internal void RecordClick(NativeMethods.Point screen, NativeMethods.Point client, int width, int height)
    {
        if (width <= 0 || height <= 0 || client.X < 0 || client.Y < 0 || client.X >= width || client.Y >= height) return;
        _clicks.Add(client, width, height, Environment.TickCount64);
        // One event snapshot only. Moving/resizing after the click must not change
        // the number; its original client dimensions remain visible underneath.
        _recentClick = CoordinateReadout.Describe(screen, client, width, height, null);
        _recentClickDirty = true;
    }

    private void InvalidateCrosshair(Point? point)
    {
        if (point is not { } p) return;
        Invalidate(new Rectangle(p.X - 2, 0, 5, ClientSize.Height));
        Invalidate(new Rectangle(0, p.Y - 2, ClientSize.Width, 5));
    }

    private void InvalidateMarker(ClickMarkerTrail.Marker marker)
    {
        var p = marker.AtSize(ClientSize);
        Invalidate(new Rectangle(p.X - 17, p.Y - 17, 35, 35));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (ClientSize.Width < 8 || ClientSize.Height < 8) return;
        var graphics = e.Graphics;
        // Exact color-key transparency preserves every untouched game pixel.
        graphics.SmoothingMode = SmoothingMode.None;
        if (_cursor is { } cursor)
        {
            using var line = new Pen(Color.FromArgb(92, 224, 255), 1F);
            graphics.DrawLine(line, cursor.X, 0, cursor.X, ClientSize.Height);
            graphics.DrawLine(line, 0, cursor.Y, ClientSize.Width, cursor.Y);
        }
        foreach (var marker in _clicks.Markers)
        {
            var point = marker.AtSize(ClientSize);
            using var outline = new Pen(Color.Black, 5F);
            using var red = new Pen(Color.FromArgb(255, 65, 75), 3F);
            graphics.DrawEllipse(outline, point.X - 12, point.Y - 12, 24, 24);
            graphics.DrawEllipse(red, point.X - 12, point.Y - 12, 24, 24);
            graphics.DrawLine(red, point.X - 6, point.Y, point.X + 6, point.Y);
            graphics.DrawLine(red, point.X, point.Y - 6, point.X, point.Y + 6);
        }
        DrawText(graphics, "마우스 위치 · 창 좌표", 12, 10, 15, Color.FromArgb(92, 224, 255));
        DrawText(graphics, _display.Current, 12, 31, 25, Color.White);
        DrawText(graphics, _display.WindowSize, 12, 63, 14, Color.FromArgb(215, 225, 237));
        if (_display.BindingTitle.Length > 0)
        {
            DrawText(graphics, _display.BindingTitle, 12, 91, 15, Color.FromArgb(255, 222, 130));
            DrawText(graphics, _display.BindingValue, 12, 112, 23, Color.White);
            DrawText(graphics, _display.BindingSize, 12, 142, 14, Color.FromArgb(215, 225, 237));
        }
        if (_observer is { IsInstalled: false })
            DrawText(graphics, "클릭 표시 연결 실패 · 좌표/안내선만 표시", 12, 165, 13, Color.Salmon);
        if (_recentClick is { } recent)
        {
            var top = Math.Max(0, ClientSize.Height - 86);
            DrawText(graphics, "최근 클릭 · 창 좌표", 12, top, 15, Color.FromArgb(255, 110, 120));
            DrawText(graphics, recent.Current, 12, top + 21, 23, Color.White);
            DrawText(graphics, recent.WindowSize.Replace("현재 창", "클릭 당시 창"), 12, top + 51, 14, Color.FromArgb(215, 225, 237));
        }
    }

    private static void DrawText(Graphics graphics, string text, int x, int y, int size, Color color)
    {
        using var path = new GraphicsPath();
        using var family = new FontFamily("Malgun Gothic");
        path.AddString(text, family, (int)FontStyle.Bold, size, new PointF(x, y), StringFormat.GenericDefault);
        using var outline = new Pen(Color.Black, 3F) { LineJoin = LineJoin.Round };
        using var fill = new SolidBrush(color);
        graphics.DrawPath(outline, path);
        graphics.FillPath(fill, path);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _observer?.Dispose(); _observer = null; }
        base.Dispose(disposing);
    }
}
