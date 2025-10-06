namespace SkiaSharp.QrCode.Internals;

/// <summary>
/// Rectangle structure for tracking blocked module regions.
/// Used during QR code matrix generation to avoid overwriting patterns.
/// </summary>
internal readonly record struct Rectangle
{
    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }

    public Rectangle(int x, int y, int w, int h)
    {
        X = x;
        Y = y;
        Width = w;
        Height = h;
    }
}
