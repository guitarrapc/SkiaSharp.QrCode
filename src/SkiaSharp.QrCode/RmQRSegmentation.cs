namespace SkiaSharp.QrCode;

/// <summary>
/// How <c>RmQRCodeGenerator</c> splits the content into encoding-mode segments.
/// rMQR capacities are small, so mixing modes (for example a Byte prefix followed
/// by a Numeric tail) can drop the symbol by one or more versions.
/// </summary>
public enum RmQRSegmentation
{
    /// <summary>
    /// One segment in the single mode that can represent the whole content
    /// (Numeric, else Alphanumeric, else Byte). The default, and the cheapest to
    /// encode.
    /// </summary>
    Single = 0,

    /// <summary>
    /// Fewest total bits over all mixed-mode splits, computed by dynamic
    /// programming.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Never selects a larger symbol than <see cref="Single"/>, and falls back to the
    /// <see cref="Single"/> bit stream verbatim whenever the split would not shrink
    /// the symbol. It also encodes content that overflows every version in a single
    /// mode: 100 letters followed by 100 digits is 200 Byte-mode characters, 50 over
    /// the largest capacity, but fits R17x139 once the digits become their own run.
    /// </para>
    /// <para>
    /// This minimises bits, not symbol dimensions: the version it lands on is still
    /// whichever one <see cref="RmQRFitStrategy"/> ranks best among those the plan
    /// fits.
    /// </para>
    /// <para>
    /// Planning is a search over candidate versions, which is why it is opt-in. It
    /// allocates nothing, and the version a split could reach is bounded before any
    /// planning runs, so content no split can shrink mostly costs nothing: measured
    /// against a <see cref="Single"/> encode of the same content, 120 digits and 150
    /// lowercase both land at 1.0-1.1x. Where planning does run its cost tracks how
    /// much the split helps — the more it lowers the bit cost, the more candidate
    /// versions become plausible — so half letters half digits costs 4.8x and
    /// characters alternating in tens 6.4x. The one shape that pays without winning is
    /// finely alternating content (2.2x), which the cheap bound cannot rule out.
    /// Content longer than 361 characters, which no rMQR symbol holds in any mode, is
    /// rejected without planning.
    /// </para>
    /// </remarks>
    Optimal = 1,
}
