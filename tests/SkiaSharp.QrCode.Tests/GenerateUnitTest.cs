using SkiaSharp.QrCode.Models;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace SkiaSharp.QrCode.Tests.Shared
{
    public class GenerateUnitTest
    {
        private readonly string content = "testtesttest";

        [Fact]
        public void SimpleGenerateUnitTest()
        {
            var actual = GenerateQrCode(content, null);
            var expect = File.ReadAllBytes($"samples/testtesttest_white.png");
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
                var actual2 = GenerateQrCode(content, item.color, true);
                var expect = File.ReadAllBytes($"samples/testtesttest_{item.name}.png");
                Assert.True(actual.SequenceEqual(expect));
                Assert.True(actual2.SequenceEqual(expect));
            }
        }

        [Fact]
        public void ColorInverseGenerateUnitTest()
        {
            // yellow, red
            foreach (var item in new (string name, SKColor codeColor, SKColor backgroundColor)[] {
                ("black", SKColors.White, SKColors.Black)
                })
            {
                var actual = GenerateQrCode(content, item.codeColor, item.backgroundColor);
                var actual2 = GenerateQrCode(content, item.codeColor, item.backgroundColor, true);
                var expect = File.ReadAllBytes($"samples/testtesttest_inverse_{item.name}.png");
                Assert.True(actual.SequenceEqual(expect));
                Assert.True(actual2.SequenceEqual(expect));
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
            var actual2 = GenerateQrCode(content, SKColor.Parse("000000"), icon, true);
            var expect = File.ReadAllBytes($"samples/testtesttest_icon.png");
            Assert.True(actual.SequenceEqual(expect));
            Assert.True(actual2.SequenceEqual(expect));
        }

        [Fact]
        public void IconInverseGenerateUnitTest()
        {
            // github icon
            var logo = File.ReadAllBytes("samples/icon.png");
            var icon = new IconData
            {
                Icon = SKBitmap.Decode(logo),
                IconSizePercent = 10,
            };
            var actual = GenerateQrCode(content, SKColors.White, SKColors.Black, icon);
            var actual2 = GenerateQrCode(content, SKColors.White, SKColors.Black, icon, true);
            var expect = File.ReadAllBytes($"samples/testtesttest_inverse_icon.png");
            Assert.True(actual.SequenceEqual(expect));
            Assert.True(actual2.SequenceEqual(expect));
        }

        [Fact]
        public void SKImageInfoBaseTest()
        {
            // no param
            var a = new SKImageInfo();
            Assert.Equal(0, a.Size.Height);
            Assert.Equal(0, a.Size.Width);
            Assert.Equal(0, a.BytesSize);
            Assert.Equal(SKAlphaType.Unknown, a.AlphaType);
            Assert.Equal(SKColorType.Unknown, a.ColorType);

            // size
            var b = new SKImageInfo(100, 100);
            Assert.Equal(100, b.Size.Height);
            Assert.Equal(100, b.Size.Width);
            Assert.Equal(40000, b.BytesSize);
            Assert.Equal(SKAlphaType.Premul, b.AlphaType);
            Assert.Equal(SKColorType.Bgra8888, b.ColorType);
        }

        [Fact]
        public void SKImageInfoColorTest()
        {
            foreach (SKColorType colorType in Enum.GetValues(typeof(SKColorType)))
            {
                var c = new SKImageInfo(100, 100, colorType);
                Assert.Equal(100, c.Size.Height);
                Assert.Equal(100, c.Size.Width);
                Assert.Equal(SKAlphaType.Premul, c.AlphaType);
                Assert.Equal(colorType, c.ColorType);

                switch (colorType)
                {
                    case SKColorType.Unknown:
                        Assert.Equal(0, c.BytesSize);
                        break;
                    case SKColorType.Alpha8:
                        Assert.Equal(10000, c.BytesSize);
                        break;
                    case SKColorType.Rgb565:
                        Assert.Equal(20000, c.BytesSize);
                        break;
                    case SKColorType.Argb4444:
                        Assert.Equal(20000, c.BytesSize);
                        break;
                    case SKColorType.Rgba8888:
                        Assert.Equal(40000, c.BytesSize);
                        break;
                    case SKColorType.Rgb888x:
                        Assert.Equal(40000, c.BytesSize);
                        break;
                    case SKColorType.Bgra8888:
                        Assert.Equal(40000, c.BytesSize);
                        break;
                    case SKColorType.Rgba1010102:
                        Assert.Equal(40000, c.BytesSize);
                        break;
                    case SKColorType.Rgb101010x:
                        Assert.Equal(40000, c.BytesSize);
                        break;
                    case SKColorType.Gray8:
                        Assert.Equal(10000, c.BytesSize);
                        break;
                    case SKColorType.RgbaF16:
                        Assert.Equal(80000, c.BytesSize);
                        break;
                    case SKColorType.RgbaF16Clamped:
                        Assert.Equal(80000, c.BytesSize);
                        break;
                    case SKColorType.RgbaF32:
                        Assert.Equal(160000, c.BytesSize);
                        break;
                    case SKColorType.Rg88:
                        Assert.Equal(20000, c.BytesSize);
                        break;
                    case SKColorType.AlphaF16:
                        Assert.Equal(20000, c.BytesSize);
                        break;
                    case SKColorType.RgF16:
                        Assert.Equal(40000, c.BytesSize);
                        break;
                    case SKColorType.Alpha16:
                        Assert.Equal(20000, c.BytesSize);
                        break;
                    case SKColorType.Rg1616:
                        Assert.Equal(40000, c.BytesSize);
                        break;
                    case SKColorType.Rgba16161616:
                        Assert.Equal(80000, c.BytesSize);
                        break;
                    case SKColorType.Bgra1010102:
                        Assert.Equal(40000, c.BytesSize);
                        break;
                    case SKColorType.Bgr101010x:
                        Assert.Equal(40000, c.BytesSize);
                        break;
                    default:
                        throw new NotImplementedException($"{nameof(colorType)} {colorType}");
                }
            }
        }

        [Fact]
        public void SKImageInfoAlphaTest()
        {
            foreach (SKColorType colorType in Enum.GetValues(typeof(SKColorType)))
            {
                foreach (SKAlphaType alphaType in Enum.GetValues(typeof(SKAlphaType)))
                {
                    var d = new SKImageInfo(100, 100, colorType, alphaType);
                    Assert.Equal(100, d.Size.Height);
                    Assert.Equal(100, d.Size.Width);
                    Assert.Equal(alphaType, d.AlphaType);
                    Assert.Equal(colorType, d.ColorType);

                    switch (colorType)
                    {
                        case SKColorType.Unknown:
                            Assert.Equal(0, d.BytesSize);
                            break;
                        case SKColorType.Alpha8:
                            Assert.Equal(10000, d.BytesSize);
                            break;
                        case SKColorType.Rgb565:
                            Assert.Equal(20000, d.BytesSize);
                            break;
                        case SKColorType.Argb4444:
                            Assert.Equal(20000, d.BytesSize);
                            break;
                        case SKColorType.Rgba8888:
                            Assert.Equal(40000, d.BytesSize);
                            break;
                        case SKColorType.Rgb888x:
                            Assert.Equal(40000, d.BytesSize);
                            break;
                        case SKColorType.Bgra8888:
                            Assert.Equal(40000, d.BytesSize);
                            break;
                        case SKColorType.Rgba1010102:
                            Assert.Equal(40000, d.BytesSize);
                            break;
                        case SKColorType.Rgb101010x:
                            Assert.Equal(40000, d.BytesSize);
                            break;
                        case SKColorType.Gray8:
                            Assert.Equal(10000, d.BytesSize);
                            break;
                        case SKColorType.RgbaF16:
                            Assert.Equal(80000, d.BytesSize);
                            break;
                        case SKColorType.RgbaF16Clamped:
                            Assert.Equal(80000, d.BytesSize);
                            break;
                        case SKColorType.RgbaF32:
                            Assert.Equal(160000, d.BytesSize);
                            break;
                        case SKColorType.Rg88:
                            Assert.Equal(20000, d.BytesSize);
                            break;
                        case SKColorType.AlphaF16:
                            Assert.Equal(20000, d.BytesSize);
                            break;
                        case SKColorType.RgF16:
                            Assert.Equal(40000, d.BytesSize);
                            break;
                        case SKColorType.Alpha16:
                            Assert.Equal(20000, d.BytesSize);
                            break;
                        case SKColorType.Rg1616:
                            Assert.Equal(40000, d.BytesSize);
                            break;
                        case SKColorType.Rgba16161616:
                            Assert.Equal(80000, d.BytesSize);
                            break;
                        case SKColorType.Bgra1010102:
                            Assert.Equal(40000, d.BytesSize);
                            break;
                        case SKColorType.Bgr101010x:
                            Assert.Equal(40000, d.BytesSize);
                            break;
                        default:
                            throw new NotImplementedException($"{nameof(colorType)} {colorType}");
                    }
                }
            }
        }

        private byte[] GenerateQrCode(string content, SKColor? codeColor, bool useRect = false)
        {
            // Generate QrCode
            using var generator = new QRCodeGenerator();
            var qr = generator.CreateQrCode(content, ECCLevel.L);

            // Render to canvas
            var info = new SKImageInfo(512, 512);
            using var surface = SKSurface.Create(info);
            var canvas = surface.Canvas;
            if (!codeColor.HasValue)
            {
                canvas.Render(qr, info.Width, info.Height);
            }
            else
            {
                if (useRect)
                {
                    canvas.Render(qr, new SKRect(0, 0, info.Width, info.Height), SKColor.Empty, codeColor.Value);
                }
                else
                {
                    canvas.Render(qr, info.Width, info.Height, SKColor.Empty, codeColor.Value);
                }
            }

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);

            return data.ToArray();
        }

        private byte[] GenerateQrCode(string content, SKColor codeColor, SKColor backgroundColor, bool useRect = false)
        {
            // Generate QrCode
            using var generator = new QRCodeGenerator();
            var qr = generator.CreateQrCode(content, ECCLevel.L);

            // Render to canvas
            var info = new SKImageInfo(512, 512);
            using var surface = SKSurface.Create(info);
            var canvas = surface.Canvas;
            if (useRect)
            {
                canvas.Render(qr, new SKRect(0, 0, info.Width, info.Height), SKColor.Empty, codeColor, backgroundColor);
            }
            else
            {
                canvas.Render(qr, info.Width, info.Height, SKColor.Empty, codeColor, backgroundColor);
            }

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);

            return data.ToArray();
        }

        private byte[] GenerateQrCode(string content, SKColor? codeColor, IconData iconData, bool useRect = false)
        {
            // Generate QrCode
            using var generator = new QRCodeGenerator();
            var qr = generator.CreateQrCode(content, ECCLevel.L);

            // Render to canvas
            var info = new SKImageInfo(512, 512);
            using var surface = SKSurface.Create(info);
            var canvas = surface.Canvas;
            if (useRect)
            {
                canvas.Render(qr, new SKRect(0, 0, info.Width, info.Height), SKColor.Empty, codeColor.Value, iconData);
            }
            else
            {
                canvas.Render(qr, info.Width, info.Height, SKColor.Empty, codeColor.Value, iconData);
            }

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);

            return data.ToArray();
        }

        private byte[] GenerateQrCode(string content, SKColor codeColor, SKColor backgroundColor, IconData iconData, bool useRect = false)
        {
            // Generate QrCode
            using var generator = new QRCodeGenerator();
            var qr = generator.CreateQrCode(content, ECCLevel.L);

            // Render to canvas
            var info = new SKImageInfo(512, 512);
            using var surface = SKSurface.Create(info);
            var canvas = surface.Canvas;
            if (useRect)
            {
                canvas.Render(qr, new SKRect(0, 0, info.Width, info.Height), SKColor.Empty, codeColor, backgroundColor, iconData);
            }
            else
            {
                canvas.Render(qr, info.Width, info.Height, SKColor.Empty, codeColor, backgroundColor, iconData);
            }

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);

            return data.ToArray();
        }
    }
}
