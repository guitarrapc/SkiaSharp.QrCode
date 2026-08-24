namespace SkiaSharp.QrCode.Tests;

/// <summary>
/// The non-throwing sizing surface across all three symbologies: <c>false</c> only ever
/// means "does not fit", argument errors still throw. The load-bearing case is
/// <c>Agrees_WithThrowingOverload</c> — a divergence there means a buffer sized by
/// <c>Try</c> is the wrong size for the matrix <c>Create</c> writes.
/// </summary>
public class TryGetRequiredBufferSizeTest
{
    // Content that fits nothing: rMQR tops out at 361 digits / 150 bytes (R17x139-M),
    // Micro QR at 35 digits (M4-L), Standard QR at 2,953 bytes (v40-L).
    private static readonly string TooLongForRmQR = new('0', 380);

    // ---- rMQR: fits / does not fit ------------------------------------------------

    [Test]
    public async Task RmQR_Fits_ReturnsTrue_AndMatchesThrowingOverload()
    {
        var ok = RmQRCodeGenerator.TryGetRequiredBufferSize("012345678901", RmQREccLevel.M, out var size);
        var expected = RmQRCodeGenerator.GetRequiredBufferSize("012345678901", RmQREccLevel.M);

        await Assert.That(ok).IsTrue();
        await Assert.That(size.Version).IsEqualTo(expected.Version);
        await Assert.That(size.BufferSize).IsEqualTo(expected.BufferSize);
        await Assert.That(size.Width).IsEqualTo(expected.Width);
        await Assert.That(size.Height).IsEqualTo(expected.Height);
    }

    [Test]
    public async Task RmQR_TooLong_ReturnsFalse_AndLeavesSizeDefault()
    {
        var ok = RmQRCodeGenerator.TryGetRequiredBufferSize(TooLongForRmQR, RmQREccLevel.M, out var size);

        await Assert.That(ok).IsFalse();
        await Assert.That(size.BufferSize).IsEqualTo(0);
        await Assert.That(size.Width).IsEqualTo(0);
        await Assert.That(size.Height).IsEqualTo(0);
    }

    [Test]
    public async Task RmQR_RequestedVersionTooSmall_ReturnsFalse()
    {
        // R7x43 at M holds 12 digits; 13 is one over.
        await Assert.That(RmQRCodeGenerator.TryGetRequiredBufferSize("0123456789012", RmQREccLevel.M, out _, RmQRVersion.R7x43)).IsFalse();
        await Assert.That(RmQRCodeGenerator.TryGetRequiredBufferSize("012345678901", RmQREccLevel.M, out _, RmQRVersion.R7x43)).IsTrue();
    }

    [Test]
    public async Task RmQR_HeightConstraintExcludesEveryFittingVersion_ReturnsFalse()
    {
        // 150 digits fit at M (R11x139 holds 198) but no 7-module-high version does
        // (R7x139, the widest, holds 102). The height is legal, the content is not.
        var content = new string('0', 150);
        await Assert.That(RmQRCodeGenerator.TryGetRequiredBufferSize(content, RmQREccLevel.M, out _)).IsTrue();
        await Assert.That(RmQRCodeGenerator.TryGetRequiredBufferSize(content, RmQREccLevel.M, out _, height: RmQRHeight.H7)).IsFalse();
    }

    // ---- rMQR: mixed-mode segmentation --------------------------------------------

    [Test]
    public async Task RmQR_OptimalSegmentation_FitsOnlyWhenSplit_ReturnsTrue()
    {
        // 100 letters then 100 digits is 200 Byte-mode characters, 50 over the largest
        // capacity, but fits once the digit run is split off into Numeric.
        var content = new string('a', 100) + new string('0', 100);

        await Assert.That(RmQRCodeGenerator.TryGetRequiredBufferSize(content, RmQREccLevel.M, out _)).IsFalse();

        var ok = RmQRCodeGenerator.TryGetRequiredBufferSize(content, RmQREccLevel.M, out var size, segmentation: RmQRSegmentation.Optimal);
        var expected = RmQRCodeGenerator.GetRequiredBufferSize(content, RmQREccLevel.M, segmentation: RmQRSegmentation.Optimal);

        await Assert.That(ok).IsTrue();
        await Assert.That(size.Version).IsEqualTo(expected.Version);
        await Assert.That(size.BufferSize).IsEqualTo(expected.BufferSize);
    }

    [Test]
    public async Task RmQR_OptimalSegmentation_StillTooLong_ReturnsFalse()
    {
        var content = new string('a', 200) + new string('0', 200);
        await Assert.That(RmQRCodeGenerator.TryGetRequiredBufferSize(content, RmQREccLevel.M, out _, segmentation: RmQRSegmentation.Optimal)).IsFalse();
    }

    [Test]
    public async Task RmQR_OptimalSegmentation_RequestedVersionTooSmall_ReturnsFalse()
    {
        var content = new string('a', 100) + new string('0', 100);
        await Assert.That(RmQRCodeGenerator.TryGetRequiredBufferSize(content, RmQREccLevel.M, out _, RmQRVersion.R7x43, segmentation: RmQRSegmentation.Optimal)).IsFalse();
    }

    // ---- rMQR: ECI overload --------------------------------------------------------

    [Test]
    public async Task RmQR_WithEci_Fits_MatchesThrowingOverload()
    {
        var ok = RmQRCodeGenerator.TryGetRequiredBufferSizeWithEci("日本語", RmQREccLevel.M, EciMode.Utf8, out var size);
        var expected = RmQRCodeGenerator.GetRequiredBufferSizeWithEci("日本語", RmQREccLevel.M, EciMode.Utf8);

        await Assert.That(ok).IsTrue();
        await Assert.That(size.Version).IsEqualTo(expected.Version);
        await Assert.That(size.BufferSize).IsEqualTo(expected.BufferSize);
    }

    [Test]
    public async Task RmQR_WithEci_TooLong_ReturnsFalse()
    {
        await Assert.That(RmQRCodeGenerator.TryGetRequiredBufferSizeWithEci(TooLongForRmQR, RmQREccLevel.M, EciMode.Utf8, out _)).IsFalse();
    }

    // ---- rMQR: argument errors still throw ----------------------------------------

    [Test]
    public async Task RmQR_InvalidArguments_Throw_NotFalse()
    {
        await Assert.That(() => RmQRCodeGenerator.TryGetRequiredBufferSize("1", (RmQREccLevel)2, out _)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => RmQRCodeGenerator.TryGetRequiredBufferSize("1", RmQREccLevel.M, out _, (RmQRVersion)0)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => RmQRCodeGenerator.TryGetRequiredBufferSize("1", RmQREccLevel.M, out _, (RmQRVersion)33)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => RmQRCodeGenerator.TryGetRequiredBufferSize("1", RmQREccLevel.M, out _, fitStrategy: (RmQRFitStrategy)5)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => RmQRCodeGenerator.TryGetRequiredBufferSize("1", RmQREccLevel.M, out _, height: (RmQRHeight)10)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => RmQRCodeGenerator.TryGetRequiredBufferSize("1", RmQREccLevel.M, out _, quietZoneSize: -1)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => RmQRCodeGenerator.TryGetRequiredBufferSize("1", RmQREccLevel.M, out _, quietZoneSize: 10_001)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => RmQRCodeGenerator.TryGetRequiredBufferSize("1", RmQREccLevel.M, out _, segmentation: (RmQRSegmentation)7)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task RmQR_VersionAndHeightContradiction_Throws_NotFalse()
    {
        // Both are individually legal; the caller asked for two different things.
        await Assert.That(() => RmQRCodeGenerator.TryGetRequiredBufferSize("1", RmQREccLevel.M, out _, RmQRVersion.R9x43, height: RmQRHeight.H7)).Throws<ArgumentException>();
    }

    [Test]
    public async Task RmQR_WithEci_InvalidEciOrCharsetMismatch_Throws_NotFalse()
    {
        await Assert.That(() => RmQRCodeGenerator.TryGetRequiredBufferSizeWithEci("a", RmQREccLevel.M, (EciMode)999, out _)).Throws<ArgumentOutOfRangeException>();
        // The caller declared Latin-1 and the content is not Latin-1: a broken promise,
        // not a capacity outcome. Reporting "too long" here would hide the real cause.
        await Assert.That(() => RmQRCodeGenerator.TryGetRequiredBufferSizeWithEci("日本語", RmQREccLevel.M, EciMode.Iso8859_1, out _)).Throws<ArgumentException>();
    }

    // ---- rMQR: agreement with the throwing overload over a broad matrix -----------

    public static IEnumerable<(string content, RmQREccLevel ecc, RmQRFitStrategy strategy, RmQRHeight? height, RmQRSegmentation segmentation)> AgreementCases()
    {
        string[] contents =
        [
            "",
            "1",
            "012345678901",
            "HELLO WORLD",
            "https://example.com/p/1234567890123456",
            new string('0', 150),
            new string('0', 361),
            new string('0', 362),
            new string('a', 100) + new string('0', 100),
            new string('a', 200) + new string('0', 200),
            TooLongForRmQR,
        ];

        foreach (var content in contents)
            foreach (var ecc in new[] { RmQREccLevel.M, RmQREccLevel.H })
                foreach (var strategy in Enum.GetValues<RmQRFitStrategy>())
                    foreach (var height in new RmQRHeight?[] { null, RmQRHeight.H7, RmQRHeight.H11, RmQRHeight.H17 })
                        foreach (var segmentation in Enum.GetValues<RmQRSegmentation>())
                            yield return (content, ecc, strategy, height, segmentation);
    }

    [Test]
    [MethodDataSource(nameof(AgreementCases))]
    public async Task RmQR_Agrees_WithThrowingOverload(string content, RmQREccLevel ecc, RmQRFitStrategy strategy, RmQRHeight? height, RmQRSegmentation segmentation)
    {
        RmQRCodeCalculatedSize thrown = default;
        var threw = false;
        try
        {
            thrown = RmQRCodeGenerator.GetRequiredBufferSize(content, ecc, null, strategy, height, 2, segmentation);
        }
        catch (ArgumentException)
        {
            threw = true;
        }

        var ok = RmQRCodeGenerator.TryGetRequiredBufferSize(content, ecc, out var size, null, strategy, height, 2, segmentation);

        await Assert.That(ok).IsEqualTo(!threw);
        if (ok)
        {
            await Assert.That(size.Version).IsEqualTo(thrown.Version);
            await Assert.That(size.BufferSize).IsEqualTo(thrown.BufferSize);
        }
    }

    // ---- the documented two-call pattern -------------------------------------------

    [Test]
    [Arguments("012345678901", RmQRSegmentation.Single)]
    [Arguments("https://example.com/p/1234567890123456", RmQRSegmentation.Single)]
    [Arguments("https://example.com/p/1234567890123456", RmQRSegmentation.Optimal)]
    [Arguments("HELLO WORLD 123", RmQRSegmentation.Optimal)]
    public async Task RmQR_ReportedVersion_EncodesIntoTheReportedBuffer(string content, RmQRSegmentation segmentation)
    {
        // README and migration notes promise this composition: feeding the reported
        // version back removes the fit, so the encode cannot fail on length.
        var ok = RmQRCodeGenerator.TryGetRequiredBufferSize(content, RmQREccLevel.M, out var size, segmentation: segmentation);
        await Assert.That(ok).IsTrue();

        var buffer = new byte[size.BufferSize];
        var written = RmQRCodeGenerator.CreateRmQRCode(content, RmQREccLevel.M, buffer, requestedVersion: size.Version, segmentation: segmentation);

        await Assert.That(written).IsEqualTo(size.BufferSize);
        await Assert.That(RmQRCodeDecoder.TryDecode(buffer, size.Width, size.Height, out var decoded, out _)).IsTrue();
        await Assert.That(decoded).IsEqualTo(content);
    }

    [Test]
    public async Task MicroQR_ReportedVersion_EncodesIntoTheReportedBuffer()
    {
        var ok = MicroQRCodeGenerator.TryGetRequiredBufferSize("12345", MicroQREccLevel.L, out var size);
        await Assert.That(ok).IsTrue();

        var buffer = new byte[size.BufferSize];
        var written = MicroQRCodeGenerator.CreateMicroQRCode("12345", MicroQREccLevel.L, buffer, requestedVersion: size.Version);
        await Assert.That(written).IsEqualTo(size.BufferSize);
    }

    [Test]
    public async Task StandardQR_ReportedSize_EncodesIntoTheReportedBuffer()
    {
        var ok = QRCodeGenerator.TryGetRequiredBufferSize("hello world", ECCLevel.M, out var size);
        await Assert.That(ok).IsTrue();

        var buffer = new byte[size.BufferSize];
        var written = QRCodeGenerator.CreateQrCode("hello world".AsSpan(), ECCLevel.M, buffer);
        await Assert.That(written).IsEqualTo(size.BufferSize);
    }

    // ---- Micro QR ------------------------------------------------------------------

    [Test]
    public async Task MicroQR_Fits_ReturnsTrue_AndMatchesThrowingOverload()
    {
        var ok = MicroQRCodeGenerator.TryGetRequiredBufferSize("12345", MicroQREccLevel.ErrorDetectionOnly, out var size);
        var expected = MicroQRCodeGenerator.GetRequiredBufferSize("12345", MicroQREccLevel.ErrorDetectionOnly);

        await Assert.That(ok).IsTrue();
        await Assert.That(size.Version).IsEqualTo(expected.Version);
        await Assert.That(size.BufferSize).IsEqualTo(expected.BufferSize);
        await Assert.That(size.QrSize).IsEqualTo(expected.QrSize);
    }

    [Test]
    public async Task MicroQR_TooLong_ReturnsFalse_AndLeavesSizeDefault()
    {
        var ok = MicroQRCodeGenerator.TryGetRequiredBufferSize(new string('0', 40), MicroQREccLevel.L, out var size);

        await Assert.That(ok).IsFalse();
        await Assert.That(size.BufferSize).IsEqualTo(0);
        await Assert.That(size.QrSize).IsEqualTo(0);
    }

    [Test]
    public async Task MicroQR_ModeUnavailableForContent_ReturnsFalse()
    {
        // The text picks the mode, so "M1 cannot carry Byte" is a property of the
        // content, not of the arguments: false, not a throw.
        await Assert.That(MicroQRCodeGenerator.TryGetRequiredBufferSize("abc", MicroQREccLevel.ErrorDetectionOnly, out _, MicroQRVersion.M1)).IsFalse();
        // No version at all supports Byte at ErrorDetectionOnly (that level pins M1).
        await Assert.That(MicroQRCodeGenerator.TryGetRequiredBufferSize("abc", MicroQREccLevel.ErrorDetectionOnly, out _)).IsFalse();
    }

    [Test]
    public async Task MicroQR_InvalidArguments_Throw_NotFalse()
    {
        await Assert.That(() => MicroQRCodeGenerator.TryGetRequiredBufferSize("1", (MicroQREccLevel)9, out _)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => MicroQRCodeGenerator.TryGetRequiredBufferSize("1", MicroQREccLevel.L, out _, (MicroQRVersion)0)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => MicroQRCodeGenerator.TryGetRequiredBufferSize("1", MicroQREccLevel.L, out _, (MicroQRVersion)5)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => MicroQRCodeGenerator.TryGetRequiredBufferSize("1", MicroQREccLevel.L, out _, quietZoneSize: -1)).Throws<ArgumentOutOfRangeException>();
        // M1 accepts ErrorDetectionOnly only: a version/ECC contradiction, text-independent.
        await Assert.That(() => MicroQRCodeGenerator.TryGetRequiredBufferSize("1", MicroQREccLevel.L, out _, MicroQRVersion.M1)).Throws<ArgumentException>();
    }

    public static IEnumerable<(string content, MicroQREccLevel ecc, MicroQRVersion? version)> MicroAgreementCases()
    {
        string[] contents = ["", "1", "12345", "123456", "HELLO", "abc", new string('0', 35), new string('0', 36)];
        foreach (var content in contents)
            foreach (var ecc in Enum.GetValues<MicroQREccLevel>())
                foreach (var version in new MicroQRVersion?[] { null, MicroQRVersion.M1, MicroQRVersion.M2, MicroQRVersion.M3, MicroQRVersion.M4 })
                    yield return (content, ecc, version);
    }

    [Test]
    [MethodDataSource(nameof(MicroAgreementCases))]
    public async Task MicroQR_Agrees_WithThrowingOverload(string content, MicroQREccLevel ecc, MicroQRVersion? version)
    {
        MicroQRCodeCalculatedSize thrown = default;
        var threw = false;
        try
        {
            thrown = MicroQRCodeGenerator.GetRequiredBufferSize(content, ecc, version);
        }
        catch (ArgumentException)
        {
            threw = true;
        }

        // A version/ECC contradiction is an argument error on both surfaces, so it is
        // excluded from the "throws ⟺ returns false" equivalence.
        if (threw && version is { } v && !IsValidMicroCombination(v, ecc))
        {
            await Assert.That(() => MicroQRCodeGenerator.TryGetRequiredBufferSize(content, ecc, out _, version)).Throws<ArgumentException>();
            return;
        }

        var ok = MicroQRCodeGenerator.TryGetRequiredBufferSize(content, ecc, out var size, version);

        await Assert.That(ok).IsEqualTo(!threw);
        if (ok)
        {
            await Assert.That(size.Version).IsEqualTo(thrown.Version);
            await Assert.That(size.BufferSize).IsEqualTo(thrown.BufferSize);
        }
    }

    private static bool IsValidMicroCombination(MicroQRVersion version, MicroQREccLevel ecc) => version switch
    {
        MicroQRVersion.M1 => ecc == MicroQREccLevel.ErrorDetectionOnly,
        MicroQRVersion.M2 or MicroQRVersion.M3 => ecc is MicroQREccLevel.L or MicroQREccLevel.M,
        _ => ecc is MicroQREccLevel.L or MicroQREccLevel.M or MicroQREccLevel.Q,
    };

    // ---- Standard QR ---------------------------------------------------------------

    [Test]
    public async Task StandardQR_Fits_ReturnsTrue_AndMatchesThrowingOverload()
    {
        var ok = QRCodeGenerator.TryGetRequiredBufferSize("hello world", ECCLevel.M, out var size);
        var expected = QRCodeGenerator.GetRequiredBufferSize("hello world", ECCLevel.M);

        await Assert.That(ok).IsTrue();
        await Assert.That(size).IsEqualTo(expected);
    }

    [Test]
    public async Task StandardQR_TooLong_ReturnsFalse_AndLeavesSizeDefault()
    {
        // Version 40 at L holds 2,953 bytes.
        var ok = QRCodeGenerator.TryGetRequiredBufferSize(new string('a', 3000), ECCLevel.L, out var size);

        await Assert.That(ok).IsFalse();
        await Assert.That(size.BufferSize).IsEqualTo(0);
        await Assert.That(size.QrSize).IsEqualTo(0);
        await Assert.That(size.Version).IsEqualTo(0);
    }

    [Test]
    public async Task StandardQR_InvalidArguments_Throw_NotFalse()
    {
        await Assert.That(() => QRCodeGenerator.TryGetRequiredBufferSize("1", ECCLevel.M, out _, quietZoneSize: -1)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => QRCodeGenerator.TryGetRequiredBufferSize("1", ECCLevel.M, out _, quietZoneSize: int.MaxValue)).Throws<ArgumentOutOfRangeException>();
    }

    public static IEnumerable<(string content, ECCLevel ecc, bool utf8BOM, EciMode eciMode)> StandardAgreementCases()
    {
        // The BOM adds 3 bytes and ECI a 12-bit header, so both shift the version boundary;
        // the cases straddle it at v40-L (2,953 bytes / 7,089 digits) and at v1.
        string[] contents =
        [
            "", "1", "hello world", "日本語",
            new string('a', 2953), new string('a', 2954),
            new string('a', 2950), new string('a', 2951),
            new string('0', 7089), new string('0', 7090),
            new string('a', 17), new string('a', 18),
        ];

        foreach (var content in contents)
            foreach (var ecc in Enum.GetValues<ECCLevel>())
                foreach (var eciMode in new[] { EciMode.Default, EciMode.Iso8859_1, EciMode.Utf8 })
                    foreach (var utf8BOM in new[] { false, true })
                        yield return (content, ecc, utf8BOM, eciMode);
    }

    [Test]
    [MethodDataSource(nameof(StandardAgreementCases))]
    public async Task StandardQR_Agrees_WithThrowingOverload(string content, ECCLevel ecc, bool utf8BOM, EciMode eciMode)
    {
        QRCodeCalculatedSize thrown = default;
        var threw = false;
        try
        {
            thrown = QRCodeGenerator.GetRequiredBufferSize(content, ecc, utf8BOM, eciMode);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            threw = true;
        }

        var ok = QRCodeGenerator.TryGetRequiredBufferSize(content, ecc, out var size, utf8BOM, eciMode);

        await Assert.That(ok).IsEqualTo(!threw);
        if (ok)
            await Assert.That(size).IsEqualTo(thrown);
    }
}
