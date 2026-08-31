namespace SkiaSharp.QrCode.Tests;

/// <summary>
/// <see cref="MicroQRSegmentation.Optimal"/> end to end. The three properties every
/// case is held to are: the symbol never selects a larger version than the
/// single-mode default, it still decodes to the original content, and when it lands
/// on the same version as the single-mode default it is byte-for-byte the same
/// matrix. The rest covers the Micro QR-specific branches: per-version mode
/// availability (M1 Numeric-only, M2 without Byte), the version range, mask
/// pinning, and both directions of "does not fit".
/// </summary>
public class MicroQRSegmentationTest
{
    /// <summary>
    /// Contents spanning the equivalence classes: single-mode only (no gain
    /// possible), mixed with a gain, mixed without a gain, Latin-1 and UTF-8 Byte
    /// charsets, and the degenerate lengths. Everything here fits under Single too.
    /// </summary>
    public static IEnumerable<string> Corpus() =>
    [
        "1",
        "A",
        "a",
        "12345",
        "ABC123",
        "abc12",
        "AB1234567890",
        "AB12345678901234567",
        "A 12:345/",
        "a1234567890",
        "é12345",
        "あ12",
        "日本1",
    ];

    [Test]
    public async Task Optimal_IsNotTheDefault()
    {
        await Assert.That(default(MicroQRCodeGeneratorOptions).Segmentation).IsEqualTo(MicroQRSegmentation.Single);
        await Assert.That(MicroQRCodeGeneratorOptions.Default.Segmentation).IsEqualTo(MicroQRSegmentation.Single);
    }

    [Test]
    [MethodDataSource(nameof(Corpus))]
    public async Task Optimal_IsNeverLargerThanSingle_AndAlwaysRoundTrips(string content)
    {
        var single = MicroQRCodeGenerator.CreateMicroQRCode(content, MicroQREccLevel.L, MicroQRCodeGeneratorOptions.Default);
        var optimal = MicroQRCodeGenerator.CreateMicroQRCode(content, MicroQREccLevel.L, new MicroQRCodeGeneratorOptions { Segmentation = MicroQRSegmentation.Optimal });

        await Assert.That((int)optimal.Version).IsLessThanOrEqualTo((int)single.Version);

        await Assert.That(MicroQRCodeDecoder.TryDecode(optimal, out var decoded)).IsTrue();
        await Assert.That(decoded).IsEqualTo(content);
    }

    [Test]
    [MethodDataSource(nameof(Corpus))]
    public async Task Optimal_SameVersionAsSingle_ProducesTheIdenticalMatrix(string content)
    {
        var single = MicroQRCodeGenerator.CreateMicroQRCode(content, MicroQREccLevel.L, MicroQRCodeGeneratorOptions.Default);
        var optimal = MicroQRCodeGenerator.CreateMicroQRCode(content, MicroQREccLevel.L, new MicroQRCodeGeneratorOptions { Segmentation = MicroQRSegmentation.Optimal });

        if (optimal.Version != single.Version)
            return; // a genuine gain; the round-trip test covers it

        await Assert.That(optimal.GetRawData()).IsEquivalentTo(single.GetRawData());
    }

    [Test]
    [Arguments("AB12345678901234567")]
    [Arguments("ab1234567890123")]
    public async Task Optimal_MixedContent_SelectsSmallerVersionThanSingle(string content)
    {
        var single = MicroQRCodeGenerator.CreateMicroQRCode(content, MicroQREccLevel.L, MicroQRCodeGeneratorOptions.Default);
        var optimal = MicroQRCodeGenerator.CreateMicroQRCode(content, MicroQREccLevel.L, new MicroQRCodeGeneratorOptions { Segmentation = MicroQRSegmentation.Optimal });

        await Assert.That((int)optimal.Version).IsLessThan((int)single.Version);

        await Assert.That(MicroQRCodeDecoder.TryDecode(optimal, out var decoded)).IsTrue();
        await Assert.That(decoded).IsEqualTo(content);
    }

    [Test]
    [Arguments("12345")]
    [Arguments("HELLO")]
    [Arguments("hello")]
    public async Task Optimal_SingleModeContent_KeepsTheSameVersion(string content)
    {
        var single = MicroQRCodeGenerator.CreateMicroQRCode(content, MicroQREccLevel.L, MicroQRCodeGeneratorOptions.Default);
        var optimal = MicroQRCodeGenerator.CreateMicroQRCode(content, MicroQREccLevel.L, new MicroQRCodeGeneratorOptions { Segmentation = MicroQRSegmentation.Optimal });

        await Assert.That(optimal.Version).IsEqualTo(single.Version);
        await Assert.That(optimal.GetRawData()).IsEquivalentTo(single.GetRawData());
    }

    [Test]
    public async Task Optimal_TooLongForEverySingleMode_StillEncodesWhenMixedFits()
    {
        // 21 Byte-mode characters overflow M4-L (15 bytes), but a Byte run plus a
        // Numeric run fit; this is the case Optimal accepts input Single rejects.
        var content = "a" + new string('1', 20);

        await Assert.That(MicroQRCodeGenerator.TryGetRequiredBufferSize(content, MicroQREccLevel.L, out _, MicroQRCodeGeneratorOptions.Default)).IsFalse();

        var options = new MicroQRCodeGeneratorOptions { Segmentation = MicroQRSegmentation.Optimal };
        await Assert.That(MicroQRCodeGenerator.TryGetRequiredBufferSize(content, MicroQREccLevel.L, out var size, options)).IsTrue();

        var data = MicroQRCodeGenerator.CreateMicroQRCode(content, MicroQREccLevel.L, options);
        await Assert.That(data.Version).IsEqualTo(size.Version);

        await Assert.That(MicroQRCodeDecoder.TryDecode(data, out var decoded)).IsTrue();
        await Assert.That(decoded).IsEqualTo(content);
    }

    [Test]
    public async Task Optimal_RequestedVersion_FitsWhereSingleModeDoesNot()
    {
        // At M3-L the single Alphanumeric stream (19 chars = 111 bits > 84)
        // overflows, but Alnum(2) + Numeric(17) = 81 bits fits.
        var content = "AB12345678901234567";
        Assert.Throws<ArgumentException>(() => MicroQRCodeGenerator.CreateMicroQRCode(content, MicroQREccLevel.L, new MicroQRCodeGeneratorOptions { Version = MicroQRVersion.M3 }));

        var data = MicroQRCodeGenerator.CreateMicroQRCode(content, MicroQREccLevel.L, new MicroQRCodeGeneratorOptions { Version = MicroQRVersion.M3, Segmentation = MicroQRSegmentation.Optimal });
        await Assert.That(data.Version).IsEqualTo(MicroQRVersion.M3);

        await Assert.That(MicroQRCodeDecoder.TryDecode(data, out var decoded)).IsTrue();
        await Assert.That(decoded).IsEqualTo(content);
    }

    [Test]
    public async Task Optimal_PlansAtM2_WhereByteModeDoesNotExist()
    {
        // M2 offers only Numeric and Alphanumeric; the plan must respect that mode
        // set. "A" + 7 digits: single Alphanumeric is 8 chars = 44 + 4 header = 48
        // bits > M2-L's 40, landing on M3; split Alnum(1) + Numeric(7) =
        // (1+3+6) + (1+4+24) = 39 bits fits M2-L exactly under the cap.
        var content = "A1234567";
        var single = MicroQRCodeGenerator.CreateMicroQRCode(content, MicroQREccLevel.L, MicroQRCodeGeneratorOptions.Default);
        var optimal = MicroQRCodeGenerator.CreateMicroQRCode(content, MicroQREccLevel.L, new MicroQRCodeGeneratorOptions { Segmentation = MicroQRSegmentation.Optimal });

        await Assert.That(optimal.Version).IsEqualTo(MicroQRVersion.M2);
        await Assert.That((int)optimal.Version).IsLessThan((int)single.Version);

        await Assert.That(MicroQRCodeDecoder.TryDecode(optimal, out var decoded)).IsTrue();
        await Assert.That(decoded).IsEqualTo(content);
    }

    [Test]
    public async Task Optimal_WithPinnedMask_UsesThatMask()
    {
        var content = "AB12345678901234567";
        var data = MicroQRCodeGenerator.CreateMicroQRCode(content, MicroQREccLevel.L, new MicroQRCodeGeneratorOptions { Segmentation = MicroQRSegmentation.Optimal, MaskPattern = 2 });

        await Assert.That(MicroQRCodeDecoder.TryDecode(data, out var decoded, out var info)).IsTrue();
        await Assert.That(decoded).IsEqualTo(content);
        await Assert.That(info.MaskPattern).IsEqualTo(2);
    }

    [Test]
    public async Task Optimal_InvalidSegmentationValue_Throws_OnEveryEntryPoint()
    {
        var options = new MicroQRCodeGeneratorOptions { Segmentation = (MicroQRSegmentation)5 };
        var buffer = new byte[1024];

        var fromCreate = Assert.Throws<ArgumentOutOfRangeException>(() => MicroQRCodeGenerator.CreateMicroQRCode("12345", MicroQREccLevel.L, options));
        var fromCreateSpan = Assert.Throws<ArgumentOutOfRangeException>(() => MicroQRCodeGenerator.CreateMicroQRCode("12345".AsSpan(), MicroQREccLevel.L, buffer, options));
        var fromSizing = Assert.Throws<ArgumentOutOfRangeException>(() => MicroQRCodeGenerator.TryGetRequiredBufferSize("12345", MicroQREccLevel.L, out _, options));

        await Assert.That(fromCreate.ParamName).IsEqualTo("segmentation");
        await Assert.That(fromCreateSpan.ParamName).IsEqualTo("segmentation");
        await Assert.That(fromSizing.ParamName).IsEqualTo("segmentation");
    }

    [Test]
    public async Task Optimal_NegativeQuietZone_WinsOverInvalidSegmentation_OnEveryEntryPoint()
    {
        var options = new MicroQRCodeGeneratorOptions { Segmentation = (MicroQRSegmentation)3, QuietZoneSize = -1 };
        var buffer = new byte[1024];

        var fromCreate = Assert.Throws<ArgumentOutOfRangeException>(() => MicroQRCodeGenerator.CreateMicroQRCode("12345", MicroQREccLevel.L, options));
        var fromCreateSpan = Assert.Throws<ArgumentOutOfRangeException>(() => MicroQRCodeGenerator.CreateMicroQRCode("12345".AsSpan(), MicroQREccLevel.L, buffer, options));
        var fromSizing = Assert.Throws<ArgumentOutOfRangeException>(() => MicroQRCodeGenerator.TryGetRequiredBufferSize("12345", MicroQREccLevel.L, out _, options));

        await Assert.That(fromCreate.ParamName).IsEqualTo("quietZoneSize");
        await Assert.That(fromCreateSpan.ParamName).IsEqualTo("quietZoneSize");
        await Assert.That(fromSizing.ParamName).IsEqualTo("quietZoneSize");
    }

    [Test]
    [MethodDataSource(nameof(Corpus))]
    public async Task Optimal_SpanDestination_MatchesTheAllocatingOverload(string content)
    {
        foreach (var quietZone in new[] { 0, 2 })
        {
            var options = new MicroQRCodeGeneratorOptions { Segmentation = MicroQRSegmentation.Optimal, QuietZoneSize = quietZone };
            await Assert.That(MicroQRCodeGenerator.TryGetRequiredBufferSize(content, MicroQREccLevel.L, out var size, options)).IsTrue();

            var buffer = new byte[size.BufferSize];
            var written = MicroQRCodeGenerator.CreateMicroQRCode(content.AsSpan(), MicroQREccLevel.L, buffer, options);
            await Assert.That(written).IsEqualTo(size.BufferSize);

            var expected = MicroQRCodeGenerator.CreateMicroQRCode(content, MicroQREccLevel.L, options);
            for (var row = 0; row < size.QrSize; row++)
            {
                for (var col = 0; col < size.QrSize; col++)
                {
                    if (expected[row, col] != (buffer[row * size.QrSize + col] != 0))
                        throw new InvalidOperationException($"module mismatch at ({row}, {col}), quietZone={quietZone}");
                }
            }
        }
    }

    [Test]
    public async Task Builder_WithSegmentation_ProducesADecodableImage()
    {
        var content = "AB12345678901234567";
        var png = new Image.MicroQRCodeImageBuilder(content)
            .WithErrorCorrection(MicroQREccLevel.L)
            .WithSegmentation(MicroQRSegmentation.Optimal)
            .ToByteArray();
        await Assert.That(png.Length).IsGreaterThan(0);

        Assert.Throws<ArgumentOutOfRangeException>(() => new Image.MicroQRCodeImageBuilder("12345").WithSegmentation((MicroQRSegmentation)5));
        var data = MicroQRCodeGenerator.CreateMicroQRCode("12345", MicroQREccLevel.L);
        Assert.Throws<InvalidOperationException>(() => new Image.MicroQRCodeImageBuilder(data).WithSegmentation(MicroQRSegmentation.Optimal));
    }

    [Test]
    public async Task Optimal_AgreesWithTryGetRequiredBufferSize()
    {
        foreach (var content in Corpus())
        {
            var options = new MicroQRCodeGeneratorOptions { Segmentation = MicroQRSegmentation.Optimal };
            await Assert.That(MicroQRCodeGenerator.TryGetRequiredBufferSize(content, MicroQREccLevel.L, out var size, options)).IsTrue();

            var buffer = new byte[size.BufferSize];
            var written = MicroQRCodeGenerator.CreateMicroQRCode(content.AsSpan(), MicroQREccLevel.L, buffer, options);
            await Assert.That(written).IsEqualTo(size.BufferSize);

            var data = MicroQRCodeGenerator.CreateMicroQRCode(content, MicroQREccLevel.L, options);
            await Assert.That(data.Version).IsEqualTo(size.Version);
        }
    }
}
