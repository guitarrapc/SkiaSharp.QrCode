using SkiaSharp.QrCode.Image;
using System.Buffers;
using Xunit;

namespace SkiaSharp.QrCode.Tests;

public class QRCodeImageBuilderTest
{
    private const string TestContent = "https://github.com/guitarrapc/SkiaSharp.QrCode";

    #region Constructor Tests

    [Fact]
    public void Constructor_ValidContent_Success()
    {
        var builder = new QRCodeImageBuilder(TestContent);
        Assert.NotNull(builder);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_InvalidContent_ThrowsArgumentException(string? content)
    {
        Assert.Throws<ArgumentException>(() => new QRCodeImageBuilder(content!));
    }

    #endregion

    #region Static Method Tests - GetPngBytes

    [Fact]
    public void GetPngBytes_DefaultParameters_ReturnsValidPngBytes()
    {
        var bytes = QRCodeImageBuilder.GetPngBytes(TestContent);

        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
        // PNG signature: 89 50 4E 47 0D 0A 1A 0A
        Assert.Equal(0x89, bytes[0]);
        Assert.Equal(0x50, bytes[1]);
        Assert.Equal(0x4E, bytes[2]);
        Assert.Equal(0x47, bytes[3]);
    }

    [Theory]
    [InlineData(ECCLevel.L)]
    [InlineData(ECCLevel.M)]
    [InlineData(ECCLevel.Q)]
    [InlineData(ECCLevel.H)]
    public void GetPngBytes_DifferentEccLevels_ReturnsValidPngBytes(ECCLevel eccLevel)
    {
        var bytes = QRCodeImageBuilder.GetPngBytes(TestContent, eccLevel);

        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
    }

    [Theory]
    [InlineData(128)]
    [InlineData(256)]
    [InlineData(512)]
    [InlineData(1024)]
    public void GetPngBytes_DifferentSizes_ReturnsValidPngBytes(int size)
    {
        var bytes = QRCodeImageBuilder.GetPngBytes(TestContent, size: size);

        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
    }

    #endregion

    #region Static Method Tests - GetImageBytes

    [Theory]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    [InlineData(SKEncodedImageFormat.Webp)]
    public void GetImageBytes_DifferentFormats_ReturnsValidBytes(SKEncodedImageFormat format)
    {
        var bytes = QRCodeImageBuilder.GetImageBytes(TestContent, format);

        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
    }

    [Theory]
    [InlineData(50)]
    [InlineData(75)]
    [InlineData(100)]
    public void GetImageBytes_DifferentQuality_ReturnsValidBytes(int quality)
    {
        var bytes = QRCodeImageBuilder.GetImageBytes(TestContent, SKEncodedImageFormat.Jpeg, quality: quality);

        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
    }

    #endregion

    #region Static Method Tests - SavePng

    [Fact]
    public void SavePng_ValidStream_WritesData()
    {
        using var stream = new MemoryStream();
        QRCodeImageBuilder.SavePng(TestContent, stream);

        Assert.True(stream.Length > 0);
        stream.Position = 0;
        var bytes = stream.ToArray();
        // Verify PNG signature
        Assert.Equal(0x89, bytes[0]);
        Assert.Equal(0x50, bytes[1]);
    }

    #endregion

    #region Static Method Tests - WritePng

    [Fact]
    public void WritePng_ValidBufferWriter_WritesData()
    {
        var writer = new ArrayBufferWriter<byte>();
        QRCodeImageBuilder.WritePng(TestContent, writer);

        var writtenBytes = writer.WrittenSpan;
        Assert.True(writtenBytes.Length > 0);
        // Verify PNG signature
        Assert.Equal(0x89, writtenBytes[0]);
        Assert.Equal(0x50, writtenBytes[1]);
    }

    #endregion

    #region Static Method Tests - WriteImage

    [Theory]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    [InlineData(SKEncodedImageFormat.Webp)]
    public void WriteImage_DifferentFormats_WritesData(SKEncodedImageFormat format)
    {
        var writer = new ArrayBufferWriter<byte>();
        QRCodeImageBuilder.WriteImage(TestContent, writer, format);

        var writtenBytes = writer.WrittenSpan;
        Assert.True(writtenBytes.Length > 0);
    }

    #endregion

    #region Fluent API Tests - WithSize

    [Fact]
    public void WithSize_ValidSize_ReturnsBuilder()
    {
        var builder = new QRCodeImageBuilder(TestContent);
        var result = builder.WithSize(256, 256);

        Assert.Same(builder, result);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(-1, 100)]
    [InlineData(100, 0)]
    [InlineData(100, -1)]
    public void WithSize_InvalidSize_ThrowsArgumentOutOfRangeException(int width, int height)
    {
        var builder = new QRCodeImageBuilder(TestContent);
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithSize(width, height));
    }

    [Fact]
    public void WithSize_CustomSize_GeneratesCorrectSize()
    {
        var builder = new QRCodeImageBuilder(TestContent);
        var bitmap = builder.WithSize(300, 400).ToBitmap();

        using (bitmap)
        {
            Assert.Equal(300, bitmap.Width);
            Assert.Equal(400, bitmap.Height);
        }
    }

    #endregion

    #region Fluent API Tests - WithModulePixelSize

    [Fact]
    public void WithModulePixelSize_ValidSize_ReturnsBuilder()
    {
        var builder = new QRCodeImageBuilder(TestContent);
        var result = builder.WithModulePixelSize(10);

        Assert.Same(builder, result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void WithModulePixelSize_InvalidSize_ThrowsArgumentOutOfRangeException(int modulePixelSize)
    {
        var builder = new QRCodeImageBuilder(TestContent);
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithModulePixelSize(modulePixelSize));
    }

    [Fact]
    public void WithModulePixelSize_CustomSize_GeneratesCorrectSize()
    {
        const int modulePixelSize = 10;
        var qrCodeData = QRCodeGenerator.CreateQrCode(TestContent, ECCLevel.M);
        var expectedSide = qrCodeData.Size * modulePixelSize;

        using var bitmap = new QRCodeImageBuilder(TestContent)
            .WithModulePixelSize(modulePixelSize)
            .ToBitmap();

        Assert.Equal(expectedSide, bitmap.Width);
        Assert.Equal(expectedSide, bitmap.Height);
    }

    [Fact]
    public void WithModulePixelSize_QRCodeDataBuilder_GeneratesCorrectSize()
    {
        const int modulePixelSize = 8;
        var qrCodeData = QRCodeGenerator.CreateQrCode(TestContent, ECCLevel.H, requestedVersion: 10);
        var expectedSide = qrCodeData.Size * modulePixelSize;

        using var bitmap = new QRCodeImageBuilder(qrCodeData)
            .WithModulePixelSize(modulePixelSize)
            .ToBitmap();

        Assert.Equal(expectedSide, bitmap.Width);
        Assert.Equal(expectedSide, bitmap.Height);
    }

    [Fact]
    public void WithModulePixelSize_AndLargerCanvas_PadsAndCentersContent()
    {
        const int modulePixelSize = 6;
        var qrCodeData = QRCodeGenerator.CreateQrCode(TestContent, ECCLevel.M);
        var contentSide = qrCodeData.Size * modulePixelSize;
        const int canvasWidth = 400;
        const int canvasHeight = 500;
        Assert.True(canvasWidth >= contentSide);
        Assert.True(canvasHeight >= contentSide);

        using var bitmap = new QRCodeImageBuilder(TestContent)
            .WithModulePixelSize(modulePixelSize)
            .WithSize(canvasWidth, canvasHeight)
            .WithColors(codeColor: SKColors.Black, backgroundColor: SKColors.White, clearColor: SKColors.Transparent)
            .ToBitmap();

        Assert.Equal(canvasWidth, bitmap.Width);
        Assert.Equal(canvasHeight, bitmap.Height);

        var expectedLeft = (canvasWidth - contentSide) / 2;
        var expectedTop = (canvasHeight - contentSide) / 2;

        // Padding outside content should keep clearColor (transparent).
        Assert.Equal(0, bitmap.GetPixel(0, 0).Alpha);
        Assert.Equal(0, bitmap.GetPixel(canvasWidth - 1, canvasHeight - 1).Alpha);

        // Content area corners should be QR background (quiet zone).
        Assert.Equal(SKColors.White, bitmap.GetPixel(expectedLeft, expectedTop));
        Assert.Equal(SKColors.White, bitmap.GetPixel(expectedLeft + contentSide - 1, expectedTop + contentSide - 1));
    }

    [Fact]
    public void WithModulePixelSize_AndTooSmallCanvas_ThrowsInvalidOperationException()
    {
        const int modulePixelSize = 10;
        var qrCodeData = QRCodeGenerator.CreateQrCode(TestContent, ECCLevel.M);
        var contentSide = qrCodeData.Size * modulePixelSize;

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new QRCodeImageBuilder(TestContent)
                .WithModulePixelSize(modulePixelSize)
                .WithSize(contentSide - 1, contentSide)
                .ToBitmap());

        Assert.Contains("smaller than QR content size", ex.Message);
    }

    [Fact]
    public void WithModulePixelSize_AndExactCanvas_MatchesContentSize()
    {
        const int modulePixelSize = 8;
        var qrCodeData = QRCodeGenerator.CreateQrCode(TestContent, ECCLevel.M);
        var contentSide = qrCodeData.Size * modulePixelSize;

        using var bitmap = new QRCodeImageBuilder(TestContent)
            .WithModulePixelSize(modulePixelSize)
            .WithSize(contentSide, contentSide)
            .ToBitmap();

        Assert.Equal(contentSide, bitmap.Width);
        Assert.Equal(contentSide, bitmap.Height);
    }

    #endregion

    #region Fluent API Tests - WithFormat

    [Fact]
    public void WithFormat_ValidFormat_ReturnsBuilder()
    {
        var builder = new QRCodeImageBuilder(TestContent);
        var result = builder.WithFormat(SKEncodedImageFormat.Jpeg, 80);

        Assert.Same(builder, result);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void WithFormat_InvalidQuality_ThrowsArgumentOutOfRangeException(int quality)
    {
        var builder = new QRCodeImageBuilder(TestContent);
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithFormat(SKEncodedImageFormat.Png, quality));
    }

    #endregion

    #region Fluent API Tests - WithErrorCorrection

    [Theory]
    [InlineData(ECCLevel.L)]
    [InlineData(ECCLevel.M)]
    [InlineData(ECCLevel.Q)]
    [InlineData(ECCLevel.H)]
    public void WithErrorCorrection_AllLevels_ReturnsBuilder(ECCLevel eccLevel)
    {
        var builder = new QRCodeImageBuilder(TestContent);
        var result = builder.WithErrorCorrection(eccLevel);

        Assert.Same(builder, result);
    }

    #endregion

    #region Fluent API Tests - WithEciMode

    [Theory]
    [InlineData(EciMode.Default)]
    [InlineData(EciMode.Iso8859_1)]
    [InlineData(EciMode.Utf8)]
    public void WithEciMode_ValidModes_ReturnsBuilder(EciMode eciMode)
    {
        var builder = new QRCodeImageBuilder(TestContent);
        var result = builder.WithEciMode(eciMode);

        Assert.Same(builder, result);
    }

    #endregion

    #region Fluent API Tests - WithVersion

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(40)]
    public void WithVersion_ValidVersions_ReturnsBuilder(int version)
    {
        var builder = new QRCodeImageBuilder(TestContent);
        var result = builder.WithVersion(version);

        Assert.Same(builder, result);
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(0)]
    [InlineData(41)]
    public void WithVersion_InvalidVersion_ThrowsArgumentOutOfRangeException(int version)
    {
        var builder = new QRCodeImageBuilder(TestContent);

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithVersion(version));
    }

    [Fact]
    public void WithVersion_QRCodeDataBuilder_ThrowsInvalidOperationException()
    {
        var qrCodeData = QRCodeGenerator.CreateQrCode(TestContent, ECCLevel.H);
        var builder = new QRCodeImageBuilder(qrCodeData);

        Assert.Throws<InvalidOperationException>(() => builder.WithVersion(10));
    }

    [Fact]
    public void WithVersion_FixedVersion_MatchesQRCodeDataRendering()
    {
        const int fixedVersion = 10;
        using var expectedBitmap = new QRCodeImageBuilder(QRCodeGenerator.CreateQrCode(TestContent, ECCLevel.H, requestedVersion: fixedVersion))
            .WithSize(256, 256)
            .ToBitmap();

        using var actualBitmap = new QRCodeImageBuilder(TestContent)
            .WithErrorCorrection(ECCLevel.H)
            .WithVersion(fixedVersion)
            .WithSize(256, 256)
            .ToBitmap();

        Assert.True(BitmapsAreEqual(expectedBitmap, actualBitmap));
    }

    #endregion

    #region Fluent API Tests - WithQuietZone

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(10)]
    public void WithQuietZone_ValidSize_ReturnsBuilder(int size)
    {
        var builder = new QRCodeImageBuilder(TestContent);
        var result = builder.WithQuietZone(size);

        Assert.Same(builder, result);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    public void WithQuietZone_InvalidSize_ThrowsArgumentOutOfRangeException(int size)
    {
        var builder = new QRCodeImageBuilder(TestContent);
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithQuietZone(size));
    }

    #endregion

    #region Fluent API Tests - WithColors

    [Fact]
    public void WithColors_ValidColors_ReturnsBuilder()
    {
        var builder = new QRCodeImageBuilder(TestContent);
        var result = builder.WithColors(SKColors.Blue, SKColors.Yellow, SKColors.Transparent);

        Assert.Same(builder, result);
    }

    [Fact]
    public void WithColors_NullColors_ReturnsBuilder()
    {
        var builder = new QRCodeImageBuilder(TestContent);
        var result = builder.WithColors(null, null, null);

        Assert.Same(builder, result);
    }

    #endregion

    #region Fluent API Tests - WithModuleShape

    [Theory]
    [InlineData(0.5f)]
    [InlineData(0.8f)]
    [InlineData(1.0f)]
    public void WithModuleShape_ValidSizePercent_ReturnsBuilder(float sizePercent)
    {
        var builder = new QRCodeImageBuilder(TestContent);
        var result = builder.WithModuleShape(CircleModuleShape.Default, sizePercent);

        Assert.Same(builder, result);
    }

    [Theory]
    [InlineData(0.4f)]
    [InlineData(1.1f)]
    public void WithModuleShape_InvalidSizePercent_ThrowsArgumentOutOfRangeException(float sizePercent)
    {
        var builder = new QRCodeImageBuilder(TestContent);
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithModuleShape(CircleModuleShape.Default, sizePercent));
    }

    #endregion

    #region Fluent API Tests - WithGradient

    [Fact]
    public void WithGradient_ValidOptions_ReturnsBuilder()
    {
        var gradientOptions = new GradientOptions([SKColors.Red, SKColors.Blue], GradientDirection.TopToBottom);
        var builder = new QRCodeImageBuilder(TestContent);
        var result = builder.WithGradient(gradientOptions);

        Assert.Same(builder, result);
    }

    [Fact]
    public void WithGradient_Null_ReturnsBuilder()
    {
        var builder = new QRCodeImageBuilder(TestContent);
        var result = builder.WithGradient(null);

        Assert.Same(builder, result);
    }

    #endregion

    #region Fluent API Tests - WithIcon

    [Fact]
    public void WithIcon_Null_ReturnsBuilder()
    {
        var builder = new QRCodeImageBuilder(TestContent);
        var result = builder.WithIcon(null);

        Assert.Same(builder, result);
    }

    #endregion

    #region Output Method Tests - SaveTo Stream

    [Fact]
    public void SaveTo_Stream_ValidStream_WritesData()
    {
        var builder = new QRCodeImageBuilder(TestContent);
        using var stream = new MemoryStream();

        builder.SaveTo(stream);

        Assert.True(stream.Length > 0);
    }

    [Fact]
    public void SaveTo_Stream_NullStream_ThrowsArgumentNullException()
    {
        var builder = new QRCodeImageBuilder(TestContent);
        Assert.Throws<ArgumentNullException>(() => builder.SaveTo((Stream)null!));
    }

    [Fact]
    public void SaveTo_Stream_NonWritableStream_ThrowsArgumentException()
    {
        var builder = new QRCodeImageBuilder(TestContent);
        using var stream = new MemoryStream(new byte[100], writable: false);

        Assert.Throws<ArgumentException>(() => builder.SaveTo(stream));
    }

    #endregion

    #region Output Method Tests - SaveTo IBufferWriter

    [Fact]
    public void SaveTo_BufferWriter_ValidWriter_WritesData()
    {
        var builder = new QRCodeImageBuilder(TestContent);
        var writer = new ArrayBufferWriter<byte>();

        builder.SaveTo(writer);

        Assert.True(writer.WrittenCount > 0);
    }

    [Fact]
    public void SaveTo_BufferWriter_NullWriter_ThrowsArgumentNullException()
    {
        var builder = new QRCodeImageBuilder(TestContent);
        Assert.Throws<ArgumentNullException>(() => builder.SaveTo((IBufferWriter<byte>)null!));
    }

    #endregion

    #region Output Method Tests - ToByteArray

    [Fact]
    public void ToByteArray_ReturnsValidBytes()
    {
        var builder = new QRCodeImageBuilder(TestContent);
        var bytes = builder.ToByteArray();

        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
    }

    #endregion

    #region Output Method Tests - ToImage

    [Fact]
    public void ToImage_ReturnsValidSKImage()
    {
        var builder = new QRCodeImageBuilder(TestContent);
        using var image = builder.ToImage();

        Assert.NotNull(image);
        Assert.Equal(512, image.Width);
        Assert.Equal(512, image.Height);
    }

    #endregion

    #region Output Method Tests - ToBitmap

    [Fact]
    public void ToBitmap_ReturnsValidSKBitmap()
    {
        var builder = new QRCodeImageBuilder(TestContent);
        using var bitmap = builder.ToBitmap();

        Assert.NotNull(bitmap);
        Assert.Equal(512, bitmap.Width);
        Assert.Equal(512, bitmap.Height);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void FluentAPI_ChainMultipleMethods_GeneratesValidQRCode()
    {
        var builder = new QRCodeImageBuilder(TestContent)
            .WithSize(400, 400)
            .WithErrorCorrection(ECCLevel.H)
            .WithQuietZone(2)
            .WithColors(SKColors.DarkBlue, SKColors.LightYellow)
            .WithFormat(SKEncodedImageFormat.Png, 95);

        var bytes = builder.ToByteArray();

        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void FluentAPI_WithAllCustomizations_GeneratesValidQRCode()
    {
        var gradientOptions = new GradientOptions([SKColors.Purple, SKColors.Pink], GradientDirection.TopLeftToBottomRight);
        var builder = new QRCodeImageBuilder(TestContent)
            .WithSize(600, 600)
            .WithErrorCorrection(ECCLevel.H)
            .WithEciMode(EciMode.Utf8)
            .WithQuietZone(3)
            .WithColors(SKColors.Navy, SKColors.Beige, SKColors.Transparent)
            .WithModuleShape(CircleModuleShape.Default, 0.9f)
            .WithGradient(gradientOptions)
            .WithFormat(SKEncodedImageFormat.Png, 100);

        using var bitmap = builder.ToBitmap();

        Assert.NotNull(bitmap);
        Assert.Equal(600, bitmap.Width);
        Assert.Equal(600, bitmap.Height);
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
