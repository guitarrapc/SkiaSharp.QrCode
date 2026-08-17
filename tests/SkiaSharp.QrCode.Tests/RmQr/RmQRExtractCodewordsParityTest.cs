using SkiaSharp.QrCode.Internals.RmQr;

namespace SkiaSharp.QrCode.Tests;

/// <summary>
/// Both <see cref="RmQRMatrixDecoder.ExtractCodewords(ReadOnlySpan{byte}, int, int, RmQRVersion, Span{byte}, bool)"/>
/// tiers (the bit-plane kernel and the portable table walk) against
/// <see cref="RmQRNaiveReference.ExtractInterleavedStream"/>, byte for byte, for
/// every version over module grids that pin the contract corners: all light, all
/// dark written as 1 / 0xFF / 2 (the API is "0 = light, non-zero = dark", so a
/// kernel that bit-tests instead of comparing against zero fails here), several
/// pseudo-random grids, and grids that are dark only in the function-module regions.
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
    public async Task BothTiers_MatchNaiveReference_EveryGridShape(RmQRVersion version)
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
            RmQRMatrixDecoder.ExtractCodewords(modules, width, height, version, scalar, forceScalar: true);
            await Assert.That(scalar).IsEquivalentTo(expected)
                .Because($"scalar tier, version {version} ({width}x{height}), grid shape {shape}");

            if (!RmQRMatrixDecoder.IsBitPlaneTierSupported)
                continue;

            var bitPlanes = new byte[totalCodewords];
            bitPlanes.AsSpan().Fill(0xA5);
            RmQRMatrixDecoder.ExtractCodewords(modules, width, height, version, bitPlanes, forceScalar: false);
            await Assert.That(bitPlanes).IsEquivalentTo(expected)
                .Because($"bit-plane tier, version {version} ({width}x{height}), grid shape {shape}");
        }
    }

    /// <summary>
    /// The bit-plane transpose reads 16 bytes at a time and deliberately runs past the
    /// end of a row, relying on there always being a row below rows 1..h-2. This pins
    /// that the kernel never reads outside the caller's span: the grid is placed at the
    /// end of a larger buffer whose trailing bytes are a guard the kernel must not need.
    /// </summary>
    [Test]
    [MethodDataSource(nameof(AllVersions))]
    public async Task BitPlaneTier_StaysInsideTheModuleSpan(RmQRVersion version)
    {
        if (!RmQRMatrixDecoder.IsBitPlaneTierSupported)
            return;

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

        var actual = new byte[totalCodewords];
        RmQRMatrixDecoder.ExtractCodewords(padded.AsSpan(0, length), width, height, version, actual, forceScalar: false);
        await Assert.That(actual).IsEquivalentTo(expected)
            .Because($"version {version} ({width}x{height}) must not depend on bytes past width*height");
    }
}
