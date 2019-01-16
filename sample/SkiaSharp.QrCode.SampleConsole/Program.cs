using SkiaSharp;
using System;
using System.IO;
using SkiaSharp.QrCode;

namespace SkiaQrCodeSampleConsole
{
    class Program
    {
        static void Main(string[] args)
        {
            var content = "testtesttest";
            using (var generator = new QRCodeGenerator())
            {
                // Generate QrCode
                var qr = generator.CreateQrCode(content, ECCLevel.L);

                // Render to canvas
                var info = new SKImageInfo(512, 512);
                using (var surface = SKSurface.Create(info))
                {
                    var canvas = surface.Canvas;
                    canvas.Render(qr, info.Width, info.Height);

                    // Output to Stream -> File
                    using (var image = surface.Snapshot())
                    using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
                    using (var stream = File.OpenWrite(@"output/hoge.png"))
                    {
                        data.SaveTo(stream);
                    }
                }
            }
        }
    }
}