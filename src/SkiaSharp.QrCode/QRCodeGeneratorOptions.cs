namespace SkiaSharp.QrCode;

/// <summary>
/// Optional settings for <see cref="QRCodeGenerator"/>. <c>default</c> is the complete
/// default configuration, so set only what you need:
/// <c>new QRCodeGeneratorOptions { EciMode = EciMode.Utf8, QuietZoneSize = 0 }</c>.
/// </summary>
/// <remarks>
/// Standard QR specific rather than shared: the three generators agree on almost nothing.
/// <see cref="Version"/> is a different type in each, <see cref="QuietZoneSize"/> has a
/// different specified default in each, Micro QR has no ECI, and rMQR carries fit
/// options that mean nothing here.
/// </remarks>
public readonly record struct QRCodeGeneratorOptions
{
    /// <summary>ISO/IEC 18004 quiet zone for Standard QR, and the parameter list default.</summary>
    internal const int DefaultQuietZone = 4;

    // Offset from the specified default, so default(T) carries 4 rather than 0 (a legitimate
    // caller choice that cannot double as "unset"). An offset rather than a value+1 sentinel
    // keeps the canonical form unique, so the generated equality does not report two
    // identical option sets as different.
    private readonly int _quietZoneSizeOffset;

    /// <summary>The default configuration, identical to <c>default</c>.</summary>
    public static QRCodeGeneratorOptions Default => default;

    /// <summary>
    /// Character encoding declaration. The default auto-detects ASCII (no ECI),
    /// ISO-8859-1 (assignment 3) or UTF-8 (assignment 26) from the content.
    /// </summary>
    public EciMode EciMode { get; init; }

    /// <summary>
    /// Include a UTF-8 byte order mark. Ignored unless the content is written as UTF-8
    /// in Byte mode.
    /// </summary>
    public bool Utf8BOM { get; init; }

    /// <summary>
    /// The versions the generator may choose from. Defaults to
    /// <see cref="QRCodeVersionRange.Any"/>; an <c>int</c> or <c>int?</c> converts
    /// implicitly, so <c>Version = 15</c> pins one and a <c>null</c> means automatic.
    /// </summary>
    public QRCodeVersionRange Version { get; init; }

    /// <summary>
    /// Quiet zone width in modules. Defaults to 4, the ISO/IEC 18004 value; 0 is valid.
    /// </summary>
    public int QuietZoneSize
    {
        get => DefaultQuietZone + _quietZoneSizeOffset;
        init => _quietZoneSizeOffset = value - DefaultQuietZone;
    }
}
