using SkiaSharp.QrCode.Models;
using System;
using System.Linq;

namespace SkiaSharp.QrCode
{
    public class QRCodeRenderer : IDisposable
    {
        /// <summary>
        /// Render the specified data into the given area of the target canvas.
        /// </summary>
        /// <param name="canvas">The canvas.</param>
        /// <param name="area">The area.</param>
        /// <param name="data">The data.</param>
        /// <param name="qrColor">The color.</param>
        /// <param name="iconData">The icon settings</param>
        public void Render(SKCanvas canvas, SKRect area, QRCodeData data, SKColor? qrColor, SKColor? backgroundColor, IconData iconData = null)
        {
            if (data != null)
            {
                using (var lightPaint = new SKPaint() { Color = (backgroundColor.HasValue ? backgroundColor.Value : SKColors.White), Style = SKPaintStyle.StrokeAndFill })
                using (var darkPaint = new SKPaint() { Color = (qrColor.HasValue ? qrColor.Value : SKColors.Black), Style = SKPaintStyle.StrokeAndFill })
                {

                    var rows = data.ModuleMatrix.Count;
                    var columns = data.ModuleMatrix.Select(x => x.Length).Max();
                    var cellHeight = area.Height / rows;
                    var cellWidth = area.Width / columns;

                    for (int y = 0; y < rows; y++)
                    {
                        var row = data.ModuleMatrix.ElementAt(y);
                        for (int x = 0; x < row.Length; x++)
                            canvas.DrawRect(SKRect.Create(area.Left + x * cellWidth, area.Top + y * cellHeight, cellWidth, cellHeight), (row[x] ? darkPaint : lightPaint));
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
        }

        /// <summary>
        /// Releases all resource used by the <see cref="T:SkiaSharp.QRCodeGeneration.QRCodeRenderer"/> object.
        /// </summary>
        /// <remarks>Call <see cref="Dispose"/> when you are finished using the
        /// <see cref="T:SkiaSharp.QRCodeGeneration.QRCodeRenderer"/>. The <see cref="Dispose"/> method leaves the
        /// <see cref="T:SkiaSharp.QRCodeGeneration.QRCodeRenderer"/> in an unusable state. After calling
        /// <see cref="Dispose"/>, you must release all references to the
        /// <see cref="T:SkiaSharp.QRCodeGeneration.QRCodeRenderer"/> so the garbage collector can reclaim the memory
        /// that the <see cref="T:SkiaSharp.QRCodeGeneration.QRCodeRenderer"/> was occupying.</remarks>
        public void Dispose()
        { }
    }
}
