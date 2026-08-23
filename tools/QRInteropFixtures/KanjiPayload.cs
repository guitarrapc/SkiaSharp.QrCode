using System.Text;

namespace QRInteropFixtures;

/// <summary>
/// Shift_JIS payload plumbing for Kanji-mode fixtures.
///
/// qrtool's <c>--mode kanji</c> takes the payload as raw Shift_JIS bytes (UTF-8
/// input fails with "invalid character"), so the corpus has to hand it bytes, not
/// text. For ordinary payloads CP932 produces them; for the seven cells where
/// JIS X 0208 and CP932 disagree it cannot, because .NET has no encoder for the
/// JIS X 0208 reading (U+301C is not in CP932 at all). Those cases carry their
/// bytes explicitly instead.
/// </summary>
public static class KanjiPayload
{
    public const string ModeName = "Kanji";

    private static Encoding? shiftJis;

    public static Encoding ShiftJis
    {
        get
        {
            if (shiftJis is null)
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                shiftJis = Encoding.GetEncoding(932);
            }
            return shiftJis;
        }
    }

    /// <summary>
    /// The bytes to hand an encoder for this case: the explicit ones when the case
    /// pins them, otherwise the CP932 encoding of the payload text.
    /// </summary>
    public static byte[] ToShiftJisBytes(string payloadText, string? shiftJisHex)
    {
        if (shiftJisHex is not null)
            return Convert.FromHexString(shiftJisHex);

        var bytes = ShiftJis.GetBytes(payloadText);
        // A '?' that the payload did not contain means CP932 dropped a character:
        // silently shipping it would put a fixture in the corpus that asserts the
        // wrong text, which is exactly what the corpus exists to catch.
        if (!payloadText.Contains('?') && Array.IndexOf(bytes, (byte)'?') >= 0)
            throw new InvalidOperationException($"CP932 cannot encode \"{payloadText}\"; pin the Shift_JIS bytes on the case instead.");

        return bytes;
    }
}
