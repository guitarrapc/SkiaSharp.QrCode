using Xunit;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using ZXing.QrCode.Internal;

namespace SkiaSharp.QrCode.Tests;

/// <summary>
/// Cross-validation against ZXing.Net: QR codes produced by a completely independent
/// encoder must decode correctly with this library's decoder. Complements the
/// existing ZXing-decodes-our-output tests (QRCodeDecodabilityTest) in the other direction.
/// </summary>
public class QRCodeDecoderZXingCrossTest
{
    [Theory]
    [InlineData("0123456789")]
    [InlineData("HELLO WORLD $%*+-./:")]
    [InlineData("Hello, World! lowercase")]
    [InlineData("https://example.com/path?query=value")]
    public void Decode_ZXingEncoded_Ascii(string content)
        => AssertDecodesZXingQr(content, ErrorCorrectionLevel.M);

    [Theory]
    [InlineData("こんにちは世界")]
    [InlineData("你好世界")]
    [InlineData("Привет мир")]
    [InlineData("🎉 emoji content 🎊")]
    public void Decode_ZXingEncoded_Utf8(string content)
        => AssertDecodesZXingQr(content, ErrorCorrectionLevel.M, characterSet: "UTF-8");

    [Theory]
    [InlineData("Café")]
    [InlineData("Zürich Résumé")]
    public void Decode_ZXingEncoded_Iso8859_1(string content)
        => AssertDecodesZXingQr(content, ErrorCorrectionLevel.M, characterSet: "ISO-8859-1");

    [Fact]
    public void Decode_ZXingEncoded_AllEccLevels()
    {
        foreach (var (zxingLevel, expectedLevel) in new[]
        {
            (ErrorCorrectionLevel.L, ECCLevel.L),
            (ErrorCorrectionLevel.M, ECCLevel.M),
            (ErrorCorrectionLevel.Q, ECCLevel.Q),
            (ErrorCorrectionLevel.H, ECCLevel.H),
        })
        {
            var content = $"ecc level {zxingLevel}";
            var modules = EncodeWithZXing(content, zxingLevel, characterSet: null, out var size);

            Assert.True(QRCodeDecoder.TryDecode(modules, size, out var decoded, out var info), $"level={zxingLevel}, status={info.Status}");
            Assert.Equal(content, decoded);
            Assert.Equal(expectedLevel, info.EccLevel);
        }
    }

    [Fact]
    public void Decode_ZXingEncoded_LargeContent_HigherVersion()
    {
        var content = string.Join(" ", Enumerable.Range(0, 60).Select(i => $"chunk{i:D3}"));
        var modules = EncodeWithZXing(content, ErrorCorrectionLevel.Q, characterSet: null, out var size);

        Assert.True(QRCodeDecoder.TryDecode(modules, size, out var decoded, out var info), $"status={info.Status}");
        Assert.Equal(content, decoded);
        Assert.True(info.Version > 5, $"expected a higher version, got {info.Version}");
    }

    private static void AssertDecodesZXingQr(string content, ErrorCorrectionLevel level, string? characterSet = null)
    {
        var modules = EncodeWithZXing(content, level, characterSet, out var size);

        Assert.True(QRCodeDecoder.TryDecode(modules, size, out var decoded, out var info), $"decode failed: status={info.Status}, version={info.Version}");
        Assert.Equal(content, decoded);
    }

    private static byte[] EncodeWithZXing(string content, ErrorCorrectionLevel level, string? characterSet, out int size)
    {
        var options = new QrCodeEncodingOptions
        {
            ErrorCorrection = level,
            Margin = 4,
        };
        if (characterSet is not null)
        {
            options.CharacterSet = characterSet;
        }

        var writer = new BarcodeWriterGeneric
        {
            Format = BarcodeFormat.QR_CODE,
            Options = options,
        };

        BitMatrix matrix = writer.Encode(content);
        Assert.Equal(matrix.Width, matrix.Height);

        size = matrix.Width;
        var modules = new byte[size * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                modules[y * size + x] = matrix[x, y] ? (byte)1 : (byte)0;
            }
        }
        return modules;
    }
}
