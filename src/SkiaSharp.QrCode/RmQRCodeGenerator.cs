using System.Buffers;
using System.Diagnostics;
using SkiaSharp.QrCode.Internals;
using SkiaSharp.QrCode.Internals.RmQr;

namespace SkiaSharp.QrCode;

/// <summary>
/// rMQR Code (ISO/IEC 23941, R7x43-R17x139) generator: text → rectangular module
/// matrix. Sibling of <see cref="QRCodeGenerator"/> and <see cref="MicroQRCodeGenerator"/>
/// with rMQR-typed version, ECC and fit parameters.
/// </summary>
/// <remarks>
/// <para>
/// Version selection: an exact <see cref="RmQRVersion"/>, or automatic fit among
/// the versions that hold the content by <see cref="RmQRFitStrategy"/> (default
/// <see cref="RmQRFitStrategy.MinimizeArea"/>: fewest modules, the choice both
/// reference encoders make; note it can prefer a taller, narrower symbol, e.g.
/// 12 digits at M give R11x27 (297 modules) rather than R7x43 (301); use
/// <see cref="RmQRFitStrategy.MinimizeHeight"/> or a fixed <see cref="RmQRHeight"/>
/// for the flattest symbol), optionally restricted to one height.
/// </para>
/// <para>
/// Modes: Numeric, Alphanumeric and Byte. Byte mode emits ECI assignment 3 for
/// ISO-8859-1 and assignment 26 for UTF-8 (automatically selected by default, or
/// explicitly requested); Kanji is intentionally unsupported, use UTF-8 instead.
/// The quiet zone defaults to the ISO/IEC 23941 value of 2 modules.
/// </para>
/// </remarks>
public static class RmQRCodeGenerator
{
    private const int DefaultQuietZone = RmQRConstants.QuietZoneModules;
    private const int MaxDataCodewords = 152;   // R17x139-M
    private const int MaxFinalMessageBytes = 233; // R17x139: 232 codewords + remainder byte

    /// <summary>
    /// Creates an rMQR code from the provided plain text.
    /// </summary>
    /// <param name="plainText">The text to encode.</param>
    /// <param name="eccLevel">Error correction level (M or H).</param>
    /// <param name="requestedVersion">Specific version, or null to fit automatically.</param>
    /// <param name="fitStrategy">How to choose among fitting versions when <paramref name="requestedVersion"/> is null.</param>
    /// <param name="height">Restrict automatic fit to this symbol height (must agree with <paramref name="requestedVersion"/> when both are given).</param>
    /// <param name="quietZoneSize">Quiet zone width in modules (rMQR specification: 2).</param>
    /// <returns>An <see cref="RmQRCodeData"/> containing the generated matrix.</returns>
    /// <exception cref="ArgumentException">Thrown when the data does not fit or the arguments contradict each other.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for invalid version / ECC / strategy / height / quiet zone values.</exception>
    public static RmQRCodeData CreateRmQRCode(string plainText, RmQREccLevel eccLevel, RmQRVersion? requestedVersion = null, RmQRFitStrategy fitStrategy = RmQRFitStrategy.MinimizeArea, RmQRHeight? height = null, int quietZoneSize = DefaultQuietZone)
        => CreateRmQRCode(plainText.AsSpan(), eccLevel, requestedVersion, fitStrategy, height, quietZoneSize);

    /// <summary>Creates an rMQR code with an explicit or automatically resolved ECI mode.</summary>
    /// <param name="plainText">The text to encode.</param>
    /// <param name="eccLevel">Error correction level (M or H).</param>
    /// <param name="eciMode">Character encoding declaration. Default auto-detects ASCII / ISO-8859-1 / UTF-8.</param>
    /// <param name="requestedVersion">Specific version, or null to fit automatically.</param>
    /// <param name="fitStrategy">How to choose among fitting versions.</param>
    /// <param name="height">Optional fixed-height constraint.</param>
    /// <param name="quietZoneSize">Quiet zone width in modules.</param>
    public static RmQRCodeData CreateRmQRCodeWithEci(string plainText, RmQREccLevel eccLevel, EciMode eciMode, RmQRVersion? requestedVersion = null, RmQRFitStrategy fitStrategy = RmQRFitStrategy.MinimizeArea, RmQRHeight? height = null, int quietZoneSize = DefaultQuietZone)
        => CreateRmQRCodeWithEci(plainText.AsSpan(), eccLevel, eciMode, requestedVersion, fitStrategy, height, quietZoneSize);

    /// <inheritdoc cref="CreateRmQRCode(string, RmQREccLevel, RmQRVersion?, RmQRFitStrategy, RmQRHeight?, int)"/>
    /// <param name="textSpan">The text span to encode.</param>
    public static RmQRCodeData CreateRmQRCode(ReadOnlySpan<char> textSpan, RmQREccLevel eccLevel, RmQRVersion? requestedVersion = null, RmQRFitStrategy fitStrategy = RmQRFitStrategy.MinimizeArea, RmQRHeight? height = null, int quietZoneSize = DefaultQuietZone)
    {
        ValidateQuietZone(quietZoneSize);
        var config = PrepareConfigurationAutoEci(textSpan, eccLevel, requestedVersion, fitStrategy, height);
        var result = new RmQRCodeData(config.Version, quietZoneSize);
        var coreWidth = result.GetCoreWidth();
        var coreHeight = result.GetCoreHeight();
        var coreLength = coreWidth * coreHeight;
        var rented = ArrayPool<byte>.Shared.Rent(coreLength);
        try
        {
            var core = rented.AsSpan(0, coreLength);
            WriteCoreModulesAutoEci(textSpan, in config, core, coreWidth);
            result.SetCoreData(core);
            return result;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented, clearArray: false);
        }
    }

    /// <summary>Creates an rMQR code with an explicit or automatically resolved ECI mode.</summary>
    public static RmQRCodeData CreateRmQRCodeWithEci(ReadOnlySpan<char> textSpan, RmQREccLevel eccLevel, EciMode eciMode, RmQRVersion? requestedVersion = null, RmQRFitStrategy fitStrategy = RmQRFitStrategy.MinimizeArea, RmQRHeight? height = null, int quietZoneSize = DefaultQuietZone)
    {
        ValidateQuietZone(quietZoneSize);
        var config = PrepareConfigurationWithEci(textSpan, eccLevel, eciMode, requestedVersion, fitStrategy, height);
        var result = new RmQRCodeData(config.Version, quietZoneSize);
        var coreWidth = result.GetCoreWidth();
        var coreHeight = result.GetCoreHeight();
        var coreLength = coreWidth * coreHeight;

        // Core matrix up to 17 × 139 = 2,363 bytes: rented, not stack (same policy as
        // the Standard QR generator), returned in finally, never escapes.
        var rented = ArrayPool<byte>.Shared.Rent(coreLength);
        try
        {
            var core = rented.AsSpan(0, coreLength);
            WriteCoreModulesWithEci(textSpan, in config, core, coreWidth);
            result.SetCoreData(core);
            return result;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented, clearArray: false);
        }
    }

    /// <summary>
    /// Creates an rMQR code and writes the module matrix into the caller-provided
    /// buffer without heap allocation.
    /// </summary>
    /// <remarks>
    /// Output format matches the other generators: one byte per module (0 = light,
    /// 1 = dark), flat row-major over the full width, quiet zone included. Use
    /// <see cref="GetRequiredBufferSize"/> to size the destination.
    /// </remarks>
    /// <param name="textSpan">The text span to encode.</param>
    /// <param name="eccLevel">Error correction level (M or H).</param>
    /// <param name="destination">Destination buffer; at least <see cref="RmQRCodeCalculatedSize.BufferSize"/> bytes.</param>
    /// <param name="requestedVersion">Specific version, or null to fit automatically.</param>
    /// <param name="fitStrategy">How to choose among fitting versions when <paramref name="requestedVersion"/> is null.</param>
    /// <param name="height">Restrict automatic fit to this symbol height.</param>
    /// <param name="quietZoneSize">Quiet zone width in modules.</param>
    /// <returns>The number of bytes written (width × height, quiet zone included).</returns>
    /// <exception cref="ArgumentException">Thrown when the destination is too small, the data does not fit, or the arguments contradict each other.</exception>
    public static int CreateRmQRCode(ReadOnlySpan<char> textSpan, RmQREccLevel eccLevel, Span<byte> destination, RmQRVersion? requestedVersion = null, RmQRFitStrategy fitStrategy = RmQRFitStrategy.MinimizeArea, RmQRHeight? height = null, int quietZoneSize = DefaultQuietZone)
    {
        // Keep the shipped auto-detect entry point as a direct hot method.
        // Routing it through the explicit-ECI public method regresses the largest
        // span encode by ~30% because that method is too large to inline.
        ValidateQuietZone(quietZoneSize);
        var config = PrepareConfigurationAutoEci(textSpan, eccLevel, requestedVersion, fitStrategy, height);
        var coreWidth = RmQRConstants.GetWidth(config.Version);
        var coreHeight = RmQRConstants.GetHeight(config.Version);
        var totalWidth = coreWidth + quietZoneSize * 2;
        var totalHeight = coreHeight + quietZoneSize * 2;
        var requiredSize = totalWidth * totalHeight;
        if (destination.Length < requiredSize)
            throw new ArgumentException($"Destination buffer too small: {requiredSize} bytes required (version {config.Version}, {totalWidth}x{totalHeight} modules), got {destination.Length} bytes. Use {nameof(GetRequiredBufferSize)} to calculate the required size.", nameof(destination));

        var target = destination.Slice(0, requiredSize);
        if (quietZoneSize == 0)
        {
            WriteCoreModulesAutoEci(textSpan, in config, target, coreWidth);
            return requiredSize;
        }

        var margin = quietZoneSize * totalWidth;
        target.Slice(0, margin + quietZoneSize).Clear();
        for (var row = 1; row < coreHeight; row++)
            target.Slice(margin + row * totalWidth - quietZoneSize, 2 * quietZoneSize).Clear();
        target.Slice(margin + coreHeight * totalWidth - quietZoneSize).Clear();
        WriteCoreModulesAutoEci(textSpan, in config, target.Slice(margin + quietZoneSize), totalWidth);
        return requiredSize;
    }

    /// <summary>Writes an rMQR matrix with an explicit or automatically resolved ECI mode.</summary>
    public static int CreateRmQRCodeWithEci(ReadOnlySpan<char> textSpan, RmQREccLevel eccLevel, Span<byte> destination, EciMode eciMode, RmQRVersion? requestedVersion = null, RmQRFitStrategy fitStrategy = RmQRFitStrategy.MinimizeArea, RmQRHeight? height = null, int quietZoneSize = DefaultQuietZone)
    {
        ValidateQuietZone(quietZoneSize);
        var config = PrepareConfigurationWithEci(textSpan, eccLevel, eciMode, requestedVersion, fitStrategy, height);
        var coreWidth = RmQRConstants.GetWidth(config.Version);
        var coreHeight = RmQRConstants.GetHeight(config.Version);
        var totalWidth = coreWidth + quietZoneSize * 2;
        var totalHeight = coreHeight + quietZoneSize * 2;
        var requiredSize = totalWidth * totalHeight;
        if (destination.Length < requiredSize)
            throw new ArgumentException($"Destination buffer too small: {requiredSize} bytes required (version {config.Version}, {totalWidth}x{totalHeight} modules), got {destination.Length} bytes. Use {nameof(GetRequiredBufferSize)} to calculate the required size.", nameof(destination));

        var target = destination.Slice(0, requiredSize);
        if (quietZoneSize == 0)
        {
            // The placer writes every core module, no clear needed.
            WriteCoreModulesWithEci(textSpan, in config, target, coreWidth);
            return requiredSize;
        }

        // Quiet zone: light rows above and below, light margins on every core row; the
        // placer writes the core straight into the strided window in between (no
        // intermediate core buffer, no row copies).
        var margin = quietZoneSize * totalWidth;
        target.Slice(0, margin + quietZoneSize).Clear();                          // top rows + first row's left margin
        for (var row = 1; row < coreHeight; row++)
        {
            // right margin of row - 1 and left margin of row are contiguous
            target.Slice(margin + row * totalWidth - quietZoneSize, 2 * quietZoneSize).Clear();
        }
        target.Slice(margin + coreHeight * totalWidth - quietZoneSize).Clear();     // last row's right margin + bottom rows
        WriteCoreModulesWithEci(textSpan, in config, target.Slice(margin + quietZoneSize), totalWidth);

        return requiredSize;
    }

    /// <summary>
    /// Calculates the required buffer size, dimensions and version for encoding the
    /// specified text as an rMQR code.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="eccLevel">Error correction level (M or H).</param>
    /// <param name="requestedVersion">Specific version, or null to fit automatically.</param>
    /// <param name="fitStrategy">How to choose among fitting versions when <paramref name="requestedVersion"/> is null.</param>
    /// <param name="height">Restrict automatic fit to this symbol height.</param>
    /// <param name="quietZoneSize">Quiet zone width in modules.</param>
    /// <exception cref="ArgumentException">Thrown when the data does not fit or the arguments contradict each other.</exception>
    public static RmQRCodeCalculatedSize GetRequiredBufferSize(ReadOnlySpan<char> text, RmQREccLevel eccLevel, RmQRVersion? requestedVersion = null, RmQRFitStrategy fitStrategy = RmQRFitStrategy.MinimizeArea, RmQRHeight? height = null, int quietZoneSize = DefaultQuietZone)
    {
        ValidateQuietZone(quietZoneSize);
        var config = PrepareConfigurationAutoEci(text, eccLevel, requestedVersion, fitStrategy, height);
        var totalWidth = RmQRConstants.GetWidth(config.Version) + quietZoneSize * 2;
        var totalHeight = RmQRConstants.GetHeight(config.Version) + quietZoneSize * 2;
        return new RmQRCodeCalculatedSize(totalWidth * totalHeight, totalWidth, totalHeight, config.Version);
    }

    /// <summary>Calculates dimensions with an explicit or automatically resolved ECI mode.</summary>
    public static RmQRCodeCalculatedSize GetRequiredBufferSizeWithEci(ReadOnlySpan<char> text, RmQREccLevel eccLevel, EciMode eciMode, RmQRVersion? requestedVersion = null, RmQRFitStrategy fitStrategy = RmQRFitStrategy.MinimizeArea, RmQRHeight? height = null, int quietZoneSize = DefaultQuietZone)
    {
        ValidateQuietZone(quietZoneSize);
        var config = PrepareConfigurationWithEci(text, eccLevel, eciMode, requestedVersion, fitStrategy, height);
        var totalWidth = RmQRConstants.GetWidth(config.Version) + quietZoneSize * 2;
        var totalHeight = RmQRConstants.GetHeight(config.Version) + quietZoneSize * 2;
        return new RmQRCodeCalculatedSize(totalWidth * totalHeight, totalWidth, totalHeight, config.Version);
    }

    private static void ValidateQuietZone(int quietZoneSize)
    {
        // (139 + 2·qz) × (17 + 2·qz) must stay far below int.MaxValue; 10000 modules of
        // quiet zone is already absurd, so a simple hard cap keeps the math safe.
        if (quietZoneSize < 0 || quietZoneSize > 10_000)
            throw new ArgumentOutOfRangeException(nameof(quietZoneSize), $"Quiet zone size must be 0-10000, got {quietZoneSize}");
    }

    /// <summary>Auto-detects ECI while preserving an isolated no-ECI selector for ASCII.</summary>
    private static RmQRConfiguration PrepareConfigurationAutoEci(ReadOnlySpan<char> textSpan, RmQREccLevel eccLevel, RmQRVersion? requestedVersion, RmQRFitStrategy fitStrategy, RmQRHeight? height)
    {
        var analysis = TextAnalyzer.Analyze(textSpan, EciMode.Default);
        var version = analysis.EciMode == EciMode.Default
            ? RmQRVersionSelector.Select(analysis.EncodingMode, analysis.DataLength, eccLevel, requestedVersion, fitStrategy, height)
            : RmQRVersionSelector.Select(analysis.EncodingMode, analysis.DataLength, analysis.EciMode, eccLevel, requestedVersion, fitStrategy, height);
        return new RmQRConfiguration(version, eccLevel, analysis);
    }

    /// <summary>Analyzes ECI text and selects / validates the version.</summary>
    private static RmQRConfiguration PrepareConfigurationWithEci(ReadOnlySpan<char> textSpan, RmQREccLevel eccLevel, EciMode eciMode, RmQRVersion? requestedVersion, RmQRFitStrategy fitStrategy, RmQRHeight? height)
    {
        if (eciMode is not (EciMode.Default or EciMode.Iso8859_1 or EciMode.Utf8))
            throw new ArgumentOutOfRangeException(nameof(eciMode), $"Unsupported ECI mode for rMQR: {eciMode}");
        if (eciMode == EciMode.Iso8859_1 && !CharacterSets.IsValidISO88591(textSpan))
            throw new ArgumentException("The content contains characters that cannot be represented by ISO-8859-1. Use EciMode.Utf8 or EciMode.Default.", nameof(eciMode));

        // Default resolves to no ECI for ASCII, assignment 3 for Latin-1 beyond
        // ASCII, and assignment 26 for Unicode. DataLength is the encoded byte count.
        var analysis = TextAnalyzer.Analyze(textSpan, eciMode);
        var version = analysis.EciMode == EciMode.Default
            ? RmQRVersionSelector.Select(analysis.EncodingMode, analysis.DataLength, eccLevel, requestedVersion, fitStrategy, height)
            : RmQRVersionSelector.Select(analysis.EncodingMode, analysis.DataLength, analysis.EciMode, eccLevel, requestedVersion, fitStrategy, height);
        return new RmQRConfiguration(version, eccLevel, analysis);
    }

    /// <summary>
    /// Runs encode → ECC + interleave → placement into a byte-per-module core window
    /// (width × height, rows <paramref name="stride"/> bytes apart, every core module
    /// written; stride == width for a packed core). Allocation-free: fixed stack budgets.
    /// </summary>
    private static void WriteCoreModulesAutoEci(ReadOnlySpan<char> textSpan, in RmQRConfiguration config, Span<byte> core, int stride)
    {
        if (config.Analysis.EciMode == EciMode.Default)
            WriteCoreModulesWithoutEci(textSpan, in config, core, stride);
        else
            WriteCoreModulesWithEci(textSpan, in config, core, stride);
    }

    private static void WriteCoreModulesWithoutEci(ReadOnlySpan<char> textSpan, in RmQRConfiguration config, Span<byte> core, int stride)
    {
        Span<byte> dataCodewords = stackalloc byte[MaxDataCodewords];
        var analysis = config.Analysis;
        Debug.Assert(analysis.EciMode == EciMode.Default);
        var dataCount = RmQRBinaryEncoder.EncodeDataCodewordsWithoutEci(textSpan, config.Version, config.EccLevel, in analysis, dataCodewords);

        Span<byte> finalMessage = stackalloc byte[MaxFinalMessageBytes];
        finalMessage = finalMessage.Slice(0, RmQRCodewordEncoder.GetFinalMessageSize(config.Version));
        RmQRCodewordEncoder.AssembleFinalMessage(dataCodewords.Slice(0, dataCount), config.Version, config.EccLevel, finalMessage);

        RmQRModulePlacer.PlaceSymbol(core, stride, config.Version, config.EccLevel, finalMessage);
    }

    private static void WriteCoreModulesWithEci(ReadOnlySpan<char> textSpan, in RmQRConfiguration config, Span<byte> core, int stride)
    {
        Span<byte> dataCodewords = stackalloc byte[MaxDataCodewords];
        var analysis = config.Analysis;
        var dataCount = RmQRBinaryEncoder.EncodeDataCodewords(textSpan, config.Version, config.EccLevel, in analysis, dataCodewords);

        Span<byte> finalMessage = stackalloc byte[MaxFinalMessageBytes];
        finalMessage = finalMessage.Slice(0, RmQRCodewordEncoder.GetFinalMessageSize(config.Version));
        RmQRCodewordEncoder.AssembleFinalMessage(dataCodewords.Slice(0, dataCount), config.Version, config.EccLevel, finalMessage);

        RmQRModulePlacer.PlaceSymbol(core, stride, config.Version, config.EccLevel, finalMessage);
    }

    private readonly record struct RmQRConfiguration(RmQRVersion Version, RmQREccLevel EccLevel, TextAnalysisResult Analysis);
}
