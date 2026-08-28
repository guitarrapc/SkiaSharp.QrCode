namespace SkiaSharp.QrCode;

/// <summary>
/// Optional settings for <see cref="MicroQRCodeGenerator"/>. Every generator entry point
/// has an overload taking one of these instead of a parameter list, so a new option never
/// changes an existing signature.
/// </summary>
/// <remarks>
/// <para>
/// <c>default</c> is the complete default configuration, identical to what the parameter
/// list overloads apply when nothing is passed.
/// </para>
/// <code>
/// var data = MicroQRCodeGenerator.CreateMicroQRCode("12345", MicroQREccLevel.L,
///     new MicroQRCodeGeneratorOptions { Version = MicroQRVersion.M3, QuietZoneSize = 0 });
/// </code>
/// <para>
/// Micro QR has the smallest option set of the three symbologies: there is no ECI in Micro
/// QR, and no fit strategy because M1-M4 are totally ordered by capacity. It is still its
/// own type rather than a shared one, because <see cref="Version"/> is Micro QR typed and
/// <see cref="QuietZoneSize"/> has a Micro QR specific default.
/// </para>
/// </remarks>
public readonly record struct MicroQRCodeGeneratorOptions
{
    // Stored as an offset from the specified default, for the reasons recorded on
    // QRCodeGeneratorOptions.QuietZoneSize: default(T) has to mean 2, and 0 has to stay
    // expressible and distinct from unset.
    private readonly int _quietZoneSizeOffset;

    /// <summary>The default configuration, identical to <c>default</c>.</summary>
    public static MicroQRCodeGeneratorOptions Default => default;

    /// <summary>
    /// A specific version (M1-M4), or <c>null</c> (the default) to select the smallest
    /// version that holds the content.
    /// </summary>
    public MicroQRVersion? Version { get; init; }

    /// <summary>
    /// Quiet zone width in modules. Defaults to 2, the ISO/IEC 18004 value for Micro QR;
    /// 0 is valid and renders the symbol with no margin.
    /// </summary>
    public int QuietZoneSize
    {
        get => MicroQRCodeGenerator.DefaultQuietZone + _quietZoneSizeOffset;
        init => _quietZoneSizeOffset = value - MicroQRCodeGenerator.DefaultQuietZone;
    }
}
