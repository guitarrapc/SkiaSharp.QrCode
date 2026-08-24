using System.Buffers;
using System.Diagnostics;
using System.Text;

namespace SkiaSharp.QrCode.Internals.RmQr;

/// <summary>
/// Mixed-mode segmentation for <see cref="RmQRSegmentation.Optimal"/>: the split of
/// the content into Numeric / Alphanumeric / Byte runs whose total bit cost is
/// minimal for a given version, and the version fit that follows from it.
/// </summary>
/// <remarks>
/// A run's cost is not a per-character constant (Numeric packs 3 digits into 10 bits,
/// Alphanumeric 2 characters into 11), so the dynamic program carries the group
/// remainder in its state rather than rounding an average. When the content fits in a
/// single mode that fit caps the search, so a plan is produced only when it lowers the
/// core module count; otherwise the single-mode stream is emitted unchanged. Design
/// rationale, bounds and measurements: specs/rmqr-encoder.md, "Mixed-mode segmentation".
/// </remarks>
internal static class RmQRSegmentPlanner
{
    /// <summary>
    /// Upper bound on the runs a plan can contain; the largest capacity cannot hold
    /// more than 76, and <c>RmQRSegmentPlannerUnitTest</c> pins that maximum below this.
    /// </summary>
    public const int MaxSegments = 96;

    /// <summary>
    /// Longest content any rMQR symbol can hold, in characters (361 digits at
    /// R17x139-M). No mixed plan can beat it: mixing only adds headers to a
    /// denser-per-character mode that does not exist.
    /// </summary>
    /// <remarks>
    /// A rejection rule, not only a work cap: a mixed plan can encode content no single
    /// mode holds, so this is what declares longer content impossible. The margin is
    /// 3 bits (362 digits cost 1219 against 1216), so re-derive it rather than nudge it
    /// if the capacity tables change. Pinned by <c>RmQRSegmentPlannerUnitTest</c>.
    /// </remarks>
    private const int MaxPlannableChars = 361;

    // Dynamic programming states. Numeric and Alphanumeric carry the number of
    // characters already accumulated into the current packing group.
    private const int StateNumeric0 = 0;
    private const int StateNumeric1 = 1;
    private const int StateNumeric2 = 2;
    private const int StateAlnum0 = 3;
    private const int StateAlnum1 = 4;
    private const int StateByte = 5;
    private const int StateStart = 6;
    private const int StateCount = 7;

    /// <summary>Cost of an unreachable state; small enough that adding a transition cost cannot overflow.</summary>
    private const int Unreachable = int.MaxValue / 4;

    /// <summary>Parent bytes that fit the stack budget (73 characters); longer content rents.</summary>
    private const int MaxStackParents = 512;

    /// <summary>
    /// Count indicator triples memoised during a version scan. The 32 versions share 13
    /// distinct triples, but the bounds narrow the band so far that measured sweeps
    /// never stored more than 3. Sized for that plus headroom: undersizing is safe, not
    /// wrong, because the store is guarded and a miss simply recomputes.
    /// </summary>
    private const int MemoCapacity = 8;

    // Narrowest count indicator any version uses, per mode (ISO/IEC 23941 Table 3;
    // pinned by RmQRSegmentPlannerUnitTest). A cost run at these widths is a lower
    // bound for every version, because widening a count indicator can only raise the
    // price of the run that carries it, and the minimum over plans of a pointwise
    // larger cost is itself larger.
    private const int MinCountBitsNumeric = 4;
    private const int MinCountBitsAlnum = 3;
    private const int MinCountBitsByte = 3;

    /// <summary>Narrowest count indicator of any mode at any version; the minimum of the three widths pinned by RmQRSegmentPlannerUnitTest.</summary>
    private const int MinCountBitsAny = 3;

    // Cheapest bits a single character can cost in any mode, in sixths so the numeric
    // and alphanumeric packing rates stay exact: 10 bits per 3 digits, 11 per 2
    // alphanumerics, 8 per byte. A partial group only ever costs more per character
    // (one digit is 4 bits against a rate of 3 1/3), so summing these rates can never
    // exceed what a real plan pays.
    private const int SixthsPerDigit = 20;
    private const int SixthsPerAlnum = 33;
    private const int SixthsPerByte = 48;

    /// <summary>rMQR ECI prefix: 3-bit mode indicator 111 plus a one-byte assignment designator.</summary>
    private const int EciHeaderBits = RmQRConstants.ModeIndicatorLength + 8;

    /// <summary>
    /// Version fit for mixed-mode segmentation. Returns the version to encode at and
    /// whether a mixed-mode plan is what makes it fit; when
    /// <paramref name="useSegments"/> is false the caller emits the ordinary
    /// single-mode stream, bit-identical to <see cref="RmQRSegmentation.Single"/>.
    /// Throws exactly what <see cref="RmQRVersionSelector"/> throws.
    /// </summary>
    public static RmQRVersion SelectVersion(ReadOnlySpan<char> text, in TextAnalysisResult analysis, RmQREccLevel eccLevel, RmQRVersion? requestedVersion, RmQRFitStrategy fitStrategy, RmQRHeight? height, out bool useSegments)
    {
        if (TrySelectVersion(text, in analysis, eccLevel, requestedVersion, fitStrategy, height, out var version, out useSegments))
            return version;

        // Neither one mode nor a mixed plan fits: the single-mode selector owns the
        // message. Only the Numeric shortcut can reach here with a requested version,
        // and it wants that wording, so forwarding it unchanged is correct.
        return Select(analysis.EncodingMode, analysis.DataLength, analysis.EciMode, eccLevel, requestedVersion, fitStrategy, height);
    }

    /// <summary>
    /// <see cref="SelectVersion"/> without the capacity throw; argument errors still
    /// throw, in the same order and with the same messages.
    /// </summary>
    public static bool TrySelectVersion(ReadOnlySpan<char> text, in TextAnalysisResult analysis, RmQREccLevel eccLevel, RmQRVersion? requestedVersion, RmQRFitStrategy fitStrategy, RmQRHeight? height, out RmQRVersion selected, out bool useSegments)
    {
        useSegments = false;
        var charset = analysis.EciMode;
        var mode = analysis.EncodingMode;
        var dataLength = analysis.DataLength;

        // Validate before any planning so argument errors keep their current type,
        // message and precedence; the selector re-validates on the paths reaching it.
        RmQRVersionSelector.ValidateFitArguments(eccLevel, fitStrategy, height, requestedVersion, charset);

        // All-Numeric content is already at the optimum: splitting a Numeric run never
        // lowers its payload and every extra run adds a header. Numeric-only — an
        // Alphanumeric or Byte payload can still hide a digit run worth splitting off.
        if (mode == EncodingMode.Numeric)
            return RmQRVersionSelector.TrySelect(mode, dataLength, charset, eccLevel, requestedVersion, fitStrategy, height, out selected);

        var eciBits = charset == EciMode.Default ? 0 : EciHeaderBits;

        if (requestedVersion is { } requested)
        {
            // Single mode already fits: mixing cannot shrink a fixed version, so the
            // stream stays exactly as it is today.
            selected = requested;
            if (Fits(requested, eccLevel, mode, dataLength, charset))
                return true;

            // One candidate, so pricing it here would decide what building the plan
            // decides anyway; TryBuildPlan rejects a plan the version cannot hold and
            // the caller falls back to the single-mode selector, which owns the error.
            useSegments = true;
            return true;
        }

        // Ceiling: the version single-mode encoding lands on, when there is one. When
        // there is none the scan runs to the end instead of stopping early, because
        // content that overflows every version in one mode can still fit once the
        // modes are mixed (100 letters followed by 100 digits is 200 Byte-mode
        // characters, 50 over the largest capacity, but 1157 bits when split).
        var hasSingle = RmQRVersionSelector.TrySelectAutoFit(mode, dataLength, charset, eccLevel, fitStrategy, height, out var single);

        // Unplannable content skips the scan before any cost run, which is what keeps
        // a pathological length from paying for one (the floor run below is not itself
        // guarded, unlike the per-version runs in PlanFits).
        if (text.Length is 0 or > MaxPlannableChars)
        {
            selected = single;
            return hasSingle;
        }

        var order = RmQRVersionSelector.GetFitOrder(fitStrategy);
        var heightMask = RmQRVersionSelector.GetFitHeightMask(fitStrategy, height);

        // Three filters, cheapest first, so a candidate only reaches an expensive one
        // when the cheap ones could not answer. Soundness in one line each: the trivial
        // bound and the floor are lower bounds, so they may only reject; the ceiling is
        // the price of a real plan, so it may only accept. Rationale and measurements:
        // specs/rmqr-encoder.md, "Bounding the scan".
        var trivialBits = TrivialLowerBoundBits(text, charset) + eciBits;

        var floorPayload = -1;
        var runsNumeric = 0;
        var runsAlnum = 0;
        var runsByte = 0;

        Span<int> memoKeys = stackalloc int[MemoCapacity];
        Span<int> memoCosts = stackalloc int[MemoCapacity];
        var memoCount = 0;

        for (var rank = 0; rank < order.Length; rank++)
        {
            var candidate = (RmQRVersion)order[rank];
            if (hasSingle && candidate == single)
                break; // every later rank is no better than the single-mode fit
            if ((heightMask & (1u << rank)) == 0)
                continue;

            var capacityBits = 8 * RmQRConstants.GetDataCodewordCount(candidate, eccLevel);
            if (capacityBits < trivialBits)
                continue; // no split of this content could fit, whatever the plan

            if (floorPayload < 0)
                floorPayload = ComputeFloor(text, charset, out runsNumeric, out runsAlnum, out runsByte);

            if (capacityBits < floorPayload + eciBits)
                continue; // cannot hold even the cheapest count indicators
            if (capacityBits >= UpperBound(floorPayload, runsNumeric, runsAlnum, runsByte, candidate) + eciBits)
            {
                // Holds the floor plan re-priced at this version, so it holds the
                // optimum too; no cost run needed.
                useSegments = true;
                selected = candidate;
                return true;
            }

            if (PlanFits(text, charset, candidate, eccLevel, eciBits, memoKeys, memoCosts, ref memoCount))
            {
                useSegments = true;
                selected = candidate;
                return true;
            }
        }

        selected = single;
        return hasSingle;
    }

    /// <summary>
    /// Builds the minimal-cost plan for <paramref name="version"/> into
    /// <paramref name="segments"/>. Returns false when the content is unplannable,
    /// the plan needs more runs than the caller lent room for, or the exact re-costed
    /// stream would not fit; the caller answers all three by falling back to the
    /// single-mode stream.
    /// </summary>
    public static bool TryBuildPlan(ReadOnlySpan<char> text, EciMode charset, RmQRVersion version, RmQREccLevel eccLevel, Span<RmQRSegment> segments, out int segmentCount)
    {
        segmentCount = 0;
        if (text.Length is 0 or > MaxPlannableChars)
            return false;

        var cciNumeric = RmQRConstants.GetCountIndicatorLength(version, EncodingMode.Numeric);
        var cciAlnum = RmQRConstants.GetCountIndicatorLength(version, EncodingMode.Alphanumeric);
        var cciByte = RmQRConstants.GetCountIndicatorLength(version, EncodingMode.Byte);

        var parentLength = text.Length * StateCount;
        byte[]? rented = null;
        Span<byte> parents = parentLength <= MaxStackParents
            ? stackalloc byte[MaxStackParents]
            : (rented = ArrayPool<byte>.Shared.Rent(parentLength));
        int plannedBits;
        try
        {
            var window = parents.Slice(0, parentLength);
            plannedBits = ComputeCosts(text, charset, cciNumeric, cciAlnum, cciByte, window, out var finalState);
            Debug.Assert(plannedBits < Unreachable, "Byte mode encodes every character, so a plan is always reachable");
            if (!Reconstruct(text, window, finalState, segments, out segmentCount))
            {
                segmentCount = 0;
                return false;
            }
        }
        finally
        {
            if (rented is not null)
                ArrayPool<byte>.Shared.Return(rented, clearArray: false);
        }

        FillUnitCounts(text, charset, segments.Slice(0, segmentCount));

        // Re-cost the reconstructed plan from the byte counts the encoder will
        // actually emit. Disagreeing with the dynamic programming cost model is a bug
        // in the model (the version scan would have compared the wrong number against
        // a capacity), so it fails loudly in Debug and rejects the plan in Release
        // rather than becoming a stream that overruns the data codewords.
        var measuredBits = MeasurePlan(version, segments.Slice(0, segmentCount));
        Debug.Assert(measuredBits == plannedBits, "the reconstructed plan must cost exactly what the dynamic program computed");

        var capacityBits = 8 * RmQRConstants.GetDataCodewordCount(version, eccLevel);
        if (measuredBits != plannedBits || measuredBits + (charset == EciMode.Default ? 0 : EciHeaderBits) > capacityBits)
        {
            // Either the model disagreed, or this version simply cannot hold the plan
            // (a legitimate answer for a caller that asked about a specific version).
            segmentCount = 0;
            return false;
        }

        return true;
    }

    /// <summary>
    /// The single-mode fit for the analyzed content, i.e. what
    /// <see cref="RmQRSegmentation.Single"/> would select. Throws the ordinary
    /// "content is too long" error when nothing fits.
    /// </summary>
    public static RmQRVersion SelectSingle(in TextAnalysisResult analysis, RmQREccLevel eccLevel, RmQRVersion? requestedVersion, RmQRFitStrategy fitStrategy, RmQRHeight? height)
        => Select(analysis.EncodingMode, analysis.DataLength, analysis.EciMode, eccLevel, requestedVersion, fitStrategy, height);

    /// <summary>Exact bit cost of a plan: per run, mode indicator + count indicator + payload.</summary>
    public static int MeasurePlan(RmQRVersion version, ReadOnlySpan<RmQRSegment> segments)
    {
        var total = 0;
        foreach (var segment in segments)
        {
            var mode = segment.Mode;
            total += RmQRConstants.ModeIndicatorLength + RmQRConstants.GetCountIndicatorLength(version, mode) + PayloadBits(mode, segment.UnitCount);
        }
        return total;
    }

    /// <summary>Payload bits of <paramref name="unitCount"/> units in <paramref name="mode"/> (ISO/IEC 23941 7.4).</summary>
    public static int PayloadBits(EncodingMode mode, int unitCount) => mode switch
    {
        EncodingMode.Numeric => unitCount / 3 * 10 + (unitCount % 3) switch { 2 => 7, 1 => 4, _ => 0 },
        EncodingMode.Alphanumeric => unitCount / 2 * 11 + unitCount % 2 * 6,
        EncodingMode.Byte => unitCount * 8,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), $"Encoding mode {mode} is not supported by rMQR."),
    };

    /// <summary>Encoded byte count of a Byte-mode run, i.e. the value its count indicator carries.</summary>
    /// <remarks>
    /// Deliberately asks <see cref="Encoding.UTF8"/> rather than reusing the planner's
    /// own per-character model: comparing the two is what would catch an error in that
    /// model, so they have to stay independent computations.
    /// </remarks>
    public static int ByteUnitCount(ReadOnlySpan<char> text, EciMode charset)
    {
        if (charset != EciMode.Utf8)
            return text.Length;
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        return Encoding.UTF8.GetByteCount(text);
#else
        // netstandard2.0 has no span overload, and the pointer overload would mean
        // enabling unsafe across the library, which this project has declined. Rent
        // rather than ToString(), so planning stays allocation-free on every target.
        var rented = ArrayPool<char>.Shared.Rent(Math.Max(text.Length, 1));
        try
        {
            text.CopyTo(rented);
            return Encoding.UTF8.GetByteCount(rented, 0, text.Length);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rented, clearArray: false);
        }
#endif
    }

    /// <summary>
    /// Named entry point for <c>RmQRSegmentPlannerUnitTest</c>: the minimal payload
    /// bits (no ECI prefix) at explicit count indicator widths, which is the value
    /// the version scan compares against a data capacity.
    /// </summary>
    public static int MinimumPayloadBits(ReadOnlySpan<char> text, EciMode charset, int cciNumeric, int cciAlnum, int cciByte)
        => ComputeCosts(text, charset, cciNumeric, cciAlnum, cciByte, default, out _);

    /// <summary>
    /// Named entry point for <c>RmQRSegmentPlannerUnitTest</c>: the upper bound the
    /// version scan uses to accept a candidate without pricing it — the floor plan
    /// re-priced at <paramref name="version"/>, in payload bits (no ECI prefix).
    /// </summary>
    public static int FloorPlanUpperBound(ReadOnlySpan<char> text, EciMode charset, RmQRVersion version)
    {
        var floor = ComputeFloor(text, charset, out var runsNumeric, out var runsAlnum, out var runsByte);
        return UpperBound(floor, runsNumeric, runsAlnum, runsByte, version);
    }

    // ---------------------------------------------------------------
    // Version scan
    // ---------------------------------------------------------------

    private static bool PlanFits(ReadOnlySpan<char> text, EciMode charset, RmQRVersion version, RmQREccLevel eccLevel, int eciBits, Span<int> memoKeys, Span<int> memoCosts, ref int memoCount)
    {
        if (text.Length is 0 or > MaxPlannableChars)
            return false;

        var cciNumeric = RmQRConstants.GetCountIndicatorLength(version, EncodingMode.Numeric);
        var cciAlnum = RmQRConstants.GetCountIndicatorLength(version, EncodingMode.Alphanumeric);
        var cciByte = RmQRConstants.GetCountIndicatorLength(version, EncodingMode.Byte);
        var key = cciNumeric | (cciAlnum << 8) | (cciByte << 16);

        var cost = -1;
        for (var i = 0; i < memoCount; i++)
        {
            if (memoKeys[i] == key)
            {
                cost = memoCosts[i];
                break;
            }
        }

        if (cost < 0)
        {
            cost = ComputeCosts(text, charset, cciNumeric, cciAlnum, cciByte, default, out _);
            if (memoCount < memoKeys.Length)
            {
                memoKeys[memoCount] = key;
                memoCosts[memoCount] = cost;
                memoCount++;
            }
        }

        return cost + eciBits <= 8 * RmQRConstants.GetDataCodewordCount(version, eccLevel);
    }

    // ---------------------------------------------------------------
    // Scan bounds
    // ---------------------------------------------------------------

    /// <summary>
    /// A lower bound on any plan at any version, in payload bits, computed in one O(n)
    /// pass with no dynamic programming table: each character priced at the cheapest
    /// rate any mode could give it, plus the cheapest possible single segment header.
    /// </summary>
    /// <remarks>
    /// Deliberately cruder than <see cref="ComputeFloor"/> and far cheaper: it answers
    /// "could a split reach a better version at all" before any cost run. Loose where
    /// a split is worth searching, tight where it is not.
    /// </remarks>
    public static int TrivialLowerBoundBits(ReadOnlySpan<char> text, EciMode charset)
    {
        var sixths = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (CharacterSets.IsNumeric(c))
                sixths += SixthsPerDigit;
            else if (CharacterSets.IsAlphanumeric(c))
                sixths += SixthsPerAlnum;
            else
                sixths += SixthsPerByte * ByteCost(text, i, charset);
        }

        // Round up to whole bits, then add the cheapest header a stream can carry.
        return (sixths + 5) / 6 + RmQRConstants.ModeIndicatorLength + MinCountBitsAny;
    }

    /// <summary>
    /// The floor cost run: minimal payload bits at the narrowest count indicator
    /// widths, plus how many runs of each mode the plan achieving it contains. The run
    /// counts are what let <see cref="UpperBound"/> re-price that same plan at any
    /// version without a second cost run.
    /// </summary>
    private static int ComputeFloor(ReadOnlySpan<char> text, EciMode charset, out int runsNumeric, out int runsAlnum, out int runsByte)
    {
        var parentLength = text.Length * StateCount;
        byte[]? rented = null;
        Span<byte> parents = parentLength <= MaxStackParents
            ? stackalloc byte[MaxStackParents]
            : (rented = ArrayPool<byte>.Shared.Rent(parentLength));
        try
        {
            var window = parents.Slice(0, parentLength);
            var cost = ComputeCosts(text, charset, MinCountBitsNumeric, MinCountBitsAlnum, MinCountBitsByte, window, out var finalState);
            CountRuns(window, finalState, text.Length, out runsNumeric, out runsAlnum, out runsByte);
            return cost;
        }
        finally
        {
            if (rented is not null)
                ArrayPool<byte>.Shared.Return(rented, clearArray: false);
        }
    }

    /// <summary>
    /// The floor plan re-priced at <paramref name="version"/>: its payload and run
    /// structure are unchanged, so only each run's count indicator grows. Being the
    /// price of a real plan, this is an upper bound on the version's optimum.
    /// </summary>
    private static int UpperBound(int floorPayload, int runsNumeric, int runsAlnum, int runsByte, RmQRVersion version)
        => floorPayload
            + runsNumeric * (RmQRConstants.GetCountIndicatorLength(version, EncodingMode.Numeric) - MinCountBitsNumeric)
            + runsAlnum * (RmQRConstants.GetCountIndicatorLength(version, EncodingMode.Alphanumeric) - MinCountBitsAlnum)
            + runsByte * (RmQRConstants.GetCountIndicatorLength(version, EncodingMode.Byte) - MinCountBitsByte);

    /// <summary>Counts the runs of each mode on the minimal-cost path, without materialising it.</summary>
    private static void CountRuns(ReadOnlySpan<byte> parents, int finalState, int length, out int runsNumeric, out int runsAlnum, out int runsByte)
    {
        runsNumeric = 0;
        runsAlnum = 0;
        runsByte = 0;

        var state = finalState;
        for (var i = length - 1; i >= 0; i--)
        {
            var parent = parents[i * StateCount + state];
            if (parent == StateStart || ModeIndexOf(parent) != ModeIndexOf(state))
            {
                switch (ModeIndexOf(state))
                {
                    case 0: runsNumeric++; break;
                    case 1: runsAlnum++; break;
                    default: runsByte++; break;
                }
            }
            state = parent;
        }
    }

    // ---------------------------------------------------------------
    // Dynamic program
    // ---------------------------------------------------------------

    /// <summary>
    /// Minimal payload bits (excluding any ECI prefix) for the content at the given
    /// count indicator widths. When <paramref name="parents"/> is non-empty it
    /// receives one predecessor state per (character, state) pair for reconstruction.
    /// </summary>
    private static int ComputeCosts(ReadOnlySpan<char> text, EciMode charset, int cciNumeric, int cciAlnum, int cciByte, Span<byte> parents, out int finalState)
    {
        Debug.Assert(parents.IsEmpty || parents.Length == text.Length * StateCount);

        Span<int> prev = stackalloc int[StateCount];
        Span<int> cur = stackalloc int[StateCount];
        for (var s = 0; s < StateCount; s++)
            prev[s] = Unreachable;
        prev[StateStart] = 0;

        var track = !parents.IsEmpty;
        var openNumeric = RmQRConstants.ModeIndicatorLength + cciNumeric;
        var openAlnum = RmQRConstants.ModeIndicatorLength + cciAlnum;
        var openByte = RmQRConstants.ModeIndicatorLength + cciByte;

        for (var i = 0; i < text.Length; i++)
        {
            for (var s = 0; s < StateCount; s++)
                cur[s] = Unreachable;

            var c = text[i];
            var isNumeric = CharacterSets.IsNumeric(c);
            var isAlnum = CharacterSets.IsAlphanumeric(c);
            var byteBits = 8 * ByteCost(text, i, charset);
            var parentBase = i * StateCount;

            for (var from = 0; from < StateCount; from++)
            {
                var basis = prev[from];
                if (basis >= Unreachable)
                    continue;

                if (isNumeric)
                {
                    int target, cost;
                    if (from <= StateNumeric2)
                    {
                        // Continue the run: the first digit of a group costs 4 bits, the next two 3 each.
                        cost = basis + (from == StateNumeric0 ? 4 : 3);
                        target = from == StateNumeric2 ? StateNumeric0 : from + 1;
                    }
                    else
                    {
                        cost = basis + openNumeric + 4;
                        target = StateNumeric1;
                    }
                    Relax(cur, parents, parentBase, target, cost, from, track);
                }

                if (isAlnum)
                {
                    int target, cost;
                    if (from is StateAlnum0 or StateAlnum1)
                    {
                        // 11 bits per pair: 6 for the first character of a pair, 5 for the second.
                        cost = basis + (from == StateAlnum0 ? 6 : 5);
                        target = from == StateAlnum0 ? StateAlnum1 : StateAlnum0;
                    }
                    else
                    {
                        cost = basis + openAlnum + 6;
                        target = StateAlnum1;
                    }
                    Relax(cur, parents, parentBase, target, cost, from, track);
                }

                {
                    // Byte mode encodes every character, so this transition always exists.
                    var cost = from == StateByte ? basis + byteBits : basis + openByte + byteBits;
                    Relax(cur, parents, parentBase, StateByte, cost, from, track);
                }
            }

            cur.CopyTo(prev);
        }

        var best = Unreachable;
        finalState = StateByte;
        for (var s = 0; s <= StateByte; s++)
        {
            if (prev[s] < best)
            {
                best = prev[s];
                finalState = s;
            }
        }
        return best;
    }

    private static void Relax(Span<int> cur, Span<byte> parents, int parentBase, int target, int cost, int from, bool track)
    {
        if (cost >= cur[target])
            return;
        cur[target] = cost;
        if (track)
            parents[parentBase + target] = (byte)from;
    }

    /// <summary>
    /// Walks the predecessor table back into runs, oldest first. Returns false when
    /// the plan needs more runs than the caller lent room for.
    /// </summary>
    private static bool Reconstruct(ReadOnlySpan<char> text, ReadOnlySpan<byte> parents, int finalState, Span<RmQRSegment> segments, out int segmentCount)
    {
        segmentCount = 0;
        var state = finalState;
        var end = text.Length;
        var count = 0;

        for (var i = text.Length - 1; i >= 0; i--)
        {
            var parent = parents[i * StateCount + state];
            if (parent == StateStart || ModeIndexOf(parent) != ModeIndexOf(state))
            {
                if (count >= segments.Length)
                    return false;
                segments[count++] = new RmQRSegment(ModeIndexOf(state), i, end - i, 0);
                end = i;
            }
            state = parent;
        }

        Debug.Assert(state == StateStart, "the walk must terminate at the virtual start state");
        segments.Slice(0, count).Reverse();
        segmentCount = count;
        return true;
    }

    /// <summary>Fills each run with the value its count indicator carries.</summary>
    private static void FillUnitCounts(ReadOnlySpan<char> text, EciMode charset, Span<RmQRSegment> segments)
    {
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            var units = segment.ModeIndex == 2
                ? ByteUnitCount(text.Slice(segment.Start, segment.Length), charset)
                : segment.Length;
            segments[i] = new RmQRSegment(segment.ModeIndex, segment.Start, segment.Length, units);
        }
    }

    /// <summary>Dense mode index of a state, or -1 for the virtual start state.</summary>
    private static int ModeIndexOf(int state) => state switch
    {
        <= StateNumeric2 => 0,
        StateAlnum0 or StateAlnum1 => 1,
        StateByte => 2,
        _ => -1,
    };

    /// <summary>
    /// Encoded byte length of one character in Byte mode. Latin-1 charsets are one
    /// byte per character; UTF-8 mirrors what <see cref="Encoding.UTF8"/> emits,
    /// including its greedy surrogate pairing (a paired high surrogate carries all
    /// four bytes and its low surrogate none, an unpaired surrogate costs the three
    /// bytes of the replacement character).
    /// </summary>
    private static int ByteCost(ReadOnlySpan<char> text, int index, EciMode charset)
    {
        if (charset != EciMode.Utf8)
            return 1;

        var c = text[index];
        if (c < 0x80)
            return 1;
        if (c < 0x800)
            return 2;
        if (char.IsHighSurrogate(c))
            return index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]) ? 4 : 3;
        if (char.IsLowSurrogate(c))
            return index > 0 && char.IsHighSurrogate(text[index - 1]) ? 0 : 3;
        return 3;
    }

    // ---------------------------------------------------------------
    // Selector adapters (the two Select overloads stay apart, as the selector keeps them)
    // ---------------------------------------------------------------

    private static bool Fits(RmQRVersion version, RmQREccLevel eccLevel, EncodingMode mode, int dataLength, EciMode charset)
        => charset == EciMode.Default
            ? RmQRVersionSelector.Fits(version, eccLevel, mode, dataLength)
            : RmQRVersionSelector.Fits(version, eccLevel, mode, dataLength, charset);

    private static RmQRVersion Select(EncodingMode mode, int dataLength, EciMode charset, RmQREccLevel eccLevel, RmQRVersion? requestedVersion, RmQRFitStrategy fitStrategy, RmQRHeight? height)
        => charset == EciMode.Default
            ? RmQRVersionSelector.Select(mode, dataLength, eccLevel, requestedVersion, fitStrategy, height)
            : RmQRVersionSelector.Select(mode, dataLength, charset, eccLevel, requestedVersion, fitStrategy, height);
}
