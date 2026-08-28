namespace SkiaSharp.QrCode;

/// <summary>
/// Optional settings for <see cref="MicroQRCodeGenerator"/>. <c>default</c> is the complete
/// default configuration:
/// <c>new MicroQRCodeGeneratorOptions { Version = MicroQRVersion.M3, QuietZoneSize = 0 }</c>.
/// </summary>
/// <remarks>
/// The smallest option set of the three symbologies: Micro QR has no ECI, and no fit
/// strategy because M1-M4 are totally ordered by capacity.
/// </remarks>
public readonly record struct MicroQRCodeGeneratorOptions
{
    // Offset from the specified default, for the reasons on QRCodeGeneratorOptions.QuietZoneSize.
    private readonly int _quietZoneSizeOffset;

    /// <summary>The default configuration, identical to <c>default</c>.</summary>
    public static MicroQRCodeGeneratorOptions Default => default;

    /// <summary>
    /// The versions the generator may choose from. Defaults to
    /// <see cref="MicroQRVersionRange.Any"/>; a <see cref="MicroQRVersion"/> or its
    /// nullable converts implicitly, so a <c>null</c> means automatic.
    /// </summary>
    public MicroQRVersionRange Version { get; init; }

    /// <summary>
    /// Quiet zone width in modules. Defaults to 2, the ISO/IEC 18004 value for Micro QR;
    /// 0 is valid.
    /// </summary>
    public int QuietZoneSize
    {
        get => MicroQRCodeGenerator.DefaultQuietZone + _quietZoneSizeOffset;
        init => _quietZoneSizeOffset = value - MicroQRCodeGenerator.DefaultQuietZone;
    }
}
