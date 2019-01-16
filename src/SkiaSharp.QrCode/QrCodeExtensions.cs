using SkiaSharp;

namespace SkiaQrCode
{
    public static class QrCodeExtensions
    {
        public static void Render(this SKCanvas canvas, QRCodeData data, int width, int hight)
        {
            canvas.Clear(SKColors.Transparent);

            using (var renderer = new QRCodeRenderer())
            {
                var area = SKRect.Create(0, 0, width, hight);
                renderer.Render(canvas, area, data);
            }
        }

        public static void Render(this SKCanvas canvas, QRCodeData data, int width, int hight, SKColor clearColor)
        {
            canvas.Clear(clearColor);

            using (var renderer = new QRCodeRenderer())
            {
                var area = SKRect.Create(0, 0, width, hight);
                renderer.Render(canvas, area, data);
            }
        }

        public static void Render(this SKCanvas canvas, QRCodeData data, SKRect area, SKColor clearColor)
        {
            canvas.Clear(clearColor);

            using (var renderer = new QRCodeRenderer())
            {
                renderer.Render(canvas, area, data);
            }
        }
    }
}
