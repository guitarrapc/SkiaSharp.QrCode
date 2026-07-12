namespace SkiaSharp.QrCode.Internals.ImageDecoders;

/// <summary>
/// A detected finder pattern candidate (center in pixel coordinates).
/// </summary>
internal struct FinderPattern
{
    public float X;
    public float Y;
    public float ModuleSize;
    public int Count;
}

/// <summary>
/// Locates the three 7×7 finder patterns in a binarized luminance image.
/// </summary>
/// <remarks>
/// Scans rows for the characteristic 1:1:3:1:1 dark/light run ratio, then
/// cross-checks each hit vertically, horizontally and diagonally before accepting
/// it as a candidate (the standard ZXing-style detection approach). Designed for
/// Tier-1 inputs — clean, well-lit, screen-rendered or scanned images with mild
/// rotation — not for low-contrast photos.
/// </remarks>
internal static class FinderPatternFinder
{
    private const int MaxCandidates = 32;

    /// <summary>
    /// Searches the image for finder patterns and returns the best three.
    /// </summary>
    /// <param name="luminance">Grayscale pixels, row-major, width × height bytes.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="threshold">Binarization threshold: a pixel is dark when luminance &lt; threshold.</param>
    /// <param name="patterns">Receives the three finder patterns (top-left first is NOT guaranteed).</param>
    /// <returns>True when at least three mutually consistent finder patterns were found.</returns>
    public static bool TryFind(ReadOnlySpan<byte> luminance, int width, int height, byte threshold, Span<FinderPattern> patterns)
    {
        Span<FinderPattern> candidates = stackalloc FinderPattern[MaxCandidates];
        var candidateCount = 0;

        Span<int> runs = stackalloc int[5];

        for (var y = 0; y < height; y++)
        {
            var row = luminance.Slice(y * width, width);
            runs.Clear();
            var runIndex = 0;
            var currentDark = false;

            for (var x = 0; x < width; x++)
            {
                var dark = row[x] < threshold;
                if (x == 0)
                {
                    currentDark = dark;
                    runs[0] = 1;
                    runIndex = 0;
                    // A pattern window must start with a dark run
                    if (!dark)
                        runIndex = -1;
                    continue;
                }

                if (dark == currentDark)
                {
                    if (runIndex >= 0)
                        runs[runIndex]++;
                    continue;
                }

                // Color flipped: advance the run window
                currentDark = dark;
                if (runIndex < 0)
                {
                    // Waiting for the first dark run
                    if (dark)
                    {
                        runIndex = 0;
                        runs[0] = 1;
                    }
                    continue;
                }

                if (runIndex < 4)
                {
                    runIndex++;
                    runs[runIndex] = 1;
                    continue;
                }

                // Window full (5 runs) and a new dark run begins: evaluate, then shift
                if (IsFinderRatio(runs))
                {
                    TryAddCandidate(luminance, width, height, threshold, runs, x, y, candidates, ref candidateCount);
                }

                // Shift out the oldest dark/light pair; the window still starts
                // with a dark run and the incoming light run continues at index 3.
                runs[0] = runs[2];
                runs[1] = runs[3];
                runs[2] = runs[4];
                runs[3] = 1;
                runs[4] = 0;
                runIndex = 3;
            }

            // End of row: evaluate a complete trailing window
            if (runIndex == 4 && IsFinderRatio(runs))
            {
                TryAddCandidate(luminance, width, height, threshold, runs, width, y, candidates, ref candidateCount);
            }
        }

        return TrySelectBestThree(candidates.Slice(0, candidateCount), patterns);
    }

    /// <summary>
    /// Checks the 1:1:3:1:1 ratio with 50% per-module tolerance.
    /// </summary>
    private static bool IsFinderRatio(ReadOnlySpan<int> runs)
    {
        var total = 0;
        for (var i = 0; i < 5; i++)
        {
            if (runs[i] == 0)
                return false;
            total += runs[i];
        }
        if (total < 7)
            return false;

        var moduleSize = total / 7f;
        var maxVariance = moduleSize / 2f;
        return Math.Abs(moduleSize - runs[0]) < maxVariance
            && Math.Abs(moduleSize - runs[1]) < maxVariance
            && Math.Abs(3f * moduleSize - runs[2]) < 3f * maxVariance
            && Math.Abs(moduleSize - runs[3]) < maxVariance
            && Math.Abs(moduleSize - runs[4]) < maxVariance;
    }

    /// <summary>
    /// Cross-checks a horizontal hit vertically, then horizontally again, then
    /// diagonally; merges the refined center into the candidate list.
    /// </summary>
    private static void TryAddCandidate(ReadOnlySpan<byte> luminance, int width, int height, byte threshold, ReadOnlySpan<int> runs, int endX, int y, Span<FinderPattern> candidates, ref int candidateCount)
    {
        var total = runs[0] + runs[1] + runs[2] + runs[3] + runs[4];
        var centerX = endX - runs[4] - runs[3] - runs[2] / 2f;

        var centerY = CrossCheck(luminance, width, height, threshold, (int)centerX, y, vertical: true, total, out _);
        if (float.IsNaN(centerY))
            return;

        centerX = CrossCheck(luminance, width, height, threshold, (int)centerX, (int)centerY, vertical: false, total, out var refinedTotal);
        if (float.IsNaN(centerX))
            return;

        if (!CrossCheckDiagonal(luminance, width, height, threshold, (int)centerX, (int)centerY))
            return;

        var moduleSize = refinedTotal / 7f;

        // Merge with an existing candidate when centers and module sizes agree
        for (var i = 0; i < candidateCount; i++)
        {
            ref var existing = ref candidates[i];
            if (Math.Abs(existing.X - centerX) <= existing.ModuleSize
                && Math.Abs(existing.Y - centerY) <= existing.ModuleSize
                && Math.Abs(existing.ModuleSize - moduleSize) <= existing.ModuleSize / 2f)
            {
                var weight = existing.Count;
                existing.X = (existing.X * weight + centerX) / (weight + 1);
                existing.Y = (existing.Y * weight + centerY) / (weight + 1);
                existing.ModuleSize = (existing.ModuleSize * weight + moduleSize) / (weight + 1);
                existing.Count++;
                return;
            }
        }

        if (candidateCount < candidates.Length)
        {
            candidates[candidateCount++] = new FinderPattern { X = centerX, Y = centerY, ModuleSize = moduleSize, Count = 1 };
        }
    }

    /// <summary>
    /// Walks outwards from a supposed center along one axis and re-validates the
    /// 1:1:3:1:1 ratio. Returns the refined center coordinate on that axis, or NaN.
    /// </summary>
    private static float CrossCheck(ReadOnlySpan<byte> luminance, int width, int height, byte threshold, int centerX, int centerY, bool vertical, int expectedTotal, out int total)
    {
        total = 0;
        var limit = vertical ? height : width;
        var center = vertical ? centerY : centerX;

        Span<int> runs = stackalloc int[5];

        // Middle dark run: walk both directions from the center
        var i = center;
        while (i >= 0 && IsDark(luminance, width, vertical ? centerX : i, vertical ? i : centerY, threshold))
        {
            runs[2]++;
            i--;
        }
        if (i < 0)
            return float.NaN;

        // Light then dark run above/left
        while (i >= 0 && !IsDark(luminance, width, vertical ? centerX : i, vertical ? i : centerY, threshold) && runs[1] <= expectedTotal)
        {
            runs[1]++;
            i--;
        }
        while (i >= 0 && IsDark(luminance, width, vertical ? centerX : i, vertical ? i : centerY, threshold) && runs[0] <= expectedTotal)
        {
            runs[0]++;
            i--;
        }

        var start = i;

        i = center + 1;
        while (i < limit && IsDark(luminance, width, vertical ? centerX : i, vertical ? i : centerY, threshold))
        {
            runs[2]++;
            i++;
        }
        if (i >= limit)
            return float.NaN;

        while (i < limit && !IsDark(luminance, width, vertical ? centerX : i, vertical ? i : centerY, threshold) && runs[3] <= expectedTotal)
        {
            runs[3]++;
            i++;
        }
        while (i < limit && IsDark(luminance, width, vertical ? centerX : i, vertical ? i : centerY, threshold) && runs[4] <= expectedTotal)
        {
            runs[4]++;
            i++;
        }

        total = runs[0] + runs[1] + runs[2] + runs[3] + runs[4];

        // Reject when the cross section is wildly different from the row hit
        if (5 * Math.Abs(total - expectedTotal) >= 2 * expectedTotal)
            return float.NaN;

        if (!IsFinderRatio(runs))
            return float.NaN;

        return i - runs[4] - runs[3] - runs[2] / 2f;
    }

    /// <summary>
    /// Validates the 1:1:3:1:1 ratio along the top-left → bottom-right diagonal,
    /// killing false positives that pass both axis checks (e.g. dense data areas).
    /// </summary>
    private static bool CrossCheckDiagonal(ReadOnlySpan<byte> luminance, int width, int height, byte threshold, int centerX, int centerY)
    {
        Span<int> runs = stackalloc int[5];

        var i = 0;
        while (centerX - i >= 0 && centerY - i >= 0 && IsDark(luminance, width, centerX - i, centerY - i, threshold))
        {
            runs[2]++;
            i++;
        }
        if (centerX - i < 0 || centerY - i < 0)
            return false;

        while (centerX - i >= 0 && centerY - i >= 0 && !IsDark(luminance, width, centerX - i, centerY - i, threshold))
        {
            runs[1]++;
            i++;
        }
        while (centerX - i >= 0 && centerY - i >= 0 && IsDark(luminance, width, centerX - i, centerY - i, threshold))
        {
            runs[0]++;
            i++;
        }

        i = 1;
        while (centerX + i < width && centerY + i < height && IsDark(luminance, width, centerX + i, centerY + i, threshold))
        {
            runs[2]++;
            i++;
        }
        if (centerX + i >= width || centerY + i >= height)
            return false;

        while (centerX + i < width && centerY + i < height && !IsDark(luminance, width, centerX + i, centerY + i, threshold))
        {
            runs[3]++;
            i++;
        }
        while (centerX + i < width && centerY + i < height && IsDark(luminance, width, centerX + i, centerY + i, threshold))
        {
            runs[4]++;
            i++;
        }

        return IsFinderRatio(runs);
    }

    private static bool IsDark(ReadOnlySpan<byte> luminance, int width, int x, int y, byte threshold)
        => luminance[y * width + x] < threshold;

    /// <summary>
    /// Picks the three candidates with the most consistent module size,
    /// preferring repeatedly confirmed ones.
    /// </summary>
    private static bool TrySelectBestThree(Span<FinderPattern> candidates, Span<FinderPattern> patterns)
    {
        // Confirmed candidates (seen in multiple rows) are far more trustworthy
        var confirmed = 0;
        for (var i = 0; i < candidates.Length; i++)
        {
            if (candidates[i].Count >= 2)
                confirmed++;
        }

        // Compact to the confirmed subset when it is large enough to choose from
        if (confirmed >= 3 && confirmed < candidates.Length)
        {
            var w = 0;
            for (var i = 0; i < candidates.Length; i++)
            {
                if (candidates[i].Count >= 2)
                    candidates[w++] = candidates[i];
            }
            candidates = candidates.Slice(0, w);
        }

        if (candidates.Length < 3)
            return false;

        if (candidates.Length == 3)
        {
            candidates.CopyTo(patterns);
            return true;
        }

        // More than 3: sort by module size and take the most similar window of 3,
        // breaking ties toward higher confirmation counts.
        // Insertion sort: netstandard2.0 has no Span.Sort, and the list is tiny (≤ 32).
        for (var i = 1; i < candidates.Length; i++)
        {
            var current = candidates[i];
            var j = i - 1;
            while (j >= 0 && candidates[j].ModuleSize > current.ModuleSize)
            {
                candidates[j + 1] = candidates[j];
                j--;
            }
            candidates[j + 1] = current;
        }

        var bestIndex = 0;
        var bestScore = float.MaxValue;
        for (var i = 0; i + 3 <= candidates.Length; i++)
        {
            var spread = candidates[i + 2].ModuleSize - candidates[i].ModuleSize;
            var count = candidates[i].Count + candidates[i + 1].Count + candidates[i + 2].Count;
            var score = spread / count;
            if (score < bestScore)
            {
                bestScore = score;
                bestIndex = i;
            }
        }

        candidates.Slice(bestIndex, 3).CopyTo(patterns);
        return true;
    }
}
