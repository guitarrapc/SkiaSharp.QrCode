namespace FeatherQR;

/// <summary>
/// Result status of a QR code decode attempt.
/// </summary>
public enum QRCodeDecodeStatus
{
    /// <summary>Decoding succeeded.</summary>
    Success = 0,

    /// <summary>The input is not a valid QR module matrix (invalid size or no dark modules).</summary>
    InvalidMatrix,

    /// <summary>Both format information copies are corrupted beyond BCH correction capacity.</summary>
    FormatInformationInvalid,

    /// <summary>One or more Reed-Solomon blocks contain more errors than the ECC level can correct.</summary>
    DataUncorrectable,

    /// <summary>The data bitstream is malformed (invalid segment values or truncated data).</summary>
    InvalidBitstream,

    /// <summary>
    /// The bitstream is well-formed but uses a feature this decoder does not support
    /// (FNC1, Structured Append, or an unsupported ECI charset). A property of the
    /// symbol's structure, not of its text; see <see cref="UnmappedCharacter"/> for the
    /// per-character case.
    /// </summary>
    UnsupportedContent,

    /// <summary>The destination buffer is too small for the decoded text.</summary>
    DestinationTooSmall,

    /// <summary>No QR code was detected in the image (finder patterns not found or inconsistent).</summary>
    NotDetected,

    /// <summary>
    /// The symbol was read correctly, but one character has no mapping under the
    /// charset this decoder applies, so no text is produced.
    /// </summary>
    /// <remarks>
    /// Today this means a Kanji mode cell outside JIS X 0208 — in practice one of the
    /// 83 characters Microsoft CP932 adds in NEC row 13 (circled digits, roman
    /// numerals, unit ligatures). It is deliberately distinct from
    /// <see cref="UnsupportedContent"/>: that says the symbol uses a feature this
    /// library does not implement, and no reader choice changes it, whereas this says
    /// the symbol is well formed and a CP932-capable reader would read it. A caller
    /// that wants to fall back to such a reader should branch on exactly this status.
    /// </remarks>
    UnmappedCharacter,
}

/// <summary>
/// Diagnostic information produced by a QR code decode attempt.
/// </summary>
public readonly struct QRCodeDecodeInfo
{
    internal QRCodeDecodeInfo(QRCodeDecodeStatus status, int version, ECCLevel eccLevel, int maskPattern, int errorsCorrected)
    {
        Status = status;
        Version = version;
        EccLevel = eccLevel;
        MaskPattern = maskPattern;
        ErrorsCorrected = errorsCorrected;
    }

    /// <summary>Decode result status. <see cref="QRCodeDecodeStatus.Success"/> when decoding succeeded.</summary>
    public QRCodeDecodeStatus Status { get; }

    /// <summary>QR code version (1-40), or 0 when the matrix was invalid.</summary>
    public int Version { get; }

    /// <summary>Error correction level read from the format information.</summary>
    public ECCLevel EccLevel { get; }

    /// <summary>Mask pattern (0-7) read from the format information, or -1 when unknown.</summary>
    public int MaskPattern { get; }

    /// <summary>Total number of codeword errors corrected by Reed-Solomon decoding.</summary>
    public int ErrorsCorrected { get; }
}
