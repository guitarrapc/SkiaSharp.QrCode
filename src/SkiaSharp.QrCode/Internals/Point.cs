namespace SkiaSharp.QrCode.Internals;

/// <summary>
/// Simple 2D point structure for alignment pattern coordinates.
/// </summary>
internal readonly record struct Point
{
    public int X { get; }
    public int Y { get; }
    public Point(int x, int y)
    {
        this.X = x;
        this.Y = y;
    }
}
