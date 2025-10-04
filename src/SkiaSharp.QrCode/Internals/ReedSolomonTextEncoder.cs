using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using static SkiaSharp.QrCode.Internals.QRCodeConstants;

namespace SkiaSharp.QrCode.Internals;

/// <summary>
/// Reed-Solomon error correction encoder (text-based).
/// Temporary text-based implementation for migration to byte-based architecture.
/// </summary>
/// <remarks>
/// This encoder implements Reed-Solomon error correction using polynomial operations
/// on binary string representations.
/// 
/// Reed-Solomon error correction process:
/// 1. Convert data bit string to message polynomial
/// 2. Generate Reed-Solomon generator polynomial (based on required ECC words)
/// 3. Multiply message polynomial by x^n (where n = ECC word count)
/// 4. Perform polynomial division in GF(256) using XOR operations
/// 5. Remainder becomes the error correction codewords
/// </remarks>
internal class ReedSolomonTextEncoder
{
    /// <summary>
    /// Calculates error correction codewords using Reed-Solomon algorithm.
    /// </summary>
    /// <param name="dataBits">Binary string representing data codewords (multiple of 8 bits).</param>
    /// <param name="eccWordCount">Number of error correction codewords to generate.</param>
    /// <returns>List of ECC codewords as binary strings (8 bits each).</returns>
    /// <remarks>
    /// Algorithm (ISO/IEC 18004 Section 8.5):
    /// 1. Create message polynomial M(x) from data bits
    /// 2. Create generator polynomial G(x) = (x-α^0)(x-α^1)...(x-α^(n-1))
    /// 3. Multiply M(x) by x^n to shift coefficients
    /// 4. Divide M(x)·x^n by G(x) using GF(256) arithmetic
    /// 5. Remainder R(x) contains the ECC codewords
    /// 
    /// Example (simplified):
    /// - Data: [64, 86, 134, 86] → M(x) = 64x^3 + 86x^2 + 134x + 86
    /// - ECC count: 10
    /// - Generator: G(x) = (x-α^0)(x-α^1)...(x-α^9)
    /// - Result: 10 ECC codewords [196, 35, 39, 119, 235, 215, 231, 226, 93, 23]
    /// </remarks>
    public List<string> CalculateECC(string dataBits, int eccWordCount)
    {
        var messagePolynom = CalculateMessagePolynom(dataBits);
        var generatorPolynom = CalculateGeneratorPolynom(eccWordCount);

        // Multiply message polynomial by x^eccWordCount (shift exponents)
        for (var i = 0; i < messagePolynom.PolyItems.Count; i++)
        {
            var coefficient = messagePolynom.PolyItems[i].Coefficient;
            var exponent = messagePolynom.PolyItems[i].Exponent + eccWordCount;
            messagePolynom.PolyItems[i] = new PolynomItem(coefficient, exponent);
        }

        // Multiply generator polynomial by x^(message degree)
        for (var i = 0; i < generatorPolynom.PolyItems.Count; i++)
        {
            var coefficient = generatorPolynom.PolyItems[i].Coefficient;
            var exponent = generatorPolynom.PolyItems[i].Exponent + (messagePolynom.PolyItems.Count - 1);
            generatorPolynom.PolyItems[i] = new PolynomItem(coefficient, exponent);
        }

        // Perform polynomial division in GF(256)
        var leadTermSource = messagePolynom;
        for (var i = 0; (leadTermSource.PolyItems.Count > 0 && leadTermSource.PolyItems[^1].Exponent > 0); i++)
        {
            if (leadTermSource.PolyItems[0].Coefficient == 0)
            {
                leadTermSource.PolyItems.RemoveAt(0);
                leadTermSource.PolyItems.Add(new PolynomItem(0, leadTermSource.PolyItems[^1].Exponent - 1));
            }
            else
            {
                var resPoly = MultiplyGeneratorPolynomByLeadterm(generatorPolynom, ConvertToAlphaNotation(leadTermSource).PolyItems[0], i);
                resPoly = ConvertToDecNotation(resPoly);
                resPoly = XORPolynoms(leadTermSource, resPoly);
                leadTermSource = resPoly;
            }
        }
        return leadTermSource.PolyItems
            .Select(x => DecToBin(x.Coefficient, 8))
            .ToList();
    }

    /// <summary>
    /// Creates message polynomial from bit string.
    /// Each 8-bit block becomes a coefficient with decreasing exponent.
    /// </summary>
    /// <param name="bitString">Binary data string.</param>
    /// <returns>Polynomial representation of message.</returns>
    /// <remarks>
    /// Example: "11010011 10101100" → α^1·x^1 + α^0·x^0
    /// Example:
    /// Input: "01000000 01010110 10000110 01010110" (32 bits = 4 bytes)
    /// Output: M(x) = 64x^3 + 86x^2 + 134x + 86
    /// 
    /// Processing:
    /// - "01000000" (64) → coefficient for x^3
    /// - "01010110" (86) → coefficient for x^2
    /// - "10000110" (134) → coefficient for x^1
    /// - "01010110" (86) → coefficient for x^0
    /// </remarks>
    private Polynom CalculateMessagePolynom(string bitString)
    {
        var messagePol = new Polynom();
        var byteCount = bitString.Length / 8;
        for (var i = byteCount - 1; i >= 0; i--)
        {
            var byteBits = bitString.Substring(0, 8);
            var coefficient = BinToDec(byteBits);
            messagePol.PolyItems.Add(new PolynomItem(coefficient, i));
            bitString = bitString.Remove(0, 8);
        }
        return messagePol;
    }

    /// <summary>
    /// Generates Reed-Solomon generator polynomial.
    /// </summary>
    /// <param name="eccWordCount">Number of error correction words needed.</param>
    /// <returns>Generator polynomial in alpha notation.</returns>
    /// <remarks>
    /// Formula: (x - α^0)(x - α^1)...(x - α^(n-1)) where n = numEccWords
    /// Example for eccWordCount = 3:
    /// G(x) = (x - α^0)(x - α^1)(x - α^2)
    ///      = (x - 1)(x - α)(x - α^2)
    ///      = x^3 + α^0·x^2 + α^1·x + α^0
    /// 
    /// The generator polynomial is built iteratively:
    /// 1. Start with (x - α^0) = [α^0·x^1, α^0·x^0]
    /// 2. Multiply by (x - α^1), (x - α^2), etc.
    /// 3. Result has degree equal to eccWordCount
    /// </remarks>
    private Polynom CalculateGeneratorPolynom(int eccWordCount)
    {
        // init with (x - α^0)
        var generatorPolynom = new Polynom();
        generatorPolynom.PolyItems.AddRange([
            new PolynomItem(0, 1), // α^0 x^1
            new PolynomItem(0, 0), // α^0 x^0
        ]);

        // Multiply by (x - α^i) for i = 1 to eccWordCount - 1
        for (var i = 1; i <= eccWordCount - 1; i++)
        {
            var multiplierPolynom = new Polynom();
            multiplierPolynom.PolyItems.AddRange([
                new PolynomItem(0, 1), // α^0 x^1
                new PolynomItem(i, 0) // α^i x^0
            ]);
            generatorPolynom = MultiplyAlphaPolynoms(generatorPolynom, multiplierPolynom);
        }

        return generatorPolynom;
    }

    // Galois Field Operations (GF(256))

    /// <summary>
    /// Converts polynomial from decimal notation to alpha notation (α^n).
    /// </summary>
    /// <param name="poly">Polynomial in decimal notation.</param>
    /// <returns>Polynomial in alpha notation.</returns>
    /// <remarks>
    /// Example:
    /// Decimal: 64x^3 + 86x^2 + 134x + 86
    /// Alpha:   α^192·x^3 + α^25·x^2 + α^173·x + α^25
    /// 
    /// Uses Galois field lookup: integer → α^n exponent
    /// </remarks>
    private Polynom ConvertToAlphaNotation(Polynom poly)
    {
        var newPoly = new Polynom();
        for (var i = 0; i < poly.PolyItems.Count; i++)
        {
            var coefficient = poly.PolyItems[i].Coefficient != 0
                ? GetAlphaExpFromIntVal(poly.PolyItems[i].Coefficient)
                : 0;
            var data = new PolynomItem(coefficient, poly.PolyItems[i].Exponent);
            newPoly.PolyItems.Add(data);
        }
        return newPoly;
    }

    /// <summary>
    /// Converts polynomial from alpha notation (α^n) to decimal notation.
    /// </summary>
    /// <param name="poly">Polynomial in alpha notation.</param>
    /// <returns>Polynomial in decimal notation.</returns>
    /// <remarks>
    /// Example:
    /// Alpha:   α^192·x^3 + α^25·x^2 + α^173·x + α^25
    /// Decimal: 64x^3 + 86x^2 + 134x + 86
    /// 
    /// Uses Galois field lookup: α^n exponent → integer
    /// </remarks>
    private Polynom ConvertToDecNotation(Polynom poly)
    {
        var newPoly = new Polynom();
        for (var i = 0; i < poly.PolyItems.Count; i++)
        {
            var coefficient = GetIntValFromAlphaExp(poly.PolyItems[i].Coefficient);
            var data = new PolynomItem(coefficient, poly.PolyItems[i].Exponent);
            newPoly.PolyItems.Add(data);
        }
        return newPoly;
    }

    /// <summary>
    /// Performs XOR operation on two polynomials coefficient-wise.
    /// Used in Reed-Solomon division process (GF(256) subtraction = XOR).
    /// </summary>
    /// <param name="messagePolynom">Message polynomial.</param>
    /// <param name="resPolynom">Result polynomial from previous step.</param>
    /// <returns>XORed polynomial.</returns>
    /// <remarks>
    /// Example:
    /// Message: 64x^3 + 86x^2 + 134x + 86
    /// Result:  64x^3 + 20x^2 + 200x + 150
    /// XOR:     0x^3 + 70x^2 + 78x + 196
    /// 
    /// In GF(256): subtraction = addition = XOR
    /// </remarks>
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
            var coefficient = longPoly.PolyItems[i].Coefficient
                ^ (shortPoly.PolyItems.Count > i ? shortPoly.PolyItems[i].Coefficient : 0);
            var data = new PolynomItem(coefficient, messagePolynom.PolyItems[0].Exponent - i);
            resultPolynom.PolyItems.Add(data);
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
    /// <remarks>
    /// In GF(256):
    /// - Multiplication: Add exponents (with modulo 255)
    /// - Addition: XOR in decimal, then convert back to alpha
    /// 
    /// Example:
    /// (α^2·x + α^3) × (x + α^1)
    /// = α^2·x^2 + α^3·x + α^3·x + α^4
    /// = α^2·x^2 + (α^3 ⊕ α^3)·x + α^4  (⊕ = XOR)
    /// = α^2·x^2 + 0·x + α^4
    /// </remarks>
    private Polynom MultiplyAlphaPolynoms(Polynom polynomBase, Polynom polynomMultiplier)
    {
        var resultPolynom = new Polynom();

        // Multiply each term of multiplier with each term of base
        foreach (var polItemBase in polynomMultiplier.PolyItems)
        {
            foreach (var polItemMulti in polynomBase.PolyItems)
            {
                var coefficient = ShrinkAlphaExp(polItemBase.Coefficient + polItemMulti.Coefficient);
                var polItemRes = new PolynomItem(coefficient, (polItemBase.Exponent + polItemMulti.Exponent));
                resultPolynom.PolyItems.Add(polItemRes);
            }
        }

        // Find duplicate exponents
        var exponentsToGlue = resultPolynom.PolyItems
            .GroupBy(x => x.Exponent)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key)
            .ToList();

        // Combine duplicates using XOR
        var gluedPolynoms = new List<PolynomItem>();
        foreach (var exponent in exponentsToGlue)
        {
            var coefficient = resultPolynom.PolyItems
                .Where(x => x.Exponent == exponent)
                .Aggregate(0, (current, polynomOld) => current ^ GetIntValFromAlphaExp(polynomOld.Coefficient));
            var polynomFixed = new PolynomItem(GetAlphaExpFromIntVal(coefficient), exponent);
            gluedPolynoms.Add(polynomFixed);
        }

        // Remove duplicates and add combined terms
        resultPolynom.PolyItems.RemoveAll(x => exponentsToGlue.Contains(x.Exponent));
        resultPolynom.PolyItems.AddRange(gluedPolynoms);
        resultPolynom.PolyItems = resultPolynom.PolyItems.OrderByDescending(x => x.Exponent).ToList();

        return resultPolynom;
    }

    /// <summary>
    /// Multiplies generator polynomial by lead term during Reed-Solomon division.
    /// Part of the polynomial division process in GF(256).
    /// </summary>
    /// <param name="genPolynom">Generator polynomial in alpha notation.</param>
    /// <param name="leadTerm">Lead term of current message polynomial.</param>
    /// <param name="lowerExponentBy">Amount to reduce exponents.</param>
    /// <returns>Multiplied polynomial.</returns>
    private Polynom MultiplyGeneratorPolynomByLeadterm(Polynom genPolynom, PolynomItem leadTerm, int lowerExponentBy)
    {
        var resultPolynom = new Polynom();
        foreach (var polItemBase in genPolynom.PolyItems)
        {
            var coefficient = (polItemBase.Coefficient + leadTerm.Coefficient) % 255;
            var polItemRes = new PolynomItem(coefficient, polItemBase.Exponent - lowerExponentBy);
            resultPolynom.PolyItems.Add(polItemRes);
        }
        return resultPolynom;
    }

    /// <summary>
    /// Normalizes alpha exponent to 0-255 range.
    /// Formula: (exp mod 256) + floor(exp / 256)
    /// </summary>
    /// <param name="alphaExp">Alpha exponent to normalize.</param>
    /// <returns>Normalized exponent (0-255).</returns>
    /// <remarks>
    /// In GF(256), α^255 = α^0 (cyclic group)
    /// So we need to keep exponents in range [0, 255]
    /// </remarks>
    private static int ShrinkAlphaExp(int alphaExp)
    {
        return (int)((alphaExp % 256) + Math.Floor((double)(alphaExp / 256)));
    }

    // helpers

    /// <summary>
    /// Converts binary string to decimal integer.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int BinToDec(string binStr)
    {
        return Convert.ToInt32(binStr, 2);
    }

    /// <summary>
    /// Converts decimal number to binary string with optional padding.
    /// </summary>
    /// <param name="decNum">Decimal number.</param>
    /// <param name="padLeftUpTo">Minimum bit length (pads with leading zeros).</param>
    /// <returns>Binary string.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string DecToBin(int decNum, int padLeftUpTo)
    {
        var binStr = Convert.ToString(decNum, 2);
        return binStr.PadLeft(padLeftUpTo, '0');
    }
}
