namespace SkiaSharp.QrCode;

/// <summary>
/// How <c>QRCodeGenerator</c> splits the content into encoding-mode segments.
/// Mixed content (for example a URL prefix followed by a long numeric identifier)
/// packs into fewer bits when each run is encoded in its densest mode, which can
/// drop the symbol by one or more versions.
/// </summary>
public enum QRCodeSegmentation
{
    /// <summary>
    /// One segment in the single mode that can represent the whole content
    /// (Numeric, else Alphanumeric, else Byte). The default, and the cheapest to
    /// encode.
    /// </summary>
    Single = 0,

    /// <summary>
    /// The mixed-mode split with the fewest total bits. Never selects a larger
    /// version than <see cref="Single"/>, emits the <see cref="Single"/> bit stream
    /// verbatim when a split would not shrink the symbol, and additionally encodes
    /// content that overflows every version in a single mode.
    /// </summary>
    /// <remarks>
    /// Opt-in because it searches candidate versions; the search itself allocates
    /// nothing for typical content and rents pooled buffers for long content.
    /// <see cref="QRCodeGeneratorOptions.Utf8BOM"/> disables the split (the BOM is a
    /// stream-level prefix, and a split would relocate it into the middle of the
    /// decoded text), so that combination emits the <see cref="Single"/> stream.
    /// Size buffers with the same segmentation you encode with, the two can select
    /// different versions.
    /// </remarks>
    Optimal = 1,
}
