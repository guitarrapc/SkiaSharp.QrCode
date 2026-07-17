using SkiaSharp.QrCode.Image;
using System.Buffers;

namespace SkiaSharp.QrCode.Tests;

public class MicroQrCodeImageBuilderUnitTest
{
    private const string TestContent = "MICRO QR TEST";

    public static IEnumerable<(MicroQrVersion version, MicroQrEccLevel eccLevel, string content)> AllVersionEccCombinations()
    {
        yield return (MicroQrVersion.M1, MicroQrEccLevel.ErrorDetectionOnly, "123");
        yield return (MicroQrVersion.M2, MicroQrEccLevel.L, "12345");
        yield return (MicroQrVersion.M2, MicroQrEccLevel.M, "12345");
        yield return (MicroQrVersion.M3, MicroQrEccLevel.L, "1234567");
        yield return (MicroQrVersion.M3, MicroQrEccLevel.M, "1234567");
        yield return (MicroQrVersion.M4, MicroQrEccLevel.L, "123456789");
        yield return (MicroQrVersion.M4, MicroQrEccLevel.M, "123456789");
        yield return (MicroQrVersion.M4, MicroQrEccLevel.Q, "123456789");
    }

    #region Constructor

    [Test]
    public async Task Constructor_ValidContent_Success()
    {
        var builder = new MicroQrCodeImageBuilder(TestContent);
        await Assert.That(builder).IsNotNull();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    public async Task Constructor_InvalidContent_ThrowsArgumentException(string? content)
    {
        Assert.Throws<ArgumentException>(() => new MicroQrCodeImageBuilder(content!));
    }

    [Test]
    public async Task Constructor_NullData_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new MicroQrCodeImageBuilder((MicroQrCodeData)null!));
    }

    #endregion

    #region Module pixel parity (rendered image matches the module matrix)

    // The strongest rendering correctness check: render at an exact integer module
    // pixel size and verify EVERY module (quiet zone included) maps to the expected
    // color at its pixel center, for every version × ECC combination.
    [Test]
    [MethodDataSource(nameof(AllVersionEccCombinations))]
    public async Task ToBitmap_ModulePixelSize_EveryModuleMatchesMatrix(MicroQrVersion version, MicroQrEccLevel eccLevel, string content)
    {
        const int modulePixelSize = 4;
        var data = MicroQrCodeGenerator.CreateMicroQrCode(content, eccLevel, version);

        using var bitmap = new MicroQrCodeImageBuilder(data)
            .WithModulePixelSize(modulePixelSize)
            .ToBitmap();

        await Assert.That(bitmap.Width).IsEqualTo(data.Size * modulePixelSize);
        await Assert.That(bitmap.Height).IsEqualTo(data.Size * modulePixelSize);

        for (var row = 0; row < data.Size; row++)
        {
            for (var col = 0; col < data.Size; col++)
            {
                var expected = data[row, col] ? SKColors.Black : SKColors.White;
                var actual = bitmap.GetPixel(col * modulePixelSize + modulePixelSize / 2, row * modulePixelSize + modulePixelSize / 2);
                if (actual != expected)
                {
                    Assert.Fail($"Module ({row},{col}) expected {expected} but was {actual} (version={version}, ecc={eccLevel})");
                }
            }
        }
    }

    [Test]
    public async Task ToBitmap_CustomColors_UsesConfiguredColors()
    {
        const int modulePixelSize = 4;
        var data = MicroQrCodeGenerator.CreateMicroQrCode("12345", MicroQrEccLevel.L);

        using var bitmap = new MicroQrCodeImageBuilder(data)
            .WithModulePixelSize(modulePixelSize)
            .WithColors(codeColor: SKColors.DarkBlue, backgroundColor: SKColors.Beige)
            .ToBitmap();

        for (var row = 0; row < data.Size; row++)
        {
            for (var col = 0; col < data.Size; col++)
            {
                var expected = data[row, col] ? SKColors.DarkBlue : SKColors.Beige;
                var actual = bitmap.GetPixel(col * modulePixelSize + modulePixelSize / 2, row * modulePixelSize + modulePixelSize / 2);
                if (actual != expected)
                {
                    Assert.Fail($"Module ({row},{col}) expected {expected} but was {actual}");
                }
            }
        }
    }

    [Test]
    public async Task ToBitmap_CircleModuleShape_DarkModuleCentersAreDark()
    {
        const int modulePixelSize = 8;
        var data = MicroQrCodeGenerator.CreateMicroQrCode("12345", MicroQrEccLevel.L);

        using var bitmap = new MicroQrCodeImageBuilder(data)
            .WithModulePixelSize(modulePixelSize)
            .WithModuleShape(CircleModuleShape.Default)
            .ToBitmap();

        for (var row = 0; row < data.Size; row++)
        {
            for (var col = 0; col < data.Size; col++)
            {
                var expected = data[row, col] ? SKColors.Black : SKColors.White;
                var actual = bitmap.GetPixel(col * modulePixelSize + modulePixelSize / 2, row * modulePixelSize + modulePixelSize / 2);
                if (actual != expected)
                {
                    Assert.Fail($"Module ({row},{col}) center expected {expected} but was {actual}");
                }
            }
        }
    }

    #endregion

    #region Quiet zone defaults (Micro QR requires 2 modules, not Standard QR's 4)

    [Test]
    public async Task ContentBuilder_DefaultQuietZone_IsTwoModules()
    {
        const int modulePixelSize = 4;
        // Same content and ECC as the builder's internal generation.
        var expectedData = MicroQrCodeGenerator.CreateMicroQrCode(TestContent, MicroQrEccLevel.M);
        var coreSize = expectedData.Size - 2 * 2; // default quiet zone is 2 modules per side

        using var bitmap = new MicroQrCodeImageBuilder(TestContent)
            .WithModulePixelSize(modulePixelSize)
            .ToBitmap();

        await Assert.That(bitmap.Width).IsEqualTo((coreSize + 4) * modulePixelSize);
    }

    [Test]
    public async Task ContentBuilder_WithQuietZone_OverridesDefault()
    {
        const int modulePixelSize = 4;
        var zeroQuietData = MicroQrCodeGenerator.CreateMicroQrCode(TestContent, MicroQrEccLevel.M, quietZoneSize: 0);

        using var bitmap = new MicroQrCodeImageBuilder(TestContent)
            .WithQuietZone(0)
            .WithModulePixelSize(modulePixelSize)
            .ToBitmap();

        await Assert.That(bitmap.Width).IsEqualTo(zeroQuietData.Size * modulePixelSize);
    }

    [Test]
    [Arguments(-1)]
    [Arguments(11)]
    public async Task WithQuietZone_InvalidSize_ThrowsArgumentOutOfRangeException(int size)
    {
        var builder = new MicroQrCodeImageBuilder(TestContent);
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithQuietZone(size));
    }

    #endregion

    #region Version / ECC configuration

    [Test]
    public async Task WithVersion_FixedVersion_MatchesDataRendering()
    {
        using var expectedBitmap = new MicroQrCodeImageBuilder(MicroQrCodeGenerator.CreateMicroQrCode("12345", MicroQrEccLevel.M, MicroQrVersion.M4))
            .WithSize(256, 256)
            .ToBitmap();

        using var actualBitmap = new MicroQrCodeImageBuilder("12345")
            .WithErrorCorrection(MicroQrEccLevel.M)
            .WithVersion(MicroQrVersion.M4)
            .WithSize(256, 256)
            .ToBitmap();

        await Assert.That(BitmapsAreEqual(expectedBitmap, actualBitmap)).IsTrue();
    }

    [Test]
    public async Task WithVersion_DataBuilder_ThrowsInvalidOperationException()
    {
        var data = MicroQrCodeGenerator.CreateMicroQrCode("12345", MicroQrEccLevel.L);
        var builder = new MicroQrCodeImageBuilder(data);

        Assert.Throws<InvalidOperationException>(() => builder.WithVersion(MicroQrVersion.M4));
    }

    [Test]
    public async Task WithErrorCorrection_DataBuilder_ThrowsInvalidOperationException()
    {
        var data = MicroQrCodeGenerator.CreateMicroQrCode("12345", MicroQrEccLevel.L);
        var builder = new MicroQrCodeImageBuilder(data);

        Assert.Throws<InvalidOperationException>(() => builder.WithErrorCorrection(MicroQrEccLevel.M));
    }

    [Test]
    public async Task ContentBuilder_IllegalVersionEccCombination_ThrowsOnRender()
    {
        // M1 supports error detection only; requesting ECC L on M1 must fail.
        var builder = new MicroQrCodeImageBuilder("123")
            .WithErrorCorrection(MicroQrEccLevel.L)
            .WithVersion(MicroQrVersion.M1);

        Assert.Throws<ArgumentException>(() => builder.ToByteArray());
    }

    #endregion

    #region Layout (size, module pixel size, padding)

    [Test]
    public async Task WithSize_CustomSize_GeneratesCorrectSize()
    {
        using var bitmap = new MicroQrCodeImageBuilder(TestContent)
            .WithSize(300, 400)
            .ToBitmap();

        await Assert.That(bitmap.Width).IsEqualTo(300);
        await Assert.That(bitmap.Height).IsEqualTo(400);
    }

    [Test]
    [Arguments(0, 100)]
    [Arguments(-1, 100)]
    [Arguments(100, 0)]
    [Arguments(100, -1)]
    public async Task WithSize_InvalidSize_ThrowsArgumentOutOfRangeException(int width, int height)
    {
        var builder = new MicroQrCodeImageBuilder(TestContent);
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithSize(width, height));
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    public async Task WithModulePixelSize_InvalidSize_ThrowsArgumentOutOfRangeException(int modulePixelSize)
    {
        var builder = new MicroQrCodeImageBuilder(TestContent);
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithModulePixelSize(modulePixelSize));
    }

    [Test]
    public async Task WithModulePixelSize_AndLargerCanvas_PadsAndCentersContent()
    {
        const int modulePixelSize = 6;
        var data = MicroQrCodeGenerator.CreateMicroQrCode(TestContent, MicroQrEccLevel.M);
        var contentSide = data.Size * modulePixelSize;
        const int canvasWidth = 200;
        const int canvasHeight = 240;
        await Assert.That(canvasWidth >= contentSide).IsTrue();
        await Assert.That(canvasHeight >= contentSide).IsTrue();

        using var bitmap = new MicroQrCodeImageBuilder(data)
            .WithModulePixelSize(modulePixelSize)
            .WithSize(canvasWidth, canvasHeight)
            .WithColors(codeColor: SKColors.Black, backgroundColor: SKColors.White, clearColor: SKColors.Transparent)
            .ToBitmap();

        await Assert.That(bitmap.Width).IsEqualTo(canvasWidth);
        await Assert.That(bitmap.Height).IsEqualTo(canvasHeight);

        var expectedLeft = (canvasWidth - contentSide) / 2;
        var expectedTop = (canvasHeight - contentSide) / 2;

        // Padding outside content keeps clearColor (transparent).
        await Assert.That(bitmap.GetPixel(0, 0).Alpha).IsEqualTo((byte)0);
        await Assert.That(bitmap.GetPixel(canvasWidth - 1, canvasHeight - 1).Alpha).IsEqualTo((byte)0);

        // Content area corners are QR background (quiet zone).
        await Assert.That(bitmap.GetPixel(expectedLeft, expectedTop)).IsEqualTo(SKColors.White);
        await Assert.That(bitmap.GetPixel(expectedLeft + contentSide - 1, expectedTop + contentSide - 1)).IsEqualTo(SKColors.White);
    }

    [Test]
    public async Task WithModulePixelSize_AndTooSmallCanvas_ThrowsInvalidOperationException()
    {
        const int modulePixelSize = 10;
        var data = MicroQrCodeGenerator.CreateMicroQrCode(TestContent, MicroQrEccLevel.M);
        var contentSide = data.Size * modulePixelSize;

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new MicroQrCodeImageBuilder(data)
                .WithModulePixelSize(modulePixelSize)
                .WithSize(contentSide - 1, contentSide)
                .ToBitmap());

        await Assert.That(ex.Message).Contains("smaller than QR content size");
    }

    #endregion

    #region Static helpers

    [Test]
    public async Task GetPngBytes_DefaultParameters_ReturnsValidPngBytes()
    {
        var bytes = MicroQrCodeImageBuilder.GetPngBytes(TestContent);

        await Assert.That(bytes).IsNotNull();
        await Assert.That(bytes).IsNotEmpty();
        // PNG signature: 89 50 4E 47
        await Assert.That(bytes[0]).IsEqualTo((byte)0x89);
        await Assert.That(bytes[1]).IsEqualTo((byte)0x50);
        await Assert.That(bytes[2]).IsEqualTo((byte)0x4E);
        await Assert.That(bytes[3]).IsEqualTo((byte)0x47);
    }

    [Test]
    public async Task GetPngBytes_FromData_ReturnsValidPngBytes()
    {
        var data = MicroQrCodeGenerator.CreateMicroQrCode("12345", MicroQrEccLevel.L);
        var bytes = MicroQrCodeImageBuilder.GetPngBytes(data);

        await Assert.That(bytes).IsNotNull();
        await Assert.That(bytes[0]).IsEqualTo((byte)0x89);
    }

    [Test]
    [Arguments(SKEncodedImageFormat.Png)]
    [Arguments(SKEncodedImageFormat.Jpeg)]
    [Arguments(SKEncodedImageFormat.Webp)]
    public async Task GetImageBytes_DifferentFormats_ReturnsValidBytes(SKEncodedImageFormat format)
    {
        var bytes = MicroQrCodeImageBuilder.GetImageBytes(TestContent, format);

        await Assert.That(bytes).IsNotNull();
        await Assert.That(bytes).IsNotEmpty();
    }

    [Test]
    public async Task SavePng_ValidStream_WritesData()
    {
        using var stream = new MemoryStream();
        MicroQrCodeImageBuilder.SavePng(TestContent, stream);

        await Assert.That(stream.Length > 0).IsTrue();
        var bytes = stream.ToArray();
        await Assert.That(bytes[0]).IsEqualTo((byte)0x89);
        await Assert.That(bytes[1]).IsEqualTo((byte)0x50);
    }

    [Test]
    public async Task WritePng_ValidBufferWriter_WritesData()
    {
        var writer = new ArrayBufferWriter<byte>();
        MicroQrCodeImageBuilder.WritePng(TestContent, writer);

        var writtenBytes = writer.WrittenSpan.ToArray();
        await Assert.That(writtenBytes.Length > 0).IsTrue();
        await Assert.That(writtenBytes[0]).IsEqualTo((byte)0x89);
    }

    #endregion

    #region SVG

    [Test]
    public async Task GetSvgString_DefaultSettings_ContainsViewBoxAndCrispEdges()
    {
        var svg = MicroQrCodeImageBuilder.GetSvgString(TestContent);

        await Assert.That(svg).Contains("<svg");
        await Assert.That(svg).Contains("viewBox=\"0 0 512 512\"");
        // Rectangle modules at 100% size render with crispEdges (no antialias seams).
        await Assert.That(svg).Contains("shape-rendering=\"crispEdges\"");
    }

    [Test]
    public async Task GetSvgBytes_ReturnsUtf8SvgDocument()
    {
        var bytes = MicroQrCodeImageBuilder.GetSvgBytes(TestContent);
        var svg = System.Text.Encoding.UTF8.GetString(bytes);

        await Assert.That(svg).Contains("<svg");
    }

    [Test]
    public async Task SaveToSvg_CustomShape_OmitsCrispEdges()
    {
        var svg = new MicroQrCodeImageBuilder(TestContent)
            .WithModuleShape(CircleModuleShape.Default, 0.9f)
            .ToSvgString();

        await Assert.That(svg).Contains("<svg");
        await Assert.That(svg).DoesNotContain("crispEdges");
    }

    #endregion

    #region Output methods

    [Test]
    public async Task ToImage_ReturnsValidSKImage()
    {
        using var image = new MicroQrCodeImageBuilder(TestContent).ToImage();

        await Assert.That(image).IsNotNull();
        await Assert.That(image.Width).IsEqualTo(512);
        await Assert.That(image.Height).IsEqualTo(512);
    }

    [Test]
    public async Task SaveTo_Stream_NullStream_ThrowsArgumentNullException()
    {
        var builder = new MicroQrCodeImageBuilder(TestContent);
        Assert.Throws<ArgumentNullException>(() => builder.SaveTo((Stream)null!));
    }

    [Test]
    public async Task SaveTo_BufferWriter_NullWriter_ThrowsArgumentNullException()
    {
        var builder = new MicroQrCodeImageBuilder(TestContent);
        Assert.Throws<ArgumentNullException>(() => builder.SaveTo((IBufferWriter<byte>)null!));
    }

    #endregion

    #region Renderer / canvas extension entry points

    [Test]
    public async Task QRCodeRenderer_Render_MicroQrData_EveryModuleMatchesMatrix()
    {
        const int modulePixelSize = 4;
        var data = MicroQrCodeGenerator.CreateMicroQrCode("12345", MicroQrEccLevel.L);
        var side = data.Size * modulePixelSize;

        using var bitmap = new SKBitmap(side, side);
        using (var canvas = new SKCanvas(bitmap))
        {
            QRCodeRenderer.Render(canvas, SKRect.Create(0, 0, side, side), data, codeColor: null, backgroundColor: null);
        }

        for (var row = 0; row < data.Size; row++)
        {
            for (var col = 0; col < data.Size; col++)
            {
                var expected = data[row, col] ? SKColors.Black : SKColors.White;
                var actual = bitmap.GetPixel(col * modulePixelSize + modulePixelSize / 2, row * modulePixelSize + modulePixelSize / 2);
                if (actual != expected)
                {
                    Assert.Fail($"Module ({row},{col}) expected {expected} but was {actual}");
                }
            }
        }
    }

    [Test]
    public async Task CanvasExtension_Render_MicroQrData_DrawsSymbol()
    {
        var data = MicroQrCodeGenerator.CreateMicroQrCode("12345", MicroQrEccLevel.L);
        const int side = 150; // 15 * 10: exact multiple of the M2 default matrix (11 + 2*2 quiet zone)

        using var bitmap = new SKBitmap(side, side);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Render(data, side, side);
        }

        // Finder corner: first core module is dark. Quiet zone 2 modules → module (2,2), 10px cells.
        await Assert.That(bitmap.GetPixel(25, 25)).IsEqualTo(SKColors.Black);
        // Quiet zone stays light (background white).
        await Assert.That(bitmap.GetPixel(5, 5)).IsEqualTo(SKColors.White);
    }

    #endregion

    private static bool BitmapsAreEqual(SKBitmap left, SKBitmap right)
    {
        if (left.Width != right.Width || left.Height != right.Height)
            return false;

        for (var y = 0; y < left.Height; y++)
        {
            for (var x = 0; x < left.Width; x++)
            {
                if (left.GetPixel(x, y) != right.GetPixel(x, y))
                    return false;
            }
        }

        return true;
    }
}
