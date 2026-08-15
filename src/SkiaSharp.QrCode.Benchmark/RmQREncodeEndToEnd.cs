/// <summary>
/// End-to-end rMQR matrix encoding through the public API (RmQRCodeGenerator).
/// Baseline for the future placement / bit-stream fast paths, and the guard that
/// the reference-shaped pipeline stays allocation-free on the span path.
///
/// Scenarios (requested versions pin the size so the fit search is not what is measured):
///   Numeric_R7x43_M      : smallest symbol, 12 digits (capacity boundary)
///   Alphanumeric_R11x59_M: mid symbol, 43 chars (capacity boundary)
///   Byte_R17x139_M       : largest symbol, 150 bytes (capacity boundary, 4 RS blocks)
///   Numeric_AutoFit_M    : automatic version selection cost on top of the smallest symbol
/// </summary>
public class RmQREncodeEndToEnd
{
    private string _numeric = default!;
    private string _alphanumeric = default!;
    private string _byte = default!;
    private byte[] _spanDestination = default!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _numeric = "012345678901";                                     // R7x43-M numeric boundary
        _alphanumeric = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 $%*+-.";   // 43 chars: R11x59-M alphanumeric boundary
        _byte = string.Concat(Enumerable.Repeat("the quick brown fox jumps over the lazy dog?! ", 4)).Substring(0, 150); // R17x139-M byte boundary
        _spanDestination = new byte[Math.Max(
            RmQRCodeGenerator.GetRequiredBufferSize(_byte.AsSpan(), RmQREccLevel.M, RmQRVersion.R17x139).BufferSize,
            SkiaSharp.QrCode.QRCodeGenerator.GetRequiredBufferSize(_numeric.AsSpan(), ECCLevel.L).BufferSize)];
    }

    // Class API (allocates the result object only)

    [Benchmark(Baseline = true)]
    public RmQRCodeData RmQR_Numeric_R7x43_Encode()
    {
        return RmQRCodeGenerator.CreateRmQRCode(_numeric.AsSpan(), RmQREccLevel.M, RmQRVersion.R7x43);
    }

    [Benchmark]
    public RmQRCodeData RmQR_Alphanumeric_R11x59_Encode()
    {
        return RmQRCodeGenerator.CreateRmQRCode(_alphanumeric.AsSpan(), RmQREccLevel.M, RmQRVersion.R11x59);
    }

    [Benchmark]
    public RmQRCodeData RmQR_Byte_R17x139_Encode()
    {
        return RmQRCodeGenerator.CreateRmQRCode(_byte.AsSpan(), RmQREccLevel.M, RmQRVersion.R17x139);
    }

    // Span destination (zero-allocation) variants

    [Benchmark(Description = "RmQR_Numeric_R7x43_Encode (Span)")]
    public int RmQR_Numeric_R7x43_EncodeSpan()
    {
        return RmQRCodeGenerator.CreateRmQRCode(_numeric.AsSpan(), RmQREccLevel.M, _spanDestination, RmQRVersion.R7x43);
    }

    [Benchmark(Description = "RmQR_Alphanumeric_R11x59_Encode (Span)")]
    public int RmQR_Alphanumeric_R11x59_EncodeSpan()
    {
        return RmQRCodeGenerator.CreateRmQRCode(_alphanumeric.AsSpan(), RmQREccLevel.M, _spanDestination, RmQRVersion.R11x59);
    }

    [Benchmark(Description = "RmQR_Byte_R17x139_Encode (Span)")]
    public int RmQR_Byte_R17x139_EncodeSpan()
    {
        return RmQRCodeGenerator.CreateRmQRCode(_byte.AsSpan(), RmQREccLevel.M, _spanDestination, RmQRVersion.R17x139);
    }

    [Benchmark(Description = "RmQR_Numeric_AutoFit_Encode (Span)")]
    public int RmQR_Numeric_AutoFit_EncodeSpan()
    {
        return RmQRCodeGenerator.CreateRmQRCode(_numeric.AsSpan(), RmQREccLevel.M, _spanDestination);
    }

    // Standard QR version 1 with the same numeric payload, for scale reference.

    [Benchmark(Description = "StandardQr_Numeric_V1_Encode (Span)")]
    public int StandardQr_Numeric_V1_EncodeSpan()
    {
        return SkiaSharp.QrCode.QRCodeGenerator.CreateQrCode(_numeric.AsSpan(), ECCLevel.L, _spanDestination);
    }
}
