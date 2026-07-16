using System.Text;

namespace QrInteropFixtures;

/// <summary>
/// The deterministic Standard QR fixture corpus. Cases are chosen to cover every mode
/// (Numeric / Alphanumeric / Byte) and ECC level, plus capacity boundaries, ECI (UTF-8)
/// payloads, and the smallest/largest versions. Everything is a fixed literal or a
/// fixed repetition — no randomness, no timestamps — so regeneration is reproducible.
/// </summary>
public static class StandardQrCorpus
{
    public static IReadOnlyList<FixtureCaseDefinition> Cases { get; } = Build();

    private static List<FixtureCaseDefinition> Build()
    {
        var cases = new List<FixtureCaseDefinition>();

        // Every mode × every ECC level at small versions.
        foreach (var ecc in new[] { "L", "M", "Q", "H" })
        {
            var suffix = ecc.ToLowerInvariant();
            cases.Add(new($"numeric-10digits-{suffix}", "1234567890", ecc));
            cases.Add(new($"alphanumeric-helloworld-{suffix}", "HELLO WORLD", ecc));
            cases.Add(new($"byte-url-{suffix}", "https://example.com/path?query=value&lang=en", ecc));
        }

        // Mid-size and maximum-capacity versions.
        cases.Add(new("numeric-500digits-m", Digits(500), "M"));
        cases.Add(new("numeric-500digits-h", Digits(500), "H"));
        cases.Add(new("numeric-7089digits-l", Digits(7089), "L")); // exactly version 40-L numeric capacity
        cases.Add(new("byte-long-sentence-m", RepeatSentence(900), "M"));

        // Alphanumeric charset coverage and version 1-L capacity boundary (25 chars).
        cases.Add(new("alphanumeric-fullset-m", "ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:0123456789", "M"));
        cases.Add(new("alphanumeric-25chars-v1-boundary-l", "HELLO WORLD 0123456789 :A", "L"));

        // UTF-8 (ECI) payloads.
        cases.Add(new("byte-utf8-japanese-m", "こんにちは世界、QRコードのテストです。", "M", Utf8: true));
        cases.Add(new("byte-utf8-japanese-h", "こんにちは世界、QRコードのテストです。", "H", Utf8: true));
        cases.Add(new("byte-utf8-emoji-m", "Emoji 🎌 test ✅ done", "M", Utf8: true));

        return cases;
    }

    private static string Digits(int count)
    {
        var sb = new StringBuilder(count);
        for (var i = 0; i < count; i++)
        {
            sb.Append((char)('0' + (i % 10)));
        }
        return sb.ToString();
    }

    private static string RepeatSentence(int minLength)
    {
        const string Sentence = "The quick brown fox jumps over the lazy dog 0123456789. ";
        var sb = new StringBuilder(minLength + Sentence.Length);
        while (sb.Length < minLength)
        {
            sb.Append(Sentence);
        }
        return sb.ToString();
    }
}
