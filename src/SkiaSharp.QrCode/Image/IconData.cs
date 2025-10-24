namespace SkiaSharp.QrCode.Image;

public class IconData
{
    /// <summary>
    /// The icon bitmap to overlay on the QR code.
    /// </summary>
    public SKBitmap? Icon { get; set; }
    /// <summary>
    /// The size of the icon as a percentage of the QR code size (1-100).
    /// </summary>
    public int IconSizePercent { get; set; } = 10;
    /// <summary>
    /// The border width around the icon in pixels. Creates a background-colored padding around the icon.
    /// </summary>
    public int IconBorderWidth { get; set; } = 2;
}
