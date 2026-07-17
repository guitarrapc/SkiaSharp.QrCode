using BenchmarkDotNet.Configs;

/// <summary>
/// End-to-end Micro QR matrix decoding through the public API (MicroQrCodeDecoder).
/// Payloads mirror MicroQrEncode so encode and decode costs are directly comparable;
/// a Standard QR v1 decode of the same numeric payload gives the scale reference.
/// Matrices are quiet-zone-free (the decoder's in-place fast path).
///
/// Scenarios:
///   Numeric_M2_L : M2-L (numeric capacity boundary)
///   Alphanumeric_M3_L : M3-L (alphanumeric capacity boundary)
///   Byte_M4_M : M4-M (byte capacity boundary)
/// </summary>
public class MicroQrDecodeEndToEnd
{
    private byte[] _numericModules = default!;
    private int _numericSize;
    private byte[] _alphanumericModules = default!;
    private int _alphanumericSize;
    private byte[] _byteModules = default!;
    private int _byteSize;
    private byte[] _standardModules = default!;
    private int _standardSize;
    private char[] _chars = default!;
    private char[] _standardChars = default!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        (_numericModules, _numericSize) = BuildMicro("0123456789", MicroQrEccLevel.L);          // M2-L
        (_alphanumericModules, _alphanumericSize) = BuildMicro("HELLO WORLD 14", MicroQrEccLevel.L); // M3-L
        (_byteModules, _byteSize) = BuildMicro("bytes m4 mode", MicroQrEccLevel.M);             // M4-M
        _chars = new char[MicroQrCodeDecoder.GetMaxDecodedLength(MicroQrVersion.M4)];

        var calculated = SkiaSharp.QrCode.QRCodeGenerator.GetRequiredBufferSize("0123456789", ECCLevel.L, quietZoneSize: 0);
        _standardModules = new byte[calculated.BufferSize];
        SkiaSharp.QrCode.QRCodeGenerator.CreateQrCode("0123456789", ECCLevel.L, _standardModules, quietZoneSize: 0);
        _standardSize = calculated.QrSize;
        _standardChars = new char[QRCodeDecoder.GetMaxDecodedLength(1)];
    }

    // Span destination (zero-allocation) path

    [Benchmark(Baseline = true, Description = "MicroQr_Numeric_M2_Decode (Span)")]
    public int MicroQr_Numeric_M2_DecodeSpan()
    {
        MicroQrCodeDecoder.TryDecode(_numericModules, _numericSize, _chars, out var written, out _);
        return written;
    }

    [Benchmark(Description = "MicroQr_Alphanumeric_M3_Decode (Span)")]
    public int MicroQr_Alphanumeric_M3_DecodeSpan()
    {
        MicroQrCodeDecoder.TryDecode(_alphanumericModules, _alphanumericSize, _chars, out var written, out _);
        return written;
    }

    [Benchmark(Description = "MicroQr_Byte_M4_Decode (Span)")]
    public int MicroQr_Byte_M4_DecodeSpan()
    {
        MicroQrCodeDecoder.TryDecode(_byteModules, _byteSize, _chars, out var written, out _);
        return written;
    }

    // String path (allocates the result string only)

    [Benchmark]
    public string MicroQr_Numeric_M2_Decode()
    {
        MicroQrCodeDecoder.TryDecode(_numericModules, _numericSize, out var text, out _);
        return text;
    }

    [Benchmark]
    public string MicroQr_Byte_M4_Decode()
    {
        MicroQrCodeDecoder.TryDecode(_byteModules, _byteSize, out var text, out _);
        return text;
    }

    // Standard QR version 1 with the same numeric payload, for scale reference.

    [Benchmark(Description = "StandardQr_Numeric_V1_Decode (Span)")]
    public int StandardQr_Numeric_V1_DecodeSpan()
    {
        QRCodeDecoder.TryDecode(_standardModules, _standardSize, _standardChars, out var written, out _);
        return written;
    }

    private static (byte[] modules, int size) BuildMicro(string content, MicroQrEccLevel eccLevel)
    {
        var calculated = MicroQrCodeGenerator.GetRequiredBufferSize(content.AsSpan(), eccLevel, quietZoneSize: 0);
        var buffer = new byte[calculated.BufferSize];
        MicroQrCodeGenerator.CreateMicroQrCode(content.AsSpan(), eccLevel, buffer, quietZoneSize: 0);
        return (buffer, calculated.QrSize);
    }
}
