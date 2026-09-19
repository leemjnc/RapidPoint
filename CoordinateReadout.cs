namespace RapidPoint;

internal static class CoordinateReadout
{
    internal sealed record Display(string Current, string WindowSize, string BindingTitle, string BindingValue, string BindingSize);

    internal static Display Describe(NativeMethods.Point screen, NativeMethods.Point client,
        int width, int height, CoordinateBindingSettings? binding)
    {
        var inside = client.X >= 0 && client.Y >= 0 && client.X < width && client.Y < height;
        var current = inside ? $"X: {client.X}    Y: {client.Y}" : "마우스가 창 밖에 있습니다";
        var size = $"현재 창  {width} × {height}";
        if (binding == null) return new(current, size, "", "", "");
        var name = binding.Name.Length > 18 ? binding.Name[..18] + "…" : binding.Name;
        if (binding.CoordinateSpace == CoordinateSpace.CurrentCursor)
            return new(current, size, name, "현재 위치 사용", "저장 좌표 없음");
        if (binding.CoordinateSpace == CoordinateSpace.Screen)
            return new(current, size, name + " · 설정 입력용", $"X: {screen.X}    Y: {screen.Y}", "화면 절대 좌표");
        var saved = MacroEngine.ScaleMappedPoint(client.X, client.Y,
            binding.ReferenceWidth, binding.ReferenceHeight, width, height);
        return new(current, size, name + " · 설정 입력용",
            inside ? $"X: {saved.X}    Y: {saved.Y}" : "—",
            $"저장 기준  {binding.ReferenceWidth} × {binding.ReferenceHeight}");
    }

    // Both the picker and live HUD use the client area, excluding the title bar/borders.
    internal static bool TryClientPoint(IntPtr window, NativeMethods.Point screen,
        out NativeMethods.Point client, out NativeMethods.Rect bounds)
    {
        client = screen;
        bounds = default;
        return window != IntPtr.Zero && NativeMethods.IsWindow(window) &&
            NativeMethods.GetClientRect(window, out bounds) && bounds.Width > 0 && bounds.Height > 0 &&
            NativeMethods.ScreenToClient(window, ref client);
    }

    internal static string Format(NativeMethods.Point screen, NativeMethods.Point client,
        int width, int height, CoordinateBindingSettings? binding)
    {
        var inside = client.X >= 0 && client.Y >= 0 && client.X < width && client.Y < height;
        var first = inside ? $"창 X: {client.X}  Y: {client.Y}   ({width} × {height})"
            : $"창 밖   ({width} × {height})";
        if (binding == null) return first;
        var name = binding.Name.Length > 18 ? binding.Name[..18] + "…" : binding.Name;
        if (binding.CoordinateSpace == CoordinateSpace.Screen)
            return first + $"\n{name} · 화면 X: {screen.X}  Y: {screen.Y}";
        if (binding.CoordinateSpace == CoordinateSpace.CurrentCursor)
            return first + $"\n{name} · 현재 위치 방식 (저장 좌표 없음)";
        if (!inside) return first + $"\n{name} · 기준 {binding.ReferenceWidth} × {binding.ReferenceHeight}";
        var saved = MacroEngine.ScaleMappedPoint(client.X, client.Y,
            binding.ReferenceWidth, binding.ReferenceHeight, width, height);
        return first + $"\n{name} X: {saved.X}  Y: {saved.Y}   (기준 {binding.ReferenceWidth} × {binding.ReferenceHeight})";
    }
}
