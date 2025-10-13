using System.Runtime.CompilerServices;
using System.Text;
using SkiaSharp.QrCode.Internals;
using SkiaSharp.QrCode.Internals.BinaryEncoders;
using SkiaSharp.QrCode.Internals.TextEncoders;


namespace SkiaSharp.QrCode;

/// <summary>
/// QR code generator based on ISO/IEC 18004 standard.
/// Supports QR code versions 1-40 with multiple encoding modes and error correction levels.
/// </summary>
public class QRCodeGenerator : IDisposable
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
    /// <param name="utf8BOM">Include UTF-8 BOM (Byte Order Mark) in encoded data.</param>
    /// <param name="eciMode">ECI mode for character encoding.</param>
    /// <param name="requestedVersion">Specific version to use (1-40), or -1 for automatic selection.</param>
    /// <param name="quietZoneSize">Size of the quiet zone (white border) in modules.</param>
    /// <returns>QRCodeData containing the generated QR code matrix.</returns>
    public QRCodeData CreateQrCode(string plainText, ECCLevel eccLevel, bool utf8BOM = false, EciMode eciMode = EciMode.Default, int requestedVersion = -1, int quietZoneSize = 4)
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

        if (requestedVersion is < -1 or > 40)
            throw new ArgumentOutOfRangeException(nameof(requestedVersion), $"Version must be -1 (auto) or 1-40, got {requestedVersion}");
        if (quietZoneSize < 0)
            throw new ArgumentOutOfRangeException(nameof(quietZoneSize), $"Quiet zone size must be non-negative, got {quietZoneSize}");

        // prepare configuration
        var config = PrepareConfiguration(plainText, eccLevel, utf8BOM, eciMode, requestedVersion);

        // Encode data
        var encodedBits = EncodeData(plainText, config);

        // Calculate Error Correction
        var codewordBlocks = CalculateErrorCorrection(encodedBits, config.EccInfo);

        // Interleave data
        var interleavedData = InterleaveCodewords(codewordBlocks, config.EccInfo, config.Version);

        // Create QR code matrix
        var qrMatrix = CreateQRMatrix(config.Version, interleavedData, config.EccLevel);

        // Add quiet zone
        ModulePlacer.AddQuietZone(ref qrMatrix, quietZoneSize);

        return qrMatrix;
    }

    /// <summary>
    /// Creates a QR code from the provided plain text.
    /// </summary>
    /// <param name="textSpan">The text span to encode in the QR code.</param>
    /// <param name="eccLevel">Error correction level (L: 7%, M: 15%, Q: 25%, H: 30%).</param>
    /// <param name="utf8BOM">Include UTF-8 BOM (Byte Order Mark) in encoded data.</param>
    /// <param name="eciMode">ECI mode for character encoding.</param>
    /// <param name="requestedVersion">Specific version to use (1-40), or -1 for automatic selection.</param>
    /// <param name="quietZoneSize">Size of the quiet zone (white border) in modules.</param>
    /// <returns>QRCodeData containing the generated QR code matrix.</returns>
    public QRCodeData CreateQrCode(ReadOnlySpan<char> textSpan, ECCLevel eccLevel, bool utf8BOM = false, EciMode eciMode = EciMode.Default, int requestedVersion = -1, int quietZoneSize = 4)
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

        if (requestedVersion is < -1 or > 40)
            throw new ArgumentOutOfRangeException(nameof(requestedVersion), $"Version must be -1 (auto) or 1-40, got {requestedVersion}");
        if (quietZoneSize < 0)
            throw new ArgumentOutOfRangeException(nameof(quietZoneSize), $"Quiet zone size must be non-negative, got {quietZoneSize}");

        // prepare configuration
        var config = PrepareConfiguration(textSpan, eccLevel, utf8BOM, eciMode, requestedVersion);

        // Calculate buffer sizes
        var dataCapacity = CalculateMaxBitStringLength(config.Version, config.EccLevel, config.Encoding);
        var dataBufferSize = (dataCapacity + 7) / 8; // bits to bytes, rounded up
        var totalBlocks = config.EccInfo.BlocksInGroup1 + config.EccInfo.BlocksInGroup2;
        var eccBufferSize = totalBlocks * config.EccInfo.ECCPerBlock;
        var interleavedSize = BinaryInterleaver.CalculateInterleavedSize(config.EccInfo, config.Version);

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

        // Create QR code matrix
        var qrMatrix = CreateQRMatrix(config.Version, interleavedBuffer, config.EccLevel);

        // Add quiet zone
        ModulePlacer.AddQuietZone(ref qrMatrix, quietZoneSize);

        return qrMatrix;
    }

    // Text

    /// <summary>
    /// Prepares QR configuration by determining encoding, ECI mode, and version.
    /// </summary>
    /// <param name="plainText"></param>
    /// <param name="eccLevel"></param>
    /// <param name="utf8Bom"></param>
    /// <param name="eciMode"></param>
    /// <param name="requestedVersion"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static QRConfiguration PrepareConfiguration(string plainText, ECCLevel eccLevel, bool utf8Bom, EciMode eciMode, int requestedVersion)
    {
        // When EciMode.Default is specified, choose encoding based on text content.
        var actualEciMode = eciMode == EciMode.Default
            ? DetermineEciMode(plainText)
            : eciMode;

        // Auto-detect optimal encoding mode (Numeric > Alphanumeric > Byte)
        var encoding = GetEncoding(plainText);

        // Select QR code version (auto or manual)
        var dataInputLength = GetDataLength(plainText, encoding, actualEciMode);
        var version = requestedVersion == -1
            ? GetVersion(dataInputLength, encoding, eccLevel, actualEciMode)
            : requestedVersion;

        // Create ECCInfo
        var eccInfo = QRCodeConstants.GetEccInfo(version, eccLevel);

        return new QRConfiguration(version, eccLevel, encoding, actualEciMode, utf8Bom, eccInfo);
    }

    /// <summary>
    /// Encodes the input text into a binary string.
    /// </summary>
    /// <param name="plainText"></param>
    /// <param name="config"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string EncodeData(string plainText, in QRConfiguration config)
    {
        var capacity = CalculateMaxBitStringLength(config.Version, config.EccLevel, config.Encoding);
        var encoder = new QRTextEncoder(capacity);

        var dataInputLength = GetDataLength(plainText, config.Encoding, config.EciMode);

        encoder.WriteMode(config.Encoding, config.EciMode);
        encoder.WriteCharacterCount(dataInputLength, config.Encoding.GetCountIndicatorLength(config.Version));
        encoder.WriteData(plainText, config.Encoding, config.EciMode, config.Utf8BOM);
        encoder.WritePadding(config.EccInfo.TotalDataCodewords * 8);

        var bitString = encoder.ToBinaryString();
        return bitString;
    }

    /// <summary>
    /// Calculates error correction codewords for the given bit string and ECC info.
    /// </summary>
    /// <param name="bitString">The binary string representing the encoded QR code data.</param>
    /// <param name="eccInfo">Error correction information for the QR code version and ECC level.</param>
    /// <returns>A list of codeword blocks containing data and error correction codewords.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static List<CodewordTextBlock> CalculateErrorCorrection(string bitString, in ECCInfo eccInfo)
    {
        var blocks = new List<CodewordTextBlock>(eccInfo.BlocksInGroup1 + eccInfo.BlocksInGroup2);

        // Process group 1 blocks
        var offset = 0;
        for (var i = 0; i < eccInfo.BlocksInGroup1; i++)
        {
            var block = CreateCodewordBlock(bitString, offset, eccInfo.CodewordsInGroup1, eccInfo.ECCPerBlock, groupNumber: 1, blockNumber: i + 1);
            blocks.Add(block);
            offset += eccInfo.CodewordsInGroup1 * 8;
        }

        // Process group 2 blocks
        for (var i = 0; i < eccInfo.BlocksInGroup2; i++)
        {
            var block = CreateCodewordBlock(bitString, offset, eccInfo.CodewordsInGroup2, eccInfo.ECCPerBlock, groupNumber: 2, blockNumber: i + 1);
            blocks.Add(block);
            offset += eccInfo.CodewordsInGroup2 * 8;
        }

        return blocks;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string InterleaveCodewords(List<CodewordTextBlock> blocks, in ECCInfo eccInfo, int version)
    {
        return TextInterleaver.InterleaveCodewords(blocks, eccInfo, version);
    }

    /// <summary>
    /// Creates a codeword block with data and ECC codewords.
    /// </summary>
    /// <param name="bitString">The binary string containing the encoded data.</param>
    /// <param name="offset">The starting bit offset in the binary string for this block.</param>
    /// <param name="codewordCount">The number of data codewords in this block.</param>
    /// <param name="eccEncoder">The ECC encoder used to generate error correction codewords.</param>
    /// <param name="eccPerBlock">The number of ECC codewords to generate for this block.</param>
    /// <param name="groupNumber">The group number (1 or 2) for this block as per QR code specification.</param>
    /// <param name="blockNumber">The sequential block number within the group.</param>
    /// <returns>A <see cref="CodewordBlock"/> containing the data and ECC codewords for this block.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static CodewordTextBlock CreateCodewordBlock(string bitString, int offset, int codewordCount, int eccPerBlock, int groupNumber, int blockNumber)
    {
        var blockBits = bitString.Substring(offset, codewordCount * 8);
        var codeWords = BinaryStringToBitBlockList(blockBits);
        var eccWords = EccTextEncoder.CalculateECC(blockBits, eccPerBlock);
        var codewordBlock = new CodewordTextBlock(groupNumber, blockNumber, blockBits, codeWords, eccWords);

        return codewordBlock;
    }

    /// <summary>
    /// Creates the QR code matrix by placing patterns, data, applying mask, and adding format/version info.
    /// </summary>
    /// <param name="version">The QR code version (1-40) to generate.</param>
    /// <param name="interleavedData">The encoded and interleaved data string to be placed in the QR code.</param>
    /// <param name="eccLevel">The error correction level to use for the QR code.</param>
    /// <returns>A <see cref="QRCodeData"/> object containing the generated QR code matrix.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static QRCodeData CreateQRMatrix(int version, string interleavedData, ECCLevel eccLevel)
    {
        var qrCodeData = new QRCodeData(version);

        // Version 1:  approximately  9
        // Version 7:  approximately 27
        // Version 40: approximately 57
        var blockedModules = new List<Rectangle>(30);

        // place all patterns
        PlacePatterns(ref qrCodeData, version, ref blockedModules);

        // place data
        ModulePlacer.PlaceDataWords(ref qrCodeData, interleavedData, ref blockedModules);

        // Apply mask and format
        ApplyMaskAndFormat(ref qrCodeData, version, eccLevel, ref blockedModules);

        // Place version information (version 7+)
        if (version >= 7)
        {
            var versionBits = QRCodeConstants.GetVersionBits(version);
            ModulePlacer.PlaceVersion(ref qrCodeData, versionBits);
        }

        return qrCodeData;
    }

    /// <summary>
    /// Places all fixed patterns on the QR code matrix and reserves their areas.
    /// </summary>
    /// <param name="qrCodeData">The QRCodeData object representing the QR code matrix to modify.</param>
    /// <param name="version">The QR code version (1-40) determining pattern placement.</param>
    /// <param name="blockedModules">A list of rectangles representing reserved areas in the matrix where patterns are placed.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void PlacePatterns(ref QRCodeData qrCodeData, int version, ref List<Rectangle> blockedModules)
    {
        var alignmentPatternLocations = GetAlignmentPatternPositions(version);

        ModulePlacer.PlaceFinderPatterns(ref qrCodeData, ref blockedModules);
        ModulePlacer.ReserveSeperatorAreas(qrCodeData.Size, ref blockedModules);
        ModulePlacer.PlaceAlignmentPatterns(ref qrCodeData, alignmentPatternLocations, ref blockedModules);
        ModulePlacer.PlaceTimingPatterns(ref qrCodeData, ref blockedModules);
        ModulePlacer.PlaceDarkModule(ref qrCodeData, version, ref blockedModules);
        ModulePlacer.ReserveVersionAreas(qrCodeData.Size, version, ref blockedModules);
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
    /// Applies the optimal mask to the QR code data and places the format information.
    /// </summary>
    /// <param name="qrCodeData">The QRCodeData matrix to apply the mask and format information to.</param>
    /// <param name="version">The QR code version (1-40).</param>
    /// <param name="eccLevel">The error correction level to use.</param>
    /// <param name="blockedModules">A list of rectangles representing modules that should not be masked (reserved areas).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ApplyMaskAndFormat(ref QRCodeData qrCodeData, int version, ECCLevel eccLevel, ref List<Rectangle> blockedModules)
    {
        var maskVersion = ModulePlacer.MaskCode(ref qrCodeData, version, ref blockedModules, eccLevel);
        var formatBit = QRCodeConstants.GetFormatBits(eccLevel, maskVersion);
        ModulePlacer.PlaceFormat(ref qrCodeData, formatBit);
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
        if (version < 1 || version > 40)
            throw new ArgumentOutOfRangeException(nameof(version), $"Version must be 1-40, but was {version}");

        // QR codes are always padded to full capacity with 0xEC/0x11 bytes
        // So the final bit string length = data capacity in bits
        // ECCInfo contains the actual byte capacity (TotalDataCodewords)
        var eccInfo = QRCodeConstants.GetEccInfo(version, eccLevel);
        return eccInfo.TotalDataCodewords * 8; // Convert bytes to bits
    }

    // Binary

    /// <summary>
    /// Prepares QR configuration by determining encoding, ECI mode, and version.
    /// </summary>
    /// <param name="plainText"></param>
    /// <param name="eccLevel"></param>
    /// <param name="utf8Bom"></param>
    /// <param name="eciMode"></param>
    /// <param name="requestedVersion"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static QRConfiguration PrepareConfiguration(ReadOnlySpan<char> textSpan, ECCLevel eccLevel, bool utf8Bom, EciMode eciMode, int requestedVersion)
    {
        // When EciMode.Default is specified, choose encoding based on text content.
        var actualEciMode = eciMode == EciMode.Default
            ? DetermineEciMode(textSpan)
            : eciMode;

        // Auto-detect optimal encoding mode (Numeric > Alphanumeric > Byte)
        var encoding = GetEncoding(textSpan);

        // Select QR code version (auto or manual)
        var dataInputLength = GetDataLength(textSpan, encoding, actualEciMode);
        var version = requestedVersion == -1
            ? GetVersion(dataInputLength, encoding, eccLevel, actualEciMode)
            : requestedVersion;

        // Create ECCInfo
        var eccInfo = QRCodeConstants.GetEccInfo(version, eccLevel);

        return new QRConfiguration(version, eccLevel, encoding, actualEciMode, utf8Bom, eccInfo);
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

        var dataInputLength = GetDataLength(textSpan, config.Encoding, config.EciMode);

        encoder.WriteMode(config.Encoding, config.EciMode);
        encoder.WriteCharacterCount(dataInputLength, config.Encoding.GetCountIndicatorLength(config.Version));
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void InterleaveCodewords(ReadOnlySpan<byte> dataBuffer, ReadOnlySpan<byte> eccBuffer, in ECCInfo eccInfo, int version, Span<byte> output)
    {
        BinaryInterleaver.InterleaveCodewords(dataBuffer, eccBuffer, output, version, eccInfo);
    }

    /// <summary>
    /// Creates the QR code matrix by placing patterns, data, applying mask, and adding format/version info.
    /// </summary>
    /// <param name="version">The QR code version (1-40) to generate.</param>
    /// <param name="interleavedData">The encoded and interleaved data bytes to be placed in the QR code.</param>
    /// <param name="eccLevel">The error correction level to use for the QR code.</param>
    /// <returns>A <see cref="QRCodeData"/> object containing the generated QR code matrix.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static QRCodeData CreateQRMatrix(int version, ReadOnlySpan<byte> interleavedData, ECCLevel eccLevel)
    {
        var qrCodeData = new QRCodeData(version);

        // Version 1:  approximately  9
        // Version 7:  approximately 27
        // Version 40: approximately 57
        var blockedModules = new List<Rectangle>(30);

        // place all patterns
        PlacePatterns(ref qrCodeData, version, ref blockedModules);

        // place data
        ModulePlacer.PlaceDataWords(ref qrCodeData, interleavedData, ref blockedModules);

        // Apply mask and format
        ApplyMaskAndFormat(ref qrCodeData, version, eccLevel, ref blockedModules);

        // Place version information (version 7+)
        if (version >= 7)
        {
            var versionBits = QRCodeConstants.GetVersionBits(version);
            ModulePlacer.PlaceVersion(ref qrCodeData, versionBits);
        }

        return qrCodeData;
    }

    // Utilities

    /// <summary>
    /// Determines appropriate ECI mode based on text content.
    /// </summary>
    /// <param name="plainText">Text to analyze.</param>
    /// <returns>
    /// <list type="bullet">
    /// <item><see cref="EciMode.Default"/> for ASCII-only text (no ECI header)</item>
    /// <item><see cref="EciMode.Iso8859_1"/> for Latin-1 compatible text</item>
    /// <item><see cref="EciMode.Utf8"/> for other Unicode text</item>
    /// </list>
    /// </returns>
    private static EciMode DetermineEciMode(string plainText) => DetermineEciMode(plainText.AsSpan());

    /// <summary>
    /// Determines appropriate ECI mode based on text content.
    /// </summary>
    /// <param name="plainText">Text to analyze.</param>
    /// <returns>
    /// <list type="bullet">
    /// <item><see cref="EciMode.Default"/> for ASCII-only text (no ECI header)</item>
    /// <item><see cref="EciMode.Iso8859_1"/> for Latin-1 compatible text</item>
    /// <item><see cref="EciMode.Utf8"/> for other Unicode text</item>
    /// </list>
    /// </returns>
    private static EciMode DetermineEciMode(ReadOnlySpan<char> textSpan)
    {
        // ASCII-only → No ECI header (backward compatibility)
        if (IsAsciiOnly(textSpan))
        {
            return EciMode.Default;
        }

        // ISO-8859-1 compatible → ECI 3
        if (QRCodeConstants.IsValidISO88591(textSpan))
        {
            return EciMode.Iso8859_1;
        }

        // Unicode (emojis, CJK, etc.) → ECI 26
        return EciMode.Utf8;
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
    private static int GetVersion(int length, EncodingMode encoding, ECCLevel eccLevel, EciMode eciMode)
    {
        // ECI header overhead if eci specified
        var eciHeaderBits = eciMode.GetHeaderBits();

        // Mode indicator (4 bits)
        var modeIndicatorBits = 4;

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
                EncodingMode.Byte => length * 8,
                EncodingMode.Kanji => length * 13, // Kanji: 13 bits per character
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
    /// Determines the optimal encoding mode for given text.
    /// Priority: Numeric (most efficient) > Alphanumeric > Byte (least efficient).
    /// </summary>
    /// <param name="plainText">Text to analyze.</param>
    /// <returns>Optimal encoding mode.</returns>
    private static EncodingMode GetEncoding(string plainText) => GetEncoding(plainText.AsSpan());

    /// <summary>
    /// Determines the optimal encoding mode for given text.
    /// Priority: Numeric (most efficient) > Alphanumeric > Byte (least efficient).
    /// </summary>
    /// <param name="textSpan">Text to analyze.</param>
    /// <returns>Optimal encoding mode.</returns>
    private static EncodingMode GetEncoding(ReadOnlySpan<char> textSpan)
    {
        var result = EncodingMode.Numeric;

        foreach (char c in textSpan)
        {
            if (QRCodeConstants.IsNumeric(c)) continue;

            result = EncodingMode.Alphanumeric;
            if (!QRCodeConstants.IsAlphanumeric(c))
            {
                return EncodingMode.Byte;
            }
        }

        return result;
    }

    /// <summary>
    /// Calculates actual data length for capacity checking.
    /// Returns character count for Numeric/Alphanumeric, byte count for Byte mode.
    /// </summary>
    /// <param name="plainText">Original text.</param>
    /// <param name="encoding">Encoding mode.</param>
    /// <param name="eciMode">ECI mode for character encoding.</param>
    /// <returns>Data length for version selection.</returns>
    private static int GetDataLength(string plainText, EncodingMode encoding, EciMode eciMode) => GetDataLength(plainText.AsSpan(), encoding, eciMode);

    /// <summary>
    /// Calculates actual data length for capacity checking.
    /// Returns character count for Numeric/Alphanumeric, byte count for Byte mode.
    /// </summary>
    /// <param name="textSpan">Original text.</param>
    /// <param name="encoding">Encoding mode.</param>
    /// <param name="eciMode">ECI mode for character encoding.</param>
    /// <returns>Data length for version selection.</returns>
    private static int GetDataLength(ReadOnlySpan<char> textSpan, EncodingMode encoding, EciMode eciMode)
    {
        return encoding switch
        {
            EncodingMode.Numeric => textSpan.Length,
            EncodingMode.Alphanumeric => textSpan.Length,
            EncodingMode.Byte => CalculateByteCount(textSpan, eciMode),
            EncodingMode.Kanji => textSpan.Length,  // Not implemented
            _ => textSpan.Length
        };

        static int CalculateByteCount(ReadOnlySpan<char> textSpan, EciMode eciMode)
        {
#if NETSTANDARD2_1_OR_GREATER
            ReadOnlySpan<char> input = textSpan;
#else
            string input = textSpan.ToString();
#endif
            // ISO-8859-x encoding based on ECI mode
            return eciMode switch
            {
                EciMode.Default => QRCodeConstants.IsValidISO88591(input)
                    ? Encoding.GetEncoding("ISO-8859-1").GetByteCount(input)
                    : Encoding.UTF8.GetByteCount(input),
                EciMode.Iso8859_1 => Encoding.GetEncoding("ISO-8859-1").GetByteCount(input),
                EciMode.Utf8 => Encoding.UTF8.GetByteCount(input),
                _ => throw new ArgumentOutOfRangeException(nameof(eciMode), "Unsupported ECI mode for Byte encoding"),
            };
        }
    }

    /// <summary>
    /// Validates if text contains only ASCII characters (0-127).
    /// </summary>
    /// <param name="text">The string to check for ASCII-only characters.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsAsciiOnly(string text) => IsAsciiOnly(text.AsSpan());

    /// <summary>
    /// Validates if text contains only ASCII characters (0-127).
    /// </summary>
    /// <param name="textSpan">The string to check for ASCII-only characters.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsAsciiOnly(ReadOnlySpan<char> textSpan)
    {
        foreach (var c in textSpan)
        {
            if (c > 127) return false;
        }
        return true;
    }

    /// <summary>
    /// Converts binary string (8-bit blocks) to list of binary strings.
    /// Example: "110100111010110000010001" → ["11010011", "10101100", "00010001"]
    /// </summary>
    private static List<string> BinaryStringToBitBlockList(string bitString)
    {
        if (bitString.Length % 8 != 0)
        {
            var remainder = bitString.Length % 8;
            throw new ArgumentException($"Binary string length must be a multiple of 8. "
                + $"Length: {bitString.Length}, Remainder: {remainder} bits. "
                + $"Data may be corrupted or improperly padded.",
                nameof(bitString));
        }

        var byteCount = bitString.Length / 8;
        var result = new List<string>(byteCount);
        for (var i = 0; i < byteCount; i++)
        {
            result.Add(bitString.Substring(i * 8, 8));
        }
        return result;
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
    private readonly record struct QRConfiguration(int Version, ECCLevel EccLevel, EncodingMode Encoding, EciMode EciMode, bool Utf8BOM, in ECCInfo EccInfo);

    public void Dispose()
    {
        // will be removed in future, or remain.
    }
}
