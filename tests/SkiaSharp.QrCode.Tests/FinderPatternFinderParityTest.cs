using SkiaSharp.QrCode.Internals.ImageDecoders;
using Xunit;

namespace SkiaSharp.QrCode.Tests;

/// <summary>
/// Parity test for the finder-pattern row-scan kernels: the SIMD mask walk
/// (net8.0+, AVX2) evaluates the 1:1:3:1:1 window at exactly the positions the
/// scalar walk does, so <see cref="FinderPatternFinder.TryFind"/> and
/// <see cref="FinderPatternFinder.TryFindScalar"/> must agree bit-for-bit —
/// found flag, centers and module sizes — across synthetic QR-like scenes
/// (finder patterns + timing lines + random data modules, 3/2/0 patterns).
/// </summary>
public class FinderPatternFinderParityTest
{
    private const byte Threshold = 128;

    [Fact]
    public void SimdAndScalarKernels_AgreeBitForBit()
    {
        // (dimension, ppm, finderCount): both benchmark shapes, a 2-finder scene
        // (found only if noise fakes a third candidate — kernels must agree either
        // way), and a no-pattern scene.
        var configs = new (int Dimension, int Ppm, int FinderCount)[]
        {
            (37, 8, 3),
            (97, 5, 3),
            (37, 8, 2),
            (56, 8, 0),
        };

        Span<FinderPattern> simd = stackalloc FinderPattern[3];
        Span<FinderPattern> scalar = stackalloc FinderPattern[3];

        foreach (var seed in new[] { 1, 7, 42, 1234, 20260712 })
        {
            foreach (var (dimension, ppm, finderCount) in configs)
            {
                var scene = BuildQrScene(dimension, ppm, finderCount, seed, out var width, out var height);
                var simdFound = FinderPatternFinder.TryFind(scene, width, height, Threshold, simd);
                var scalarFound = FinderPatternFinder.TryFindScalar(scene, width, height, Threshold, scalar);

                Assert.True(simdFound == scalarFound, $"found mismatch (seed={seed}, dim={dimension}, finders={finderCount}): simd={simdFound}, scalar={scalarFound}");
                if (simdFound)
                {
                    for (var i = 0; i < 3; i++)
                    {
                        Assert.True(
                            simd[i].X == scalar[i].X && simd[i].Y == scalar[i].Y && simd[i].ModuleSize == scalar[i].ModuleSize,
                            $"pattern {i} mismatch (seed={seed}, dim={dimension}): simd=({simd[i].X},{simd[i].Y},{simd[i].ModuleSize}), scalar=({scalar[i].X},{scalar[i].Y},{scalar[i].ModuleSize})");
                    }
                }

                if (finderCount == 3)
                {
                    Assert.True(scalarFound, $"3-finder scene not detected (seed={seed}, dim={dimension}) — scene generator or finder broken");
                }
            }
        }
    }

    /// <summary>
    /// QR-like scene: quiet zone, up to three 7×7 finder patterns (dark border,
    /// light ring, dark center) with light separators, timing lines, and ~45%
    /// random dark data modules elsewhere.
    /// </summary>
    private static byte[] BuildQrScene(int dimension, int ppm, int finderCount, int seed, out int width, out int height)
    {
        const int Quiet = 4;
        var totalModules = dimension + 2 * Quiet;
        width = totalModules * ppm;
        height = totalModules * ppm;
        var scene = new byte[width * height];
        scene.AsSpan().Fill(255);

        // Finder pattern top-left corners in symbol module coordinates
        var corners = new (int X, int Y)[] { (0, 0), (dimension - 7, 0), (0, dimension - 7) };

        var random = new Random(seed);
        for (var my = 0; my < dimension; my++)
        {
            for (var mx = 0; mx < dimension; mx++)
            {
                // Reserved: finder zones + their 1-module separators (light)
                var reserved = false;
                for (var f = 0; f < finderCount; f++)
                {
                    if (mx >= corners[f].X - 1 && mx <= corners[f].X + 7 && my >= corners[f].Y - 1 && my <= corners[f].Y + 7)
                    {
                        reserved = true;
                        break;
                    }
                }
                if (reserved)
                    continue;

                if (finderCount > 0 && (my == 6 || mx == 6))
                {
                    // Timing lines: alternating, dark on even module index
                    if ((mx + my) % 2 == 0)
                        FillModule(scene, width, ppm, Quiet + mx, Quiet + my, 0);
                }
                else if (random.Next(100) < 45)
                {
                    FillModule(scene, width, ppm, Quiet + mx, Quiet + my, 0);
                }
            }
        }

        for (var f = 0; f < finderCount; f++)
        {
            for (var dy = 0; dy < 7; dy++)
            {
                for (var dx = 0; dx < 7; dx++)
                {
                    // dark border (ring 3), light ring (ring 2), dark 3×3 center
                    var ring = Math.Max(Math.Abs(dx - 3), Math.Abs(dy - 3));
                    if (ring != 2)
                    {
                        FillModule(scene, width, ppm, Quiet + corners[f].X + dx, Quiet + corners[f].Y + dy, 0);
                    }
                }
            }
        }

        return scene;
    }

    private static void FillModule(byte[] scene, int width, int ppm, int moduleX, int moduleY, byte value)
    {
        for (var y = moduleY * ppm; y < (moduleY + 1) * ppm; y++)
        {
            scene.AsSpan(y * width + moduleX * ppm, ppm).Fill(value);
        }
    }
}
