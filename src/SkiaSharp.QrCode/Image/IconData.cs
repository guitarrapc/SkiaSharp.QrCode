namespace SkiaSharp.QrCode.Image;

public class IconData
{
    /// <summary>
    /// The icon shape to overlay on the QR code.
    /// </summary>
    public required IconShape Icon { get; set; }
    /// <summary>
    /// The size of the icon as a percentage of the QR code size (1-100).
    /// </summary>
    public int IconSizePercent { get; set; } = 10;
    /// <summary>
    /// The border width around the icon in pixels. Creates a background-colored padding around the icon.
    /// </summary>
    public int IconBorderWidth { get; set; } = 2;

    /// <summary>
    /// Create IconData from a bitmap image.
    /// </summary>
    /// <param name="image">The bitmap image to display as the icon.</param>
    /// <param name="iconSizePercent">The size of the icon as a percentage of the QR code size (1-100). Default is 10.</param>
    /// <param name="iconBorderWidth">The border width around the icon in pixels. Default is 2.</param>
    /// <returns>A new <see cref="IconData"/> instance configured with the specified image, size, and border width.</returns>
    public static IconData FromImage(SKBitmap image, int iconSizePercent = 10, int iconBorderWidth = 2)
    {
        return new IconData
        {
            Icon = new ImageIconShape(image),
            IconSizePercent = iconSizePercent,
            IconBorderWidth = iconBorderWidth
        };
    }
}
