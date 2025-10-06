using System.Runtime.CompilerServices;
using System.Text;
using SkiaSharp.QrCode.Internals;
using static SkiaSharp.QrCode.Internals.QRCodeConstants;

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
        var interleavedData = InterleavedDataCapacity(codewordBlocks, config.Version, config.EccInfo);

        // Create QR code matrix
        var qrMatrix = CreateQRMatrix(config.Version, interleavedData, config.EccLevel);

        // Add quiet zone
        ModulePlacer.AddQuietZone(ref qrMatrix, quietZoneSize);

        return qrMatrix;
    }

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
        var encoding = GetEncodingFromPlaintext(plainText);

        // Select QR code version (auto or manual)
        var dataInputLength = GetDataLength(encoding, plainText, actualEciMode);
        var version = requestedVersion == -1
            ? GetVersion(dataInputLength, encoding, eccLevel, actualEciMode)
            : requestedVersion;

        // Create ECCInfo
        var eccInfo = CapacityECCTable.Single(x => x.Version == version && x.ErrorCorrectionLevel == eccLevel);

        return new QRConfiguration(version, eccLevel, encoding, actualEciMode, utf8Bom, eccInfo);
    }

    /// <summary>
    /// Encodes the input text into a binary string based on the specified QR configuration.
    /// </summary>
    /// <param name="plainText"></param>
    /// <param name="config"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string EncodeData(string plainText, QRConfiguration config)
    {
        var capacity = CalculateMaxBitStringLength(config.Version, config.EccLevel, config.Encoding);
        var encoder = new QRTextEncoder(capacity);

        var dataInputLength = GetDataLength(config.Encoding, plainText, config.EciMode);

        encoder.WriteMode(config.Encoding, config.EciMode);
        encoder.WriteCharacterCount(dataInputLength, config.Version, config.Encoding);
        encoder.WriteData(plainText, config.Encoding, config.EciMode, config.Utf8BOM);
        encoder.WritePadding(config.EccInfo.TotalDataCodewords * 8);

        var bitString = encoder.ToBinaryString();
        return bitString;
    }

    /// <summary>
    /// Calculates error correction codewords for the given bit string and ECC info.
    /// </summary>
    /// <param name="bitString"></param>
    /// <param name="eccInfo"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static List<CodewordBlock> CalculateErrorCorrection(string bitString, in ECCInfo eccInfo)
    {
        var eccEncoder = new EccTextEncoder();
        var blocks = new List<CodewordBlock>(eccInfo.BlocksInGroup1 + eccInfo.BlocksInGroup2);

        // Process group 1 blocks
        var offset = 0;
        for (var i = 0; i < eccInfo.BlocksInGroup1; i++)
        {
            var block = CreateCodewordBlock(bitString, offset, eccInfo.CodewordsInGroup1, eccEncoder, eccInfo.ECCPerBlock, groupNumber: 1, blockNumber: i + 1);
            blocks.Add(block);
            offset += eccInfo.CodewordsInGroup1 * 8;
        }

        // Process group 2 blocks
        for (var i = 0; i < eccInfo.BlocksInGroup2; i++)
        {
            var block = CreateCodewordBlock(bitString, offset, eccInfo.CodewordsInGroup2, eccEncoder, eccInfo.ECCPerBlock, groupNumber: 2, blockNumber: i + 1);
            blocks.Add(block);
            offset += eccInfo.CodewordsInGroup2 * 8;
        }

        return blocks;
    }

    /// <summary>
    /// Creates a codeword block with data and ECC codewords.
    /// </summary>
    /// <param name="bitString"></param>
    /// <param name="offset"></param>
    /// <param name="codewordCount"></param>
    /// <param name="eccEncoder"></param>
    /// <param name="eccPerBlock"></param>
    /// <param name="groupNumber"></param>
    /// <param name="blockNumber"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static CodewordBlock CreateCodewordBlock(string bitString, int offset, int codewordCount, EccTextEncoder eccEncoder, int eccPerBlock, int groupNumber, int blockNumber)
    {
        var blockBits = bitString.Substring(offset, codewordCount * 8);
        var codeWords = BinaryStringToBitBlockList(blockBits);
        var codeWordsInt = BinaryStringListToDecList(codeWords);

        var eccWords = eccEncoder.CalculateECC(blockBits, eccPerBlock);
        var eccWordListDec = BinaryStringListToDecList(eccWords);
        var codewordBlock = new CodewordBlock(groupNumber, blockNumber, blockBits, codeWords, eccWords, codeWordsInt, eccWordListDec);

        return codewordBlock;
    }

    /// <summary>
    /// Calculates the final interleaved data capacity including data codewords, ECC codewords, and remainder bits.
    /// </summary>
    /// <param name="blocks"></param>
    /// <param name="version"></param>
    /// <param name="eccInfo"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string InterleavedDataCapacity(List<CodewordBlock> blocks, int version, in ECCInfo eccInfo)
    {
        var interleaveCapacity = CalculateInterleavedDataCapacity(version, eccInfo);
        var result = new StringBuilder(interleaveCapacity);
        var maxCodewordCount = Math.Max(eccInfo.CodewordsInGroup1, eccInfo.CodewordsInGroup2);

        // Interleave data codewords
        for (var i = 0; i < maxCodewordCount; i++)
        {
            foreach (var codeBlock in blocks)
            {
                if (codeBlock.CodeWords.Count > i)
                {
                    result.Append(codeBlock.CodeWords[i]);
                }
            }
        }
        // Interleave ECC codewords
        for (var i = 0; i < eccInfo.ECCPerBlock; i++)
        {
            foreach (var codeBlock in blocks)
            {
                if (codeBlock.ECCWords.Count > i)
                {
                    result.Append(codeBlock.ECCWords[i]);
                }
            }
        }
        // Add remainder bits
        var remainderBitsCount = GetRemainderBits(version);
        if (remainderBitsCount > 0)
        {
            result.Append('0', remainderBitsCount);
        }

        return result.ToString();
    }

    /// <summary>
    /// Creates the QR code matrix by placing patterns, data, applying mask, and adding format/version info. 
    /// </summary>
    /// <param name="version"></param>
    /// <param name="interleavedData"></param>
    /// <param name="eccLevel"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static QRCodeData CreateQRMatrix(int version, string interleavedData, ECCLevel eccLevel)
    {
        var qrCodeData = new QRCodeData(version);
        var blockedModules = new List<Rectangle>();

        // place all patterns
        PlacePatterns(ref qrCodeData, version, ref blockedModules);

        // place data
        ModulePlacer.PlaceDataWords(ref qrCodeData, interleavedData, ref blockedModules);

        // Apply mask and format
        ApplyMaskAndFormat(ref qrCodeData, version, eccLevel, ref blockedModules);

        // Place version information (version 7+)
        if (version >= 7)
        {
            var versionString = GetVersionString(version);
            ModulePlacer.PlaceVersion(ref qrCodeData, versionString);
        }

        return qrCodeData;
    }

    /// <summary>
    /// Places all fixed patterns on the QR code matrix and reserves their areas.
    /// </summary>
    /// <param name="qrCodeData"></param>
    /// <param name="version"></param>
    /// <param name="blockedModules"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void PlacePatterns(ref QRCodeData qrCodeData, int version, ref List<Rectangle> blockedModules)
    {
        ModulePlacer.PlaceFinderPatterns(ref qrCodeData, ref blockedModules);
        ModulePlacer.ReserveSeperatorAreas(qrCodeData.Size, ref blockedModules);
        var alignmentPatternLocations = AlignmentPatternTable.Where(x => x.Version == version).Select(x => x.PatternPositions).First();
        ModulePlacer.PlaceAlignmentPatterns(ref qrCodeData, alignmentPatternLocations, ref blockedModules);
        ModulePlacer.PlaceTimingPatterns(ref qrCodeData, ref blockedModules);
        ModulePlacer.PlaceDarkModule(ref qrCodeData, version, ref blockedModules);
        ModulePlacer.ReserveVersionAreas(qrCodeData.Size, version, ref blockedModules);
    }

    /// <summary>
    /// Applies the optimal mask to the QR code data and places the format information.
    /// </summary>
    /// <param name="qrCodeData"></param>
    /// <param name="version"></param>
    /// <param name="eccLevel"></param>
    /// <param name="blockedModules"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ApplyMaskAndFormat(ref QRCodeData qrCodeData, int version, ECCLevel eccLevel, ref List<Rectangle> blockedModules)
    {
        var maskVersion = ModulePlacer.MaskCode(ref qrCodeData, version, ref blockedModules, eccLevel);
        var formatStr = GetFormatString(eccLevel, maskVersion);
        ModulePlacer.PlaceFormat(ref qrCodeData, formatStr);
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
        var eccInfo = CapacityECCTable.Single(x => x.Version == version && x.ErrorCorrectionLevel == eccLevel);
        return eccInfo.TotalDataCodewords * 8; // Convert bytes to bits
    }

    internal static int CalculateInterleavedDataCapacity(int version, in ECCInfo eccInfo)
    {
        // -----------------------------------------------------
        // QR Code Interleaved data structure (ISO/IEC 18004):
        // -----------------------------------------------------
        //
        // ┌──────────────────────────────────────────────────┐
        // │ Interleaved Data Codewords                       │
        // │ (TotalDataCodewords × 8 bits)                    │
        // ├──────────────────────────────────────────────────┤
        // │ Interleaved ECC Codewords                        │
        // │ (ECCPerBlock × TotalBlocks × 8 bits)             │
        // ├──────────────────────────────────────────────────┤
        // │ Remainder Bits                                   │
        // │ (0-7 bits, version dependent)                    │
        // └──────────────────────────────────────────────────┘
        //
        // -----------------------------------------------------
        // // ECCInfo for Version 5, Q
        // eccInfo = {
        //     Version = 5,
        //     ErrorCorrectionLevel = Q,
        //     TotalDataCodewords = 80,       // Data capacity
        //     ECCPerBlock = 18,              // ECC Word count per block
        //     BlocksInGroup1 = 2,            // Group 1 block count
        //     CodewordsInGroup1 = 15,        // Group 1 data word count per block
        //     BlocksInGroup2 = 2,            // Group 2 block count
        //     CodewordsInGroup2 = 16,        // Group 2 data word count per block
        // };
        //
        // // Calculation:
        // var dataCodewordsBits = 80 * 8 = 640 bits
        // var eccCodewordsBits = 18 * (2 + 2) * 8 = 576 bits
        // var remainderBits = GetRemainderBits(5) = 0 bits  // Version 5 has no module remainder bits
        //
        // Total = 640 + 576 + 0 = 1,216 bits (152 bytes)
        // -----------------------------------------------------

        var dataCodewordsBits = eccInfo.TotalDataCodewords * 8;
        var eccCodewordsBits = eccInfo.ECCPerBlock
            * (eccInfo.BlocksInGroup1 + eccInfo.BlocksInGroup2)
            * 8;
        var remainderBits = GetRemainderBits(version);
        return dataCodewordsBits + eccCodewordsBits + remainderBits;
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
    private static EciMode DetermineEciMode(string plainText)
    {
        // ASCII-only → No ECI header (backward compatibility)
        if (IsAsciiOnly(plainText))
        {
            return EciMode.Default;
        }

        // ISO-8859-1 compatible → ECI 3
        if (IsValidISO88591(plainText))
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
            var eccInfo = CapacityECCTable.Single(x => x.Version == version && x.ErrorCorrectionLevel == eccLevel);
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
    private static EncodingMode GetEncodingFromPlaintext(string plainText)
    {
        var result = EncodingMode.Numeric;

        foreach (char c in plainText)
        {
            if (IsNumeric(c)) continue;

            result = EncodingMode.Alphanumeric;
            if (!IsAlphanumeric(c))
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
    /// <param name="encoding">Encoding mode.</param>
    /// <param name="plainText">Original text.</param>
    /// <param name="eciMode">ECI mode for character encoding.</param>
    /// <returns>Data length for version selection.</returns>
    private static int GetDataLength(EncodingMode encoding, string plainText, EciMode eciMode)
    {
        return encoding switch
        {
            EncodingMode.Numeric => plainText.Length,
            EncodingMode.Alphanumeric => plainText.Length,
            EncodingMode.Byte => CalculateByteCount(plainText, eciMode),
            EncodingMode.Kanji => plainText.Length,  // Not implemented
            _ => plainText.Length
        };

        static int CalculateByteCount(string plainText, EciMode eciMode)
        {
            // ISO-8859-x encoding based on ECI mode
            return eciMode switch
            {
                EciMode.Default => IsValidISO88591(plainText)
                    ? Encoding.GetEncoding("ISO-8859-1").GetByteCount(plainText)
                    : Encoding.UTF8.GetByteCount(plainText),
                EciMode.Iso8859_1 => Encoding.GetEncoding("ISO-8859-1").GetByteCount(plainText),
                EciMode.Utf8 => Encoding.UTF8.GetByteCount(plainText),
                _ => throw new ArgumentOutOfRangeException(nameof(eciMode), "Unsupported ECI mode for Byte encoding"),
            };
        }
    }

    /// <summary>
    /// Validates if text contains only ASCII characters (0-127).
    /// </summary>
    /// <param name="text">The string to check for ASCII-only characters.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsAsciiOnly(string text)
    {
        foreach (var c in text)
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
    /// Converts list of binary strings to list of decimal integers.
    /// Example: ["11010011", "10101100"] → [211, 172]
    /// </summary>
    private static List<int> BinaryStringListToDecList(List<string> binaryStringList)
    {
        var result = new List<int>(binaryStringList.Count);
        foreach (var item in binaryStringList)
        {
            result.Add(BinToDec(item));
        }
        return result;
    }

    /// <summary>
    /// Represents a codeword block in the interleaving process.
    /// QR codes split data into multiple blocks for error correction.
    /// </summary>
    private struct CodewordBlock
    {
        public CodewordBlock(int groupNumber, int blockNumber, string bitString, List<string> codeWords,
            List<string> eccWords, List<int> codeWordsInt, List<int> eccWordsInt)
        {
            GroupNumber = groupNumber;
            BlockNumber = blockNumber;
            BitString = bitString;
            CodeWords = codeWords;
            ECCWords = eccWords;
            CodeWordsInt = codeWordsInt;
            ECCWordsInt = eccWordsInt;
        }

        public int GroupNumber { get; }
        public int BlockNumber { get; }
        public string BitString { get; }
        public List<string> CodeWords { get; }
        public List<int> CodeWordsInt { get; }
        public List<string> ECCWords { get; }
        public List<int> ECCWordsInt { get; }
    }

    private record QRConfiguration(int Version, ECCLevel EccLevel, EncodingMode Encoding, EciMode EciMode, bool Utf8BOM, ECCInfo EccInfo);

    public void Dispose()
    {
        // will be removed in future, or remain.
    }
}
