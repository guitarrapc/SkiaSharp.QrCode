using static SkiaSharp.QrCode.Internals.QRCodeConstants;

namespace SkiaSharp.QrCode.Internals;

/// <summary>
/// Ecc encoder with Reed-Solomon error correction (text-based).
/// </summary>
/// <remarks>
/// This ecc encoder implements Reed-Solomon error correction using polynomial operations
/// on binary string representations.
/// 
/// Reed-Solomon error correction process:
/// 1. Convert data bit string to message polynomial
/// 2. Generate Reed-Solomon generator polynomial (based on required ECC words)
/// 3. Multiply message polynomial by x^n (where n = ECC word count)
/// 4. Perform polynomial division in GF(256) using XOR operations
/// 5. Remainder becomes the error correction codewords
/// </remarks>
internal class EccTextEncoder
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
        for (var i = 0; i < messagePolynom.Count; i++)
        {
            var coefficient = messagePolynom[i].Coefficient;
            var exponent = messagePolynom[i].Exponent + eccWordCount;
            messagePolynom[i] = new PolynomItem(coefficient, exponent);
        }

        // Perform polynomial division in GF(256)
        var leadTermSource = messagePolynom;
        for (var i = 0; (leadTermSource.Count > 0 && leadTermSource[^1].Exponent > 0); i++)
        {
            if (leadTermSource[0].Coefficient == 0)
            {
                // Shift zero coefficient: remove lead term and add zero at lower degree
                leadTermSource.RemoveAt(0);
                leadTermSource.Add(new PolynomItem(0, leadTermSource[^1].Exponent - 1));
            }
            else
            {
                // Multiply generator by lead term, convert to decimal, and XOR with current polynomial
                var resPoly = MultiplyGeneratorPolynomByLeadterm(generatorPolynom, ConvertToAlphaNotation(leadTermSource)[0], i);
                resPoly = ConvertToDecNotation(resPoly);
                resPoly = XORPolynoms(leadTermSource, resPoly);
                leadTermSource = resPoly;
            }
        }

        // --- Important: Reconstruct coefficients by degree to ensure fixed length ---
        // Reed-Solomon remainder polynomial R(x) has degree < eccWordCount,
        // so we need exactly eccWordCount coefficients (from x^(n-1) down to x^0).
        // Missing exponents are implicitly 0 (e.g., sparse polynomials).
        var coeffByExp = BuildCoeffDictionary(leadTermSource.Items, eccWordCount);
        var ecc = new List<string>(eccWordCount);
        for (int exp = eccWordCount - 1; exp >= 0; exp--)
        {
            coeffByExp.TryGetValue(exp, out int c); // 0 if missing
            ecc.Add(DecToBin(c, 8));
        }
        return ecc;

        // Same as `var coeffByExp = leadTermSource.PolyItems.ToDictionary(p => p.Exponent, p => p.Coefficient);`, but avoid exception on duplicates
        static Dictionary<int, int> BuildCoeffDictionary(IReadOnlyList<PolynomItem> polyItems, int eccWordCount)
        {
            var coeffByExp = new Dictionary<int, int>(Math.Min(polyItems.Count, eccWordCount));
            foreach (var item in polyItems)
            {
                // If the same exponent already exists, combine using XOR (equivalent to addition in GF(256))
                // exp: α^3 + α^3 = 0, α^3 + α^2 + α^3 = α^2
                coeffByExp[item.Exponent] = coeffByExp.TryGetValue(item.Exponent, out var existing)
                    ? existing ^ item.Coefficient
                    : item.Coefficient;
            }
            return coeffByExp;
        }
    }

    /// <summary>
    /// Creates message polynomial from bit string.
    /// Each 8-bit block becomes a coefficient with decreasing exponent.
    /// </summary>
    /// <param name="bitString">Binary data string.</param>
    /// <returns>Polynomial representation of message.</returns>
    /// <remarks>
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

        // Process from left to right, highest degree to lowest
        for (var i = byteCount - 1; i >= 0; i--)
        {
            var byteBits = bitString[..8];
            var coefficient = BinToDec(byteBits);
            messagePol.Add(new (coefficient, i));
            bitString = bitString[8..];
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
        generatorPolynom.AddRange([
            new (0, 1), // α^0 x^1
            new (0, 0), // α^0 x^0
        ]);

        // Multiply by (x - α^i) for i = 1 to eccWordCount - 1
        for (var i = 1; i <= eccWordCount - 1; i++)
        {
            var multiplierPolynom = new Polynom();
            multiplierPolynom.AddRange([
                new (0, 1), // α^0 x^1
                new (i, 0) // α^i x^0
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
        foreach (var item in poly.Items)
        {
            var coefficient = item.Coefficient != 0
                ? GetAlphaExpFromIntVal(item.Coefficient)
                : 0;
            newPoly.Add(new PolynomItem(coefficient, item.Exponent));
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
        foreach (var item in poly.Items)
        {
            var coefficient = GetIntValFromAlphaExp(item.Coefficient);
            newPoly.Add(new PolynomItem(coefficient, item.Exponent));
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
        if (messagePolynom.Count >= resPolynom.Count)
        {
            longPoly = messagePolynom;
            shortPoly = resPolynom;
        }
        else
        {
            longPoly = resPolynom;
            shortPoly = messagePolynom;
        }

        for (var i = 0; i < longPoly.Count; i++)
        {
            var coefficient = longPoly[i].Coefficient
                ^ (shortPoly.Count > i ? shortPoly[i].Coefficient : 0);
            var data = new PolynomItem(coefficient, messagePolynom[0].Exponent - i);
            resultPolynom.Add(data);
        }
        resultPolynom.RemoveAt(0);
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
        foreach (var polItemBase in polynomMultiplier.Items)
        {
            foreach (var polItemMulti in polynomBase.Items)
            {
                var coefficient = ShrinkAlphaExp(polItemBase.Coefficient + polItemMulti.Coefficient);
                var exponent = polItemBase.Exponent + polItemMulti.Exponent;
                resultPolynom.Add(new PolynomItem(coefficient, exponent));
            }
        }

        // Find exponents that appear multiple times (need to combine via XOR)
        var duplicateExponents = resultPolynom.Items
            .GroupBy(x => x.Exponent)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key)
            .ToList();

        // Combine duplicate exponents using XOR (GF(256) addition)
        var combinedTerms = new List<PolynomItem>(duplicateExponents.Count);
        foreach (var exponent in duplicateExponents)
        {
            var coefficient = resultPolynom.Items
                .Where(x => x.Exponent == exponent)
                .Select(x => GetIntValFromAlphaExp(x.Coefficient))
                .Aggregate(0, (current, val) => current ^ val);
            combinedTerms.Add(new PolynomItem(GetAlphaExpFromIntVal(coefficient), exponent));
        }

        // Remove duplicates and add combined terms
        resultPolynom.RemoveAll(x => duplicateExponents.Contains(x.Exponent));
        resultPolynom.AddRange(combinedTerms);
        resultPolynom.SortByExponentDescending();

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
        foreach (var polItemBase in genPolynom.Items)
        {
            var coefficient = (polItemBase.Coefficient + leadTerm.Coefficient) % 255;
            var polItemRes = new PolynomItem(coefficient, polItemBase.Exponent - lowerExponentBy);
            resultPolynom.Add(polItemRes);
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
}
