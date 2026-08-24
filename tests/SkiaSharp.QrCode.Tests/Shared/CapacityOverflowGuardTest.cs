using SkiaSharp.QrCode.Internals;
using SkiaSharp.QrCode.Internals.RmQr;

namespace SkiaSharp.QrCode.Tests;

/// <summary>
/// Byte-mode capacity pricing multiplies the data length by 8, which wraps <see cref="int"/>
/// past ~268M units and makes an absurd payload look like it fits. Reachable only with a
/// quarter-gigabyte span, so these drive the selectors directly rather than allocating one.
/// </summary>
public class CapacityOverflowGuardTest
{
    // 8 x this exceeds int.MaxValue; the largest symbol of any symbology holds 7,089 units.
    private const int OverflowingByteLength = 300_000_000;

    [Test]
    [Arguments(RmQRVersion.R7x43)]
    [Arguments(RmQRVersion.R17x139)]
    public async Task RmQR_Fits_RejectsLengthThatWrapsTheBitCount(RmQRVersion version)
    {
        await Assert.That(RmQRVersionSelector.Fits(version, RmQREccLevel.M, EncodingMode.Byte, OverflowingByteLength)).IsFalse();
        await Assert.That(RmQRVersionSelector.Fits(version, RmQREccLevel.M, EncodingMode.Byte, OverflowingByteLength, EciMode.Utf8)).IsFalse();
    }

    [Test]
    public async Task RmQR_TrySelect_RequestedVersion_RejectsWrappingLength()
    {
        // The auto-fit scan compares against a capacity table and was never at risk;
        // the requested-version branch is the one that prices the content itself.
        await Assert.That(RmQRVersionSelector.TrySelect(EncodingMode.Byte, OverflowingByteLength, EciMode.Default, RmQREccLevel.M, RmQRVersion.R7x43, RmQRFitStrategy.MinimizeArea, null, out _)).IsFalse();
        await Assert.That(RmQRVersionSelector.TrySelect(EncodingMode.Byte, OverflowingByteLength, EciMode.Default, RmQREccLevel.M, null, RmQRFitStrategy.MinimizeArea, null, out _)).IsFalse();
    }

    [Test]
    [Arguments(MicroQRVersion.M3)]
    [Arguments(MicroQRVersion.M4)]
    public async Task MicroQR_TrySelectVersion_RejectsWrappingLength(MicroQRVersion version)
    {
        var analysis = new TextAnalysisResult(EncodingMode.Byte, EciMode.Default, OverflowingByteLength);

        await Assert.That(MicroQRCodeGenerator.TrySelectVersion(in analysis, MicroQREccLevel.L, version, out _)).IsFalse();
        await Assert.That(MicroQRCodeGenerator.TrySelectVersion(in analysis, MicroQREccLevel.L, null, out _)).IsFalse();
    }

    [Test]
    public async Task StandardQR_TryGetVersion_RejectsWrappingLength()
    {
        await Assert.That(QRCodeGenerator.TryGetVersion(OverflowingByteLength, EncodingMode.Byte, ECCLevel.L, EciMode.Default, utf8BOM: false, out _)).IsFalse();
        await Assert.That(QRCodeGenerator.TryGetVersion(OverflowingByteLength, EncodingMode.Byte, ECCLevel.L, EciMode.Utf8, utf8BOM: true, out _)).IsFalse();
    }

    [Test]
    public async Task LargestFittingLengths_StillFit()
    {
        // The guard must not clip legitimate content: these are the published maxima.
        await Assert.That(RmQRVersionSelector.Fits(RmQRVersion.R17x139, RmQREccLevel.M, EncodingMode.Byte, 150)).IsTrue();
        await Assert.That(RmQRVersionSelector.Fits(RmQRVersion.R17x139, RmQREccLevel.M, EncodingMode.Numeric, 361)).IsTrue();

        var micro = new TextAnalysisResult(EncodingMode.Numeric, EciMode.Default, 35);
        await Assert.That(MicroQRCodeGenerator.TrySelectVersion(in micro, MicroQREccLevel.L, MicroQRVersion.M4, out _)).IsTrue();

        await Assert.That(QRCodeGenerator.TryGetVersion(2953, EncodingMode.Byte, ECCLevel.L, EciMode.Default, utf8BOM: false, out var v40Byte)).IsTrue();
        await Assert.That(v40Byte).IsEqualTo(40);
        await Assert.That(QRCodeGenerator.TryGetVersion(7089, EncodingMode.Numeric, ECCLevel.L, EciMode.Default, utf8BOM: false, out var v40Numeric)).IsTrue();
        await Assert.That(v40Numeric).IsEqualTo(40);
    }
}
