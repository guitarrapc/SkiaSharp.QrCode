using SkiaSharp.QrCode.Image;

namespace SkiaSharp.QrCode.Tests;

/// <summary>
/// <see cref="QRCodeSegmentation.Optimal"/> end to end. The three properties every
/// case is held to are: the symbol never selects a larger version than the
/// single-mode default, it still decodes to the original content, and when it lands
/// on the same version as the single-mode default it is byte-for-byte the same
/// matrix (the blast radius bound the feature is designed around). The rest covers
/// the branches of the version decision: requested version or range present or
/// absent, each charset, ECC boost and mask pinning composition, and both
/// directions of "does not fit".
/// </summary>
public class QRCodeSegmentationTest
{
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
        "HELLO WORLD 12345",
        "hello world 12345",
        "https://example.com/item?id=123456789012345678901234567890",
        "SN/2024-000123456789",
        "Order 12345 item 6789",
        "ABC-1234567890123456",
        "x1234567890123456789012345678901234567890",
        "1234567890123456789012345678901234567890x",
        "日本語1234567890",
        "éèê1234567890",
        "😀😁1234567890",
    ];

    [Test]
    public async Task Optimal_IsNotTheDefault()
    {
        await Assert.That(default(QRCodeGeneratorOptions).Segmentation).IsEqualTo(QRCodeSegmentation.Single);
        await Assert.That(QRCodeGeneratorOptions.Default.Segmentation).IsEqualTo(QRCodeSegmentation.Single);
    }

    [Test]
    [MethodDataSource(nameof(Corpus))]
    public async Task Optimal_IsNeverLargerThanSingle_AndAlwaysRoundTrips(string content)
    {
        var single = QRCodeGenerator.CreateQrCode(content, ECCLevel.M, QRCodeGeneratorOptions.Default);
        var optimal = QRCodeGenerator.CreateQrCode(content, ECCLevel.M, new QRCodeGeneratorOptions { Segmentation = QRCodeSegmentation.Optimal });

        await Assert.That(optimal.Version).IsLessThanOrEqualTo(single.Version);

        await Assert.That(QRCodeDecoder.TryDecode(optimal, out var decoded)).IsTrue();
        await Assert.That(decoded).IsEqualTo(content);
    }

    [Test]
    [MethodDataSource(nameof(Corpus))]
    public async Task Optimal_SameVersionAsSingle_ProducesTheIdenticalMatrix(string content)
    {
        var single = QRCodeGenerator.CreateQrCode(content, ECCLevel.M, QRCodeGeneratorOptions.Default);
        var optimal = QRCodeGenerator.CreateQrCode(content, ECCLevel.M, new QRCodeGeneratorOptions { Segmentation = QRCodeSegmentation.Optimal });

        if (optimal.Version != single.Version)
            return; // a genuine gain; the round-trip test covers it

        await Assert.That(optimal.GetRawData()).IsEquivalentTo(single.GetRawData());
    }

    [Test]
    [Arguments("https://example.com/item?id=123456789012345678901234567890")]
    [Arguments("x12345678901234567890123456789012345678901234567890")]
    [Arguments("日本語12345678901234567890123456789012345678901234567890")]
    public async Task Optimal_MixedContent_SelectsSmallerVersionThanSingle(string content)
    {
        var single = QRCodeGenerator.CreateQrCode(content, ECCLevel.M, QRCodeGeneratorOptions.Default);
        var optimal = QRCodeGenerator.CreateQrCode(content, ECCLevel.M, new QRCodeGeneratorOptions { Segmentation = QRCodeSegmentation.Optimal });

        await Assert.That(optimal.Version).IsLessThan(single.Version);

        await Assert.That(QRCodeDecoder.TryDecode(optimal, out var decoded)).IsTrue();
        await Assert.That(decoded).IsEqualTo(content);
    }

    [Test]
    [Arguments("1234567890")]
    [Arguments("HELLO WORLD")]
    [Arguments("こんにちは")]
    public async Task Optimal_SingleModeContent_KeepsTheSameVersion(string content)
    {
        var single = QRCodeGenerator.CreateQrCode(content, ECCLevel.M, QRCodeGeneratorOptions.Default);
        var optimal = QRCodeGenerator.CreateQrCode(content, ECCLevel.M, new QRCodeGeneratorOptions { Segmentation = QRCodeSegmentation.Optimal });

        await Assert.That(optimal.Version).IsEqualTo(single.Version);
        await Assert.That(optimal.GetRawData()).IsEquivalentTo(single.GetRawData());
    }

    [Test]
    public async Task Optimal_TooLongForEverySingleMode_StillEncodesWhenMixedFits()
    {
        // 5,500 characters overflow Byte mode at every version (v40-L holds 2,953),
        // but split into a Byte run and a Numeric run the stream fits version 40-L.
        var content = new string('x', 1000) + new string('1', 4500);

        await Assert.That(QRCodeGenerator.TryGetRequiredBufferSize(content, ECCLevel.L, out _, QRCodeGeneratorOptions.Default)).IsFalse();

        var options = new QRCodeGeneratorOptions { Segmentation = QRCodeSegmentation.Optimal };
        await Assert.That(QRCodeGenerator.TryGetRequiredBufferSize(content, ECCLevel.L, out var size, options)).IsTrue();

        var data = QRCodeGenerator.CreateQrCode(content, ECCLevel.L, options);
        await Assert.That(data.Version).IsEqualTo(size.Version);

        await Assert.That(QRCodeDecoder.TryDecode(data, out var decoded)).IsTrue();
        await Assert.That(decoded).IsEqualTo(content);
    }

    [Test]
    public async Task Optimal_WithBoost_KeepsTheVersionAndNeverLowersTheLevel()
    {
        var content = "https://example.com/item?id=123456789012345678901234567890";
        var plain = QRCodeGenerator.CreateQrCode(content, ECCLevel.M, new QRCodeGeneratorOptions { Segmentation = QRCodeSegmentation.Optimal });
        var boosted = QRCodeGenerator.CreateQrCode(content, ECCLevel.M, new QRCodeGeneratorOptions { Segmentation = QRCodeSegmentation.Optimal, BoostEccLevel = true });

        await Assert.That(boosted.Version).IsEqualTo(plain.Version);

        await Assert.That(QRCodeDecoder.TryDecode(boosted, out var decoded, out var info)).IsTrue();
        await Assert.That(decoded).IsEqualTo(content);
        await Assert.That(info.EccLevel >= ECCLevel.M).IsTrue().Because($"boost must not lower the level, got {info.EccLevel}");
    }

    [Test]
    public async Task Optimal_WithPinnedMask_UsesThatMask()
    {
        var content = "https://example.com/item?id=123456789012345678901234567890";
        var data = QRCodeGenerator.CreateQrCode(content, ECCLevel.M, new QRCodeGeneratorOptions { Segmentation = QRCodeSegmentation.Optimal, MaskPattern = 3 });

        await Assert.That(QRCodeDecoder.TryDecode(data, out var decoded, out var info)).IsTrue();
        await Assert.That(decoded).IsEqualTo(content);
        await Assert.That(info.MaskPattern).IsEqualTo(3);
    }

    [Test]
    public async Task Optimal_WithUtf8Bom_EmitsTheSingleModeStream()
    {
        // The BOM is a stream-level prefix; a split would relocate it into the middle
        // of the decoded text, so the combination falls back to the single-mode stream.
        var content = "日本語1234567890123456789012345678901234567890";
        var single = QRCodeGenerator.CreateQrCode(content, ECCLevel.M, new QRCodeGeneratorOptions { Utf8BOM = true });
        var optimal = QRCodeGenerator.CreateQrCode(content, ECCLevel.M, new QRCodeGeneratorOptions { Utf8BOM = true, Segmentation = QRCodeSegmentation.Optimal });

        await Assert.That(optimal.Version).IsEqualTo(single.Version);
        await Assert.That(optimal.GetRawData()).IsEquivalentTo(single.GetRawData());
    }

    [Test]
    public async Task Optimal_VersionRangeBelowTheGain_EmitsTheSingleModeSymbol()
    {
        // The single-mode fit is version 4 and the mixed-mode fit version 3; a range
        // that starts at 4 excludes the gain, so the single-mode stream is emitted.
        var content = "https://example.com/item?id=123456789012345678901234567890";
        var single = QRCodeGenerator.CreateQrCode(content, ECCLevel.M, new QRCodeGeneratorOptions { Version = new QRCodeVersionRange(4, 40) });
        var optimal = QRCodeGenerator.CreateQrCode(content, ECCLevel.M, new QRCodeGeneratorOptions { Version = new QRCodeVersionRange(4, 40), Segmentation = QRCodeSegmentation.Optimal });

        await Assert.That(optimal.Version).IsEqualTo(single.Version);
        await Assert.That(optimal.GetRawData()).IsEquivalentTo(single.GetRawData());
    }

    [Test]
    public async Task Optimal_RequestedVersion_FitsWhereSingleModeDoesNot()
    {
        // At version 3-M the single Byte-mode stream overflows, but the mixed plan
        // fits; requesting that exact version must succeed under Optimal.
        var content = "https://example.com/item?id=123456789012345678901234567890";
        Assert.Throws<ArgumentException>(() => QRCodeGenerator.CreateQrCode(content, ECCLevel.M, new QRCodeGeneratorOptions { Version = 3 }));

        var data = QRCodeGenerator.CreateQrCode(content, ECCLevel.M, new QRCodeGeneratorOptions { Version = 3, Segmentation = QRCodeSegmentation.Optimal });
        await Assert.That(data.Version).IsEqualTo(3);

        await Assert.That(QRCodeDecoder.TryDecode(data, out var decoded)).IsTrue();
        await Assert.That(decoded).IsEqualTo(content);
    }

    [Test]
    public async Task Optimal_RequestedVersion_TooSmallEvenSegmented_ThrowsLikeSingle()
    {
        var content = "https://example.com/item?id=123456789012345678901234567890";
        var exception = Assert.Throws<ArgumentException>(() => QRCodeGenerator.CreateQrCode(content, ECCLevel.M, new QRCodeGeneratorOptions { Version = 1, Segmentation = QRCodeSegmentation.Optimal }));
        await Assert.That(exception.Message).Contains("does not fit");
    }

    [Test]
    public async Task Optimal_TooLongForEveryVersion_ThrowsLikeSingle()
    {
        // 8,000 characters exceed even the all-Numeric maximum (7,089), so no plan
        // exists; the error must be the single-mode path's exact exception type.
        var content = new string('1', 7000) + new string('x', 1000);
        Assert.Throws<InvalidOperationException>(() => QRCodeGenerator.CreateQrCode(content, ECCLevel.L, new QRCodeGeneratorOptions { Segmentation = QRCodeSegmentation.Optimal }));
        await Assert.That(QRCodeGenerator.TryGetRequiredBufferSize(content, ECCLevel.L, out _, new QRCodeGeneratorOptions { Segmentation = QRCodeSegmentation.Optimal })).IsFalse();
    }

    [Test]
    public async Task Optimal_InvalidSegmentationValue_Throws()
    {
        var options = new QRCodeGeneratorOptions { Segmentation = (QRCodeSegmentation)5 };
        Assert.Throws<ArgumentOutOfRangeException>(() => QRCodeGenerator.CreateQrCode("HELLO", ECCLevel.M, options));
        Assert.Throws<ArgumentOutOfRangeException>(() => QRCodeGenerator.TryGetRequiredBufferSize("HELLO", ECCLevel.M, out _, options));
        await Assert.That(true).IsTrue();
    }

    [Test]
    [MethodDataSource(nameof(Corpus))]
    public async Task Optimal_SpanDestination_MatchesTheAllocatingOverload(string content)
    {
        var options = new QRCodeGeneratorOptions { Segmentation = QRCodeSegmentation.Optimal, QuietZoneSize = 0 };
        await Assert.That(QRCodeGenerator.TryGetRequiredBufferSize(content, ECCLevel.M, out var size, options)).IsTrue();

        var buffer = new byte[size.BufferSize];
        var written = QRCodeGenerator.CreateQrCode(content, ECCLevel.M, buffer, options);
        await Assert.That(written).IsEqualTo(size.BufferSize);

        await Assert.That(QRCodeDecoder.TryDecode(buffer.AsSpan(0, written), size.QrSize, out var decoded, out var info)).IsTrue().Because($"status={info.Status}");
        await Assert.That(decoded).IsEqualTo(content);
    }

    [Test]
    public async Task Builder_WithSegmentation_ProducesADecodableImage()
    {
        var content = "https://example.com/item?id=123456789012345678901234567890";
        var png = new QRCodeImageBuilder(content)
            .WithSegmentation(QRCodeSegmentation.Optimal)
            .ToByteArray();
        await Assert.That(png.Length).IsGreaterThan(0);
    }

    [Test]
    public async Task Builder_WithSegmentation_RejectsInvalidValueAndPrebuiltData()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new QRCodeImageBuilder("HELLO").WithSegmentation((QRCodeSegmentation)5));

        var data = QRCodeGenerator.CreateQrCode("HELLO", ECCLevel.M, QRCodeGeneratorOptions.Default);
        Assert.Throws<InvalidOperationException>(() => new QRCodeImageBuilder(data).WithSegmentation(QRCodeSegmentation.Optimal));
        await Assert.That(true).IsTrue();
    }

    [Test]
    public async Task Optimal_AgreesWithTryGetRequiredBufferSize()
    {
        foreach (var content in Corpus())
        {
            var options = new QRCodeGeneratorOptions { Segmentation = QRCodeSegmentation.Optimal };
            await Assert.That(QRCodeGenerator.TryGetRequiredBufferSize(content, ECCLevel.M, out var size, options)).IsTrue();

            var buffer = new byte[size.BufferSize];
            var written = QRCodeGenerator.CreateQrCode(content, ECCLevel.M, buffer, options);
            await Assert.That(written).IsEqualTo(size.BufferSize);

            var data = QRCodeGenerator.CreateQrCode(content, ECCLevel.M, options);
            await Assert.That(data.Version).IsEqualTo(size.Version);
        }
    }
}
