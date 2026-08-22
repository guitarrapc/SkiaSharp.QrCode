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
    /// allocates nothing, but its cost is driven by how much the split helps rather
    /// than by how mixed the content looks: the more a split lowers the bit cost, the
    /// more candidate versions become plausible and the more of them get priced. At
    /// 120 characters, all digits costs 1.0x a <see cref="Single"/> encode (planning
    /// is short-circuited), all lowercase 2.1x (searched, never wins), and half
    /// letters half digits 4.2x (searched, wins a version). Cost is linear in length
    /// at a fixed shape. Content longer than 361 characters — which no rMQR symbol
    /// holds in any mode — is rejected without planning.
    /// </para>
    /// </remarks>
    Optimal = 1,
}
