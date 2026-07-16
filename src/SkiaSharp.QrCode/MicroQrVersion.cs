namespace SkiaSharp.QrCode;

/// <summary>
/// Micro QR Code symbol version (ISO/IEC 18004). Determines symbol size:
/// M1 = 11×11, M2 = 13×13, M3 = 15×15, M4 = 17×17 modules.
/// </summary>
public enum MicroQrVersion
{
    /// <summary>11×11 modules. Numeric mode only, error detection only.</summary>
    M1 = 1,

    /// <summary>13×13 modules. Numeric and Alphanumeric modes, ECC L/M.</summary>
    M2 = 2,

    /// <summary>15×15 modules. Numeric, Alphanumeric and Byte modes, ECC L/M.</summary>
    M3 = 3,

    /// <summary>17×17 modules. Numeric, Alphanumeric and Byte modes, ECC L/M/Q.</summary>
    M4 = 4,
}
