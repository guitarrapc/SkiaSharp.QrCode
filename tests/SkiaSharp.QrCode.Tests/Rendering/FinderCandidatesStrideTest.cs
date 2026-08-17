using SkiaSharp.QrCode.Image;
using SkiaSharp.QrCode.Internals.ImageDecoders;

namespace SkiaSharp.QrCode.Tests;

/// <summary>
/// <see cref="FinderPatternFinder.FindCandidates"/> (strided, with the
/// confirmation-triggered complementary pass) against
/// <see cref="FinderPatternFinder.FindCandidatesFullSweep"/> on real renders across
/// the documented module-size envelope.
/// </summary>
/// <remarks>
/// Striding is not bit-identical by construction: fewer rows merge into a candidate,
/// so its sub-pixel centre and its <c>Count</c> both move. The invariant that matters
/// to the decoders is that no *confirmed* candidate of a full sweep disappears, since
/// that is what they rank and try. The no-symbol case is covered too: there the
/// strided pass finds nothing confirmed, so the complementary pass must run and the
/// two paths must agree exactly.
/// </remarks>
public class FinderCandidatesStrideTest
{
    public static IEnumerable<int> ModulePixelSizes() => [3, 4, 5, 8, 13];

    private static (byte[] Luminance, int Width, int Height, byte Threshold) Render(RmQRCodeData data, int modulePixelSize)
    {
        using var bitmap = new RmQRCodeImageBuilder(data).WithModulePixelSize(modulePixelSize).ToBitmap();
        var luminance = new byte[bitmap.Width * bitmap.Height];
        LuminanceConverter.Convert(bitmap, luminance);
        return (luminance, bitmap.Width, bitmap.Height, Binarizer.ComputeOtsuThreshold(luminance));
    }

    [Test]
    [MethodDataSource(nameof(ModulePixelSizes))]
    public async Task StridedPath_KeepsEveryConfirmedCandidateOfAFullSweep(int modulePixelSize)
    {
        // Both extremes of the rMQR shape range: the narrowest and the widest symbol.
        var symbols = new[]
        {
            RmQRCodeGenerator.CreateRmQRCode("RMQR 43", RmQREccLevel.M, RmQRVersion.R7x43),
            RmQRCodeGenerator.CreateRmQRCode("27", RmQREccLevel.M, RmQRVersion.R11x27),
            RmQRCodeGenerator.CreateRmQRCode(
                string.Concat(Enumerable.Repeat("the quick brown fox jumps over the lazy dog?! ", 4)).Substring(0, 150),
                RmQREccLevel.M, RmQRVersion.R17x139),
        };

        foreach (var data in symbols)
        {
            var (luminance, width, height, threshold) = Render(data, modulePixelSize);

            var full = new FinderPattern[FinderPatternFinder.MaxFinderCandidates];
            var fullCount = FinderPatternFinder.FindCandidatesFullSweep(luminance, width, height, threshold, full);

            var strided = new FinderPattern[FinderPatternFinder.MaxFinderCandidates];
            var stridedCount = FinderPatternFinder.FindCandidates(luminance, width, height, threshold, strided);

            await Assert.That(fullCount).IsGreaterThan(0)
                .Because($"{data.Version} at {modulePixelSize} px/module must have a findable finder at all");

            for (var i = 0; i < fullCount; i++)
            {
                if (full[i].Count < 2)
                    continue;

                var kept = false;
                for (var j = 0; j < stridedCount; j++)
                {
                    if (Math.Abs(strided[j].X - full[i].X) <= 1f
                        && Math.Abs(strided[j].Y - full[i].Y) <= 1f
                        && Math.Abs(strided[j].ModuleSize - full[i].ModuleSize) <= 0.75f)
                    {
                        kept = true;
                        break;
                    }
                }

                await Assert.That(kept).IsTrue()
                    .Because($"{data.Version} at {modulePixelSize} px/module: strided scan lost the confirmed candidate at ({full[i].X}, {full[i].Y}) seen on {full[i].Count} rows");
            }
        }
    }

    /// <summary>
    /// With no symbol present nothing gets confirmed, so the complementary pass always
    /// runs and the strided path must reproduce the full sweep exactly - that is what
    /// makes a detection miss cost one sweep instead of two.
    /// </summary>
    [Test]
    public async Task NoSymbol_FallsBackToAnExactFullSweep()
    {
        const int width = 320;
        const int height = 200;
        var luminance = new byte[width * height];
        var state = 12345u;
        for (var i = 0; i < luminance.Length; i++)
        {
            state = state * 1664525u + 1013904223u;
            luminance[i] = (byte)(state >> 24);
        }

        var full = new FinderPattern[FinderPatternFinder.MaxFinderCandidates];
        var fullCount = FinderPatternFinder.FindCandidatesFullSweep(luminance, width, height, 128, full);

        var strided = new FinderPattern[FinderPatternFinder.MaxFinderCandidates];
        var stridedCount = FinderPatternFinder.FindCandidates(luminance, width, height, 128, strided);

        // Order differs (the strided rows come first), so compare as sets.
        await Assert.That(stridedCount).IsEqualTo(fullCount);
        for (var i = 0; i < fullCount; i++)
        {
            var matched = false;
            for (var j = 0; j < stridedCount; j++)
            {
                if (strided[j].X == full[i].X && strided[j].Y == full[i].Y
                    && strided[j].ModuleSize == full[i].ModuleSize && strided[j].Count == full[i].Count)
                {
                    matched = true;
                    break;
                }
            }
            await Assert.That(matched).IsTrue()
                .Because($"noise image: candidate ({full[i].X}, {full[i].Y}) count {full[i].Count} missing from the strided path");
        }
    }
}
