using BenchmarkDotNet.Configs;

/// <summary>
/// End-to-end Micro QR matrix encoding through the public API (MicroQrCodeGenerator).
/// Used to measure the user-visible impact of internal kernel changes such as the
/// Reed-Solomon ECC encoder optimization.
///
/// Scenarios:
///   Numeric_M2_L : M2-L (numeric capacity boundary)
///   Alphanumeric_M3_L : M3-L (alphanumeric capacity boundary)
///   Byte_M4_M : M4-M (byte capacity boundary)
/// </summary>
public class MicroQrEncodeEndToend
{
    // Representative payloads: numeric M2-L, alphanumeric M3-L, byte M4-M.
    private string _numeric = default!;
    private string _alphanumeric = default!;
    private string _byte = default!;
    private byte[] _spanDestination = default!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _numeric = "0123456789";        // M2-L (numeric capacity boundary)
        _alphanumeric = "HELLO WORLD 14"; // M3-L (alphanumeric capacity boundary)
        _byte = "bytes m4 mode";        // M4-M (byte capacity boundary)
        // Sized for the largest consumer: the Standard QR v1 reference benchmark
        // (29x29 with quiet zone) exceeds every Micro QR buffer size.
        _spanDestination = new byte[Math.Max(
            MicroQrCodeGenerator.GetRequiredBufferSize(_byte.AsSpan(), MicroQrEccLevel.M).BufferSize,
            SkiaSharp.QrCode.QRCodeGenerator.GetRequiredBufferSize(_numeric.AsSpan(), ECCLevel.L).BufferSize)];
    }

    // Class API (allocates the result object only)

    [Benchmark(Baseline = true)]
    public MicroQrCodeData MicroQr_Numeric_M2_Encode()
    {
        return MicroQrCodeGenerator.CreateMicroQrCode(_numeric.AsSpan(), MicroQrEccLevel.L);
    }

    [Benchmark]
    public MicroQrCodeData MicroQr_Alphanumeric_M3_Encode()
    {
        return MicroQrCodeGenerator.CreateMicroQrCode(_alphanumeric.AsSpan(), MicroQrEccLevel.L);
    }

    [Benchmark]
    public MicroQrCodeData MicroQr_Byte_M4_Encode()
    {
        return MicroQrCodeGenerator.CreateMicroQrCode(_byte.AsSpan(), MicroQrEccLevel.M);
    }

    // Span destination (zero-allocation) variants

    [Benchmark(Description = "MicroQr_Numeric_M2_Encode (Span)")]
    public int MicroQr_Numeric_M2_EncodeSpan()
    {
        return MicroQrCodeGenerator.CreateMicroQrCode(_numeric.AsSpan(), MicroQrEccLevel.L, _spanDestination);
    }

    [Benchmark(Description = "MicroQr_Alphanumeric_M3_Encode (Span)")]
    public int MicroQr_Alphanumeric_M3_EncodeSpan()
    {
        return MicroQrCodeGenerator.CreateMicroQrCode(_alphanumeric.AsSpan(), MicroQrEccLevel.L, _spanDestination);
    }

    [Benchmark(Description = "MicroQr_Byte_M4_Encode (Span)")]
    public int MicroQr_Byte_M4_EncodeSpan()
    {
        return MicroQrCodeGenerator.CreateMicroQrCode(_byte.AsSpan(), MicroQrEccLevel.M, _spanDestination);
    }

    // Standard QR version 1 with the same numeric payload, for scale reference.

    [Benchmark(Description = "StandardQr_Numeric_V1_Encode (Span)")]
    public int StandardQr_Numeric_V1_EncodeSpan()
    {
        return SkiaSharp.QrCode.QRCodeGenerator.CreateQrCode(_numeric.AsSpan(), ECCLevel.L, _spanDestination);
    }
}
