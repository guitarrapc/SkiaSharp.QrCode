using TUnit.Assertions.Enums;
using SkiaSharp.QrCode.Internals.RmQr;

namespace SkiaSharp.QrCode.Tests;

/// <summary>
/// All three <see cref="RmQRMatrixDecoder.ExtractCodewords(ReadOnlySpan{byte}, int, int, RmQRVersion, Span{byte}, RmQRMatrixDecoder.ExtractKernel)"/>
/// tiers (the x64 bit-plane kernel, the ARM64 pair-plane kernel and the portable
/// table walk) against
/// <see cref="RmQRNaiveReference.ExtractInterleavedStream"/>, byte for byte, for
/// every version over module grids that pin the contract corners: all light, all
/// dark written as 1 / 0xFF / 2 (the API is "0 = light, non-zero = dark", so a
/// kernel that bit-tests instead of comparing against zero fails here), several
/// pseudo-random grids.
/// </summary>
/// <remarks>
/// The reference walks the zigzag per module with its own independently written
/// function-module map, so it shares no code with either kernel.
/// </remarks>
public class RmQRExtractCodewordsParityTest
{
    public static IEnumerable<RmQRVersion> AllVersions() => Enum.GetValues<RmQRVersion>();

    private static byte[] Grid(int length, int shape, int seed)
    {
        var grid = new byte[length];
        switch (shape)
        {
            case 0:
                break; // all light
            case 1:
                grid.AsSpan().Fill(1);
                break;
            case 2:
                grid.AsSpan().Fill(0xFF);
                break;
            case 3:
                grid.AsSpan().Fill(2); // dark, but neither 1 nor 0xFF
                break;
            default:
                var state = (uint)seed * 2654435761u + 7u;
                for (var i = 0; i < length; i++)
                {
                    state = state * 1664525u + 1013904223u;
                    var r = state >> 16;
                    // Mix in non-1 dark values so the != 0 contract stays covered.
                    grid[i] = (r & 1) == 0 ? (byte)0 : (byte)((r & 6) == 0 ? 2 : 1);
                }
                break;
        }
        return grid;
    }

    [Test]
    [MethodDataSource(nameof(AllVersions))]
    public async Task EveryTier_MatchesNaiveReference_EveryGridShape(RmQRVersion version)
    {
        var width = RmQRConstants.GetWidth(version);
        var height = RmQRConstants.GetHeight(version);
        var totalCodewords = RmQRConstants.GetTotalCodewordCount(version);

        for (var shape = 0; shape < 8; shape++)
        {
            var modules = Grid(width * height, shape, (int)version * 31 + shape);
            var expected = RmQRNaiveReference.ExtractInterleavedStream(modules, height, width, out _)
                .AsSpan(0, totalCodewords)
                .ToArray();

            // Poison the destination so a kernel that fails to write a byte is caught.
            var scalar = new byte[totalCodewords];
            scalar.AsSpan().Fill(0xA5);
            RmQRMatrixDecoder.ExtractCodewords(modules, width, height, version, scalar, RmQRMatrixDecoder.ExtractKernel.Scalar);
            await Assert.That(scalar).IsEquivalentTo(expected, CollectionOrdering.Matching)
                .Because($"scalar tier, version {version} ({width}x{height}), grid shape {shape}");

            if (RmQRMatrixDecoder.IsBitPlaneTierSupported)
            {
                var bitPlanes = new byte[totalCodewords];
                bitPlanes.AsSpan().Fill(0xA5);
                RmQRMatrixDecoder.ExtractCodewords(modules, width, height, version, bitPlanes, RmQRMatrixDecoder.ExtractKernel.BitPlanes);
                await Assert.That(bitPlanes).IsEquivalentTo(expected, CollectionOrdering.Matching)
                    .Because($"bit-plane tier, version {version} ({width}x{height}), grid shape {shape}");
            }

            if (RmQRMatrixDecoder.IsPairPlaneTierSupported)
            {
                var pairPlanes = new byte[totalCodewords];
                pairPlanes.AsSpan().Fill(0xA5);
                RmQRMatrixDecoder.ExtractCodewords(modules, width, height, version, pairPlanes, RmQRMatrixDecoder.ExtractKernel.PairPlanes);
                await Assert.That(pairPlanes).IsEquivalentTo(expected, CollectionOrdering.Matching)
                    .Because($"pair-plane tier, version {version} ({width}x{height}), grid shape {shape}");
            }
        }
    }

    /// <summary>
    /// Both vector transposes deliberately run past the end of a row (16 bytes at a time
    /// on x64, up to 32 on ARM64), relying on there always being a row below rows
    /// 1..h-2. This pins that neither kernel reads outside the caller's span: the grid is
    /// followed by bytes of the opposite polarity that the kernel must not need.
    /// </summary>
    [Test]
    [MethodDataSource(nameof(AllVersions))]
    public async Task VectorTiers_StayInsideTheModuleSpan(RmQRVersion version)
    {
        if (!RmQRMatrixDecoder.IsBitPlaneTierSupported && !RmQRMatrixDecoder.IsPairPlaneTierSupported)
        {
            Skip.Test("no vector extraction tier on this machine");
            return;
        }

        var width = RmQRConstants.GetWidth(version);
        var height = RmQRConstants.GetHeight(version);
        var totalCodewords = RmQRConstants.GetTotalCodewordCount(version);
        var length = width * height;

        var modules = Grid(length, 4, (int)version * 7 + 3);
        var expected = RmQRNaiveReference.ExtractInterleavedStream(modules, height, width, out _)
            .AsSpan(0, totalCodewords)
            .ToArray();

        // Same grid, but the span ends exactly at the last module and is followed by
        // bytes of the opposite polarity: an overread past width*height would change
        // the result.
        var padded = new byte[length + 64];
        padded.AsSpan().Fill(0xFF);
        modules.CopyTo(padded, 0);

        if (RmQRMatrixDecoder.IsBitPlaneTierSupported)
        {
            var actual = new byte[totalCodewords];
            RmQRMatrixDecoder.ExtractCodewords(padded.AsSpan(0, length), width, height, version, actual, RmQRMatrixDecoder.ExtractKernel.BitPlanes);
            await Assert.That(actual).IsEquivalentTo(expected, CollectionOrdering.Matching)
                .Because($"bit-plane tier, version {version} ({width}x{height}) must not depend on bytes past width*height");
        }

        if (RmQRMatrixDecoder.IsPairPlaneTierSupported)
        {
            var actual = new byte[totalCodewords];
            RmQRMatrixDecoder.ExtractCodewords(padded.AsSpan(0, length), width, height, version, actual, RmQRMatrixDecoder.ExtractKernel.PairPlanes);
            await Assert.That(actual).IsEquivalentTo(expected, CollectionOrdering.Matching)
                .Because($"pair-plane tier, version {version} ({width}x{height}) must not depend on bytes past width*height");
        }
    }
}
