namespace SkiaSharp.QrCode.Tests;

/// <summary>
/// Encode → decode round trips through the public Micro QR pipeline
/// (<see cref="MicroQrCodeGenerator"/> → <see cref="MicroQrCodeDecoder"/>):
/// every version/ECC combination, every supported mode, boundary lengths,
/// quiet-zone handling, and span/class API parity.
/// </summary>
public class MicroQrCodeDecoderRoundTripTest
{
    public static IEnumerable<(string text, MicroQrEccLevel ecc, MicroQrVersion version)> RoundTripCases()
    {
        yield return ("1", MicroQrEccLevel.ErrorDetectionOnly, MicroQrVersion.M1);
        yield return ("12345", MicroQrEccLevel.ErrorDetectionOnly, MicroQrVersion.M1);
        yield return ("0123456789", MicroQrEccLevel.L, MicroQrVersion.M2);
        yield return ("12345678", MicroQrEccLevel.M, MicroQrVersion.M2);
        yield return ("AC-42", MicroQrEccLevel.L, MicroQrVersion.M2);
        yield return ("HELLO", MicroQrEccLevel.M, MicroQrVersion.M2);
        yield return ("12345678901234567890123", MicroQrEccLevel.L, MicroQrVersion.M3);
        yield return ("HELLO WORLD 14", MicroQrEccLevel.L, MicroQrVersion.M3);
        yield return ("byte hi", MicroQrEccLevel.M, MicroQrVersion.M3);
        yield return ("99999999999999999999999999999999999", MicroQrEccLevel.L, MicroQrVersion.M4);
        yield return ("HELLO WORLD PLUS 21ST", MicroQrEccLevel.L, MicroQrVersion.M4);
        yield return ("bytes m4 mode", MicroQrEccLevel.M, MicroQrVersion.M4);
        yield return ("bytes!!!!", MicroQrEccLevel.Q, MicroQrVersion.M4);
        yield return ("Café au lait", MicroQrEccLevel.L, MicroQrVersion.M4);
        yield return ("こんにちは", MicroQrEccLevel.L, MicroQrVersion.M4);
    }

    [Test]
    [MethodDataSource(nameof(RoundTripCases))]
    public async Task RoundTrip_MicroQrCodeData(string text, MicroQrEccLevel ecc, MicroQrVersion version)
    {
        var data = MicroQrCodeGenerator.CreateMicroQrCode(text, ecc);

        var success = MicroQrCodeDecoder.TryDecode(data, out var decoded, out var info);

        await Assert.That(success).IsTrue();
        await Assert.That(decoded).IsEqualTo(text);
        await Assert.That(info.Status).IsEqualTo(QRCodeDecodeStatus.Success);
        await Assert.That(info.Version).IsEqualTo(version);
        await Assert.That(info.EccLevel).IsEqualTo(ecc);
        await Assert.That(info.MaskPattern).IsBetween(0, 3);
        await Assert.That(info.ErrorsCorrected).IsEqualTo(0);
    }

    [Test]
    [MethodDataSource(nameof(RoundTripCases))]
    public async Task RoundTrip_ModuleMatrix_WithAndWithoutQuietZone(string text, MicroQrEccLevel ecc, MicroQrVersion version)
    {
        foreach (var quietZone in (int[])[0, 2, 4])
        {
            var calculated = MicroQrCodeGenerator.GetRequiredBufferSize(text.AsSpan(), ecc, quietZoneSize: quietZone);
            var modules = new byte[calculated.BufferSize];
            MicroQrCodeGenerator.CreateMicroQrCode(text.AsSpan(), ecc, modules, quietZoneSize: quietZone);

            var success = MicroQrCodeDecoder.TryDecode(modules, calculated.QrSize, out var decoded, out var info);

            await Assert.That(success).IsTrue();
            await Assert.That(decoded).IsEqualTo(text);
            await Assert.That(info.Version).IsEqualTo(version);
            await Assert.That(info.EccLevel).IsEqualTo(ecc);
        }
    }

    [Test]
    [MethodDataSource(nameof(RoundTripCases))]
    public async Task RoundTrip_SpanDestination_MatchesStringOverload(string text, MicroQrEccLevel ecc, MicroQrVersion version)
    {
        // Quiet zone 0 exercises the in-place fast path, 2 the core-copy branch.
        foreach (var quietZone in (int[])[0, 2])
        {
            var calculated = MicroQrCodeGenerator.GetRequiredBufferSize(text.AsSpan(), ecc, quietZoneSize: quietZone);
            var modules = new byte[calculated.BufferSize];
            MicroQrCodeGenerator.CreateMicroQrCode(text.AsSpan(), ecc, modules, quietZoneSize: quietZone);

            var destination = new char[MicroQrCodeDecoder.GetMaxDecodedLength(version)];
            var success = MicroQrCodeDecoder.TryDecode(modules, calculated.QrSize, destination, out var charsWritten, out var info);

            await Assert.That(success).IsTrue();
            await Assert.That(new string(destination, 0, charsWritten)).IsEqualTo(text);
            await Assert.That(info.Version).IsEqualTo(version);
        }
    }

    [Test]
    public async Task TryDecode_AsymmetricBorder_ReportsInvalidMatrix()
    {
        // A valid M1 core placed at row 1, column 0 of a 13×13 buffer: the first
        // dark row (1) and the minimum dark column (0) disagree, so this is not a
        // uniform quiet zone and must be rejected rather than misread.
        var calculated = MicroQrCodeGenerator.GetRequiredBufferSize("12345".AsSpan(), MicroQrEccLevel.ErrorDetectionOnly, quietZoneSize: 0);
        var core = new byte[calculated.BufferSize];
        MicroQrCodeGenerator.CreateMicroQrCode("12345".AsSpan(), MicroQrEccLevel.ErrorDetectionOnly, core, quietZoneSize: 0);

        const int size = 13;
        var modules = new byte[size * size];
        for (var row = 0; row < calculated.QrSize; row++)
        {
            core.AsSpan(row * calculated.QrSize, calculated.QrSize).CopyTo(modules.AsSpan((row + 1) * size));
        }

        var success = MicroQrCodeDecoder.TryDecode(modules, size, out _, out var info);

        await Assert.That(success).IsFalse();
        await Assert.That(info.Status).IsEqualTo(QRCodeDecodeStatus.InvalidMatrix);
    }

    [Test]
    public async Task TryDecode_NullData_Throws()
    {
        await Assert.That(() => MicroQrCodeDecoder.TryDecode((MicroQrCodeData)null!, out _)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task GetMaxDecodedLength_InvalidVersion_Throws()
    {
        await Assert.That(() => MicroQrCodeDecoder.GetMaxDecodedLength((MicroQrVersion)0)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => MicroQrCodeDecoder.GetMaxDecodedLength((MicroQrVersion)5)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task TryDecode_NonMicroSize_ReportsInvalidMatrix()
    {
        // 12×12 is not a Micro QR size (11/13/15/17)
        var modules = new byte[12 * 12];
        modules[0] = 1; // avoid the all-light early exit hiding the size check

        var success = MicroQrCodeDecoder.TryDecode(modules, 12, out var text, out var info);

        await Assert.That(success).IsFalse();
        await Assert.That(text).IsEqualTo("");
        await Assert.That(info.Status).IsEqualTo(QRCodeDecodeStatus.InvalidMatrix);
    }

    [Test]
    public async Task TryDecode_AllLightMatrix_Fails()
    {
        var modules = new byte[11 * 11];

        var success = MicroQrCodeDecoder.TryDecode(modules, 11, out _, out var info);

        await Assert.That(success).IsFalse();
        await Assert.That(info.Status).IsNotEqualTo(QRCodeDecodeStatus.Success);
    }

    [Test]
    public async Task GetMaxDecodedLength_CoversDensestPayload()
    {
        // The densest legal payloads must fit the advertised bound.
        await Assert.That(MicroQrCodeDecoder.GetMaxDecodedLength(MicroQrVersion.M1)).IsGreaterThanOrEqualTo(5);
        await Assert.That(MicroQrCodeDecoder.GetMaxDecodedLength(MicroQrVersion.M2)).IsGreaterThanOrEqualTo(10);
        await Assert.That(MicroQrCodeDecoder.GetMaxDecodedLength(MicroQrVersion.M3)).IsGreaterThanOrEqualTo(23);
        await Assert.That(MicroQrCodeDecoder.GetMaxDecodedLength(MicroQrVersion.M4)).IsGreaterThanOrEqualTo(35);
    }

    [Test]
    public async Task TryDecode_BufferTooSmall_Throws()
    {
        var modules = new byte[11 * 11 - 1];

        await Assert.That(() => MicroQrCodeDecoder.TryDecode(modules, 11, out _, out _)).Throws<ArgumentException>();
    }
}
