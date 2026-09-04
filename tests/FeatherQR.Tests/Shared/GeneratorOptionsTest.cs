namespace FeatherQR.Tests;

/// <summary>
/// <see cref="QRCodeGeneratorOptions"/> and <see cref="MicroQRCodeGeneratorOptions"/>,
/// the options overloads added alongside the released parameter lists
/// (specs/qrcode-symbologies.md, Public API direction).
/// </summary>
/// <remarks>
/// <para>
/// Two things are pinned here. First, <c>default(T)</c> has to carry the documented
/// defaults, which for quiet zone is 4 (Standard QR) and 2 (Micro QR) rather than the
/// zero value, while 0 stays expressible. Second, and this is the point of the phase,
/// the options overload and the released overload have to produce the same symbol for
/// every configuration the released one can express: the released overloads are the
/// shipped contract, and the new ones are only a different way to spell them.
/// </para>
/// <para>
/// The one place the options overloads do <em>more</em> than the released ones is
/// Standard QR sizing. The released <c>GetRequiredBufferSize</c> has never had a version parameter,
/// so an ignored <c>Version</c> would have been a silent trap; the options overloads
/// honour it and report a version that cannot hold the content the same way Micro QR
/// and rMQR already do.
/// </para>
/// </remarks>
public class GeneratorOptionsTest
{
    private const int StandardQrQuietZone = 4;
    private const int MicroQrQuietZone = 2;   // ISO/IEC 18004

    private const string Ascii = "HELLO WORLD 123";
    private const string Latin1 = "Café déjà vu";
    private const string Unicode = "日本語のテキスト";
    private const string Digits = "0123456789";

    // ---- default(T) --------------------------------------------------------------

    [Test]
    public async Task StandardQrOptions_Default_CarriesTheDocumentedDefaults()
    {
        var options = default(QRCodeGeneratorOptions);

        await Assert.That(options.EciMode).IsEqualTo(EciMode.Default);
        await Assert.That(options.Utf8BOM).IsFalse();
        await Assert.That(options.Version.IsAny).IsTrue();
        await Assert.That(options.QuietZoneSize).IsEqualTo(StandardQrQuietZone);
        await Assert.That(QRCodeGeneratorOptions.Default).IsEqualTo(options);
    }

    [Test]
    public async Task MicroQrOptions_Default_CarriesTheDocumentedDefaults()
    {
        var options = default(MicroQRCodeGeneratorOptions);

        await Assert.That(options.Version.IsAny).IsTrue();
        await Assert.That(options.QuietZoneSize).IsEqualTo(MicroQrQuietZone);
        await Assert.That(MicroQRCodeGeneratorOptions.Default).IsEqualTo(options);
    }

    [Test]
    public async Task QuietZoneSize_WrittenAsItsDefaultValue_IsIndistinguishableFromUnset()
    {
        // Offset encoding, as in RmQRCodeGeneratorOptions: an explicitly written default
        // must collapse onto the unset form or the generated equality lies.
        var standard = new QRCodeGeneratorOptions { QuietZoneSize = StandardQrQuietZone };
        await Assert.That(standard).IsEqualTo(default(QRCodeGeneratorOptions));
        await Assert.That(standard.GetHashCode()).IsEqualTo(default(QRCodeGeneratorOptions).GetHashCode());

        var micro = new MicroQRCodeGeneratorOptions { QuietZoneSize = MicroQrQuietZone };
        await Assert.That(micro).IsEqualTo(default(MicroQRCodeGeneratorOptions));
        await Assert.That(micro.GetHashCode()).IsEqualTo(default(MicroQRCodeGeneratorOptions).GetHashCode());
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(4)]
    [Arguments(9)]
    public async Task QuietZoneSize_RoundTripsOverItsRange(int quietZoneSize)
    {
        await Assert.That(new QRCodeGeneratorOptions { QuietZoneSize = quietZoneSize }.QuietZoneSize).IsEqualTo(quietZoneSize);
        await Assert.That(new MicroQRCodeGeneratorOptions { QuietZoneSize = quietZoneSize }.QuietZoneSize).IsEqualTo(quietZoneSize);
    }

    [Test]
    public async Task QuietZoneSize_Zero_IsExpressibleAndDiffersFromUnset()
    {
        await Assert.That(new QRCodeGeneratorOptions { QuietZoneSize = 0 }).IsNotEqualTo(default(QRCodeGeneratorOptions));
        await Assert.That(new MicroQRCodeGeneratorOptions { QuietZoneSize = 0 }).IsNotEqualTo(default(MicroQRCodeGeneratorOptions));
    }

    // ---- Standard QR: options overload == released overload -------------------------

    public static IEnumerable<(string text, ECCLevel ecc, bool utf8BOM, EciMode eci, int requestedVersion, int quietZone)> StandardQrConfigurations()
    {
        yield return (Ascii, ECCLevel.M, false, EciMode.Default, -1, 4);
        yield return (Ascii, ECCLevel.L, false, EciMode.Default, -1, 0);
        yield return (Ascii, ECCLevel.Q, false, EciMode.Default, -1, 7);
        yield return (Ascii, ECCLevel.H, false, EciMode.Default, 10, 4);
        yield return (Digits, ECCLevel.M, false, EciMode.Default, 1, 4);
        yield return (Digits, ECCLevel.M, false, EciMode.Default, 40, 2);
        yield return (Latin1, ECCLevel.M, false, EciMode.Default, -1, 4);
        yield return (Latin1, ECCLevel.M, false, EciMode.Iso8859_1, -1, 4);
        yield return (Latin1, ECCLevel.M, false, EciMode.Utf8, -1, 4);
        yield return (Unicode, ECCLevel.M, false, EciMode.Default, -1, 4);
        yield return (Unicode, ECCLevel.M, false, EciMode.Utf8, -1, 1);
        yield return (Unicode, ECCLevel.H, true, EciMode.Utf8, -1, 4);
        yield return (Ascii, ECCLevel.M, true, EciMode.Utf8, 12, 3);
        yield return ("", ECCLevel.M, false, EciMode.Default, -1, 4);
    }

    [Test]
    [MethodDataSource(nameof(StandardQrConfigurations))]
    public async Task StandardQr_OptionsOverload_MatchesReleasedOverload(string text, ECCLevel ecc, bool utf8BOM, EciMode eci, int requestedVersion, int quietZone)
    {
        var options = new QRCodeGeneratorOptions
        {
            Utf8BOM = utf8BOM,
            EciMode = eci,
            Version = requestedVersion == -1 ? QRCodeVersionRange.Any : QRCodeVersionRange.Exactly(requestedVersion),
            QuietZoneSize = quietZone,
        };

        var released = QRCodeGenerator.CreateQrCode(text, ecc, utf8BOM, eci, requestedVersion, quietZone);
        var viaOptions = QRCodeGenerator.CreateQrCode(text, ecc, options);
        var viaOptionsSpan = QRCodeGenerator.CreateQrCode(text.AsSpan(), ecc, options);

        await Assert.That(viaOptions.Version).IsEqualTo(released.Version);
        await Assert.That(viaOptions.GetRawData().AsSpan().SequenceEqual(released.GetRawData())).IsTrue();
        await Assert.That(viaOptionsSpan.GetRawData().AsSpan().SequenceEqual(released.GetRawData())).IsTrue();
    }

    [Test]
    [MethodDataSource(nameof(StandardQrConfigurations))]
    public async Task StandardQr_OptionsDestinationOverload_MatchesReleasedOverload(string text, ECCLevel ecc, bool utf8BOM, EciMode eci, int requestedVersion, int quietZone)
    {
        var options = new QRCodeGeneratorOptions
        {
            Utf8BOM = utf8BOM,
            EciMode = eci,
            Version = requestedVersion == -1 ? QRCodeVersionRange.Any : QRCodeVersionRange.Exactly(requestedVersion),
            QuietZoneSize = quietZone,
        };

        var size = Sizing.Required(text.AsSpan(), ecc, options);
        var fromReleased = new byte[size.BufferSize];
        var fromOptions = new byte[size.BufferSize];
        var fromOptionsString = new byte[size.BufferSize];

        var releasedWritten = QRCodeGenerator.CreateQrCode(text.AsSpan(), ecc, fromReleased, utf8BOM, eci, requestedVersion, quietZone);
        var optionsWritten = QRCodeGenerator.CreateQrCode(text.AsSpan(), ecc, fromOptions, options);
        var optionsStringWritten = QRCodeGenerator.CreateQrCode(text, ecc, fromOptionsString, options);

        await Assert.That(optionsWritten).IsEqualTo(releasedWritten);
        await Assert.That(optionsStringWritten).IsEqualTo(releasedWritten);
        await Assert.That(fromOptions).IsEquivalentTo(fromReleased);
        await Assert.That(fromOptionsString).IsEquivalentTo(fromReleased);
    }

    [Test]
    [MethodDataSource(nameof(StandardQrConfigurations))]
    public async Task StandardQr_OptionsSizing_MatchesReleasedSizing_WhenVersionIsAutomatic(string text, ECCLevel ecc, bool utf8BOM, EciMode eci, int requestedVersion, int quietZone)
    {
        // The released sizing overloads have no version parameter, so they are only
        // comparable on the automatic-version rows.
        if (requestedVersion != -1)
            return;

        var options = new QRCodeGeneratorOptions { Utf8BOM = utf8BOM, EciMode = eci, QuietZoneSize = quietZone };

        var released = Sizing.ReleasedRequired(text.AsSpan(), ecc, utf8BOM, eci, quietZone);
        var viaOptions = Sizing.Required(text.AsSpan(), ecc, options);

        await Assert.That(viaOptions).IsEqualTo(released);
        await Assert.That(QRCodeGenerator.TryGetRequiredBufferSize(text.AsSpan(), ecc, out var tryOptions, options)).IsTrue();
        await Assert.That(tryOptions).IsEqualTo(released);
    }

    // ---- Micro QR: options overload == released overload -----------------------------

    public static IEnumerable<(string text, MicroQREccLevel ecc, MicroQRVersion? requestedVersion, int quietZone)> MicroQrConfigurations()
    {
        yield return ("12345", MicroQREccLevel.ErrorDetectionOnly, null, 2);
        yield return ("12345", MicroQREccLevel.ErrorDetectionOnly, MicroQRVersion.M1, 0);
        yield return ("1234567890", MicroQREccLevel.L, null, 2);
        yield return ("1234567890", MicroQREccLevel.L, MicroQRVersion.M3, 5);
        yield return ("AC-42", MicroQREccLevel.L, null, 2);
        yield return ("AC-42", MicroQREccLevel.M, MicroQRVersion.M3, 2);
        yield return ("hello", MicroQREccLevel.L, MicroQRVersion.M4, 1);
        yield return ("hello", MicroQREccLevel.Q, MicroQRVersion.M4, 2);
    }

    [Test]
    [MethodDataSource(nameof(MicroQrConfigurations))]
    public async Task MicroQr_OptionsOverload_MatchesReleasedOverload(string text, MicroQREccLevel ecc, MicroQRVersion? requestedVersion, int quietZone)
    {
        var options = new MicroQRCodeGeneratorOptions { Version = requestedVersion is null ? MicroQRVersionRange.Any : MicroQRVersionRange.Exactly(requestedVersion.Value), QuietZoneSize = quietZone };

        var released = MicroQRCodeGenerator.CreateMicroQRCode(text, ecc, requestedVersion, quietZone);
        var viaOptions = MicroQRCodeGenerator.CreateMicroQRCode(text, ecc, options);
        var viaOptionsSpan = MicroQRCodeGenerator.CreateMicroQRCode(text.AsSpan(), ecc, options);

        await Assert.That(viaOptions.Version).IsEqualTo(released.Version);
        await Assert.That(viaOptions.GetRawData().AsSpan().SequenceEqual(released.GetRawData())).IsTrue();
        await Assert.That(viaOptionsSpan.GetRawData().AsSpan().SequenceEqual(released.GetRawData())).IsTrue();
    }

    [Test]
    [MethodDataSource(nameof(MicroQrConfigurations))]
    public async Task MicroQr_OptionsSizingAndDestination_MatchReleasedOverloads(string text, MicroQREccLevel ecc, MicroQRVersion? requestedVersion, int quietZone)
    {
        var options = new MicroQRCodeGeneratorOptions { Version = requestedVersion is null ? MicroQRVersionRange.Any : MicroQRVersionRange.Exactly(requestedVersion.Value), QuietZoneSize = quietZone };

        var released = Sizing.ReleasedRequired(text.AsSpan(), ecc, requestedVersion, quietZone);
        var viaOptions = Sizing.Required(text.AsSpan(), ecc, options);
        await Assert.That(viaOptions).IsEqualTo(released);

        await Assert.That(MicroQRCodeGenerator.TryGetRequiredBufferSize(text.AsSpan(), ecc, out var tryOptions, options)).IsTrue();
        await Assert.That(tryOptions).IsEqualTo(released);

        var fromReleased = new byte[released.BufferSize];
        var fromOptions = new byte[released.BufferSize];
        var releasedWritten = MicroQRCodeGenerator.CreateMicroQRCode(text.AsSpan(), ecc, fromReleased, requestedVersion, quietZone);
        var optionsWritten = MicroQRCodeGenerator.CreateMicroQRCode(text.AsSpan(), ecc, fromOptions, options);

        await Assert.That(optionsWritten).IsEqualTo(releasedWritten);
        await Assert.That(fromOptions).IsEquivalentTo(fromReleased);
    }

    // ---- Standard QR sizing honours Version (the one added capability) ---------------

    [Test]
    public async Task StandardQrSizing_ExplicitVersion_ReportsThatVersion()
    {
        var automatic = Sizing.Required(Digits.AsSpan(), ECCLevel.M, QRCodeGeneratorOptions.Default);
        var pinned = Sizing.Required(Digits.AsSpan(), ECCLevel.M, new QRCodeGeneratorOptions { Version = QRCodeVersionRange.Exactly(15) });

        await Assert.That(automatic.Version).IsEqualTo(1);
        await Assert.That(pinned.Version).IsEqualTo(15);
        await Assert.That(pinned.QrSize).IsEqualTo(QRCodeData.SizeFromVersion(15) + 8);

        // and the size it reports is the size the encode actually writes
        var buffer = new byte[pinned.BufferSize];
        var written = QRCodeGenerator.CreateQrCode(Digits.AsSpan(), ECCLevel.M, buffer, new QRCodeGeneratorOptions { Version = QRCodeVersionRange.Exactly(15) });
        await Assert.That(written).IsEqualTo(pinned.BufferSize);
    }

    [Test]
    public async Task StandardQrSizing_ExplicitVersionTooSmallForTheContent_IsNotAFit()
    {
        var content = new string('A', 100);   // needs well above version 1

        await Assert.That(QRCodeGenerator.TryGetRequiredBufferSize(content.AsSpan(), ECCLevel.M, out var size, new QRCodeGeneratorOptions { Version = QRCodeVersionRange.Exactly(1) })).IsFalse();
        await Assert.That(size).IsEqualTo(default(QRCodeCalculatedSize));

        // the same content at a version that does hold it is a fit
        await Assert.That(QRCodeGenerator.TryGetRequiredBufferSize(content.AsSpan(), ECCLevel.M, out var ok, new QRCodeGeneratorOptions { Version = QRCodeVersionRange.Exactly(10) })).IsTrue();
        await Assert.That(ok.Version).IsEqualTo(10);
    }

    [Test]
    [Arguments(0)]
    [Arguments(41)]
    [Arguments(-2)]
    public async Task StandardQrOptions_VersionOutOfRange_ThrowsBeforeAGeneratorIsCalled(int version)
    {
        // An invalid version is an argument error, not a "does not fit". Since Phase 3 it
        // is rejected when the range is constructed rather than when a generator reads it,
        // so an option set carrying an impossible version cannot be built at all. The
        // per-factory coverage lives in VersionRangeTest.
        await Assert.That(() => new QRCodeGeneratorOptions { Version = QRCodeVersionRange.Exactly(version) }).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task DefaultLiteralAsTheThirdArgument_StillMeansAllDefaults()
    {
        // Adding the options overloads moved this call: `default` now binds to the options
        // parameter rather than to `utf8BOM` / `requestedVersion`, because a candidate that
        // needs no optional-parameter substitution wins. Both readings mean "all defaults",
        // so the symbol must be unchanged. (Three sibling shapes did not compile at all
        // before, being ambiguous with the Span<byte> destination overload.)
        var standardDefault = QRCodeGenerator.CreateQrCode("hello world", ECCLevel.M, default);
        var standardOmitted = QRCodeGenerator.CreateQrCode("hello world", ECCLevel.M);
        await Assert.That(standardDefault.Version).IsEqualTo(standardOmitted.Version);
        await Assert.That(standardDefault.GetRawData().AsSpan().SequenceEqual(standardOmitted.GetRawData())).IsTrue();

        var microDefault = MicroQRCodeGenerator.CreateMicroQRCode("12345", MicroQREccLevel.L, default);
        var microOmitted = MicroQRCodeGenerator.CreateMicroQRCode("12345", MicroQREccLevel.L);
        await Assert.That(microDefault.Version).IsEqualTo(microOmitted.Version);
        await Assert.That(microDefault.GetRawData().AsSpan().SequenceEqual(microOmitted.GetRawData())).IsTrue();

        // the span spellings, which were ambiguous before the options overloads existed
        await Assert.That(QRCodeGenerator.CreateQrCode("hello world".AsSpan(), ECCLevel.M, default).Version).IsEqualTo(standardOmitted.Version);
        await Assert.That(MicroQRCodeGenerator.CreateMicroQRCode("12345".AsSpan(), MicroQREccLevel.L, default).Version).IsEqualTo(microOmitted.Version);
    }

    [Test]
    public async Task Options_NegativeQuietZone_ThrowsFromEveryEntryPoint()
    {
        var standard = new QRCodeGeneratorOptions { QuietZoneSize = -1 };
        await Assert.That(() => QRCodeGenerator.CreateQrCode(Digits, ECCLevel.M, standard)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => QRCodeGenerator.TryGetRequiredBufferSize(Digits.AsSpan(), ECCLevel.M, out _, standard)).Throws<ArgumentOutOfRangeException>();

        var micro = new MicroQRCodeGeneratorOptions { QuietZoneSize = -1 };
        await Assert.That(() => MicroQRCodeGenerator.CreateMicroQRCode(Digits, MicroQREccLevel.L, micro)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => MicroQRCodeGenerator.TryGetRequiredBufferSize(Digits.AsSpan(), MicroQREccLevel.L, out _, micro)).Throws<ArgumentOutOfRangeException>();
    }
}
