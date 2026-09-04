using BenchmarkDotNet.Configs;

/// <summary>
/// Cross-library rMQR encoding and decoding. CodeGlyphX is the only other library
/// compared in this project that implements ISO/IEC 23941 at all, so these are the only
/// rMQR rows with something to compare against.
///
/// Each row pins the same version on both libraries. rMQR versions are not ordered by
/// size, and the two libraries pick differently when left to fit automatically, so
/// pinning is what makes the rows measure the same symbol. The version numbering is the
/// same on both sides (1 is R7x43, 32 is R17x139), which is why the cast works.
///
/// Comparability notes:
///
///   SkiaSharp.QrCode places a quiet zone in the returned matrix; CodeGlyphX returns the
///   bare symbol.
///   Both libraries pick one mode for the whole payload by default, so the encode rows do
///   the same work.
///   Decode rows compare matrix decoding only. CodeGlyphX does not decode rMQR from an
///   image, so there is no image row to compare.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class SimpleRmQR
{
    private const string SkiaEncode = "SkiaSharp.QrCode (encode)";
    private const string GlyphEncode = "CodeGlyphX (encode)";
    private const string SkiaDecode = "SkiaSharp.QrCode (decode)";
    private const string GlyphDecode = "CodeGlyphX (decode)";

    private const string Numeric = "012345678901";                                          // R7x43-M
    private const string Alphanumeric = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 $%*+-.";      // R11x59-M

    private string _byte = default!;                                                        // R17x139-M

    private byte[] _numericModules = default!;
    private byte[] _alphanumericModules = default!;
    private byte[] _byteModules = default!;
    private (int width, int height) _numericExtent;
    private (int width, int height) _alphanumericExtent;
    private (int width, int height) _byteExtent;
    private CodeGlyphX.BitMatrix _numericMatrix = default!;
    private CodeGlyphX.BitMatrix _alphanumericMatrix = default!;
    private CodeGlyphX.BitMatrix _byteMatrix = default!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _byte = string.Concat(Enumerable.Repeat("the quick brown fox jumps over the lazy dog?! ", 4)).Substring(0, 150);

        (_numericModules, _numericExtent) = BuildModules(Numeric, RmQRVersion.R7x43);
        (_alphanumericModules, _alphanumericExtent) = BuildModules(Alphanumeric, RmQRVersion.R11x59);
        (_byteModules, _byteExtent) = BuildModules(_byte, RmQRVersion.R17x139);

        _numericMatrix = ToBitMatrix(_numericModules, _numericExtent);
        _alphanumericMatrix = ToBitMatrix(_alphanumericModules, _alphanumericExtent);
        _byteMatrix = ToBitMatrix(_byteModules, _byteExtent);

        VerifyEveryDecoderSucceeds();
    }

    // SkiaSharp.QrCode, encode

    [Benchmark(Baseline = true)]
    [BenchmarkCategory(SkiaEncode)]
    public RmQRCodeData SkiaSharpQrCode_Numeric_R7x43_Encode()
        => RmQRCodeGenerator.CreateRmQRCode(Numeric.AsSpan(), RmQREccLevel.M, new RmQRCodeGeneratorOptions { Version = RmQRVersion.R7x43 });

    [Benchmark]
    [BenchmarkCategory(SkiaEncode)]
    public RmQRCodeData SkiaSharpQrCode_Alphanumeric_R11x59_Encode()
        => RmQRCodeGenerator.CreateRmQRCode(Alphanumeric.AsSpan(), RmQREccLevel.M, new RmQRCodeGeneratorOptions { Version = RmQRVersion.R11x59 });

    [Benchmark]
    [BenchmarkCategory(SkiaEncode)]
    public RmQRCodeData SkiaSharpQrCode_Byte_R17x139_Encode()
        => RmQRCodeGenerator.CreateRmQRCode(_byte.AsSpan(), RmQREccLevel.M, new RmQRCodeGeneratorOptions { Version = RmQRVersion.R17x139 });

    // CodeGlyphX, encode

    [Benchmark(Baseline = true)]
    [BenchmarkCategory(GlyphEncode)]
    public CodeGlyphX.RmQrCode CodeGlyphX_Numeric_R7x43_Encode()
        => CodeGlyphX.RmQrCodeEncoder.EncodeText(Numeric, GlyphOptions(RmQRVersion.R7x43));

    [Benchmark]
    [BenchmarkCategory(GlyphEncode)]
    public CodeGlyphX.RmQrCode CodeGlyphX_Alphanumeric_R11x59_Encode()
        => CodeGlyphX.RmQrCodeEncoder.EncodeText(Alphanumeric, GlyphOptions(RmQRVersion.R11x59));

    [Benchmark]
    [BenchmarkCategory(GlyphEncode)]
    public CodeGlyphX.RmQrCode CodeGlyphX_Byte_R17x139_Encode()
        => CodeGlyphX.RmQrCodeEncoder.EncodeText(_byte, GlyphOptions(RmQRVersion.R17x139));

    // SkiaSharp.QrCode, decode

    [Benchmark(Baseline = true)]
    [BenchmarkCategory(SkiaDecode)]
    public string SkiaSharpQrCode_Numeric_R7x43_Decode()
    {
        RmQRCodeDecoder.TryDecode(_numericModules, _numericExtent.width, _numericExtent.height, out var text, out _);
        return text;
    }

    [Benchmark]
    [BenchmarkCategory(SkiaDecode)]
    public string SkiaSharpQrCode_Alphanumeric_R11x59_Decode()
    {
        RmQRCodeDecoder.TryDecode(_alphanumericModules, _alphanumericExtent.width, _alphanumericExtent.height, out var text, out _);
        return text;
    }

    [Benchmark]
    [BenchmarkCategory(SkiaDecode)]
    public string SkiaSharpQrCode_Byte_R17x139_Decode()
    {
        RmQRCodeDecoder.TryDecode(_byteModules, _byteExtent.width, _byteExtent.height, out var text, out _);
        return text;
    }

    // CodeGlyphX, decode

    [Benchmark(Baseline = true)]
    [BenchmarkCategory(GlyphDecode)]
    public string CodeGlyphX_Numeric_R7x43_Decode()
    {
        CodeGlyphX.RmQrDecoder.TryDecode(_numericMatrix, out var decoded);
        return decoded.Text;
    }

    [Benchmark]
    [BenchmarkCategory(GlyphDecode)]
    public string CodeGlyphX_Alphanumeric_R11x59_Decode()
    {
        CodeGlyphX.RmQrDecoder.TryDecode(_alphanumericMatrix, out var decoded);
        return decoded.Text;
    }

    [Benchmark]
    [BenchmarkCategory(GlyphDecode)]
    public string CodeGlyphX_Byte_R17x139_Decode()
    {
        CodeGlyphX.RmQrDecoder.TryDecode(_byteMatrix, out var decoded);
        return decoded.Text;
    }

    private static CodeGlyphX.RmQrEncodingOptions GlyphOptions(RmQRVersion version) => new()
    {
        ErrorCorrectionLevel = CodeGlyphX.QrErrorCorrectionLevel.M,
        MinimumVersion = (int)version,
        MaximumVersion = (int)version,
    };

    private void VerifyEveryDecoderSucceeds()
    {
        Check(nameof(SkiaSharpQrCode_Numeric_R7x43_Decode), Numeric, SkiaSharpQrCode_Numeric_R7x43_Decode());
        Check(nameof(SkiaSharpQrCode_Alphanumeric_R11x59_Decode), Alphanumeric, SkiaSharpQrCode_Alphanumeric_R11x59_Decode());
        Check(nameof(SkiaSharpQrCode_Byte_R17x139_Decode), _byte, SkiaSharpQrCode_Byte_R17x139_Decode());
        Check(nameof(CodeGlyphX_Numeric_R7x43_Decode), Numeric, CodeGlyphX_Numeric_R7x43_Decode());
        Check(nameof(CodeGlyphX_Alphanumeric_R11x59_Decode), Alphanumeric, CodeGlyphX_Alphanumeric_R11x59_Decode());
        Check(nameof(CodeGlyphX_Byte_R17x139_Decode), _byte, CodeGlyphX_Byte_R17x139_Decode());

        static void Check(string path, string expected, string? decoded)
        {
            if (decoded != expected)
            {
                throw new InvalidOperationException(
                    $"{path} did not round-trip: expected \"{expected}\", got \"{decoded ?? "<null>"}\".");
            }
        }
    }

    private static (byte[] modules, (int width, int height) extent) BuildModules(string content, RmQRVersion version)
    {
        var options = new RmQRCodeGeneratorOptions { Version = version, QuietZoneSize = 0 };
        var calculated = Sizing.Required(content.AsSpan(), RmQREccLevel.M, options);
        var buffer = new byte[calculated.BufferSize];
        RmQRCodeGenerator.CreateRmQRCode(content.AsSpan(), RmQREccLevel.M, buffer, options);
        return (buffer, (calculated.Width, calculated.Height));
    }

    private static CodeGlyphX.BitMatrix ToBitMatrix(byte[] modules, (int width, int height) extent)
    {
        var matrix = new CodeGlyphX.BitMatrix(extent.width, extent.height);
        for (var y = 0; y < extent.height; y++)
        {
            for (var x = 0; x < extent.width; x++)
            {
                matrix.Set(x, y, modules[y * extent.width + x] != 0);
            }
        }
        return matrix;
    }
}
