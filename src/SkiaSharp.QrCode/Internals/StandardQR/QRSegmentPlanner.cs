using System.Buffers;
using System.Diagnostics;
using System.Text;

namespace SkiaSharp.QrCode.Internals.StandardQr;

/// <summary>
/// Mixed-mode segmentation for <see cref="QRCodeSegmentation.Optimal"/>: the split of
/// the content into Numeric / Alphanumeric / Byte runs whose total bit cost is
/// minimal for a given version, and the version fit that follows from it.
/// </summary>
/// <remarks>
/// A run's cost is not a per-character constant (Numeric packs 3 digits into 10 bits,
/// Alphanumeric 2 characters into 11), so the dynamic program carries the group
/// remainder in its state rather than rounding an average. The version scan is cheap
/// by construction: character count indicator widths are constant within the three
/// ISO/IEC 18004 version bands (1-9 / 10-26 / 27-40), so the optimal cost is computed
/// at most once per band, and the single-mode fit caps the scan so a plan is produced
/// only when it lowers the version. Design rationale, bounds and measurements:
/// specs/standardqr-encoder.md, "Mixed-mode segmentation".
/// </remarks>
internal static class QRSegmentPlanner
{
    /// <summary>
    /// Content length up to which a plan buffer fits the stack
    /// (<see cref="MaxStackSegments"/> runs, 512 bytes); longer content rents a
    /// text-length buffer, which no plan can outgrow because every run holds at
    /// least one character.
    /// </summary>
    public const int MaxStackSegments = 64;

    /// <summary>
    /// Longest content any Standard QR symbol can hold, in characters (7089 digits
    /// at version 40-L, an exact fit: 2363 groups × 10 bits + 4 + 14 = 23,648 bits).
    /// No mixed plan can beat it: mixing only adds headers to a denser-per-character
    /// mode that does not exist.
    /// </summary>
    /// <remarks>
    /// A rejection rule, not only a work cap: a mixed plan can encode content no single
    /// mode holds, so this is what declares longer content impossible. The margin is
    /// 4 bits (7090 digits cost 23,652 against 23,648), so re-derive it rather than
    /// nudge it if the capacity tables change. Pinned by <c>QRSegmentPlannerUnitTest</c>.
    /// </remarks>
    public const int MaxPlannableChars = 7089;

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

    /// <summary>Standard QR mode indicator width (ISO/IEC 18004 7.4.1).</summary>
    private const int ModeIndicatorBits = 4;

    /// <summary>Standard QR ECI prefix: 4-bit mode indicator 0111 plus a one-byte assignment designator.</summary>
    private const int EciHeaderBits = ModeIndicatorBits + 8;

    /// <summary>Narrowest count indicator of any mode at any version (Byte at versions 1-9); pinned by QRSegmentPlannerUnitTest.</summary>
    private const int MinCountBitsAny = 8;

    // Cheapest bits a single character can cost in any mode, in sixths so the numeric
    // and alphanumeric packing rates stay exact: 10 bits per 3 digits, 11 per 2
    // alphanumerics, 8 per byte. A partial group only ever costs more per character
    // (one digit is 4 bits against a rate of 3 1/3), so summing these rates can never
    // exceed what a real plan pays.
    private const int SixthsPerDigit = 20;
    private const int SixthsPerAlnum = 33;
    private const int SixthsPerByte = 48;

    /// <summary>
    /// Version fit for mixed-mode segmentation, restricted to
    /// <paramref name="minVersion"/> through <paramref name="maxVersion"/>. Returns
    /// the version to encode at and whether a mixed-mode plan is what makes it fit;
    /// when <paramref name="useSegments"/> is false the caller emits the ordinary
    /// single-mode stream, bit-identical to <see cref="QRCodeSegmentation.Single"/>.
    /// <c>false</c> means the content fits neither one mode nor a mixed plan in the
    /// window; the caller owns the error.
    /// </summary>
    /// <remarks>
    /// The scan needs no bounding machinery: count indicator widths are constant
    /// within the three version bands, so the optimal cost is computed at most once
    /// per band (three O(n) runs in the worst case, no reconstruction table), and
    /// capacity grows monotonically inside a band, so the first version that holds
    /// the band cost is the smallest.
    /// </remarks>
    public static bool TrySelectVersion(ReadOnlySpan<char> text, in TextAnalysisResult analysis, ECCLevel eccLevel, int minVersion, int maxVersion, out int selected, out bool useSegments)
    {
        useSegments = false;
        var charset = analysis.EciMode;

        // Ceiling: the version single-mode encoding lands on, when there is one. When
        // there is none the scan runs to the window's end instead of stopping early,
        // because content that overflows every version in one mode can still fit once
        // the modes are mixed (3000 letters followed by 4000 digits is 7000 Byte-mode
        // characters, far over the largest Byte capacity, but fits as two runs).
        var hasSingle = QRCodeGenerator.TryGetVersionInRange(analysis.DataLength, analysis.EncodingMode, eccLevel, charset, utf8BOM: false, minVersion, maxVersion, out var single);

        // All-Numeric content is already at the optimum: splitting a Numeric run never
        // lowers its payload and every extra run adds a header. Numeric-only — an
        // Alphanumeric or Byte payload can still hide a digit run worth splitting off.
        if (analysis.EncodingMode == EncodingMode.Numeric)
        {
            selected = single;
            return hasSingle;
        }

        // Unplannable content skips the scan before any cost run, which is what keeps
        // a pathological length from paying for one.
        if (text.Length is 0 or > MaxPlannableChars)
        {
            selected = single;
            return hasSingle;
        }

        var eciBits = charset == EciMode.Default ? 0 : EciHeaderBits;

        // Strictly below the single-mode fit: at that version the single-mode stream
        // already fits, and emitting it unchanged is the blast-radius bound the
        // feature is designed around.
        var top = hasSingle ? Math.Min(single - 1, maxVersion) : maxVersion;
        if (top < minVersion)
        {
            selected = single;
            return hasSingle;
        }

        // One O(n) pass pricing each character at the cheapest rate any mode could
        // give it: a lower bound on any plan at any version, so it may only reject.
        // It is what keeps Optimal roughly free on content no split can shrink — a
        // candidate only pays for a cost run when it could hold at least this much.
        var trivialBits = TrivialLowerBoundBits(text, charset) + eciBits;

        var band = -1;
        var bandCost = 0;
        for (var version = minVersion; version <= top; version++)
        {
            var capacityBits = QRCodeConstants.GetEccInfo(version, eccLevel).TotalDataCodewords * 8;
            if (capacityBits < trivialBits)
                continue; // no split of this content could fit, whatever the plan

            var candidateBand = version < 10 ? 0 : version < 27 ? 1 : 2;
            if (candidateBand != band)
            {
                band = candidateBand;
                bandCost = ComputeCosts(
                    text, charset,
                    EncodingMode.Numeric.GetCountIndicatorLength(version),
                    EncodingMode.Alphanumeric.GetCountIndicatorLength(version),
                    EncodingMode.Byte.GetCountIndicatorLength(version),
                    default, out _);
            }

            if (bandCost + eciBits <= capacityBits)
            {
                useSegments = true;
                selected = version;
                return true;
            }
        }

        selected = single;
        return hasSingle;
    }

    /// <summary>
    /// A lower bound on any plan at any version, in payload bits, computed in one O(n)
    /// pass with no dynamic programming table: each character priced at the cheapest
    /// rate any mode could give it, plus the cheapest possible single segment header.
    /// </summary>
    /// <remarks>
    /// Deliberately crude: it answers "could a split reach a better version at all"
    /// before any cost run. Loose where a split is worth searching, tight where it is
    /// not. Its blind spot is finely alternating content, which looks far cheaper than
    /// it is because seeing that switching modes every character never pays requires
    /// modelling the switch cost — that is the dynamic program itself.
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
        return (sixths + 5) / 6 + ModeIndicatorBits + MinCountBitsAny;
    }

    /// <summary>
    /// Builds the minimal-cost plan for <paramref name="version"/> into
    /// <paramref name="segments"/>. Returns false when the content is unplannable,
    /// the plan needs more runs than the caller lent room for, or the exact re-costed
    /// stream would not fit; the caller answers all three by falling back to the
    /// single-mode stream.
    /// </summary>
    public static bool TryBuildPlan(ReadOnlySpan<char> text, EciMode charset, int version, ECCLevel eccLevel, Span<QRSegment> segments, out int segmentCount)
    {
        segmentCount = 0;
        if (text.Length is 0 or > MaxPlannableChars)
            return false;

        var cciNumeric = EncodingMode.Numeric.GetCountIndicatorLength(version);
        var cciAlnum = EncodingMode.Alphanumeric.GetCountIndicatorLength(version);
        var cciByte = EncodingMode.Byte.GetCountIndicatorLength(version);

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

        var capacityBits = QRCodeConstants.GetEccInfo(version, eccLevel).TotalDataCodewords * 8;
        if (measuredBits != plannedBits || measuredBits + (charset == EciMode.Default ? 0 : EciHeaderBits) > capacityBits)
        {
            // Either the model disagreed, or this version simply cannot hold the plan
            // (a legitimate answer for a caller that asked about a specific version).
            segmentCount = 0;
            return false;
        }

        return true;
    }

    /// <summary>Exact bit cost of a plan (excluding any ECI prefix): per run, mode indicator + count indicator + payload.</summary>
    public static int MeasurePlan(int version, ReadOnlySpan<QRSegment> segments)
    {
        var total = 0;
        foreach (var segment in segments)
        {
            var mode = segment.Mode;
            total += ModeIndicatorBits + mode.GetCountIndicatorLength(version) + PayloadBits(mode, segment.UnitCount);
        }
        return total;
    }

    /// <summary>Payload bits of <paramref name="unitCount"/> units in <paramref name="mode"/> (ISO/IEC 18004 7.4).</summary>
    public static int PayloadBits(EncodingMode mode, int unitCount) => mode switch
    {
        EncodingMode.Numeric => unitCount / 3 * 10 + (unitCount % 3) switch { 2 => 7, 1 => 4, _ => 0 },
        EncodingMode.Alphanumeric => unitCount / 2 * 11 + unitCount % 2 * 6,
        EncodingMode.Byte => unitCount * 8,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), $"Encoding mode {mode} is not plannable."),
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
    /// Named entry point for <c>QRSegmentPlannerUnitTest</c>: the minimal payload
    /// bits (no ECI prefix) at explicit count indicator widths, which is the value
    /// the version scan compares against a data capacity.
    /// </summary>
    public static int MinimumPayloadBits(ReadOnlySpan<char> text, EciMode charset, int cciNumeric, int cciAlnum, int cciByte)
        => ComputeCosts(text, charset, cciNumeric, cciAlnum, cciByte, default, out _);

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
        var openNumeric = ModeIndicatorBits + cciNumeric;
        var openAlnum = ModeIndicatorBits + cciAlnum;
        var openByte = ModeIndicatorBits + cciByte;

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
    private static bool Reconstruct(ReadOnlySpan<char> text, ReadOnlySpan<byte> parents, int finalState, Span<QRSegment> segments, out int segmentCount)
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
                segments[count++] = new QRSegment(ModeIndexOf(state), i, end - i, 0);
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
    private static void FillUnitCounts(ReadOnlySpan<char> text, EciMode charset, Span<QRSegment> segments)
    {
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            var units = segment.ModeIndex == 2
                ? ByteUnitCount(text.Slice(segment.Start, segment.Length), charset)
                : segment.Length;
            segments[i] = new QRSegment(segment.ModeIndex, segment.Start, segment.Length, units);
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
}
