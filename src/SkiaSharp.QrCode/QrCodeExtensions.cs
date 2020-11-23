using SkiaSharp;
using SkiaSharp.QrCode.Models;

namespace SkiaSharp.QrCode
{
    public static class QrCodeExtensions
    {
        public static void Render(this SKCanvas canvas, QRCodeData data, int width, int hight)
        {
            canvas.Clear(SKColors.Transparent);

            using (var renderer = new QRCodeRenderer())
            {
                var area = SKRect.Create(0, 0, width, hight);
                renderer.Render(canvas, area, data, null);
            }
        }

        public static void Render(this SKCanvas canvas, QRCodeData data, int width, int hight, SKColor clearColor, SKColor codeColor)
        {
            canvas.Clear(clearColor);

            using (var renderer = new QRCodeRenderer())
            {
                var area = SKRect.Create(0, 0, width, hight);
                renderer.Render(canvas, area, data, codeColor);
            }
        }

        public static void Render(this SKCanvas canvas, QRCodeData data, int width, int hight, SKColor clearColor, SKColor codeColor, IconData iconData)
        {
            canvas.Clear(clearColor);

            using (var renderer = new QRCodeRenderer())
            {
                var area = SKRect.Create(0, 0, width, hight);
                renderer.Render(canvas, area, data, codeColor, iconData);
            }
        }

        public static void Render(this SKCanvas canvas, QRCodeData data, SKRect area, SKColor clearColor, SKColor codeColor)
        {
            canvas.Clear(clearColor);

            using (var renderer = new QRCodeRenderer())
            {
                renderer.Render(canvas, area, data, codeColor);
            }
        }

        public static void Render(this SKCanvas canvas, QRCodeData data, SKRect area, SKColor clearColor, SKColor codeColor, IconData iconData)
        {
            canvas.Clear(clearColor);

            using (var renderer = new QRCodeRenderer())
            {
                renderer.Render(canvas, area, data, codeColor, iconData);
            }
        }
    }
}
