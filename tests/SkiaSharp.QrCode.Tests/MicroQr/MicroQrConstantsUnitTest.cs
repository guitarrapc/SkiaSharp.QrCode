using SkiaSharp.QrCode.Internals.MicroQr;

namespace SkiaSharp.QrCode.Tests;

public class MicroQrConstantsUnitTest
{
    // The 32 Micro QR format information patterns (ISO/IEC 18004: BCH(15,5) with
    // generator 0x537, XOR mask 0x4445), indexed by (symbolNumber << 2 | mask).
    // Values are independently derivable from the ISO definition; index 0
    // (data 00000 -> 0x0000 ^ 0x4445) and index 1 (data 00001 -> 0x0537 ^ 0x4445)
    // are verified by hand in the naive reference test below.
    private static readonly ushort[] expectedFormatInfos =
    [
        0x4445, 0x4172, 0x4E2B, 0x4B1C, 0x55AE, 0x5099, 0x5FC0, 0x5AF7,
        0x6793, 0x62A4, 0x6DFD, 0x68CA, 0x7678, 0x734F, 0x7C16, 0x7921,
        0x06DE, 0x03E9, 0x0CB0, 0x0987, 0x1735, 0x1202, 0x1D5B, 0x186C,
        0x2508, 0x203F, 0x2F66, 0x2A51, 0x34E3, 0x31D4, 0x3E8D, 0x3BBA,
    ];

    // Symbol number encoding (ISO/IEC 18004): version + ECC level -> 3-bit value.
    private static readonly (MicroQrVersion Version, MicroQrEccLevel Ecc, int SymbolNumber)[] symbolNumbers =
    [
        (MicroQrVersion.M1, MicroQrEccLevel.ErrorDetectionOnly, 0),
        (MicroQrVersion.M2, MicroQrEccLevel.L, 1),
        (MicroQrVersion.M2, MicroQrEccLevel.M, 2),
        (MicroQrVersion.M3, MicroQrEccLevel.L, 3),
        (MicroQrVersion.M3, MicroQrEccLevel.M, 4),
        (MicroQrVersion.M4, MicroQrEccLevel.L, 5),
        (MicroQrVersion.M4, MicroQrEccLevel.M, 6),
        (MicroQrVersion.M4, MicroQrEccLevel.Q, 7),
    ];

    public static IEnumerable<(MicroQrVersion version, MicroQrEccLevel ecc, int mask, ushort expected)> FormatInfoCases()
    {
        foreach (var (version, ecc, symbolNumber) in symbolNumbers)
        {
            for (var mask = 0; mask < 4; mask++)
            {
                yield return (version, ecc, mask, expectedFormatInfos[(symbolNumber << 2) | mask]);
            }
        }
    }

    [Test]
    [MethodDataSource(nameof(FormatInfoCases))]
    public async Task GetFormatBits_MatchesIsoTable(MicroQrVersion version, MicroQrEccLevel ecc, int mask, ushort expected)
    {
        var actual = MicroQrConstants.GetFormatBits(version, ecc, mask);
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async Task FormatInfoTable_MatchesNaiveBchReference()
    {
        // Independent naive BCH(15,5) reference: remainder of data*x^10 mod 0x537,
        // then XOR 0x4445 (ISO/IEC 18004 Micro QR format specification).
        for (var data = 0; data < 32; data++)
        {
            var value = data << 10;
            for (var bit = 14; bit >= 10; bit--)
            {
                if ((value & (1 << bit)) != 0)
                {
                    value ^= 0x537 << (bit - 10);
                }
            }
            var expected = (ushort)(((data << 10) | value) ^ 0x4445);
            await Assert.That(expectedFormatInfos[data]).IsEqualTo(expected);
        }
    }

    [Test]
    [Arguments(MicroQrVersion.M1, MicroQrEccLevel.ErrorDetectionOnly, 20, 3, 2)]
    [Arguments(MicroQrVersion.M2, MicroQrEccLevel.L, 40, 5, 5)]
    [Arguments(MicroQrVersion.M2, MicroQrEccLevel.M, 32, 4, 6)]
    [Arguments(MicroQrVersion.M3, MicroQrEccLevel.L, 84, 11, 6)]
    [Arguments(MicroQrVersion.M3, MicroQrEccLevel.M, 68, 9, 8)]
    [Arguments(MicroQrVersion.M4, MicroQrEccLevel.L, 128, 16, 8)]
    [Arguments(MicroQrVersion.M4, MicroQrEccLevel.M, 112, 14, 10)]
    [Arguments(MicroQrVersion.M4, MicroQrEccLevel.Q, 80, 10, 14)]
    public async Task CodewordTables_MatchIso(MicroQrVersion version, MicroQrEccLevel ecc, int dataBits, int dataCodewords, int eccCodewords)
    {
        await Assert.That(MicroQrConstants.GetDataBitCapacity(version, ecc)).IsEqualTo(dataBits);
        await Assert.That(MicroQrConstants.GetDataCodewordCount(version, ecc)).IsEqualTo(dataCodewords);
        await Assert.That(MicroQrConstants.GetEccCodewordCount(version, ecc)).IsEqualTo(eccCodewords);
    }

    [Test]
    [Arguments(MicroQrVersion.M1, 11)]
    [Arguments(MicroQrVersion.M2, 13)]
    [Arguments(MicroQrVersion.M3, 15)]
    [Arguments(MicroQrVersion.M4, 17)]
    public async Task SizeFromVersion_IsoSizes(MicroQrVersion version, int expectedSize)
    {
        await Assert.That(MicroQrConstants.SizeFromVersion(version)).IsEqualTo(expectedSize);
    }

    [Test]
    [Arguments(MicroQrVersion.M1, MicroQrEccLevel.L, false)]
    [Arguments(MicroQrVersion.M1, MicroQrEccLevel.ErrorDetectionOnly, true)]
    [Arguments(MicroQrVersion.M2, MicroQrEccLevel.ErrorDetectionOnly, false)]
    [Arguments(MicroQrVersion.M2, MicroQrEccLevel.L, true)]
    [Arguments(MicroQrVersion.M2, MicroQrEccLevel.M, true)]
    [Arguments(MicroQrVersion.M2, MicroQrEccLevel.Q, false)]
    [Arguments(MicroQrVersion.M3, MicroQrEccLevel.Q, false)]
    [Arguments(MicroQrVersion.M4, MicroQrEccLevel.Q, true)]
    [Arguments(MicroQrVersion.M4, MicroQrEccLevel.ErrorDetectionOnly, false)]
    public async Task IsValidCombination_VersionEccLegality(MicroQrVersion version, MicroQrEccLevel ecc, bool expected)
    {
        await Assert.That(MicroQrConstants.IsValidCombination(version, ecc)).IsEqualTo(expected);
    }
}
