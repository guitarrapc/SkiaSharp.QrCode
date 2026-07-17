using System.Buffers;
using SkiaSharp.QrCode.Internals.ImageDecoders;

namespace SkiaSharp.QrCode.Internals.MicroQr;

/// <summary>
/// Decodes a Micro QR code from a grayscale image: clean, well-lit,
/// screen-rendered or scanned inputs.
/// </summary>
/// <remarks>
/// Pipeline:
/// <code>
/// 1. Global binarization threshold (Otsu, shared with Standard QR)
/// 2. Finder pattern candidates (shared 1:1:3:1:1 scan; ALL candidates, not best three)
/// 3. Module size refinement through the finder center (dark-light-dark runs)
/// 4. Axis-aligned grid sampling anchored on the single finder, trying every
///    version size (M4..M1) × 4 right-angle orientations × transpose (mirror)
/// 5. Matrix decoding arbitrates: format info is cross-checked against the matrix
///    size and RS + the ISO Table 9 capacity cap reject wrong-grid samples
/// </code>
/// A Micro QR symbol has a single finder pattern, so orientation cannot be derived
/// from finder geometry the way three finders allow for Standard QR. The detector
/// therefore supports the axis-aligned envelope: 90°/180°/270° rotations,
/// mirroring, reflectance reversal, scaling and translation. Small-angle rotation
/// and perspective distortion are out of scope (documented in the spec map).
/// </remarks>
internal static class MicroQrImageDecoder
{
    /// <summary>Candidates actually tried, most-confirmed first (false hits rank behind).</summary>
    private const int MaxCandidatesToTry = 8;

    /// <summary>
    /// Decodes a Micro QR code from grayscale pixels. Reflectance-reversed symbols
    /// (light modules on a dark background) are handled by one inverted retry when
    /// the normal attempt fails.
    /// </summary>
    public static QRCodeDecodeStatus DecodeLuminance(ReadOnlySpan<byte> luminance, int width, int height, Span<char> destination, out int charsWritten, out MicroQrCodeDecodeInfo info)
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

    private static QRCodeDecodeStatus DecodeLuminanceCore(ReadOnlySpan<byte> luminance, int width, int height, Span<char> destination, out int charsWritten, out MicroQrCodeDecodeInfo info)
    {
        charsWritten = 0;

        var threshold = Binarizer.ComputeOtsuThreshold(luminance);

        Span<FinderPattern> candidates = stackalloc FinderPattern[FinderPatternFinder.MaxFinderCandidates];
        var candidateCount = FinderPatternFinder.FindCandidates(luminance, width, height, threshold, candidates);
        if (candidateCount == 0)
        {
            info = new MicroQrCodeDecodeInfo(QRCodeDecodeStatus.NotDetected, 0, default, -1, 0);
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
        var bestInfo = new MicroQrCodeDecodeInfo(QRCodeDecodeStatus.NotDetected, 0, default, -1, 0);

        Span<byte> modules = stackalloc byte[17 * 17];

        var tried = Math.Min(candidateCount, MaxCandidatesToTry);
        for (var c = 0; c < tried; c++)
        {
            ref readonly var candidate = ref candidates[c];
            var moduleSize = RefineModuleSize(luminance, width, height, threshold, candidate);
            if (moduleSize < 1f)
                continue; // below one pixel per module nothing can be sampled reliably

            // Right-angle orientations as grid axis pairs (u = grid column axis,
            // v = grid row axis, in pixels per module).
            for (var orientation = 0; orientation < 4; orientation++)
            {
                var (uX, uY, vX, vY) = orientation switch
                {
                    0 => (moduleSize, 0f, 0f, moduleSize),   // finder at symbol top-left
                    1 => (0f, moduleSize, -moduleSize, 0f),  // rotated 90° clockwise
                    2 => (-moduleSize, 0f, 0f, -moduleSize), // rotated 180°
                    _ => (0f, -moduleSize, moduleSize, 0f),  // rotated 270°
                };

                // Grid origin: the finder center sits at grid (3.5, 3.5)
                var originX = candidate.X - 3.5f * (uX + vX);
                var originY = candidate.Y - 3.5f * (uY + vY);

                // Larger sizes first: a real M4 sampled as M2 reads a garbled
                // sub-grid, while trying real sizes first exits at the first success.
                for (var size = 17; size >= 11; size -= 2)
                {
                    if (!SymbolFitsImage(originX, originY, uX, uY, vX, vY, size, width, height, moduleSize))
                        continue;

                    SampleGrid(luminance, width, height, threshold, originX, originY, uX, uY, vX, vY, size, modules);

                    var status = MicroQrMatrixDecoder.DecodeMatrix(modules.Slice(0, size * size), size, destination, out charsWritten, out var attemptInfo);
                    if (status == QRCodeDecodeStatus.Success)
                    {
                        info = attemptInfo;
                        return status;
                    }
                    TrackBestFailure(status, attemptInfo, ref bestStatus, ref bestInfo);

                    // Mirrored capture (e.g. front camera): finder geometry is
                    // identical but the data grid is transposed.
                    TransposeInPlace(modules, size);
                    var mirroredStatus = MicroQrMatrixDecoder.DecodeMatrix(modules.Slice(0, size * size), size, destination, out charsWritten, out var mirroredInfo);
                    if (mirroredStatus == QRCodeDecodeStatus.Success)
                    {
                        info = mirroredInfo;
                        return mirroredStatus;
                    }
                    TrackBestFailure(mirroredStatus, mirroredInfo, ref bestStatus, ref bestInfo);
                }
            }
        }

        charsWritten = 0;
        info = bestInfo;
        return bestStatus;
    }

    /// <summary>
    /// Ranks decode failures by how far the attempt progressed; keeps the deepest.
    /// Wrong-grid samples overwhelmingly die at format decoding, so anything past
    /// it almost certainly hit the real grid.
    /// </summary>
    private static void TrackBestFailure(QRCodeDecodeStatus status, in MicroQrCodeDecodeInfo attemptInfo, ref QRCodeDecodeStatus bestStatus, ref MicroQrCodeDecodeInfo bestInfo)
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

    /// <summary>
    /// Refines the module size by walking dark-light-dark runs from the finder
    /// center along both image axes: center square (3) + light ring (1) + dark
    /// ring (1) on each side spans exactly 7 modules of the 1:1:3:1:1 structure.
    /// Falls back to the row-scan estimate when both axes clip the image edge.
    /// </summary>
    private static float RefineModuleSize(ReadOnlySpan<byte> luminance, int width, int height, byte threshold, in FinderPattern candidate)
    {
        var sum = 0f;
        var count = 0;

        var horizontal = MeasureAxis(luminance, width, height, threshold, candidate.X, candidate.Y, 1f, 0f);
        if (!float.IsNaN(horizontal))
        {
            sum += horizontal;
            count++;
        }

        var vertical = MeasureAxis(luminance, width, height, threshold, candidate.X, candidate.Y, 0f, 1f);
        if (!float.IsNaN(vertical))
        {
            sum += vertical;
            count++;
        }

        return count > 0 ? sum / count : candidate.ModuleSize;
    }

    private static float MeasureAxis(ReadOnlySpan<byte> luminance, int width, int height, byte threshold, float centerX, float centerY, float dirX, float dirY)
    {
        var forward = DarkLightDarkRun(luminance, width, height, threshold, centerX, centerY, dirX, dirY);
        var backward = DarkLightDarkRun(luminance, width, height, threshold, centerX, centerY, -dirX, -dirY);
        if (float.IsNaN(forward) || float.IsNaN(backward))
            return float.NaN;

        return (forward + backward) / 7f;
    }

    /// <summary>
    /// Walks from the finder center along a direction until the dark-light-dark
    /// sequence completes (center square → light ring → dark ring → out), returning
    /// the traveled distance (≈ 3.5 modules). NaN when the image edge interrupts.
    /// Returning step − 0.5 centers the one-pixel overshoot of the integer-step
    /// walk (same correction as the Standard QR measurement).
    /// </summary>
    private static float DarkLightDarkRun(ReadOnlySpan<byte> luminance, int width, int height, byte threshold, float startX, float startY, float dirX, float dirY)
    {
        var phase = 0;
        for (var step = 1f; ; step += 1f)
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
