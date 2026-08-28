namespace SkiaSharp.QrCode;

/// <summary>
/// Optional settings for <see cref="QRCodeGenerator"/>. Every generator entry point has
/// an overload taking one of these instead of a parameter list, so a new option never
/// changes an existing signature.
/// </summary>
/// <remarks>
/// <para>
/// <c>default</c> is the complete default configuration, identical to what the parameter
/// list overloads apply when nothing is passed. Set only what you need, with an object
/// initializer or <c>with</c>:
/// </para>
/// <code>
/// var data = QRCodeGenerator.CreateQrCode("https://example.com", ECCLevel.M,
///     new QRCodeGeneratorOptions { EciMode = EciMode.Utf8, QuietZoneSize = 0 });
/// </code>
/// <para>
/// The parameter list overloads remain the shortest way to spell the common cases and are
/// not going away; this type is where options are added from now on.
/// </para>
/// <para>
/// This is a Standard QR specific type rather than one shared across symbologies. The
/// three generators agree on almost nothing: <see cref="Version"/> is a different type in
/// each, <see cref="QuietZoneSize"/> has a different specified default in each, Micro QR
/// has no ECI at all, and rMQR carries fit strategy, height and segmentation options that
/// mean nothing here.
/// </para>
/// </remarks>
public readonly record struct QRCodeGeneratorOptions
{
    /// <summary>ISO/IEC 18004 quiet zone for Standard QR, and the parameter list default.</summary>
    internal const int DefaultQuietZone = 4;

    // Stored as an offset from the specified default so that default(T) carries 4 rather
    // than 0, which is a legitimate caller choice and therefore cannot double as "unset".
    //
    // An offset rather than a value+1 sentinel, because the offset makes the canonical form
    // unique: writing 4 explicitly produces the same field value as not writing it, so the
    // generated equality does not report two identical option sets as different.
    private readonly int _quietZoneSizeOffset;

    /// <summary>The default configuration, identical to <c>default</c>.</summary>
    public static QRCodeGeneratorOptions Default => default;

    /// <summary>
    /// Character encoding declaration. The default auto-detects ASCII (no ECI),
    /// ISO-8859-1 (assignment 3) or UTF-8 (assignment 26) from the content.
    /// </summary>
    public EciMode EciMode { get; init; }

    /// <summary>
    /// Include a UTF-8 byte order mark in the encoded data. Ignored unless the content is
    /// written as UTF-8 in Byte mode.
    /// </summary>
    public bool Utf8BOM { get; init; }

    /// <summary>
    /// A specific version (1-40), or <c>null</c> (the default) to select the smallest
    /// version that holds the content.
    /// </summary>
    /// <remarks>
    /// Equivalent to the <c>requestedVersion</c> parameter, where <c>null</c> plays the
    /// role of <c>-1</c>.
    /// </remarks>
    public int? Version { get; init; }

    /// <summary>
    /// Quiet zone width in modules. Defaults to 4, the ISO/IEC 18004 value; 0 is valid and
    /// renders the symbol with no margin.
    /// </summary>
    public int QuietZoneSize
    {
        get => DefaultQuietZone + _quietZoneSizeOffset;
        init => _quietZoneSizeOffset = value - DefaultQuietZone;
    }
}
