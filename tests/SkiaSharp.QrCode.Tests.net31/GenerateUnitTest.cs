using SkiaSharp.QrCode;
using SkiaSharp.QrCode.Models;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace SkiaSharp.QrCode.Tests.Shared
{
    public class GenerateUnitTest
    {
        private readonly string content = "testtesttest";
#if NET5_0
        private readonly string netcore = "net5.0";
#else
        private readonly string netcore = "net3.1";
#endif

        [Fact]
        public void SimpleGenerateUnitTest()
        {
            var actual = GenerateQrCode(content, null);
            var expect = File.ReadAllBytes($"samples/{netcore}/testtesttest_white.png");
            Assert.True(actual.SequenceEqual(expect));
        }

        [Fact]
        public void ColorGenerateUnitTest()
        {
            // yellow, red
            foreach (var item in new (string name, SKColor color)[] {
                ("yellow", SKColor.FromHsl(60, 100, 50)),
                ("red", SKColor.FromHsl(0, 100, 50))
                })
            {
                var actual = GenerateQrCode(content, item.color);
                var expect = File.ReadAllBytes($"samples/{netcore}/testtesttest_{item.name}.png");
                Assert.True(actual.SequenceEqual(expect));
            }
        }

        [Fact]
        public void IconGenerateUnitTest()
        {
            // github icon
            var logo = File.ReadAllBytes("samples/icon.png");
            var icon = new IconData
            {
                Icon = SKBitmap.Decode(logo),
                IconSizePercent = 10,
            };
            var actual = GenerateQrCode(content, SKColor.Parse("000000"), icon);
            var expect = File.ReadAllBytes($"samples/{netcore}/testtesttest_icon.png");
            Assert.True(actual.SequenceEqual(expect));
        }

        private byte[] GenerateQrCode(string content, SKColor? color)
        {
            // Generate QrCode
            using var generator = new QRCodeGenerator();
            var qr = generator.CreateQrCode(content, ECCLevel.L);

            // Render to canvas
            var info = new SKImageInfo(512, 512);
            using var surface = SKSurface.Create(info);
            var canvas = surface.Canvas;
            if (color == null)
            {
                canvas.Render(qr, info.Width, info.Height);
            }
            else
            {
                canvas.Render(qr, info.Width, info.Height, SKColor.Empty, color.Value);
            }

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);

            return data.ToArray();
        }

        private byte[] GenerateQrCode(string content, SKColor? color, IconData iconData)
        {
            // Generate QrCode
            using var generator = new QRCodeGenerator();
            var qr = generator.CreateQrCode(content, ECCLevel.L);

            // Render to canvas
            var info = new SKImageInfo(512, 512);
            using var surface = SKSurface.Create(info);
            var canvas = surface.Canvas;
            canvas.Render(qr, info.Width, info.Height, SKColor.Empty, color.Value, iconData);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);

            return data.ToArray();
        }
    }
}
