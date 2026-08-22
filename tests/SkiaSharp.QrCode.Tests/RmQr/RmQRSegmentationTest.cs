using SkiaSharp.QrCode;
using SkiaSharp.QrCode.Image;
using SkiaSharp.QrCode.Internals.RmQr;

namespace SkiaSharp.QrCode.Tests;

/// <summary>
/// <see cref="RmQRSegmentation.Optimal"/> end to end. The three properties every
/// case is held to are: the symbol is never larger than the single-mode default, it
/// still decodes to the original content, and when it lands on the same version as
/// the single-mode default it is byte-for-byte the same matrix (the blast radius
/// bound the feature is designed around). The rest covers the branches of the
/// version decision: requested version present or absent, height constrained or not,
/// each fit strategy, each charset, and both directions of "does not fit".
/// </summary>
public class RmQRSegmentationTest
{
    private static int Area(RmQRCodeData data) => (data.Width - 4) * (data.Height - 4);

    /// <summary>
    /// Contents spanning the equivalence classes of the decision: single-mode only
    /// (no gain possible), mixed with a gain, mixed without a gain, every charset,
    /// and the degenerate lengths.
    /// </summary>
    public static IEnumerable<string> Corpus() =>
    [
        "0",
        "A",
        "a",
        "1234567890",
        "ABCDEFGHIJ",
        "abcdefghij",
        "123Abc",
        "https://example.com/p/1234567890123456",
        "https://example.com/",
        "ABC-1234567890123456",
        "ORDER 12345 ITEM 6789",
        "a1b2c3d4e5f6g7h8i9",
        "12A34B56C78D90E",
        "SN/2024-000123456789",
        "AAAAAAAAAA1111111111AAAAAAAAAA",
        "x1234567890123456789012345678901234567890",
        "1234567890123456789012345678901234567890x",
        "$%*+-./: 0123456789",
        "éèê1234567890",
        "日本語1234567890",
        "こんにちは",
        "😀😁1234567890",
        "é😀A1",
    ];

    public static IEnumerable<RmQRFitStrategy> Strategies() => Enum.GetValues<RmQRFitStrategy>();

    // -----------------------------------------------------------------
    // Properties that must hold for every content, level and strategy
    // -----------------------------------------------------------------

    [Test]
    [MethodDataSource(nameof(Corpus))]
    public async Task Optimal_IsNeverLargerThanSingle_AndAlwaysRoundTrips(string content)
    {
        foreach (var ecc in new[] { RmQREccLevel.M, RmQREccLevel.H })
        {
            foreach (var strategy in Strategies())
            {
                var single = RmQRCodeGenerator.CreateRmQRCode(content, ecc, fitStrategy: strategy);
                var optimal = RmQRCodeGenerator.CreateRmQRCode(content, ecc, fitStrategy: strategy, segmentation: RmQRSegmentation.Optimal);

                await Assert.That(Area(optimal)).IsLessThanOrEqualTo(Area(single));
                await Assert.That(RmQRCodeDecoder.TryDecode(optimal, out var decoded, out var info)).IsTrue();
                await Assert.That(decoded).IsEqualTo(content);
                await Assert.That(info.Version).IsEqualTo(optimal.Version);
                await Assert.That(info.EccLevel).IsEqualTo(ecc);
            }
        }
    }

    /// <summary>
    /// Whenever segmentation does not shrink the symbol the emitted matrix has to be
    /// the single-mode one, unchanged. This is what keeps the option from perturbing
    /// existing output.
    /// </summary>
    [Test]
    [MethodDataSource(nameof(Corpus))]
    public async Task Optimal_SameVersionAsSingle_ProducesTheIdenticalMatrix(string content)
    {
        foreach (var ecc in new[] { RmQREccLevel.M, RmQREccLevel.H })
        {
            foreach (var strategy in Strategies())
            {
                var single = RmQRCodeGenerator.CreateRmQRCode(content, ecc, fitStrategy: strategy);
                var optimal = RmQRCodeGenerator.CreateRmQRCode(content, ecc, fitStrategy: strategy, segmentation: RmQRSegmentation.Optimal);
                if (optimal.Version != single.Version)
                    continue;

                await Assert.That(optimal.GetRawData()).IsEquivalentTo(single.GetRawData());
            }
        }
    }

    [Test]
    public async Task Optimal_IsNotTheDefault()
    {
        const string content = "https://example.com/p/1234567890123456";
        var implicitDefault = RmQRCodeGenerator.CreateRmQRCode(content, RmQREccLevel.M);
        var explicitSingle = RmQRCodeGenerator.CreateRmQRCode(content, RmQREccLevel.M, segmentation: RmQRSegmentation.Single);

        await Assert.That(implicitDefault.Version).IsEqualTo(explicitSingle.Version);
        await Assert.That(implicitDefault.GetRawData()).IsEquivalentTo(explicitSingle.GetRawData());
    }

    // -----------------------------------------------------------------
    // The gain itself
    // -----------------------------------------------------------------

    [Test]
    public async Task Optimal_UrlWithNumericTail_SelectsSmallerSymbolThanSingle()
    {
        // 22 Byte-mode characters followed by 16 digits: Byte-only needs 313 bits
        // (R11x77), splitting into Byte + Numeric needs 249 (R15x43).
        const string content = "https://example.com/p/1234567890123456";

        var single = RmQRCodeGenerator.CreateRmQRCode(content, RmQREccLevel.M);
        var optimal = RmQRCodeGenerator.CreateRmQRCode(content, RmQREccLevel.M, segmentation: RmQRSegmentation.Optimal);

        await Assert.That(single.Version).IsEqualTo(RmQRVersion.R11x77);
        await Assert.That(optimal.Version).IsEqualTo(RmQRVersion.R15x43);
        await Assert.That(RmQRCodeDecoder.TryDecode(optimal, out var decoded)).IsTrue();
        await Assert.That(decoded).IsEqualTo(content);
    }

    /// <summary>Contents verified to cross a version boundary once the modes are mixed.</summary>
    [Test]
    [Arguments("Order 12345 item 6789", RmQRVersion.R13x43, RmQRVersion.R9x59)]
    [Arguments("ABC-1234567890123456", RmQRVersion.R11x43, RmQRVersion.R13x27)]
    [Arguments("x1234567890123456789012345678901234567890", RmQRVersion.R11x77, RmQRVersion.R9x59)]
    [Arguments("1234567890123456789012345678901234567890x", RmQRVersion.R11x77, RmQRVersion.R9x59)]
    [Arguments("日本語1234567890", RmQRVersion.R13x43, RmQRVersion.R11x43)]
    [Arguments("éèê1234567890", RmQRVersion.R11x43, RmQRVersion.R13x27)]
    public async Task Optimal_MixedContent_ShrinksTheSymbol(string content, RmQRVersion expectedSingle, RmQRVersion expectedOptimal)
    {
        var single = RmQRCodeGenerator.CreateRmQRCode(content, RmQREccLevel.M);
        var optimal = RmQRCodeGenerator.CreateRmQRCode(content, RmQREccLevel.M, segmentation: RmQRSegmentation.Optimal);

        await Assert.That(single.Version).IsEqualTo(expectedSingle);
        await Assert.That(optimal.Version).IsEqualTo(expectedOptimal);
        await Assert.That(Area(optimal)).IsLessThan(Area(single));
        await Assert.That(RmQRCodeDecoder.TryDecode(optimal, out var decoded)).IsTrue();
        await Assert.That(decoded).IsEqualTo(content);
    }

    /// <summary>Content whose single mode is already alphanumeric gains nothing from mixing.</summary>
    [Test]
    [Arguments("ORDER 12345 ITEM 6789")]
    [Arguments("SN/2024-000123456789")]
    [Arguments("$%*+-./: 0123456789")]
    public async Task Optimal_AlphanumericContent_KeepsTheSingleModeSymbol(string content)
    {
        var single = RmQRCodeGenerator.CreateRmQRCode(content, RmQREccLevel.M);
        var optimal = RmQRCodeGenerator.CreateRmQRCode(content, RmQREccLevel.M, segmentation: RmQRSegmentation.Optimal);

        await Assert.That(optimal.Version).IsEqualTo(single.Version);
        await Assert.That(optimal.GetRawData()).IsEquivalentTo(single.GetRawData());
    }

    /// <summary>Content that a single mode already encodes optimally must not move.</summary>
    [Test]
    [Arguments("1234567890")]
    [Arguments("ABCDEFGHIJ")]
    [Arguments("abcdefghij")]
    [Arguments("こんにちは")]
    public async Task Optimal_SingleModeContent_KeepsTheSameVersion(string content)
    {
        var single = RmQRCodeGenerator.CreateRmQRCode(content, RmQREccLevel.M);
        var optimal = RmQRCodeGenerator.CreateRmQRCode(content, RmQREccLevel.M, segmentation: RmQRSegmentation.Optimal);
        await Assert.That(optimal.Version).IsEqualTo(single.Version);
    }

    // -----------------------------------------------------------------
    // Requested version
    // -----------------------------------------------------------------

    [Test]
    public async Task Optimal_RequestedVersion_FitsWhereSingleModeDoesNot()
    {
        // R7x99-M holds 27 Byte-mode characters; this is 30 characters as Byte but
        // fits once the digit tail becomes a Numeric run.
        const string content = "abcdefghij1234567890abcdefghij";
        const RmQRVersion version = RmQRVersion.R7x99;

        await Assert.That(() => RmQRCodeGenerator.CreateRmQRCode(content, RmQREccLevel.M, version)).Throws<ArgumentException>();

        var optimal = RmQRCodeGenerator.CreateRmQRCode(content, RmQREccLevel.M, version, segmentation: RmQRSegmentation.Optimal);
        await Assert.That(optimal.Version).IsEqualTo(version);
        await Assert.That(RmQRCodeDecoder.TryDecode(optimal, out var decoded)).IsTrue();
        await Assert.That(decoded).IsEqualTo(content);
    }

    [Test]
    public async Task Optimal_RequestedVersion_SingleModeFits_ProducesTheIdenticalMatrix()
    {
        const string content = "https://example.com/p/1234567890123456";
        const RmQRVersion version = RmQRVersion.R17x139;

        var single = RmQRCodeGenerator.CreateRmQRCode(content, RmQREccLevel.M, version);
        var optimal = RmQRCodeGenerator.CreateRmQRCode(content, RmQREccLevel.M, version, segmentation: RmQRSegmentation.Optimal);

        await Assert.That(optimal.GetRawData()).IsEquivalentTo(single.GetRawData());
    }

    [Test]
    public async Task Optimal_RequestedVersion_TooLongEvenSegmented_ThrowsLikeSingle()
    {
        var content = new string('a', 60) + "1234567890";
        var single = Assert.Throws<ArgumentException>(() => RmQRCodeGenerator.CreateRmQRCode(content, RmQREccLevel.M, RmQRVersion.R7x43));
        var optimal = Assert.Throws<ArgumentException>(() => RmQRCodeGenerator.CreateRmQRCode(content, RmQREccLevel.M, RmQRVersion.R7x43, segmentation: RmQRSegmentation.Optimal));

        await Assert.That(optimal!.Message).IsEqualTo(single!.Message);
    }

    [Test]
    public async Task Optimal_TooLongForEveryVersion_ThrowsLikeSingle()
    {
        var content = new string('a', 400);
        var single = Assert.Throws<ArgumentException>(() => RmQRCodeGenerator.CreateRmQRCode(content, RmQREccLevel.M));
        var optimal = Assert.Throws<ArgumentException>(() => RmQRCodeGenerator.CreateRmQRCode(content, RmQREccLevel.M, segmentation: RmQRSegmentation.Optimal));

        await Assert.That(optimal!.Message).IsEqualTo(single!.Message);
    }

    /// <summary>
    /// The single-mode fit is only a ceiling when there is one. Content that overflows
    /// every version in one mode can still fit once the modes are mixed, and rejecting
    /// it would fail exactly where the option is worth the most.
    /// </summary>
    [Test]
    public async Task Optimal_TooLongForEverySingleMode_StillEncodesWhenMixedFits()
    {
        // 200 characters: 200 bytes as one Byte run, 50 over the 150 R17x139-M holds,
        // but 811 + 346 = 1157 bits of the 1216 available once the digits split off.
        var content = new string('a', 100) + new string('7', 100);

        await Assert.That(() => RmQRCodeGenerator.CreateRmQRCode(content, RmQREccLevel.M)).Throws<ArgumentException>();

        var optimal = RmQRCodeGenerator.CreateRmQRCode(content, RmQREccLevel.M, segmentation: RmQRSegmentation.Optimal);
        await Assert.That(optimal.Version).IsEqualTo(RmQRVersion.R17x139);
        await Assert.That(RmQRCodeDecoder.TryDecode(optimal, out var decoded)).IsTrue();
        await Assert.That(decoded).IsEqualTo(content);
    }

    /// <summary>
    /// The constrained candidate set has its own capacity ceiling: R7x139-M is 352
    /// bits, so the fixture must be a shape that overflows 42 Byte-mode characters yet
    /// costs under 352 bits when split (89 + 210 here), not merely a long one.
    /// </summary>
    [Test]
    [Arguments(10, 60)]
    [Arguments(10, 40)]
    [Arguments(5, 50)]
    [Arguments(20, 40)]
    public async Task Optimal_TooLongForEverySingleMode_HonoursTheHeightConstraint(int letters, int digits)
    {
        var content = new string('a', letters) + new string('7', digits);

        await Assert.That(() => RmQRCodeGenerator.CreateRmQRCode(content, RmQREccLevel.M, height: RmQRHeight.H7)).Throws<ArgumentException>();

        var optimal = RmQRCodeGenerator.CreateRmQRCode(content, RmQREccLevel.M, height: RmQRHeight.H7, segmentation: RmQRSegmentation.Optimal);
        await Assert.That(optimal.Height - 4).IsEqualTo(7);
        await Assert.That(RmQRCodeDecoder.TryDecode(optimal, out var decoded)).IsTrue();
        await Assert.That(decoded).IsEqualTo(content);
    }

    /// <summary>
    /// The mixed-only path with no single-mode ceiling, across the remaining
    /// strategies and charsets: those combinations only had coverage where a
    /// single-mode fit existed to bound the scan.
    /// </summary>
    [Test]
    public async Task Optimal_TooLongForEverySingleMode_AcrossStrategiesAndCharsets()
    {
        var ascii = new string('a', 100) + new string('7', 100);
        var latin1 = new string('é', 60) + new string('7', 100);
        var utf8 = new string('あ', 30) + new string('7', 120);

        foreach (var strategy in Strategies())
        {
            await Assert.That(() => RmQRCodeGenerator.CreateRmQRCode(ascii, RmQREccLevel.M, fitStrategy: strategy)).Throws<ArgumentException>();
            var optimal = RmQRCodeGenerator.CreateRmQRCode(ascii, RmQREccLevel.M, fitStrategy: strategy, segmentation: RmQRSegmentation.Optimal);
            await Assert.That(RmQRCodeDecoder.TryDecode(optimal, out var decoded)).IsTrue();
            await Assert.That(decoded).IsEqualTo(ascii);
        }

        foreach (var (content, eci) in new[] { (latin1, EciMode.Iso8859_1), (utf8, EciMode.Utf8) })
        {
            await Assert.That(() => RmQRCodeGenerator.CreateRmQRCodeWithEci(content, RmQREccLevel.M, eci)).Throws<ArgumentException>();
            var optimal = RmQRCodeGenerator.CreateRmQRCodeWithEci(content, RmQREccLevel.M, eci, segmentation: RmQRSegmentation.Optimal);
            await Assert.That(RmQRCodeDecoder.TryDecode(optimal, out var decoded)).IsTrue();
            await Assert.That(decoded).IsEqualTo(content);
        }
    }

    [Test]
    public async Task Optimal_TooLongForEverySingleMode_AgreesWithGetRequiredBufferSize()
    {
        var content = new string('a', 100) + new string('7', 100);
        var size = RmQRCodeGenerator.GetRequiredBufferSize(content.AsSpan(), RmQREccLevel.M, segmentation: RmQRSegmentation.Optimal);
        var data = RmQRCodeGenerator.CreateRmQRCode(content, RmQREccLevel.M, segmentation: RmQRSegmentation.Optimal);

        await Assert.That(size.Version).IsEqualTo(data.Version);
        await Assert.That(size.BufferSize).IsEqualTo(data.Width * data.Height);
    }

    // -----------------------------------------------------------------
    // Height and strategy constraints
    // -----------------------------------------------------------------

    [Test]
    [Arguments(RmQRHeight.H7)]
    [Arguments(RmQRHeight.H9)]
    [Arguments(RmQRHeight.H11)]
    [Arguments(RmQRHeight.H13)]
    [Arguments(RmQRHeight.H15)]
    [Arguments(RmQRHeight.H17)]
    public async Task Optimal_HeightConstraint_IsRespected(RmQRHeight height)
    {
        const string content = "https://example.com/p/1234567890123456";
        var optimal = RmQRCodeGenerator.CreateRmQRCode(content, RmQREccLevel.M, height: height, segmentation: RmQRSegmentation.Optimal);
        var single = RmQRCodeGenerator.CreateRmQRCode(content, RmQREccLevel.M, height: height);

        await Assert.That(optimal.Height - 4).IsEqualTo((int)height);
        await Assert.That(optimal.Width).IsLessThanOrEqualTo(single.Width);
        await Assert.That(RmQRCodeDecoder.TryDecode(optimal, out var decoded)).IsTrue();
        await Assert.That(decoded).IsEqualTo(content);
    }

    [Test]
    public async Task Optimal_MinimizeWidth_PicksANarrowerSymbolThanSingle()
    {
        const string content = "https://example.com/p/1234567890123456";
        var single = RmQRCodeGenerator.CreateRmQRCode(content, RmQREccLevel.M, fitStrategy: RmQRFitStrategy.MinimizeWidth);
        var optimal = RmQRCodeGenerator.CreateRmQRCode(content, RmQREccLevel.M, fitStrategy: RmQRFitStrategy.MinimizeWidth, segmentation: RmQRSegmentation.Optimal);

        await Assert.That(optimal.Width).IsLessThanOrEqualTo(single.Width);
        await Assert.That(RmQRCodeDecoder.TryDecode(optimal, out var decoded)).IsTrue();
        await Assert.That(decoded).IsEqualTo(content);
    }

    [Test]
    public async Task Optimal_MinimizeHeight_PicksAFlatterOrEqualSymbolThanSingle()
    {
        const string content = "https://example.com/p/1234567890123456";
        var single = RmQRCodeGenerator.CreateRmQRCode(content, RmQREccLevel.M, fitStrategy: RmQRFitStrategy.MinimizeHeight);
        var optimal = RmQRCodeGenerator.CreateRmQRCode(content, RmQREccLevel.M, fitStrategy: RmQRFitStrategy.MinimizeHeight, segmentation: RmQRSegmentation.Optimal);

        await Assert.That(optimal.Height).IsLessThanOrEqualTo(single.Height);
        await Assert.That(RmQRCodeDecoder.TryDecode(optimal, out var decoded)).IsTrue();
        await Assert.That(decoded).IsEqualTo(content);
    }

    // -----------------------------------------------------------------
    // ECI
    // -----------------------------------------------------------------

    [Test]
    [Arguments("日本語1234567890")]
    [Arguments("😀😁1234567890")]
    [Arguments("é😀A1")]
    public async Task Optimal_Utf8_RoundTrips(string content)
    {
        var optimal = RmQRCodeGenerator.CreateRmQRCodeWithEci(content, RmQREccLevel.M, EciMode.Utf8, segmentation: RmQRSegmentation.Optimal);
        var single = RmQRCodeGenerator.CreateRmQRCodeWithEci(content, RmQREccLevel.M, EciMode.Utf8);

        await Assert.That(Area(optimal)).IsLessThanOrEqualTo(Area(single));
        await Assert.That(RmQRCodeDecoder.TryDecode(optimal, out var decoded)).IsTrue();
        await Assert.That(decoded).IsEqualTo(content);
    }

    [Test]
    [Arguments("éèê1234567890")]
    [Arguments("Café 12345678901234")]
    public async Task Optimal_Iso88591_RoundTrips(string content)
    {
        var optimal = RmQRCodeGenerator.CreateRmQRCodeWithEci(content, RmQREccLevel.M, EciMode.Iso8859_1, segmentation: RmQRSegmentation.Optimal);
        var single = RmQRCodeGenerator.CreateRmQRCodeWithEci(content, RmQREccLevel.M, EciMode.Iso8859_1);

        await Assert.That(Area(optimal)).IsLessThanOrEqualTo(Area(single));
        await Assert.That(RmQRCodeDecoder.TryDecode(optimal, out var decoded)).IsTrue();
        await Assert.That(decoded).IsEqualTo(content);
    }

    [Test]
    public async Task Optimal_ExplicitEciOnAsciiContent_StillRoundTrips()
    {
        // The ECI prefix is 11 bits the plan must pay for even though no Byte run needs it.
        const string content = "1234567890ABCDEFGH1234567890";
        var optimal = RmQRCodeGenerator.CreateRmQRCodeWithEci(content, RmQREccLevel.M, EciMode.Utf8, segmentation: RmQRSegmentation.Optimal);
        await Assert.That(RmQRCodeDecoder.TryDecode(optimal, out var decoded)).IsTrue();
        await Assert.That(decoded).IsEqualTo(content);
    }

    [Test]
    public async Task Optimal_Iso88591WithUnrepresentableContent_ThrowsLikeSingle()
    {
        const string content = "日本語";
        var single = Assert.Throws<ArgumentException>(() => RmQRCodeGenerator.CreateRmQRCodeWithEci(content, RmQREccLevel.M, EciMode.Iso8859_1));
        var optimal = Assert.Throws<ArgumentException>(() => RmQRCodeGenerator.CreateRmQRCodeWithEci(content, RmQREccLevel.M, EciMode.Iso8859_1, segmentation: RmQRSegmentation.Optimal));

        await Assert.That(optimal!.Message).IsEqualTo(single!.Message);
    }

    // -----------------------------------------------------------------
    // Overload consistency
    // -----------------------------------------------------------------

    [Test]
    [MethodDataSource(nameof(Corpus))]
    public async Task Optimal_SpanOverload_MatchesTheDataOverload(string content)
    {
        foreach (var quietZone in new[] { 0, 2, 5 })
        {
            var size = RmQRCodeGenerator.GetRequiredBufferSize(content.AsSpan(), RmQREccLevel.M, quietZoneSize: quietZone, segmentation: RmQRSegmentation.Optimal);
            var buffer = new byte[size.BufferSize];
            var written = RmQRCodeGenerator.CreateRmQRCode(content.AsSpan(), RmQREccLevel.M, buffer, quietZoneSize: quietZone, segmentation: RmQRSegmentation.Optimal);

            await Assert.That(written).IsEqualTo(size.BufferSize);

            var data = RmQRCodeGenerator.CreateRmQRCode(content, RmQREccLevel.M, quietZoneSize: quietZone, segmentation: RmQRSegmentation.Optimal);
            await Assert.That(size.Version).IsEqualTo(data.Version);
            await Assert.That(size.Width).IsEqualTo(data.Width);
            await Assert.That(size.Height).IsEqualTo(data.Height);

            for (var row = 0; row < data.Height; row++)
            {
                for (var col = 0; col < data.Width; col++)
                    await Assert.That(buffer[row * data.Width + col] != 0).IsEqualTo(data[row, col]);
            }
        }
    }

    [Test]
    [MethodDataSource(nameof(Corpus))]
    public async Task Optimal_EciSpanOverload_MatchesTheEciDataOverload(string content)
    {
        var size = RmQRCodeGenerator.GetRequiredBufferSizeWithEci(content.AsSpan(), RmQREccLevel.H, EciMode.Utf8, segmentation: RmQRSegmentation.Optimal);
        var buffer = new byte[size.BufferSize];
        RmQRCodeGenerator.CreateRmQRCodeWithEci(content.AsSpan(), RmQREccLevel.H, buffer, EciMode.Utf8, segmentation: RmQRSegmentation.Optimal);

        var data = RmQRCodeGenerator.CreateRmQRCodeWithEci(content, RmQREccLevel.H, EciMode.Utf8, segmentation: RmQRSegmentation.Optimal);
        await Assert.That(size.Version).IsEqualTo(data.Version);

        for (var row = 0; row < data.Height; row++)
        {
            for (var col = 0; col < data.Width; col++)
                await Assert.That(buffer[row * data.Width + col] != 0).IsEqualTo(data[row, col]);
        }
    }

    // -----------------------------------------------------------------
    // Argument validation
    // -----------------------------------------------------------------

    /// <summary>
    /// All eight entry points, because the span and ECI overloads reach the validation
    /// through their own cold method rather than sharing one guard.
    /// </summary>
    [Test]
    public async Task InvalidSegmentation_Throws_OnEveryEntryPoint()
    {
        const RmQRSegmentation invalid = (RmQRSegmentation)7;
        var buffer = new byte[4096];

        await Assert.That(() => RmQRCodeGenerator.CreateRmQRCode("123", RmQREccLevel.M, segmentation: invalid)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => RmQRCodeGenerator.CreateRmQRCode("123".AsSpan(), RmQREccLevel.M, segmentation: invalid)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => RmQRCodeGenerator.CreateRmQRCode("123".AsSpan(), RmQREccLevel.M, buffer, segmentation: invalid)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => RmQRCodeGenerator.GetRequiredBufferSize("123".AsSpan(), RmQREccLevel.M, segmentation: invalid)).Throws<ArgumentOutOfRangeException>();

        await Assert.That(() => RmQRCodeGenerator.CreateRmQRCodeWithEci("123", RmQREccLevel.M, EciMode.Utf8, segmentation: invalid)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => RmQRCodeGenerator.CreateRmQRCodeWithEci("123".AsSpan(), RmQREccLevel.M, EciMode.Utf8, segmentation: invalid)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => RmQRCodeGenerator.CreateRmQRCodeWithEci("123".AsSpan(), RmQREccLevel.M, buffer, EciMode.Utf8, segmentation: invalid)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => RmQRCodeGenerator.GetRequiredBufferSizeWithEci("123".AsSpan(), RmQREccLevel.M, EciMode.Utf8, segmentation: invalid)).Throws<ArgumentOutOfRangeException>();
    }

    /// <summary>The ECI validation the ECI entry points share must fire under Optimal too.</summary>
    [Test]
    public async Task Optimal_InvalidEci_ThrowsOnEveryEciEntryPoint()
    {
        var buffer = new byte[4096];
        const EciMode unsupported = (EciMode)4;

        await Assert.That(() => RmQRCodeGenerator.CreateRmQRCodeWithEci("123", RmQREccLevel.M, unsupported, segmentation: RmQRSegmentation.Optimal)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => RmQRCodeGenerator.CreateRmQRCodeWithEci("123".AsSpan(), RmQREccLevel.M, unsupported, segmentation: RmQRSegmentation.Optimal)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => RmQRCodeGenerator.CreateRmQRCodeWithEci("123".AsSpan(), RmQREccLevel.M, buffer, unsupported, segmentation: RmQRSegmentation.Optimal)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => RmQRCodeGenerator.GetRequiredBufferSizeWithEci("123".AsSpan(), RmQREccLevel.M, unsupported, segmentation: RmQRSegmentation.Optimal)).Throws<ArgumentOutOfRangeException>();
    }

#if !DEBUG
    /// <summary>
    /// The span destination is the library's zero-allocation path; the mixed-mode
    /// planner must keep it that way (its parent table rents from the pool rather
    /// than allocating). Debug builds are excluded per repo notes.
    /// </summary>
    [Test]
    public async Task Optimal_SpanDestination_IsAllocationFree()
    {
        var url = "https://example.com/p/1234567890123456";
        var longMixed = new string('a', 100) + new string('7', 100);   // over 73 chars, so the planner rents
        var utf8 = "rMQR 矩形コード 1234567890";
        var buffer = new byte[4096];

        for (var i = 0; i < 3; i++)
        {
            RmQRCodeGenerator.CreateRmQRCode(url.AsSpan(), RmQREccLevel.M, buffer, segmentation: RmQRSegmentation.Optimal);
            RmQRCodeGenerator.CreateRmQRCode(longMixed.AsSpan(), RmQREccLevel.M, buffer, segmentation: RmQRSegmentation.Optimal);
            RmQRCodeGenerator.CreateRmQRCodeWithEci(utf8.AsSpan(), RmQREccLevel.M, buffer, EciMode.Utf8, segmentation: RmQRSegmentation.Optimal);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 16; i++)
        {
            RmQRCodeGenerator.CreateRmQRCode(url.AsSpan(), RmQREccLevel.M, buffer, segmentation: RmQRSegmentation.Optimal);
            RmQRCodeGenerator.CreateRmQRCode(longMixed.AsSpan(), RmQREccLevel.M, buffer, segmentation: RmQRSegmentation.Optimal);
            RmQRCodeGenerator.CreateRmQRCodeWithEci(utf8.AsSpan(), RmQREccLevel.M, buffer, EciMode.Utf8, segmentation: RmQRSegmentation.Optimal);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(allocated).IsEqualTo(0);
    }
#endif

    [Test]
    public async Task Optimal_InvalidArguments_ThrowLikeSingle()
    {
        await Assert.That(() => RmQRCodeGenerator.CreateRmQRCode("123", (RmQREccLevel)9, segmentation: RmQRSegmentation.Optimal)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => RmQRCodeGenerator.CreateRmQRCode("123", RmQREccLevel.M, fitStrategy: (RmQRFitStrategy)9, segmentation: RmQRSegmentation.Optimal)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => RmQRCodeGenerator.CreateRmQRCode("123", RmQREccLevel.M, height: (RmQRHeight)8, segmentation: RmQRSegmentation.Optimal)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => RmQRCodeGenerator.CreateRmQRCode("123", RmQREccLevel.M, (RmQRVersion)99, segmentation: RmQRSegmentation.Optimal)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => RmQRCodeGenerator.CreateRmQRCode("123", RmQREccLevel.M, RmQRVersion.R7x43, height: RmQRHeight.H11, segmentation: RmQRSegmentation.Optimal)).Throws<ArgumentException>();
        await Assert.That(() => RmQRCodeGenerator.CreateRmQRCode("123", RmQREccLevel.M, quietZoneSize: -1, segmentation: RmQRSegmentation.Optimal)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Optimal_DestinationTooSmall_Throws()
    {
        var buffer = new byte[4];
        await Assert.That(() => RmQRCodeGenerator.CreateRmQRCode("https://example.com/p/1234567890123456".AsSpan(), RmQREccLevel.M, buffer, segmentation: RmQRSegmentation.Optimal)).Throws<ArgumentException>();
    }

    // -----------------------------------------------------------------
    // Image builder
    // -----------------------------------------------------------------

    [Test]
    public async Task ImageBuilder_WithSegmentation_UsesTheSmallerSymbol()
    {
        const string content = "https://example.com/p/1234567890123456";
        var optimal = new RmQRCodeImageBuilder(content)
            .WithSegmentation(RmQRSegmentation.Optimal)
            .ToByteArray();
        var single = new RmQRCodeImageBuilder(content).ToByteArray();

        await Assert.That(optimal).IsNotEquivalentTo(single);

        using var bitmap = SKBitmap.Decode(optimal);
        await Assert.That(RmQRCodeDecoder.TryDecode(bitmap, out var decoded)).IsTrue();
        await Assert.That(decoded).IsEqualTo(content);
    }

    [Test]
    public async Task ImageBuilder_WithSegmentation_RejectsInvalidValueAndPrebuiltData()
    {
        await Assert.That(() => new RmQRCodeImageBuilder("123").WithSegmentation((RmQRSegmentation)7)).Throws<ArgumentOutOfRangeException>();

        var data = RmQRCodeGenerator.CreateRmQRCode("123", RmQREccLevel.M);
        await Assert.That(() => new RmQRCodeImageBuilder(data).WithSegmentation(RmQRSegmentation.Optimal)).Throws<InvalidOperationException>();
    }
}
