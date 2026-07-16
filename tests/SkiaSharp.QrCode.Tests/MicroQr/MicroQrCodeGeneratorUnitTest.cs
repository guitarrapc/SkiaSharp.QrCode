using SkiaSharp.QrCode.Internals.MicroQr;

namespace SkiaSharp.QrCode.Tests;

public class MicroQrCodeGeneratorUnitTest
{
    // ---------------------------------------------------------------
    // Version auto-selection (mode x ECC x length equivalence classes)
    // ---------------------------------------------------------------

    [Test]
    // Numeric
    [Arguments("12345", MicroQrEccLevel.ErrorDetectionOnly, MicroQrVersion.M1)] // M1 numeric capacity boundary (5)
    [Arguments("12345", MicroQrEccLevel.L, MicroQrVersion.M2)]                  // EDO-only M1 skipped when L requested
    [Arguments("1234567890", MicroQrEccLevel.L, MicroQrVersion.M2)]             // M2-L numeric boundary (10)
    [Arguments("12345678901", MicroQrEccLevel.L, MicroQrVersion.M3)]            // one over M2-L
    [Arguments("12345678901234567890123", MicroQrEccLevel.L, MicroQrVersion.M3)] // M3-L numeric boundary (23)
    [Arguments("123456789012345678901234", MicroQrEccLevel.L, MicroQrVersion.M4)] // one over M3-L
    [Arguments("12345678901234567890123456789012345", MicroQrEccLevel.L, MicroQrVersion.M4)] // M4-L numeric boundary (35)
    [Arguments("123456789012345678", MicroQrEccLevel.M, MicroQrVersion.M3)]     // M3-M numeric boundary (18)
    [Arguments("123456789012345678901", MicroQrEccLevel.Q, MicroQrVersion.M4)]  // M4-Q numeric boundary (21)
    // Alphanumeric
    [Arguments("HELLO*", MicroQrEccLevel.L, MicroQrVersion.M2)]                 // M2-L alphanumeric boundary (6)
    [Arguments("HELLO*+", MicroQrEccLevel.L, MicroQrVersion.M3)]                // one over M2-L
    [Arguments("HELLO WORLD 14", MicroQrEccLevel.L, MicroQrVersion.M3)]         // M3-L alphanumeric boundary (14)
    [Arguments("HELLO WORLD PLUS 21ST", MicroQrEccLevel.L, MicroQrVersion.M4)]  // M4-L alphanumeric boundary (21)
    // Byte
    [Arguments("byte data", MicroQrEccLevel.L, MicroQrVersion.M3)]              // M3-L byte boundary (9)
    [Arguments("byte data!", MicroQrEccLevel.L, MicroQrVersion.M4)]             // one over M3-L
    [Arguments("bytes!!", MicroQrEccLevel.M, MicroQrVersion.M3)]                // M3-M byte boundary (7)
    [Arguments("bytes!!!!", MicroQrEccLevel.Q, MicroQrVersion.M4)]              // M4-Q byte boundary (9)
    public async Task CreateMicroQrCode_AutoVersion_SelectsSmallestLegalVersion(string text, MicroQrEccLevel ecc, MicroQrVersion expectedVersion)
    {
        var data = MicroQrCodeGenerator.CreateMicroQrCode(text, ecc);
        await Assert.That(data.Version).IsEqualTo(expectedVersion);
    }

    // ---------------------------------------------------------------
    // Illegal combinations (must throw, never silently downgrade)
    // ---------------------------------------------------------------

    [Test]
    [Arguments("123456", MicroQrEccLevel.ErrorDetectionOnly)] // over M1 capacity; EDO cannot escalate past M1
    [Arguments("HELLO", MicroQrEccLevel.ErrorDetectionOnly)]  // alphanumeric not available on M1
    [Arguments("bytes", MicroQrEccLevel.ErrorDetectionOnly)]  // byte not available on M1
    [Arguments("123456789012345678901234567890123456", MicroQrEccLevel.L)] // over M4-L numeric capacity (36 digits)
    [Arguments("this payload is far too long for M4", MicroQrEccLevel.Q)]  // over M4-Q byte capacity
    public async Task CreateMicroQrCode_AutoVersion_ThrowsWhenNothingFits(string text, MicroQrEccLevel ecc)
    {
        await Assert.That(() => MicroQrCodeGenerator.CreateMicroQrCode(text, ecc)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("123", MicroQrEccLevel.L, MicroQrVersion.M1)]        // M1 supports error detection only
    [Arguments("123", MicroQrEccLevel.Q, MicroQrVersion.M2)]        // Q requires M4
    [Arguments("123", MicroQrEccLevel.Q, MicroQrVersion.M3)]        // Q requires M4
    [Arguments("123", MicroQrEccLevel.ErrorDetectionOnly, MicroQrVersion.M2)] // EDO is M1-only
    [Arguments("HELLO", MicroQrEccLevel.L, MicroQrVersion.M1)]      // requested version cannot hold the mode
    [Arguments("bytes", MicroQrEccLevel.L, MicroQrVersion.M2)]      // M2 has no byte mode
    [Arguments("123456789012345678901234", MicroQrEccLevel.L, MicroQrVersion.M3)] // over requested version capacity
    public async Task CreateMicroQrCode_RequestedVersion_ThrowsOnIllegalCombination(string text, MicroQrEccLevel ecc, MicroQrVersion version)
    {
        await Assert.That(() => MicroQrCodeGenerator.CreateMicroQrCode(text, ecc, version)).Throws<ArgumentException>();
    }

    [Test]
    public async Task CreateMicroQrCode_RequestedVersion_UsesRequestedVersion()
    {
        var data = MicroQrCodeGenerator.CreateMicroQrCode("123", MicroQrEccLevel.L, MicroQrVersion.M4);
        await Assert.That(data.Version).IsEqualTo(MicroQrVersion.M4);
        await Assert.That(data.Size).IsEqualTo(17 + 2 * 2); // core 17 + default quiet zone 2 each side
    }

    // ---------------------------------------------------------------
    // Matrix structure invariants
    // ---------------------------------------------------------------

    [Test]
    public async Task CreateMicroQrCode_M2_MatrixStructure()
    {
        var data = MicroQrCodeGenerator.CreateMicroQrCode("01234567", MicroQrEccLevel.L, quietZoneSize: 0);

        await Assert.That(data.Version).IsEqualTo(MicroQrVersion.M2);
        await Assert.That(data.Size).IsEqualTo(13);

        // Finder pattern: outer ring dark, inner separator light, 3x3 center dark.
        await Assert.That(data[0, 0]).IsTrue();
        await Assert.That(data[0, 6]).IsTrue();
        await Assert.That(data[6, 0]).IsTrue();
        await Assert.That(data[1, 1]).IsFalse();
        await Assert.That(data[5, 5]).IsFalse();
        await Assert.That(data[3, 3]).IsTrue();

        // Separator: row 7 cols 0-7 and col 7 rows 0-7 are light.
        for (var i = 0; i <= 7; i++)
        {
            await Assert.That(data[7, i]).IsFalse();
            await Assert.That(data[i, 7]).IsFalse();
        }

        // Timing patterns: row 0 and column 0 from index 8, dark at even indices.
        for (var i = 8; i < 13; i++)
        {
            await Assert.That(data[0, i]).IsEqualTo(i % 2 == 0);
            await Assert.That(data[i, 0]).IsEqualTo(i % 2 == 0);
        }
    }

    [Test]
    [Arguments("12345", MicroQrEccLevel.ErrorDetectionOnly)]
    [Arguments("01234567", MicroQrEccLevel.L)]
    [Arguments("HELLO WORLD 14", MicroQrEccLevel.L)]
    [Arguments("byte data", MicroQrEccLevel.M)]
    [Arguments("bytes!!!!", MicroQrEccLevel.Q)]
    public async Task CreateMicroQrCode_FormatInfo_RoundTripsFromMatrix(string text, MicroQrEccLevel ecc)
    {
        var data = MicroQrCodeGenerator.CreateMicroQrCode(text, ecc, quietZoneSize: 0);

        // Read the 15 placed format modules back (ISO order: bit14..bit8 along
        // row 8 cols 1..7, bit7 at (8,8), bits 6..0 down col 8 rows 7..1) and
        // compare against the expected pattern for every mask; exactly one mask
        // must match, proving format info placement is self-consistent.
        ushort placed = 0;
        for (var col = 1; col <= 8; col++)
        {
            placed = (ushort)((placed << 1) | (data[8, col] ? 1 : 0));
        }
        for (var row = 7; row >= 1; row--)
        {
            placed = (ushort)((placed << 1) | (data[row, 8] ? 1 : 0));
        }

        var matches = 0;
        for (var mask = 0; mask < 4; mask++)
        {
            if (MicroQrConstants.GetFormatBits(data.Version, ecc, mask) == placed)
            {
                matches++;
            }
        }
        await Assert.That(matches).IsEqualTo(1);
    }

    [Test]
    public async Task CreateMicroQrCode_QuietZone_IsLightAndSizedCorrectly()
    {
        var data = MicroQrCodeGenerator.CreateMicroQrCode("12345", MicroQrEccLevel.ErrorDetectionOnly, quietZoneSize: 2);

        await Assert.That(data.Size).IsEqualTo(11 + 4);
        for (var i = 0; i < data.Size; i++)
        {
            await Assert.That(data[0, i]).IsFalse();
            await Assert.That(data[1, i]).IsFalse();
            await Assert.That(data[i, data.Size - 1]).IsFalse();
        }
        // Core top-left finder corner sits just inside the quiet zone.
        await Assert.That(data[2, 2]).IsTrue();
    }

    // ---------------------------------------------------------------
    // Span (zero-allocation) API parity with the class API
    // ---------------------------------------------------------------

    [Test]
    [Arguments("12345", MicroQrEccLevel.ErrorDetectionOnly, 0)]
    [Arguments("01234567", MicroQrEccLevel.L, 2)]
    [Arguments("byte data", MicroQrEccLevel.M, 4)]
    public async Task CreateMicroQrCode_SpanApi_MatchesClassApi(string text, MicroQrEccLevel ecc, int quietZone)
    {
        var data = MicroQrCodeGenerator.CreateMicroQrCode(text, ecc, quietZoneSize: quietZone);

        var calculated = MicroQrCodeGenerator.GetRequiredBufferSize(text.AsSpan(), ecc, quietZoneSize: quietZone);
        await Assert.That(calculated.QrSize).IsEqualTo(data.Size);

        var buffer = new byte[calculated.BufferSize];
        var written = MicroQrCodeGenerator.CreateMicroQrCode(text.AsSpan(), ecc, buffer, quietZoneSize: quietZone);
        await Assert.That(written).IsEqualTo(calculated.BufferSize);

        for (var row = 0; row < data.Size; row++)
        {
            for (var col = 0; col < data.Size; col++)
            {
                var expected = data[row, col];
                var actual = buffer[row * data.Size + col] != 0;
                if (expected != actual)
                {
                    Assert.Fail($"Module mismatch at ({row},{col}): class={expected}, span={actual}");
                }
            }
        }
    }

    [Test]
    public async Task CreateMicroQrCode_SpanApi_ThrowsWhenBufferTooSmall()
    {
        var buffer = new byte[8];
        await Assert.That(() => MicroQrCodeGenerator.CreateMicroQrCode("123".AsSpan(), MicroQrEccLevel.L, buffer)).Throws<ArgumentException>();
    }

    [Test]
    public async Task CreateMicroQrCode_IsDeterministic()
    {
        var first = MicroQrCodeGenerator.CreateMicroQrCode("HELLO WORLD 14", MicroQrEccLevel.L);
        var second = MicroQrCodeGenerator.CreateMicroQrCode("HELLO WORLD 14", MicroQrEccLevel.L);
        await Assert.That(first.GetRawData()).IsEquivalentTo(second.GetRawData());
    }
}
