/// <summary>
/// End-to-end rMQR matrix encoding through the public API (RmQRCodeGenerator).
/// Baseline for the future placement / bit-stream fast paths, and the guard that
/// the reference-shaped pipeline stays allocation-free on the span path.
///
/// Scenarios (requested versions pin the size so the fit search is not what is measured):
///   Numeric_R7x43_M      : smallest symbol, 12 digits (capacity boundary)
///   Alphanumeric_R11x59_M: mid symbol, 43 chars (capacity boundary)
///   Byte_R17x139_M       : largest symbol, 150 bytes (capacity boundary, 4 RS blocks)
///   Latin1_Eci_R17x139_M : explicit ECI 3 Byte segment
///   Utf8_Eci_R17x139_M   : explicit ECI 26 Byte segment
///   Numeric_AutoFit_M    : automatic version selection cost on top of the smallest symbol
///   MixedUrl_*           : what RmQRSegmentation.Optimal costs against Single on the
///                          same content, where the split does shrink the symbol
///   MaxNumeric/MaxByte_* : the two shapes Optimal cannot help with — all digits
///                          (planning short-circuited) and all bytes (planning runs, never wins)
/// </summary>
public class RmQREncodeEndToEnd
{
    private string _numeric = default!;
    private string _alphanumeric = default!;
    private string _byte = default!;
    private string _latin1 = default!;
    private string _utf8 = default!;
    private string _mixedUrl = default!;
    private string _maxNumeric = default!;
    private string _maxByte = default!;
    private byte[] _spanDestination = default!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _numeric = "012345678901";                                     // R7x43-M numeric boundary
        _alphanumeric = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 $%*+-.";   // 43 chars: R11x59-M alphanumeric boundary
        _byte = string.Concat(Enumerable.Repeat("the quick brown fox jumps over the lazy dog?! ", 4)).Substring(0, 150); // R17x139-M byte boundary
        _latin1 = string.Concat(Enumerable.Repeat("Café déjà vu. ", 8));
        _utf8 = string.Concat(Enumerable.Repeat("日本語QRコード", 5));
        _mixedUrl = "https://example.com/p/1234567890123456";        // Byte + Numeric: R11x77 as one run, R15x43 split
        _maxNumeric = new string('7', 361);                           // R17x139-M numeric boundary: planning is short-circuited
        _maxByte = new string('a', 150);                              // R17x139-M byte boundary: planning runs and never wins
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

    [Benchmark]
    public RmQRCodeData RmQR_Latin1Eci_R17x139_Encode()
    {
        return RmQRCodeGenerator.CreateRmQRCodeWithEci(_latin1.AsSpan(), RmQREccLevel.M, EciMode.Iso8859_1, RmQRVersion.R17x139);
    }

    [Benchmark]
    public RmQRCodeData RmQR_Utf8Eci_R17x139_Encode()
    {
        return RmQRCodeGenerator.CreateRmQRCodeWithEci(_utf8.AsSpan(), RmQREccLevel.M, EciMode.Utf8, RmQRVersion.R17x139);
    }

    [Benchmark]
    public RmQRCodeData RmQR_Numeric_AutoFit_Encode()
    {
        return RmQRCodeGenerator.CreateRmQRCode(_numeric.AsSpan(), RmQREccLevel.M);
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

    [Benchmark(Description = "RmQR_Latin1_ECI_R17x139_Encode (Span)")]
    public int RmQR_Latin1Eci_R17x139_EncodeSpan()
    {
        return RmQRCodeGenerator.CreateRmQRCodeWithEci(_latin1.AsSpan(), RmQREccLevel.M, _spanDestination, EciMode.Iso8859_1, RmQRVersion.R17x139);
    }

    [Benchmark(Description = "RmQR_UTF8_ECI_R17x139_Encode (Span)")]
    public int RmQR_Utf8Eci_R17x139_EncodeSpan()
    {
        return RmQRCodeGenerator.CreateRmQRCodeWithEci(_utf8.AsSpan(), RmQREccLevel.M, _spanDestination, EciMode.Utf8, RmQRVersion.R17x139);
    }

    [Benchmark(Description = "RmQR_Numeric_AutoFit_Encode (Span)")]
    public int RmQR_Numeric_AutoFit_EncodeSpan()
    {
        return RmQRCodeGenerator.CreateRmQRCode(_numeric.AsSpan(), RmQREccLevel.M, _spanDestination);
    }

    // Mixed-mode segmentation: what opting in costs, on the three shapes that answer
    // it differently — content the split shrinks (the URL, R11x77 → R15x43), content
    // that provably cannot benefit and is short-circuited (all digits), and content
    // where the split must be searched but never wins (all lowercase).

    [Benchmark(Description = "RmQR_MixedUrl_AutoFit_Encode (Span, Single)")]
    public int RmQR_MixedUrl_AutoFit_SingleEncodeSpan()
    {
        return RmQRCodeGenerator.CreateRmQRCode(_mixedUrl.AsSpan(), RmQREccLevel.M, _spanDestination);
    }

    [Benchmark(Description = "RmQR_MixedUrl_AutoFit_Encode (Span, Optimal)")]
    public int RmQR_MixedUrl_AutoFit_OptimalEncodeSpan()
    {
        return RmQRCodeGenerator.CreateRmQRCode(_mixedUrl.AsSpan(), RmQREccLevel.M, _spanDestination, segmentation: RmQRSegmentation.Optimal);
    }

    [Benchmark(Description = "RmQR_MaxNumeric_AutoFit_Encode (Span, Single)")]
    public int RmQR_MaxNumeric_AutoFit_SingleEncodeSpan()
    {
        return RmQRCodeGenerator.CreateRmQRCode(_maxNumeric.AsSpan(), RmQREccLevel.M, _spanDestination);
    }

    [Benchmark(Description = "RmQR_MaxNumeric_AutoFit_Encode (Span, Optimal)")]
    public int RmQR_MaxNumeric_AutoFit_OptimalEncodeSpan()
    {
        return RmQRCodeGenerator.CreateRmQRCode(_maxNumeric.AsSpan(), RmQREccLevel.M, _spanDestination, segmentation: RmQRSegmentation.Optimal);
    }

    [Benchmark(Description = "RmQR_MaxByte_AutoFit_Encode (Span, Single)")]
    public int RmQR_MaxByte_AutoFit_SingleEncodeSpan()
    {
        return RmQRCodeGenerator.CreateRmQRCode(_maxByte.AsSpan(), RmQREccLevel.M, _spanDestination);
    }

    [Benchmark(Description = "RmQR_MaxByte_AutoFit_Encode (Span, Optimal)")]
    public int RmQR_MaxByte_AutoFit_OptimalEncodeSpan()
    {
        return RmQRCodeGenerator.CreateRmQRCode(_maxByte.AsSpan(), RmQREccLevel.M, _spanDestination, segmentation: RmQRSegmentation.Optimal);
    }

    // Standard QR version 1 with the same numeric payload, for scale reference.

    [Benchmark(Description = "StandardQr_Numeric_V1_Encode (Span)")]
    public int StandardQr_Numeric_V1_EncodeSpan()
    {
        return SkiaSharp.QrCode.QRCodeGenerator.CreateQrCode(_numeric.AsSpan(), ECCLevel.L, _spanDestination);
    }
}
