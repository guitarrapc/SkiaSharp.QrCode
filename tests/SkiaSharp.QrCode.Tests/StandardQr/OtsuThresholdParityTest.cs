using SkiaSharp.QrCode.Internals.StandardQr;
using SkiaSharp.QrCode.Internals.ImageDecoders;

namespace SkiaSharp.QrCode.Tests;

/// <summary>
/// Parity test for the run-aggregated Otsu histogram fill: the wide-load walk in
/// <see cref="QRImageDecoder.ComputeOtsuThreshold"/> (8 pixels per ulong, uniform
/// groups folded into a single += 8) must return a byte-identical threshold to a
/// naive per-pixel reference across QR-like scenes, noise, gradients, constants,
/// and ragged lengths (tail handling).
/// </summary>
public class OtsuThresholdParityTest
{
    [Test]
    public async Task WideLoadAndNaiveHistogram_AgreeByteForByte()
    {
        var inputs = new List<byte[]>
        {
            BuildQrLike(360, ppm: 8, seed: 42),
            BuildQrLike(96, ppm: 4, seed: 7),
            BuildNoise(512, seed: 42),
            BuildNoise(64, seed: 1234),
            BuildGradient(256),
            new byte[1000],          // all zero
            Filled(1000, 255),       // all white
            Filled(777, 128),        // constant mid-gray
            Array.Empty<byte>(),     // empty (returns the 128 default)
        };
        // Ragged lengths exercise the scalar tail after the 8-byte groups
        var rng = new Random(9);
        for (var length = 1; length <= 67; length++)
        {
            var ragged = new byte[length];
            rng.NextBytes(ragged);
            inputs.Add(ragged);
        }

        foreach (var input in inputs)
        {
            var expected = NaiveOtsuThreshold(input);
            var actual = QRImageDecoder.ComputeOtsuThreshold(input);
            await Assert.That(expected == actual).IsTrue().Because($"threshold mismatch on length={input.Length}: wide={actual}, naive={expected}");
        }
    }

    /// <summary>Per-pixel reference: the original histogram fill + the same search.</summary>
    private static byte NaiveOtsuThreshold(ReadOnlySpan<byte> luminance)
    {
        Span<int> histogram = stackalloc int[256];
        histogram.Clear();
        foreach (var value in luminance)
        {
            histogram[value]++;
        }

        var total = luminance.Length;
        long sumAll = 0;
        for (var i = 0; i < 256; i++)
        {
            sumAll += (long)i * histogram[i];
        }

        long sumBackground = 0;
        long weightBackground = 0;
        var bestVariance = -1.0;
        var bestThreshold = 128;

        for (var t = 0; t < 256; t++)
        {
            weightBackground += histogram[t];
            if (weightBackground == 0)
                continue;
            var weightForeground = total - weightBackground;
            if (weightForeground == 0)
                break;

            sumBackground += (long)t * histogram[t];
            var meanBackground = (double)sumBackground / weightBackground;
            var meanForeground = (double)(sumAll - sumBackground) / weightForeground;
            var diff = meanBackground - meanForeground;
            var variance = weightBackground * (double)weightForeground * diff * diff;

            if (variance > bestVariance)
            {
                bestVariance = variance;
                bestThreshold = t + 1;
            }
        }

        return (byte)Math.Min(bestThreshold, 255);
    }

    private static byte[] BuildQrLike(int size, int ppm, int seed)
    {
        var scene = new byte[size * size];
        scene.AsSpan().Fill(255);
        var random = new Random(seed);
        var modules = size / ppm;
        for (var my = 0; my < modules; my++)
        {
            for (var mx = 0; mx < modules; mx++)
            {
                if (random.Next(100) < 45)
                {
                    for (var y = my * ppm; y < (my + 1) * ppm; y++)
                    {
                        scene.AsSpan(y * size + mx * ppm, ppm).Clear();
                    }
                }
            }
        }
        return scene;
    }

    private static byte[] BuildNoise(int size, int seed)
    {
        var scene = new byte[size * size];
        new Random(seed).NextBytes(scene);
        return scene;
    }

    private static byte[] BuildGradient(int size)
    {
        var scene = new byte[size * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                scene[y * size + x] = (byte)((x + y) * 255 / (2 * size - 2));
            }
        }
        return scene;
    }

    private static byte[] Filled(int length, byte value)
    {
        var buffer = new byte[length];
        buffer.AsSpan().Fill(value);
        return buffer;
    }
}
