using TUnit.Assertions.Enums;
using SkiaSharp.QrCode.Internals.ImageDecoders;

namespace SkiaSharp.QrCode.Tests;

/// <summary>
/// Whichever <see cref="LuminanceConverter"/> vector tier this machine runs (the AVX2
/// kernel on x86-64, the NEON kernel on ARM64) against the portable per-pixel loop,
/// byte for byte, over every pixel layout the converter accepts, premultiplied and
/// straight alpha, and alpha shapes that pin the contract corners: fully opaque, fully
/// transparent, transparent-or-opaque only, and arbitrary partial alpha.
/// </summary>
/// <remarks>
/// Widths straddle every vector step of both kernels (8, 16 and 32) and include the
/// degenerate narrow cases, and rows are tested both tightly packed and padded, because
/// <c>SKPixmap.RowBytes</c> may exceed width × 4.
/// <para>
/// The alpha shapes are not decoration: each kernel routes them to different code. The
/// NEON tier alone has four row modes — no alpha, premultiplied, an optimistic pass for
/// fully opaque rows, and a per-block classifier that whitens or composites — and each
/// carries its own block and tail arithmetic, so a shape that is not exercised is a
/// kernel that is not tested.
/// </para>
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

    /// <summary>
    /// The specification, written the obvious way: one pixel at a time, plain indexing,
    /// no specialization. Slow and not shipped — it exists so the shipped scalar tier
    /// has something to be checked against.
    /// </summary>
    private static byte[] NaiveReference(byte[] pixels, int width, int height, int rowBytes, int ro, int go, int bo, int ao, bool premultiplied)
    {
        var result = new byte[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var p = y * rowBytes + x * 4;
                int r = pixels[p + ro];
                int g = pixels[p + go];
                int b = pixels[p + bo];

                if (ao >= 0)
                {
                    int a = pixels[p + ao];
                    if (a != 255)
                    {
                        if (premultiplied)
                        {
                            r += 255 - a;
                            g += 255 - a;
                            b += 255 - a;
                        }
                        else
                        {
                            r = (r * a + 255 * (255 - a)) / 255;
                            g = (g * a + 255 * (255 - a)) / 255;
                            b = (b * a + 255 * (255 - a)) / 255;
                        }
                    }
                }

                result[y * width + x] = (byte)((77 * r + 150 * g + 29 * b) >> 8);
            }
        }
        return result;
    }

    /// <summary>
    /// The scalar tier against that naive reference. This is not redundant with the
    /// vector-parity test below: that one uses the scalar tier <em>as</em> its expected
    /// value, so once the scalar tier stopped being a plain per-pixel loop — it
    /// specializes per layout, walks by ref and takes four pixels per iteration — an
    /// error shared by both tiers would pass unnoticed. This test is what pins the
    /// scalar tier to the specification.
    /// </summary>
    [Test]
    [MethodDataSource(nameof(Layouts))]
    public async Task ScalarTier_MatchesNaiveReference_EveryLayoutAndAlphaShape(int layout)
    {
        var (ro, go, bo, ao) = Offsets(layout);
        // 4, 5, 7, 8 straddle the four-pixel step and its remainder.
        int[] widths = [1, 2, 3, 4, 5, 6, 7, 8, 9, 15, 16, 17, 43, 139, 376];
        var alphaShapes = layout == Rgb888x ? new[] { 3 } : [0, 1, 2, 3];

        foreach (var alphaShape in alphaShapes)
        {
            foreach (var premultiplied in ao < 0 ? new[] { false } : [false, true])
            {
                foreach (var width in widths)
                {
                    foreach (var height in new[] { 1, 2, 5 })
                    {
                        foreach (var pad in new[] { 0, 12 })
                        {
                            var rowBytes = width * 4 + pad;
                            var pixels = MakePixels(width, height, rowBytes, alphaShape, width * 7 + height + pad + alphaShape);

                            var expected = NaiveReference(pixels, width, height, rowBytes, ro, go, bo, ao, premultiplied);

                            var actual = new byte[width * height];
                            actual.AsSpan().Fill(0xA5);
                            LuminanceConverter.ConvertRgbaForTest(pixels, actual, width, height, rowBytes, ro, go, bo, ao, premultiplied, forceScalar: true);

                            await Assert.That(actual).IsEquivalentTo(expected, CollectionOrdering.Matching)
                                .Because($"layout {layout}, {width}x{height}, rowBytes {rowBytes}, alphaShape {alphaShape}, premultiplied {premultiplied}");
                        }
                    }
                }
            }
        }
    }

    [Test]
    [MethodDataSource(nameof(Layouts))]
    public async Task VectorTier_MatchesScalarTier_EveryLayoutAndAlphaShape(int layout)
    {
        if (!LuminanceConverter.IsAvx2TierSupported && !LuminanceConverter.IsAdvSimdTierSupported)
        {
            Skip.Test("No vector tier (AVX2 or NEON) on this machine");
            return;
        }

        var (ro, go, bo, ao) = Offsets(layout);
        // 16, 17, 20 and 24 straddle the NEON block and its overlapping tail (a width of
        // 20 makes the final block redo 12 pixels the main loop already wrote); 15 and
        // below fall under the NEON tier's minimum and must still come back correct via
        // the scalar path.
        int[] widths = [1, 3, 4, 5, 7, 8, 9, 15, 16, 17, 20, 24, 31, 32, 33, 43, 63, 139, 376];
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

                            await Assert.That(actual).IsEquivalentTo(expected, CollectionOrdering.Matching)
                                .Because($"layout {layout}, {width}x{height}, rowBytes {rowBytes}, alphaShape {alphaShape}, premultiplied {premultiplied}");
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// The vector loops write a whole block of luminance bytes at a time (32 on AVX2,
    /// 16 on NEON) and finish the row with an overlapping block, so a block-count that
    /// rounds the wrong way writes past the caller's destination. The destination is
    /// given a poisoned tail beyond the bytes the converter owns, and that tail must
    /// come back untouched.
    /// </summary>
    /// <remarks>
    /// Comparing only the first width × height bytes cannot see this: an over-write
    /// lands past the compared region and every asserted byte is still correct.
    /// Verified by mutation — rounding <c>blockEnd</c> up in
    /// <c>LuminanceConverter.Simd.cs</c> leaves the parity assertions green and is
    /// caught only by the tail.
    /// </remarks>
    [Test]
    [MethodDataSource(nameof(Layouts))]
    public async Task VectorTier_WritesNothingPastTheDestination(int layout)
    {
        if (!LuminanceConverter.IsAvx2TierSupported && !LuminanceConverter.IsAdvSimdTierSupported)
        {
            Skip.Test("No vector tier (AVX2 or NEON) on this machine");
            return;
        }

        const byte Poison = 0x5A;
        const int TailBytes = 64;

        var (ro, go, bo, ao) = Offsets(layout);
        // Every alpha shape, not just one per alpha mode: the straight-alpha and
        // premultiplied kernels are separate loops with their own block arithmetic, and
        // within straight alpha the opaque, whitening and compositing paths are three
        // more (verified by mutation — an over-run in an uncovered path passed the whole
        // class). Shape 0 is opaque, 2 is transparent-or-opaque (whitening), 3 is
        // arbitrary partial alpha (compositing).
        var alphaModes = ao < 0 ? new[] { false } : [false, true];

        foreach (var premultiplied in alphaModes)
        foreach (var alphaShape in ao < 0 ? new[] { 3 } : [0, 1, 2, 3])
        {
            foreach (var width in new[] { 1, 7, 8, 9, 16, 17, 20, 24, 31, 32, 33, 40, 139, 376 })
            {
                foreach (var height in new[] { 1, 3 })
                {
                    var rowBytes = width * 4;
                    var pixels = MakePixels(width, height, rowBytes, alphaShape, width + alphaShape);

                    var expected = new byte[width * height];
                    LuminanceConverter.ConvertRgbaForTest(pixels, expected, width, height, rowBytes, ro, go, bo, ao, premultiplied, forceScalar: true);

                    // Destination is longer than the converter's region; only the first
                    // width × height bytes are handed over as its span.
                    var backing = new byte[width * height + TailBytes];
                    backing.AsSpan().Fill(Poison);
                    var destination = backing.AsSpan(0, width * height);

                    // Exactly sized source too, so an overread past rowBytes × height is
                    // not silently satisfied by the next object on the heap.
                    var exact = pixels.AsSpan(0, rowBytes * height);
                    LuminanceConverter.ConvertRgbaForTest(exact, destination, width, height, rowBytes, ro, go, bo, ao, premultiplied, forceScalar: false);

                    await Assert.That(backing.AsSpan(0, width * height).ToArray())
                        .IsEquivalentTo(expected, CollectionOrdering.Matching)
                        .Because($"layout {layout}, {width}x{height}, alphaShape {alphaShape}, premultiplied {premultiplied}");

                    for (var i = width * height; i < backing.Length; i++)
                    {
                        await Assert.That(backing[i]).IsEqualTo(Poison)
                            .Because($"layout {layout}, {width}x{height}, alphaShape {alphaShape}, premultiplied {premultiplied}: wrote {i - width * height + 1} byte(s) past the destination");
                    }
                }
            }
        }
    }
}
