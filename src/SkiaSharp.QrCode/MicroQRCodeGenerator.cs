using SkiaSharp.QrCode.Internals;
using SkiaSharp.QrCode.Internals.BinaryEncoders;
using SkiaSharp.QrCode.Internals.MicroQR;

namespace SkiaSharp.QrCode;

/// <summary>
/// Micro QR code generator based on ISO/IEC 18004 (versions M1-M4).
/// </summary>
/// <remarks>
/// <para>
/// Micro QR constraints enforced by this generator (they differ per version, so
/// invalid combinations throw instead of silently degrading):
/// </para>
/// <list type="bullet">
/// <item>M1: Numeric mode only, <see cref="MicroQREccLevel.ErrorDetectionOnly"/> only.</item>
/// <item>M2: Numeric/Alphanumeric, ECC L or M.</item>
/// <item>M3: Numeric/Alphanumeric/Byte, ECC L or M.</item>
/// <item>M4: Numeric/Alphanumeric/Byte, ECC L, M or Q.</item>
/// </list>
/// <para>
/// Micro QR has no ECI mode; text that is not ISO-8859-1-representable is encoded
/// as raw UTF-8 bytes in Byte mode. Kanji mode is not written;
/// <see cref="MicroQRCodeDecoder"/> does read the Kanji segments (M3 and M4) that
/// other encoders produce, so the two directions are deliberately asymmetric.
/// </para>
/// </remarks>
public static class MicroQRCodeGenerator
{
    private const int MaxCoreSize = 17;
    internal const int DefaultQuietZone = 2; // ISO/IEC 18004: Micro QR requires a 2-module quiet zone

    /// <summary>
    /// Creates a Micro QR code from the provided plain text.
    /// </summary>
    /// <param name="plainText">The text to encode.</param>
    /// <param name="eccLevel">Error correction level; must be valid for the (selected) version.</param>
    /// <param name="requestedVersion">Specific version (M1-M4), or null to select the smallest version that fits.</param>
    /// <param name="quietZoneSize">Quiet zone width in modules (Micro QR specification: 2).</param>
    /// <returns>A <see cref="MicroQRCodeData"/> containing the generated matrix.</returns>
    /// <exception cref="ArgumentException">Thrown when the data does not fit or the version/ECC/mode combination is invalid.</exception>
    public static MicroQRCodeData CreateMicroQRCode(string plainText, MicroQREccLevel eccLevel, MicroQRVersion? requestedVersion = null, int quietZoneSize = DefaultQuietZone)
        => CreateMicroQRCode(plainText.AsSpan(), eccLevel, requestedVersion, quietZoneSize);

    /// <inheritdoc cref="CreateMicroQRCode(string, MicroQREccLevel, MicroQRVersion?, int)"/>
    /// <param name="textSpan">The text span to encode.</param>
    public static MicroQRCodeData CreateMicroQRCode(ReadOnlySpan<char> textSpan, MicroQREccLevel eccLevel, MicroQRVersion? requestedVersion = null, int quietZoneSize = DefaultQuietZone)
    {
        ValidateQuietZone(quietZoneSize);
        var config = PrepareConfiguration(textSpan, eccLevel, requestedVersion);
        var size = MicroQRConstants.SizeFromVersion(config.Version);

        Span<byte> core = stackalloc byte[MaxCoreSize * MaxCoreSize];
        core = core.Slice(0, size * size);
        core.Clear();
        WriteCoreModules(textSpan, config, core, size);

        var result = new MicroQRCodeData(config.Version, quietZoneSize);
        result.SetCoreData(core);
        return result;
    }

    /// <summary>
    /// Creates a Micro QR code and writes the module matrix into the caller-provided
    /// buffer without per-call heap allocation.
    /// </summary>
    /// <remarks>
    /// Output format matches <see cref="QRCodeGenerator.CreateQrCode(ReadOnlySpan{char}, ECCLevel, Span{byte}, bool, EciMode, int, int)"/>:
    /// one byte per module (0 = light, 1 = dark), flat row-major, quiet zone included.
    /// Use <see cref="GetRequiredBufferSize"/> to size the destination.
    /// </remarks>
    /// <param name="textSpan">The text span to encode.</param>
    /// <param name="eccLevel">Error correction level; must be valid for the (selected) version.</param>
    /// <param name="destination">Destination buffer; at least <see cref="MicroQRCodeCalculatedSize.BufferSize"/> bytes.</param>
    /// <param name="requestedVersion">Specific version (M1-M4), or null for automatic selection.</param>
    /// <param name="quietZoneSize">Quiet zone width in modules.</param>
    /// <returns>The number of bytes written (always qrSize × qrSize).</returns>
    /// <exception cref="ArgumentException">Thrown when the destination is too small, the data does not fit, or the combination is invalid.</exception>
    public static int CreateMicroQRCode(ReadOnlySpan<char> textSpan, MicroQREccLevel eccLevel, Span<byte> destination, MicroQRVersion? requestedVersion = null, int quietZoneSize = DefaultQuietZone)
    {
        ValidateQuietZone(quietZoneSize);
        var config = PrepareConfiguration(textSpan, eccLevel, requestedVersion);
        var size = MicroQRConstants.SizeFromVersion(config.Version);
        var totalSize = size + quietZoneSize * 2;
        var requiredSize = totalSize * totalSize;
        if (destination.Length < requiredSize)
            throw new ArgumentException($"Destination buffer too small: {requiredSize} bytes required (version {config.Version}, {totalSize}x{totalSize} modules), got {destination.Length} bytes. Use {nameof(GetRequiredBufferSize)} to calculate the required size.", nameof(destination));

        var target = destination.Slice(0, requiredSize);
        target.Clear();

        if (quietZoneSize == 0)
        {
            WriteCoreModules(textSpan, config, target, size);
        }
        else
        {
            Span<byte> core = stackalloc byte[MaxCoreSize * MaxCoreSize];
            core = core.Slice(0, size * size);
            core.Clear();
            WriteCoreModules(textSpan, config, core, size);

            for (var row = 0; row < size; row++)
            {
                var destOffset = (row + quietZoneSize) * totalSize + quietZoneSize;
                core.Slice(row * size, size).CopyTo(target.Slice(destOffset, size));
            }
        }

        return requiredSize;
    }

    /// <summary>
    /// Calculates the required buffer size for encoding the specified text as a Micro QR code.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="eccLevel">Error correction level.</param>
    /// <param name="requestedVersion">Specific version (M1-M4), or null for automatic selection.</param>
    /// <param name="quietZoneSize">Quiet zone width in modules.</param>
    /// <exception cref="ArgumentException">Thrown when the data does not fit or the combination is invalid.</exception>
    public static MicroQRCodeCalculatedSize GetRequiredBufferSize(ReadOnlySpan<char> text, MicroQREccLevel eccLevel, MicroQRVersion? requestedVersion = null, int quietZoneSize = DefaultQuietZone)
    {
        ValidateQuietZone(quietZoneSize);
        var config = PrepareConfiguration(text, eccLevel, requestedVersion);
        var size = MicroQRConstants.SizeFromVersion(config.Version);
        var totalSize = size + quietZoneSize * 2;
        return new MicroQRCodeCalculatedSize(totalSize * totalSize, totalSize, config.Version);
    }

    /// <inheritdoc cref="GetRequiredBufferSize"/>
    /// <summary>
    /// Non-throwing <see cref="GetRequiredBufferSize"/>: <c>false</c> means the content
    /// does not fit, which here includes an encoding mode the version / ECC level does
    /// not offer, since the text is what picks the mode. Argument errors throw exactly
    /// as that overload raises them (rationale: specs/rmqr-encoder.md).
    /// </summary>
    /// <param name="size">Matrix size and version on success; <c>default</c> when the content does not fit.</param>
    /// <returns><c>true</c> when the content fits.</returns>
    public static bool TryGetRequiredBufferSize(ReadOnlySpan<char> text, MicroQREccLevel eccLevel, out MicroQRCodeCalculatedSize size, MicroQRVersion? requestedVersion = null, int quietZoneSize = DefaultQuietZone)
    {
        size = default;
        ValidateQuietZone(quietZoneSize);

        var analysis = TextAnalyzer.Analyze(text, EciMode.Default);
        if (!TrySelectVersion(in analysis, eccLevel, requestedVersion, out var version))
            return false;

        var totalSize = MicroQRConstants.SizeFromVersion(version) + quietZoneSize * 2;
        size = new MicroQRCodeCalculatedSize(totalSize * totalSize, totalSize, version);
        return true;
    }

    // ---- options overloads ---------------------------------------------------------
    //
    // Same operations, spelled with MicroQRCodeGeneratorOptions instead of a parameter
    // list. They unpack onto the overloads above rather than the other way round, so the
    // released overloads keep their exact exceptions, messages and codegen; nothing here
    // adds behaviour. `options` deliberately has no default value: with one,
    // CreateMicroQRCode(text, ecc) would be ambiguous between these and the released
    // overloads. See plans/generator-api-options-plan.md.

    /// <inheritdoc cref="CreateMicroQRCode(string, MicroQREccLevel, MicroQRVersion?, int)"/>
    /// <param name="plainText">The text to encode.</param>
    /// <param name="eccLevel">Error correction level; must be valid for the (selected) version.</param>
    /// <param name="options">Version and quiet zone settings. Pass <see cref="MicroQRCodeGeneratorOptions.Default"/> for the defaults.</param>
    public static MicroQRCodeData CreateMicroQRCode(string plainText, MicroQREccLevel eccLevel, in MicroQRCodeGeneratorOptions options)
        => CreateMicroQRCode(plainText.AsSpan(), eccLevel, options);

    /// <inheritdoc cref="CreateMicroQRCode(string, MicroQREccLevel, in MicroQRCodeGeneratorOptions)"/>
    /// <param name="textSpan">The text span to encode.</param>
    /// <param name="eccLevel">Error correction level; must be valid for the (selected) version.</param>
    /// <param name="options">Version and quiet zone settings.</param>
    public static MicroQRCodeData CreateMicroQRCode(ReadOnlySpan<char> textSpan, MicroQREccLevel eccLevel, in MicroQRCodeGeneratorOptions options)
        => CreateMicroQRCode(textSpan, eccLevel, ResolveVersion(textSpan, eccLevel, options), options.QuietZoneSize);

    /// <inheritdoc cref="CreateMicroQRCode(ReadOnlySpan{char}, MicroQREccLevel, Span{byte}, MicroQRVersion?, int)"/>
    /// <param name="textSpan">The text span to encode.</param>
    /// <param name="eccLevel">Error correction level; must be valid for the (selected) version.</param>
    /// <param name="destination">Destination buffer; at least <see cref="MicroQRCodeCalculatedSize.BufferSize"/> bytes.</param>
    /// <param name="options">Version and quiet zone settings. Size <paramref name="destination"/> with the same options.</param>
    public static int CreateMicroQRCode(ReadOnlySpan<char> textSpan, MicroQREccLevel eccLevel, Span<byte> destination, in MicroQRCodeGeneratorOptions options)
        => CreateMicroQRCode(textSpan, eccLevel, destination, ResolveVersion(textSpan, eccLevel, options), options.QuietZoneSize);

    /// <inheritdoc cref="GetRequiredBufferSize(ReadOnlySpan{char}, MicroQREccLevel, MicroQRVersion?, int)"/>
    /// <param name="text">The text to encode.</param>
    /// <param name="eccLevel">Error correction level.</param>
    /// <param name="options">Version and quiet zone settings.</param>
    public static MicroQRCodeCalculatedSize GetRequiredBufferSize(ReadOnlySpan<char> text, MicroQREccLevel eccLevel, in MicroQRCodeGeneratorOptions options)
        => GetRequiredBufferSize(text, eccLevel, ResolveVersion(text, eccLevel, options), options.QuietZoneSize);

    /// <inheritdoc cref="TryGetRequiredBufferSize(ReadOnlySpan{char}, MicroQREccLevel, out MicroQRCodeCalculatedSize, MicroQRVersion?, int)"/>
    /// <param name="text">The text to encode.</param>
    /// <param name="eccLevel">Error correction level.</param>
    /// <param name="size">Matrix size and version on success; <c>default</c> when the content does not fit.</param>
    /// <param name="options">Version and quiet zone settings.</param>
    public static bool TryGetRequiredBufferSize(ReadOnlySpan<char> text, MicroQREccLevel eccLevel, out MicroQRCodeCalculatedSize size, in MicroQRCodeGeneratorOptions options)
        => TryGetRequiredBufferSizeRanged(text, eccLevel, out size, options);

    private static void ValidateQuietZone(int quietZoneSize)
    {
        // 17 + 2·qz squared must stay far below int.MaxValue; 10000 modules of
        // quiet zone is already absurd, so a simple hard cap keeps the math safe.
        if (quietZoneSize < 0 || quietZoneSize > 10_000)
            throw new ArgumentOutOfRangeException(nameof(quietZoneSize), $"Quiet zone size must be 0-10000, got {quietZoneSize}");
    }

    /// <summary>
    /// Analyzes the text, selects/validates the version, and returns the encode configuration.
    /// </summary>
    private static MicroQRConfiguration PrepareConfiguration(ReadOnlySpan<char> textSpan, MicroQREccLevel eccLevel, MicroQRVersion? requestedVersion)
    {
        // Micro QR has no ECI, so analysis runs with the default charset rules;
        // for Byte mode the analyzer's DataLength is already the encoded byte
        // count (ISO-8859-1 char count or UTF-8 byte count).
        var analysis = TextAnalyzer.Analyze(textSpan, EciMode.Default);
        if (TrySelectVersion(in analysis, eccLevel, requestedVersion, out var version))
            return new MicroQRConfiguration(version, eccLevel, analysis.EncodingMode);

        throw NotFittingError(analysis.EncodingMode, analysis.DataLength, eccLevel, requestedVersion);
    }

    /// <summary>
    /// The version fit without the "does not fit" throw. Argument errors still throw:
    /// those hold of the arguments alone, independently of the text.
    /// </summary>
    internal static bool TrySelectVersion(in TextAnalysisResult analysis, MicroQREccLevel eccLevel, MicroQRVersion? requestedVersion, out MicroQRVersion selected)
    {
        if ((uint)eccLevel > (uint)MicroQREccLevel.Q)
            throw new ArgumentOutOfRangeException(nameof(eccLevel), $"Invalid Micro QR ECC level: {eccLevel}");

        var mode = analysis.EncodingMode;
        var dataLength = analysis.DataLength;
        selected = default;

        if (requestedVersion is { } version)
        {
            if ((uint)((int)version - 1) > 3)
                throw new ArgumentOutOfRangeException(nameof(requestedVersion), $"Invalid Micro QR version: {version}");
            if (!MicroQRConstants.IsValidCombination(version, eccLevel))
                throw new ArgumentException($"ECC level {eccLevel} is not valid for Micro QR version {version} (M1: ErrorDetectionOnly; M2/M3: L, M; M4: L, M, Q).", nameof(eccLevel));
            if (!MicroQRConstants.IsModeSupported(version, mode))
                return false;
            if (GetRequiredBits(version, mode, dataLength) > MicroQRConstants.GetDataBitCapacity(version, eccLevel))
                return false;

            selected = version;
            return true;
        }

        for (var candidate = MicroQRVersion.M1; candidate <= MicroQRVersion.M4; candidate++)
        {
            if (!MicroQRConstants.IsValidCombination(candidate, eccLevel) || !MicroQRConstants.IsModeSupported(candidate, mode))
                continue;
            if (GetRequiredBits(candidate, mode, dataLength) <= MicroQRConstants.GetDataBitCapacity(candidate, eccLevel))
            {
                selected = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The smallest version inside <paramref name="range"/> that holds the content, or
    /// <c>false</c> when none does.
    /// </summary>
    /// <remarks>
    /// A range can rule out every version for two different reasons, and they are not the
    /// same kind of answer. If no version in the range offers <paramref name="eccLevel"/>
    /// at all, the arguments contradict each other whatever the content is, so that throws
    /// exactly as pinning such a version does. If versions are available but none carries
    /// the mode the text requires, or none is long enough, that is an ordinary "does not
    /// fit" and returns <c>false</c>, because the text is what picks the mode.
    /// </remarks>
    internal static bool TrySelectVersionInRange(in TextAnalysisResult analysis, MicroQREccLevel eccLevel, MicroQRVersionRange range, out MicroQRVersion selected)
    {
        if ((uint)eccLevel > (uint)MicroQREccLevel.Q)
            throw new ArgumentOutOfRangeException(nameof(eccLevel), $"Invalid Micro QR ECC level: {eccLevel}");

        selected = default;
        var mode = analysis.EncodingMode;
        var anyValidCombination = false;

        for (var candidate = range.Min; candidate <= range.Max; candidate++)
        {
            if (!MicroQRConstants.IsValidCombination(candidate, eccLevel))
                continue;

            anyValidCombination = true;
            if (!MicroQRConstants.IsModeSupported(candidate, mode))
                continue;
            if (GetRequiredBits(candidate, mode, analysis.DataLength) <= MicroQRConstants.GetDataBitCapacity(candidate, eccLevel))
            {
                selected = candidate;
                return true;
            }
        }

        if (!anyValidCombination)
        {
            throw new ArgumentException(
                $"ECC level {eccLevel} is not available on any Micro QR version in {range} (M1: ErrorDetectionOnly; M2/M3: L, M; M4: L, M, Q).",
                nameof(eccLevel));
        }

        return false;
    }

    /// <summary>
    /// The version a ranged option set resolves to, or <c>null</c> when the range is
    /// unconstrained so the parameter list overloads can select as they always have.
    /// </summary>
    private static bool TryGetRequiredBufferSizeRanged(ReadOnlySpan<char> text, MicroQREccLevel eccLevel, out MicroQRCodeCalculatedSize size, in MicroQRCodeGeneratorOptions options)
    {
        if (options.Version.IsAny)
            return TryGetRequiredBufferSize(text, eccLevel, out size, requestedVersion: null, options.QuietZoneSize);

        size = default;
        ValidateQuietZone(options.QuietZoneSize);

        var analysis = TextAnalyzer.Analyze(text, EciMode.Default);
        if (!TrySelectVersionInRange(in analysis, eccLevel, options.Version, out var version))
            return false;

        var totalSize = MicroQRConstants.SizeFromVersion(version) + options.QuietZoneSize * 2;
        size = new MicroQRCodeCalculatedSize(totalSize * totalSize, totalSize, version);
        return true;
    }

    private static MicroQRVersion? ResolveVersion(ReadOnlySpan<char> textSpan, MicroQREccLevel eccLevel, in MicroQRCodeGeneratorOptions options)
    {
        if (options.Version.IsAny)
            return null;

        var analysis = TextAnalyzer.Analyze(textSpan, EciMode.Default);
        if (!TrySelectVersionInRange(in analysis, eccLevel, options.Version, out var version))
            throw NotFittingError(analysis.EncodingMode, analysis.DataLength, eccLevel, options.Version.IsExact ? options.Version.Min : null);

        return version;
    }

    /// <summary>
    /// The actionable "does not fit" error, built off the success path: which constraint
    /// binds (mode availability versus length) and what the applicable maximum is.
    /// </summary>
    private static ArgumentException NotFittingError(EncodingMode mode, int dataLength, MicroQREccLevel eccLevel, MicroQRVersion? requestedVersion)
    {
        if (requestedVersion is { } version)
        {
            if (!MicroQRConstants.IsModeSupported(version, mode))
                return new ArgumentException($"Encoding mode {mode} is not available on Micro QR version {version} (M1: Numeric; M2: +Alphanumeric; M3/M4: +Byte).", nameof(requestedVersion));

            return new ArgumentException(
                $"Content is too long for Micro QR {version} at ECC level {eccLevel}: {FormatDataLength(dataLength, mode)} in {mode} mode, " +
                $"but the maximum is {FormatDataLength(GetMaxDataLength(version, eccLevel, mode), mode)}. " +
                "Shorten the content, lower the ECC level, or use Standard QR (QRCodeGenerator) for longer content.",
                nameof(requestedVersion));
        }

        var bestMax = -1;
        var bestVersion = MicroQRVersion.M1;
        for (var candidate = MicroQRVersion.M1; candidate <= MicroQRVersion.M4; candidate++)
        {
            if (!MicroQRConstants.IsValidCombination(candidate, eccLevel) || !MicroQRConstants.IsModeSupported(candidate, mode))
                continue;

            var candidateMax = GetMaxDataLength(candidate, eccLevel, mode);
            if (candidateMax > bestMax)
            {
                bestMax = candidateMax;
                bestVersion = candidate;
            }
        }

        // No version supports this mode/ECC combination at any length, a constraint
        // problem, not a length problem; say which constraint binds.
        if (bestMax < 0)
        {
            return new ArgumentException(
                $"Micro QR cannot encode {mode} mode at ECC level {eccLevel}: {nameof(MicroQREccLevel.ErrorDetectionOnly)} limits the symbol to M1 " +
                "(Numeric only, 5 digits); Alphanumeric requires M2+, Byte requires M3+, and level Q requires M4. " +
                "Choose another ECC level or use Standard QR (QRCodeGenerator).");
        }

        return new ArgumentException(
            $"Content is too long for Micro QR: {FormatDataLength(dataLength, mode)} in {mode} mode, " +
            $"but ECC level {eccLevel} fits at most {FormatDataLength(bestMax, mode)} ({bestVersion}). " +
            "Shorten the content, lower the ECC level, or use Standard QR (QRCodeGenerator) for longer content.");
    }

    /// <summary>Human unit per mode: Numeric counts digits, Alphanumeric characters, Byte encoded bytes (UTF-8 for non-Latin-1 text).</summary>
    private static string FormatDataLength(int dataLength, EncodingMode mode) => mode switch
    {
        EncodingMode.Numeric => $"{dataLength} digits",
        EncodingMode.Alphanumeric => $"{dataLength} characters",
        _ => $"{dataLength} bytes",
    };

    /// <summary>
    /// Largest data length that fits a version/ECC/mode combination, the inverse of
    /// <see cref="GetRequiredBits"/> against the ISO Table 7 bit capacity. Error-path
    /// only (capacity-exceeded messages).
    /// </summary>
    private static int GetMaxDataLength(MicroQRVersion version, MicroQREccLevel eccLevel, EncodingMode mode)
    {
        var headerBits = MicroQRConstants.GetModeIndicatorLength(version) + MicroQRConstants.GetCountIndicatorLength(version, mode);
        var dataBits = MicroQRConstants.GetDataBitCapacity(version, eccLevel) - headerBits;
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
    /// Total bit count for the header plus data (ISO/IEC 18004 Micro QR segment
    /// sizes). The character count indicator range never binds below the bit
    /// capacity for any version/mode, so no separate range check is needed.
    /// </summary>
    /// <remarks>
    /// Returns <see cref="long"/>: Byte mode costs <c>8 × dataLength</c>, which wraps
    /// <see cref="int"/> for a span past ~268M bytes and would read as a fit. Widening
    /// keeps the comparison honest without an early return that would skip the argument
    /// validation around it.
    /// </remarks>
    private static long GetRequiredBits(MicroQRVersion version, EncodingMode mode, int dataLength)
    {
        long headerBits = MicroQRConstants.GetModeIndicatorLength(version) + MicroQRConstants.GetCountIndicatorLength(version, mode);
        var dataBits = mode switch
        {
            EncodingMode.Numeric => dataLength / 3 * 10L + (dataLength % 3) switch { 2 => 7, 1 => 4, _ => 0 },
            EncodingMode.Alphanumeric => dataLength / 2 * 11L + dataLength % 2 * 6,
            EncodingMode.Byte => dataLength * 8L,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), $"Encoding mode {mode} is not supported by Micro QR."),
        };
        return headerBits + dataBits;
    }

    /// <summary>
    /// Runs the encode → ECC → placement → masking → format pipeline into a zeroed
    /// byte-per-module core buffer. Allocation-free: all intermediates are stackalloc.
    /// </summary>
    private static void WriteCoreModules(ReadOnlySpan<char> textSpan, in MicroQRConfiguration config, Span<byte> core, int size)
    {
        var eccCount = MicroQRConstants.GetEccCodewordCount(config.Version, config.EccLevel);
        var dataBitCount = MicroQRConstants.GetDataBitCapacity(config.Version, config.EccLevel);

        Span<byte> dataCodewords = stackalloc byte[16]; // max data codewords (M4-L)
        var dataCount = MicroQRBinaryEncoder.EncodeDataCodewords(textSpan, config.Version, config.EccLevel, config.Mode, dataCodewords);

        // Reed-Solomon over the data codeword bytes as-is; a final half codeword
        // (M1/M3) participates as its high-nibble byte value.
        Span<byte> eccCodewords = stackalloc byte[14]; // max ECC codewords (M4-Q)
        EccBinaryEncoder.CalculateECC(dataCodewords.Slice(0, dataCount), eccCodewords, eccCount);

        MicroQRModulePlacer.PlaceSymbol(core, size, dataCodewords.Slice(0, dataCount), eccCodewords.Slice(0, eccCount), dataBitCount, config.Version, config.EccLevel);
    }

    private readonly record struct MicroQRConfiguration(MicroQRVersion Version, MicroQREccLevel EccLevel, EncodingMode Mode);
}
