using System.Text;

namespace QRInteropFixtures;

/// <summary>
/// The rMQR fixture corpus. Two lineages get different case lists on purpose:
///
/// zint-libzint (ASCII-only through the ZXingCpp wrapper) carries the systematic
/// sweep: every version × mode with a one-character payload (the leading data
/// codewords then pin the count-indicator width of that version/mode from the bit
/// stream, and every version appears at both ECC levels), plus capacity-boundary
/// payloads per height (padding-free streams).
///
/// qrtool (Rust qrcode2 crate) carries one capacity-boundary case per version
/// with rotating mode / ECC, plus the UTF-8 / Japanese byte-mode cases (no ECI;
/// readers detect UTF-8 heuristically or expose raw bytes).
///
/// Payloads are deterministic (fixed literals or fixed cyclic patterns) so
/// regeneration is byte-reproducible for a given generator version.
/// </summary>
public static class RmQRCorpus
{
    private const string NumericAlphabet = "0123456789";
    private const string AlphanumericAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 $%*+-./:";
    private const string ByteAlphabet = "the quick brown fox jumps over the lazy dog?! ";

    public static readonly RmQRFixtureCaseDefinition[] ZintCases = BuildZintCases();
    public static readonly RmQRFixtureCaseDefinition[] QrtoolCases = BuildQrtoolCases();

    private static RmQRFixtureCaseDefinition[] BuildZintCases()
    {
        var cases = new List<RmQRFixtureCaseDefinition>();

        // Systematic single-character sweep: pins count-indicator widths (N/A/B per
        // version) and format information (both ECC levels per version).
        foreach (var v in RmQRVersionTable.Entries)
        {
            var byteEcc = v.Index % 2 == 0 ? "M" : "H";
            cases.Add(new($"{Slug(v)}-m-numeric-1", "1", "M", v.Height, v.Width, "Numeric"));
            cases.Add(new($"{Slug(v)}-h-alphanumeric-1", "A", "H", v.Height, v.Width, "Alphanumeric"));
            cases.Add(new($"{Slug(v)}-{byteEcc.ToLowerInvariant()}-byte-1", "a", byteEcc, v.Height, v.Width, "Byte"));
        }

        // Capacity boundaries per height: numeric max at M on the widest width,
        // byte max at H on the narrowest width (padding-free, multi-block for the
        // large sizes).
        foreach (var height in new[] { 7, 9, 11, 13, 15, 17 })
        {
            var ofHeight = RmQRVersionTable.Entries.Where(e => e.Height == height).ToArray();
            var widest = ofHeight[^1];
            var narrowest = ofHeight[0];
            cases.Add(new($"{Slug(widest)}-m-numeric-max", Cyclic(NumericAlphabet, widest.NumericM), "M", widest.Height, widest.Width, "Numeric"));
            cases.Add(new($"{Slug(narrowest)}-h-byte-max", Cyclic(ByteAlphabet, narrowest.ByteH), "H", narrowest.Height, narrowest.Width, "Byte"));
        }

        return [.. cases];
    }

    private static RmQRFixtureCaseDefinition[] BuildQrtoolCases()
    {
        var cases = new List<RmQRFixtureCaseDefinition>();

        // One capacity-boundary case per version, mode and ECC rotating so every
        // mode × ECC pair appears across the sweep.
        foreach (var v in RmQRVersionTable.Entries)
        {
            var mode = (v.Index % 3) switch { 0 => "Numeric", 1 => "Alphanumeric", _ => "Byte" };
            var ecc = (v.Index / 3) % 2 == 0 ? "M" : "H";
            var alphabet = mode switch { "Numeric" => NumericAlphabet, "Alphanumeric" => AlphanumericAlphabet, _ => ByteAlphabet };
            cases.Add(new($"{Slug(v)}-{ecc.ToLowerInvariant()}-{mode.ToLowerInvariant()}-max", Cyclic(alphabet, v.Capacity(ecc, mode)), ecc, v.Height, v.Width, mode));
        }

        // UTF-8 byte payloads (no ECI): smallest byte capacities and a long text.
        cases.Add(new("r7x43-m-utf8-japanese", "こ", "M", 7, 43, "Byte", Utf8: true));                    // 3 bytes of 5
        cases.Add(new("r11x27-h-utf8-japanese", "あ", "H", 11, 27, "Byte", Utf8: true));                  // 3 bytes of 4
        cases.Add(new("r13x59-m-utf8-japanese", "こんにちは世界", "M", 13, 59, "Byte", Utf8: true));      // 21 bytes of 36
        cases.Add(new("r17x139-h-utf8-mixed", "rMQR 矩形コード ✓ naïve café", "H", 17, 139, "Byte", Utf8: true));

        return [.. cases];
    }

    private static string Slug(RmQRVersionTable.Entry v) => $"r{v.Height}x{v.Width}";

    private static string Cyclic(string alphabet, int length)
    {
        var sb = new StringBuilder(length);
        for (var i = 0; i < length; i++)
            sb.Append(alphabet[i % alphabet.Length]);
        return sb.ToString();
    }
}
