using BenchmarkDotNet.Configs;

/// <summary>
/// Cross-library Micro QR encoding and decoding. Micro QR narrows the field sharply:
/// of the libraries compared elsewhere in this project only QRCoder and CodeGlyphX
/// support it at all, and only CodeGlyphX decodes it.
///
/// Payloads sit on the capacity boundary of three versions, and every library selects
/// the same version for each (M2-L numeric, M3-L alphanumeric, M4-M byte), so the rows
/// encode the same symbol.
///
/// Comparability notes:
///
///   SkiaSharp.QrCode and QRCoder place a quiet zone in the returned matrix; CodeGlyphX
///   returns the bare symbol.
///   SkiaSharp.QrCode picks the encoding mode from the payload, while CodeGlyphX exposes
///   one entry point per mode, so each row calls the CodeGlyphX method for the mode this
///   library would have chosen.
///   Decode rows compare matrix decoding only. Neither library's Micro QR image path is
///   measured here.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class SimpleMicroQR
{
    private const string SkiaEncode = "SkiaSharp.QrCode (encode)";
    private const string GlyphEncode = "CodeGlyphX (encode)";
    private const string CoderEncode = "QRCoder (encode)";
    private const string SkiaDecode = "SkiaSharp.QrCode (decode)";
    private const string GlyphDecode = "CodeGlyphX (decode)";

    private const string Numeric = "0123456789";          // M2-L
    private const string Alphanumeric = "HELLO WORLD 14"; // M3-L
    private const string Byte = "bytes m4 mode";          // M4-M

    private byte[] _numericModules = default!;
    private byte[] _alphanumericModules = default!;
    private byte[] _byteModules = default!;
    private int _numericSize;
    private int _alphanumericSize;
    private int _byteSize;
    private CodeGlyphX.BitMatrix _numericMatrix = default!;
    private CodeGlyphX.BitMatrix _alphanumericMatrix = default!;
    private CodeGlyphX.BitMatrix _byteMatrix = default!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        (_numericModules, _numericSize) = BuildModules(Numeric, MicroQREccLevel.L);
        (_alphanumericModules, _alphanumericSize) = BuildModules(Alphanumeric, MicroQREccLevel.L);
        (_byteModules, _byteSize) = BuildModules(Byte, MicroQREccLevel.M);

        _numericMatrix = ToBitMatrix(_numericModules, _numericSize);
        _alphanumericMatrix = ToBitMatrix(_alphanumericModules, _alphanumericSize);
        _byteMatrix = ToBitMatrix(_byteModules, _byteSize);

        VerifyEveryDecoderSucceeds();
    }

    // SkiaSharp.QrCode, encode

    [Benchmark(Baseline = true)]
    [BenchmarkCategory(SkiaEncode)]
    public MicroQRCodeData SkiaSharpQrCode_Numeric_M2_Encode()
        => MicroQRCodeGenerator.CreateMicroQRCode(Numeric.AsSpan(), MicroQREccLevel.L);

    [Benchmark]
    [BenchmarkCategory(SkiaEncode)]
    public MicroQRCodeData SkiaSharpQrCode_Alphanumeric_M3_Encode()
        => MicroQRCodeGenerator.CreateMicroQRCode(Alphanumeric.AsSpan(), MicroQREccLevel.L);

    [Benchmark]
    [BenchmarkCategory(SkiaEncode)]
    public MicroQRCodeData SkiaSharpQrCode_Byte_M4_Encode()
        => MicroQRCodeGenerator.CreateMicroQRCode(Byte.AsSpan(), MicroQREccLevel.M);

    // CodeGlyphX, encode

    [Benchmark(Baseline = true)]
    [BenchmarkCategory(GlyphEncode)]
    public CodeGlyphX.MicroQrCode CodeGlyphX_Numeric_M2_Encode()
        => CodeGlyphX.MicroQrCodeEncoder.EncodeNumeric(Numeric, CodeGlyphX.QrErrorCorrectionLevel.L);

    [Benchmark]
    [BenchmarkCategory(GlyphEncode)]
    public CodeGlyphX.MicroQrCode CodeGlyphX_Alphanumeric_M3_Encode()
        => CodeGlyphX.MicroQrCodeEncoder.EncodeAlphanumeric(Alphanumeric, CodeGlyphX.QrErrorCorrectionLevel.L);

    [Benchmark]
    [BenchmarkCategory(GlyphEncode)]
    public CodeGlyphX.MicroQrCode CodeGlyphX_Byte_M4_Encode()
        => CodeGlyphX.MicroQrCodeEncoder.EncodeText(Byte, CodeGlyphX.QrTextEncoding.Latin1, CodeGlyphX.QrErrorCorrectionLevel.M);

    // QRCoder, encode

    [Benchmark(Baseline = true)]
    [BenchmarkCategory(CoderEncode)]
    public QRCoder.QRCodeData QRCoder_Numeric_M2_Encode()
        => QRCoder.QRCodeGenerator.GenerateMicroQrCode(Numeric, QRCoder.QRCodeGenerator.ECCLevel.L);

    [Benchmark]
    [BenchmarkCategory(CoderEncode)]
    public QRCoder.QRCodeData QRCoder_Alphanumeric_M3_Encode()
        => QRCoder.QRCodeGenerator.GenerateMicroQrCode(Alphanumeric, QRCoder.QRCodeGenerator.ECCLevel.L);

    [Benchmark]
    [BenchmarkCategory(CoderEncode)]
    public QRCoder.QRCodeData QRCoder_Byte_M4_Encode()
        => QRCoder.QRCodeGenerator.GenerateMicroQrCode(Byte, QRCoder.QRCodeGenerator.ECCLevel.M);

    // SkiaSharp.QrCode, decode

    [Benchmark(Baseline = true)]
    [BenchmarkCategory(SkiaDecode)]
    public string SkiaSharpQrCode_Numeric_M2_Decode()
    {
        MicroQRCodeDecoder.TryDecode(_numericModules, _numericSize, out var text, out _);
        return text;
    }

    [Benchmark]
    [BenchmarkCategory(SkiaDecode)]
    public string SkiaSharpQrCode_Alphanumeric_M3_Decode()
    {
        MicroQRCodeDecoder.TryDecode(_alphanumericModules, _alphanumericSize, out var text, out _);
        return text;
    }

    [Benchmark]
    [BenchmarkCategory(SkiaDecode)]
    public string SkiaSharpQrCode_Byte_M4_Decode()
    {
        MicroQRCodeDecoder.TryDecode(_byteModules, _byteSize, out var text, out _);
        return text;
    }

    // CodeGlyphX, decode

    [Benchmark(Baseline = true)]
    [BenchmarkCategory(GlyphDecode)]
    public string CodeGlyphX_Numeric_M2_Decode()
    {
        CodeGlyphX.MicroQrDecoder.TryDecode(_numericMatrix, out var decoded);
        return decoded.Text;
    }

    [Benchmark]
    [BenchmarkCategory(GlyphDecode)]
    public string CodeGlyphX_Alphanumeric_M3_Decode()
    {
        CodeGlyphX.MicroQrDecoder.TryDecode(_alphanumericMatrix, out var decoded);
        return decoded.Text;
    }

    [Benchmark]
    [BenchmarkCategory(GlyphDecode)]
    public string CodeGlyphX_Byte_M4_Decode()
    {
        CodeGlyphX.MicroQrDecoder.TryDecode(_byteMatrix, out var decoded);
        return decoded.Text;
    }

    private void VerifyEveryDecoderSucceeds()
    {
        Check(nameof(SkiaSharpQrCode_Numeric_M2_Decode), Numeric, SkiaSharpQrCode_Numeric_M2_Decode());
        Check(nameof(SkiaSharpQrCode_Alphanumeric_M3_Decode), Alphanumeric, SkiaSharpQrCode_Alphanumeric_M3_Decode());
        Check(nameof(SkiaSharpQrCode_Byte_M4_Decode), Byte, SkiaSharpQrCode_Byte_M4_Decode());
        Check(nameof(CodeGlyphX_Numeric_M2_Decode), Numeric, CodeGlyphX_Numeric_M2_Decode());
        Check(nameof(CodeGlyphX_Alphanumeric_M3_Decode), Alphanumeric, CodeGlyphX_Alphanumeric_M3_Decode());
        Check(nameof(CodeGlyphX_Byte_M4_Decode), Byte, CodeGlyphX_Byte_M4_Decode());

        static void Check(string path, string expected, string? decoded)
        {
            if (decoded != expected)
            {
                throw new InvalidOperationException(
                    $"{path} did not round-trip: expected \"{expected}\", got \"{decoded ?? "<null>"}\".");
            }
        }
    }

    private static (byte[] modules, int size) BuildModules(string content, MicroQREccLevel eccLevel)
    {
        var calculated = Sizing.Required(content.AsSpan(), eccLevel, 0);
        var buffer = new byte[calculated.BufferSize];
        MicroQRCodeGenerator.CreateMicroQRCode(content.AsSpan(), eccLevel, buffer, quietZoneSize: 0);
        return (buffer, calculated.QrSize);
    }

    private static CodeGlyphX.BitMatrix ToBitMatrix(byte[] modules, int size)
    {
        var matrix = new CodeGlyphX.BitMatrix(size, size);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                matrix.Set(x, y, modules[y * size + x] != 0);
            }
        }
        return matrix;
    }
}
