using Xunit;

namespace SkiaSharp.QrCode.Tests;

/// <summary>
/// Image-level decode tests (Tier 1): rendered bitmaps, scaling, rotation,
/// mirroring and negative cases.
/// </summary>
public class QRCodeDecoderImageTest
{
    [Theory]
    [InlineData("Hello, World!", ECCLevel.M)]
    [InlineData("0123456789", ECCLevel.L)]
    [InlineData("HELLO WORLD $%*+-./:", ECCLevel.Q)]
    [InlineData("https://example.com/path?query=value", ECCLevel.H)]
    public void Decode_RenderedBitmap(string content, ECCLevel eccLevel)
    {
        using var bitmap = RenderQr(content, eccLevel, pixelsPerModule: 8);

        Assert.True(QRCodeDecoder.TryDecode(bitmap, out var decoded, out var info), $"status={info.Status}");
        Assert.Equal(content, decoded);
        Assert.Equal(eccLevel, info.EccLevel);
    }

    [Theory]
    [InlineData("こんにちは世界")]
    [InlineData("🎉 emoji 🎊")]
    public void Decode_RenderedBitmap_Utf8(string content)
    {
        using var bitmap = RenderQr(content, ECCLevel.M, pixelsPerModule: 8, eciMode: EciMode.Utf8);

        Assert.True(QRCodeDecoder.TryDecode(bitmap, out var decoded, out var info), $"status={info.Status}");
        Assert.Equal(content, decoded);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(11)]
    public void Decode_VariousModuleSizes(int pixelsPerModule)
    {
        var content = "module size test";
        using var bitmap = RenderQr(content, ECCLevel.M, pixelsPerModule);

        Assert.True(QRCodeDecoder.TryDecode(bitmap, out var decoded, out var info), $"ppm={pixelsPerModule}, status={info.Status}");
        Assert.Equal(content, decoded);
    }

    [Theory]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)] // 512px / 81 modules = 6.32 px/module — snapped one version low before the run-boundary fix
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)] // 5.51 px/module — same regression
    [InlineData(18)]
    [InlineData(20)]
    [InlineData(25)]
    public void Decode_FixedCanvas_NonIntegerModuleSize(int version)
    {
        // A fixed 512px canvas gives fractional pixels-per-module at most versions;
        // the pixel-quantized module-size measurement must not snap the dimension
        // estimate to a neighboring version (regression: v14/v17/v18/v20 read as
        // one version lower and failed with DataUncorrectable).
        var content = "https://github.com/guitarrapc/SkiaSharp.QrCode";
        var qr = QRCodeGenerator.CreateQrCode(content, ECCLevel.H, requestedVersion: version, quietZoneSize: 4);
        using var bitmap = new SKBitmap(new SKImageInfo(512, 512, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            QRCodeRenderer.Render(canvas, SKRect.Create(0, 0, 512, 512), qr, SKColors.Black, SKColors.White);
            canvas.Flush();
        }

        Assert.True(QRCodeDecoder.TryDecode(bitmap, out var decoded, out var info), $"version={version}, status={info.Status}, detected version={info.Version}");
        Assert.Equal(content, decoded);
        Assert.Equal(version, info.Version);
    }

    [Fact]
    public void Decode_LargerVersion()
    {
        var content = string.Join(";", Enumerable.Range(0, 40).Select(i => $"item{i:D4}"));
        using var bitmap = RenderQr(content, ECCLevel.M, pixelsPerModule: 6);

        Assert.True(QRCodeDecoder.TryDecode(bitmap, out var decoded, out var info), $"status={info.Status}");
        Assert.Equal(content, decoded);
        Assert.True(info.Version >= 10, $"expected a large version, got {info.Version}");
    }

    [Theory]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void Decode_RightAngleRotations(int degrees)
    {
        var content = $"rotation {degrees}";
        using var bitmap = RenderRotatedQr(content, ECCLevel.M, pixelsPerModule: 8, degrees);

        Assert.True(QRCodeDecoder.TryDecode(bitmap, out var decoded, out var info), $"degrees={degrees}, status={info.Status}");
        Assert.Equal(content, decoded);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(-7)]
    [InlineData(30)]
    [InlineData(45)]
    public void Decode_ArbitraryRotations(int degrees)
    {
        var content = $"tilt {degrees}";
        using var bitmap = RenderRotatedQr(content, ECCLevel.M, pixelsPerModule: 8, degrees);

        Assert.True(QRCodeDecoder.TryDecode(bitmap, out var decoded, out var info), $"degrees={degrees}, status={info.Status}");
        Assert.Equal(content, decoded);
    }

    [Fact]
    public void Decode_InvertedImage_LightOnDark()
    {
        // Reflectance-reversed rendering (dark-mode style): white modules on black
        var content = "inverted palette";
        var qr = QRCodeGenerator.CreateQrCode(content, ECCLevel.M);
        var sizePx = qr.Size * 8;
        using var bitmap = new SKBitmap(new SKImageInfo(sizePx, sizePx, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            QRCodeRenderer.Render(canvas, SKRect.Create(0, 0, sizePx, sizePx), qr, SKColors.White, SKColors.Black);
            canvas.Flush();
        }

        Assert.True(QRCodeDecoder.TryDecode(bitmap, out var decoded, out var info), $"status={info.Status}");
        Assert.Equal(content, decoded);
    }

    [Fact]
    public void Decode_MirroredImage()
    {
        var content = "mirrored capture";
        using var source = RenderQr(content, ECCLevel.M, pixelsPerModule: 8);
        using var mirrored = new SKBitmap(new SKImageInfo(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(mirrored))
        {
            canvas.Clear(SKColors.White);
            canvas.Scale(-1, 1, source.Width / 2f, 0);
            canvas.DrawBitmap(source, 0, 0, SKSamplingOptions.Default);
        }

        Assert.True(QRCodeDecoder.TryDecode(mirrored, out var decoded, out var info), $"status={info.Status}");
        Assert.Equal(content, decoded);
    }

    [Fact]
    public void Decode_LuminanceSpan()
    {
        var content = "luminance span";
        var qr = QRCodeGenerator.CreateQrCode(content, ECCLevel.M);
        var size = qr.Size;
        const int Scale = 6;
        var width = size * Scale;
        var luminance = new byte[width * width];
        luminance.AsSpan().Fill(255);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                if (!qr[y, x])
                    continue;
                for (var dy = 0; dy < Scale; dy++)
                {
                    luminance.AsSpan(((y * Scale + dy) * width) + x * Scale, Scale).Clear();
                }
            }
        }

        Assert.True(QRCodeDecoder.TryDecodeImage(luminance, width, width, out var decoded, out var info), $"status={info.Status}");
        Assert.Equal(content, decoded);

        // Char-span destination path
        Span<char> destination = stackalloc char[QRCodeDecoder.GetMaxDecodedLength(info.Version)];
        Assert.True(QRCodeDecoder.TryDecodeImage(luminance, width, width, destination, out var charsWritten, out _));
        Assert.Equal(content, destination.Slice(0, charsWritten).ToString());
    }

    [Fact]
    public void Decode_GrayBitmap_ColorTypeVariants()
    {
        var content = "gray8 bitmap";
        using var source = RenderQr(content, ECCLevel.M, pixelsPerModule: 8);
        using var gray = new SKBitmap(new SKImageInfo(source.Width, source.Height, SKColorType.Gray8));
        using (var canvas = new SKCanvas(gray))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(source, 0, 0, SKSamplingOptions.Default);
        }

        Assert.True(QRCodeDecoder.TryDecode(gray, out var decoded, out var info), $"status={info.Status}");
        Assert.Equal(content, decoded);
    }

    [Fact]
    public void Decode_BlankBitmap_ReturnsNotDetected()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(200, 200, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
        }

        Assert.False(QRCodeDecoder.TryDecode(bitmap, out var text, out var info));
        Assert.Equal(string.Empty, text);
        Assert.Equal(QRCodeDecodeStatus.NotDetected, info.Status);
    }

    [Fact]
    public void Decode_NoiseBitmap_ReturnsNotDetected()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(200, 200, SKColorType.Bgra8888, SKAlphaType.Premul));
        var random = new Random(42);
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var v = (byte)random.Next(256);
                bitmap.SetPixel(x, y, new SKColor(v, v, v));
            }
        }

        Assert.False(QRCodeDecoder.TryDecode(bitmap, out _, out var info));
        Assert.Equal(QRCodeDecodeStatus.NotDetected, info.Status);
    }

    [Fact]
    public void Decode_TinyBitmap_ReturnsNotDetected()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(10, 10, SKColorType.Bgra8888, SKAlphaType.Premul));

        Assert.False(QRCodeDecoder.TryDecode(bitmap, out _, out var info));
        Assert.Equal(QRCodeDecodeStatus.NotDetected, info.Status);
    }

    [Fact]
    public void Decode_NullBitmap_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => QRCodeDecoder.TryDecode((SKBitmap)null!, out _));
    }

    private static SKBitmap RenderQr(string content, ECCLevel eccLevel, int pixelsPerModule, EciMode eciMode = EciMode.Default)
    {
        var qr = QRCodeGenerator.CreateQrCode(content, eccLevel, eciMode: eciMode);
        var sizePx = qr.Size * pixelsPerModule;
        var bitmap = new SKBitmap(new SKImageInfo(sizePx, sizePx, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        QRCodeRenderer.Render(canvas, SKRect.Create(0, 0, sizePx, sizePx), qr, SKColors.Black, SKColors.White);
        canvas.Flush();
        return bitmap;
    }

    private static SKBitmap RenderRotatedQr(string content, ECCLevel eccLevel, int pixelsPerModule, float degrees)
    {
        var qr = QRCodeGenerator.CreateQrCode(content, eccLevel, eciMode: EciMode.Default);
        var qrPx = qr.Size * pixelsPerModule;
        // Room for the rotated square plus margin
        var canvasPx = (int)(qrPx * 1.5f) + 16;
        var bitmap = new SKBitmap(new SKImageInfo(canvasPx, canvasPx, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        canvas.Translate(canvasPx / 2f, canvasPx / 2f);
        canvas.RotateDegrees(degrees);
        canvas.Translate(-qrPx / 2f, -qrPx / 2f);
        QRCodeRenderer.Render(canvas, SKRect.Create(0, 0, qrPx, qrPx), qr, SKColors.Black, SKColors.White);
        canvas.Flush();
        return bitmap;
    }
}
