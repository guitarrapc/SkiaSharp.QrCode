namespace SkiaSharp.QrCode.Image;

/// <summary>
/// Specifies the vertical alignment of text relative to the icon.
/// </summary>
public enum TextVerticalAlignment
{
    /// <summary>
    /// Text is positioned below the icon (default).
    /// </summary>
    Bottom = 0,

    /// <summary>
    /// Text is centered vertically with the icon.
    /// </summary>
    Center = 1,

    /// <summary>
    /// Text is positioned above the icon.
    /// </summary>
    Top = 2,
}

/// <summary>
/// Defines the shape and rendering behavior of a QR code center icon/logo.
/// </summary>
public abstract class IconShape
{
    /// <summary>
    /// Draw an icon bitmap at the specified location.
    /// </summary>
    /// <param name="canvas">The canvas to render on.</param>
    /// <param name="rect">The rectangular area for the icon.</param>
    /// <param name="borderRect">The rectangular area for the border (if border width > 0).</param>
    /// <param name="backgroundColor">Border background color</param>
    public abstract void Draw(SKCanvas canvas, SKRect rect, SKRect borderRect, SKColor backgroundColor);
}

/// <summary>
/// Icon shape that draws an image.
/// </summary>
public sealed class ImageIconShape : IconShape
{
    private readonly SKBitmap _image;

    public ImageIconShape(SKBitmap image)
    {
        _image = image ?? throw new ArgumentNullException(nameof(image));
    }

    /// <inheritdoc/>
    public override void Draw(SKCanvas canvas, SKRect rect, SKRect borderRect, SKColor backgroundColor)
    {
        // Draw border background color padding if specified
        if (borderRect != rect)
        {
            using var borderPaint = new SKPaint()
            {
                Color = backgroundColor,
                Style = SKPaintStyle.Fill,
            };
            canvas.DrawRect(borderRect, borderPaint);
        }

        // Draw an image
        canvas.DrawBitmap(_image, rect);
    }
}

/// <summary>
/// Icon shape that draws an image with text positioned relative to it.
/// </summary>
public sealed class ImageTextIconShape : IconShape
{
    private readonly SKBitmap _image;
    private readonly string _text;
    private readonly SKColor _textColor;
    private readonly SKFont _font;
    private readonly SKTextAlign _horizontalAlign;
    private readonly TextVerticalAlignment _verticalAlign;
    private readonly int _textPadding;

    /// <summary>
    /// Creates an icon shape that displays an image with text positioned relative to it.
    /// </summary>
    /// <param name="image">The bitmap image to display.</param>
    /// <param name="text">The text to display relative to the image.</param>
    /// <param name="textColor">The color of the text.</param>
    /// <param name="font">The font to use for the text.</param>
    /// <param name="horizontalAlign">Horizontal text alignment (Left, Center, or Right). Default is Center.</param>
    /// <param name="verticalAlign">Vertical text alignment relative to the image (Top, Center, or Bottom). Default is Bottom.</param>
    /// <param name="textPadding">Padding between the image and text in pixels. Default is 0.</param>
    public ImageTextIconShape(SKBitmap image, string text, SKColor textColor, SKFont font, SKTextAlign horizontalAlign = SKTextAlign.Center, TextVerticalAlignment verticalAlign = TextVerticalAlignment.Bottom, int textPadding = 0)
    {
        _image = image ?? throw new ArgumentNullException(nameof(image));
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _textColor = textColor;
        _font = font ?? throw new ArgumentNullException(nameof(font));
        _horizontalAlign = horizontalAlign;
        _verticalAlign = verticalAlign;
        _textPadding = textPadding;
    }

    /// <inheritdoc/>
    public override void Draw(SKCanvas canvas, SKRect rect, SKRect borderRect, SKColor backgroundColor)
    {
        // Draw border background color padding if specified
        if (borderRect != rect)
        {
            using var borderPaint = new SKPaint()
            {
                Color = backgroundColor,
                Style = SKPaintStyle.Fill,
            };
            canvas.DrawRect(borderRect, borderPaint);
        }

        // Draw an image
        canvas.DrawBitmap(_image, rect);

        // Draw text below the image
        if (!string.IsNullOrEmpty(_text))
        {
            using var textPaint = new SKPaint
            {
                Color = _textColor,
                IsAntialias = true,
            };

            // Calculate text X position based on horizontal alignment
            var textX = _horizontalAlign switch
            {
                SKTextAlign.Left => borderRect.Left,
                SKTextAlign.Center => borderRect.MidX,
                SKTextAlign.Right => borderRect.Right,
                _ => borderRect.Left,
            };

            // Calculate text Y position based on vertical alignment
            // CapHeight: Capital letter height, to better align text vertically
            var textY = _verticalAlign switch
            {
                TextVerticalAlignment.Top => rect.Top - _textPadding,
                TextVerticalAlignment.Center => rect.MidY + (_font.Metrics.CapHeight / 2),
                TextVerticalAlignment.Bottom => rect.Bottom + _font.Metrics.CapHeight + _textPadding,
                _ => rect.Bottom + _font.Metrics.CapHeight + _textPadding,
            };

            canvas.DrawText(_text, textX, textY, _horizontalAlign, _font, textPaint);
        }
    }
}
