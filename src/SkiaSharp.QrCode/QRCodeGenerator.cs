using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    /// <remarks>
    /// QR code generation process:
    /// 1. Determine optimal encoding mode (Numeric/Alphanumeric/Byte)
    /// 2. Encode data to binary string
    /// 3. Select QR code version (based on data length and ECC level)
    /// 4. Add mode indicator and character count indicator
    /// 5. Pad data to fill code word capacity
    /// 6. Calculate error correction codewords using Reed-Solomon
    /// 7. Interleave data and ECC codewords
    /// 8. Place finder patterns, alignment patterns, timing patterns
    /// 9. Place data modules in zigzag pattern
    /// 10. Apply optimal mask pattern (test all 8 patterns)
    /// 11. Add format and version information
    /// 12. Add quiet zone (white border)
    /// </remarks>
    public QRCodeData CreateQrCode(string plainText, ECCLevel eccLevel, bool utf8BOM = false, EciMode eciMode = EciMode.Default, int requestedVersion = -1, int quietZoneSize = 4)
    {
        // Auto-detect optimal encoding mode (Numeric > Alphanumeric > Byte)
        EncodingMode encoding = GetEncodingFromPlaintext(plainText);
        var dataInputLength = GetDataLength(encoding, plainText, eciMode);

        // Select QR code version (auto or manual)
        int version = requestedVersion;
        if (version == -1)
        {
            version = GetVersion(dataInputLength, encoding, eccLevel);
        }

        var eccInfo = CapacityECCTable.Single(x => x.Version == version && x.ErrorCorrectionLevel == eccLevel);

        // Encode data
        var capacity = CalculateMaxBitStringLength(version, eccLevel, encoding);
        var qrEncoder = new QRTextEncoder(capacity);
        qrEncoder.WriteMode(encoding, eciMode);
        qrEncoder.WriteCharacterCount(dataInputLength, version, encoding);
        qrEncoder.WriteData(plainText, encoding, eciMode, utf8BOM);
        qrEncoder.WritePadding(eccInfo.TotalDataCodewords * 8);
        var bitString = qrEncoder.ToBinaryString();

        // Calculate ECC
        var eccEncoder = new EccTextEncoder();
        var codeWordWithECC = new List<CodewordBlock>();
        // Process group 1 blocks
        for (var i = 0; i < eccInfo.BlocksInGroup1; i++)
        {
            var bitStr = bitString.Substring(i * eccInfo.CodewordsInGroup1 * 8, eccInfo.CodewordsInGroup1 * 8);
            var bitBlockList = BinaryStringToBitBlockList(bitStr);
            var bitBlockListDec = BinaryStringListToDecList(bitBlockList);

            var eccWordList = eccEncoder.CalculateECC(bitStr, eccInfo.ECCPerBlock);
            var eccWordListDec = BinaryStringListToDecList(eccWordList);
            var codewordBlock = new CodewordBlock(1, i + 1, bitStr, bitBlockList, eccWordList, bitBlockListDec, eccWordListDec);
            codeWordWithECC.Add(codewordBlock);
        }
        bitString = bitString.Substring(eccInfo.BlocksInGroup1 * eccInfo.CodewordsInGroup1 * 8);
        // Process group 2 blocks
        for (var i = 0; i < eccInfo.BlocksInGroup2; i++)
        {
            var blockData = bitString.Substring(i * eccInfo.CodewordsInGroup2 * 8, eccInfo.CodewordsInGroup2 * 8);
            var bitBlockList = BinaryStringToBitBlockList(blockData);
            var bitBlockListDec = BinaryStringListToDecList(bitBlockList);

            var eccWordList = eccEncoder.CalculateECC(blockData, eccInfo.ECCPerBlock);
            var eccWordListDec = BinaryStringListToDecList(eccWordList);
            var codewordBlock = new CodewordBlock(2, i + 1, blockData, bitBlockList, eccWordList, bitBlockListDec, eccWordListDec);
            codeWordWithECC.Add(codewordBlock);
        }

        // Interleave code words % module placement
        var interleaveCapacity = CalculateInterleavedDataCapacity(version, eccInfo);
        var interleavedWordsSb = new StringBuilder(interleaveCapacity);
        var maxCodewordCount = Math.Max(eccInfo.CodewordsInGroup1, eccInfo.CodewordsInGroup2);
        // Interleave data codewords
        for (var i = 0; i < maxCodewordCount; i++)
        {
            foreach (var codeBlock in codeWordWithECC)
            {
                if (codeBlock.CodeWords.Count > i)
                {
                    interleavedWordsSb.Append(codeBlock.CodeWords[i]);
                }
            }
        }
        // Interleave ECC codewords
        for (var i = 0; i < eccInfo.ECCPerBlock; i++)
        {
            foreach (var codeBlock in codeWordWithECC)
            {
                if (codeBlock.ECCWords.Count > i)
                {
                    interleavedWordsSb.Append(codeBlock.ECCWords[i]);
                }
            }
        }
        // Add remainder bits
        var remainderBitsCount = GetRemainderBits(version);
        if (remainderBitsCount > 0)
        {
            interleavedWordsSb.Append('0', remainderBitsCount);
        }
        var interleavedData = interleavedWordsSb.ToString();

        // Place all patterns and data on QR code matrix
        var qr = new QRCodeData(version);
        var blockedModules = new List<Rectangle>();

        ModulePlacer.PlaceFinderPatterns(ref qr, ref blockedModules);
        ModulePlacer.ReserveSeperatorAreas(qr.ModuleMatrix.Count, ref blockedModules);
        ModulePlacer.PlaceAlignmentPatterns(ref qr, AlignmentPatternTable.Where(x => x.Version == version).Select(x => x.PatternPositions).First(), ref blockedModules);
        ModulePlacer.PlaceTimingPatterns(ref qr, ref blockedModules);
        ModulePlacer.PlaceDarkModule(ref qr, version, ref blockedModules);
        ModulePlacer.ReserveVersionAreas(qr.ModuleMatrix.Count, version, ref blockedModules);
        ModulePlacer.PlaceDataWords(ref qr, interleavedData, ref blockedModules);

        // Apply mask and get optimal mask number
        var maskVersion = ModulePlacer.MaskCode(ref qr, version, ref blockedModules, eccLevel);
        var formatStr = GetFormatString(eccLevel, maskVersion);
        ModulePlacer.PlaceFormat(ref qr, formatStr);

        // Place version information (version 7+)
        if (version >= 7)
        {
            var versionString = GetVersionString(version);
            ModulePlacer.PlaceVersion(ref qr, versionString);
        }

        // Add quiet zone
        ModulePlacer.AddQuietZone(ref qr, quietZoneSize);

        return qr;
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

    internal static int CalculateInterleavedDataCapacity(int version, ECCInfo eccInfo)
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

    // Format and Version String Generation

    /// <summary>
    /// Generates 15-bit format information string.
    /// Contains ECC level and mask pattern with error correction.
    /// Formula: (ECC bits + mask bits) + BCH(15,5) error correction + XOR mask
    /// </summary>
    /// <param name="level">Error correction level.</param>
    /// <param name="maskVersion">Mask pattern version (0-7).</param>
    /// <returns>15-bit format string.</returns>
    private static string GetFormatString(ECCLevel level, int maskVersion)
    {
        var generator = "10100110111";
        var fStrMask = "101010000010010";

        var fStr = (level == ECCLevel.L) ? "01" : (level == ECCLevel.M) ? "00" : (level == ECCLevel.Q) ? "11" : "10";
        fStr += DecToBin(maskVersion, 3);
        var fStrEcc = fStr.PadRight(15, '0').TrimStart('0');
        while (fStrEcc.Length > 10)
        {
            var sb = new StringBuilder();
            generator = generator.PadRight(fStrEcc.Length, '0');
            for (var i = 0; i < fStrEcc.Length; i++)
            {
                sb.Append((Convert.ToInt32(fStrEcc[i]) ^ Convert.ToInt32(generator[i])).ToString());
            }
            fStrEcc = sb.ToString().TrimStart('0');
        }
        fStrEcc = fStrEcc.PadLeft(10, '0');
        fStr += fStrEcc;

        var sbMask = new StringBuilder();
        for (var i = 0; i < fStr.Length; i++)
            sbMask.Append((Convert.ToInt32(fStr[i]) ^ Convert.ToInt32(fStrMask[i])).ToString());
        return sbMask.ToString();
    }

    /// <summary>
    /// Generates 18-bit version information string (for version 7+).
    /// Contains version number with error correction.
    /// Formula: (6 bits version) + BCH(18,6) error correction
    /// </summary>
    /// <param name="version">QR code version (7-40).</param>
    /// <returns>18-bit version string.</returns>
    private static string GetVersionString(int version)
    {
        var generator = "1111100100101";

        var vStr = DecToBin(version, 6);
        var vStrEcc = vStr.PadRight(18, '0').TrimStart('0');
        while (vStrEcc.Length > 12)
        {
            var sb = new StringBuilder();
            generator = generator.PadRight(vStrEcc.Length, '0');
            for (var i = 0; i < vStrEcc.Length; i++)
            {
                sb.Append((Convert.ToInt32(vStrEcc[i]) ^ Convert.ToInt32(generator[i])).ToString());
            }
            vStrEcc = sb.ToString().TrimStart('0');
        }
        vStrEcc = vStrEcc.PadLeft(12, '0');
        vStr += vStrEcc;

        return vStr;
    }

    // Utilities

    /// <summary>
    /// Determines the minimum QR code version required for given data length.
    /// Searches capacity table for smallest version that can hold the data.
    /// </summary>
    /// <param name="length">Data length (in characters or bytes).</param>
    /// <param name="encMode">Encoding mode being used.</param>
    /// <param name="eccLevel">Error correction level.</param>
    /// <returns>Version number (1-40).</returns>
    private int GetVersion(int length, EncodingMode encMode, ECCLevel eccLevel)
    {
        var version = CapacityTable
            .Where(x => x.Details
                .Count(y => (y.ErrorCorrectionLevel == eccLevel && y.CapacityDict[encMode] >= Convert.ToInt32(length))) > 0)
            .Select(x => new
            {
                version = x.Version,
                capacity = x.Details.Single(y => y.ErrorCorrectionLevel == eccLevel).CapacityDict[encMode]
            })
            .Min(x => x.version);
        return version;
    }

    /// <summary>
    /// Determines the optimal encoding mode for given text.
    /// Priority: Numeric (most efficient) > Alphanumeric > Byte (least efficient).
    /// </summary>
    /// <param name="plainText">Text to analyze.</param>
    /// <returns>Optimal encoding mode.</returns>
    private EncodingMode GetEncodingFromPlaintext(string plainText)
    {
        EncodingMode result = EncodingMode.Numeric;

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
    private int GetDataLength(EncodingMode encoding, string plainText, EciMode eciMode)
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
                EciMode.Default => IsValidISO(plainText)
                    ? Encoding.GetEncoding("ISO-8859-1").GetByteCount(plainText)
                    : Encoding.UTF8.GetByteCount(plainText),
                EciMode.Iso8859_1 => Encoding.GetEncoding("ISO-8859-1").GetByteCount(plainText),
                EciMode.Utf8 => Encoding.UTF8.GetByteCount(plainText),
                _ => throw new ArgumentOutOfRangeException(nameof(eciMode), "Unsupported ECI mode for Byte encoding"),
            };
        }
    }

    /// <summary>
    /// Validates if text can be encoded in ISO-8859-1 without data loss.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsValidISO(string input)
    {
        var bytes = Encoding.GetEncoding("ISO-8859-1").GetBytes(input);
        //var result = Encoding.GetEncoding("ISO-8859-1").GetString(bytes);
        var result = Encoding.GetEncoding("ISO-8859-1").GetString(bytes, 0, bytes.Length);
        return string.Equals(input, result);
    }

    /// <summary>
    /// Converts binary string to decimal integer.
    /// </summary>
    private int BinToDec(string binStr)
    {
        return Convert.ToInt32(binStr, 2);
    }

    /// <summary>
    /// Converts decimal number to binary string with optional padding.
    /// </summary>
    /// <param name="decNum">Decimal number.</param>
    /// <param name="padLeftUpTo">Minimum bit length (pads with leading zeros).</param>
    /// <returns>Binary string.</returns>
    private static string DecToBin(int decNum, int padLeftUpTo)
    {
        var binStr = Convert.ToString(decNum, 2);
        return binStr.PadLeft(padLeftUpTo, '0');
    }

    /// <summary>
    /// Converts binary string (8-bit blocks) to list of binary strings.
    /// Example: "110100111010110000010001" → ["11010011", "10101100", "00010001"]
    /// </summary>
    private List<string> BinaryStringToBitBlockList(string bitString)
    {
        return new List<char>(bitString.ToCharArray())
            .Select((x, i) => new { Index = i, Value = x })
            .GroupBy(x => x.Index / 8)
            .Select(x => string.Join("", x.Select(v => v.Value.ToString()).ToArray()))
            .ToList();
    }

    /// <summary>
    /// Converts list of binary strings to list of decimal integers.
    /// Example: ["11010011", "10101100"] → [211, 172]
    /// </summary>
    private List<int> BinaryStringListToDecList(List<string> binaryStringList)
    {
        return binaryStringList
            .Select(binaryString => BinToDec(binaryString))
            .ToList();
    }

    /// <summary>
    /// Static utility class for placing QR code modules (patterns and data).
    /// Handles all matrix manipulation operations during QR code generation.
    /// </summary>
    private static class ModulePlacer
    {
        /// <summary>
        /// Adds white border (quiet zone) around the QR code.
        /// Required by ISO/IEC 18004 standard (minimum 4 modules width).
        /// </summary>
        /// <param name="qrCode">QR code data to modify.</param>
        /// <param name="quietZoneSize">Quiet zone width in modules (default: 4).</param>
        public static void AddQuietZone(ref QRCodeData qrCode, int quietZoneSize)
        {
            if (quietZoneSize <= 0)
            {
                return;
            }

            var quietLine = new bool[qrCode.ModuleMatrix.Count + quietZoneSize * 2];
            for (var i = 0; i < quietLine.Length; i++)
            {
                quietLine[i] = false;
            }
            for (var i = 0; i < quietZoneSize; i++)
            {
                qrCode.ModuleMatrix.Insert(0, new BitArray(quietLine));
            }
            for (var i = 0; i < quietZoneSize; i++)
            {
                qrCode.ModuleMatrix.Add(new BitArray(quietLine));
            }
            for (var i = quietZoneSize; i < qrCode.ModuleMatrix.Count - quietZoneSize; i++)
            {
                bool[] quietPart = new bool[quietZoneSize];
                var tmpLine = new List<bool>(quietPart);
                tmpLine.AddRange(qrCode.ModuleMatrix[i].Cast<bool>());
                tmpLine.AddRange(quietPart);
                qrCode.ModuleMatrix[i] = new BitArray(tmpLine.ToArray());
            }
        }

        /// <summary>
        /// Reverses a string character by character.
        /// Used for version and format string bit ordering.
        /// </summary>
        private static string ReverseString(string inp)
        {
            string newStr = string.Empty;
            if (inp.Length > 0)
            {
                for (int i = inp.Length - 1; i >= 0; i--)
                {
                    newStr += inp[i];
                }
            }
            return newStr;
        }

        /// <summary>
        /// Places version information patterns (for QR code version 7 and above).
        /// Two identical 3×6 patterns placed at top-right and bottom-left corners.
        /// </summary>
        public static void PlaceVersion(ref QRCodeData qrCode, string versionStr)
        {
            var size = qrCode.ModuleMatrix.Count;
            var vStr = ReverseString(versionStr);
            for (var x = 0; x < 6; x++)
            {
                for (var y = 0; y < 3; y++)
                {
                    qrCode.ModuleMatrix[y + size - 11][x] = vStr[x * 3 + y] == '1';
                    qrCode.ModuleMatrix[x][y + size - 11] = vStr[x * 3 + y] == '1';
                }
            }
        }

        /// <summary>
        /// Places format information patterns around finder patterns.
        /// Contains error correction level and mask pattern information.
        /// Two identical 15-bit sequences for redundancy.
        /// </summary>
        public static void PlaceFormat(ref QRCodeData qrCode, string formatStr)
        {
            var size = qrCode.ModuleMatrix.Count;
            var fStr = ReverseString(formatStr);
            var modules = new[,]
            {
                { 8, 0, size - 1, 8 },
                { 8, 1, size - 2, 8 },
                { 8, 2, size - 3, 8 },
                { 8, 3, size - 4, 8 },
                { 8, 4, size - 5, 8 },
                { 8, 5, size - 6, 8 },
                { 8, 7, size - 7, 8 },
                { 8, 8, size - 8, 8 },
                { 7, 8, 8, size - 7 },
                { 5, 8, 8, size - 6 },
                { 4, 8, 8, size - 5 },
                { 3, 8, 8, size - 4 },
                { 2, 8, 8, size - 3 },
                { 1, 8, 8, size - 2 },
                { 0, 8, 8, size - 1 }
            };
            for (var i = 0; i < 15; i++)
            {
                var p1 = new Point(modules[i, 0], modules[i, 1]);
                var p2 = new Point(modules[i, 2], modules[i, 3]);
                qrCode.ModuleMatrix[p1.Y][p1.X] = fStr[i] == '1';
                qrCode.ModuleMatrix[p2.Y][p2.X] = fStr[i] == '1';
            }
        }

        /// <summary>
        /// Applies mask pattern to data area and selects optimal pattern.
        /// Tests all 8 mask patterns and selects one with lowest penalty score.
        /// </summary>
        /// <returns>Selected mask pattern number (0-7).</returns>
        public static int MaskCode(ref QRCodeData qrCode, int version, ref List<Rectangle> blockedModules, ECCLevel eccLevel)
        {
            var patternName = string.Empty;
            var patternScore = 0;
            var size = qrCode.ModuleMatrix.Count;

            var methods = typeof(MaskPattern).GetTypeInfo().DeclaredMethods;

            foreach (var pattern in methods)
            {
                if (pattern.Name.Length == 8 && pattern.Name.Substring(0, 7) == "Pattern")
                {
                    var qrTemp = new QRCodeData(version);
                    for (var y = 0; y < size; y++)
                    {
                        for (var x = 0; x < size; x++)
                        {
                            qrTemp.ModuleMatrix[y][x] = qrCode.ModuleMatrix[y][x];
                        }
                    }

                    var formatStr = GetFormatString(eccLevel, Convert.ToInt32((pattern.Name.Substring(7, 1))) - 1);
                    PlaceFormat(ref qrTemp, formatStr);
                    if (version >= 7)
                    {
                        var versionString = GetVersionString(version);
                        PlaceVersion(ref qrTemp, versionString);
                    }

                    for (var x = 0; x < size; x++)
                    {
                        for (var y = 0; y < size; y++)
                        {
                            if (!IsBlocked(new Rectangle(x, y, 1, 1), blockedModules))
                            {
                                qrTemp.ModuleMatrix[y][x] ^= (bool)pattern.Invoke(null, [x, y]);
                            }
                        }
                    }

                    var score = MaskPattern.Score(ref qrTemp);
                    if (string.IsNullOrEmpty(patternName) || patternScore > score)
                    {
                        patternName = pattern.Name;
                        patternScore = score;
                    }
                }
            }

            var patterMethod = typeof(MaskPattern).GetTypeInfo().GetDeclaredMethod(patternName);
            for (var x = 0; x < size; x++)
            {
                for (var y = 0; y < size; y++)
                {
                    if (!IsBlocked(new Rectangle(x, y, 1, 1), blockedModules))
                    {
                        qrCode.ModuleMatrix[y][x] ^= (bool)patterMethod.Invoke(null, [x, y]);
                    }
                }
            }
            return Convert.ToInt32(patterMethod.Name.Substring(patterMethod.Name.Length - 1, 1)) - 1;
        }

        /// <summary>
        /// Places encoded data and ECC words into the QR code matrix.
        /// Fills modules in zigzag pattern from bottom-right to top-left.
        /// </summary>
        public static void PlaceDataWords(ref QRCodeData qrCode, string data, ref List<Rectangle> blockedModules)
        {
            var size = qrCode.ModuleMatrix.Count;
            var up = true;
            var datawords = new Queue<bool>();
            for (int i = 0; i < data.Length; i++)
            {
                datawords.Enqueue(data[i] != '0');
            }
            for (var x = size - 1; x >= 0; x = x - 2)
            {
                if (x == 6)
                    x = 5;
                for (var yMod = 1; yMod <= size; yMod++)
                {
                    int y;
                    if (up)
                    {
                        y = size - yMod;
                        if (datawords.Count > 0 && !IsBlocked(new Rectangle(x, y, 1, 1), blockedModules))
                            qrCode.ModuleMatrix[y][x] = datawords.Dequeue();
                        if (datawords.Count > 0 && x > 0 && !IsBlocked(new Rectangle(x - 1, y, 1, 1), blockedModules))
                            qrCode.ModuleMatrix[y][x - 1] = datawords.Dequeue();
                    }
                    else
                    {
                        y = yMod - 1;
                        if (datawords.Count > 0 && !IsBlocked(new Rectangle(x, y, 1, 1), blockedModules))
                            qrCode.ModuleMatrix[y][x] = datawords.Dequeue();
                        if (datawords.Count > 0 && x > 0 && !IsBlocked(new Rectangle(x - 1, y, 1, 1), blockedModules))
                            qrCode.ModuleMatrix[y][x - 1] = datawords.Dequeue();
                    }
                }
                up = !up;
            }
        }

        /// <summary>
        /// Reserves separator areas (white borders) around finder patterns.
        /// 1-module wide white border separates finder patterns from data area.
        /// </summary>
        public static void ReserveSeperatorAreas(int size, ref List<Rectangle> blockedModules)
        {
            blockedModules.AddRange([
                new Rectangle(7, 0, 1, 8),
                new Rectangle(0, 7, 7, 1),
                new Rectangle(0, size-8, 8, 1),
                new Rectangle(7, size-7, 1, 7),
                new Rectangle(size-8, 0, 1, 8),
                new Rectangle(size-7, 7, 7, 1)
            ]);
        }

        /// <summary>
        /// Reserves areas for format and version information.
        /// These areas are filled later with actual format/version data.
        /// </summary>
        public static void ReserveVersionAreas(int size, int version, ref List<Rectangle> blockedModules)
        {
            blockedModules.AddRange([
                new Rectangle(8, 0, 1, 6),
                new Rectangle(8, 7, 1, 1),
                new Rectangle(0, 8, 6, 1),
                new Rectangle(7, 8, 2, 1),
                new Rectangle(size-8, 8, 8, 1),
                new Rectangle(8, size-7, 1, 7)
            ]);

            if (version >= 7)
            {
                blockedModules.AddRange([
                    new Rectangle(size-11, 0, 3, 6),
                    new Rectangle(0, size-11, 6, 3)
                ]);
            }
        }

        /// <summary>
        /// Places the dark module (always dark/black).
        /// Located at position (8, 4*version + 9).
        /// Required by QR code specification for all versions.
        /// </summary>
        public static void PlaceDarkModule(ref QRCodeData qrCode, int version, ref List<Rectangle> blockedModules)
        {
            qrCode.ModuleMatrix[4 * version + 9][8] = true;
            blockedModules.Add(new Rectangle(8, 4 * version + 9, 1, 1));
        }

        /// <summary>
        /// Places three finder patterns (position detection patterns).
        /// 7×7 patterns located at top-left, top-right, and bottom-left corners.
        /// </summary>
        public static void PlaceFinderPatterns(ref QRCodeData qrCode, ref List<Rectangle> blockedModules)
        {
            var size = qrCode.ModuleMatrix.Count;
            int[] locations = [0, 0, size - 7, 0, 0, size - 7];

            for (var i = 0; i < 6; i = i + 2)
            {
                for (var x = 0; x < 7; x++)
                {
                    for (var y = 0; y < 7; y++)
                    {
                        if (!(((x == 1 || x == 5) && y > 0 && y < 6) || (x > 0 && x < 6 && (y == 1 || y == 5))))
                        {
                            qrCode.ModuleMatrix[y + locations[i + 1]][x + locations[i]] = true;
                        }
                    }
                }
                blockedModules.Add(new Rectangle(locations[i], locations[i + 1], 7, 7));
            }
        }

        /// <summary>
        /// Places alignment patterns throughout the QR code.
        /// Number and positions vary by version (version 2+).
        /// 5×5 patterns help with image recognition and distortion correction.
        /// </summary>
        public static void PlaceAlignmentPatterns(ref QRCodeData qrCode, List<Point> alignmentPatternLocations, ref List<Rectangle> blockedModules)
        {
            foreach (var loc in alignmentPatternLocations)
            {
                var alignmentPatternRect = new Rectangle(loc.X, loc.Y, 5, 5);
                var blocked = false;
                foreach (var blockedRect in blockedModules)
                {
                    if (Intersects(alignmentPatternRect, blockedRect))
                    {
                        blocked = true;
                        break;
                    }
                }
                if (blocked)
                {
                    continue;
                }

                for (var x = 0; x < 5; x++)
                {
                    for (var y = 0; y < 5; y++)
                    {
                        if (y == 0 || y == 4 || x == 0 || x == 4 || (x == 2 && y == 2))
                        {
                            qrCode.ModuleMatrix[loc.Y + y][loc.X + x] = true;
                        }
                    }
                }
                blockedModules.Add(new Rectangle(loc.X, loc.Y, 5, 5));
            }
        }


        /// <summary>
        /// Places timing patterns (alternating dark/light modules).
        /// Horizontal and vertical lines at row 6 and column 6.
        /// Used for module coordinate mapping during decoding.
        /// </summary>
        public static void PlaceTimingPatterns(ref QRCodeData qrCode, ref List<Rectangle> blockedModules)
        {
            var size = qrCode.ModuleMatrix.Count;
            for (var i = 8; i < size - 8; i++)
            {
                if (i % 2 == 0)
                {
                    qrCode.ModuleMatrix[6][i] = true;
                    qrCode.ModuleMatrix[i][6] = true;
                }
            }
            blockedModules.AddRange([
                new Rectangle(6, 8, 1, size-16),
                new Rectangle(8, 6, size-16, 1)
            ]);
        }

        /// <summary>
        /// Checks if two rectangles intersect.
        /// </summary>
        private static bool Intersects(Rectangle r1, Rectangle r2)
        {
            return r2.X < r1.X + r1.Width
                && r1.X < r2.X + r2.Width
                && r2.Y < r1.Y + r1.Height
                && r1.Y < r2.Y + r2.Height;
        }

        /// <summary>
        /// Checks if a rectangle overlaps with any blocked module area.
        /// </summary>
        private static bool IsBlocked(Rectangle r1, List<Rectangle> blockedModules)
        {
            var isBlocked = false;
            foreach (var blockedMod in blockedModules)
            {
                if (Intersects(blockedMod, r1))
                {
                    isBlocked = true;
                }
            }
            return isBlocked;
        }

        // private class/strusts

        /// <summary>
        /// Mask pattern implementations and scoring algorithm.
        /// ISO/IEC 18004 defines 8 mask patterns and 4 penalty rules.
        /// </summary>
        private static class MaskPattern
        {
            /// <summary>
            /// Creates a checkerboard pattern.
            /// </summary>
            public static bool Pattern1(int x, int y) => (x + y) % 2 == 0;

            /// <summary>
            /// Creates horizontal stripes.
            /// </summary>
            public static bool Pattern2(int x, int y) => y % 2 == 0;

            /// <summary>
            /// Creates vertical stripes (wider than pattern 1).
            /// </summary>
            public static bool Pattern3(int x, int y) => x % 3 == 0;

            /// <summary>
            /// Creates diagonal stripes (wider than pattern 0).
            /// </summary>
            public static bool Pattern4(int x, int y) => (x + y) % 3 == 0;

            /// <summary>
            /// Creates a combination of horizontal and vertical patterns.
            /// </summary>
            public static bool Pattern5(int x, int y) => ((int)(Math.Floor(y / 2d) + Math.Floor(x / 3d)) % 2) == 0;

            /// <summary>
            /// Creates a complex grid pattern.
            /// </summary>
            public static bool Pattern6(int x, int y) => ((x * y) % 2) + ((x * y) % 3) == 0;

            /// <summary>
            /// Creates an alternating complex pattern.
            /// </summary>
            public static bool Pattern7(int x, int y) => (((x * y) % 2) + ((x * y) % 3)) % 2 == 0;

            /// <summary>
            /// Creates a combination of checkerboard and grid patterns.
            /// </summary>
            public static bool Pattern8(int x, int y) => (((x + y) % 2) + ((x * y) % 3)) % 2 == 0;

            /// <summary>
            /// Calculates penalty score for a masked QR code.
            /// Lower score = better readability and scanning reliability.
            /// Applies 4 penalty rules from ISO/IEC 18004 Section 8.8.2:
            ///
            /// Rule 1 (Consecutive modules):
            ///   - 5 consecutive modules: +3 points
            ///   - Each additional consecutive module: +1 point
            ///   - Applied to both rows and columns
            ///
            /// Rule 2 (Block patterns):
            ///   - Each 2×2 block of same color: +3 points
            ///
            /// Rule 3 (Finder-like patterns):
            ///   - Pattern "1:1:3:1:1 ratio with 4 light modules on either side": +40 points
            ///   - Helps avoid false positives during QR code detection
            ///
            /// Rule 4 (Balance):
            ///   - Deviation from 50% dark modules
            ///   - Score = (|percentage - 50| / 5) × 10
            ///   - Encourages even distribution of dark/light modules
            /// </summary>
            public static int Score(ref QRCodeData qrCode)
            {
                int score1 = 0,
                    score2 = 0,
                    score3 = 0,
                    score4 = 0;
                var size = qrCode.ModuleMatrix.Count;

                // Penalty 1: Consecutive modules
                for (var y = 0; y < size; y++)
                {
                    var modInRow = 0;
                    var modInColumn = 0;
                    var lastValRow = qrCode.ModuleMatrix[y][0];
                    var lastValColumn = qrCode.ModuleMatrix[0][y];
                    for (var x = 0; x < size; x++)
                    {
                        if (qrCode.ModuleMatrix[y][x] == lastValRow)
                        {
                            modInRow++;
                        }
                        else
                        {
                            modInRow = 1;
                        }
                        if (modInRow == 5)
                        {
                            score1 += 3;
                        }
                        else if (modInRow > 5)
                        {
                            score1++;
                        }
                        lastValRow = qrCode.ModuleMatrix[y][x];

                        if (qrCode.ModuleMatrix[x][y] == lastValColumn)
                        {
                            modInColumn++;
                        }
                        else
                        {
                            modInColumn = 1;
                        }
                        if (modInColumn == 5)
                        {
                            score1 += 3;
                        }
                        else if (modInColumn > 5)
                        {
                            score1++;
                        }
                        lastValColumn = qrCode.ModuleMatrix[x][y];
                    }
                }


                // Penalty 2: Block patterns
                for (var y = 0; y < size - 1; y++)
                {
                    for (var x = 0; x < size - 1; x++)
                    {
                        if (qrCode.ModuleMatrix[y][x] == qrCode.ModuleMatrix[y][x + 1] &&
                            qrCode.ModuleMatrix[y][x] == qrCode.ModuleMatrix[y + 1][x] &&
                            qrCode.ModuleMatrix[y][x] == qrCode.ModuleMatrix[y + 1][x + 1])
                        {
                            score2 += 3;
                        }
                    }
                }

                // Penalty 3: Finder-like patterns
                for (var y = 0; y < size; y++)
                {
                    for (var x = 0; x < size - 10; x++)
                    {
                        if ((qrCode.ModuleMatrix[y][x] &&
                            !qrCode.ModuleMatrix[y][x + 1] &&
                            qrCode.ModuleMatrix[y][x + 2] &&
                            qrCode.ModuleMatrix[y][x + 3] &&
                            qrCode.ModuleMatrix[y][x + 4] &&
                            !qrCode.ModuleMatrix[y][x + 5] &&
                            qrCode.ModuleMatrix[y][x + 6] &&
                            !qrCode.ModuleMatrix[y][x + 7] &&
                            !qrCode.ModuleMatrix[y][x + 8] &&
                            !qrCode.ModuleMatrix[y][x + 9] &&
                            !qrCode.ModuleMatrix[y][x + 10]) ||
                            (!qrCode.ModuleMatrix[y][x] &&
                            !qrCode.ModuleMatrix[y][x + 1] &&
                            !qrCode.ModuleMatrix[y][x + 2] &&
                            !qrCode.ModuleMatrix[y][x + 3] &&
                            qrCode.ModuleMatrix[y][x + 4] &&
                            !qrCode.ModuleMatrix[y][x + 5] &&
                            qrCode.ModuleMatrix[y][x + 6] &&
                            qrCode.ModuleMatrix[y][x + 7] &&
                            qrCode.ModuleMatrix[y][x + 8] &&
                            !qrCode.ModuleMatrix[y][x + 9] &&
                            qrCode.ModuleMatrix[y][x + 10]))
                        {
                            score3 += 40;
                        }

                        if ((qrCode.ModuleMatrix[x][y] &&
                            !qrCode.ModuleMatrix[x + 1][y] &&
                            qrCode.ModuleMatrix[x + 2][y] &&
                            qrCode.ModuleMatrix[x + 3][y] &&
                            qrCode.ModuleMatrix[x + 4][y] &&
                            !qrCode.ModuleMatrix[x + 5][y] &&
                            qrCode.ModuleMatrix[x + 6][y] &&
                            !qrCode.ModuleMatrix[x + 7][y] &&
                            !qrCode.ModuleMatrix[x + 8][y] &&
                            !qrCode.ModuleMatrix[x + 9][y] &&
                            !qrCode.ModuleMatrix[x + 10][y]) ||
                            (!qrCode.ModuleMatrix[x][y] &&
                            !qrCode.ModuleMatrix[x + 1][y] &&
                            !qrCode.ModuleMatrix[x + 2][y] &&
                            !qrCode.ModuleMatrix[x + 3][y] &&
                            qrCode.ModuleMatrix[x + 4][y] &&
                            !qrCode.ModuleMatrix[x + 5][y] &&
                            qrCode.ModuleMatrix[x + 6][y] &&
                            qrCode.ModuleMatrix[x + 7][y] &&
                            qrCode.ModuleMatrix[x + 8][y] &&
                            !qrCode.ModuleMatrix[x + 9][y] &&
                            qrCode.ModuleMatrix[x + 10][y]))
                        {
                            score3 += 40;
                        }
                    }
                }

                // Penalty 4: Dark/light balance
                double blackModules = 0;
                foreach (var row in qrCode.ModuleMatrix)
                {
                    foreach (bool bit in row)
                    {
                        if (bit) blackModules++;
                    }
                }

                var percent = (blackModules / (qrCode.ModuleMatrix.Count * qrCode.ModuleMatrix.Count)) * 100;
                var prevMultipleOf5 = Math.Abs((int)Math.Floor(percent / 5) * 5 - 50) / 5;
                var nextMultipleOf5 = Math.Abs((int)Math.Floor(percent / 5) * 5 - 45) / 5;
                score4 = Math.Min(prevMultipleOf5, nextMultipleOf5) * 10;

                return score1 + score2 + score3 + score4;
            }
        }

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

    /// <summary>
    /// Rectangle structure for tracking blocked module regions.
    /// Used during QR code matrix generation to avoid overwriting patterns.
    /// </summary>
    private readonly record struct Rectangle
    {
        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }

        public Rectangle(int x, int y, int w, int h)
        {
            X = x;
            Y = y;
            Width = w;
            Height = h;
        }
    }

    public void Dispose()
    {
        // will be removed in future, or remain.
    }
}
