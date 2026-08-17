using SkiaSharp.QrCode.Internals.ImageDecoders;

namespace SkiaSharp.QrCode.Tests;

/// <summary>
/// The two <see cref="LuminanceConverter"/> tiers (the AVX2 kernel and the portable
/// per-pixel loop) against each other, byte for byte, over every pixel layout the
/// converter accepts, premultiplied and straight alpha, and alpha shapes that pin the
/// contract corners: fully opaque, fully transparent, transparent-or-opaque only, and
/// arbitrary partial alpha (which is what the straight-alpha vector path must hand
/// back to the scalar formula).
/// </summary>
/// <remarks>
/// Widths straddle every vector step (8 and 32) and include the degenerate narrow
/// cases, and rows are tested both tightly packed and padded, because
/// <c>SKPixmap.RowBytes</c> may exceed width × 4.
/// </remarks>
public class LuminanceConverterParityTest
{
    private const int Bgra = 0;
    private const int Rgba = 1;
    private const int Rgb888x = 2;

    public static IEnumerable<int> Layouts() => [Bgra, Rgba, Rgb888x];

    private static (int R, int G, int B, int A) Offsets(int layout) => layout switch
    {
        Bgra => (2, 1, 0, 3),
        Rgba => (0, 1, 2, 3),
        _ => (0, 1, 2, -1),
    };

    /// <summary>Alpha shapes: 0 opaque, 1 transparent, 2 transparent-or-opaque, 3 arbitrary partial.</summary>
    private static byte[] MakePixels(int width, int height, int rowBytes, int alphaShape, int seed)
    {
        var pixels = new byte[rowBytes * height];
        var state = (uint)seed * 2654435761u + 7u;
        for (var i = 0; i + 4 <= pixels.Length; i += 4)
        {
            state = state * 1664525u + 1013904223u;
            var r = state >> 8;
            // Mostly saturated black or white as a QR render is, plus mid-tones so the
            // channel weights actually matter.
            var mono = (r & 3) == 0 ? (byte)(r >> 16) : (byte)((r & 4) == 0 ? 0 : 255);
            pixels[i] = mono;
            pixels[i + 1] = (byte)(mono ^ (byte)((r >> 5) & 3));
            pixels[i + 2] = (byte)(mono ^ (byte)((r >> 7) & 7));
            pixels[i + 3] = alphaShape switch
            {
                0 => 255,
                1 => 0,
                2 => (r & 3) == 0 ? (byte)0 : (byte)255,
                _ => (r & 15) == 0 ? (byte)(r >> 20) : (byte)255,
            };
        }
        return pixels;
    }

    [Test]
    [MethodDataSource(nameof(Layouts))]
    public async Task Avx2Tier_MatchesScalarTier_EveryLayoutAndAlphaShape(int layout)
    {
        if (!LuminanceConverter.IsAvx2TierSupported)
            return;

        var (ro, go, bo, ao) = Offsets(layout);
        int[] widths = [1, 3, 4, 5, 7, 8, 9, 15, 16, 31, 32, 33, 43, 63, 139, 376];
        int[] heights = [1, 2, 5];
        var alphaShapes = layout == Rgb888x ? new[] { 3 } : [0, 1, 2, 3];

        foreach (var alphaShape in alphaShapes)
        {
            foreach (var premultiplied in ao < 0 ? new[] { false } : [false, true])
            {
                foreach (var width in widths)
                {
                    foreach (var height in heights)
                    {
                        foreach (var pad in new[] { 0, 12 })
                        {
                            var rowBytes = width * 4 + pad;
                            var pixels = MakePixels(width, height, rowBytes, alphaShape, width * 7 + height + pad + alphaShape);

                            var expected = new byte[width * height];
                            LuminanceConverter.ConvertRgbaForTest(pixels, expected, width, height, rowBytes, ro, go, bo, ao, premultiplied, forceScalar: true);

                            // Poison the destination so a kernel that skips a pixel is caught.
                            var actual = new byte[width * height];
                            actual.AsSpan().Fill(0xA5);
                            LuminanceConverter.ConvertRgbaForTest(pixels, actual, width, height, rowBytes, ro, go, bo, ao, premultiplied, forceScalar: false);

                            await Assert.That(actual).IsEquivalentTo(expected)
                                .Because($"layout {layout}, {width}x{height}, rowBytes {rowBytes}, alphaShape {alphaShape}, premultiplied {premultiplied}");
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// The vector loops read 128 bytes at a time and the overlapping tail block reads
    /// the last 8 pixels again, so this pins that neither runs past the row: the pixel
    /// span ends exactly at the last pixel and is followed by nothing.
    /// </summary>
    [Test]
    [MethodDataSource(nameof(Layouts))]
    public async Task Avx2Tier_StaysInsideThePixelSpan(int layout)
    {
        if (!LuminanceConverter.IsAvx2TierSupported)
            return;

        var (ro, go, bo, ao) = Offsets(layout);
        foreach (var width in new[] { 8, 33, 40, 139, 376 })
        {
            const int height = 3;
            var rowBytes = width * 4;
            var pixels = MakePixels(width, height, rowBytes, 0, width);

            var expected = new byte[width * height];
            LuminanceConverter.ConvertRgbaForTest(pixels, expected, width, height, rowBytes, ro, go, bo, ao, premultiplied: false, forceScalar: true);

            // Exactly sized span: an overread past rowBytes * height would fault or
            // read the following heap bytes, and the padded copy below would differ.
            var exact = pixels.AsSpan(0, rowBytes * height);
            var actual = new byte[width * height];
            LuminanceConverter.ConvertRgbaForTest(exact, actual, width, height, rowBytes, ro, go, bo, ao, premultiplied: false, forceScalar: false);

            await Assert.That(actual).IsEquivalentTo(expected).Because($"layout {layout}, width {width}");
        }
    }
}
