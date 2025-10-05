using Xunit;
using ZXing;
using ZXing.SkiaSharp;

[assembly: CaptureConsole]

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
    [InlineData("0123456789", ECCLevel.L, EciMode.Utf8)]
    [InlineData("Hello, World!", ECCLevel.L, EciMode.Utf8)]
    [InlineData("special", ECCLevel.L, EciMode.Utf8)]
    public void CreateQrCode_Default_ascii_words_IsDecodable(string content, ECCLevel eccLevel, EciMode eciMode)
    {
        // default is ISO-8859-1 for ASCII-only
        AssertQrCodeIsDecodable(content, eccLevel, eciMode);
    }

    [Theory]
    [InlineData("こんにちは", ECCLevel.M, EciMode.Utf8)]
    [InlineData("你好世界", ECCLevel.Q, EciMode.Utf8)]
    [InlineData("Привет мир", ECCLevel.H, EciMode.Utf8)]
    [InlineData("🎉🎊🎈", ECCLevel.L, EciMode.Utf8)]
    [InlineData("café", ECCLevel.M, EciMode.Utf8)]
    [InlineData("Café", ECCLevel.L, EciMode.Utf8)]
    [InlineData("Résumé", ECCLevel.M, EciMode.Utf8)]
    [InlineData("Naïve", ECCLevel.Q, EciMode.Utf8)]
    [InlineData("Zürich", ECCLevel.H, EciMode.Utf8)]
    public void CreateQrCode_Default_utf8_words_IsDecodable(string content, ECCLevel eccLevel, EciMode eciMode)
    {
        // automatic ECI mode selection should match Utf8 for non-ASCII
        AssertQrCodeIsDecodable(content, eccLevel, eciMode);
    }

    [Theory]
    [InlineData("0123456789", ECCLevel.L, EciMode.Utf8)]
    [InlineData("Hello, World!", ECCLevel.L, EciMode.Utf8)]
    [InlineData("special", ECCLevel.L, EciMode.Utf8)]
    [InlineData("こんにちは", ECCLevel.M, EciMode.Utf8)]
    [InlineData("你好世界", ECCLevel.Q, EciMode.Utf8)]
    [InlineData("Привет мир", ECCLevel.H, EciMode.Utf8)]
    [InlineData("🎉🎊🎈", ECCLevel.L, EciMode.Utf8)]
    [InlineData("café", ECCLevel.M, EciMode.Utf8)]
    [InlineData("Café", ECCLevel.L, EciMode.Utf8)]
    [InlineData("Résumé", ECCLevel.M, EciMode.Utf8)]
    [InlineData("Naïve", ECCLevel.Q, EciMode.Utf8)]
    [InlineData("Zürich", ECCLevel.H, EciMode.Utf8)]
    public void CreateQrCode_Utf8_IsDecodable(string content, ECCLevel eccLevel, EciMode eciMode)
    {
        AssertQrCodeIsDecodable(content, eccLevel, eciMode);
    }

    [Theory]
    [InlineData("0123456789", ECCLevel.L, EciMode.Iso8859_1)]
    [InlineData("HELLO WORLD", ECCLevel.M, EciMode.Iso8859_1)]
    [InlineData("special", ECCLevel.L, EciMode.Iso8859_1)]
    [InlineData("ABC-123", ECCLevel.Q, EciMode.Iso8859_1)]
    [InlineData("Test123", ECCLevel.H, EciMode.Iso8859_1)]
    [InlineData("Café", ECCLevel.L, EciMode.Iso8859_1)]
    [InlineData("Résumé", ECCLevel.M, EciMode.Iso8859_1)]
    [InlineData("Naïve", ECCLevel.Q, EciMode.Iso8859_1)]
    [InlineData("Zürich", ECCLevel.H, EciMode.Iso8859_1)]
    public void CreateQrCode_Iso8859_IsDecodable(string content, ECCLevel eccLevel, EciMode eciMode)
    {
        AssertQrCodeIsDecodable(content, eccLevel, eciMode);
    }

    [Fact]
    public void Debug_Zurich_Version_Check()
    {
        var content = "Zürich";

        // check byte length in UTF-8
        var utf8Bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var byteCount = utf8Bytes.Length;

        using var generator = new QRCodeGenerator();

        var qrH = generator.CreateQrCode(content, ECCLevel.H, eciMode: EciMode.Utf8);
        var qrM = generator.CreateQrCode(content, ECCLevel.M, eciMode: EciMode.Utf8);
        var qrL = generator.CreateQrCode(content, ECCLevel.L, eciMode: EciMode.Utf8);

        // debug output
        Console.WriteLine($"Content: \"{content}\"");
        Console.WriteLine($"UTF-8 Bytes: {byteCount} [{string.Join(", ", utf8Bytes.Select(b => $"0x{b:X2}"))}]");
        Console.WriteLine($"");
        Console.WriteLine($"ECC Level H → Version {qrH.Version} (Size: {qrH.Size}x{qrH.Size})");
        Console.WriteLine($"ECC Level M → Version {qrM.Version} (Size: {qrM.Size}x{qrM.Size})");
        Console.WriteLine($"ECC Level L → Version {qrL.Version} (Size: {qrL.Size}x{qrL.Size})");
        Console.WriteLine($"");

        // Version 1 logical capacity
        Console.WriteLine("Version 1 Byte mode capacity:");
        Console.WriteLine("  ECC L: 17 bytes");
        Console.WriteLine("  ECC M: 14 bytes");
        Console.WriteLine("  ECC Q: 11 bytes");
        Console.WriteLine("  ECC H: 7 bytes");
        Console.WriteLine($"");
        Console.WriteLine($"Required (with ECI header): ~10-12 bytes");
        Console.WriteLine($"");

        // Expected
        Console.WriteLine("Expected versions:");
        Console.WriteLine("  ECC L: Version 1 (17 bytes available)");
        Console.WriteLine("  ECC M: Version 1 (14 bytes available)");
        Console.WriteLine("  ECC Q: Version 1 (11 bytes available)");
        Console.WriteLine("  ECC H: Version 2 (16 bytes available)");

        // Should be automatically upgrade to Version 2
        Assert.True(qrH.Version >= 2, $"ECC H should use Version 2 or higher, but got Version {qrH.Version}");
        Assert.True(qrM.Version >= 1, $"ECC M should use Version 1 or higher, but got Version {qrM.Version}");
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
        var size = qr.Size;
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
                if (qr[y, x])
                {
                    canvas.DrawRect(x * scale, y * scale, scale, scale, paint);
                }
            }
        }

        return bitmap;
    }
}
