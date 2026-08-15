namespace SkiaSharp.QrCode.Internals.RmQr;

/// <summary>
/// rMQR version fit (ISO/IEC 23941 capacities, design record
/// specs/rmqr-encoder.md): exact requested version, or the best of the versions
/// that hold the content according to an <see cref="RmQRFitStrategy"/>, optionally
/// restricted to one <see cref="RmQRHeight"/>. Also owns the capacity arithmetic
/// (required bits per mode, its inverse for error messages).
/// </summary>
internal static class RmQRVersionSelector
{
    /// <summary>
    /// Total bit count for header (3-bit mode + count indicator) plus data. The
    /// count indicator range never binds below the bit capacity for any
    /// version/mode (verified by RmQRConstantsUnitTest), so no range check is needed.
    /// </summary>
    public static int GetRequiredBits(RmQRVersion version, EncodingMode mode, int dataLength)
    {
        var headerBits = RmQRConstants.ModeIndicatorLength + RmQRConstants.GetCountIndicatorLength(version, mode);
        var dataBits = mode switch
        {
            EncodingMode.Numeric => dataLength / 3 * 10 + (dataLength % 3) switch { 2 => 7, 1 => 4, _ => 0 },
            EncodingMode.Alphanumeric => dataLength / 2 * 11 + dataLength % 2 * 6,
            EncodingMode.Byte => dataLength * 8,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), $"Encoding mode {mode} is not supported by rMQR."),
        };
        return headerBits + dataBits;
    }

    /// <summary>Whether <paramref name="dataLength"/> units of <paramref name="mode"/> fit the version at the ECC level.</summary>
    public static bool Fits(RmQRVersion version, RmQREccLevel eccLevel, EncodingMode mode, int dataLength)
        => GetRequiredBits(version, mode, dataLength) <= 8 * RmQRConstants.GetDataCodewordCount(version, eccLevel);

    /// <summary>
    /// Largest data length (digits / characters / bytes) that fits a version × ECC × mode,
    /// the inverse of <see cref="GetRequiredBits"/> against the data bit capacity.
    /// </summary>
    public static int GetMaxDataLength(RmQRVersion version, RmQREccLevel eccLevel, EncodingMode mode)
    {
        var headerBits = RmQRConstants.ModeIndicatorLength + RmQRConstants.GetCountIndicatorLength(version, mode);
        var dataBits = 8 * RmQRConstants.GetDataCodewordCount(version, eccLevel) - headerBits;
        if (dataBits <= 0)
            return 0;

        switch (mode)
        {
            case EncodingMode.Numeric:
                {
                    // 10 bits per 3-digit group; a 2-digit tail costs 7 bits, 1 digit costs 4
                    var groups = dataBits / 10;
                    var remainder = dataBits - groups * 10;
                    return groups * 3 + (remainder >= 7 ? 2 : remainder >= 4 ? 1 : 0);
                }
            case EncodingMode.Alphanumeric:
                {
                    // 11 bits per character pair; a single tail character costs 6 bits
                    var pairs = dataBits / 11;
                    var remainder = dataBits - pairs * 11;
                    return pairs * 2 + (remainder >= 6 ? 1 : 0);
                }
            default:
                return dataBits / 8;
        }
    }

    /// <summary>
    /// Fit-strategy comparator: true when <paramref name="candidate"/> should replace
    /// <paramref name="incumbent"/>. MinimizeArea: fewer modules, ties → smaller
    /// height; MinimizeWidth: smaller width, ties → smaller height; MinimizeHeight:
    /// smaller height, ties → smaller width. Equal versions never replace each other.
    /// </summary>
    public static bool IsBetter(RmQRVersion candidate, RmQRVersion incumbent, RmQRFitStrategy strategy)
    {
        var ch = RmQRConstants.GetHeight(candidate);
        var cw = RmQRConstants.GetWidth(candidate);
        var ih = RmQRConstants.GetHeight(incumbent);
        var iw = RmQRConstants.GetWidth(incumbent);

        switch (strategy)
        {
            case RmQRFitStrategy.MinimizeArea:
                {
                    var ca = ch * cw;
                    var ia = ih * iw;
                    return ca != ia ? ca < ia : ch < ih;
                }
            case RmQRFitStrategy.MinimizeWidth:
                return cw != iw ? cw < iw : ch < ih;
            case RmQRFitStrategy.MinimizeHeight:
                return ch != ih ? ch < ih : cw < iw;
            default:
                throw new ArgumentOutOfRangeException(nameof(strategy), $"Invalid rMQR fit strategy: {strategy}");
        }
    }

    /// <summary>
    /// Selects the version for the analyzed content. Validates every enum argument;
    /// throws <see cref="ArgumentException"/> with an actionable message (actual
    /// length, applicable maximum in mode units, remedy) when the content does not fit.
    /// </summary>
    public static RmQRVersion Select(EncodingMode mode, int dataLength, RmQREccLevel eccLevel, RmQRVersion? requestedVersion, RmQRFitStrategy fitStrategy, RmQRHeight? height)
    {
        if (!RmQRConstants.IsValidEccLevel(eccLevel))
            throw new ArgumentOutOfRangeException(nameof(eccLevel), $"Invalid rMQR ECC level: {eccLevel}");
        if (fitStrategy is < RmQRFitStrategy.MinimizeArea or > RmQRFitStrategy.MinimizeHeight)
            throw new ArgumentOutOfRangeException(nameof(fitStrategy), $"Invalid rMQR fit strategy: {fitStrategy}");
        if (height is { } h && h is not (RmQRHeight.H7 or RmQRHeight.H9 or RmQRHeight.H11 or RmQRHeight.H13 or RmQRHeight.H15 or RmQRHeight.H17))
            throw new ArgumentOutOfRangeException(nameof(height), $"Invalid rMQR height: {height}");

        if (requestedVersion is { } version)
        {
            if (!RmQRConstants.IsValidVersion(version))
                throw new ArgumentOutOfRangeException(nameof(requestedVersion), $"Invalid rMQR version: {version}");
            if (height is { } requiredHeight && RmQRConstants.GetHeight(version) != (int)requiredHeight)
                throw new ArgumentException($"Requested rMQR version {version} is {RmQRConstants.GetHeight(version)} modules high, but height {requiredHeight} was requested. Specify one or the other, or make them agree.", nameof(height));
            if (!Fits(version, eccLevel, mode, dataLength))
            {
                throw new ArgumentException(
                    $"Content is too long for rMQR {version} at ECC level {eccLevel}: {FormatDataLength(dataLength, mode)} in {mode} mode, " +
                    $"but the maximum is {FormatDataLength(GetMaxDataLength(version, eccLevel, mode), mode)}. " +
                    "Shorten the content, lower the ECC level, choose a larger version, or use Standard QR (QRCodeGenerator) for longer content.",
                    nameof(requestedVersion));
            }

            return version;
        }

        // Candidate set: all versions, or those of the requested height. Track the
        // best fitting version by strategy.
        var best = (RmQRVersion)0;
        for (var i = 1; i <= RmQRConstants.VersionCount; i++)
        {
            var candidate = (RmQRVersion)i;
            if (height is { } wanted && RmQRConstants.GetHeight(candidate) != (int)wanted)
                continue;

            if (Fits(candidate, eccLevel, mode, dataLength) && (best == 0 || IsBetter(candidate, best, fitStrategy)))
                best = candidate;
        }

        if (best != 0)
            return best;

        // Nothing fits: find the most capacious candidate for the error message
        // (failure path only, the success path never pays for it).
        var largest = (RmQRVersion)0;
        var largestMax = -1;
        for (var i = 1; i <= RmQRConstants.VersionCount; i++)
        {
            var candidate = (RmQRVersion)i;
            if (height is { } wanted && RmQRConstants.GetHeight(candidate) != (int)wanted)
                continue;

            var candidateMax = GetMaxDataLength(candidate, eccLevel, mode);
            if (candidateMax > largestMax)
            {
                largestMax = candidateMax;
                largest = candidate;
            }
        }

        var scope = height is { } hh ? $"rMQR height {hh}" : "rMQR";
        throw new ArgumentException(
            $"Content is too long for {scope}: {FormatDataLength(dataLength, mode)} in {mode} mode, " +
            $"but ECC level {eccLevel} fits at most {FormatDataLength(largestMax, mode)} ({largest}). " +
            (height is null
                ? "Shorten the content, lower the ECC level, or use Standard QR (QRCodeGenerator) for longer content."
                : "Shorten the content, lower the ECC level, allow a taller symbol, or use Standard QR (QRCodeGenerator) for longer content."));
    }

    /// <summary>Human unit per mode: Numeric counts digits, Alphanumeric characters, Byte encoded bytes.</summary>
    private static string FormatDataLength(int dataLength, EncodingMode mode) => mode switch
    {
        EncodingMode.Numeric => $"{dataLength} digits",
        EncodingMode.Alphanumeric => $"{dataLength} characters",
        _ => $"{dataLength} bytes",
    };
}
