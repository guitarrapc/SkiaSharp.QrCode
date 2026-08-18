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
    // rMQR ECI prefix for the assignments exposed by EciMode: 3-bit mode 111
    // followed by the one-byte assignment designator (3 or 26).
    private const int EciHeaderBits = RmQRConstants.ModeIndicatorLength + 8;

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

    /// <summary>
    /// Total bit count including the optional rMQR ECI prefix. The supported
    /// ISO-8859-1 and UTF-8 assignments both use the one-byte designator form.
    /// </summary>
    public static int GetRequiredBits(RmQRVersion version, EncodingMode mode, int dataLength, EciMode eciMode)
    {
        var headerBits = GetEciHeaderBits(eciMode) + RmQRConstants.ModeIndicatorLength + RmQRConstants.GetCountIndicatorLength(version, mode);
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

    /// <summary>Whether the data and optional ECI prefix fit the version at the ECC level.</summary>
    public static bool Fits(RmQRVersion version, RmQREccLevel eccLevel, EncodingMode mode, int dataLength, EciMode eciMode)
        => GetRequiredBits(version, mode, dataLength, eciMode) <= 8 * RmQRConstants.GetDataCodewordCount(version, eccLevel);

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
                    var groups = dataBits / 10;
                    var remainder = dataBits - groups * 10;
                    return groups * 3 + (remainder >= 7 ? 2 : remainder >= 4 ? 1 : 0);
                }
            case EncodingMode.Alphanumeric:
                {
                    var pairs = dataBits / 11;
                    var remainder = dataBits - pairs * 11;
                    return pairs * 2 + (remainder >= 6 ? 1 : 0);
                }
            default:
                return dataBits / 8;
        }
    }

    /// <summary>Largest data length after accounting for the optional ECI prefix.</summary>
    public static int GetMaxDataLength(RmQRVersion version, RmQREccLevel eccLevel, EncodingMode mode, EciMode eciMode)
    {
        var headerBits = GetEciHeaderBits(eciMode) + RmQRConstants.ModeIndicatorLength + RmQRConstants.GetCountIndicatorLength(version, mode);
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
        // Deliberately keep the pre-ECI hot path separate. Forwarding this overload
        // through the larger ECI selector measurably changes end-to-end JIT codegen.
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

        var capacities = FitCapacities[((RmQRConstants.GetModeIndex(mode) * 2 + (int)eccLevel) * 2) * 3 + (int)fitStrategy];
        var order = FitOrders[(int)fitStrategy];
        var heightMask = height is { } fitHeight ? FitHeightMasks[(int)fitStrategy][((int)fitHeight - 7) / 2] : uint.MaxValue;
        for (var j = 0; j < capacities.Length; j++)
        {
            if (capacities[j] >= dataLength && (heightMask & (1u << j)) != 0)
                return (RmQRVersion)order[j];
        }

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

    /// <summary>Selects a version while accounting for an optional ECI prefix.</summary>
    public static RmQRVersion Select(EncodingMode mode, int dataLength, EciMode eciMode, RmQREccLevel eccLevel, RmQRVersion? requestedVersion, RmQRFitStrategy fitStrategy, RmQRHeight? height)
    {
        _ = GetEciHeaderBits(eciMode); // validate before either requested/auto path
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
            if (!Fits(version, eccLevel, mode, dataLength, eciMode))
            {
                throw new ArgumentException(
                    $"Content is too long for rMQR {version} at ECC level {eccLevel}: {FormatDataLength(dataLength, mode)} in {mode} mode, " +
                    $"but the maximum is {FormatDataLength(GetMaxDataLength(version, eccLevel, mode, eciMode), mode)}. " +
                    "Shorten the content, lower the ECC level, choose a larger version, or use Standard QR (QRCodeGenerator) for longer content.",
                    nameof(requestedVersion));
            }

            return version;
        }

        // Candidate set: all versions, or those of the requested height; the best
        // fitting version by strategy. The versions are laid out best-first per
        // strategy with each version's capacity for the (mode, ECC) precomputed
        // (static tables built from GetMaxDataLength / IsBetter at type init), so the
        // fit is the first rank whose capacity holds the length and whose height is
        // allowed — the same result as scanning all 32 versions with Fits + IsBetter
        // (pinned by RmQRVersionSelectorUnitTest), at a fraction of the cost: the
        // scan was about a third of a small auto-fit encode.
        var eciIndex = eciMode == EciMode.Default ? 0 : 1;
        var capacities = FitCapacities[((RmQRConstants.GetModeIndex(mode) * 2 + (int)eccLevel) * 2 + eciIndex) * 3 + (int)fitStrategy];
        var order = FitOrders[(int)fitStrategy];
        var heightMask = height is { } fitHeight ? FitHeightMasks[(int)fitStrategy][((int)fitHeight - 7) / 2] : uint.MaxValue;
        for (var j = 0; j < capacities.Length; j++)
        {
            if (capacities[j] >= dataLength && (heightMask & (1u << j)) != 0)
                return (RmQRVersion)order[j];
        }

        // Nothing fits: find the most capacious candidate for the error message
        // (failure path only, the success path never pays for it).
        var largest = (RmQRVersion)0;
        var largestMax = -1;
        for (var i = 1; i <= RmQRConstants.VersionCount; i++)
        {
            var candidate = (RmQRVersion)i;
            if (height is { } wanted && RmQRConstants.GetHeight(candidate) != (int)wanted)
                continue;

            var candidateMax = GetMaxDataLength(candidate, eccLevel, mode, eciMode);
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

    // ---------------------------------------------------------------
    // Auto-fit tables (built once at type init from IsBetter / GetMaxDataLength):
    //   FitOrders[strategy][rank]                       = version (1..32), best first
    //   FitCapacities[((mode*2+ecc)*2+eci)*3+strategy]  = that version's max data length
    //   FitHeightMasks[strategy][(height-7)/2]          = bit `rank` set when the version has that height
    // 3 × 32 B + 36 × 64 B + 3 × 24 B ≈ 2.5 KB of table data. Declaration order matters:
    // static field initializers run textually, and the two lower tables index FitOrders.
    // ---------------------------------------------------------------
    private static readonly byte[][] FitOrders = BuildFitOrders();
    private static readonly ushort[][] FitCapacities = BuildFitCapacities();
    private static readonly uint[][] FitHeightMasks = BuildFitHeightMasks();

    private static byte[][] BuildFitOrders()
    {
        var orders = new byte[3][];
        for (var s = 0; s < 3; s++)
        {
            // insertion sort under the strict IsBetter comparator: it is a total order
            // (equal area and height means the same version), so the ranking is unique
            var sorted = new List<byte>(RmQRConstants.VersionCount);
            for (var v = 1; v <= RmQRConstants.VersionCount; v++)
            {
                var pos = 0;
                while (pos < sorted.Count && !IsBetter((RmQRVersion)v, (RmQRVersion)sorted[pos], (RmQRFitStrategy)s)) pos++;
                sorted.Insert(pos, (byte)v);
            }
            orders[s] = sorted.ToArray();
        }
        return orders;
    }

    private static ushort[][] BuildFitCapacities()
    {
        var tables = new ushort[RmQRConstants.ModeCount * 2 * 2 * 3][];
        foreach (var mode in new[] { EncodingMode.Numeric, EncodingMode.Alphanumeric, EncodingMode.Byte })
            for (var e = 0; e < 2; e++)
                for (var eci = 0; eci < 2; eci++)
                    for (var s = 0; s < 3; s++)
                    {
                        var t = new ushort[RmQRConstants.VersionCount];
                        var eciMode = eci == 0 ? EciMode.Default : EciMode.Utf8;
                        for (var rank = 0; rank < RmQRConstants.VersionCount; rank++)
                            t[rank] = (ushort)GetMaxDataLength((RmQRVersion)FitOrders[s][rank], (RmQREccLevel)e, mode, eciMode);
                        tables[((RmQRConstants.GetModeIndex(mode) * 2 + e) * 2 + eci) * 3 + s] = t; // same index function as Select
                    }
        return tables;
    }

    private static uint[][] BuildFitHeightMasks()
    {
        var masks = new uint[3][];
        for (var s = 0; s < 3; s++)
        {
            masks[s] = new uint[6];
            for (var rank = 0; rank < RmQRConstants.VersionCount; rank++)
                masks[s][(RmQRConstants.GetHeight((RmQRVersion)FitOrders[s][rank]) - 7) / 2] |= 1u << rank;
        }
        return masks;
    }

    /// <summary>Human unit per mode: Numeric counts digits, Alphanumeric characters, Byte encoded bytes.</summary>
    private static string FormatDataLength(int dataLength, EncodingMode mode) => mode switch
    {
        EncodingMode.Numeric => $"{dataLength} digits",
        EncodingMode.Alphanumeric => $"{dataLength} characters",
        _ => $"{dataLength} bytes",
    };

    private static int GetEciHeaderBits(EciMode eciMode) => eciMode switch
    {
        EciMode.Default => 0,
        EciMode.Iso8859_1 or EciMode.Utf8 => EciHeaderBits,
        _ => throw new ArgumentOutOfRangeException(nameof(eciMode), $"Unsupported ECI mode for rMQR: {eciMode}"),
    };
}
