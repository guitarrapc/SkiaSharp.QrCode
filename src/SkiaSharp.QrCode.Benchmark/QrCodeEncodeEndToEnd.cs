using System.Text;

/// <summary>
/// End-to-end QR generation through the public API (QRCodeGenerator.CreateQrCode).
/// Used to measure the user-visible impact of internal kernel changes such as the
/// Reed-Solomon ECC encoder optimization.
///
/// Scenarios cover the version/ECC spread:
///   Short_M : tiny payload, version ~1-2 (per-call overhead dominated)
///   Url_M   : typical URL payload, version ~4-5
///   Long_L  : ~2900 chars -> version 40, ECC level L (largest data blocks)
///   Long_H  : ~1200 chars -> version 40, ECC level H (81 blocks x 30 ecc, max ECC share)
/// </summary>
public class QrCodeEncodeEndToEnd
{
    private string _short = default!;
    private string _url = default!;
    private string _longL = default!;
    private string _longH = default!;

    [GlobalSetup]
    public void Setup()
    {
        _short = "HELLO WORLD 2026";
        _url = "https://github.com/guitarrapc/SkiaSharp.QrCode/blob/main/README.md?foo=sample&bar=dummy";
        _longL = BuildDeterministicText(2900); // fits version 40-L byte mode (max 2953)
        _longH = BuildDeterministicText(1200); // fits version 40-H byte mode (max 1273)
    }

    [Benchmark]
    public QRCodeData Short_M() => QRCodeGenerator.CreateQrCode(_short, ECCLevel.M);

    [Benchmark]
    public QRCodeData Url_M() => QRCodeGenerator.CreateQrCode(_url, ECCLevel.M);

    [Benchmark]
    public QRCodeData Long_L() => QRCodeGenerator.CreateQrCode(_longL, ECCLevel.L);

    [Benchmark]
    public QRCodeData Long_H() => QRCodeGenerator.CreateQrCode(_longH, ECCLevel.H);

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
