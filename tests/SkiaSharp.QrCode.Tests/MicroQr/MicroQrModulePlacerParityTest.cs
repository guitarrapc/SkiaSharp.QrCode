using SkiaSharp.QrCode.Internals.MicroQr;

namespace SkiaSharp.QrCode.Tests;

/// <summary>
/// Parity guard for the optimized Micro QR placement pipeline
/// (<see cref="MicroQrModulePlacer.PlaceSymbol"/>): the fused implementation
/// (bulk stream pack, static segments, packed-edge mask scoring, size-dispatched
/// apply, packed-row pipeline for sizes 13/15/17) must produce a byte-identical
/// matrix AND the same selected mask as the naive per-module reference below,
/// for every valid (version, ECC) combination and stream content.
/// </summary>
/// <remarks>
/// The reference is an independent transcription of ISO/IEC 18004 Micro QR
/// placement (the pre-optimization implementation), kept naive on purpose:
/// per-module function predicate, per-bit indexed stream reads, per-module mask
/// switch. It shares no code with the production fast path.
/// </remarks>
public class MicroQrModulePlacerParityTest
{
    public static IEnumerable<(MicroQrVersion version, MicroQrEccLevel ecc, int seed)> AllCombinationsAndSeeds()
    {
        var combos = new[]
        {
            (MicroQrVersion.M1, MicroQrEccLevel.ErrorDetectionOnly),
            (MicroQrVersion.M2, MicroQrEccLevel.L),
            (MicroQrVersion.M2, MicroQrEccLevel.M),
            (MicroQrVersion.M3, MicroQrEccLevel.L),
            (MicroQrVersion.M3, MicroQrEccLevel.M),
            (MicroQrVersion.M4, MicroQrEccLevel.L),
            (MicroQrVersion.M4, MicroQrEccLevel.M),
            (MicroQrVersion.M4, MicroQrEccLevel.Q),
        };

        // seed -1 = all-zero stream, seed -2 = all-0xFF stream, 0..2 = random
        var seeds = new[] { -1, -2, 0, 1, 2 };

        foreach (var (version, ecc) in combos)
        {
            foreach (var seed in seeds)
            {
                yield return (version, ecc, seed);
            }
        }
    }

    [Test]
    [MethodDataSource(nameof(AllCombinationsAndSeeds))]
    public async Task PlaceSymbol_MatchesNaiveReference(MicroQrVersion version, MicroQrEccLevel ecc, int seed)
    {
        var size = MicroQrConstants.SizeFromVersion(version);
        var dataCount = MicroQrConstants.GetDataCodewordCount(version, ecc);
        var eccCount = MicroQrConstants.GetEccCodewordCount(version, ecc);
        var dataBitCount = MicroQrConstants.GetDataBitCapacity(version, ecc);

        var data = new byte[dataCount];
        var eccBytes = new byte[eccCount];
        switch (seed)
        {
            case -1:
                break;
            case -2:
                Array.Fill(data, (byte)0xFF);
                Array.Fill(eccBytes, (byte)0xFF);
                break;
            default:
                new Random(seed).NextBytes(data);
                new Random(seed + 100).NextBytes(eccBytes);
                break;
        }

        var expected = new byte[size * size];
        var expectedMask = ReferencePlace(expected, size, data, eccBytes, dataBitCount, version, ecc);

        var actual = new byte[size * size];
        var actualMask = MicroQrModulePlacer.PlaceSymbol(actual, size, data, eccBytes, dataBitCount, version, ecc);

        await Assert.That(actualMask).IsEqualTo(expectedMask);
        await Assert.That(actual).IsEquivalentTo(expected);
    }

    /// <summary>
    /// The scalar-unpack fallback (taken at runtime when SSSE3 is unavailable)
    /// must match the reference too — on SIMD-capable test machines it is
    /// exercised through this named internal entry point.
    /// </summary>
    [Test]
    [MethodDataSource(nameof(AllCombinationsAndSeeds))]
    public async Task PlaceSymbolScalar_MatchesNaiveReference(MicroQrVersion version, MicroQrEccLevel ecc, int seed)
    {
        var size = MicroQrConstants.SizeFromVersion(version);
        var dataCount = MicroQrConstants.GetDataCodewordCount(version, ecc);
        var eccCount = MicroQrConstants.GetEccCodewordCount(version, ecc);
        var dataBitCount = MicroQrConstants.GetDataBitCapacity(version, ecc);

        var data = new byte[dataCount];
        var eccBytes = new byte[eccCount];
        switch (seed)
        {
            case -1:
                break;
            case -2:
                Array.Fill(data, (byte)0xFF);
                Array.Fill(eccBytes, (byte)0xFF);
                break;
            default:
                new Random(seed).NextBytes(data);
                new Random(seed + 100).NextBytes(eccBytes);
                break;
        }

        var expected = new byte[size * size];
        var expectedMask = ReferencePlace(expected, size, data, eccBytes, dataBitCount, version, ecc);

        var actual = new byte[size * size];
        var actualMask = MicroQrModulePlacer.PlaceSymbolScalar(actual, size, data, eccBytes, dataBitCount, version, ecc);

        await Assert.That(actualMask).IsEqualTo(expectedMask);
        await Assert.That(actual).IsEquivalentTo(expected);
    }

    /// <summary>
    /// The BMI2+AVX2 kernel (PEXT/PDEP placement permutation, AVX2 32-module
    /// unpack) must match the reference on machines that support it.
    /// </summary>
    /// <remarks>
    /// Deliberately gated on instruction SUPPORT only, not on the production
    /// dispatch's fast-PEXT vendor/family check: named-entry parity tests exist
    /// precisely to cover tiers the local dispatch would not take (the SSSE3
    /// and scalar tests run on BMI2 machines for the same reason), and PEXT/PDEP
    /// results are identical where they are merely microcoded (pre-Zen 3 AMD) —
    /// only slower, by well under a millisecond across this whole data source
    /// (~96 BMI ops per placement call).
    /// </remarks>
    [Test]
    [MethodDataSource(nameof(AllCombinationsAndSeeds))]
    public async Task PlaceSymbolBmi2_MatchesNaiveReference(MicroQrVersion version, MicroQrEccLevel ecc, int seed)
    {
#if NET8_0_OR_GREATER
        if (!System.Runtime.Intrinsics.X86.Bmi2.X64.IsSupported || !System.Runtime.Intrinsics.X86.Avx2.IsSupported)
        {
            Skip.Test("BMI2/AVX2 not supported on this machine.");
            return;
        }

        var size = MicroQrConstants.SizeFromVersion(version);
        var dataCount = MicroQrConstants.GetDataCodewordCount(version, ecc);
        var eccCount = MicroQrConstants.GetEccCodewordCount(version, ecc);
        var dataBitCount = MicroQrConstants.GetDataBitCapacity(version, ecc);

        var data = new byte[dataCount];
        var eccBytes = new byte[eccCount];
        switch (seed)
        {
            case -1:
                break;
            case -2:
                Array.Fill(data, (byte)0xFF);
                Array.Fill(eccBytes, (byte)0xFF);
                break;
            default:
                new Random(seed).NextBytes(data);
                new Random(seed + 100).NextBytes(eccBytes);
                break;
        }

        var expected = new byte[size * size];
        var expectedMask = ReferencePlace(expected, size, data, eccBytes, dataBitCount, version, ecc);

        var actual = new byte[size * size];
        var actualMask = MicroQrModulePlacer.PlaceSymbolBmi2(actual, size, data, eccBytes, dataBitCount, version, ecc);

        await Assert.That(actualMask).IsEqualTo(expectedMask);
        await Assert.That(actual).IsEquivalentTo(expected);
#else
        Skip.Test("BMI2 kernel requires net8.0+.");
        await Task.CompletedTask;
#endif
    }

    /// <summary>
    /// The SSSE3 mid-tier kernel (taken at runtime when BMI2/AVX2 are absent
    /// but SSSE3 is present) must stay covered on machines whose PlaceSymbol
    /// dispatch prefers the BMI2 kernel.
    /// </summary>
    [Test]
    [MethodDataSource(nameof(AllCombinationsAndSeeds))]
    public async Task PlaceSymbolSsse3_MatchesNaiveReference(MicroQrVersion version, MicroQrEccLevel ecc, int seed)
    {
#if NET8_0_OR_GREATER
        if (!System.Runtime.Intrinsics.X86.Ssse3.IsSupported)
        {
            Skip.Test("SSSE3 not supported on this machine.");
            return;
        }

        var size = MicroQrConstants.SizeFromVersion(version);
        var dataCount = MicroQrConstants.GetDataCodewordCount(version, ecc);
        var eccCount = MicroQrConstants.GetEccCodewordCount(version, ecc);
        var dataBitCount = MicroQrConstants.GetDataBitCapacity(version, ecc);

        var data = new byte[dataCount];
        var eccBytes = new byte[eccCount];
        switch (seed)
        {
            case -1:
                break;
            case -2:
                Array.Fill(data, (byte)0xFF);
                Array.Fill(eccBytes, (byte)0xFF);
                break;
            default:
                new Random(seed).NextBytes(data);
                new Random(seed + 100).NextBytes(eccBytes);
                break;
        }

        var expected = new byte[size * size];
        var expectedMask = ReferencePlace(expected, size, data, eccBytes, dataBitCount, version, ecc);

        var actual = new byte[size * size];
        var actualMask = MicroQrModulePlacer.PlaceSymbolSsse3(actual, size, data, eccBytes, dataBitCount, version, ecc);

        await Assert.That(actualMask).IsEqualTo(expectedMask);
        await Assert.That(actual).IsEquivalentTo(expected);
#else
        Skip.Test("SSSE3 kernel requires net8.0+.");
        await Task.CompletedTask;
#endif
    }

    /// <summary>
    /// The ARM64 NEON mid-tier kernel (packed placement + 16-module TBL/CMTST
    /// unpack) must match the reference on ARM64 machines. Exposed as a named
    /// entry point so it stays covered independently of the runtime dispatch.
    /// </summary>
    [Test]
    [MethodDataSource(nameof(AllCombinationsAndSeeds))]
    public async Task PlaceSymbolAdvSimd_MatchesNaiveReference(MicroQrVersion version, MicroQrEccLevel ecc, int seed)
    {
#if NET8_0_OR_GREATER
        if (!System.Runtime.Intrinsics.Arm.AdvSimd.Arm64.IsSupported)
        {
            Skip.Test("AdvSimd (ARM64 NEON) not supported on this machine.");
            return;
        }

        var size = MicroQrConstants.SizeFromVersion(version);
        var dataCount = MicroQrConstants.GetDataCodewordCount(version, ecc);
        var eccCount = MicroQrConstants.GetEccCodewordCount(version, ecc);
        var dataBitCount = MicroQrConstants.GetDataBitCapacity(version, ecc);

        var data = new byte[dataCount];
        var eccBytes = new byte[eccCount];
        switch (seed)
        {
            case -1:
                break;
            case -2:
                Array.Fill(data, (byte)0xFF);
                Array.Fill(eccBytes, (byte)0xFF);
                break;
            default:
                new Random(seed).NextBytes(data);
                new Random(seed + 100).NextBytes(eccBytes);
                break;
        }

        var expected = new byte[size * size];
        var expectedMask = ReferencePlace(expected, size, data, eccBytes, dataBitCount, version, ecc);

        var actual = new byte[size * size];
        var actualMask = MicroQrModulePlacer.PlaceSymbolAdvSimd(actual, size, data, eccBytes, dataBitCount, version, ecc);

        await Assert.That(actualMask).IsEqualTo(expectedMask);
        await Assert.That(actual).IsEquivalentTo(expected);
#else
        Skip.Test("AdvSimd kernel requires net8.0+.");
        await Task.CompletedTask;
#endif
    }

    [Test]
    public async Task PlaceSymbol_InvalidSize_Throws()
    {
        var matrix = new byte[17 * 17];
        await Assert.That(() => MicroQrModulePlacer.PlaceSymbol(matrix, 12, new byte[5], new byte[5], 40, MicroQrVersion.M2, MicroQrEccLevel.L))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task PlaceSymbol_MatrixTooSmall_Throws()
    {
        var matrix = new byte[13 * 13 - 1];
        await Assert.That(() => MicroQrModulePlacer.PlaceSymbol(matrix, 13, new byte[5], new byte[5], 40, MicroQrVersion.M2, MicroQrEccLevel.L))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task PlaceSymbol_StreamLengthMismatch_Throws()
    {
        // M2-L expects 40 data bits + 5 ecc bytes = 80 bits for 80 free modules;
        // one ecc byte short must be rejected instead of underfilling silently.
        var matrix = new byte[13 * 13];
        await Assert.That(() => MicroQrModulePlacer.PlaceSymbol(matrix, 13, new byte[5], new byte[4], 40, MicroQrVersion.M2, MicroQrEccLevel.L))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task PlaceSymbol_DataCodewordsShorterThanBitCount_Throws()
    {
        var matrix = new byte[13 * 13];
        await Assert.That(() => MicroQrModulePlacer.PlaceSymbol(matrix, 13, new byte[4], new byte[5], 40, MicroQrVersion.M2, MicroQrEccLevel.L))
            .Throws<ArgumentException>();
    }

    // ---------------------------------------------------------------
    // Naive reference (independent of the production fast path)
    // ---------------------------------------------------------------

    private static int ReferencePlace(Span<byte> matrix, int size, ReadOnlySpan<byte> data, ReadOnlySpan<byte> ecc, int dataBitCount, MicroQrVersion version, MicroQrEccLevel eccLevel)
    {
        ReferencePlaceFunctionModules(matrix, size);
        ReferencePlaceDataCodewords(matrix, size, data, ecc, dataBitCount);
        var mask = ReferenceSelectAndApplyMask(matrix, size);
        ReferencePlaceFormat(matrix, size, MicroQrConstants.GetFormatBits(version, eccLevel, mask));
        return mask;
    }

    private static void ReferencePlaceFunctionModules(Span<byte> matrix, int size)
    {
        for (var i = 0; i < 7; i++)
        {
            matrix[i] = 1;
            matrix[6 * size + i] = 1;
            matrix[i * size] = 1;
            matrix[i * size + 6] = 1;
        }
        for (var row = 2; row <= 4; row++)
        {
            for (var col = 2; col <= 4; col++)
            {
                matrix[row * size + col] = 1;
            }
        }
        for (var i = 8; i < size; i += 2)
        {
            matrix[i] = 1;
            matrix[i * size] = 1;
        }
    }

    private static bool ReferenceIsFunctionModule(int row, int col) => row == 0 || col == 0 || (row <= 8 && col <= 8);

    private static void ReferencePlaceDataCodewords(Span<byte> matrix, int size, ReadOnlySpan<byte> dataCodewords, ReadOnlySpan<byte> eccCodewords, int dataBitCount)
    {
        var totalBits = dataBitCount + eccCodewords.Length * 8;
        var bitIndex = 0;
        var upward = true;

        for (var right = size - 1; right >= 2; right -= 2)
        {
            for (var step = 0; step < size; step++)
            {
                var row = upward ? size - 1 - step : step;
                for (var side = 0; side < 2; side++)
                {
                    var col = right - side;
                    if (ReferenceIsFunctionModule(row, col))
                        continue;

                    if (bitIndex < totalBits)
                    {
                        byte bit;
                        if (bitIndex < dataBitCount)
                        {
                            bit = (byte)((dataCodewords[bitIndex >> 3] >> (7 - (bitIndex & 7))) & 1);
                        }
                        else
                        {
                            var eccBit = bitIndex - dataBitCount;
                            bit = (byte)((eccCodewords[eccBit >> 3] >> (7 - (eccBit & 7))) & 1);
                        }
                        matrix[row * size + col] = bit;
                        bitIndex++;
                    }
                }
            }
            upward = !upward;
        }
    }

    private static bool ReferenceMaskBit(int mask, int row, int col) => mask switch
    {
        0 => row % 2 == 0,
        1 => (row / 2 + col / 3) % 2 == 0,
        2 => (row * col % 2 + row * col % 3) % 2 == 0,
        _ => ((row + col) % 2 + row * col % 3) % 2 == 0,
    };

    private static int ReferenceSelectAndApplyMask(Span<byte> matrix, int size)
    {
        var bestMask = 0;
        var bestScore = -1;

        for (var mask = 0; mask < 4; mask++)
        {
            var sum1 = 0;
            var sum2 = 0;
            var lastRowOffset = (size - 1) * size;

            for (var i = 1; i < size; i++)
            {
                if ((matrix[i * size + size - 1] != 0) ^ ReferenceMaskBit(mask, i, size - 1))
                {
                    sum1++;
                }
                if ((matrix[lastRowOffset + i] != 0) ^ ReferenceMaskBit(mask, size - 1, i))
                {
                    sum2++;
                }
            }

            var score = sum1 <= sum2 ? sum1 * 16 + sum2 : sum2 * 16 + sum1;
            if (score > bestScore)
            {
                bestScore = score;
                bestMask = mask;
            }
        }

        for (var row = 1; row < size; row++)
        {
            var rowOffset = row * size;
            var colStart = row <= 8 ? 9 : 1;
            for (var col = colStart; col < size; col++)
            {
                if (ReferenceMaskBit(bestMask, row, col))
                {
                    matrix[rowOffset + col] ^= 1;
                }
            }
        }

        return bestMask;
    }

    private static void ReferencePlaceFormat(Span<byte> matrix, int size, ushort formatBits)
    {
        var bit = 14;
        for (var col = 1; col <= 8; col++, bit--)
        {
            matrix[8 * size + col] = (byte)((formatBits >> bit) & 1);
        }
        for (var row = 7; row >= 1; row--, bit--)
        {
            matrix[row * size + 8] = (byte)((formatBits >> bit) & 1);
        }
    }
}
