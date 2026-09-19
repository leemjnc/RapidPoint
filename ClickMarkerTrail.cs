namespace RapidPoint;

internal sealed class ClickMarkerTrail
{
    internal const int LifetimeMs = 800;
    internal const int MaximumMarkers = 8;
    internal readonly record struct Marker(int X, int Y, int Width, int Height, long CreatedAt)
    {
        internal Point AtSize(Size size)
        {
            var scaled = MacroEngine.ScaleMappedPoint(X, Y, size.Width, size.Height, Width, Height);
            return new Point(scaled.X, scaled.Y);
        }
    }

    private readonly List<Marker> _markers = new(MaximumMarkers);
    internal IReadOnlyList<Marker> Markers => _markers;

    internal void Add(NativeMethods.Point point, int width, int height, long now)
    {
        if (width <= 0 || height <= 0 || point.X < 0 || point.Y < 0 || point.X >= width || point.Y >= height) return;
        Expire(now);
        if (_markers.Count == MaximumMarkers) _markers.RemoveAt(0);
        _markers.Add(new Marker(point.X, point.Y, width, height, now));
    }

    internal void Expire(long now) => _markers.RemoveAll(marker => now - marker.CreatedAt >= LifetimeMs);
    internal void Clear() => _markers.Clear();
}
