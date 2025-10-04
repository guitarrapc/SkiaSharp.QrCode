using Xunit;
using ZXing;
using ZXing.SkiaSharp;

namespace SkiaSharp.QrCode.Tests;

public class QRCodeDecodabilityTest
{
    private readonly BarcodeReader _reader;

    public QRCodeDecodabilityTest()
    {
        _reader = new BarcodeReader
        {
            AutoRotate = true,
            Options = new ZXing.Common.DecodingOptions
            {
                TryHarder = true,
                TryInverted = true,
                PossibleFormats = new[] { BarcodeFormat.QR_CODE },
            }
        };
    }

    [Theory]
    [InlineData("0123456789", ECCLevel.L, EciMode.Default)]
    [InlineData("HELLO WORLD", ECCLevel.M, EciMode.Default)]
    [InlineData("ABC-123", ECCLevel.Q, EciMode.Default)]
    [InlineData("Test123", ECCLevel.H, EciMode.Default)]
    public void CreateQrCode_Default_IsDecodable(string content, ECCLevel eccLevel, EciMode eciMode)
    {
        AssertQrCodeIsDecodable(content, eccLevel, eciMode);
    }

    [Theory]
    [InlineData("Hello, World!", ECCLevel.L, EciMode.Utf8)]
    [InlineData("こんにちは", ECCLevel.M, EciMode.Utf8)]
    [InlineData("你好世界", ECCLevel.Q, EciMode.Utf8)]
    [InlineData("Привет мир", ECCLevel.H, EciMode.Utf8)]
    [InlineData("🎉🎊🎈", ECCLevel.L, EciMode.Utf8)]
    [InlineData("café", ECCLevel.M, EciMode.Utf8)]
    public void CreateQrCode_Utf8_IsDecodable(string content, ECCLevel eccLevel, EciMode eciMode)
    {
        AssertQrCodeIsDecodable(content, eccLevel, eciMode);
    }

    [Theory]
    [InlineData("Café", ECCLevel.L, EciMode.Iso8859_1)]
    [InlineData("Résumé", ECCLevel.M, EciMode.Iso8859_1)]
    public void CreateQrCode_Iso8859_IsDecodable(string content, ECCLevel eccLevel, EciMode eciMode)
    {
        AssertQrCodeIsDecodable(content, eccLevel, eciMode);
    }

    [Theory]
    [InlineData("", ECCLevel.L, EciMode.Default)]
    [InlineData("A", ECCLevel.M, EciMode.Default)]
    [InlineData(" ", ECCLevel.Q, EciMode.Default)]
    [InlineData("\t", ECCLevel.H, EciMode.Default)]
    [InlineData("\n", ECCLevel.L, EciMode.Utf8)]
    public void CreateQrCode_EdgeCases_IsDecodable(string content, ECCLevel eccLevel, EciMode eciMode)
    {
        AssertQrCodeIsDecodable(content, eccLevel, eciMode);
    }

    [Theory]
    [InlineData(ECCLevel.L, 41)]  // Version 1 max
    [InlineData(ECCLevel.L, 42)]  // Version 2 min
    [InlineData(ECCLevel.M, 34)]  // Version 1 max
    [InlineData(ECCLevel.M, 35)]  // Version 2 min
    [InlineData(ECCLevel.H, 17)]  // Version 1 max
    [InlineData(ECCLevel.H, 18)]  // Version 2 min
    public void CreateQrCode_VersionBoundaries_IsDecodable(ECCLevel eccLevel, int charCount)
    {
        var content = new string('1', charCount);
        AssertQrCodeIsDecodable(content, eccLevel, EciMode.Default);
    }

    [Theory]
    [InlineData(ECCLevel.L, 100)]
    [InlineData(ECCLevel.M, 500)]
    [InlineData(ECCLevel.Q, 1000)]
    public void CreateQrCode_LargeData_IsDecodable(ECCLevel eccLevel, int charCount)
    {
        var content = new string('A', charCount);
        AssertQrCodeIsDecodable(content, eccLevel, EciMode.Default);
    }

    [Theory]
    [InlineData("TEST", ECCLevel.L)]
    [InlineData("TEST", ECCLevel.M)]
    [InlineData("TEST", ECCLevel.Q)]
    [InlineData("TEST", ECCLevel.H)]
    public void CreateQrCode_AllEccLevels_IsDecodable(string content, ECCLevel eccLevel)
    {
        AssertQrCodeIsDecodable(content, eccLevel, EciMode.Default);
    }

    // helpers

    /// <summary>
    /// Assert that generated QR code is decodable and content matches.
    /// </summary>
    private void AssertQrCodeIsDecodable(string expectedContent, ECCLevel eccLevel, EciMode eciMode)
    {
        using var generator = new QRCodeGenerator();
        var qr = generator.CreateQrCode(expectedContent, eccLevel, eciMode: eciMode);

        // Convert QRCodeData to SKBitmap
        using var bitmap = QrCodeToSKBitmap(qr);

        // Decode using ZXing
        var result = _reader.Decode(bitmap);

        // Assert decoding succeeded
        Assert.NotNull(result);
        Assert.Equal(BarcodeFormat.QR_CODE, result.BarcodeFormat);

        // Assert content matches
        Assert.Equal(expectedContent, result.Text);

        // Additional metadata checks
        if (result.ResultMetadata != null)
        {
            // Verify ECC level if available
            if (result.ResultMetadata.ContainsKey(ZXing.ResultMetadataType.ERROR_CORRECTION_LEVEL))
            {
                var decodedEccLevel = result.ResultMetadata[ZXing.ResultMetadataType.ERROR_CORRECTION_LEVEL].ToString();
                var expectedEccString = eccLevel.ToString();
                Assert.Equal(expectedEccString, decodedEccLevel);
            }
        }
    }

    /// <summary>
    /// Convert QRCodeData to SKBitmap for decoding.
    /// Uses scaling for better decoding reliability.
    /// </summary>
    private static SKBitmap QrCodeToSKBitmap(QRCodeData qr)
    {
        var size = qr.ModuleMatrix.Count;
        var scale = 10; // Scale up for better decoding
        var bitmap = new SKBitmap(size * scale, size * scale);

        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint();

        // Fill white background
        canvas.Clear(SKColors.White);

        // Draw black modules
        paint.Color = SKColors.Black;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                if (qr.ModuleMatrix[y][x])
                {
                    canvas.DrawRect(x * scale, y * scale, scale, scale, paint);
                }
            }
        }

        return bitmap;
    }
}
