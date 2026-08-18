using TUnit.Assertions.Enums;
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
        {
            Skip.Test("AVX2 not supported on this machine");
            return;
        }

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

                            await Assert.That(actual).IsEquivalentTo(expected, CollectionOrdering.Matching)
                                .Because($"layout {layout}, {width}x{height}, rowBytes {rowBytes}, alphaShape {alphaShape}, premultiplied {premultiplied}");
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// The vector loops write 32 luminance bytes at a time and the tail redoes the last
    /// 8 pixels, so a block-count that rounds the wrong way writes past the caller's
    /// destination. The destination is given a poisoned tail beyond the bytes the
    /// converter owns, and that tail must come back untouched.
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
    public async Task Avx2Tier_WritesNothingPastTheDestination(int layout)
    {
        if (!LuminanceConverter.IsAvx2TierSupported)
        {
            Skip.Test("AVX2 not supported on this machine");
            return;
        }

        const byte Poison = 0x5A;
        const int TailBytes = 64;

        var (ro, go, bo, ao) = Offsets(layout);
        // Both AVX2 kernels: the straight-alpha one and the premultiplied one are separate
        // loops with their own block arithmetic, so covering only one leaves the other's
        // bounds untested (verified by mutation — an over-run in the uncovered kernel
        // passed the whole class).
        var alphaModes = ao < 0 ? new[] { false } : [false, true];

        foreach (var premultiplied in alphaModes)
        {
            foreach (var width in new[] { 1, 7, 8, 9, 31, 32, 33, 40, 139, 376 })
            {
                foreach (var height in new[] { 1, 3 })
                {
                    var rowBytes = width * 4;
                    // Alpha shape 1 keeps some pixels non-opaque, so the premultiplied
                    // kernel's in-vector alpha handling is actually entered.
                    var pixels = MakePixels(width, height, rowBytes, premultiplied ? 1 : 0, width);

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
                        .Because($"layout {layout}, {width}x{height}, premultiplied {premultiplied}");

                    for (var i = width * height; i < backing.Length; i++)
                    {
                        await Assert.That(backing[i]).IsEqualTo(Poison)
                            .Because($"layout {layout}, {width}x{height}, premultiplied {premultiplied}: wrote {i - width * height + 1} byte(s) past the destination");
                    }
                }
            }
        }
    }
}
