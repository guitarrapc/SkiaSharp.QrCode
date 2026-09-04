namespace FeatherQR.Tests;

/// <summary>
/// The exception type every argument-error path of the sizing surface actually raises,
/// pinned case by case.
/// </summary>
/// <remarks>
/// <para>
/// This exists because the XML docs on these methods were repeatedly wrong about it and
/// nothing could tell. A doc comment has no checker behind it, so review was the only
/// thing catching the drift, one comment at a time. The table below is what those docs are
/// now written from, so a doc that disagrees with reality also disagrees with a test.
/// </para>
/// <para>
/// Two inconsistencies are deliberately pinned rather than fixed, because they are the
/// behaviour released in v1.1.1: Standard QR reports a content overflow as
/// <see cref="InvalidOperationException"/> while Micro QR reports it as
/// <see cref="ArgumentException"/>, and Standard QR reports an undefined
/// <see cref="ECCLevel"/> as <see cref="ArgumentException"/> where the other two use
/// <see cref="ArgumentOutOfRangeException"/>. Changing either is a break, so they stay
/// frozen for as long as the deprecated members exist.
/// </para>
/// </remarks>
public class SizingExceptionContractTest
{
    private static readonly string TooLong = new('0', 8000);
    private static readonly string NotLatin1 = "日本語";

    /// <summary>Label, the call, and the exception it must raise (<c>null</c> = must not throw).</summary>
    public static IEnumerable<(string Label, Action Call, Type? Expected)> Cases()
    {
#pragma warning disable CS0618 // the released sizing overloads are part of the contract under test
        yield return ("QR Get / content too long", () => QRCodeGenerator.GetRequiredBufferSize(TooLong.AsSpan(), ECCLevel.L), typeof(InvalidOperationException));
        yield return ("QR Get / quiet zone negative", () => QRCodeGenerator.GetRequiredBufferSize("1".AsSpan(), ECCLevel.L, quietZoneSize: -1), typeof(ArgumentOutOfRangeException));
        yield return ("QR Get / quiet zone int.MaxValue", () => QRCodeGenerator.GetRequiredBufferSize("1".AsSpan(), ECCLevel.L, quietZoneSize: int.MaxValue), typeof(ArgumentOutOfRangeException));
        yield return ("QR Get / quiet zone squares past int.MaxValue", () => QRCodeGenerator.GetRequiredBufferSize("1".AsSpan(), ECCLevel.L, quietZoneSize: 40000), typeof(ArgumentOutOfRangeException));
        yield return ("QR Get / ECC undefined", () => QRCodeGenerator.GetRequiredBufferSize("1".AsSpan(), (ECCLevel)9), typeof(ArgumentException));

        yield return ("Micro Get / content too long", () => MicroQRCodeGenerator.GetRequiredBufferSize(TooLong.AsSpan(), MicroQREccLevel.L), typeof(ArgumentException));
        yield return ("Micro Get / quiet zone negative", () => MicroQRCodeGenerator.GetRequiredBufferSize("1".AsSpan(), MicroQREccLevel.L, quietZoneSize: -1), typeof(ArgumentOutOfRangeException));
        yield return ("Micro Get / quiet zone above 10000", () => MicroQRCodeGenerator.GetRequiredBufferSize("1".AsSpan(), MicroQREccLevel.L, quietZoneSize: 10001), typeof(ArgumentOutOfRangeException));
        yield return ("Micro Get / ECC undefined", () => MicroQRCodeGenerator.GetRequiredBufferSize("1".AsSpan(), (MicroQREccLevel)9), typeof(ArgumentOutOfRangeException));
        yield return ("Micro Get / version undefined", () => MicroQRCodeGenerator.GetRequiredBufferSize("1".AsSpan(), MicroQREccLevel.L, (MicroQRVersion)9), typeof(ArgumentOutOfRangeException));
        yield return ("Micro Get / M1 with ECC L", () => MicroQRCodeGenerator.GetRequiredBufferSize("1".AsSpan(), MicroQREccLevel.L, MicroQRVersion.M1), typeof(ArgumentException));
#pragma warning restore CS0618

        yield return ("QR Try / content too long", () => QRCodeGenerator.TryGetRequiredBufferSize(TooLong.AsSpan(), ECCLevel.L, out _), null);
        yield return ("QR Try / quiet zone negative", () => QRCodeGenerator.TryGetRequiredBufferSize("1".AsSpan(), ECCLevel.L, out _, new QRCodeGeneratorOptions { QuietZoneSize = -1 }), typeof(ArgumentOutOfRangeException));
        yield return ("QR Try / quiet zone squares past int.MaxValue", () => QRCodeGenerator.TryGetRequiredBufferSize("1".AsSpan(), ECCLevel.L, out _, new QRCodeGeneratorOptions { QuietZoneSize = 40000 }), typeof(ArgumentOutOfRangeException));
        yield return ("QR Try / ECC undefined", () => QRCodeGenerator.TryGetRequiredBufferSize("1".AsSpan(), (ECCLevel)9, out _), typeof(ArgumentException));
        yield return ("QR Try / segmentation undefined", () => QRCodeGenerator.TryGetRequiredBufferSize("1".AsSpan(), ECCLevel.L, out _, new QRCodeGeneratorOptions { Segmentation = (QRCodeSegmentation)7 }), typeof(ArgumentOutOfRangeException));

        yield return ("Micro Try / content too long", () => MicroQRCodeGenerator.TryGetRequiredBufferSize(TooLong.AsSpan(), MicroQREccLevel.L, out _), null);
        yield return ("Micro Try / quiet zone negative", () => MicroQRCodeGenerator.TryGetRequiredBufferSize("1".AsSpan(), MicroQREccLevel.L, out _, new MicroQRCodeGeneratorOptions { QuietZoneSize = -1 }), typeof(ArgumentOutOfRangeException));
        yield return ("Micro Try / quiet zone above 10000", () => MicroQRCodeGenerator.TryGetRequiredBufferSize("1".AsSpan(), MicroQREccLevel.L, out _, new MicroQRCodeGeneratorOptions { QuietZoneSize = 10001 }), typeof(ArgumentOutOfRangeException));
        yield return ("Micro Try / ECC undefined", () => MicroQRCodeGenerator.TryGetRequiredBufferSize("1".AsSpan(), (MicroQREccLevel)9, out _), typeof(ArgumentOutOfRangeException));
        yield return ("Micro Try / version undefined", () => MicroQRCodeGenerator.TryGetRequiredBufferSize("1".AsSpan(), MicroQREccLevel.L, out _, new MicroQRCodeGeneratorOptions { Version = (MicroQRVersion)9 }), typeof(ArgumentOutOfRangeException));
        yield return ("Micro Try / M1 with ECC L", () => MicroQRCodeGenerator.TryGetRequiredBufferSize("1".AsSpan(), MicroQREccLevel.L, out _, new MicroQRCodeGeneratorOptions { Version = MicroQRVersion.M1 }), typeof(ArgumentException));
        yield return ("Micro Try / segmentation undefined", () => MicroQRCodeGenerator.TryGetRequiredBufferSize("1".AsSpan(), MicroQREccLevel.L, out _, new MicroQRCodeGeneratorOptions { Segmentation = (MicroQRSegmentation)7 }), typeof(ArgumentOutOfRangeException));

        yield return ("rMQR Try / content too long", () => RmQRCodeGenerator.TryGetRequiredBufferSize(TooLong.AsSpan(), RmQREccLevel.M, out _), null);
        yield return ("rMQR Try / quiet zone negative", () => RmQRCodeGenerator.TryGetRequiredBufferSize("1".AsSpan(), RmQREccLevel.M, out _, new RmQRCodeGeneratorOptions { QuietZoneSize = -1 }), typeof(ArgumentOutOfRangeException));
        yield return ("rMQR Try / quiet zone out of range", () => RmQRCodeGenerator.TryGetRequiredBufferSize("1".AsSpan(), RmQREccLevel.M, out _, new RmQRCodeGeneratorOptions { QuietZoneSize = 40000 }), typeof(ArgumentOutOfRangeException));
        yield return ("rMQR Try / ECC undefined", () => RmQRCodeGenerator.TryGetRequiredBufferSize("1".AsSpan(), (RmQREccLevel)2, out _), typeof(ArgumentOutOfRangeException));
        yield return ("rMQR Try / version undefined", () => RmQRCodeGenerator.TryGetRequiredBufferSize("1".AsSpan(), RmQREccLevel.M, out _, new RmQRCodeGeneratorOptions { Version = (RmQRVersion)0 }), typeof(ArgumentOutOfRangeException));
        yield return ("rMQR Try / fit strategy undefined", () => RmQRCodeGenerator.TryGetRequiredBufferSize("1".AsSpan(), RmQREccLevel.M, out _, new RmQRCodeGeneratorOptions { FitStrategy = (RmQRFitStrategy)5 }), typeof(ArgumentOutOfRangeException));
        yield return ("rMQR Try / segmentation undefined", () => RmQRCodeGenerator.TryGetRequiredBufferSize("1".AsSpan(), RmQREccLevel.M, out _, new RmQRCodeGeneratorOptions { Segmentation = (RmQRSegmentation)7 }), typeof(ArgumentOutOfRangeException));
        yield return ("rMQR Try / version disagrees with height", () => RmQRCodeGenerator.TryGetRequiredBufferSize("1".AsSpan(), RmQREccLevel.M, out _, new RmQRCodeGeneratorOptions { Version = RmQRVersion.R7x43, Height = RmQRHeight.H11 }), typeof(ArgumentException));
        yield return ("rMQR Try / Iso8859_1 over non-Latin-1", () => RmQRCodeGenerator.TryGetRequiredBufferSize(NotLatin1.AsSpan(), RmQREccLevel.M, out _, new RmQRCodeGeneratorOptions { EciMode = EciMode.Iso8859_1 }), typeof(ArgumentException));
    }

    [Test]
    [MethodDataSource(nameof(Cases))]
    public async Task SizingRaisesExactlyTheDocumentedException(string label, Action call, Type? expected)
    {
        Type? actual = null;
        try
        {
            call();
        }
        catch (Exception e)
        {
            actual = e.GetType();
        }

        // Compared by name so a failure names both sides in one line.
        await Assert.That($"{label}: {actual?.Name ?? "(no throw)"}").IsEqualTo($"{label}: {expected?.Name ?? "(no throw)"}");
    }

    /// <summary>
    /// The headline of the change, stated once: an overflow is <c>false</c> on every
    /// symbology, and the reported size is left at its default.
    /// </summary>
    [Test]
    public async Task NonThrowingSizing_ReportsOverflowAsFalse_OnEverySymbology()
    {
        await Assert.That(QRCodeGenerator.TryGetRequiredBufferSize(TooLong.AsSpan(), ECCLevel.L, out var standard)).IsFalse();
        await Assert.That(standard).IsEqualTo(default(QRCodeCalculatedSize));

        await Assert.That(MicroQRCodeGenerator.TryGetRequiredBufferSize(TooLong.AsSpan(), MicroQREccLevel.L, out var micro)).IsFalse();
        await Assert.That(micro).IsEqualTo(default(MicroQRCodeCalculatedSize));

        await Assert.That(RmQRCodeGenerator.TryGetRequiredBufferSize(TooLong.AsSpan(), RmQREccLevel.M, out var rmqr)).IsFalse();
        await Assert.That(rmqr).IsEqualTo(default(RmQRCodeCalculatedSize));
    }
}
