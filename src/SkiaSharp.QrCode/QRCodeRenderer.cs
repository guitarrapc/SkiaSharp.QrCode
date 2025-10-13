using SkiaSharp.QrCode.Models;

namespace SkiaSharp.QrCode;

public static class QRCodeRenderer
{
    /// <summary>
    /// Render the specified data into the given area of the target canvas.
    /// </summary>
    /// <param name="canvas">The canvas.</param>
    /// <param name="area">The area.</param>
    /// <param name="data">The data.</param>
    /// <param name="qrColor">The color.</param>
    /// <param name="iconData">The icon settings</param>
    public static void Render(SKCanvas canvas, SKRect area, QRCodeData data, SKColor? qrColor, SKColor? backgroundColor, IconData? iconData = null)
    {
        if (data is null)
            return;

        using var lightPaint = new SKPaint() { Color = (backgroundColor.HasValue ? backgroundColor.Value : SKColors.White), Style = SKPaintStyle.StrokeAndFill };
        using var darkPaint = new SKPaint() { Color = (qrColor.HasValue ? qrColor.Value : SKColors.Black), Style = SKPaintStyle.StrokeAndFill };
        var size = data.Size;
        var cellHeight = area.Height / size;
        var cellWidth = area.Width / size;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                var rect = SKRect.Create(area.Left + x * cellWidth, area.Top + y * cellHeight, cellWidth, cellHeight);
                var paint = data[y, x] ? darkPaint : lightPaint;
                canvas.DrawRect(rect, paint);
            }
        }

        if (iconData?.Icon != null)
        {
            var iconWidth = (area.Width / 100) * iconData.IconSizePercent;
            var iconHeight = (area.Height / 100) * iconData.IconSizePercent;

            var x = (area.Width / 2) - (iconWidth / 2);
            var y = (area.Height / 2) - (iconHeight / 2);

            canvas.DrawBitmap(iconData.Icon, SKRect.Create(x, y, iconWidth, iconHeight));
        }
    }
}
