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
    /// <param name="moduleShape">The shape to use for drawing modules. If null, rectangles are used.</param>
    /// <param name="moduleSizePercent">The size of each module as a percentage of the cell size (0.0 to 1.0). Default is 1.0 (no gap).</param>
    /// <exception cref="ArgumentNullException"></exception>
    public static void Render(SKCanvas canvas, SKRect area, QRCodeData data, SKColor? codeColor, SKColor? backgroundColor, IconData? iconData = null, ModuleShape? moduleShape = null, float moduleSizePercent = 1.0f)
    {
        if (data is null)
            throw new ArgumentNullException(nameof(data));
        if (moduleSizePercent is < 0f or > 1.0f)
            throw new ArgumentOutOfRangeException(nameof(moduleSizePercent), "Module size percent must be between 0.5 and 1.0.");

        var bgColor = backgroundColor ?? SKColors.White;
        var fgColor = codeColor ?? SKColors.Black;
        var shape = moduleShape ?? RectangleModuleShape.Default;

        // Draw the background at once
        using (var lightPaint = new SKPaint() { Color = bgColor, Style = SKPaintStyle.Fill })
        {
            canvas.DrawRect(area, lightPaint);
        }

        // Draw the modules
        using var darkPaint = new SKPaint() { Color = fgColor, Style = SKPaintStyle.Fill, IsAntialias = true };
        var size = data.Size;
        var cellHeight = area.Height / size;
        var cellWidth = area.Width / size;

        // Calculate module size with gaps
        var moduleWidth = cellWidth * moduleSizePercent;
        var moduleHeight = cellHeight * moduleSizePercent;
        var xOffset = (cellWidth - moduleWidth) / 2;
        var yOffset = (cellHeight - moduleHeight) / 2;

        for (int row = 0; row < size; row++)
        {
            for (int col = 0; col < size; col++)
            {
                if (data[row, col])
                {
                    var x = area.Left + col * cellWidth + xOffset;
                    var y = area.Top + row * cellHeight + yOffset;
                    var rect = SKRect.Create(x, y, moduleWidth, moduleHeight);
                    shape.Draw(canvas, rect, darkPaint);
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
