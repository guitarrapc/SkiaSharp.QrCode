using SkiaSharp.QrCode.Image;

namespace SkiaSharp.QrCode;

public static class QRCodeRenderer
{
    /// <summary>
    /// Render the specified data into the given area of the target canvas.
    /// </summary>
    /// <param name="canvas">The canvas to render the QR code on.</param>
    /// <param name="area">The rectangular area where the QR code will be rendered.</param>
    /// <param name="data">The QR code data to render.</param>
    /// <param name="codeColor">The color of the QR code modules. If null, black is used.</param>
    /// <param name="backgroundColor">The background color. If null, white is used.</param>
    /// <param name="iconData">Optional icon data to overlay on the center of the QR code.</param>
    /// <exception cref="ArgumentNullException"></exception>
    public static void Render(SKCanvas canvas, SKRect area, QRCodeData data, SKColor? codeColor, SKColor? backgroundColor, IconData? iconData = null)
    {
        if (data is null)
            throw new ArgumentNullException(nameof(data));

        var bgColor = backgroundColor ?? SKColors.White;
        var fgColor = codeColor ?? SKColors.Black;

        // Draw the background at once
        using (var lightPaint = new SKPaint() { Color = bgColor, Style = SKPaintStyle.Fill })
        {
            canvas.DrawRect(area, lightPaint);
        }

        // Draw the modules
        using var darkPaint = new SKPaint() { Color = fgColor, Style = SKPaintStyle.Fill };
        var size = data.Size;
        var cellHeight = area.Height / size;
        var cellWidth = area.Width / size;
        for (int row = 0; row < size; row++)
        {
            for (int col = 0; col < size; col++)
            {
                if (data[row, col])
                {
                    var rect = SKRect.Create(area.Left + col * cellWidth, area.Top + row * cellHeight, cellWidth, cellHeight);
                    canvas.DrawRect(rect, darkPaint);
                }
            }
        }

        if (iconData?.Icon != null)
        {
            var iconSize = iconData.IconSizePercent / 100f;
            var iconWidth = area.Width * iconSize;
            var iconHeight = area.Height * iconSize;

            var x = area.Left + (area.Width - iconWidth) / 2;
            var y = area.Top + (area.Height - iconHeight) / 2;

            canvas.DrawBitmap(iconData.Icon, SKRect.Create(x, y, iconWidth, iconHeight));
        }
    }
}
