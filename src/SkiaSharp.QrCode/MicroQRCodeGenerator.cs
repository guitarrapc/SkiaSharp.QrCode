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
/// as raw UTF-8 bytes in Byte mode. Kanji mode is not implemented.
/// </para>
/// </remarks>
public static class MicroQRCodeGenerator
{
    private const int MaxCoreSize = 17;
    private const int DefaultQuietZone = 2; // ISO/IEC 18004: Micro QR requires a 2-module quiet zone

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
    /// buffer without heap allocation.
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
        if ((uint)eccLevel > (uint)MicroQREccLevel.Q)
            throw new ArgumentOutOfRangeException(nameof(eccLevel), $"Invalid Micro QR ECC level: {eccLevel}");

        // Micro QR has no ECI, so analysis runs with the default charset rules;
        // for Byte mode the analyzer's DataLength is already the encoded byte
        // count (ISO-8859-1 char count or UTF-8 byte count).
        var analysis = TextAnalyzer.Analyze(textSpan, EciMode.Default);
        var mode = analysis.EncodingMode;
        var dataLength = analysis.DataLength;

        if (requestedVersion is { } version)
        {
            if ((uint)((int)version - 1) > 3)
                throw new ArgumentOutOfRangeException(nameof(requestedVersion), $"Invalid Micro QR version: {version}");
            if (!MicroQRConstants.IsValidCombination(version, eccLevel))
                throw new ArgumentException($"ECC level {eccLevel} is not valid for Micro QR version {version} (M1: ErrorDetectionOnly; M2/M3: L, M; M4: L, M, Q).", nameof(eccLevel));
            if (!MicroQRConstants.IsModeSupported(version, mode))
                throw new ArgumentException($"Encoding mode {mode} is not available on Micro QR version {version} (M1: Numeric; M2: +Alphanumeric; M3/M4: +Byte).", nameof(requestedVersion));
            if (GetRequiredBits(version, mode, dataLength) > MicroQRConstants.GetDataBitCapacity(version, eccLevel))
            {
                throw new ArgumentException(
                    $"Content is too long for Micro QR {version} at ECC level {eccLevel}: {FormatDataLength(dataLength, mode)} in {mode} mode, " +
                    $"but the maximum is {FormatDataLength(GetMaxDataLength(version, eccLevel, mode), mode)}. " +
                    "Shorten the content, lower the ECC level, or use Standard QR (QRCodeGenerator) for longer content.",
                    nameof(requestedVersion));
            }

            return new MicroQRConfiguration(version, eccLevel, mode);
        }

        var bestMax = -1;
        var bestVersion = MicroQRVersion.M1;
        for (var candidate = MicroQRVersion.M1; candidate <= MicroQRVersion.M4; candidate++)
        {
            if (!MicroQRConstants.IsValidCombination(candidate, eccLevel) || !MicroQRConstants.IsModeSupported(candidate, mode))
                continue;
            if (GetRequiredBits(candidate, mode, dataLength) <= MicroQRConstants.GetDataBitCapacity(candidate, eccLevel))
                return new MicroQRConfiguration(candidate, eccLevel, mode);

            var candidateMax = GetMaxDataLength(candidate, eccLevel, mode);
            if (candidateMax > bestMax)
            {
                bestMax = candidateMax;
                bestVersion = candidate;
            }
        }

        // No version supports this mode/ECC combination at any length — a constraint
        // problem, not a length problem; say which constraint binds.
        if (bestMax < 0)
        {
            throw new ArgumentException(
                $"Micro QR cannot encode {mode} mode at ECC level {eccLevel}: {nameof(MicroQREccLevel.ErrorDetectionOnly)} limits the symbol to M1 " +
                "(Numeric only, 5 digits); Alphanumeric requires M2+, Byte requires M3+, and level Q requires M4. " +
                "Choose another ECC level or use Standard QR (QRCodeGenerator).");
        }

        throw new ArgumentException(
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
    /// Largest data length that fits a version/ECC/mode combination — the inverse of
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
    private static int GetRequiredBits(MicroQRVersion version, EncodingMode mode, int dataLength)
    {
        var headerBits = MicroQRConstants.GetModeIndicatorLength(version) + MicroQRConstants.GetCountIndicatorLength(version, mode);
        var dataBits = mode switch
        {
            EncodingMode.Numeric => dataLength / 3 * 10 + (dataLength % 3) switch { 2 => 7, 1 => 4, _ => 0 },
            EncodingMode.Alphanumeric => dataLength / 2 * 11 + dataLength % 2 * 6,
            EncodingMode.Byte => dataLength * 8,
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
