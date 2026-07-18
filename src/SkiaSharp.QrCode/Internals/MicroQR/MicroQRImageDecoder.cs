using System.Buffers;
using SkiaSharp.QrCode.Internals.ImageDecoders;
using SkiaSharp.QrCode.Internals.StandardQr;

namespace SkiaSharp.QrCode.Internals.MicroQR;

/// <summary>
/// Decodes a Micro QR code from a grayscale image: clean, well-lit,
/// screen-rendered or scanned inputs.
/// </summary>
/// <remarks>
/// Pipeline:
/// <code>
/// 1. Global binarization threshold (Otsu, shared with Standard QR)
/// 2. Finder pattern candidates (shared 1:1:3:1:1 scan; ALL candidates, not best three)
/// 3. Fast axis-aligned grid sampling anchored on the single finder
/// 4. Failure-path angular finder-axis recovery, local center/scale refinement and
///    a bounded projective search using the shared Standard QR sampler
/// 5. Every version size (M4..M1) × orientation × transpose (mirror) is tried
/// 6. Matrix decoding arbitrates: format info is cross-checked against the matrix
///    size and RS + the ISO Table 9 capacity cap reject wrong-grid samples
/// </code>
/// A Micro QR symbol has a single finder pattern, so orientation cannot be derived
/// from finder geometry the way three finders allow for Standard QR. The detector
/// therefore recovers the finder's local axes from angular dark-light-dark runs and
/// searches the two projective coefficients that remain unknown. This supports
/// arbitrary rotation and mild perspective; strong perspective remains out of scope.
/// </remarks>
internal static class MicroQRImageDecoder
{
    /// <summary>Candidates actually tried, most-confirmed first (false hits rank behind).</summary>
    private const int MaxCandidatesToTry = 8;

    /// <summary>Best local finder-axis estimates retained from the angular sweep.</summary>
    private const int MaxOrientationCandidates = 16;

    /// <summary>
    /// Maximum matrix decode attempts in the arbitrary-orientation failure path
    /// for one finder candidate. This bounds the multiplicative frame, orientation,
    /// size, scale and perspective searches while leaving enough for one complete
    /// orientation frame (all four axis assignments and four symbol sizes), and
    /// without making the result CPU-speed dependent.
    /// </summary>
    private const int MaxArbitraryOrientationDecodeAttempts = 10_000;

    /// <summary>
    /// Maximum angular-sweep ray length relative to the row-scan module estimate.
    /// A finder center-to-edge ray is at most 3.5√2 modules; the extra margin
    /// tolerates pixel quantization and mild perspective.
    /// </summary>
    private const float MaxAngularSweepRunModules = 8f;

    /// <summary>
    /// Decodes a Micro QR code from grayscale pixels. Reflectance-reversed symbols
    /// (light modules on a dark background) are handled by one inverted retry when
    /// the normal attempt fails.
    /// </summary>
    public static QRCodeDecodeStatus DecodeLuminance(ReadOnlySpan<byte> luminance, int width, int height, Span<char> destination, out int charsWritten, out MicroQRCodeDecodeInfo info)
    {
        if (!ImageDimensions.TryGetPixelCount(width, height, out var pixelCount) || luminance.Length < pixelCount)
        {
            charsWritten = 0;
            info = new MicroQRCodeDecodeInfo(QRCodeDecodeStatus.NotDetected, 0, default, -1, 0);
            return QRCodeDecodeStatus.NotDetected;
        }

        luminance = luminance.Slice(0, pixelCount);
        var status = DecodeLuminanceCore(luminance, width, height, destination, out charsWritten, out info);
        if (status == QRCodeDecodeStatus.Success)
            return status;

        // Reflectance reversal: invert into a rented buffer and retry once.
        // Taken only on the failure path, so the normal case stays allocation-free.
        var rented = ArrayPool<byte>.Shared.Rent(pixelCount);
        try
        {
            var inverted = rented.AsSpan(0, pixelCount);
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

    private static QRCodeDecodeStatus DecodeLuminanceCore(ReadOnlySpan<byte> luminance, int width, int height, Span<char> destination, out int charsWritten, out MicroQRCodeDecodeInfo info)
    {
        charsWritten = 0;

        var threshold = Binarizer.ComputeOtsuThreshold(luminance);

        Span<FinderPattern> candidates = stackalloc FinderPattern[FinderPatternFinder.MaxFinderCandidates];
        var candidateCount = FinderPatternFinder.FindCandidates(luminance, width, height, threshold, candidates);
        if (candidateCount == 0)
        {
            info = new MicroQRCodeDecodeInfo(QRCodeDecodeStatus.NotDetected, 0, default, -1, 0);
            return QRCodeDecodeStatus.NotDetected;
        }

        // Most-confirmed candidates first: repeated row hits separate real finder
        // patterns from data-area false positives.
        // Insertion sort: netstandard2.0 has no Span.Sort, and the list is tiny (≤ 32).
        for (var i = 1; i < candidateCount; i++)
        {
            var current = candidates[i];
            var j = i - 1;
            while (j >= 0 && candidates[j].Count < current.Count)
            {
                candidates[j + 1] = candidates[j];
                j--;
            }
            candidates[j + 1] = current;
        }

        // The furthest-progressing failure is the most useful diagnostic: an attempt
        // that passed format decoding but failed RS says more than "not detected".
        var bestStatus = QRCodeDecodeStatus.NotDetected;
        var bestInfo = new MicroQRCodeDecodeInfo(QRCodeDecodeStatus.NotDetected, 0, default, -1, 0);

        Span<byte> modules = stackalloc byte[17 * 17];

        var tried = Math.Min(candidateCount, MaxCandidatesToTry);
        for (var c = 0; c < tried; c++)
        {
            ref readonly var candidate = ref candidates[c];
            RefineModuleSize(luminance, width, height, threshold, candidate, out var horizontalModuleSize, out var verticalModuleSize);
            if (horizontalModuleSize < 1f || verticalModuleSize < 1f)
                continue; // below one pixel per module nothing can be sampled reliably
            var samplingSlack = Math.Max(horizontalModuleSize, verticalModuleSize);

            // Right-angle orientations as grid axis pairs (u = grid column axis,
            // v = grid row axis, in pixels per module).
            for (var orientation = 0; orientation < 4; orientation++)
            {
                var (uX, uY, vX, vY) = orientation switch
                {
                    0 => (horizontalModuleSize, 0f, 0f, verticalModuleSize),   // finder at symbol top-left
                    1 => (0f, verticalModuleSize, -horizontalModuleSize, 0f),  // rotated 90° clockwise
                    2 => (-horizontalModuleSize, 0f, 0f, -verticalModuleSize), // rotated 180°
                    _ => (0f, -verticalModuleSize, horizontalModuleSize, 0f),  // rotated 270°
                };

                // Grid origin: the finder center sits at grid (3.5, 3.5)
                var originX = candidate.X - 3.5f * (uX + vX);
                var originY = candidate.Y - 3.5f * (uY + vY);

                // Larger sizes first: a real M4 sampled as M2 reads a garbled
                // sub-grid, while trying real sizes first exits at the first success.
                for (var size = 17; size >= 11; size -= 2)
                {
                    if (!SymbolFitsImage(originX, originY, uX, uY, vX, vY, size, width, height, samplingSlack))
                        continue;

                    SampleGrid(luminance, width, height, threshold, originX, originY, uX, uY, vX, vY, size, modules);

                    var status = MicroQRMatrixDecoder.DecodeMatrix(modules.Slice(0, size * size), size, destination, out charsWritten, out var attemptInfo);
                    if (status == QRCodeDecodeStatus.Success)
                    {
                        info = attemptInfo;
                        return status;
                    }
                    TrackBestFailure(status, attemptInfo, ref bestStatus, ref bestInfo);

                    // Mirrored capture (e.g. front camera): finder geometry is
                    // identical but the data grid is transposed.
                    TransposeInPlace(modules, size);
                    var mirroredStatus = MicroQRMatrixDecoder.DecodeMatrix(modules.Slice(0, size * size), size, destination, out charsWritten, out var mirroredInfo);
                    if (mirroredStatus == QRCodeDecodeStatus.Success)
                    {
                        info = mirroredInfo;
                        return mirroredStatus;
                    }
                    TrackBestFailure(mirroredStatus, mirroredInfo, ref bestStatus, ref bestInfo);
                }
            }

            // The fast path above covers the overwhelmingly common axis-aligned
            // case. On failure, recover the finder square's local axes by sweeping
            // directions through 90 degrees, then sample along those rotated axes.
            // A square finder repeats every 90 degrees; the four sign/axis
            // assignments below recover the symbol orientation.
            var rotatedStatus = TryDecodeArbitraryOrientation(
                luminance,
                width,
                height,
                threshold,
                candidate,
                modules,
                destination,
                out charsWritten,
                out var rotatedInfo,
                ref bestStatus,
                ref bestInfo);
            if (rotatedStatus == QRCodeDecodeStatus.Success)
            {
                info = rotatedInfo;
                return rotatedStatus;
            }
        }

        charsWritten = 0;
        info = bestInfo;
        return bestStatus;
    }

    /// <summary>
    /// Recovers arbitrary image rotation from the single finder pattern. For a
    /// concentric square finder, a center ray crosses the shortest
    /// dark-light-dark span when it follows one of the square's local axes. An
    /// angular sweep therefore supplies the two grid-axis directions without the
    /// three finder centers available to Standard QR.
    /// </summary>
    private static QRCodeDecodeStatus TryDecodeArbitraryOrientation(
        ReadOnlySpan<byte> luminance,
        int width,
        int height,
        byte threshold,
        in FinderPattern candidate,
        Span<byte> modules,
        Span<char> destination,
        out int charsWritten,
        out MicroQRCodeDecodeInfo info,
        ref QRCodeDecodeStatus bestStatus,
        ref MicroQRCodeDecodeInfo bestInfo)
    {
        Span<OrientationCandidate> orientations = stackalloc OrientationCandidate[MaxOrientationCandidates];
        var orientationCount = FindOrientationCandidates(luminance, width, height, threshold, candidate, orientations);
        var attemptsRemaining = MaxArbitraryOrientationDecodeAttempts;

        for (var frameIndex = 0; frameIndex < orientationCount; frameIndex++)
        {
            ref readonly var frame = ref orientations[frameIndex];
            for (var orientation = 0; orientation < 4; orientation++)
            {
                var (uX, uY, vX, vY) = orientation switch
                {
                    0 => (frame.UX, frame.UY, frame.VX, frame.VY),
                    1 => (frame.VX, frame.VY, -frame.UX, -frame.UY),
                    2 => (-frame.UX, -frame.UY, -frame.VX, -frame.VY),
                    _ => (-frame.VX, -frame.VY, frame.UX, frame.UY),
                };

                var originX = candidate.X - 3.5f * (uX + vX);
                var originY = candidate.Y - 3.5f * (uY + vY);
                var samplingSlack = Math.Max(frame.USize, frame.VSize);

                for (var size = 17; size >= 11; size -= 2)
                {
                    if (attemptsRemaining == 0)
                    {
                        charsWritten = 0;
                        info = bestInfo;
                        return bestStatus;
                    }

                    if (!SymbolFitsImage(originX, originY, uX, uY, vX, vY, size, width, height, samplingSlack))
                        continue;

                    SampleGrid(luminance, width, height, threshold, originX, originY, uX, uY, vX, vY, size, modules);
                    attemptsRemaining--;
                    var status = MicroQRMatrixDecoder.DecodeMatrix(modules.Slice(0, size * size), size, destination, out charsWritten, out var attemptInfo);
                    if (status == QRCodeDecodeStatus.Success)
                    {
                        info = attemptInfo;
                        return status;
                    }
                    TrackBestFailure(status, attemptInfo, ref bestStatus, ref bestInfo);

                    if (attemptsRemaining == 0)
                        continue;

                    TransposeInPlace(modules, size);
                    attemptsRemaining--;
                    var mirroredStatus = MicroQRMatrixDecoder.DecodeMatrix(modules.Slice(0, size * size), size, destination, out charsWritten, out var mirroredInfo);
                    if (mirroredStatus == QRCodeDecodeStatus.Success)
                    {
                        info = mirroredInfo;
                        return mirroredStatus;
                    }
                    TrackBestFailure(mirroredStatus, mirroredInfo, ref bestStatus, ref bestInfo);

                    // Scale and perspective searches multiply this affine attempt
                    // by hundreds. Enter them only after either polarity decoded
                    // valid format information; wrong grids overwhelmingly fail
                    // before that point.
                    if (!IsPlausibleRefinement(status) && !IsPlausibleRefinement(mirroredStatus))
                        continue;

                    var scaledStatus = TryDecodeScaleVariants(
                        luminance, width, height, threshold, candidate,
                        uX, uY, vX, vY, size, modules, destination,
                        out charsWritten, out var scaledInfo,
                        ref bestStatus, ref bestInfo, ref attemptsRemaining);
                    if (scaledStatus == QRCodeDecodeStatus.Success)
                    {
                        info = scaledInfo;
                        return scaledStatus;
                    }

                    var projectiveStatus = TryDecodePerspectiveVariants(
                        luminance,
                        width,
                        height,
                        threshold,
                        candidate,
                        uX,
                        uY,
                        vX,
                        vY,
                        size,
                        samplingSlack,
                        modules,
                        destination,
                        out charsWritten,
                        out var projectiveInfo,
                        ref bestStatus,
                        ref bestInfo,
                        ref attemptsRemaining);
                    if (projectiveStatus == QRCodeDecodeStatus.Success)
                    {
                        info = projectiveInfo;
                        return projectiveStatus;
                    }
                }
            }
        }

        charsWritten = 0;
        info = bestInfo;
        return bestStatus;
    }

    /// <summary>
    /// Refines the two local module scales independently around the pixel-quantized
    /// finder-run estimate while keeping the finder center fixed.
    /// </summary>
    private static QRCodeDecodeStatus TryDecodeScaleVariants(
        ReadOnlySpan<byte> luminance,
        int width,
        int height,
        byte threshold,
        in FinderPattern candidate,
        float uX,
        float uY,
        float vX,
        float vY,
        int size,
        Span<byte> modules,
        Span<char> destination,
        out int charsWritten,
        out MicroQRCodeDecodeInfo info,
        ref QRCodeDecodeStatus bestStatus,
        ref MicroQRCodeDecodeInfo bestInfo,
        ref int attemptsRemaining)
    {
        ReadOnlySpan<float> centerOffsets = stackalloc float[] { 0f, -0.5f, 0.5f };
        ReadOnlySpan<float> factors = stackalloc float[] { 0.94f, 0.97f, 1f, 1.03f, 1.06f };
        var uSize = (float)Math.Sqrt(uX * uX + uY * uY);
        var vSize = (float)Math.Sqrt(vX * vX + vY * vY);
        foreach (var centerYOffset in centerOffsets)
        {
            foreach (var centerXOffset in centerOffsets)
            {
                foreach (var vFactor in factors)
                {
                    foreach (var uFactor in factors)
                    {
                        if (attemptsRemaining == 0)
                        {
                            charsWritten = 0;
                            info = bestInfo;
                            return bestStatus;
                        }

                        if (centerXOffset == 0f && centerYOffset == 0f && uFactor == 1f && vFactor == 1f)
                            continue;

                        var scaledUX = uX * uFactor;
                        var scaledUY = uY * uFactor;
                        var scaledVX = vX * vFactor;
                        var scaledVY = vY * vFactor;
                        var centerX = candidate.X + centerXOffset;
                        var centerY = candidate.Y + centerYOffset;
                        var originX = centerX - 3.5f * (scaledUX + scaledVX);
                        var originY = centerY - 3.5f * (scaledUY + scaledVY);
                        var samplingSlack = Math.Max(uSize * uFactor, vSize * vFactor);
                        if (!SymbolFitsImage(originX, originY, scaledUX, scaledUY, scaledVX, scaledVY, size, width, height, samplingSlack))
                            continue;

                        SampleGrid(luminance, width, height, threshold, originX, originY, scaledUX, scaledUY, scaledVX, scaledVY, size, modules);
                        attemptsRemaining--;
                        var status = MicroQRMatrixDecoder.DecodeMatrix(modules.Slice(0, size * size), size, destination, out charsWritten, out var attemptInfo);
                        if (status == QRCodeDecodeStatus.Success)
                        {
                            info = attemptInfo;
                            return status;
                        }
                        TrackBestFailure(status, attemptInfo, ref bestStatus, ref bestInfo);

                        if (attemptsRemaining == 0)
                            continue;

                        TransposeInPlace(modules, size);
                        attemptsRemaining--;
                        var mirroredStatus = MicroQRMatrixDecoder.DecodeMatrix(modules.Slice(0, size * size), size, destination, out charsWritten, out var mirroredInfo);
                        if (mirroredStatus == QRCodeDecodeStatus.Success)
                        {
                            info = mirroredInfo;
                            return mirroredStatus;
                        }
                        TrackBestFailure(mirroredStatus, mirroredInfo, ref bestStatus, ref bestInfo);
                    }
                }
            }
        }

        charsWritten = 0;
        info = bestInfo;
        return bestStatus;
    }

    /// <summary>
    /// A single finder determines a homography's image point and local Jacobian,
    /// leaving only the two projective denominator coefficients unknown. Search a
    /// bounded Tier-2 range for those two values; matrix format and RS validation
    /// select the correct transform without image-specific heuristics.
    /// </summary>
    private static QRCodeDecodeStatus TryDecodePerspectiveVariants(
        ReadOnlySpan<byte> luminance,
        int width,
        int height,
        byte threshold,
        in FinderPattern candidate,
        float uX,
        float uY,
        float vX,
        float vY,
        int size,
        float samplingSlack,
        Span<byte> modules,
        Span<char> destination,
        out int charsWritten,
        out MicroQRCodeDecodeInfo info,
        ref QRCodeDecodeStatus bestStatus,
        ref MicroQRCodeDecodeInfo bestInfo,
        ref int attemptsRemaining)
    {
        ReadOnlySpan<float> strengths = stackalloc float[] { -0.12f, -0.08f, -0.04f, -0.02f, 0f, 0.02f, 0.04f, 0.08f, 0.12f };
        for (var pyIndex = 0; pyIndex < strengths.Length; pyIndex++)
        {
            var perspectiveY = strengths[pyIndex] / size;
            for (var pxIndex = 0; pxIndex < strengths.Length; pxIndex++)
            {
                if (attemptsRemaining == 0)
                {
                    charsWritten = 0;
                    info = bestInfo;
                    return bestStatus;
                }

                var perspectiveX = strengths[pxIndex] / size;
                if (perspectiveX == 0f && perspectiveY == 0f)
                    continue; // the affine transform was already tried

                var transform = PerspectiveTransform.FromLocalFrame(
                    3.5f,
                    3.5f,
                    candidate.X,
                    candidate.Y,
                    uX,
                    uY,
                    vX,
                    vY,
                    perspectiveX,
                    perspectiveY);
                if (!ProjectiveSymbolFitsImage(transform, size, width, height, samplingSlack))
                    continue;

                QRImageDecoder.SampleGrid(luminance, width, height, threshold, transform, size, modules);
                attemptsRemaining--;
                var status = MicroQRMatrixDecoder.DecodeMatrix(modules.Slice(0, size * size), size, destination, out charsWritten, out var attemptInfo);
                if (status == QRCodeDecodeStatus.Success)
                {
                    info = attemptInfo;
                    return status;
                }
                TrackBestFailure(status, attemptInfo, ref bestStatus, ref bestInfo);

                if (attemptsRemaining == 0)
                    continue;

                TransposeInPlace(modules, size);
                attemptsRemaining--;
                var mirroredStatus = MicroQRMatrixDecoder.DecodeMatrix(modules.Slice(0, size * size), size, destination, out charsWritten, out var mirroredInfo);
                if (mirroredStatus == QRCodeDecodeStatus.Success)
                {
                    info = mirroredInfo;
                    return mirroredStatus;
                }
                TrackBestFailure(mirroredStatus, mirroredInfo, ref bestStatus, ref bestInfo);
            }
        }

        charsWritten = 0;
        info = bestInfo;
        return bestStatus;
    }

    /// <summary>
    /// Sweeps one quadrant because finder axes repeat every 90 degrees, retaining
    /// separated low-score directions. Pixel quantization can shift the shortest
    /// measured run several degrees away from the true finder axis, so adjacent
    /// samples of one minimum must not consume every candidate slot.
    /// </summary>
    private static int FindOrientationCandidates(
        ReadOnlySpan<byte> luminance,
        int width,
        int height,
        byte threshold,
        in FinderPattern candidate,
        Span<OrientationCandidate> destination)
    {
        Span<float> uSizes = stackalloc float[90];
        Span<float> vSizes = stackalloc float[90];
        var maxRunLength = candidate.ModuleSize * MaxAngularSweepRunModules;
        for (var degrees = 0; degrees < 90; degrees++)
        {
            var radians = degrees * (Math.PI / 180d);
            var cos = (float)Math.Cos(radians);
            var sin = (float)Math.Sin(radians);
            var uSize = MeasureAxis(luminance, width, height, threshold, candidate.X, candidate.Y, cos, sin, maxRunLength);
            var vSize = MeasureAxis(luminance, width, height, threshold, candidate.X, candidate.Y, -sin, cos, maxRunLength);
            uSizes[degrees] = uSize;
            vSizes[degrees] = vSize;
        }

        // The pixel-grid minimum can be a few degrees away from the true finder
        // axis, particularly for small rotated symbols. Select several separated
        // minima rather than filling the result with adjacent samples of one dip.
        Span<int> selectedDegrees = stackalloc int[MaxOrientationCandidates];
        var count = 0;
        while (count < destination.Length)
        {
            var bestDegree = -1;
            var bestScore = float.MaxValue;
            for (var degrees = 0; degrees < 90; degrees++)
            {
                var uSize = uSizes[degrees];
                var vSize = vSizes[degrees];
                if (float.IsNaN(uSize) || float.IsNaN(vSize) || uSize < 1f || vSize < 1f)
                    continue;

                var separated = true;
                for (var i = 0; i < count; i++)
                {
                    var distance = Math.Abs(degrees - selectedDegrees[i]);
                    if (Math.Min(distance, 90 - distance) < 2)
                    {
                        separated = false;
                        break;
                    }
                }
                if (!separated || uSize + vSize >= bestScore)
                    continue;

                bestDegree = degrees;
                bestScore = uSize + vSize;
            }

            if (bestDegree < 0)
                break;

            selectedDegrees[count] = bestDegree;
            var radians = bestDegree * (Math.PI / 180d);
            var cos = (float)Math.Cos(radians);
            var sin = (float)Math.Sin(radians);
            var bestUSize = uSizes[bestDegree];
            var bestVSize = vSizes[bestDegree];
            destination[count++] = new OrientationCandidate(
                cos * bestUSize,
                sin * bestUSize,
                -sin * bestVSize,
                cos * bestVSize,
                bestUSize,
                bestVSize);
        }

        return count;
    }

    private readonly struct OrientationCandidate(
        float uX,
        float uY,
        float vX,
        float vY,
        float uSize,
        float vSize)
    {
        public float UX { get; } = uX;
        public float UY { get; } = uY;
        public float VX { get; } = vX;
        public float VY { get; } = vY;
        public float USize { get; } = uSize;
        public float VSize { get; } = vSize;
    }

    /// <summary>
    /// Ranks decode failures by how far the attempt progressed; keeps the deepest.
    /// Wrong-grid samples overwhelmingly die at format decoding, so anything past
    /// it almost certainly hit the real grid.
    /// </summary>
    private static void TrackBestFailure(QRCodeDecodeStatus status, in MicroQRCodeDecodeInfo attemptInfo, ref QRCodeDecodeStatus bestStatus, ref MicroQRCodeDecodeInfo bestInfo)
    {
        if (Rank(status) > Rank(bestStatus))
        {
            bestStatus = status;
            bestInfo = attemptInfo;
        }

        static int Rank(QRCodeDecodeStatus s) => s switch
        {
            QRCodeDecodeStatus.NotDetected => 0,
            QRCodeDecodeStatus.InvalidMatrix => 1,
            QRCodeDecodeStatus.FormatInformationInvalid => 1,
            _ => 2, // got past format decoding
        };
    }

    private static bool IsPlausibleRefinement(QRCodeDecodeStatus status)
        => status is not QRCodeDecodeStatus.NotDetected
            and not QRCodeDecodeStatus.InvalidMatrix
            and not QRCodeDecodeStatus.FormatInformationInvalid;

    /// <summary>
    /// All four grid corners must land inside the image (with one module of slack
    /// for sampling clamp tolerance); orientations pointing off the image cannot
    /// contain the symbol and are skipped before sampling.
    /// </summary>
    private static bool SymbolFitsImage(float originX, float originY, float uX, float uY, float vX, float vY, int size, int width, int height, float moduleSize)
    {
        var slack = moduleSize;
        for (var corner = 0; corner < 4; corner++)
        {
            var gu = (corner & 1) == 0 ? 0f : size;
            var gv = (corner & 2) == 0 ? 0f : size;
            var x = originX + gu * uX + gv * vX;
            var y = originY + gu * uY + gv * vY;
            if (x < -slack || x > width + slack || y < -slack || y > height + slack)
                return false;
        }
        return true;
    }

    private static bool ProjectiveSymbolFitsImage(in PerspectiveTransform transform, int size, int width, int height, float samplingSlack)
    {
        for (var corner = 0; corner < 4; corner++)
        {
            var gridX = (corner & 1) == 0 ? 0f : size;
            var gridY = (corner & 2) == 0 ? 0f : size;
            transform.Transform(gridX, gridY, out var x, out var y);
            if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(y) || float.IsInfinity(y)
                || x < -samplingSlack || x > width + samplingSlack
                || y < -samplingSlack || y > height + samplingSlack)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Refines the horizontal and vertical module sizes independently by walking
    /// dark-light-dark runs from the finder center: center square (3) + light
    /// ring (1) + dark ring (1) on each side spans exactly 7 modules of the
    /// 1:1:3:1:1 structure. Keeping both estimates lets the decoder read symbols
    /// rendered into a non-square rectangle. A clipped axis falls back to the
    /// other axis, then to the row-scan estimate when both axes clip.
    /// </summary>
    private static void RefineModuleSize(
        ReadOnlySpan<byte> luminance,
        int width,
        int height,
        byte threshold,
        in FinderPattern candidate,
        out float horizontalModuleSize,
        out float verticalModuleSize)
    {
        horizontalModuleSize = MeasureAxis(luminance, width, height, threshold, candidate.X, candidate.Y, 1f, 0f);
        verticalModuleSize = MeasureAxis(luminance, width, height, threshold, candidate.X, candidate.Y, 0f, 1f);

        if (float.IsNaN(horizontalModuleSize))
            horizontalModuleSize = float.IsNaN(verticalModuleSize) ? candidate.ModuleSize : verticalModuleSize;
        if (float.IsNaN(verticalModuleSize))
            verticalModuleSize = horizontalModuleSize;
    }

    private static float MeasureAxis(
        ReadOnlySpan<byte> luminance,
        int width,
        int height,
        byte threshold,
        float centerX,
        float centerY,
        float dirX,
        float dirY,
        float maxRunLength = float.PositiveInfinity)
    {
        var forward = DarkLightDarkRun(luminance, width, height, threshold, centerX, centerY, dirX, dirY, maxRunLength);
        var backward = DarkLightDarkRun(luminance, width, height, threshold, centerX, centerY, -dirX, -dirY, maxRunLength);
        if (float.IsNaN(forward) || float.IsNaN(backward))
            return float.NaN;

        return (forward + backward) / 7f;
    }

    /// <summary>
    /// Walks from the finder center along a direction until the dark-light-dark
    /// sequence completes (center square → light ring → dark ring → out), returning
    /// the traveled distance (≈ 3.5 modules). NaN when the image edge or the
    /// caller's maximum run length interrupts the sequence.
    /// Returning step − 0.5 centers the one-pixel overshoot of the integer-step
    /// walk (same correction as the Standard QR measurement).
    /// </summary>
    private static float DarkLightDarkRun(ReadOnlySpan<byte> luminance, int width, int height, byte threshold, float startX, float startY, float dirX, float dirY, float maxRunLength)
    {
        var phase = 0;
        for (var step = 1f; step <= maxRunLength; step += 1f)
        {
            var x = (int)(startX + dirX * step + 0.5f);
            var y = (int)(startY + dirY * step + 0.5f);
            if (x < 0 || x >= width || y < 0 || y >= height)
            {
                // The outer dark ring may end exactly at the image edge (zero or
                // cropped quiet zone): the run is complete, not clipped.
                return phase == 2 ? step - 0.5f : float.NaN;
            }

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

        return float.NaN;
    }

    /// <summary>
    /// Samples every module center on the axis-aligned (per orientation) grid.
    /// Out-of-range positions clamp to the nearest edge pixel — mild inaccuracy at
    /// the outermost modules must not read out of bounds.
    /// </summary>
    private static void SampleGrid(ReadOnlySpan<byte> luminance, int width, int height, byte threshold, float originX, float originY, float uX, float uY, float vX, float vY, int size, Span<byte> modules)
    {
        for (var v = 0; v < size; v++)
        {
            var gridV = v + 0.5f;
            var rowX = originX + gridV * vX;
            var rowY = originY + gridV * vY;
            var rowBase = v * size;

            for (var u = 0; u < size; u++)
            {
                var gridU = u + 0.5f;
                var px = (int)(rowX + gridU * uX + 0.5f);
                var py = (int)(rowY + gridU * uY + 0.5f);

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

    private static void TransposeInPlace(Span<byte> modules, int size)
    {
        for (var y = 0; y < size; y++)
        {
            for (var x = y + 1; x < size; x++)
            {
                var a = y * size + x;
                var b = x * size + y;
                (modules[a], modules[b]) = (modules[b], modules[a]);
            }
        }
    }
}
