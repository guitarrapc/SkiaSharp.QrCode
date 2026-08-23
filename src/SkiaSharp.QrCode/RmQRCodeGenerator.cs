using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
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
/// explicitly requested). Kanji is not written, use UTF-8 instead;
/// <see cref="RmQRCodeDecoder"/> does read the Kanji segments other encoders produce,
/// so the two directions are deliberately asymmetric.
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
    /// <param name="segmentation">Whether to split the content into mixed-mode segments (see <see cref="RmQRSegmentation"/>).</param>
    /// <returns>An <see cref="RmQRCodeData"/> containing the generated matrix.</returns>
    /// <exception cref="ArgumentException">Thrown when the data does not fit or the arguments contradict each other.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for invalid version / ECC / strategy / height / quiet zone / segmentation values.</exception>
    public static RmQRCodeData CreateRmQRCode(string plainText, RmQREccLevel eccLevel, RmQRVersion? requestedVersion = null, RmQRFitStrategy fitStrategy = RmQRFitStrategy.MinimizeArea, RmQRHeight? height = null, int quietZoneSize = DefaultQuietZone, RmQRSegmentation segmentation = RmQRSegmentation.Single)
        => CreateRmQRCode(plainText.AsSpan(), eccLevel, requestedVersion, fitStrategy, height, quietZoneSize, segmentation);

    /// <summary>Creates an rMQR code with an explicit or automatically resolved ECI mode.</summary>
    /// <param name="plainText">The text to encode.</param>
    /// <param name="eccLevel">Error correction level (M or H).</param>
    /// <param name="eciMode">Character encoding declaration. Default auto-detects ASCII / ISO-8859-1 / UTF-8.</param>
    /// <param name="requestedVersion">Specific version, or null to fit automatically.</param>
    /// <param name="fitStrategy">How to choose among fitting versions.</param>
    /// <param name="height">Optional fixed-height constraint.</param>
    /// <param name="quietZoneSize">Quiet zone width in modules.</param>
    /// <param name="segmentation">Whether to split the content into mixed-mode segments (see <see cref="RmQRSegmentation"/>).</param>
    public static RmQRCodeData CreateRmQRCodeWithEci(string plainText, RmQREccLevel eccLevel, EciMode eciMode, RmQRVersion? requestedVersion = null, RmQRFitStrategy fitStrategy = RmQRFitStrategy.MinimizeArea, RmQRHeight? height = null, int quietZoneSize = DefaultQuietZone, RmQRSegmentation segmentation = RmQRSegmentation.Single)
        => CreateRmQRCodeWithEci(plainText.AsSpan(), eccLevel, eciMode, requestedVersion, fitStrategy, height, quietZoneSize, segmentation);

    /// <inheritdoc cref="CreateRmQRCode(string, RmQREccLevel, RmQRVersion?, RmQRFitStrategy, RmQRHeight?, int, RmQRSegmentation)"/>
    /// <param name="textSpan">The text span to encode.</param>
    public static RmQRCodeData CreateRmQRCode(ReadOnlySpan<char> textSpan, RmQREccLevel eccLevel, RmQRVersion? requestedVersion = null, RmQRFitStrategy fitStrategy = RmQRFitStrategy.MinimizeArea, RmQRHeight? height = null, int quietZoneSize = DefaultQuietZone, RmQRSegmentation segmentation = RmQRSegmentation.Single)
    {
        ValidateQuietZone(quietZoneSize);
        // One compare on the default path; validation of the value itself lives in the
        // cold method so Single costs a predicted not-taken branch and nothing else.
        if (segmentation != RmQRSegmentation.Single)
            return CreateOptimal(textSpan, eccLevel, EciMode.Default, requestedVersion, fitStrategy, height, quietZoneSize, segmentation);

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
    public static RmQRCodeData CreateRmQRCodeWithEci(ReadOnlySpan<char> textSpan, RmQREccLevel eccLevel, EciMode eciMode, RmQRVersion? requestedVersion = null, RmQRFitStrategy fitStrategy = RmQRFitStrategy.MinimizeArea, RmQRHeight? height = null, int quietZoneSize = DefaultQuietZone, RmQRSegmentation segmentation = RmQRSegmentation.Single)
    {
        ValidateQuietZone(quietZoneSize);
        if (segmentation != RmQRSegmentation.Single)
            return CreateOptimal(textSpan, eccLevel, eciMode, requestedVersion, fitStrategy, height, quietZoneSize, segmentation);

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
    /// buffer without per-call heap allocation.
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
    /// <param name="segmentation">Whether to split the content into mixed-mode segments (see <see cref="RmQRSegmentation"/>). Size <paramref name="destination"/> with the same value, since the two modes can select different versions.</param>
    /// <returns>The number of bytes written (width × height, quiet zone included).</returns>
    /// <exception cref="ArgumentException">Thrown when the destination is too small, the data does not fit, or the arguments contradict each other.</exception>
    public static int CreateRmQRCode(ReadOnlySpan<char> textSpan, RmQREccLevel eccLevel, Span<byte> destination, RmQRVersion? requestedVersion = null, RmQRFitStrategy fitStrategy = RmQRFitStrategy.MinimizeArea, RmQRHeight? height = null, int quietZoneSize = DefaultQuietZone, RmQRSegmentation segmentation = RmQRSegmentation.Single)
    {
        // Keep the shipped auto-detect entry point as a direct hot method.
        // Routing it through the explicit-ECI public method regresses the largest
        // span encode by ~30% because that method is too large to inline.
        ValidateQuietZone(quietZoneSize);
        if (segmentation != RmQRSegmentation.Single)
            return CreateOptimalTo(textSpan, eccLevel, EciMode.Default, destination, requestedVersion, fitStrategy, height, quietZoneSize, segmentation);

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
    public static int CreateRmQRCodeWithEci(ReadOnlySpan<char> textSpan, RmQREccLevel eccLevel, Span<byte> destination, EciMode eciMode, RmQRVersion? requestedVersion = null, RmQRFitStrategy fitStrategy = RmQRFitStrategy.MinimizeArea, RmQRHeight? height = null, int quietZoneSize = DefaultQuietZone, RmQRSegmentation segmentation = RmQRSegmentation.Single)
    {
        ValidateQuietZone(quietZoneSize);
        if (segmentation != RmQRSegmentation.Single)
            return CreateOptimalTo(textSpan, eccLevel, eciMode, destination, requestedVersion, fitStrategy, height, quietZoneSize, segmentation);

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
    /// <param name="segmentation">Whether to split the content into mixed-mode segments (see <see cref="RmQRSegmentation"/>).</param>
    /// <remarks>
    /// Pass the same <paramref name="segmentation"/> you will encode with: the two
    /// modes can select different versions, so a buffer sized for one can be too small
    /// for the other.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when the data does not fit or the arguments contradict each other.</exception>
    public static RmQRCodeCalculatedSize GetRequiredBufferSize(ReadOnlySpan<char> text, RmQREccLevel eccLevel, RmQRVersion? requestedVersion = null, RmQRFitStrategy fitStrategy = RmQRFitStrategy.MinimizeArea, RmQRHeight? height = null, int quietZoneSize = DefaultQuietZone, RmQRSegmentation segmentation = RmQRSegmentation.Single)
    {
        ValidateQuietZone(quietZoneSize);
        var version = segmentation != RmQRSegmentation.Single
            ? PlanOptimalVersion(text, eccLevel, EciMode.Default, requestedVersion, fitStrategy, height, segmentation)
            : PrepareConfigurationAutoEci(text, eccLevel, requestedVersion, fitStrategy, height).Version;
        var totalWidth = RmQRConstants.GetWidth(version) + quietZoneSize * 2;
        var totalHeight = RmQRConstants.GetHeight(version) + quietZoneSize * 2;
        return new RmQRCodeCalculatedSize(totalWidth * totalHeight, totalWidth, totalHeight, version);
    }

    /// <summary>Calculates dimensions with an explicit or automatically resolved ECI mode.</summary>
    public static RmQRCodeCalculatedSize GetRequiredBufferSizeWithEci(ReadOnlySpan<char> text, RmQREccLevel eccLevel, EciMode eciMode, RmQRVersion? requestedVersion = null, RmQRFitStrategy fitStrategy = RmQRFitStrategy.MinimizeArea, RmQRHeight? height = null, int quietZoneSize = DefaultQuietZone, RmQRSegmentation segmentation = RmQRSegmentation.Single)
    {
        ValidateQuietZone(quietZoneSize);
        var version = segmentation != RmQRSegmentation.Single
            ? PlanOptimalVersion(text, eccLevel, eciMode, requestedVersion, fitStrategy, height, segmentation)
            : PrepareConfigurationWithEci(text, eccLevel, eciMode, requestedVersion, fitStrategy, height).Version;
        var totalWidth = RmQRConstants.GetWidth(version) + quietZoneSize * 2;
        var totalHeight = RmQRConstants.GetHeight(version) + quietZoneSize * 2;
        return new RmQRCodeCalculatedSize(totalWidth * totalHeight, totalWidth, totalHeight, version);
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

    /// <summary>
    /// Everything the mixed-mode entry points must reject, gathered off the default
    /// path so <see cref="RmQRSegmentation.Single"/> pays only one compare.
    /// </summary>
    private static void ValidateOptimalEntry(ReadOnlySpan<char> textSpan, EciMode eciMode, RmQRSegmentation segmentation)
    {
        if (segmentation != RmQRSegmentation.Optimal)
            throw new ArgumentOutOfRangeException(nameof(segmentation), $"Invalid rMQR segmentation: {segmentation}");
        ValidateEci(textSpan, eciMode);
    }

    private static void ValidateEci(ReadOnlySpan<char> textSpan, EciMode eciMode)
    {
        if (eciMode is not (EciMode.Default or EciMode.Iso8859_1 or EciMode.Utf8))
            throw new ArgumentOutOfRangeException(nameof(eciMode), $"Unsupported ECI mode for rMQR: {eciMode}");
        if (eciMode == EciMode.Iso8859_1 && !CharacterSets.IsValidISO88591(textSpan))
            throw new ArgumentException("The content contains characters that cannot be represented by ISO-8859-1. Use EciMode.Utf8 or EciMode.Default.", nameof(eciMode));
    }

    /// <summary>Analyzes ECI text and selects / validates the version.</summary>
    private static RmQRConfiguration PrepareConfigurationWithEci(ReadOnlySpan<char> textSpan, RmQREccLevel eccLevel, EciMode eciMode, RmQRVersion? requestedVersion, RmQRFitStrategy fitStrategy, RmQRHeight? height)
    {
        ValidateEci(textSpan, eciMode);

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

    // ---------------------------------------------------------------
    // Mixed-mode segmentation (RmQRSegmentation.Optimal).
    //
    // Kept in its own non-inlined methods so the single-mode entry points above keep
    // their frame and codegen. The plan buffer lives here rather than in the planner
    // because a plan is a caller-lent Span<RmQRSegment> that never escapes.
    // ---------------------------------------------------------------

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static RmQRCodeData CreateOptimal(ReadOnlySpan<char> textSpan, RmQREccLevel eccLevel, EciMode eciMode, RmQRVersion? requestedVersion, RmQRFitStrategy fitStrategy, RmQRHeight? height, int quietZoneSize, RmQRSegmentation segmentation)
    {
        ValidateOptimalEntry(textSpan, eciMode, segmentation);
        Span<RmQRSegment> plan = stackalloc RmQRSegment[RmQRSegmentPlanner.MaxSegments];
        var config = PrepareConfigurationOptimal(textSpan, eccLevel, eciMode, requestedVersion, fitStrategy, height, plan, out var segmentCount);
        var segments = plan.Slice(0, segmentCount);

        var result = new RmQRCodeData(config.Version, quietZoneSize);
        var coreWidth = result.GetCoreWidth();
        var coreLength = coreWidth * result.GetCoreHeight();
        var rented = ArrayPool<byte>.Shared.Rent(coreLength);
        try
        {
            var core = rented.AsSpan(0, coreLength);
            WriteCoreModulesPlanned(textSpan, in config, segments, core, coreWidth);
            result.SetCoreData(core);
            return result;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented, clearArray: false);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int CreateOptimalTo(ReadOnlySpan<char> textSpan, RmQREccLevel eccLevel, EciMode eciMode, Span<byte> destination, RmQRVersion? requestedVersion, RmQRFitStrategy fitStrategy, RmQRHeight? height, int quietZoneSize, RmQRSegmentation segmentation)
    {
        ValidateOptimalEntry(textSpan, eciMode, segmentation);
        Span<RmQRSegment> plan = stackalloc RmQRSegment[RmQRSegmentPlanner.MaxSegments];
        var config = PrepareConfigurationOptimal(textSpan, eccLevel, eciMode, requestedVersion, fitStrategy, height, plan, out var segmentCount);
        var segments = plan.Slice(0, segmentCount);

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
            WriteCoreModulesPlanned(textSpan, in config, segments, target, coreWidth);
            return requiredSize;
        }

        var margin = quietZoneSize * totalWidth;
        target.Slice(0, margin + quietZoneSize).Clear();
        for (var row = 1; row < coreHeight; row++)
            target.Slice(margin + row * totalWidth - quietZoneSize, 2 * quietZoneSize).Clear();
        target.Slice(margin + coreHeight * totalWidth - quietZoneSize).Clear();
        WriteCoreModulesPlanned(textSpan, in config, segments, target.Slice(margin + quietZoneSize), totalWidth);
        return requiredSize;
    }

    /// <summary>
    /// Version the optimal path lands on, planned exactly as the encode does so
    /// <see cref="GetRequiredBufferSize"/> can never disagree with it.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static RmQRVersion PlanOptimalVersion(ReadOnlySpan<char> textSpan, RmQREccLevel eccLevel, EciMode eciMode, RmQRVersion? requestedVersion, RmQRFitStrategy fitStrategy, RmQRHeight? height, RmQRSegmentation segmentation)
    {
        ValidateOptimalEntry(textSpan, eciMode, segmentation);
        Span<RmQRSegment> plan = stackalloc RmQRSegment[RmQRSegmentPlanner.MaxSegments];
        return PrepareConfigurationOptimal(textSpan, eccLevel, eciMode, requestedVersion, fitStrategy, height, plan, out _).Version;
    }

    /// <summary>
    /// Analyzes the content, fits a version under mixed-mode segmentation, and
    /// writes the plan. A zero <paramref name="segmentCount"/> means the single-mode
    /// stream is what gets emitted, which is the case whenever mixing would not
    /// shrink the symbol.
    /// </summary>
    private static RmQRConfiguration PrepareConfigurationOptimal(ReadOnlySpan<char> textSpan, RmQREccLevel eccLevel, EciMode eciMode, RmQRVersion? requestedVersion, RmQRFitStrategy fitStrategy, RmQRHeight? height, Span<RmQRSegment> plan, out int segmentCount)
    {
        var analysis = TextAnalyzer.Analyze(textSpan, eciMode);
        var version = RmQRSegmentPlanner.SelectVersion(textSpan, in analysis, eccLevel, requestedVersion, fitStrategy, height, out var useSegments);
        segmentCount = 0;

        if (useSegments && !RmQRSegmentPlanner.TryBuildPlan(textSpan, analysis.EciMode, version, eccLevel, plan, out segmentCount))
        {
            // The plan that justified this version could not be rebuilt (it needed
            // more runs than the buffer holds, or the exact re-cost disagreed with the
            // dynamic program). Fall back to the single-mode fit, which throws the
            // ordinary "content is too long" error when there is no such fit — the
            // honest outcome, because with the plan gone nothing else can be emitted.
            segmentCount = 0;
            version = RmQRSegmentPlanner.SelectSingle(in analysis, eccLevel, requestedVersion, fitStrategy, height);
        }

        return new RmQRConfiguration(version, eccLevel, analysis);
    }

    private static void WriteCoreModulesPlanned(ReadOnlySpan<char> textSpan, in RmQRConfiguration config, ReadOnlySpan<RmQRSegment> segments, Span<byte> core, int stride)
    {
        if (segments.Length == 0)
        {
            WriteCoreModulesAutoEci(textSpan, in config, core, stride);
            return;
        }

        Span<byte> dataCodewords = stackalloc byte[MaxDataCodewords];
        var dataCount = RmQRBinaryEncoder.EncodeDataCodewordsSegmented(textSpan, config.Version, config.EccLevel, config.Analysis.EciMode, segments, dataCodewords);

        Span<byte> finalMessage = stackalloc byte[MaxFinalMessageBytes];
        finalMessage = finalMessage.Slice(0, RmQRCodewordEncoder.GetFinalMessageSize(config.Version));
        RmQRCodewordEncoder.AssembleFinalMessage(dataCodewords.Slice(0, dataCount), config.Version, config.EccLevel, finalMessage);

        RmQRModulePlacer.PlaceSymbol(core, stride, config.Version, config.EccLevel, finalMessage);
    }

    private readonly record struct RmQRConfiguration(RmQRVersion Version, RmQREccLevel EccLevel, TextAnalysisResult Analysis);
}
