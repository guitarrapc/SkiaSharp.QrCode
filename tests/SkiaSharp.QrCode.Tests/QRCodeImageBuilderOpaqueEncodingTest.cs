using SkiaSharp.QrCode.Image;
using Xunit;

namespace SkiaSharp.QrCode.Tests;

/// <summary>
/// The builder renders to an opaque surface when the base layer (QR background
/// fill or cleared canvas) is opaque everywhere, so encoders emit alpha-less
/// output (RGB PNG). These tests pin the opacity decision and that the visible
/// pixels stay identical to the premul (always-alpha) extension render.
/// </summary>
public class QRCodeImageBuilderOpaqueEncodingTest
{
    private const string TestContent = "opaque-encoding-test";

    [Fact]
    public void DefaultColors_ProducesOpaqueImage()
    {
        var qr = QRCodeGenerator.CreateQrCode(TestContent, ECCLevel.M);

        using var image = new QRCodeImageBuilder(qr).WithSize(300, 300).ToImage();

        Assert.Equal(SKAlphaType.Opaque, image.AlphaType);
    }

    [Fact]
    public void TransparentClearWithPadding_KeepsAlpha()
    {
        // Pad area stays transparent, so the surface must keep its alpha channel.
        const int modulePixelSize = 4;
        var qr = QRCodeGenerator.CreateQrCode(TestContent, ECCLevel.M);
        var canvasSide = qr.Size * modulePixelSize + 40;

        using var image = new QRCodeImageBuilder(qr)
            .WithModulePixelSize(modulePixelSize)
            .WithSize(canvasSide, canvasSide)
            .ToImage();

        Assert.Equal(SKAlphaType.Premul, image.AlphaType);

        using var bitmap = SKBitmap.FromImage(image);
        Assert.Equal(0, bitmap.GetPixel(0, 0).Alpha);
    }

    [Fact]
    public void OpaqueClearWithPadding_ProducesOpaqueImage()
    {
        const int modulePixelSize = 4;
        var qr = QRCodeGenerator.CreateQrCode(TestContent, ECCLevel.M);
        var canvasSide = qr.Size * modulePixelSize + 40;

        using var image = new QRCodeImageBuilder(qr)
            .WithModulePixelSize(modulePixelSize)
            .WithSize(canvasSide, canvasSide)
            .WithColors(clearColor: SKColors.Red)
            .ToImage();

        Assert.Equal(SKAlphaType.Opaque, image.AlphaType);
    }

    [Fact]
    public void TranslucentBackground_WithTransparentClear_KeepsAlpha()
    {
        // Translucent background over a transparent canvas: pixels stay
        // translucent, so the alpha channel must be preserved.
        var qr = QRCodeGenerator.CreateQrCode(TestContent, ECCLevel.M);
        var translucentWhite = new SKColor(0xFF, 0xFF, 0xFF, 0x80);

        using var image = new QRCodeImageBuilder(qr)
            .WithSize(300, 300)
            .WithColors(backgroundColor: translucentWhite)
            .ToImage();

        Assert.Equal(SKAlphaType.Premul, image.AlphaType);

        using var bitmap = SKBitmap.FromImage(image);
        Assert.True(bitmap.GetPixel(0, 0).Alpha < byte.MaxValue);
    }

    [Fact]
    public void TranslucentBackground_OverOpaqueClear_ProducesOpaqueImage()
    {
        // The opaque cleared canvas is the base layer; the translucent background
        // blends over it and every pixel stays opaque.
        var qr = QRCodeGenerator.CreateQrCode(TestContent, ECCLevel.M);
        var translucentWhite = new SKColor(0xFF, 0xFF, 0xFF, 0x80);

        using var image = new QRCodeImageBuilder(qr)
            .WithSize(300, 300)
            .WithColors(backgroundColor: translucentWhite, clearColor: SKColors.Red)
            .ToImage();

        Assert.Equal(SKAlphaType.Opaque, image.AlphaType);
    }

    [Fact]
    public void OpaquePng_DecodesToSamePixelsAsPremulRender()
    {
        // The encoded PNG must stay visually identical to the premul render;
        // only the alpha channel representation may differ.
        var qr = QRCodeGenerator.CreateQrCode(TestContent, ECCLevel.M);
        const int size = 300;

        var png = QRCodeImageBuilder.GetPngBytes(qr, size);
        using var decoded = SKBitmap.Decode(png);

        using var reference = new SKBitmap(size, size);
        using (var canvas = new SKCanvas(reference))
        {
            canvas.Render(qr, SKRect.Create(0, 0, size, size));
        }

        Assert.Equal(size, decoded.Width);
        Assert.Equal(size, decoded.Height);
        for (var y = 0; y < size; y += 3)
        {
            for (var x = 0; x < size; x += 3)
            {
                Assert.Equal(reference.GetPixel(x, y), decoded.GetPixel(x, y));
            }
        }
    }
}
