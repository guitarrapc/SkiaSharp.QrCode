using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace SkiaSharp.QrCode;

/// <summary>
/// QR code generator based on ISO/IEC 18004 standard.
/// Supports QR code versions 1-40 with multiple encoding modes and error correction levels.
/// </summary>
public class QRCodeGenerator : IDisposable
{
    private List<AlignmentPattern> alignmentPatternTable;
    private List<ECCInfo> capacityECCTable;
    private List<VersionInfo> capacityTable;
    private List<Antilog> galoisField;
    private Dictionary<char, int> alphanumEncDict;

    public QRCodeGenerator()
    {
        this.CreateAntilogTable();
        this.CreateAlphanumEncDict();
        this.CreateCapacityTable();
        this.CreateCapacityECCTable();
        this.CreateAlignmentPatternTable();
    }

    /// <summary>
    /// Creates a QR code from the provided plain text.
    /// </summary>
    /// <param name="plainText">The text to encode in the QR code.</param>
    /// <param name="eccLevel">Error correction level (L: 7%, M: 15%, Q: 25%, H: 30%).</param>
    /// <param name="forceUtf8">Force UTF-8 encoding even if ISO-8859-1 is sufficient.</param>
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
    public QRCodeData CreateQrCode(string plainText, ECCLevel eccLevel, bool forceUtf8 = false, bool utf8BOM = false, EciMode eciMode = EciMode.Default, int requestedVersion = -1, int quietZoneSize = 4)
    {
        // Step 1: Auto-detect optimal encoding mode (Numeric > Alphanumeric > Byte)
        EncodingMode encoding = GetEncodingFromPlaintext(plainText, forceUtf8);

        // Step 2: Convert plain text to binary string
        var codedText = this.PlainTextToBinary(plainText, encoding, eciMode, utf8BOM, forceUtf8);

        // Step 3: Calculate actual data length for version selection
        var dataInputLength = this.GetDataLength(encoding, plainText, codedText, forceUtf8);

        // Step 4: Select QR code version (auto or manual)
        int version = requestedVersion;
        if (version == -1)
        {
            version = this.GetVersion(dataInputLength, encoding, eccLevel);
        }

        // Step 5: Build mode indicator and character count indicator
        string modeIndicator = string.Empty;
        if (eciMode != EciMode.Default)
        {
            modeIndicator = DecToBin((int)EncodingMode.ECI, 4);
            modeIndicator += DecToBin((int)eciMode, 8);
        }
        modeIndicator += DecToBin((int)encoding, 4);
        var countIndicator = DecToBin(dataInputLength, this.GetCountIndicatorLength(version, encoding));
        var bitString = modeIndicator + countIndicator;

        bitString += codedText;

        // Step 6: Fill up data code word to capacity
        var eccInfo = this.capacityECCTable.Single(x => x.Version == version && x.ErrorCorrectionLevel.Equals(eccLevel));
        var dataLength = eccInfo.TotalDataCodewords * 8;
        var lengthDiff = dataLength - bitString.Length;
        // Add terminator (up to 4 zeros)
        if (lengthDiff > 0)
            bitString += new string('0', Math.Min(lengthDiff, 4));
        // Pad to byte boundary
        if ((bitString.Length % 8) != 0)
            bitString += new string('0', 8 - (bitString.Length % 8));
        // Fill with alternating pad bytes (11101100, 00010001)
        while (bitString.Length < dataLength)
            bitString += "1110110000010001";
        // Trim if over capacity
        if (bitString.Length > dataLength)
            bitString = bitString.Substring(0, dataLength);

        // Step 7: Calculate error correction words using Reed-Solomon
        var codeWordWithECC = new List<CodewordBlock>();
        // Process group 1 blocks
        for (var i = 0; i < eccInfo.BlocksInGroup1; i++)
        {
            var bitStr = bitString.Substring(i * eccInfo.CodewordsInGroup1 * 8, eccInfo.CodewordsInGroup1 * 8);
            var bitBlockList = this.BinaryStringToBitBlockList(bitStr);
            var bitBlockListDec = this.BinaryStringListToDecList(bitBlockList);
            var eccWordList = this.CalculateECCWords(bitStr, eccInfo);
            var eccWordListDec = this.BinaryStringListToDecList(eccWordList);
            codeWordWithECC.Add(
                new CodewordBlock(1,
                i + 1,
                bitStr,
                bitBlockList,
                eccWordList,
                bitBlockListDec,
                eccWordListDec)
            );
        }
        bitString = bitString.Substring(eccInfo.BlocksInGroup1 * eccInfo.CodewordsInGroup1 * 8);
        // Process group 2 blocks
        for (var i = 0; i < eccInfo.BlocksInGroup2; i++)
        {
            var bitStr = bitString.Substring(i * eccInfo.CodewordsInGroup2 * 8, eccInfo.CodewordsInGroup2 * 8);
            var bitBlockList = this.BinaryStringToBitBlockList(bitStr);
            var bitBlockListDec = this.BinaryStringListToDecList(bitBlockList);
            var eccWordList = this.CalculateECCWords(bitStr, eccInfo);
            var eccWordListDec = this.BinaryStringListToDecList(eccWordList);
            codeWordWithECC.Add(new CodewordBlock(2,
                i + 1,
                bitStr,
                bitBlockList,
                eccWordList,
                bitBlockListDec,
                eccWordListDec)
            );
        }

        // Step 8: Interleave code words
        var interleavedWordsSb = new StringBuilder();
        // Interleave data codewords
        for (var i = 0; i < Math.Max(eccInfo.CodewordsInGroup1, eccInfo.CodewordsInGroup2); i++)
        {
            foreach (var codeBlock in codeWordWithECC)
            {
                if (codeBlock.CodeWords.Count > i)
                    interleavedWordsSb.Append(codeBlock.CodeWords[i]);
            }
        }
        // Interleave ECC codewords
        for (var i = 0; i < eccInfo.ECCPerBlock; i++)
        {
            foreach (var codeBlock in codeWordWithECC)
            {
                if (codeBlock.ECCWords.Count > i)
                    interleavedWordsSb.Append(codeBlock.ECCWords[i]);
            }
        }
        // Add remainder bits
        interleavedWordsSb.Append(new string('0', QrCodeConstants.GetRemainderBits(version)));
        var interleavedData = interleavedWordsSb.ToString();

        // Step 9-12: Place all patterns and data on QR code matrix
        var qr = new QRCodeData(version);
        var blockedModules = new List<Rectangle>();
        // Place fixed patterns
        ModulePlacer.PlaceFinderPatterns(ref qr, ref blockedModules);
        ModulePlacer.ReserveSeperatorAreas(qr.ModuleMatrix.Count, ref blockedModules);
        ModulePlacer.PlaceAlignmentPatterns(ref qr, this.alignmentPatternTable.Where(x => x.Version == version).Select(x => x.PatternPositions).First(), ref blockedModules);
        ModulePlacer.PlaceTimingPatterns(ref qr, ref blockedModules);
        ModulePlacer.PlaceDarkModule(ref qr, version, ref blockedModules);
        ModulePlacer.ReserveVersionAreas(qr.ModuleMatrix.Count, version, ref blockedModules);
        // Place data
        ModulePlacer.PlaceDataWords(ref qr, interleavedData, ref blockedModules);
        // Apply mask and get optimal mask number
        var maskVersion = ModulePlacer.MaskCode(ref qr, version, ref blockedModules, eccLevel);
        // Place format information
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
                sb.Append((Convert.ToInt32(fStrEcc[i]) ^ Convert.ToInt32(generator[i])).ToString());
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
                sb.Append((Convert.ToInt32(vStrEcc[i]) ^ Convert.ToInt32(generator[i])).ToString());
            vStrEcc = sb.ToString().TrimStart('0');
        }
        vStrEcc = vStrEcc.PadLeft(12, '0');
        vStr += vStrEcc;

        return vStr;
    }

    // Error Correction (Reed-Solomon)

    /// <summary>
    /// Calculates error correction codewords using Reed-Solomon algorithm.
    /// </summary>
    /// <param name="bitString">Data bit string to protect.</param>
    /// <param name="eccInfo">Error correction configuration.</param>
    /// <returns>List of ECC codewords as binary strings (8 bits each).</returns>
    private List<string> CalculateECCWords(string bitString, ECCInfo eccInfo)
    {
        var eccWords = eccInfo.ECCPerBlock;
        var messagePolynom = this.CalculateMessagePolynom(bitString);
        var generatorPolynom = this.CalculateGeneratorPolynom(eccWords);

        for (var i = 0; i < messagePolynom.PolyItems.Count; i++)
            messagePolynom.PolyItems[i] = new PolynomItem(messagePolynom.PolyItems[i].Coefficient,
                messagePolynom.PolyItems[i].Exponent + eccWords);

        for (var i = 0; i < generatorPolynom.PolyItems.Count; i++)
            generatorPolynom.PolyItems[i] = new PolynomItem(generatorPolynom.PolyItems[i].Coefficient,
                generatorPolynom.PolyItems[i].Exponent + (messagePolynom.PolyItems.Count - 1));

        var leadTermSource = messagePolynom;
        for (var i = 0; (leadTermSource.PolyItems.Count > 0 && leadTermSource.PolyItems[leadTermSource.PolyItems.Count - 1].Exponent > 0); i++)
        {
            if (leadTermSource.PolyItems[0].Coefficient == 0)
            {
                leadTermSource.PolyItems.RemoveAt(0);
                leadTermSource.PolyItems.Add(new PolynomItem(0, leadTermSource.PolyItems[leadTermSource.PolyItems.Count - 1].Exponent - 1));
            }
            else
            {
                var resPoly = this.MultiplyGeneratorPolynomByLeadterm(generatorPolynom, this.ConvertToAlphaNotation(leadTermSource).PolyItems[0], i);
                resPoly = this.ConvertToDecNotation(resPoly);
                resPoly = this.XORPolynoms(leadTermSource, resPoly);
                leadTermSource = resPoly;
            }
        }
        return leadTermSource.PolyItems.Select(x => DecToBin(x.Coefficient, 8)).ToList();
    }

    /// <summary>
    /// Creates message polynomial from bit string.
    /// Each 8-bit block becomes a coefficient with decreasing exponent.
    /// Example: "11010011 10101100" → α^1·x^1 + α^0·x^0
    /// </summary>
    /// <param name="bitString">Binary data string.</param>
    /// <returns>Polynomial representation of message.</returns>
    private Polynom CalculateMessagePolynom(string bitString)
    {
        var messagePol = new Polynom();
        for (var i = bitString.Length / 8 - 1; i >= 0; i--)
        {
            messagePol.PolyItems.Add(new PolynomItem(this.BinToDec(bitString.Substring(0, 8)), i));
            bitString = bitString.Remove(0, 8);
        }
        return messagePol;
    }

    /// <summary>
    /// Generates Reed-Solomon generator polynomial.
    /// Formula: (x - α^0)(x - α^1)...(x - α^(n-1)) where n = numEccWords
    /// </summary>
    /// <param name="numEccWords">Number of error correction words needed.</param>
    /// <returns>Generator polynomial in alpha notation.</returns>
    private Polynom CalculateGeneratorPolynom(int numEccWords)
    {
        var generatorPolynom = new Polynom();
        generatorPolynom.PolyItems.AddRange(new[]{
            new PolynomItem(0,1),
            new PolynomItem(0,0)
        });
        for (var i = 1; i <= numEccWords - 1; i++)
        {
            var multiplierPolynom = new Polynom();
            multiplierPolynom.PolyItems.AddRange(new[]{
               new PolynomItem(0,1),
            new PolynomItem(i,0)
            });

            generatorPolynom = this.MultiplyAlphaPolynoms(generatorPolynom, multiplierPolynom);
        }

        return generatorPolynom;
    }

    // Galois Field Operations

    /// <summary>
    /// Converts polynomial from decimal notation to alpha notation (α^n).
    /// </summary>
    /// <param name="poly">Polynomial in decimal notation.</param>
    /// <returns>Polynomial in alpha notation.</returns>
    private Polynom ConvertToAlphaNotation(Polynom poly)
    {
        var newPoly = new Polynom();
        for (var i = 0; i < poly.PolyItems.Count; i++)
            newPoly.PolyItems.Add(
                new PolynomItem(
                    (poly.PolyItems[i].Coefficient != 0
                        ? this.GetAlphaExpFromIntVal(poly.PolyItems[i].Coefficient)
                        : 0), poly.PolyItems[i].Exponent));
        return newPoly;
    }

    /// <summary>
    /// Converts polynomial from alpha notation (α^n) to decimal notation.
    /// </summary>
    /// <param name="poly">Polynomial in alpha notation.</param>
    /// <returns>Polynomial in decimal notation.</returns>
    private Polynom ConvertToDecNotation(Polynom poly)
    {
        var newPoly = new Polynom();
        for (var i = 0; i < poly.PolyItems.Count; i++)
            newPoly.PolyItems.Add(new PolynomItem(this.GetIntValFromAlphaExp(poly.PolyItems[i].Coefficient), poly.PolyItems[i].Exponent));
        return newPoly;
    }

    /// <summary>
    /// Gets integer value from alpha exponent using Galois field lookup.
    /// Example: α^25 → galoisField[25].IntegerValue
    /// </summary>
    /// <param name="exp">Alpha exponent (0-255).</param>
    /// <returns>Integer value (0-255).</returns>
    private int GetIntValFromAlphaExp(int exp)
    {
        return this.galoisField.Where(alog => alog.ExponentAlpha == exp).Select(alog => alog.IntegerValue).First();
    }

    /// <summary>
    /// Gets alpha exponent from integer value using Galois field lookup.
    /// Example: 57 → galoisField.Find(x => x.IntegerValue == 57).ExponentAlpha
    /// </summary>
    /// <param name="intVal">Integer value (0-255).</param>
    /// <returns>Alpha exponent (0-255).</returns>
    private int GetAlphaExpFromIntVal(int intVal)
    {
        return this.galoisField.Where(alog => alog.IntegerValue == intVal).Select(alog => alog.ExponentAlpha).First();
    }

    /// <summary>
    /// Performs XOR operation on two polynomials coefficient-wise.
    /// Used in Reed-Solomon division process.
    /// </summary>
    /// <param name="messagePolynom">Message polynomial.</param>
    /// <param name="resPolynom">Result polynomial from previous step.</param>
    /// <returns>XORed polynomial.</returns>
    private Polynom XORPolynoms(Polynom messagePolynom, Polynom resPolynom)
    {
        var resultPolynom = new Polynom();
        Polynom longPoly, shortPoly;
        if (messagePolynom.PolyItems.Count >= resPolynom.PolyItems.Count)
        {
            longPoly = messagePolynom;
            shortPoly = resPolynom;
        }
        else
        {
            longPoly = resPolynom;
            shortPoly = messagePolynom;
        }

        for (var i = 0; i < longPoly.PolyItems.Count; i++)
        {
            var polItemRes = new PolynomItem
            (

                    longPoly.PolyItems[i].Coefficient ^
                    (shortPoly.PolyItems.Count > i ? shortPoly.PolyItems[i].Coefficient : 0),
                messagePolynom.PolyItems[0].Exponent - i
            );
            resultPolynom.PolyItems.Add(polItemRes);
        }
        resultPolynom.PolyItems.RemoveAt(0);
        return resultPolynom;
    }

    /// <summary>
    /// Multiplies two polynomials in alpha notation using Galois field arithmetic.
    /// </summary>
    /// <param name="polynomBase">Base polynomial.</param>
    /// <param name="polynomMultiplier">Multiplier polynomial.</param>
    /// <returns>Product polynomial.</returns>
    private Polynom MultiplyAlphaPolynoms(Polynom polynomBase, Polynom polynomMultiplier)
    {
        var resultPolynom = new Polynom();
        foreach (var polItemBase in polynomMultiplier.PolyItems)
        {
            foreach (var polItemMulti in polynomBase.PolyItems)
            {
                var polItemRes = new PolynomItem
                (
                    ShrinkAlphaExp(polItemBase.Coefficient + polItemMulti.Coefficient),
                    (polItemBase.Exponent + polItemMulti.Exponent)
                );
                resultPolynom.PolyItems.Add(polItemRes);
            }
        }
        var exponentsToGlue = resultPolynom.PolyItems.GroupBy(x => x.Exponent).Where(x => x.Count() > 1).Select(x => x.First().Exponent);
        var gluedPolynoms = new List<PolynomItem>();
        var toGlue = exponentsToGlue as IList<int> ?? exponentsToGlue.ToList();
        foreach (var exponent in toGlue)
        {
            var coefficient = resultPolynom.PolyItems.Where(x => x.Exponent == exponent).Aggregate(0, (current, polynomOld)
                => current ^ this.GetIntValFromAlphaExp(polynomOld.Coefficient));
            var polynomFixed = new PolynomItem(this.GetAlphaExpFromIntVal(coefficient), exponent);
            gluedPolynoms.Add(polynomFixed);
        }
        resultPolynom.PolyItems.RemoveAll(x => toGlue.Contains(x.Exponent));
        resultPolynom.PolyItems.AddRange(gluedPolynoms);
        resultPolynom.PolyItems = resultPolynom.PolyItems.OrderByDescending(x => x.Exponent).ToList();
        return resultPolynom;
    }

    /// <summary>
    /// Normalizes alpha exponent to 0-255 range.
    /// Formula: (exp mod 256) + floor(exp / 256)
    /// </summary>
    /// <param name="alphaExp">Alpha exponent to normalize.</param>
    /// <returns>Normalized exponent (0-255).</returns>
    private static int ShrinkAlphaExp(int alphaExp)
    {
        // ReSharper disable once PossibleLossOfFraction
        return (int)((alphaExp % 256) + Math.Floor((double)(alphaExp / 256)));
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
        var version = this.capacityTable.Where(
            x => x.Details.Count(
                y => (y.ErrorCorrectionLevel == eccLevel
                      && y.CapacityDict[encMode] >= Convert.ToInt32(length)
                      )
                ) > 0
          ).Select(x => new
          {
              version = x.Version,
              capacity = x.Details.Single(y => y.ErrorCorrectionLevel == eccLevel)
                                        .CapacityDict[encMode]
          }).Min(x => x.version);
        return version;
    }

    /// <summary>
    /// Determines the optimal encoding mode for given text.
    /// Priority: Numeric (most efficient) > Alphanumeric > Byte (least efficient).
    /// </summary>
    /// <param name="plainText">Text to analyze.</param>
    /// <param name="forceUtf8">Force byte mode with UTF-8.</param>
    /// <returns>Optimal encoding mode.</returns>
    private EncodingMode GetEncodingFromPlaintext(string plainText, bool forceUtf8)
    {
        EncodingMode result = EncodingMode.Numeric;

        if (forceUtf8)
            return EncodingMode.Byte;

        foreach (char c in plainText)
        {
            if (QrCodeConstants.IsNumeric(c))
                continue;

            result = EncodingMode.Alphanumeric;

            if (!QrCodeConstants.AlphanumEncTable.Contains(c))
                return EncodingMode.Byte;
        }

        return result;
    }

    /// <summary>
    /// Gets the length of character count indicator based on version and encoding mode.
    /// </summary>
    /// <param name="version">QR code version (1-40).</param>
    /// <param name="encMode">Encoding mode.</param>
    /// <returns>
    /// Bit length of count indicator:
    /// - Version 1-9: Numeric=10, Alphanumeric=9, Byte=8
    /// - Version 10-26: Numeric=12, Alphanumeric=11, Byte=16
    /// - Version 27-40: Numeric=14, Alphanumeric=13, Byte=16
    /// </returns>
    private int GetCountIndicatorLength(int version, EncodingMode encMode)
    {
        if (version < 10)
        {
            if (encMode.Equals(EncodingMode.Numeric))
                return 10;
            else if (encMode.Equals(EncodingMode.Alphanumeric))
                return 9;
            else
                return 8;
        }
        else if (version < 27)
        {
            if (encMode.Equals(EncodingMode.Numeric))
                return 12;
            else if (encMode.Equals(EncodingMode.Alphanumeric))
                return 11;
            else if (encMode.Equals(EncodingMode.Byte))
                return 16;
            else
                return 10;
        }
        else
        {
            if (encMode.Equals(EncodingMode.Numeric))
                return 14;
            else if (encMode.Equals(EncodingMode.Alphanumeric))
                return 13;
            else if (encMode.Equals(EncodingMode.Byte))
                return 16;
            else
                return 12;
        }
    }

    /// <summary>
    /// Calculates actual data length for capacity checking.
    /// For UTF-8, returns byte count; otherwise returns character count.
    /// </summary>
    /// <param name="encoding">Encoding mode.</param>
    /// <param name="plainText">Original text.</param>
    /// <param name="codedText">Encoded binary text.</param>
    /// <param name="forceUtf8">Whether UTF-8 is forced.</param>
    /// <returns>Data length for version selection.</returns>
    private int GetDataLength(EncodingMode encoding, string plainText, string codedText, bool forceUtf8)
    {
        return forceUtf8 || this.IsUtf8(encoding, plainText) ? (codedText.Length / 8) : plainText.Length;
    }

    /// <summary>
    /// Checks if text requires UTF-8 encoding (not valid ISO-8859-1).
    /// </summary>
    private bool IsUtf8(EncodingMode encoding, string plainText)
    {
        return (encoding == EncodingMode.Byte && !this.IsValidISO(plainText));
    }

    /// <summary>
    /// Validates if text can be encoded in ISO-8859-1 without data loss.
    /// </summary>
    private bool IsValidISO(string input)
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
        var binStr = DecToBin(decNum);
        return binStr.PadLeft(padLeftUpTo, '0');
    }

    /// <summary>
    /// Converts decimal number to binary string with optional padding.
    /// </summary>
    /// <param name="decNum">Decimal number.</param>
    /// <returns>Binary string.</returns>
    private static string DecToBin(int decNum)
    {
        return Convert.ToString(decNum, 2);
    }

    /// <summary>
    /// Converts plain text to binary string based on encoding mode.
    /// </summary>
    /// <param name="plainText">Text to encode.</param>
    /// <param name="encMode">Encoding mode (Numeric/Alphanumeric/Byte/Kanji).</param>
    /// <param name="eciMode">ECI mode for character set.</param>
    /// <param name="utf8BOM">Include UTF-8 Byte Order Mark.</param>
    /// <param name="forceUtf8">Force UTF-8 encoding.</param>
    /// <returns>Binary string representation of the input text.</returns>
    private string PlainTextToBinary(string plainText, EncodingMode encMode, EciMode eciMode, bool utf8BOM, bool forceUtf8)
    {
        switch (encMode)
        {
            case EncodingMode.Alphanumeric:
                return PlainTextToBinaryAlphanumeric(plainText);
            case EncodingMode.Numeric:
                return PlainTextToBinaryNumeric(plainText);
            case EncodingMode.Byte:
                return PlainTextToBinaryByte(plainText, eciMode, utf8BOM, forceUtf8);
            case EncodingMode.Kanji:
                return string.Empty;
            case EncodingMode.ECI:
            default:
                return string.Empty;
        }
    }

    /// <summary>
    /// Converts numeric text (0-9) to binary string.
    /// Encoding: 3 digits = 10 bits, 2 digits = 7 bits, 1 digit = 4 bits.
    /// Example: "123" → DecToBin(123, 10) = "0001111011"
    /// </summary>
    /// <param name="plainText">Numeric string (0-9 only).</param>
    /// <returns>Binary encoded string.</returns>
    private string PlainTextToBinaryNumeric(string plainText)
    {
        var codeText = string.Empty;
        while (plainText.Length >= 3)
        {
            var dec = Convert.ToInt32(plainText.Substring(0, 3));
            codeText += DecToBin(dec, 10);
            plainText = plainText.Substring(3);

        }
        if (plainText.Length == 2)
        {
            var dec = Convert.ToInt32(plainText.Substring(0, plainText.Length));
            codeText += DecToBin(dec, 7);
        }
        else if (plainText.Length == 1)
        {
            var dec = Convert.ToInt32(plainText.Substring(0, plainText.Length));
            codeText += DecToBin(dec, 4);
        }
        return codeText;
    }

    /// <summary>
    /// Converts alphanumeric text to binary string.
    /// Encoding: 2 characters = 11 bits, 1 character = 6 bits.
    /// Formula: (char1_value × 45 + char2_value) in 11 bits.
    /// Example: "AB" → (10 × 45 + 11) = 461 = "00111001101"
    /// </summary>
    /// <param name="plainText">Alphanumeric string.</param>
    /// <returns>Binary encoded string.</returns>
    private string PlainTextToBinaryAlphanumeric(string plainText)
    {
        var codeText = string.Empty;
        while (plainText.Length >= 2)
        {
            var token = plainText.Substring(0, 2);
            var dec = this.alphanumEncDict[token[0]] * 45 + this.alphanumEncDict[token[1]];
            codeText += DecToBin(dec, 11);
            plainText = plainText.Substring(2);

        }
        if (plainText.Length > 0)
        {
            codeText += DecToBin(this.alphanumEncDict[plainText[0]], 6);
        }
        return codeText;
    }

    /// <summary>
    /// Converts byte mode text to binary string.
    /// Encoding: Each byte = 8 bits.
    /// Supports ISO-8859-1, ISO-8859-2, and UTF-8 encodings.
    /// </summary>
    /// <param name="plainText">Text to encode.</param>
    /// <param name="eciMode">Character encoding mode.</param>
    /// <param name="utf8BOM">Whether to include UTF-8 BOM.</param>
    /// <param name="forceUtf8">Force UTF-8 even if ISO-8859-1 is valid.</param>
    /// <returns>Binary encoded string.</returns>
    private string PlainTextToBinaryByte(string plainText, EciMode eciMode, bool utf8BOM, bool forceUtf8)
    {
        byte[] codeBytes;
        var codeText = string.Empty;

        if (this.IsValidISO(plainText) && !forceUtf8)
        {
            codeBytes = Encoding.GetEncoding("ISO-8859-1").GetBytes(plainText);
        }
        else
        {
            switch (eciMode)
            {
                case EciMode.Iso8859_1:
                    codeBytes = Encoding.GetEncoding("ISO-8859-1").GetBytes(ConvertToIso8859(plainText, "ISO-8859-1"));
                    break;
                case EciMode.Iso8859_2:
                    codeBytes = Encoding.GetEncoding("ISO-8859-2").GetBytes(ConvertToIso8859(plainText, "ISO-8859-2"));
                    break;
                case EciMode.Default:
                case EciMode.Utf8:
                default:
                    codeBytes = utf8BOM ? Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(plainText)).ToArray() : Encoding.UTF8.GetBytes(plainText);
                    break;
            }
        }

        foreach (var b in codeBytes)
            codeText += DecToBin(b, 8);

        return codeText;
    }

    /// <summary>
    /// Converts binary string (8-bit blocks) to list of binary strings.
    /// Example: "110100111010110000010001" → ["11010011", "10101100", "00010001"]
    /// </summary>
    private List<string> BinaryStringToBitBlockList(string bitString)
    {
        return new List<char>(bitString.ToCharArray()).Select((x, i) => new { Index = i, Value = x })
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
        return binaryStringList.Select(binaryString => this.BinToDec(binaryString)).ToList();
    }

    /// <summary>
    /// Converts text to ISO-8859-1 or ISO-8859-2 encoding.
    /// Used when ECI mode specifies non-UTF-8 encoding.
    /// </summary>
    private string ConvertToIso8859(string value, string Iso = "ISO-8859-2")
    {
        Encoding iso = Encoding.GetEncoding(Iso);
        Encoding utf8 = Encoding.UTF8;
        byte[] utfBytes = utf8.GetBytes(value);
        byte[] isoBytes = Encoding.Convert(utf8, iso, utfBytes);

        return iso.GetString(isoBytes, 0, isoBytes.Length);
    }

    /// <summary>
    /// Multiplies generator polynomial by lead term during Reed-Solomon division.
    /// Part of the Reed-Solomon error correction calculation process.
    /// </summary>
    private Polynom MultiplyGeneratorPolynomByLeadterm(Polynom genPolynom, PolynomItem leadTerm, int lowerExponentBy)
    {
        var resultPolynom = new Polynom();
        foreach (var polItemBase in genPolynom.PolyItems)
        {
            var polItemRes = new PolynomItem(

                (polItemBase.Coefficient + leadTerm.Coefficient) % 255,
                polItemBase.Exponent - lowerExponentBy
            );
            resultPolynom.PolyItems.Add(polItemRes);
        }
        return resultPolynom;
    }

    // Lookup Table Initialization

    /// <summary>
    /// Initializes alphanumeric encoding dictionary.
    /// Maps each of 45 alphanumeric characters to its index (0-44).
    /// </summary>
    private void CreateAlphanumEncDict()
    {
        this.alphanumEncDict = new Dictionary<char, int>();
        //alphanumEncTable.ToList().Select((x, i) => new { Chr = x, Index = i }).ToList().ForEach(x => this.alphanumEncDict.Add(x.Chr, x.Index));
        var resList = QrCodeConstants.AlphanumEncTable.ToList().Select((x, i) => new { Chr = x, Index = i }).ToList();
        foreach (var res in resList)
        {
            this.alphanumEncDict.Add(res.Chr, res.Index);
        }
    }

    /// <summary>
    /// Initializes alignment pattern lookup table from base values.
    /// Computes actual (x, y) coordinates for each version's alignment patterns.
    /// </summary>
    private void CreateAlignmentPatternTable()
    {
        this.alignmentPatternTable = new List<AlignmentPattern>();

        for (var i = 0; i < (7 * 40); i = i + 7)
        {
            var points = new List<Point>();
            for (var x = 0; x < 7; x++)
            {
                if (QrCodeConstants.AlignmentPatternBaseValues[i + x] != 0)
                {
                    for (var y = 0; y < 7; y++)
                    {
                        if (QrCodeConstants.AlignmentPatternBaseValues[i + y] != 0)
                        {
                            var p = new Point(QrCodeConstants.AlignmentPatternBaseValues[i + x] - 2, QrCodeConstants.AlignmentPatternBaseValues[i + y] - 2);
                            if (!points.Contains(p))
                                points.Add(p);
                        }
                    }
                }
            }

            this.alignmentPatternTable.Add(new AlignmentPattern()
            {
                Version = (i + 7) / 7,
                PatternPositions = points
            });
        }
    }

    /// <summary>
    /// Initializes error correction configuration table from base values.
    /// Creates ECCInfo entries for all version/ECC level combinations.
    /// </summary>
    private void CreateCapacityECCTable()
    {
        this.capacityECCTable = new List<ECCInfo>();
        for (var i = 0; i < (4 * 6 * 40); i = i + (4 * 6))
        {
            this.capacityECCTable.AddRange(
            new[]
            {
                new ECCInfo(
                    (i+24) / 24,
                    ECCLevel.L,
                    QrCodeConstants.CapacityECCBaseValues[i],
                    QrCodeConstants.CapacityECCBaseValues[i+1],
                    QrCodeConstants.CapacityECCBaseValues[i+2],
                    QrCodeConstants.CapacityECCBaseValues[i+3],
                    QrCodeConstants.CapacityECCBaseValues[i+4],
                    QrCodeConstants.CapacityECCBaseValues[i+5]),
                new ECCInfo
                (
                    version: (i + 24) / 24,
                    errorCorrectionLevel: ECCLevel.M,
                    totalDataCodewords: QrCodeConstants.CapacityECCBaseValues[i+6],
                    eccPerBlock: QrCodeConstants.CapacityECCBaseValues[i+7],
                    blocksInGroup1: QrCodeConstants.CapacityECCBaseValues[i+8],
                    codewordsInGroup1: QrCodeConstants.CapacityECCBaseValues[i+9],
                    blocksInGroup2: QrCodeConstants.CapacityECCBaseValues[i+10],
                    codewordsInGroup2: QrCodeConstants.CapacityECCBaseValues[i+11]
                ),
                new ECCInfo
                (
                    version: (i + 24) / 24,
                    errorCorrectionLevel: ECCLevel.Q,
                    totalDataCodewords: QrCodeConstants.CapacityECCBaseValues[i+12],
                    eccPerBlock: QrCodeConstants.CapacityECCBaseValues[i+13],
                    blocksInGroup1: QrCodeConstants.CapacityECCBaseValues[i+14],
                    codewordsInGroup1: QrCodeConstants.CapacityECCBaseValues[i+15],
                    blocksInGroup2: QrCodeConstants.CapacityECCBaseValues[i+16],
                    codewordsInGroup2: QrCodeConstants.CapacityECCBaseValues[i+17]
                ),
                new ECCInfo
                (
                    version: (i + 24) / 24,
                    errorCorrectionLevel: ECCLevel.H,
                    totalDataCodewords: QrCodeConstants.CapacityECCBaseValues[i+18],
                    eccPerBlock: QrCodeConstants.CapacityECCBaseValues[i+19],
                    blocksInGroup1: QrCodeConstants.CapacityECCBaseValues[i+20],
                    codewordsInGroup1: QrCodeConstants.CapacityECCBaseValues[i+21],
                    blocksInGroup2: QrCodeConstants.CapacityECCBaseValues[i+22],
                    codewordsInGroup2: QrCodeConstants.CapacityECCBaseValues[i+23]
                )
            });
        }
    }

    /// <summary>
    /// Initializes capacity lookup table from base values.
    /// Creates VersionInfo entries mapping version/ECC/mode to max capacity.
    /// </summary>
    private void CreateCapacityTable()
    {
        this.capacityTable = new List<VersionInfo>();
        for (var i = 0; i < (16 * 40); i = i + 16)
        {
            this.capacityTable.Add(new VersionInfo(

                (i + 16) / 16,
                new List<VersionInfoDetails>
                {
                    new VersionInfoDetails(
                         ECCLevel.L,
                         new Dictionary<EncodingMode,int>(){
                             { EncodingMode.Numeric, QrCodeConstants.CapacityBaseValues[i] },
                             { EncodingMode.Alphanumeric, QrCodeConstants.CapacityBaseValues[i+1] },
                             { EncodingMode.Byte, QrCodeConstants.CapacityBaseValues[i+2] },
                             { EncodingMode.Kanji, QrCodeConstants.CapacityBaseValues[i+3] },
                        }
                    ),
                    new VersionInfoDetails(
                         ECCLevel.M,
                         new Dictionary<EncodingMode,int>(){
                             { EncodingMode.Numeric, QrCodeConstants.CapacityBaseValues[i+4] },
                             { EncodingMode.Alphanumeric, QrCodeConstants.CapacityBaseValues[i+5] },
                             { EncodingMode.Byte, QrCodeConstants.CapacityBaseValues[i+6] },
                             { EncodingMode.Kanji, QrCodeConstants.CapacityBaseValues[i+7] },
                         }
                    ),
                    new VersionInfoDetails(
                         ECCLevel.Q,
                         new Dictionary<EncodingMode,int>(){
                             { EncodingMode.Numeric, QrCodeConstants.CapacityBaseValues[i+8] },
                             { EncodingMode.Alphanumeric, QrCodeConstants.CapacityBaseValues[i+9] },
                             { EncodingMode.Byte, QrCodeConstants.CapacityBaseValues[i+10] },
                             { EncodingMode.Kanji, QrCodeConstants.CapacityBaseValues[i+11] },
                         }
                    ),
                    new VersionInfoDetails(
                         ECCLevel.H,
                         new Dictionary<EncodingMode,int>(){
                             { EncodingMode.Numeric, QrCodeConstants.CapacityBaseValues[i+12] },
                             { EncodingMode.Alphanumeric, QrCodeConstants.CapacityBaseValues[i+13] },
                             { EncodingMode.Byte, QrCodeConstants.CapacityBaseValues[i+14] },
                             { EncodingMode.Kanji, QrCodeConstants.CapacityBaseValues[i+15] },
                         }
                    )
                }
            ));
        }
    }

    /// <summary>
    /// Creates the Galois field (GF(256)) antilog table for Reed-Solomon error correction.
    /// Generates α^0 to α^255 values using polynomial x^8 + x^4 + x^3 + x^2 + 1 (0x11D).
    /// </summary>
    private void CreateAntilogTable()
    {
        this.galoisField = new List<Antilog>();

        for (var i = 0; i < 256; i++)
        {
            int gfItem;

            if (i > 7)
            {
                gfItem = this.galoisField[i - 1].IntegerValue * 2;
            }
            else
            {
                gfItem = (int)Math.Pow(2, i);
            }

            if (gfItem > 255)
            {
                gfItem = gfItem ^ 285;
            }

            this.galoisField.Add(new Antilog(i, gfItem));
        }
    }

    // Data Structures

    /// <summary>
    /// ECI (Extended Channel Interpretation) mode for character encoding.
    /// </summary>
    public enum EciMode
    {
        /// <summary>
        /// Auto-detect (ISO-8859-1 or UTF-8)
        /// </summary>
        Default = 0,
        /// <summary>
        /// Western European
        /// </summary>
        Iso8859_1 = 3,
        /// <summary>
        /// Central European
        /// </summary>
        Iso8859_2 = 4,
        /// <summary>
        /// Unicode UTF-8
        /// </summary>
        Utf8 = 26
    }

    /// <summary>
    /// Encoding mode enumeration (ISO/IEC 18004 Section 7.4.1).
    /// Determines how data is encoded in the QR code, affecting capacity and efficiency.
    /// 
    /// Mode priority in automatic detection:
    /// 1. Numeric (most efficient)
    /// 2. Alphanumeric
    /// 3. Byte (least efficient)
    /// 
    /// Note: ECI is not a data encoding mode, but a character set declaration.
    /// </summary>
    private enum EncodingMode
    {
        /// <summary>
        /// 0-9 only (10 bits per 3 digits)
        /// </summary>
        Numeric = 1,
        /// <summary>
        /// 0-9, A-Z, space, $%*+-./:  (11 bits per 2 chars).
        /// </summary>
        Alphanumeric = 2,
        /// <summary>
        /// Any 8-bit data (8 bits per byte)
        /// Default: ISO-8859-1, can be UTF-8 with ECI.
        /// </summary>
        Byte = 4,
        /// <summary>
        /// Extended Channel Interpretation (metadata only).
        /// Mode indicator: 0111 + 8-bit assignment number
        /// Specifies character encoding for Byte mode:
        ///   - ECI 3: ISO-8859-1
        ///   - ECI 4: ISO-8859-2
        ///   - ECI 26: UTF-8
        /// Always followed by another mode (typically Byte).
        /// </summary>
        ECI = 7,
        /// <summary>
        /// Shift JIS Kanji (13 bits per character)
        /// </summary>
        Kanji = 8,
    }

    internal static class QrCodeConstants
    {
        /// <summary>
        /// Checks if a character is numeric (0-9).
        /// </summary>
        /// <param name="c">Character to check.</param>
        /// <returns>True if the character is a digit (0-9).</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNumeric(char c) => c >= '0' && c <= '9';

        // TODO: Can be optimized by ASCII Table lookup
        // Alphanumeric mode character set (0-9, A-Z, space, $%*+-./:)
        // Used to validate and encode alphanumeric data (2 characters = 11 bits)
        // Total 45 characters as defined in QR code specification
        public static readonly char[] AlphanumEncTable = { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M', 'N', 'O', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z', ' ', '$', '%', '*', '+', '-', '.', '/', ':' };

        // Maximum data capacity for each QR code version (1-40) and error correction level (L,M,Q,H)
        // Array structure: [version-1][eccLevel][encodingMode]
        // - 1600 elements total (40 versions × 4 ECC levels × 4 encoding modes)
        // - Each 16-element block represents one version's capacities
        // - Within each block: [L-Numeric, L-Alpha, L-Byte, L-Kanji, M-Numeric, M-Alpha, M-Byte, M-Kanji, Q-..., H-...]
        // - Index calculation: (version-1) × 16 + eccLevel × 4 + encodingMode
        // - Example: Version 1, ECC-M, Alphanumeric = 0×16 + 1×4 + 1 = capacityBaseValues[5] = 20 characters
        // Based on ISO/IEC 18004 Table 7-11
        private static readonly int[] capacityBaseValues = [
            // ECC Level L: Numeric, Alphanumeric, Byte, Kanji
            // ECC Level M
            // ECC Level Q
            // ECC Level H
            // Version 1 (21×21 modules)
            41, 25, 17, 10,
            34, 20, 14, 8,
            27, 16, 11, 7,
            17, 10, 7, 4,
            // Version 2 (25×25 modules)
            77, 47, 32, 20,
            63, 38, 26, 16,
            48, 29, 20, 12,
            34, 20, 14, 8,
            // Version 3
            127, 77, 53, 32,
            101, 61, 42, 26,
            77, 47, 32, 20,
            58, 35, 24, 15,
            // Version 4
            187, 114, 78, 48,
            149, 90, 62, 38,
            111, 67, 46, 28,
            82, 50, 34, 21,
            // Version 5
            255, 154, 106, 65,
            202, 122, 84, 52,
            144, 87, 60, 37,
            106, 64, 44, 27,
            // Version 6
            322, 195, 134, 82,
            255, 154, 106, 65,
            178, 108, 74, 45,
            139, 84, 58, 36,
            // Version 7
            370, 224, 154, 95,
            293, 178, 122, 75,
            207, 125, 86, 53,
            154, 93, 64, 39,
            // Version 8
            461, 279, 192, 118,
            365, 221, 152, 93,
            259, 157, 108, 66,
            202, 122, 84, 52,
            // Version 9
            552, 335, 230, 141,
            432, 262, 180, 111,
            312, 189, 130, 80,
            235, 143, 98, 60,
            // Version 10
            652, 395, 271, 167,
            513, 311, 213, 131,
            364, 221, 151, 93,
            288, 174, 119, 74,
            // Version 11
            772, 468, 321, 198,
            604, 366, 251, 155,
            427, 259, 177, 109,
            331, 200, 137, 85,
            // Version 12
            883, 535, 367, 226,
            691, 419, 287, 177,
            489, 296, 203, 125,
            374, 227, 155, 96,
            // Version 13
            1022, 619, 425, 262,
            796, 483, 331, 204,
            580, 352, 241, 149,
            427, 259, 177, 109,
            // Version 14
            1101, 667, 458, 282,
            871, 528, 362, 223,
            621, 376, 258, 159,
            468, 283, 194, 120,
            // Version 15
            1250, 758, 520, 320,
            991, 600, 412, 254,
            703, 426, 292, 180,
            530, 321, 220, 136,
            // Version 16
            1408, 854, 586, 361,
            1082, 656, 450, 277,
            775, 470, 322, 198,
            602, 365, 250, 154,
            // Version 17
            1548, 938, 644, 397,
            1212, 734, 504, 310,
            876, 531, 364, 224,
            674, 408, 280, 173,
            // Version 18
            1725, 1046, 718, 442,
            1346, 816, 560, 345,
            948, 574, 394, 243,
            746, 452, 310, 191,
            // Version 19
            1903, 1153, 792, 488,
            1500, 909, 624, 384,
            1063, 644, 442, 272,
            813, 493, 338, 208,
            // Version 20
            2061, 1249, 858, 528,
            1600, 970, 666, 410,
            1159, 702, 482, 297,
            919, 557, 382, 235,
            // Version 21
            2232, 1352, 929, 572,
            1708, 1035, 711, 438,
            1224, 742, 509, 314,
            969, 587, 403, 248,
            // Version 22
            2409, 1460, 1003, 618,
            1872, 1134, 779, 480,
            1358, 823, 565, 348,
            1056, 640, 439, 270,
            // Version 23
            2620, 1588, 1091, 672,
            2059, 1248, 857, 528,
            1468, 890, 611, 376,
            1108, 672, 461, 284,
            // Version 24
            2812, 1704, 1171, 721,
            2188, 1326, 911, 561,
            1588, 963, 661, 407,
            1228, 744, 511, 315,
            // Version 25
            3057, 1853, 1273, 784,
            2395, 1451, 997, 614,
            1718, 1041, 715, 440,
            1286, 779, 535, 330,
            // Version 26
            3283, 1990, 1367, 842,
            2544, 1542, 1059, 652,
            1804, 1094, 751, 462,
            1425, 864, 593, 365,
            // Version 27
            3517, 2132, 1465, 902,
            2701, 1637, 1125, 692,
            1933, 1172, 805, 496,
            1501, 910, 625, 385,
            // Version 28
            3669, 2223, 1528, 940,
            2857, 1732, 1190, 732,
            2085, 1263, 868, 534,
            1581, 958, 658, 405,
            // Version 29
            3909, 2369, 1628, 1002,
            3035, 1839, 1264, 778,
            2181, 1322, 908, 559,
            1677, 1016, 698, 430,
            // Version 30
            4158, 2520, 1732, 1066,
            3289, 1994, 1370, 843,
            2358, 1429, 982, 604,
            1782, 1080, 742, 457,
            // Version 31
            4417, 2677, 1840, 1132,
            3486, 2113, 1452, 894,
            2473, 1499, 1030, 634,
            1897, 1150, 790, 486,
            // Version 32
            4686, 2840, 1952, 1201,
            3693, 2238, 1538, 947,
            2670, 1618, 1112, 684,
            2022, 1226, 842, 518,
            // Version 33
            4965, 3009, 2068, 1273,
            3909, 2369, 1628, 1002,
            2805, 1700, 1168, 719,
            2157, 1307, 898, 553,
            // Version 34
            5253, 3183, 2188, 1347,
            4134, 2506, 1722, 1060,
            2949, 1787, 1228, 756,
            2301, 1394, 958, 590,
            // Version 35
            5529, 3351, 2303, 1417,
            4343, 2632, 1809, 1113,
            3081, 1867, 1283, 790,
            2361, 1431, 983, 605,
            // Version 36
            5836, 3537, 2431, 1496,
            4588, 2780, 1911, 1176,
            3244, 1966, 1351, 832,
            2524, 1530, 1051, 647,
            // Version 37
            6153, 3729, 2563, 1577,
            4775, 2894, 1989, 1224,
            3417, 2071, 1423, 876,
            2625, 1591, 1093, 673,
            // Version 38
            6479, 3927, 2699, 1661,
            5039, 3054, 2099, 1292,
            3599, 2181, 1499, 923,
            2735, 1658, 1139, 701,
            // Version 39
            6743, 4087, 2809, 1729,
            5313, 3220, 2213, 1362,
            3791, 2298, 1579, 972,
            2927, 1774, 1219, 750,
            // Version 40
            7089, 4296, 2953, 1817,
            5596, 3391, 2331, 1435,
            3993, 2420, 1663, 1024,
            3057, 1852, 1273, 784,
        ];
        public static ReadOnlySpan<int> CapacityBaseValues => capacityBaseValues;

        // Error correction codewords configuration for each version and ECC level
        // Array structure: [version-1][eccLevel][6 parameters]
        // - 960 elements total (40 versions × 4 ECC levels × 6 parameters)
        // - Each 24-element block represents one version's ECC configurations
        // - Parameters per ECC level (6 values):
        //   [0] totalDataCodewords
        //   [1] eccPerBlock
        //   [2] blocksInGroup1
        //   [3] codewordsInGroup1
        //   [4] blocksInGroup2
        //   [5] codewordsInGroup2
        // Based on ISO/IEC 18004 Table 9
        private static readonly int[] capacityECCBaseValues = [
            // ECC Level L: Numeric, Alphanumeric, Byte, Kanji
            // ECC Level M
            // ECC Level Q
            // ECC Level H
            // Version 1
            19, 7, 1, 19, 0, 0,
            16, 10, 1, 16, 0, 0,
            13, 13, 1, 13, 0, 0,
            9, 17, 1, 9, 0, 0,
            // Version 2
            34, 10, 1, 34, 0, 0,
            28, 16, 1, 28, 0, 0,
            22, 22, 1, 22, 0, 0,
            16, 28, 1, 16, 0, 0,
            // Version 3
            55, 15, 1, 55, 0, 0,
            44, 26, 1, 44, 0, 0,
            34, 18, 2, 17, 0, 0,
            26, 22, 2, 13, 0, 0,
            // Version 4
            80, 20, 1, 80, 0, 0,
            64, 18, 2, 32, 0, 0,
            48, 26, 2, 24, 0, 0,
            36, 16, 4, 9, 0, 0,
            // Version 5
            108, 26, 1, 108, 0, 0,
            86, 24, 2, 43, 0, 0,
            62, 18, 2, 15, 2, 16,
            46, 22, 2, 11, 2, 12,
            // Version 6
            136, 18, 2, 68, 0, 0,
            108, 16, 4, 27, 0, 0,
            76, 24, 4, 19, 0, 0,
            60, 28, 4, 15, 0, 0,
            // Version 7
            156, 20, 2, 78, 0, 0,
            124, 18, 4, 31, 0, 0,
            88, 18, 2, 14, 4, 15,
            66, 26, 4, 13, 1, 14,
            // Version 8
            194, 24, 2, 97, 0, 0,
            154, 22, 2, 38, 2, 39,
            110, 22, 4, 18, 2, 19,
            86, 26, 4, 14, 2, 15,
            // Version 9
            232, 30, 2, 116, 0, 0,
            182, 22, 3, 36, 2, 37,
            132, 20, 4, 16, 4, 17,
            100, 24, 4, 12, 4, 13,
            // Version 10
            274, 18, 2, 68, 2, 69,
            216, 26, 4, 43, 1, 44,
            154, 24, 6, 19, 2, 20,
            122, 28, 6, 15, 2, 16,
            // Version 11
            324, 20, 4, 81, 0, 0,
            254, 30, 1, 50, 4, 51,
            180, 28, 4, 22, 4, 23,
            140, 24, 3, 12, 8, 13,
            // Version 12
            370, 24, 2, 92, 2, 93,
            290, 22, 6, 36, 2, 37,
            206, 26, 4, 20, 6, 21,
            158, 28, 7, 14, 4, 15,
            // Version 13
            428, 26, 4, 107, 0, 0,
            334, 22, 8, 37, 1, 38,
            244, 24, 8, 20, 4, 21,
            180, 22, 12, 11, 4, 12,
            // Version 14
            461, 30, 3, 115, 1, 116,
            365, 24, 4, 40, 5, 41,
            261, 20, 11, 16, 5, 17,
            197, 24, 11, 12, 5, 13,
            // Version 15
            523, 22, 5, 87, 1, 88,
            415, 24, 5, 41, 5, 42,
            295, 30, 5, 24, 7, 25,
            223, 24, 11, 12, 7, 13,
            // Version 16
            589, 24, 5, 98, 1, 99,
            453, 28, 7, 45, 3, 46,
            325, 24, 15, 19, 2, 20,
            253, 30, 3, 15, 13, 16,
            // Version 17
            647, 28, 1, 107, 5, 108,
            507, 28, 10, 46, 1, 47,
            367, 28, 1, 22, 15, 23,
            283, 28, 2, 14, 17, 15,
            // Version 18
            721, 30, 5, 120, 1, 121,
            563, 26, 9, 43, 4, 44,
            397, 28, 17, 22, 1, 23,
            313, 28, 2, 14, 19, 15,
            // Version 19
            795, 28, 3, 113, 4, 114,
            627, 26, 3, 44, 11, 45,
            445, 26, 17, 21, 4, 22,
            341, 26, 9, 13, 16, 14,
            // Version 20
            861, 28, 3, 107, 5, 108,
            669, 26, 3, 41, 13, 42,
            485, 30, 15, 24, 5, 25,
            385, 28, 15, 15, 10, 16,
            // Version 21
            932, 28, 4, 116, 4, 117,
            714, 26, 17, 42, 0, 0,
            512, 28, 17, 22, 6, 23,
            406, 30, 19, 16, 6, 17,
            // Version 22
            1006, 28, 2, 111, 7, 112,
            782, 28, 17, 46, 0, 0,
            568, 30, 7, 24, 16, 25,
            442, 24, 34, 13, 0, 0,
            // Version 23
            1094, 30, 4, 121, 5, 122,
            860, 28, 4, 47, 14, 48,
            614, 30, 11, 24, 14, 25,
            464, 30, 16, 15, 14, 16,
            // Version 24
            1174, 30, 6, 117, 4, 118,
            914, 28, 6, 45, 14, 46,
            664, 30, 11, 24, 16, 25,
            514, 30, 30, 16, 2, 17,
            // Version 25
            1276, 26, 8, 106, 4, 107,
            1000, 28, 8, 47, 13, 48,
            718, 30, 7, 24, 22, 25,
            538, 30, 22, 15, 13, 16,
            // Version 26
            1370, 28, 10, 114, 2, 115,
            1062, 28, 19, 46, 4, 47,
            754, 28, 28, 22, 6, 23,
            596, 30, 33, 16, 4, 17,
            // Version 27
            1468, 30, 8, 122, 4, 123,
            1128, 28, 22, 45, 3, 46,
            808, 30, 8, 23, 26, 24,
            628, 30, 12, 15, 28, 16,
            // Version 28
            1531, 30, 3, 117, 10, 118,
            1193, 28, 3, 45, 23, 46,
            871, 30, 4, 24, 31, 25,
            661, 30, 11, 15, 31, 16,
            // Version 29
            1631, 30, 7, 116, 7, 117,
            1267, 28, 21, 45, 7, 46,
            911, 30, 1, 23, 37, 24,
            701, 30, 19, 15, 26, 16,
            // Version 30
            1735, 30, 5, 115, 10, 116,
            1373, 28, 19, 47, 10, 48,
            985, 30, 15, 24, 25, 25,
            745, 30, 23, 15, 25, 16,
            // Version 31
            1843, 30, 13, 115, 3, 116,
            1455, 28, 2, 46, 29, 47,
            1033, 30, 42, 24, 1, 25,
            793, 30, 23, 15, 28, 16,
            // Version 32
            1955, 30, 17, 115, 0, 0,
            1541, 28, 10, 46, 23, 47,
            1115, 30, 10, 24, 35, 25,
            845, 30, 19, 15, 35, 16,
            // Version 33
            2071, 30, 17, 115, 1, 116,
            1631, 28, 14, 46, 21, 47,
            1171, 30, 29, 24, 19, 25,
            901, 30, 11, 15, 46, 16,
            // Version 34
            2191, 30, 13, 115, 6, 116,
            1725, 28, 14, 46, 23, 47,
            1231, 30, 44, 24, 7, 25,
            961, 30, 59, 16, 1, 17,
            // Version 35
            2306, 30, 12, 121, 7, 122,
            1812, 28, 12, 47, 26, 48,
            1286, 30, 39, 24, 14, 25,
            986, 30, 22, 15, 41, 16,
            // Version 36
            2434, 30, 6, 121, 14, 122,
            1914, 28, 6, 47, 34, 48,
            1354, 30, 46, 24, 10, 25,
            1054, 30, 2, 15, 64, 16,
            // Version 37
            2566, 30, 17, 122, 4, 123,
            1992, 28, 29, 46, 14, 47,
            1426, 30, 49, 24, 10, 25,
            1096, 30, 24, 15, 46, 16,
            // Version 38
            2702, 30, 4, 122, 18, 123,
            2102, 28, 13, 46, 32, 47,
            1502, 30, 48, 24, 14, 25,
            1142, 30, 42, 15, 32, 16,
            // Version 39
            2812, 30, 20, 117, 4, 118,
            2216, 28, 40, 47, 7, 48,
            1582, 30, 43, 24, 22, 25,
            1222, 30, 10, 15, 67, 16,
            // Version 40
            2956, 30, 19, 118, 6, 119,
            2334, 28, 18, 47, 31, 48,
            1666, 30, 34, 24, 34, 25,
            1276, 30, 20, 15, 61, 16
        ];
        public static ReadOnlySpan<int> CapacityECCBaseValues => capacityECCBaseValues;

        // Alignment pattern positions for each QR code version
        // Array structure: 7 positions per version × 40 versions = 280 elements
        // - Version 1: No alignment patterns (all zeros)
        // - Version 2+: Positions where alignment patterns should be placed
        // - 0 indicates no pattern at that position
        // Based on ISO/IEC 18004 Annex E
        private static readonly int[] alignmentPatternBaseValues = [
            0, 0, 0, 0, 0, 0, 0,
            6, 18, 0, 0, 0, 0, 0,
            6, 22, 0, 0, 0, 0, 0,
            6, 26, 0, 0, 0, 0, 0,
            6, 30, 0, 0, 0, 0, 0,
            6, 34, 0, 0, 0, 0, 0,
            6, 22, 38, 0, 0, 0, 0,
            6, 24, 42, 0, 0, 0, 0,
            6, 26, 46, 0, 0, 0, 0,
            6, 28, 50, 0, 0, 0, 0,
            6, 30, 54, 0, 0, 0, 0,
            6, 32, 58, 0, 0, 0, 0,
            6, 34, 62, 0, 0, 0, 0,
            6, 26, 46, 66, 0, 0, 0,
            6, 26, 48, 70, 0, 0, 0,
            6, 26, 50, 74, 0, 0, 0,
            6, 30, 54, 78, 0, 0, 0,
            6, 30, 56, 82, 0, 0, 0,
            6, 30, 58, 86, 0, 0, 0,
            6, 34, 62, 90, 0, 0, 0,
            6, 28, 50, 72, 94, 0, 0,
            6, 26, 50, 74, 98, 0, 0,
            6, 30, 54, 78, 102, 0, 0,
            6, 28, 54, 80, 106, 0, 0,
            6, 32, 58, 84, 110, 0, 0,
            6, 30, 58, 86, 114, 0, 0,
            6, 34, 62, 90, 118, 0, 0,
            6, 26, 50, 74, 98, 122, 0,
            6, 30, 54, 78, 102, 126, 0,
            6, 26, 52, 78, 104, 130, 0,
            6, 30, 56, 82, 108, 134, 0,
            6, 34, 60, 86, 112, 138, 0,
            6, 30, 58, 86, 114, 142, 0,
            6, 34, 62, 90, 118, 146, 0,
            6, 30, 54, 78, 102, 126, 150,
            6, 24, 50, 76, 102, 128, 154,
            6, 28, 54, 80, 106, 132, 158,
            6, 32, 58, 84, 110, 136, 162,
            6, 26, 54, 82, 110, 138, 166,
            6, 30, 58, 86, 114, 142, 170
        ];
        public static ReadOnlySpan<int> AlignmentPatternBaseValues => alignmentPatternBaseValues;

        // Number of remainder bits for each QR code version (1-40)
        // These bits are added as padding after all data and ECC codewords
        // Values range from 0 to 7 bits depending on version
        // Based on ISO/IEC 18004 Table 1
        private static readonly int[] remainderBits = [
            0, 7, 7, 7, 7, 7,
            0, 0, 0, 0, 0, 0, 0,
            3, 3, 3, 3, 3, 3, 3,
            4, 4, 4, 4, 4, 4, 4,
            3, 3, 3, 3, 3, 3, 3,
            0, 0, 0, 0, 0, 0
        ];

        /// <summary>
        /// Gets the number of remainder bits for a specific QR code version.
        /// </summary>
        /// <param name="version">The QR code version (1-40) for which to retrieve the remainder bits.</param>
        public static int GetRemainderBits(int version)
        {
            return remainderBits.AsSpan()[version - 1];
        }
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
                quietLine[i] = false;
            for (var i = 0; i < quietZoneSize; i++)
                qrCode.ModuleMatrix.Insert(0, new BitArray(quietLine));
            for (var i = 0; i < quietZoneSize; i++)
                qrCode.ModuleMatrix.Add(new BitArray(quietLine));
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
                    newStr += inp[i];
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
            var modules = new[,] { { 8, 0, size - 1, 8 }, { 8, 1, size - 2, 8 }, { 8, 2, size - 3, 8 }, { 8, 3, size - 4, 8 }, { 8, 4, size - 5, 8 }, { 8, 5, size - 6, 8 }, { 8, 7, size - 7, 8 }, { 8, 8, size - 8, 8 }, { 7, 8, 8, size - 7 }, { 5, 8, 8, size - 6 }, { 4, 8, 8, size - 5 }, { 3, 8, 8, size - 4 }, { 2, 8, 8, size - 3 }, { 1, 8, 8, size - 2 }, { 0, 8, 8, size - 1 } };
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
                    ModulePlacer.PlaceFormat(ref qrTemp, formatStr);
                    if (version >= 7)
                    {
                        var versionString = GetVersionString(version);
                        ModulePlacer.PlaceVersion(ref qrTemp, versionString);
                    }

                    for (var x = 0; x < size; x++)
                    {
                        for (var y = 0; y < size; y++)
                        {
                            if (!IsBlocked(new Rectangle(x, y, 1, 1), blockedModules))
                            {
                                qrTemp.ModuleMatrix[y][x] ^= (bool)pattern.Invoke(null, new object[] { x, y });
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
                        qrCode.ModuleMatrix[y][x] ^= (bool)patterMethod.Invoke(null, new object[] { x, y });
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
            blockedModules.AddRange(new[]{
                new Rectangle(7, 0, 1, 8),
                new Rectangle(0, 7, 7, 1),
                new Rectangle(0, size-8, 8, 1),
                new Rectangle(7, size-7, 1, 7),
                new Rectangle(size-8, 0, 1, 8),
                new Rectangle(size-7, 7, 7, 1)
            });
        }

        /// <summary>
        /// Reserves areas for format and version information.
        /// These areas are filled later with actual format/version data.
        /// </summary>
        public static void ReserveVersionAreas(int size, int version, ref List<Rectangle> blockedModules)
        {
            blockedModules.AddRange(new[]{
                new Rectangle(8, 0, 1, 6),
                new Rectangle(8, 7, 1, 1),
                new Rectangle(0, 8, 6, 1),
                new Rectangle(7, 8, 2, 1),
                new Rectangle(size-8, 8, 8, 1),
                new Rectangle(8, size-7, 1, 7)
            });

            if (version >= 7)
            {
                blockedModules.AddRange(new[]{
                new Rectangle(size-11, 0, 3, 6),
                new Rectangle(0, size-11, 6, 3)
            });
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
            int[] locations = { 0, 0, size - 7, 0, 0, size - 7 };

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
                    continue;

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
            blockedModules.AddRange(new[]{
                new Rectangle(6, 8, 1, size-16),
                new Rectangle(8, 6, size-16, 1)
            });
        }

        /// <summary>
        /// Checks if two rectangles intersect.
        /// </summary>
        private static bool Intersects(Rectangle r1, Rectangle r2)
        {
            return r2.X < r1.X + r1.Width && r1.X < r2.X + r2.Width && r2.Y < r1.Y + r1.Height && r1.Y < r2.Y + r2.Height;
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
                    isBlocked = true;
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
            public static bool Pattern1(int x, int y)
            {
                return (x + y) % 2 == 0;
            }

            /// <summary>
            /// Creates horizontal stripes.
            /// </summary>
            public static bool Pattern2(int x, int y)
            {
                return y % 2 == 0;
            }

            /// <summary>
            /// Creates vertical stripes (wider than pattern 1).
            /// </summary>
            public static bool Pattern3(int x, int y)
            {
                return x % 3 == 0;
            }

            /// <summary>
            /// Creates diagonal stripes (wider than pattern 0).
            /// </summary>
            public static bool Pattern4(int x, int y)
            {
                return (x + y) % 3 == 0;
            }

            /// <summary>
            /// Creates a combination of horizontal and vertical patterns.
            /// </summary>
            public static bool Pattern5(int x, int y)
            {
                return ((int)(Math.Floor(y / 2d) + Math.Floor(x / 3d)) % 2) == 0;
            }

            /// <summary>
            /// Creates a complex grid pattern.
            /// </summary>
            public static bool Pattern6(int x, int y)
            {
                return ((x * y) % 2) + ((x * y) % 3) == 0;
            }

            /// <summary>
            /// Creates an alternating complex pattern.
            /// </summary>
            public static bool Pattern7(int x, int y)
            {
                return (((x * y) % 2) + ((x * y) % 3)) % 2 == 0;
            }

            /// <summary>
            /// Creates a combination of checkerboard and grid patterns.
            /// </summary>
            public static bool Pattern8(int x, int y)
            {
                return (((x + y) % 2) + ((x * y) % 3)) % 2 == 0;
            }

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
                            modInRow++;
                        else
                            modInRow = 1;
                        if (modInRow == 5)
                            score1 += 3;
                        else if (modInRow > 5)
                            score1++;
                        lastValRow = qrCode.ModuleMatrix[y][x];


                        if (qrCode.ModuleMatrix[x][y] == lastValColumn)
                            modInColumn++;
                        else
                            modInColumn = 1;
                        if (modInColumn == 5)
                            score1 += 3;
                        else if (modInColumn > 5)
                            score1++;
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
                            score2 += 3;
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
    /// Alignment pattern information for a specific QR code version.
    /// </summary>
    private struct AlignmentPattern
    {
        public int Version;
        public List<Point> PatternPositions;
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
            this.GroupNumber = groupNumber;
            this.BlockNumber = blockNumber;
            this.BitString = bitString;
            this.CodeWords = codeWords;
            this.ECCWords = eccWords;
            this.CodeWordsInt = codeWordsInt;
            this.ECCWordsInt = eccWordsInt;
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
    /// Error correction configuration for a specific version and ECC level.
    /// </summary>
    private struct ECCInfo
    {
        public ECCInfo(int version, ECCLevel errorCorrectionLevel, int totalDataCodewords, int eccPerBlock, int blocksInGroup1,
            int codewordsInGroup1, int blocksInGroup2, int codewordsInGroup2)
        {
            this.Version = version;
            this.ErrorCorrectionLevel = errorCorrectionLevel;
            this.TotalDataCodewords = totalDataCodewords;
            this.ECCPerBlock = eccPerBlock;
            this.BlocksInGroup1 = blocksInGroup1;
            this.CodewordsInGroup1 = codewordsInGroup1;
            this.BlocksInGroup2 = blocksInGroup2;
            this.CodewordsInGroup2 = codewordsInGroup2;
        }
        public int Version { get; }
        public ECCLevel ErrorCorrectionLevel { get; }
        public int TotalDataCodewords { get; }
        public int ECCPerBlock { get; }
        public int BlocksInGroup1 { get; }
        public int CodewordsInGroup1 { get; }
        public int BlocksInGroup2 { get; }
        public int CodewordsInGroup2 { get; }
    }

    /// <summary>
    /// Version information container for QR code capacity data.
    /// Contains all encoding mode capacities for each error correction level.
    /// </summary>
    private struct VersionInfo
    {
        public VersionInfo(int version, List<VersionInfoDetails> versionInfoDetails)
        {
            this.Version = version;
            this.Details = versionInfoDetails;
        }

        /// <summary>QR code version (1-40).</summary>
        public int Version { get; }

        /// <summary>Capacity details for each error correction level (L, M, Q, H).</summary>
        public List<VersionInfoDetails> Details { get; }
    }

    /// <summary>
    /// Capacity information for a specific error correction level.
    /// Maps encoding modes to their maximum character/byte capacity.
    /// </summary>
    private struct VersionInfoDetails
    {
        public VersionInfoDetails(ECCLevel errorCorrectionLevel, Dictionary<EncodingMode, int> capacityDict)
        {
            this.ErrorCorrectionLevel = errorCorrectionLevel;
            this.CapacityDict = capacityDict;
        }

        /// <summary>Error correction level.</summary>
        public ECCLevel ErrorCorrectionLevel { get; }

        /// <summary>
        /// Maximum capacity for each encoding mode.
        /// Key: EncodingMode, Value: Maximum number of characters/bytes.
        /// </summary>
        public Dictionary<EncodingMode, int> CapacityDict { get; }
    }

    /// <summary>
    /// Galois field antilog table entry.
    /// Maps alpha exponent to integer value: α^n → integer (0-255).
    /// Used for Reed-Solomon error correction calculations.
    /// </summary>
    private struct Antilog
    {
        public Antilog(int exponentAlpha, int integerValue)
        {
            this.ExponentAlpha = exponentAlpha;
            this.IntegerValue = integerValue;
        }
        public int ExponentAlpha { get; }
        public int IntegerValue { get; }
    }

    /// <summary>
    /// Polynomial term with coefficient and exponent.
    /// Used in Reed-Solomon error correction algorithm.
    /// Can represent both alpha notation (α^n·x^m) and decimal notation.
    /// </summary>
    private struct PolynomItem
    {
        public PolynomItem(int coefficient, int exponent)
        {
            this.Coefficient = coefficient;
            this.Exponent = exponent;
        }

        public int Coefficient { get; }
        public int Exponent { get; }
    }

    /// <summary>
    /// Polynomial representation for Reed-Solomon calculations.
    /// Collection of PolynomItem terms.
    /// </summary>
    private class Polynom
    {
        public Polynom()
        {
            this.PolyItems = new List<PolynomItem>();
        }

        public List<PolynomItem> PolyItems { get; set; }

        public override string ToString()
        {
            var sb = new StringBuilder();
            //this.PolyItems.ForEach(x => sb.Append("a^" + x.Coefficient + "*x^" + x.Exponent + " + "));
            foreach (var polyItem in this.PolyItems)
            {
                sb.Append("a^" + polyItem.Coefficient + "*x^" + polyItem.Exponent + " + ");
            }

            return sb.ToString().TrimEnd(new[] { ' ', '+' });
        }
    }

    /// <summary>
    /// Simple 2D point structure for alignment pattern coordinates.
    /// </summary>
    private readonly record struct Point
    {
        public int X { get; }
        public int Y { get; }
        public Point(int x, int y)
        {
            this.X = x;
            this.Y = y;
        }
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
            this.X = x;
            this.Y = y;
            this.Width = w;
            this.Height = h;
        }
    }

    public void Dispose()
    {
        this.alignmentPatternTable = null;
        this.alphanumEncDict = null;
        this.capacityECCTable = null;
        this.capacityTable = null;
        this.galoisField = null;
    }
}
