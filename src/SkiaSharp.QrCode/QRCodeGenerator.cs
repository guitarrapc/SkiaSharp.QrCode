using System.Buffers;
using System.Runtime.CompilerServices;
using SkiaSharp.QrCode.Internals;
using SkiaSharp.QrCode.Internals.BinaryEncoders;

namespace SkiaSharp.QrCode;

/// <summary>
/// QR code generator based on ISO/IEC 18004 standard.
/// Supports QR code versions 1-40 with multiple encoding modes and error correction levels.
/// </summary>
public static class QRCodeGenerator
{
    // -----------------------------------------------------
    // QR Code Structure
    // -----------------------------------------------------
    //
    // 1. Header
    // ┌─────────────────┬───────────────┬────────────────┐
    // │ ECI (0 or 12b)  │ Mode (4b)     │ Count (8-16b)  │
    // └─────────────────┴───────────────┴────────────────┘
    // 2. Data
    // ┌──────────────────────────────────────────────────┐
    // │ Encoded data (variable length)                   │
    // └──────────────────────────────────────────────────┘
    // 3. Padding
    // ┌──────┬────────┬──────────────────────────────────┐
    // │ Term │ Align  │ Pad bytes (0xEC, 0x11...)        │
    // │ (4b) │ (0-7b) │ (until dataCapacityBits reached) │
    // └──────┴────────┴──────────────────────────────────┘

    /// <summary>
    /// Creates a QR code from the provided plain text.
    /// </summary>
    /// <param name="plainText">The text to encode in the QR code.</param>
    /// <param name="eccLevel">Error correction level (L: 7%, M: 15%, Q: 25%, H: 30%).</param>
    /// <param name="utf8BOM">Include UTF-8 BOM (Byte Order Mark) in encoded data. Ignore if data is not UTF-8.</param>
    /// <param name="eciMode">ECI mode for character encoding.</param>
    /// <param name="requestedVersion">Specific version to use (1-40), or -1 for automatic selection.</param>
    /// <param name="quietZoneSize">Size of the quiet zone (white border) in modules.</param>
    /// <returns>QRCodeData containing the generated QR code matrix.</returns>
    public static QRCodeData CreateQrCode(string plainText, ECCLevel eccLevel, bool utf8BOM = false, EciMode eciMode = EciMode.Default, int requestedVersion = -1, int quietZoneSize = 4)
    {
        return CreateQrCode(plainText.AsSpan(), eccLevel, utf8BOM, eciMode, requestedVersion, quietZoneSize);
    }

    /// <summary>
    /// Creates a QR code from the provided plain text.
    /// </summary>
    /// <param name="textSpan">The text span to encode in the QR code.</param>
    /// <param name="eccLevel">Error correction level (L: 7%, M: 15%, Q: 25%, H: 30%).</param>
    /// <param name="utf8BOM">Include UTF-8 BOM (Byte Order Mark) in encoded data. Ignore if data is not UTF-8.</param>
    /// <param name="eciMode">ECI mode for character encoding.</param>
    /// <param name="requestedVersion">Specific version to use (1-40), or -1 for automatic selection.</param>
    /// <param name="quietZoneSize">Size of the quiet zone (white border) in modules.</param>
    /// <returns>QRCodeData containing the generated QR code matrix.</returns>
    public static QRCodeData CreateQrCode(ReadOnlySpan<char> textSpan, ECCLevel eccLevel, bool utf8BOM = false, EciMode eciMode = EciMode.Default, int requestedVersion = -1, int quietZoneSize = 4)
    {
        // QR code generation process:
        // ------------------------------------------------
        // 1. Determine optimal encoding mode (Numeric/Alphanumeric/Byte)
        // 2. Encode data to binary string
        // 3. Select QR code version (based on data length and ECC level)
        // 4. Add mode indicator and character count indicator
        // 5. Pad data to fill code word capacity
        // 6. Calculate error correction codewords using Reed-Solomon
        // 7. Interleave data and ECC codewords
        // 8. Place finder patterns, alignment patterns, timing patterns
        // 9. Place data modules in zigzag pattern
        // 10. Apply optimal mask pattern (test all 8 patterns)
        // 11. Add format and version information
        // 12. Add quiet zone (white border)

        if (requestedVersion != -1 && (requestedVersion < 1 || requestedVersion > 40))
            throw new ArgumentOutOfRangeException(nameof(requestedVersion), $"Version must be 1-40 or -1(auto), but was {requestedVersion}");
        if (quietZoneSize < 0)
            throw new ArgumentOutOfRangeException(nameof(quietZoneSize), $"Quiet zone size must be non-negative, got {quietZoneSize}");

        // Prepare configuration
        var config = PrepareConfiguration(textSpan, eccLevel, utf8BOM, eciMode, requestedVersion);

        // Calculate buffer sizes
        var result = new QRCodeData(config.Version, quietZoneSize);
        var coreSize = result.GetCoreSize();
        var dataLength = coreSize * coreSize;

        var dataCapacity = CalculateMaxBitStringLength(config.Version, config.EccLevel, config.Encoding);
        var dataBufferSize = (dataCapacity + 7) / 8; // bits to bytes, rounded up
        var totalBlocks = config.EccInfo.BlocksInGroup1 + config.EccInfo.BlocksInGroup2;
        var eccBufferSize = totalBlocks * config.EccInfo.ECCPerBlock;
        var interleavedSize = BinaryInterleaver.CalculateInterleavedSize(config.EccInfo, config.Version);

        // Allocate buffers
        byte[]? rentedWorkBuffer = null;

        try
        {
            // Work buffer (without quiet zone)
            rentedWorkBuffer = ArrayPool<byte>.Shared.Rent(dataLength);
            var workBuffer = rentedWorkBuffer.AsSpan(0, dataLength);
            workBuffer.Clear();

            Span<byte> dataBuffer = stackalloc byte[dataBufferSize];
            Span<byte> eccBuffer = stackalloc byte[eccBufferSize];
            Span<byte> interleavedBuffer = stackalloc byte[interleavedSize];

            // Encode data
            var encodedLength = EncodeData(textSpan, config, dataBuffer);
            var encodedData = dataBuffer.Slice(0, encodedLength);

            // Calculate Error Correction
            CalculateErrorCorrection(encodedData, config.EccInfo, eccBuffer);

            // Interleave data
            InterleaveCodewords(encodedData, eccBuffer, config.EccInfo, config.Version, interleavedBuffer);

            // QR matrix in work buffer
            WriteQRMatrix(workBuffer, coreSize, config.Version, interleavedBuffer, config.EccLevel);

            result.SetCoreData(workBuffer);

            return result;
        }
        finally
        {
            if (rentedWorkBuffer is not null)
                ArrayPool<byte>.Shared.Return(rentedWorkBuffer, clearArray: false);
        }
    }

    /// <summary>
    /// Calculates the required buffer size for encoding the specified text as a QR code.
    /// </summary>
    /// <param name="text">The text to encode in the QR code</param>
    /// <param name="eccLevel">Error correction level</param>
    /// <param name="utf8BOM">Include UTF-8 BOM (Byte Order Mark) in encoded data. Ignore if data is not UTF-8.</param>
    /// <param name="eciMode">ECI mode for character encoding.</param>
    /// <param name="quietZoneSize">Size of the quiet zone (white border) in modules.</param>
    /// <returns>A <see cref="QRCodeCalculatedSize"/> structure containing buffer size, QR size, and version information.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the data is too large for version 40.</exception>
    public static QRCodeCalculatedSize GetRequiredBufferSize(ReadOnlySpan<char> text, ECCLevel eccLevel, bool utf8BOM = false, EciMode eciMode = EciMode.Default, int quietZoneSize = 4)
    {
        var analysisResult = TextAnalyzer.Analyze(text, eciMode);
        var version = GetVersion(analysisResult.DataLength, analysisResult.EncodingMode, eccLevel, analysisResult.EciMode, utf8BOM);

        if (version is < -1 or > 40)
            throw new ArgumentOutOfRangeException(nameof(version), $"Version must be 1-40, but was {version}");

        var baseSize = QRCodeData.SizeFromVersion(version);
        var totalSize = baseSize + quietZoneSize * 2; // for rendering with quiet zone
        var bufferSize = totalSize * totalSize; // for buffer allocation

        return new QRCodeCalculatedSize(bufferSize, totalSize, version);
    }

    // Pipelines

    /// <summary>
    /// Prepares QR configuration by determining encoding, ECI mode, and version.
    /// </summary>
    /// <param name="textSpan"></param>
    /// <param name="eccLevel"></param>
    /// <param name="utf8Bom"></param>
    /// <param name="eciMode"></param>
    /// <param name="requestedVersion"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static QRConfiguration PrepareConfiguration(ReadOnlySpan<char> textSpan, ECCLevel eccLevel, bool utf8BOM, EciMode eciMode, int requestedVersion)
    {
        var analysisResult = TextAnalyzer.Analyze(textSpan, eciMode);

        // Select QR code version (auto or manual)
        var version = requestedVersion == -1
            ? GetVersion(analysisResult.DataLength, analysisResult.EncodingMode, eccLevel, analysisResult.EciMode, utf8BOM)
            : requestedVersion;

        // Create ECCInfo
        var eccInfo = QRCodeConstants.GetEccInfo(version, eccLevel);

        return new QRConfiguration(version, eccLevel, analysisResult.EncodingMode, analysisResult.EciMode, utf8BOM, eccInfo, analysisResult.DataLength);
    }

    /// <summary>
    /// Encodes the input text into a binary format and writes it to the provided buffer.
    /// </summary>
    /// <param name="textSpan"></param>
    /// <param name="config"></param>
    /// <param name="buffer">Output buffer for encoded data.</param>
    /// <returns>Number of bytes written to the buffer.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int EncodeData(ReadOnlySpan<char> textSpan, in QRConfiguration config, Span<byte> buffer)
    {
        var encoder = new QRBinaryEncoder(buffer);

        encoder.WriteMode(config.Encoding, config.EciMode);
        encoder.WriteCharacterCount(config.DataLength, config.Encoding.GetCountIndicatorLength(config.Version));
        encoder.WriteData(textSpan, config.Encoding, config.EciMode, config.Utf8BOM);
        encoder.WritePadding(config.EccInfo.TotalDataCodewords * 8);

        return encoder.ByteCount;
    }

    /// <summary>
    /// Calculates Reed-Solomon error correction codewords and writes them to the provided ECC buffer.
    /// </summary>
    /// <param name="encodedBytes">The byte representing the encoded QR code data.</param>
    /// <param name="eccInfo">Error correction information for the QR code version and ECC level.</param>
    /// <param name="eccBuffer">Output buffer for ECC codewords <c>(eccInfo.BlocksInGroup1 + eccInfo.BlocksInGroup2) * eccInfo.ECCPerBlock</c> bytes.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CalculateErrorCorrection(ReadOnlySpan<byte> encodedBytes, in ECCInfo eccInfo, Span<byte> eccBuffer)
    {
        var dataOffset = 0;
        var eccOffset = 0;

        // Process group 1 blocks
        for (var i = 0; i < eccInfo.BlocksInGroup1; i++)
        {
            var blockData = encodedBytes.Slice(dataOffset, eccInfo.CodewordsInGroup1);
            var blockEcc = eccBuffer.Slice(eccOffset, eccInfo.ECCPerBlock);

            EccBinaryEncoder.CalculateECC(blockData, blockEcc, eccInfo.ECCPerBlock);

            dataOffset += eccInfo.CodewordsInGroup1;
            eccOffset += eccInfo.ECCPerBlock;
        }

        // Process group 2 blocks
        for (var i = 0; i < eccInfo.BlocksInGroup2; i++)
        {
            var blockData = encodedBytes.Slice(dataOffset, eccInfo.CodewordsInGroup2);
            var blockEcc = eccBuffer.Slice(eccOffset, eccInfo.ECCPerBlock);

            EccBinaryEncoder.CalculateECC(blockData, blockEcc, eccInfo.ECCPerBlock);

            dataOffset += eccInfo.CodewordsInGroup2;
            eccOffset += eccInfo.ECCPerBlock;
        }
    }

    /// <summary>
    /// Interleaves data and error correction codewords according to QR code specification.
    /// </summary>
    /// <param name="dataBuffer">The buffer containing encoded data codewords</param>
    /// <param name="eccBuffer">The buffer containing error correction codewords</param>
    /// <param name="eccInfo">Error correction information for the QR code version and ECC level</param>
    /// <param name="version">The QR code version (1-40)</param>
    /// <param name="output">Output buffer to write interleaved codewords</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void InterleaveCodewords(ReadOnlySpan<byte> dataBuffer, ReadOnlySpan<byte> eccBuffer, in ECCInfo eccInfo, int version, Span<byte> output)
    {
        BinaryInterleaver.InterleaveCodewords(dataBuffer, eccBuffer, output, version, eccInfo);
    }

    /// <summary>
    /// Writes the QR code matrix (as a 1D byte array) into the provided buffer by placing patterns, data, applying mask, and adding format/version information.
    /// </summary>
    /// <param name="buffer">The buffer to write the QR code matrix into.</param>
    /// <param name="size">The size of the QR code matrix (number of modules per side).</param>
    /// <param name="version">The QR code version (1-40) to generate.</param>
    /// <param name="interleavedData">The encoded and interleaved data bytes to be placed in the QR code.</param>
    /// <param name="eccLevel">The error correction level to use for the QR code.</param>
    /// <returns>A <see cref="QRCodeData"/> object containing the generated QR code matrix.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteQRMatrix(Span<byte> buffer, int size, int version, ReadOnlySpan<byte> interleavedData, ECCLevel eccLevel)
    {
        // Version 1-16: stack allocation (covers 95%+ use cases)
        // Version 17+: heap allocation (large/rare QR codes)
        const int StackAllocThreshold = 16;
        const int StackAllocSize = 40; // Sufficient for version 16 (33 modules needed)

        // Version 1:  approximately  9
        // Version 7:  approximately 27
        // Version 40: approximately 57
        Rectangle[]? rentedBlockedModules = null;
        byte[]? rentedBlockedMask = null;

        try
        {
            var blockedModulesSize = CalculateBlockedModulesSize(version);
            Span<Rectangle> blockedModulesBuffer = version > StackAllocThreshold
                ? (rentedBlockedModules = ArrayPool<Rectangle>.Shared.Rent(blockedModulesSize)).AsSpan(0, blockedModulesSize)
                : stackalloc Rectangle[StackAllocSize];
            blockedModulesBuffer.Clear();
            var blockedCount = 0;

            // Place all patterns
            var alignmentPatternLocations = GetAlignmentPatternPositions(version);
            ModulePlacer.PlaceFinderPatterns(buffer, size, blockedModulesBuffer, ref blockedCount);
            ModulePlacer.ReserveSeparatorAreas(size, blockedModulesBuffer, ref blockedCount);
            ModulePlacer.PlaceAlignmentPatterns(buffer, size, alignmentPatternLocations, blockedModulesBuffer, ref blockedCount);
            ModulePlacer.PlaceTimingPatterns(buffer, size, blockedModulesBuffer, ref blockedCount);
            ModulePlacer.PlaceDarkModule(buffer, size, version, blockedModulesBuffer, ref blockedCount);
            ModulePlacer.ReserveVersionAreas(size, version, blockedModulesBuffer, ref blockedCount);

            // Generate BitMask
            var maskSize = (size * size + 7) / 8;
            Span<byte> blockedMask = maskSize <= 1024
                ? stackalloc byte[maskSize]
                : (rentedBlockedMask = ArrayPool<byte>.Shared.Rent(maskSize)).AsSpan(0, maskSize);
            blockedMask.Clear();
            BuildBlockedMask(blockedMask, size, blockedModulesBuffer.Slice(0, blockedCount));

            // Place data
            ModulePlacer.PlaceDataWords(buffer, size, interleavedData, blockedMask);

            // Apply mask and format
            var maskVersion = ModulePlacer.MaskCode(buffer, size, version, blockedMask, eccLevel);
            var formatBit = QRCodeConstants.GetFormatBits(eccLevel, maskVersion);
            ModulePlacer.PlaceFormat(buffer, size, formatBit);

            // Place version information (version 7+)
            if (version >= 7)
            {
                var versionBits = QRCodeConstants.GetVersionBits(version);
                ModulePlacer.PlaceVersion(buffer, size, versionBits);
            }
        }
        finally
        {
            if (rentedBlockedModules is not null)
                ArrayPool<Rectangle>.Shared.Return(rentedBlockedModules, clearArray: false);
            if (rentedBlockedMask is not null)
                ArrayPool<byte>.Shared.Return(rentedBlockedMask, clearArray: false);
        }
    }

    /// <summary>
    /// Builds a bitmask from blocked module rectangles for O(1) lookup.
    /// Each bit in the mask represents whether a module is blocked (1) or free (0).
    /// </summary>
    /// <param name="mask">Output bitmask buffer</param>
    /// <param name="size">The size of the QR code matrix</param>
    /// <param name="blockedModules">List of rectangular areas</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void BuildBlockedMask(Span<byte> mask, int size, ReadOnlySpan<Rectangle> blockedModules)
    {
        foreach (var rect in blockedModules)
        {
            for (var y = rect.Y; y < rect.Y + rect.Height; y++)
            {
                var rowOffset = y * size;
                for (var x = rect.X; x < rect.X + rect.Width; x++)
                {
                    var bitIndex = rowOffset + x;
                    mask[bitIndex >> 3] |= (byte)(1 << (bitIndex & 7));
                }
            }
        }
    }

    /// <summary>
    /// Retrieves alignment pattern positions for the specified version.
    /// </summary>
    /// <param name="version">The QR code version for which to retrieve alignment pattern positions.</param>
    /// <returns>A list of alignment pattern positions as points for the specified QR code version.</returns>
    /// <exception cref="InvalidOperationException"></exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static List<Point> GetAlignmentPatternPositions(int version)
    {
        var table = QRCodeConstants.AlignmentPatternTable;
        for (var i = 0; i < table.Count; i++)
        {
            var item = table[i];
            if (item.Version == version)
                return item.PatternPositions;
        }

        throw new InvalidOperationException($"Alignment pattern positions not found for version {version}");
    }

    /// <summary>
    /// Calculates the maximum bit string length for the given encoding mode, data, and version.
    /// Used to pre-allocate StringBuilder capacity to avoid resizing.
    /// </summary>
    /// <param name="version">QR code version (1-40).</param>
    /// <param name="eccLevel">Error correction level.</param>
    /// <param name="encoding">Encoding mode (Numeric, Alphanumeric, Byte, Kanji).</param>
    /// <returns>Maximum bit string length in characters.</returns>
    internal static int CalculateMaxBitStringLength(int version, ECCLevel eccLevel, EncodingMode encoding)
    {
        if (version is < 1 or > 40)
            throw new ArgumentOutOfRangeException(nameof(version), $"Version must be 1-40, but was {version}");

        // QR codes are always padded to full capacity with 0xEC/0x11 bytes
        // So the final bit string length = data capacity in bits
        // ECCInfo contains the actual byte capacity (TotalDataCodewords)
        var eccInfo = QRCodeConstants.GetEccInfo(version, eccLevel);
        return eccInfo.TotalDataCodewords * 8; // Convert bytes to bits
    }

    // Utilities

    /// <summary>
    /// Calculates the number of blocked modules (reserved areas) for the given version.
    /// </summary>
    /// <param name="version">The QR code version (1-40)</param>
    /// <returns>The number of <see cref="Rectangle"/> elements needed to store all blocked module areas.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CalculateBlockedModulesSize(int version)
    {
        // Blocked modules are reserved areas in the QR matrix that contain fixed patterns:
        // - Finder patterns (3x)
        // - Separators (3x)
        // - Timing patterns (2x)
        // - Dark module (1x)
        // - Format information areas (varies by version)
        // - Version information areas (version 7+)
        // - Alignment patterns (varies by version, 0 for version 1, increases with version)
        // The calculation ensures sufficient buffer space for all reserved areas.

        const int basePatterns = 12;
        var formatVersionAreas = version >= 7 ? 8 : 6;
        var alignmentPatternCount = CalculateAlignmentPatternCount(version);
        return basePatterns + formatVersionAreas + alignmentPatternCount;
    }

    /// <summary>
    /// Calculates the number of alignment pattern positions for a given QR code version.
    /// </summary>
    /// <param name="version">The QR code version (1-40)</param>
    /// <returns>The total number of alignment patterns required for the specified version.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CalculateAlignmentPatternCount(int version)
    {
        // Alignment patterns help QR code readers correct for distortion:
        // - Version 1: No alignment patterns (0)
        // - Version 2+: Multiple alignment patterns arranged in a grid
        // - Pattern count = (positions × positions) - overlaps with finder patterns
        // - Overlaps: 3 corners where alignment patterns would conflict with finder patterns
        // The result is used to allocate buffer space for blocked module tracking.

        if (version == 1) return 0;
        var positions = GetAlignmentPatternPositions(version);
        var posCount = positions.Count;
        var totalCombinations = posCount * posCount;
        var overlaps = posCount >= 2 ? 3 : 0;
        return totalCombinations - overlaps;
    }

    /// <summary>
    /// Determines the minimum QR code version required for given data length.
    /// Searches capacity table for smallest version that can hold the data.
    /// </summary>
    /// <param name="length">Data length (in characters or bytes).</param>
    /// <param name="encoding">Encoding mode being used.</param>
    /// <param name="eccLevel">Error correction level.</param>
    /// <returns>Version number (1-40).</returns>
    /// <remarks>
    /// Calculates required bits including:
    /// - ECI header (0 or 12 bits)
    /// - Mode indicator (4 bits)
    /// - Character count indicator (8-16 bits, version-dependent)
    /// - Data (variable)
    /// </remarks>
    private static int GetVersion(int length, EncodingMode encoding, ECCLevel eccLevel, EciMode eciMode, bool utf8BOM)
    {
        // ECI header overhead if eci specified
        var eciHeaderBits = eciMode.GetHeaderBits();

        // Mode indicator (4 bits)
        var modeIndicatorBits = 4;

        // UTF-8 BOM overhead ([0xEF, 0xBB, 0xBF] = 3 bytes = 24 bits) if specified
        var effectiveLength = length;
        if (utf8BOM && encoding == EncodingMode.Byte && eciMode == EciMode.Utf8)
        {
            effectiveLength += 3;
        }

        // Iterate through versions to find the minimum suitable version
        // Character count indicator size changes at version 10 and 27
        for (var version = 1; version <= 40; version++)
        {
            var countIndicatorBits = encoding.GetCountIndicatorLength(version);

            // Data bits (already in length for Byte mode as byte count)
            var dataBits = encoding switch
            {
                EncodingMode.Numeric => CalculateNumericBits(length),
                EncodingMode.Alphanumeric => CalculateAlphanumericBits(length),
                EncodingMode.Byte => effectiveLength * 8,
                _ => throw new ArgumentOutOfRangeException(nameof(encoding), $"Unsupported encoding mode: {encoding}")
            };

            // Total required bits
            var totalRequiredBits = eciHeaderBits + modeIndicatorBits + countIndicatorBits + dataBits;

            // Get actual capacity for this version and ECC level
            // Use CapacityTable (which has VersionInfo structure)
            var eccInfo = QRCodeConstants.GetEccInfo(version, eccLevel);
            var capacityBits = eccInfo.TotalDataCodewords * 8; // convert bytes to bits

            if (capacityBits >= totalRequiredBits)
            {
                return version;
            }
        }

        throw new InvalidOperationException($"Data too large for QR code (exceeds Version 40 capacity). " +
            $"Required: {eciHeaderBits + modeIndicatorBits} header bits + {length} data units, " +
            $"Mode: {encoding}, ECC: {eccLevel}, ECI: {eciMode}");

        // Calculates actual bit count for numeric encoding.
        // 3 digits → 10 bits, 2 digits → 7 bits, 1 digit → 4 bits.
        static int CalculateNumericBits(int length)
        {
            var bits = (length / 3) * 10; // Groups of 3
            var remainder = length % 3;

            if (remainder == 2)
                bits += 7;
            else if (remainder == 1)
                bits += 4;

            return bits;
        }

        // Calculates actual bit count for alphanumeric encoding.
        // 2 characters → 11 bits, 1 character → 6 bits.
        static int CalculateAlphanumericBits(int length)
        {
            var bits = (length / 2) * 11; // Groups of 2

            if (length % 2 == 1)
                bits += 6; // Remaining 1 character

            return bits;
        }
    }

    /// <summary>
    /// Holds QR configuration parameters determined during setup.
    /// </summary>
    /// <param name="Version">QR code version (1-40) selected for encoding.</param>
    /// <param name="EccLevel">Error correction level used for the QR code.</param>
    /// <param name="Encoding">Encoding mode (Numeric, Alphanumeric, Byte, etc.).</param>
    /// <param name="EciMode">ECI mode specifying character encoding.</param>
    /// <param name="Utf8BOM">Indicates if UTF-8 BOM is included in the encoded data.</param>
    /// <param name="EccInfo">Error correction information for the selected version and ECC level.</param>
    private readonly record struct QRConfiguration(int Version, ECCLevel EccLevel, EncodingMode Encoding, EciMode EciMode, bool Utf8BOM, in ECCInfo EccInfo, int DataLength);
}
