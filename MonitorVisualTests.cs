using System.Runtime.InteropServices;

namespace RapidPoint;

internal static class MonitorVisualTests
{
    internal static void Run()
    {
        foreach (var message in new[] { 0x201, 0x204, 0x207, 0x20B })
            if (!MouseClickObserver.IsButtonDown(message)) throw new Exception("Mouse press not observed.");
        foreach (var message in new[] { 0x200, 0x202, 0x205, 0x208, 0x20A, 0x20C })
            if (MouseClickObserver.IsButtonDown(message)) throw new Exception("Move/release/wheel treated as click.");
        if (Marshal.SizeOf<MouseClickObserver.MouseHookData>() != (IntPtr.Size == 8 ? 32 : 24))
            throw new Exception("Mouse hook ABI layout mismatch.");

        var trail = new ClickMarkerTrail();
        trail.Add(new NativeMethods.Point { X = 320, Y = 180 }, 1280, 720, 100);
        if (trail.Markers[0].AtSize(new Size(1920, 1080)) != new Point(480, 270))
            throw new Exception("Click marker resize coordinate mismatch.");
        trail.Add(new NativeMethods.Point { X = -1, Y = 1 }, 1280, 720, 100);
        if (trail.Markers.Count != 1) throw new Exception("Recorded a click outside the client.");
        trail.Expire(899);
        if (trail.Markers.Count != 1) throw new Exception("Marker disappeared early.");
        trail.Expire(900);
        if (trail.Markers.Count != 0) throw new Exception("Marker did not expire.");
        for (var i = 0; i < 10000; i++)
            trail.Add(new NativeMethods.Point { X = i % 1000, Y = 10 }, 1280, 720, 1000);
        if (trail.Markers.Count != 8 || trail.Markers[^1].X != 999) throw new Exception("Click history is not bounded/latest.");
        trail.Clear();
        if (trail.Markers.Count != 0) throw new Exception("Disabled HUD retained click history.");

        var display = CoordinateReadout.Describe(new NativeMethods.Point { X = 700, Y = 500 },
            new NativeMethods.Point { X = 600, Y = 465 }, 1920, 1080,
            new CoordinateBindingSettings { Name = "바인딩 1", CoordinateSpace = CoordinateSpace.TargetWindow, ReferenceWidth = 1419, ReferenceHeight = 798 });
        if (display.Current != "X: 600    Y: 465" || display.BindingValue != "X: 443    Y: 344" ||
            !display.BindingTitle.Contains("설정 입력용") || !display.WindowSize.Contains("1920 × 1080") ||
            !display.BindingSize.Contains("1419 × 798")) throw new Exception("Readable HUD changed coordinate basis.");

        using var overlay = new CoordinateMonitorOverlay();
        overlay.SetDisplay(display, new Point(700, 400));
        overlay.AddPreviewClick(new Point(550, 300));
        _ = overlay.Handle;
        using var bitmap = new Bitmap(overlay.Width, overlay.Height);
        overlay.DrawToBitmap(bitmap, overlay.ClientRectangle);
        if (bitmap.GetPixel(700, 400).ToArgb() != Color.FromArgb(92, 224, 255).ToArgb() ||
            bitmap.GetPixel(700, 220).ToArgb() != Color.FromArgb(92, 224, 255).ToArgb() ||
            bitmap.GetPixel(820, 400).ToArgb() != Color.FromArgb(92, 224, 255).ToArgb())
            throw new Exception("Crosshair is not centered on the cursor.");
        if (bitmap.GetPixel(550, 300).ToArgb() != Color.FromArgb(255, 65, 75).ToArgb())
            throw new Exception("Click position is not marked red.");
        if (bitmap.GetPixel(699, 220).ToArgb() != overlay.TransparencyKey.ToArgb() ||
            bitmap.GetPixel(701, 220).ToArgb() != overlay.TransparencyKey.ToArgb() ||
            bitmap.GetPixel(820, 399).ToArgb() != overlay.TransparencyKey.ToArgb() ||
            bitmap.GetPixel(820, 401).ToArgb() != overlay.TransparencyKey.ToArgb())
            throw new Exception("Crosshair is thicker than one pixel.");
        if (overlay.RecentClick?.Current != "X: 550    Y: 300") throw new Exception("Recent click was not captured.");
        overlay.AddPreviewClick(new Point(400, 250));
        if (overlay.RecentClick?.Current != "X: 400    Y: 250") throw new Exception("Latest click did not replace the previous one.");
        var latest = overlay.RecentClick;
        overlay.AddPreviewClick(new Point(-1, 250));
        overlay.SetDisplay(display, new Point(650, 350));
        if (overlay.RecentClick != latest) throw new Exception("Cursor motion or outside click changed last click coordinates.");
        overlay.ClientSize = new Size(1000, 600);
        if (overlay.RecentClick != latest || !latest!.WindowSize.Contains("960 × 540"))
            throw new Exception("Resize changed the click-time coordinate basis.");
        overlay.ClientSize = new Size(960, 540);
        if (bitmap.GetPixel(850, 250).ToArgb() != overlay.TransparencyKey.ToArgb())
            throw new Exception("HUD tinted the untouched game background.");
        overlay.SetDisplay(display, null);
        overlay.DrawToBitmap(bitmap, overlay.ClientRectangle);
        if (bitmap.GetPixel(700, 400).ToArgb() != overlay.TransparencyKey.ToArgb())
            throw new Exception("Crosshair remained when pointer left the game.");
        overlay.RefreshTarget(IntPtr.Zero, null, false);
        if (overlay.RecentClick != null) throw new Exception("Disabling HUD retained the recent click.");
    }
}
