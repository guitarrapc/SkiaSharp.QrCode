using System.Buffers;
#if NET8_0_OR_GREATER
using System.Runtime.Intrinsics;
#endif

using SkiaSharp.QrCode.Internals.BinaryDecoders;

namespace SkiaSharp.QrCode.Internals.ImageDecoders;

/// <summary>
/// Decodes a QR code from a grayscale image: clean, well-lit, screen-rendered or
/// scanned inputs, including arbitrary rotation, mirroring, reflectance reversal
/// and mild perspective distortion (Tier 2).
/// </summary>
/// <remarks>
/// Pipeline:
/// <code>
/// 1. Global binarization threshold (Otsu's method over the luminance histogram)
/// 2. Finder pattern detection (1:1:3:1:1 scan + cross checks)
/// 3. Orientation from the three finder centers (rotation-invariant)
/// 4. Dimension estimate from center distances and module size
/// 5. Bottom-right alignment pattern search (version 2+; parallelogram estimate as fallback)
/// 6. Perspective grid sampling into a module matrix (4-point projective transform)
/// 7. Matrix decoding (format → unmask → deinterleave → Reed-Solomon → bitstream)
/// </code>
/// Out of scope (documented, by design): strong perspective where the four-point
/// transform no longer models the surface, uneven lighting (global threshold only),
/// blur, and multiple QR codes per image.
/// </remarks>
internal static class QRImageDecoder
{
    /// <summary>
    /// Decodes a QR code from grayscale pixels. Reflectance-reversed codes
    /// (light modules on a dark background, common in dark-mode UIs) are handled
    /// by one inverted retry when the normal attempt fails.
    /// </summary>
    /// <param name="luminance">Grayscale pixels, row-major, width × height bytes.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="destination">Destination buffer for decoded characters.</param>
    /// <param name="charsWritten">Number of characters written.</param>
    /// <param name="info">Diagnostic information.</param>
    public static QRCodeDecodeStatus DecodeLuminance(ReadOnlySpan<byte> luminance, int width, int height, Span<char> destination, out int charsWritten, out QRCodeDecodeInfo info)
    {
        var status = DecodeLuminanceCore(luminance, width, height, destination, out charsWritten, out info);
        if (status == QRCodeDecodeStatus.Success)
            return status;

        // Reflectance reversal: invert into a rented buffer and retry once.
        // Taken only on the failure path, so the normal case stays allocation-free.
        var rented = ArrayPool<byte>.Shared.Rent(width * height);
        try
        {
            var inverted = rented.AsSpan(0, width * height);
            for (var i = 0; i < inverted.Length; i++)
            {
                inverted[i] = (byte)(255 - luminance[i]);
            }

            var invertedStatus = DecodeLuminanceCore(inverted, width, height, destination, out charsWritten, out var invertedInfo);
            if (invertedStatus == QRCodeDecodeStatus.Success)
            {
                info = invertedInfo;
                return invertedStatus;
            }

            // Both polarities failed: report the original attempt's diagnostics
            return status;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented, clearArray: false);
        }
    }

    private static QRCodeDecodeStatus DecodeLuminanceCore(ReadOnlySpan<byte> luminance, int width, int height, Span<char> destination, out int charsWritten, out QRCodeDecodeInfo info)
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

        if (!TryEstimateDimension(luminance, width, height, threshold, topLeft, topRight, bottomLeft, out var dimension, out var secondaryDimension, out var moduleSize))
        {
            info = new QRCodeDecodeInfo(QRCodeDecodeStatus.NotDetected, 0, default, -1, 0);
            return QRCodeDecodeStatus.NotDetected;
        }

        var status = SampleAndDecode(luminance, width, height, threshold, topLeft, topRight, bottomLeft, dimension, moduleSize, destination, out charsWritten, out info);
        if (status == QRCodeDecodeStatus.Success)
            return status;

        // The dimension estimate can land between two valid sizes (module-size
        // measurement quantizes to pixels); when a plausible runner-up exists,
        // one retry with it rescues estimates that snapped to the wrong version.
        if (secondaryDimension != 0)
        {
            var secondaryStatus = SampleAndDecode(luminance, width, height, threshold, topLeft, topRight, bottomLeft, secondaryDimension, moduleSize, destination, out var secondaryCharsWritten, out var secondaryInfo);
            if (secondaryStatus == QRCodeDecodeStatus.Success)
            {
                charsWritten = secondaryCharsWritten;
                info = secondaryInfo;
                return secondaryStatus;
            }
        }

        // All candidates failed: report the primary attempt's diagnostics
        return status;
    }

    /// <summary>
    /// Samples the module grid at the given dimension and decodes it, retrying once
    /// transposed for mirrored images (e.g. front-camera captures): finder geometry
    /// is identical but data is transposed. The mirror retry triggers on any decode
    /// failure — a permuted format pattern may fall within BCH distance of a wrong
    /// candidate and surface as DataUncorrectable instead of FormatInformationInvalid.
    /// </summary>
    private static QRCodeDecodeStatus SampleAndDecode(ReadOnlySpan<byte> luminance, int width, int height, byte threshold, in FinderPattern topLeft, in FinderPattern topRight, in FinderPattern bottomLeft, int dimension, float moduleSize, Span<char> destination, out int charsWritten, out QRCodeDecodeInfo info)
    {
        var transform = BuildGridTransform(luminance, width, height, threshold, topLeft, topRight, bottomLeft, dimension, moduleSize);

        var rented = ArrayPool<byte>.Shared.Rent(dimension * dimension);
        try
        {
            var modules = rented.AsSpan(0, dimension * dimension);
            SampleGrid(luminance, width, height, threshold, transform, dimension, modules);

            var status = QRMatrixDecoder.DecodeMatrix(modules, dimension, destination, out charsWritten, out info);
            if (status == QRCodeDecodeStatus.Success)
                return status;

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
    internal static void OrderFinderPatterns(ReadOnlySpan<FinderPattern> patterns, out FinderPattern topLeft, out FinderPattern topRight, out FinderPattern bottomLeft)
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
    /// <param name="dimension">Nearest valid dimension to the estimate.</param>
    /// <param name="secondaryDimension">
    /// Second-nearest valid dimension when the estimate is also within one version
    /// step of it (retry candidate for estimates near a snap boundary), else 0.
    /// </param>
    /// <param name="moduleSize">Measured module size in pixels (for the alignment pattern search).</param>
    private static bool TryEstimateDimension(ReadOnlySpan<byte> luminance, int width, int height, byte threshold, in FinderPattern topLeft, in FinderPattern topRight, in FinderPattern bottomLeft, out int dimension, out int secondaryDimension, out float moduleSize)
    {
        dimension = 0;
        secondaryDimension = 0;

        moduleSize = MeasureModuleSize(luminance, width, height, threshold, topLeft, topRight, bottomLeft);
        if (moduleSize < 1f)
            return false; // below one pixel per module nothing can be sampled reliably

        // Finder centers sit 7 modules apart from the matrix edges
        var widthModules = Distance(topLeft, topRight) / moduleSize + 7f;
        var heightModules = Distance(topLeft, bottomLeft) / moduleSize + 7f;
        var estimate = (widthModules + heightModules) / 2f;

        // Snap to the nearest valid dimension; reject wild estimates
        var versionExact = (estimate - 17f) / 4f;
        var version = (int)Math.Round(versionExact);
        if (version < 1 || version > 40)
            return false;

        dimension = 17 + version * 4;
        if (Math.Abs(estimate - dimension) > 4f)
        {
            dimension = 0;
            return false;
        }

        // Runner-up on the other side of the estimate
        var secondaryVersion = versionExact > version ? version + 1 : version - 1;
        if (secondaryVersion >= 1 && secondaryVersion <= 40)
        {
            var candidate = 17 + secondaryVersion * 4;
            if (Math.Abs(estimate - candidate) <= 4f)
                secondaryDimension = candidate;
        }

        return true;
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
    /// <remarks>
    /// The walk samples at integer pixel steps, so the first light pixel after the
    /// dark ring overshoots the true boundary by up to one pixel. Returning
    /// step − 0.5 centers that error: without the correction the module size is
    /// systematically overestimated (~+0.07..+0.25 px measured), which at small
    /// pixels-per-module snaps the dimension estimate one whole version low
    /// (e.g. a 512 px version 14 render read as version 13).
    /// </remarks>
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
                        return step - 0.5f;
                    break;
            }
        }
    }

    /// <summary>
    /// Builds the grid-to-pixel projective transform from the three finder centers
    /// plus a fourth correspondence point: the bottom-right alignment pattern when
    /// one exists and is found, otherwise the parallelogram corner estimate (which
    /// degrades the transform to affine — exact for flat, on-axis captures).
    /// Grid coordinates put module (u, v)'s center at (u+0.5, v+0.5), so finder
    /// centers sit at 3.5 and the alignment center at dimension−6.5.
    /// </summary>
    internal static PerspectiveTransform BuildGridTransform(ReadOnlySpan<byte> luminance, int width, int height, byte threshold, in FinderPattern topLeft, in FinderPattern topRight, in FinderPattern bottomLeft, int dimension, float moduleSize)
    {
        // Parallelogram estimate of the bottom-right corner (grid dimension−3.5)
        var cornerX = topRight.X + bottomLeft.X - topLeft.X;
        var cornerY = topRight.Y + bottomLeft.Y - topLeft.Y;

        // Version 2+ has an alignment pattern centered 6.5 modules in from the
        // bottom-right corner; its predicted position pulls the corner estimate
        // toward the top-left by 3 modules on both axes.
        if (dimension >= 25)
        {
            var correction = 1f - 3f / (dimension - 7);
            var expectedX = topLeft.X + correction * (cornerX - topLeft.X);
            var expectedY = topLeft.Y + correction * (cornerY - topLeft.Y);

            // Per-module grid axis vectors, for orientation-aware ring validation
            var span = dimension - 7;
            var axisX = ((topRight.X - topLeft.X) / span, (topRight.Y - topLeft.Y) / span);
            var axisY = ((bottomLeft.X - topLeft.X) / span, (bottomLeft.Y - topLeft.Y) / span);

            // Expanding search window; mild perspective shifts the true position
            // further from the parallelogram prediction as the tilt grows.
            foreach (var allowance in stackalloc float[] { 4f, 8f, 16f })
            {
                if (AlignmentPatternFinder.TryFind(luminance, width, height, threshold, expectedX, expectedY, moduleSize, axisX, axisY, allowance, out var alignmentX, out var alignmentY))
                {
                    return PerspectiveTransform.QuadrilateralToQuadrilateral(
                        3.5f, 3.5f,
                        dimension - 3.5f, 3.5f,
                        dimension - 6.5f, dimension - 6.5f,
                        3.5f, dimension - 3.5f,
                        topLeft.X, topLeft.Y,
                        topRight.X, topRight.Y,
                        alignmentX, alignmentY,
                        bottomLeft.X, bottomLeft.Y);
                }
            }
        }

        // No alignment pattern (version 1) or not found: parallelogram corner
        return PerspectiveTransform.QuadrilateralToQuadrilateral(
            3.5f, 3.5f,
            dimension - 3.5f, 3.5f,
            dimension - 3.5f, dimension - 3.5f,
            3.5f, dimension - 3.5f,
            topLeft.X, topLeft.Y,
            topRight.X, topRight.Y,
            cornerX, cornerY,
            bottomLeft.X, bottomLeft.Y);
    }

    /// <summary>
    /// Samples every module center through the projective grid-to-pixel transform.
    /// Handles rotation, scale, shear and mild perspective.
    /// </summary>
    /// <remarks>
    /// The loop is bound by scalar conversion/clamp/branch overhead, not by the
    /// divisions (module computations are independent, so out-of-order execution
    /// hides division latency — halving the division count measured no gain).
    /// The SIMD path processes 8 module centers per iteration with the exact scalar
    /// op sequence (no FMA), so lane results are bit-identical to the scalar path
    /// (measured 2.7x at version 40; see the PerspectiveSample findings log).
    /// </remarks>
    internal static void SampleGrid(ReadOnlySpan<byte> luminance, int width, int height, byte threshold, in PerspectiveTransform transform, int dimension, Span<byte> modules)
    {
#if NET8_0_OR_GREATER
        if (Vector256.IsHardwareAccelerated && dimension >= 8)
        {
            SampleGridSimd(luminance, width, height, threshold, transform, dimension, modules);
            return;
        }
#endif
        SampleGridScalar(luminance, width, height, threshold, transform, dimension, modules);
    }

    internal static void SampleGridScalar(ReadOnlySpan<byte> luminance, int width, int height, byte threshold, in PerspectiveTransform transform, int dimension, Span<byte> modules)
    {
        for (var v = 0; v < dimension; v++)
        {
            var rowBase = v * dimension;
            var gridY = v + 0.5f;
            var rowNumeratorX = transform.a21 * gridY + transform.a31;
            var rowNumeratorY = transform.a22 * gridY + transform.a32;
            var rowDenominator = transform.a23 * gridY + transform.a33;

            for (var u = 0; u < dimension; u++)
            {
                var gridX = u + 0.5f;
                var reciprocal = 1f / (transform.a13 * gridX + rowDenominator);
                var x = (transform.a11 * gridX + rowNumeratorX) * reciprocal;
                var y = (transform.a12 * gridX + rowNumeratorY) * reciprocal;

                var px = (int)(x + 0.5f);
                var py = (int)(y + 0.5f);

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

#if NET8_0_OR_GREATER
    internal static void SampleGridSimd(ReadOnlySpan<byte> luminance, int width, int height, byte threshold, in PerspectiveTransform transform, int dimension, Span<byte> modules)
    {
        var laneOffsets = Vector256.Create(0.5f, 1.5f, 2.5f, 3.5f, 4.5f, 5.5f, 6.5f, 7.5f);
        var a11 = Vector256.Create(transform.a11);
        var a12 = Vector256.Create(transform.a12);
        var a13 = Vector256.Create(transform.a13);
        var half = Vector256.Create(0.5f);
        var zero = Vector256<int>.Zero;
        var maxPx = Vector256.Create(width - 1);
        var maxPy = Vector256.Create(height - 1);
        var widthVector = Vector256.Create(width);

        Span<int> indices = stackalloc int[8];

        for (var v = 0; v < dimension; v++)
        {
            var rowBase = v * dimension;
            var gridY = v + 0.5f;
            var rowNumeratorX = Vector256.Create(transform.a21 * gridY + transform.a31);
            var rowNumeratorY = Vector256.Create(transform.a22 * gridY + transform.a32);
            var rowDenominator = Vector256.Create(transform.a23 * gridY + transform.a33);

            var u = 0;
            for (; u + 8 <= dimension; u += 8)
            {
                var gridX = laneOffsets + Vector256.Create((float)u);
                var reciprocal = Vector256<float>.One / (a13 * gridX + rowDenominator);
                var x = (a11 * gridX + rowNumeratorX) * reciprocal;
                var y = (a12 * gridX + rowNumeratorY) * reciprocal;

                // (int)(x + 0.5f) truncates toward zero, matching the scalar cast for
                // every in-range value; out-of-range lanes differ from scalar
                // saturation but are clamped into bounds either way.
                var px = Vector256.ConvertToInt32(x + half);
                var py = Vector256.ConvertToInt32(y + half);
                px = Vector256.Max(Vector256.Min(px, maxPx), zero);
                py = Vector256.Max(Vector256.Min(py, maxPy), zero);

                var index = py * widthVector + px;
                index.CopyTo(indices);

                for (var lane = 0; lane < 8; lane++)
                {
                    modules[rowBase + u + lane] = luminance[indices[lane]] < threshold ? (byte)1 : (byte)0;
                }
            }

            // Scalar tail, same op sequence as SampleGridScalar
            var rowNX = transform.a21 * gridY + transform.a31;
            var rowNY = transform.a22 * gridY + transform.a32;
            var rowD = transform.a23 * gridY + transform.a33;
            for (; u < dimension; u++)
            {
                var gridXs = u + 0.5f;
                var reciprocal = 1f / (transform.a13 * gridXs + rowD);
                var x = (transform.a11 * gridXs + rowNX) * reciprocal;
                var y = (transform.a12 * gridXs + rowNY) * reciprocal;

                var px = (int)(x + 0.5f);
                var py = (int)(y + 0.5f);
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
#endif

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
