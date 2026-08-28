using SkiaSharp.QrCode.Internals.RmQr;

namespace SkiaSharp.QrCode;

/// <summary>
/// Optional settings for <see cref="RmQRCodeGenerator"/>. Every generator entry point
/// takes one of these instead of a parameter list, so a new option never changes an
/// existing signature.
/// </summary>
/// <remarks>
/// <para>
/// <c>default</c> is the complete default configuration and is what an omitted argument
/// sends, so the shortest correct call is <c>CreateRmQRCode(text, eccLevel)</c>. Set only
/// what you need, with an object initializer or <c>with</c>:
/// </para>
/// <code>
/// var data = RmQRCodeGenerator.CreateRmQRCode("https://example.com", RmQREccLevel.M,
///     new RmQRCodeGeneratorOptions { Height = RmQRHeight.H9, Segmentation = RmQRSegmentation.Optimal });
/// </code>
/// <para>
/// This is an rMQR-specific type rather than one shared across symbologies: the three
/// generators agree on almost nothing. <see cref="Version"/> is a different type in each,
/// <see cref="QuietZoneSize"/> has a different specified default in each, and
/// <see cref="FitStrategy"/>, <see cref="Height"/> and <see cref="Segmentation"/> have no
/// meaning outside rMQR. A shared type would have to accept members that are invalid for
/// two thirds of its uses and reject them at run time.
/// </para>
/// </remarks>
public readonly record struct RmQRCodeGeneratorOptions
{
    // Quiet zone is stored as an offset from the specified default so that default(T)
    // carries the ISO/IEC 23941 value of 2 rather than 0, which is a legitimate caller
    // choice and therefore cannot double as "unset".
    //
    // An offset rather than a value+1 sentinel, because the offset makes the canonical
    // form unique: writing 2 explicitly produces the same field value as not writing it,
    // so the generated equality does not report two identical option sets as different.
    private readonly int _quietZoneSizeOffset;

    /// <summary>The default configuration, identical to <c>default</c>.</summary>
    public static RmQRCodeGeneratorOptions Default => default;

    /// <summary>
    /// Character encoding declaration. The default auto-detects ASCII (no ECI),
    /// ISO-8859-1 (assignment 3) or UTF-8 (assignment 26) from the content.
    /// </summary>
    /// <remarks>
    /// Only <see cref="EciMode.Default"/>, <see cref="EciMode.Iso8859_1"/> and
    /// <see cref="EciMode.Utf8"/> are accepted; anything else throws. Declaring
    /// <see cref="EciMode.Iso8859_1"/> over content that Latin-1 cannot represent also
    /// throws, rather than silently re-encoding it.
    /// </remarks>
    public EciMode EciMode { get; init; }

    /// <summary>
    /// A specific symbol version, or <c>null</c> (the default) to fit one automatically
    /// by <see cref="FitStrategy"/> and <see cref="Height"/>.
    /// </summary>
    public RmQRVersion? Version { get; init; }

    /// <summary>
    /// How to choose among the versions that hold the content when <see cref="Version"/>
    /// is <c>null</c>. Defaults to <see cref="RmQRFitStrategy.MinimizeArea"/>, the choice
    /// both reference encoders make.
    /// </summary>
    public RmQRFitStrategy FitStrategy { get; init; }

    /// <summary>
    /// Restrict automatic fitting to one symbol height ("fixed height, automatic width"),
    /// or <c>null</c> (the default) to consider every height. Must agree with
    /// <see cref="Version"/> when both are set.
    /// </summary>
    public RmQRHeight? Height { get; init; }

    /// <summary>
    /// Quiet zone width in modules. Defaults to 2, the ISO/IEC 23941 value; 0 is valid
    /// and renders the symbol with no margin.
    /// </summary>
    public int QuietZoneSize
    {
        get => RmQRConstants.QuietZoneModules + _quietZoneSizeOffset;
        init => _quietZoneSizeOffset = value - RmQRConstants.QuietZoneModules;
    }

    /// <summary>
    /// Whether to split the content into mixed-mode segments. Defaults to
    /// <see cref="RmQRSegmentation.Single"/>.
    /// </summary>
    /// <remarks>
    /// Size a destination buffer with the same value you encode with: the two modes can
    /// select different versions.
    /// </remarks>
    public RmQRSegmentation Segmentation { get; init; }
}
