using SkiaSharp;
using System;
using System.IO;
using SkiaSharp.QrCode;
using SkiaSharp.QrCode.Models;

namespace SkiaQrCodeSampleConsole
{
    class Program
    {
        static void Main(string[] args)
        {
            Directory.CreateDirectory("output");

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

                    // gen color
                    // yellow https://rgb.to/yellow
                    //canvas.Render(qr, info.Width, info.Height, SKColor.Empty, SKColor.FromHsl(60,100,50));
                    // red https://rgb.to/red
                    //canvas.Render(qr, info.Width, info.Height, SKColor.Empty, SKColor.FromHsl(0, 100, 50));

                    // gen icon
                    //var logo = File.ReadAllBytes("samples/test.png");
                    //var icon = new IconData
                    //{
                    //    Icon = SKBitmap.Decode(logo),
                    //    IconSizePercent = 10,
                    //};
                    //canvas.Render(qr, info.Width, info.Height, SKColor.Empty, SKColor.Parse("000000"), icon);

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
