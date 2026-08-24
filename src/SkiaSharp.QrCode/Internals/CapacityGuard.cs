namespace SkiaSharp.QrCode.Internals;

/// <summary>
/// Guard shared by the three version selectors against <see cref="int"/> overflow in
/// capacity pricing.
/// </summary>
internal static class CapacityGuard
{
    /// <summary>
    /// Longest data length the fit arithmetic can price without wrapping. Byte mode costs
    /// <c>8 × length</c> bits plus a header, so anything past <c>int.MaxValue / 8</c> (less
    /// room for the header) would wrap negative and read as a fit. Rejecting above this is
    /// exact rather than conservative: the largest symbol of any symbology holds 7,089
    /// units, so nothing near this bound could fit anyway.
    /// </summary>
    public const int MaxPriceableDataLength = (int.MaxValue / 8) - 64;
}
