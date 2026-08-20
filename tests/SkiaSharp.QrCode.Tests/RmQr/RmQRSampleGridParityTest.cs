using SkiaSharp.QrCode.Internals.ImageDecoders;
using SkiaSharp.QrCode.Internals.RmQr;

namespace SkiaSharp.QrCode.Tests;

/// <summary>
/// The rMQR Vector128 grid sampler against the scalar reference, byte for byte, for
/// every version over the three capture geometries the decoder actually builds.
/// </summary>
/// <remarks>
/// The N5 contract is stricter than "decodes the same symbol": the vector kernel must
/// sample the exact same pixels as the scalar loop, so this compares module output
/// byte for byte rather than comparing decode results.
///
/// The three geometries are separate equivalence classes because the kernel dispatches
/// on the transform:
/// <list type="bullet">
/// <item>Affine (a13 = a23 = 0, a33 = 1) — every first attempt on a clean capture, since
/// the isotropic and anisotropic frames are built with perspectiveX = perspectiveY = 0.
/// The kernel skips both divisions here (the denominator is exactly 1f).</item>
/// <item>Rotated affine (a12 != 0) — still affine, but the sampled y varies along a row.
/// Without this class every affine case would be axis-aligned and a y-invariant bug
/// would pass.</item>
/// <item>Projective — the perspective search, where the divisions are actually taken.</item>
/// </list>
/// Clamping and degenerate transforms get their own tests: sampling off the image is
/// the outermost-module case the clamp exists for, and a collapsed frame is what a
/// failed geometry estimate hands the sampler.
/// </remarks>
public class RmQRSampleGridParityTest
{
    private const byte Threshold = 128;

    public static IEnumerable<RmQRVersion> AllVersions() => Enum.GetValues<RmQRVersion>();

    /// <summary>Capture geometry, matching the decoder's three transform shapes.</summary>
    public enum Geometry
    {
        Affine,
        AffineRotated,
        Projective,
    }

    [Test]
    [MethodDataSource(nameof(AllVersions))]
    public async Task Simd128AndScalarSampling_AreByteIdentical_EveryVersion(RmQRVersion version)
    {
#if NET8_0_OR_GREATER
        if (!System.Runtime.Intrinsics.Vector128.IsHardwareAccelerated)
        {
            Skip.Test("Vector128 not accelerated on this machine");
            return;
        }

        var columns = RmQRConstants.GetWidth(version);
        var rows = RmQRConstants.GetHeight(version);

        foreach (var seed in new[] { 1, 42, 1234 })
        {
            foreach (var geometry in Enum.GetValues<Geometry>())
            {
                var (luminance, width, height, transform) = BuildScene(columns, rows, geometry, seed);

                var scalar = new byte[columns * rows];
                RmQRImageDecoder.SampleGridScalar(luminance, width, height, Threshold, transform, columns, rows, scalar);

                var simd = new byte[columns * rows];
                RmQRImageDecoder.SampleGridSimd128(luminance, width, height, Threshold, transform, columns, rows, simd);

                await Assert.That(simd.AsSpan().SequenceEqual(scalar)).IsTrue()
                    .Because($"SIMD/scalar sampling mismatch ({version}, seed={seed}, {geometry})");
            }
        }
#endif
    }

    /// <summary>
    /// Frames that sample outside the image: the clamp is the only thing keeping the
    /// outermost modules in bounds, and the vector path clamps with min/max instead of
    /// the scalar branches, so the two must still agree on every edge pixel.
    /// </summary>
    [Test]
    [Arguments(-40f, 0f)]
    [Arguments(0f, -30f)]
    [Arguments(400f, 0f)]
    [Arguments(0f, 300f)]
    [Arguments(-40f, -30f)]
    public async Task Simd128AndScalarSampling_AreByteIdentical_WhenSamplingOffImage(float offsetX, float offsetY)
    {
#if NET8_0_OR_GREATER
        if (!System.Runtime.Intrinsics.Vector128.IsHardwareAccelerated)
        {
            Skip.Test("Vector128 not accelerated on this machine");
            return;
        }

        foreach (var version in new[] { RmQRVersion.R7x43, RmQRVersion.R17x139 })
        {
            var columns = RmQRConstants.GetWidth(version);
            var rows = RmQRConstants.GetHeight(version);
            var (luminance, width, height, _) = BuildScene(columns, rows, Geometry.Affine, seed: 7);

            // Same frame, shifted so a whole band of module centers lands off the image.
            var transform = PerspectiveTransform.FromLocalFrame(
                3.5f, 3.5f, 2 * 8 + 3.5f * 8 + offsetX, 2 * 8 + 3.5f * 8 + offsetY, 8f, 0f, 0f, 8f, 0f, 0f);

            var scalar = new byte[columns * rows];
            RmQRImageDecoder.SampleGridScalar(luminance, width, height, Threshold, transform, columns, rows, scalar);

            var simd = new byte[columns * rows];
            RmQRImageDecoder.SampleGridSimd128(luminance, width, height, Threshold, transform, columns, rows, simd);

            await Assert.That(simd.AsSpan().SequenceEqual(scalar)).IsTrue()
                .Because($"clamped sampling mismatch ({version}, offset {offsetX}/{offsetY})");
        }
#endif
    }

    /// <summary>
    /// Degenerate frames the geometry search can hand the sampler: a collapsed axis
    /// (zero derivative) and a projective frame whose denominator crosses zero, which
    /// produces infinities and NaN. The kernel must fall back gracefully to whatever
    /// the scalar loop does rather than diverge from it.
    /// </summary>
    [Test]
    public async Task Simd128AndScalarSampling_AreByteIdentical_ForDegenerateFrames()
    {
#if NET8_0_OR_GREATER
        if (!System.Runtime.Intrinsics.Vector128.IsHardwareAccelerated)
        {
            Skip.Test("Vector128 not accelerated on this machine");
            return;
        }

        var columns = RmQRConstants.GetWidth(RmQRVersion.R11x59);
        var rows = RmQRConstants.GetHeight(RmQRVersion.R11x59);
        var (luminance, width, height, _) = BuildScene(columns, rows, Geometry.Affine, seed: 3);

        var frames = new[]
        {
            // Collapsed row axis: every module in a row samples the same pixel.
            PerspectiveTransform.FromLocalFrame(3.5f, 3.5f, 60f, 40f, 0f, 0f, 0f, 8f, 0f, 0f),
            // Collapsed both axes.
            PerspectiveTransform.FromLocalFrame(3.5f, 3.5f, 60f, 40f, 0f, 0f, 0f, 0f, 0f, 0f),
            // Denominator crosses zero inside the symbol: infinities and NaN.
            PerspectiveTransform.FromLocalFrame(3.5f, 3.5f, 60f, 40f, 8f, 0f, 0f, 8f, -1f / 20f, 0f),
            // Negative scale (mirrored frame).
            PerspectiveTransform.FromLocalFrame(3.5f, 3.5f, 60f, 40f, -8f, 0f, 0f, -8f, 0f, 0f),
        };

        for (var i = 0; i < frames.Length; i++)
        {
            var scalar = new byte[columns * rows];
            RmQRImageDecoder.SampleGridScalar(luminance, width, height, Threshold, frames[i], columns, rows, scalar);

            var simd = new byte[columns * rows];
            RmQRImageDecoder.SampleGridSimd128(luminance, width, height, Threshold, frames[i], columns, rows, simd);

            await Assert.That(simd.AsSpan().SequenceEqual(scalar)).IsTrue()
                .Because($"degenerate frame {i} mismatch");
        }
#endif
    }

    /// <summary>
    /// Random module scene at 8 px/module with the 2-module quiet zone, and the
    /// finder-anchored local frame the decoder builds at grid (3.5, 3.5).
    /// </summary>
    private static (byte[] Luminance, int Width, int Height, PerspectiveTransform Transform) BuildScene(int columns, int rows, Geometry geometry, int seed)
    {
        const int Ppm = 8;
        const int Quiet = 2;
        var width = (columns + 2 * Quiet) * Ppm;
        var height = (rows + 2 * Quiet) * Ppm;
        var luminance = new byte[width * height];
        luminance.AsSpan().Fill(255);

        var random = new Random(seed);
        for (var my = 0; my < rows + 2 * Quiet; my++)
        {
            for (var mx = 0; mx < columns + 2 * Quiet; mx++)
            {
                if (random.Next(100) < 45)
                {
                    for (var y = my * Ppm; y < (my + 1) * Ppm; y++)
                    {
                        luminance.AsSpan(y * width + mx * Ppm, Ppm).Clear();
                    }
                }
            }
        }

        var centerX = (Quiet + 3.5f) * Ppm;
        var centerY = (Quiet + 3.5f) * Ppm;
        var perspectiveX = geometry == Geometry.Projective ? -0.03f / columns : 0f;
        var perspectiveY = geometry == Geometry.Projective ? 0.02f / rows : 0f;
        var angle = geometry == Geometry.AffineRotated ? 12f * (float)(Math.PI / 180d) : 0f;
        var cos = (float)Math.Cos(angle);
        var sin = (float)Math.Sin(angle);

        return (luminance, width, height, PerspectiveTransform.FromLocalFrame(
            3.5f, 3.5f, centerX, centerY, Ppm * cos, Ppm * sin, -Ppm * sin, Ppm * cos, perspectiveX, perspectiveY));
    }
}
