namespace SkiaSharp.QrCode.Tests;

/// <summary>
/// Buffer sizing for call sites where the fit is a <em>precondition</em> of the test
/// rather than the thing under test.
/// </summary>
/// <remarks>
/// The generators answer "how big is this" only as a <c>Try</c>
/// (plans/generator-api-options-plan.md, Phase 6), because for a caller handling
/// arbitrary content "it does not fit" is an ordinary answer. A test that has chosen its
/// own content knows it fits, and would rather crash than branch — so the throw lives
/// here, once, where it is obviously a test assertion and not a library contract.
///
/// Do not use this in a test whose subject <em>is</em> the fit: assert on
/// <c>TryGetRequiredBufferSize</c> directly there, so a wrong answer reads as a failed
/// assertion rather than an exception from a helper.
/// </remarks>
internal static class Sizing
{
    public static QRCodeCalculatedSize Required(ReadOnlySpan<char> text, ECCLevel eccLevel, in QRCodeGeneratorOptions options = default)
        => QRCodeGenerator.TryGetRequiredBufferSize(text, eccLevel, out var size, options)
            ? size
            : throw DoesNotFit("Standard QR", text.Length, eccLevel);

    public static MicroQRCodeCalculatedSize Required(ReadOnlySpan<char> text, MicroQREccLevel eccLevel, in MicroQRCodeGeneratorOptions options = default)
        => MicroQRCodeGenerator.TryGetRequiredBufferSize(text, eccLevel, out var size, options)
            ? size
            : throw DoesNotFit("Micro QR", text.Length, eccLevel);

    public static RmQRCodeCalculatedSize Required(ReadOnlySpan<char> text, RmQREccLevel eccLevel, in RmQRCodeGeneratorOptions options = default)
        => RmQRCodeGenerator.TryGetRequiredBufferSize(text, eccLevel, out var size, options)
            ? size
            : throw DoesNotFit("rMQR", text.Length, eccLevel);

    private static InvalidOperationException DoesNotFit<TEcc>(string symbology, int length, TEcc eccLevel)
        => new($"Test precondition failed: {length} characters do not fit any {symbology} symbol at ECC level {eccLevel}.");

    public static QRCodeCalculatedSize Required(ReadOnlySpan<char> text, ECCLevel eccLevel, int quietZoneSize)
        => Required(text, eccLevel, new QRCodeGeneratorOptions { QuietZoneSize = quietZoneSize });

    public static MicroQRCodeCalculatedSize Required(ReadOnlySpan<char> text, MicroQREccLevel eccLevel, int quietZoneSize)
        => Required(text, eccLevel, new MicroQRCodeGeneratorOptions { QuietZoneSize = quietZoneSize });

    public static RmQRCodeCalculatedSize Required(ReadOnlySpan<char> text, RmQREccLevel eccLevel, int quietZoneSize)
        => Required(text, eccLevel, new RmQRCodeGeneratorOptions { QuietZoneSize = quietZoneSize });

    // The parameter list sizing overloads are [Obsolete] until 2.0.0. Parity tests still
    // have to call them — comparing the replacement against the thing it replaces is the
    // whole point — so the suppression lives here once instead of in every such test.
#pragma warning disable CS0618 // GetRequiredBufferSize (parameter list)

    public static QRCodeCalculatedSize ReleasedRequired(ReadOnlySpan<char> text, ECCLevel eccLevel, bool utf8BOM = false, EciMode eciMode = EciMode.Default, int quietZoneSize = 4)
        => QRCodeGenerator.GetRequiredBufferSize(text, eccLevel, utf8BOM, eciMode, quietZoneSize);

    public static MicroQRCodeCalculatedSize ReleasedRequired(ReadOnlySpan<char> text, MicroQREccLevel eccLevel, MicroQRVersion? requestedVersion = null, int quietZoneSize = 2)
        => MicroQRCodeGenerator.GetRequiredBufferSize(text, eccLevel, requestedVersion, quietZoneSize);

#pragma warning restore CS0618 // GetRequiredBufferSize (parameter list)
}
