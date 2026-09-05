using System.Text;

/// <summary>
/// End-to-end QR matrix encoding through the public API (QRCodeGenerator).
/// Used to measure the user-visible impact of internal kernel changes such as the
/// Reed-Solomon ECC encoder optimization.
///
/// Scenarios:
///   Numeric_V1_L : version 1, numeric mode (digits only)
///   Alphanumeric_V1_M : version 1, alphanumeric mode (uppercase / punctuation subset)
///   Byte_Url_V6_M : version 6, byte mode (typical URL with lowercase)
///   Byte_V20_M : version 20-M, byte mode (mid-size, exercises the 2-word SoA mask tier)
///   Byte_V40_L : version 40-L, byte mode (largest data blocks)
///   Byte_V40_H : version 40-H, byte mode (81 blocks x 30 ecc, max ECC share)
/// </summary>
public class QRCodeEncodeEndToEnd
{
    private string _numeric = default!;
    private string _alphanumeric = default!;
    private string _byteUrl = default!;
    private string _byteMidM = default!;
    private string _byteLongL = default!;
    private string _byteLongH = default!;
    private byte[] _spanDestination = default!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _numeric = "0123456789"; // version 1-L, numeric mode
        _alphanumeric = "HELLO WORLD 2026"; // version 1-M, alphanumeric mode
        _byteUrl = "https://github.com/guitarrapc/FeatherQR/blob/main/README.md?foo=sample&bar=dummy"; // version 6-M, byte mode
        _byteMidM = BuildDeterministicText(620); // version 20-M byte mode (max 666)
        _byteLongL = BuildDeterministicText(2900); // version 40-L byte mode (max 2953)
        _byteLongH = BuildDeterministicText(1200); // version 40-H byte mode (max 1273)
        _spanDestination = new byte[Math.Max(
            Sizing.Required(_byteLongL.AsSpan(), ECCLevel.L).BufferSize,
            Sizing.Required(_numeric.AsSpan(), MicroQREccLevel.L).BufferSize)];
    }

    // Class API (allocates the result object only)

    [Benchmark(Baseline = true)]
    public QRCodeData QR_Numeric_V1_L_Encode()
    {
        return QRCodeGenerator.CreateQrCode(_numeric.AsSpan(), ECCLevel.L);
    }

    [Benchmark]
    public QRCodeData QR_Alphanumeric_V1_M_Encode()
    {
        return QRCodeGenerator.CreateQrCode(_alphanumeric.AsSpan(), ECCLevel.M);
    }

    [Benchmark]
    public QRCodeData QR_Byte_Url_V6_M_Encode()
    {
        return QRCodeGenerator.CreateQrCode(_byteUrl.AsSpan(), ECCLevel.M);
    }

    [Benchmark]
    public QRCodeData QR_Byte_V20_M_Encode()
    {
        return QRCodeGenerator.CreateQrCode(_byteMidM.AsSpan(), ECCLevel.M);
    }

    [Benchmark]
    public QRCodeData QR_Byte_V40_L_Encode()
    {
        return QRCodeGenerator.CreateQrCode(_byteLongL.AsSpan(), ECCLevel.L);
    }

    [Benchmark]
    public QRCodeData QR_Byte_V40_H_Encode()
    {
        return QRCodeGenerator.CreateQrCode(_byteLongH.AsSpan(), ECCLevel.H);
    }

    // Span destination (zero-allocation) variants

    [Benchmark(Description = "QR_Numeric_V1_L_Encode (Span)")]
    public int QR_Numeric_V1_L_EncodeSpan()
    {
        return QRCodeGenerator.CreateQrCode(_numeric.AsSpan(), ECCLevel.L, _spanDestination);
    }

    [Benchmark(Description = "QR_Alphanumeric_V1_M_Encode (Span)")]
    public int QR_Alphanumeric_V1_M_EncodeSpan()
    {
        return QRCodeGenerator.CreateQrCode(_alphanumeric.AsSpan(), ECCLevel.M, _spanDestination);
    }

    [Benchmark(Description = "QR_Byte_Url_V6_M_Encode (Span)")]
    public int QR_Byte_Url_V6_M_EncodeSpan()
    {
        return QRCodeGenerator.CreateQrCode(_byteUrl.AsSpan(), ECCLevel.M, _spanDestination);
    }

    [Benchmark(Description = "QR_Byte_V40_L_Encode (Span)")]
    public int QR_Byte_V40_L_EncodeSpan()
    {
        return QRCodeGenerator.CreateQrCode(_byteLongL.AsSpan(), ECCLevel.L, _spanDestination);
    }

    [Benchmark(Description = "QR_Byte_V40_H_Encode (Span)")]
    public int QR_Byte_V40_H_EncodeSpan()
    {
        return QRCodeGenerator.CreateQrCode(_byteLongH.AsSpan(), ECCLevel.H, _spanDestination);
    }

    // Micro QR M2-L with the same numeric payload, for scale reference.

    [Benchmark(Description = "MicroQR_Numeric_M2_Encode (Span)")]
    public int MicroQR_Numeric_M2_EncodeSpan()
    {
        return MicroQRCodeGenerator.CreateMicroQRCode(_numeric.AsSpan(), MicroQREccLevel.L, _spanDestination);
    }

    // The options overloads against the parameter list overloads they forward to. They are
    // single-expression forwarders, so these pairs should sit on top of each other; the
    // point is to keep that a measured fact, since the options overloads are the path new
    // options are added to and callers will be pointed at.

    [Benchmark(Description = "QR_Byte_V40_L_Encode (Span, options)")]
    public int QR_Byte_V40_L_EncodeSpanOptions()
    {
        return QRCodeGenerator.CreateQrCode(_byteLongL.AsSpan(), ECCLevel.L, _spanDestination, QRCodeGeneratorOptions.Default);
    }

    [Benchmark(Description = "QR_Numeric_V1_L_Encode (options)")]
    public QRCodeData QR_Numeric_V1_L_EncodeOptions()
    {
        return QRCodeGenerator.CreateQrCode(_numeric, ECCLevel.L, QRCodeGeneratorOptions.Default);
    }

    [Benchmark(Description = "MicroQR_Numeric_M2_Encode (Span, options)")]
    public int MicroQR_Numeric_M2_EncodeSpanOptions()
    {
        return MicroQRCodeGenerator.CreateMicroQRCode(_numeric.AsSpan(), MicroQREccLevel.L, _spanDestination, MicroQRCodeGeneratorOptions.Default);
    }

    private static string BuildDeterministicText(int length)
    {
        var sb = new StringBuilder(length);
        var rng = new Random(42);
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,:/?&=-_";
        for (var i = 0; i < length; i++)
        {
            sb.Append(alphabet[rng.Next(alphabet.Length)]);
        }
        return sb.ToString();
    }
}
