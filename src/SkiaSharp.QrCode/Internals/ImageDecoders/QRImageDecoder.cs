using System.Buffers;

using SkiaSharp.QrCode.Internals.BinaryDecoders;

namespace SkiaSharp.QrCode.Internals.ImageDecoders;

/// <summary>
/// Decodes a QR code from a grayscale image (Tier 1: clean, well-lit, screen-rendered
/// or scanned inputs, including arbitrary rotation).
/// </summary>
/// <remarks>
/// Pipeline:
/// <code>
/// 1. Global binarization threshold (Otsu's method over the luminance histogram)
/// 2. Finder pattern detection (1:1:3:1:1 scan + cross checks)
/// 3. Orientation from the three finder centers (rotation-invariant)
/// 4. Dimension estimate from center distances and module size
/// 5. Affine grid sampling into a module matrix
/// 6. Matrix decoding (format → unmask → deinterleave → Reed-Solomon → bitstream)
/// </code>
/// Out of scope for Tier 1 (documented, by design): perspective distortion beyond
/// what affine sampling absorbs, uneven lighting (global threshold only), blur,
/// and multiple QR codes per image.
/// </remarks>
internal static class QRImageDecoder
{
    /// <summary>
    /// Decodes a QR code from grayscale pixels.
    /// </summary>
    /// <param name="luminance">Grayscale pixels, row-major, width × height bytes.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="destination">Destination buffer for decoded characters.</param>
    /// <param name="charsWritten">Number of characters written.</param>
    /// <param name="info">Diagnostic information.</param>
    public static QRCodeDecodeStatus DecodeLuminance(ReadOnlySpan<byte> luminance, int width, int height, Span<char> destination, out int charsWritten, out QRCodeDecodeInfo info)
    {
        charsWritten = 0;

        var threshold = ComputeOtsuThreshold(luminance);

        Span<FinderPattern> patterns = stackalloc FinderPattern[3];
        if (!FinderPatternFinder.TryFind(luminance, width, height, threshold, patterns))
        {
            info = new QRCodeDecodeInfo(QRCodeDecodeStatus.NotDetected, 0, default, -1, 0);
            return QRCodeDecodeStatus.NotDetected;
        }

        OrderFinderPatterns(patterns, out var topLeft, out var topRight, out var bottomLeft);

        if (!TryEstimateDimension(luminance, width, height, threshold, topLeft, topRight, bottomLeft, out var dimension))
        {
            info = new QRCodeDecodeInfo(QRCodeDecodeStatus.NotDetected, 0, default, -1, 0);
            return QRCodeDecodeStatus.NotDetected;
        }

        var rented = ArrayPool<byte>.Shared.Rent(dimension * dimension);
        try
        {
            var modules = rented.AsSpan(0, dimension * dimension);
            SampleGrid(luminance, width, height, threshold, topLeft, topRight, bottomLeft, dimension, modules);

            var status = QRMatrixDecoder.DecodeMatrix(modules, dimension, destination, out charsWritten, out info);
            if (status == QRCodeDecodeStatus.Success)
                return status;

            // Mirrored image (e.g. front-camera captures): finder geometry is
            // identical but data is transposed. Retry on any decode failure — a
            // mirrored matrix can fail at any stage (a permuted format pattern may
            // even fall within BCH distance of a wrong candidate and surface as
            // DataUncorrectable instead of FormatInformationInvalid).
            TransposeInPlace(modules, dimension);
            var mirroredStatus = QRMatrixDecoder.DecodeMatrix(modules, dimension, destination, out charsWritten, out var mirroredInfo);
            if (mirroredStatus == QRCodeDecodeStatus.Success)
            {
                info = mirroredInfo;
                return mirroredStatus;
            }

            // Both orientations failed: report the original attempt's diagnostics
            return status;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented, clearArray: false);
        }
    }

    /// <summary>
    /// Otsu's method: picks the threshold that maximizes between-class variance
    /// of the luminance histogram. Suits Tier-1 inputs with clear bimodal contrast.
    /// </summary>
    internal static byte ComputeOtsuThreshold(ReadOnlySpan<byte> luminance)
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
                bestThreshold = t + 1; // dark: luminance < threshold
            }
        }

        return (byte)Math.Min(bestThreshold, 255);
    }

    /// <summary>
    /// Assigns the three finder centers to their corners: the two farthest apart
    /// span the diagonal (top-right / bottom-left), the remaining one is top-left;
    /// the cross product resolves which diagonal end is which.
    /// </summary>
    private static void OrderFinderPatterns(ReadOnlySpan<FinderPattern> patterns, out FinderPattern topLeft, out FinderPattern topRight, out FinderPattern bottomLeft)
    {
        var d01 = DistanceSquared(patterns[0], patterns[1]);
        var d02 = DistanceSquared(patterns[0], patterns[2]);
        var d12 = DistanceSquared(patterns[1], patterns[2]);

        FinderPattern a, b;
        if (d01 >= d02 && d01 >= d12)
        {
            topLeft = patterns[2];
            a = patterns[0];
            b = patterns[1];
        }
        else if (d02 >= d01 && d02 >= d12)
        {
            topLeft = patterns[1];
            a = patterns[0];
            b = patterns[2];
        }
        else
        {
            topLeft = patterns[0];
            a = patterns[1];
            b = patterns[2];
        }

        // Image coordinates have y pointing down, so for the standard QR layout the
        // cross product (a-topLeft) × (b-topLeft) is positive when a is top-right.
        var cross = (a.X - topLeft.X) * (b.Y - topLeft.Y) - (a.Y - topLeft.Y) * (b.X - topLeft.X);
        if (cross > 0)
        {
            topRight = a;
            bottomLeft = b;
        }
        else
        {
            topRight = b;
            bottomLeft = a;
        }
    }

    /// <summary>
    /// Estimates the matrix dimension from finder center distances and the module
    /// size, snapped to the nearest valid QR dimension (17 + 4·version).
    /// </summary>
    /// <remarks>
    /// The module size must NOT come from the horizontal-scan run widths: those are
    /// measured along image rows and grow by up to √2 under rotation (at 45° a row
    /// cuts the rotated rings diagonally). Instead it is measured along the actual
    /// finder-to-finder lines, which is rotation-invariant.
    /// </remarks>
    private static bool TryEstimateDimension(ReadOnlySpan<byte> luminance, int width, int height, byte threshold, in FinderPattern topLeft, in FinderPattern topRight, in FinderPattern bottomLeft, out int dimension)
    {
        dimension = 0;

        var moduleSize = MeasureModuleSize(luminance, width, height, threshold, topLeft, topRight, bottomLeft);
        if (moduleSize < 1f)
            return false; // below one pixel per module nothing can be sampled reliably

        // Finder centers sit 7 modules apart from the matrix edges
        var widthModules = Distance(topLeft, topRight) / moduleSize + 7f;
        var heightModules = Distance(topLeft, bottomLeft) / moduleSize + 7f;
        var estimate = (widthModules + heightModules) / 2f;

        // Snap to the nearest valid dimension; reject wild estimates
        var version = (int)Math.Round((estimate - 17f) / 4f);
        if (version < 1 || version > 40)
            return false;

        dimension = 17 + version * 4;
        return Math.Abs(estimate - dimension) <= 4f;
    }

    /// <summary>
    /// Measures the module size along the finder-to-finder axes: from each pattern
    /// center, a dark-light-dark run toward (and away from) its neighbor spans
    /// exactly 7 modules of the 1:1:3:1:1 structure — independent of rotation.
    /// </summary>
    private static float MeasureModuleSize(ReadOnlySpan<byte> luminance, int width, int height, byte threshold, in FinderPattern topLeft, in FinderPattern topRight, in FinderPattern bottomLeft)
    {
        var sum = 0f;
        var count = 0;

        Accumulate(MeasureBothWays(luminance, width, height, threshold, topLeft, topRight), ref sum, ref count);
        Accumulate(MeasureBothWays(luminance, width, height, threshold, topRight, topLeft), ref sum, ref count);
        Accumulate(MeasureBothWays(luminance, width, height, threshold, topLeft, bottomLeft), ref sum, ref count);
        Accumulate(MeasureBothWays(luminance, width, height, threshold, bottomLeft, topLeft), ref sum, ref count);

        if (count > 0)
            return sum / count;

        // All measurements clipped (pattern at the image border): fall back to the
        // horizontal-scan estimate, valid for near-axis-aligned inputs.
        return (topLeft.ModuleSize + topRight.ModuleSize + bottomLeft.ModuleSize) / 3f;

        static void Accumulate(float value, ref float sum, ref int count)
        {
            if (!float.IsNaN(value))
            {
                sum += value;
                count++;
            }
        }
    }

    /// <summary>
    /// Dark-light-dark run through <paramref name="from"/>'s center along the line
    /// toward <paramref name="towards"/>, walked in both directions: 3 center modules
    /// plus 1 light and 1 dark ring on each side = 7 modules total.
    /// Returns the module size, or NaN when the run leaves the image.
    /// </summary>
    private static float MeasureBothWays(ReadOnlySpan<byte> luminance, int width, int height, byte threshold, in FinderPattern from, in FinderPattern towards)
    {
        var dx = towards.X - from.X;
        var dy = towards.Y - from.Y;
        var length = (float)Math.Sqrt(dx * dx + dy * dy);
        if (length < 1f)
            return float.NaN;
        dx /= length;
        dy /= length;

        var forward = DarkLightDarkRun(luminance, width, height, threshold, from.X, from.Y, dx, dy);
        var backward = DarkLightDarkRun(luminance, width, height, threshold, from.X, from.Y, -dx, -dy);
        if (float.IsNaN(forward) || float.IsNaN(backward))
            return float.NaN;

        return (forward + backward) / 7f;
    }

    /// <summary>
    /// Walks from a finder center along a direction until the dark-light-dark
    /// sequence completes (center square → light ring → dark ring → out), returning
    /// the traveled distance (≈ 3.5 modules). NaN when the image edge interrupts.
    /// </summary>
    private static float DarkLightDarkRun(ReadOnlySpan<byte> luminance, int width, int height, byte threshold, float startX, float startY, float dirX, float dirY)
    {
        var phase = 0;
        for (var step = 1f; ; step += 1f)
        {
            var x = (int)(startX + dirX * step + 0.5f);
            var y = (int)(startY + dirY * step + 0.5f);
            if (x < 0 || x >= width || y < 0 || y >= height)
                return float.NaN;

            var dark = luminance[y * width + x] < threshold;
            switch (phase)
            {
                case 0: // inside the 3-module center square
                    if (!dark)
                        phase = 1;
                    break;
                case 1: // light ring
                    if (dark)
                        phase = 2;
                    break;
                default: // dark ring; run ends at the transition out of it
                    if (!dark)
                        return step;
                    break;
            }
        }
    }

    /// <summary>
    /// Samples every module center through the affine frame spanned by the three
    /// finder centers. Handles rotation, scale and shear; module (3,3)'s center maps
    /// exactly onto the top-left finder center.
    /// </summary>
    private static void SampleGrid(ReadOnlySpan<byte> luminance, int width, int height, byte threshold, in FinderPattern topLeft, in FinderPattern topRight, in FinderPattern bottomLeft, int dimension, Span<byte> modules)
    {
        var span = dimension - 7; // module distance between finder centers
        var exX = (topRight.X - topLeft.X) / span;
        var exY = (topRight.Y - topLeft.Y) / span;
        var eyX = (bottomLeft.X - topLeft.X) / span;
        var eyY = (bottomLeft.Y - topLeft.Y) / span;

        for (var v = 0; v < dimension; v++)
        {
            var rowBase = v * dimension;
            var du = -3f;
            var dv = v - 3f;
            var startX = topLeft.X + du * exX + dv * eyX;
            var startY = topLeft.Y + du * exY + dv * eyY;

            for (var u = 0; u < dimension; u++)
            {
                var px = (int)(startX + u * exX + 0.5f);
                var py = (int)(startY + u * exY + 0.5f);

                // Clamp: mild inaccuracy at the outermost modules must not read OOB
                if (px < 0)
                    px = 0;
                else if (px >= width)
                    px = width - 1;
                if (py < 0)
                    py = 0;
                else if (py >= height)
                    py = height - 1;

                modules[rowBase + u] = luminance[py * width + px] < threshold ? (byte)1 : (byte)0;
            }
        }
    }

    private static void TransposeInPlace(Span<byte> modules, int dimension)
    {
        for (var y = 0; y < dimension; y++)
        {
            for (var x = y + 1; x < dimension; x++)
            {
                var a = y * dimension + x;
                var b = x * dimension + y;
                (modules[a], modules[b]) = (modules[b], modules[a]);
            }
        }
    }

    private static float DistanceSquared(in FinderPattern a, in FinderPattern b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private static float Distance(in FinderPattern a, in FinderPattern b)
        => (float)Math.Sqrt(DistanceSquared(a, b));
}
