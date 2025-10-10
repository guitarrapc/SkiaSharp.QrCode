namespace SkiaSharp.QrCode.Internals.BinaryEncoders;

/// <summary>
/// Ecc encoder with Reed-Solomon error correction
/// </summary>
/// <remarks>
/// This ecc encoder implements Reed-Solomon error correction using polynomial operations on binary representations.
/// 
/// Reed-Solomon error correction process:
/// 1. Convert data bit string to message polynomial
/// 2. Generate Reed-Solomon generator polynomial (based on required ECC words)
/// 3. Multiply message polynomial by x^n (where n = ECC word count)
/// 4. Perform polynomial division in GF(256) using XOR operations
/// 5. Remainder becomes the error correction codewords
/// </remarks>
internal static class EccBinaryEncoder
{
    /// <summary>
    /// Calculates error correction codewords using Reed-Solomon algorithm.
    /// </summary>
    /// <param name="data">Binary string representing data codewords (multiple of 8 bits).</param>
    /// <param name="eccCount">Number of error correction codewords to generate.</param>
    /// <returns>List of ECC codewords as binary strings (8 bits each).</returns>
    /// <remarks>
    /// Algorithm (ISO/IEC 18004 Section 8.5):
    /// 1. Create generator polynomial G(x) = (x-α^0)(x-α^1)...(x-α^(n-1))
    /// 2. Create message polynomial M(x) from data bytes
    /// 3. Multiply M(x) by x^n to shift coefficients (shift by eccCount positions)
    /// 4. Divide M(x)·x^n by G(x) using GF(256) arithmetic
    /// 5. Remainder R(x) contains the ECC codewords
    /// 
    /// Example (simplified):
    /// - Data: [64, 86, 134, 86] → M(x) = 64x^3 + 86x^2 + 134x + 86
    /// - ECC count: 10
    /// - Generator: G(x) = (x-α^0)(x-α^1)...(x-α^9)
    /// - Result: 10 ECC codewords [196, 35, 39, 119, 235, 215, 231, 226, 93, 23]
    /// </remarks>
    public static void CalculateECC(ReadOnlySpan<byte> data, Span<byte> ecc, int eccCount)
    {
        // Generate generator polynomial
        Span<byte> generator = stackalloc byte[eccCount + 1];
        GenerateGeneratorpolynomial(generator, eccCount);

        // Initialize message polynomial from data bits (data + zero padding for ECC)
        Span<byte> message = stackalloc byte[data.Length + eccCount];
        data.CopyTo(message);

        // Polynomial division in GF(256)
        for (var i = 0; i < data.Length; i++)
        {
            var cofficient = message[i];
            if (cofficient == 0) continue;

            // XOR with generator polynomial scaled by lead coefficient
            for (var j = 0; j < eccCount; j++)
            {
                message[i + j + 1] ^= GaloisField.Multiply(generator[j + 1], cofficient);
            }
        }

        // Extract ECC bytes (remainder of division)
        message.Slice(data.Length, eccCount).CopyTo(ecc);
    }

    /// <summary>
    /// Generates Reed-Solomon generator polynomial.
    /// </summary>
    /// <param name="generator">Output buffer for generator polynomial coefficients.</param>
    /// <param name="eccCount">Number of error correction codewords.</param>
    /// <remarks>
    /// Formula: (x - α^0)(x - α^1)...(x - α^(n-1)) where n = numEccWords
    /// Example for eccWordCount = 3:
    /// G(x) = (x - α^0)(x - α^1)(x - α^2)
    ///      = (x - 1)(x - α)(x - α^2)
    ///      = x^3 + α^0·x^2 + α^1·x + α^0
    /// 
    /// The generator polynomial is built iteratively:
    /// 1. Start: G(x) = 1
    /// 2. Multiply by (x - α^0): G(x) = x - 1
    /// 3. Multiply by (x - α^1): G(x) = x^2 + (α^0)x + (α^1)
    /// 4. Continue until degree = eccCount. Result has degree = eccCount.
    /// </remarks>
    private static void GenerateGeneratorpolynomial(Span<byte> generator, int eccCount)
    {
        generator.Clear();

        // init with (x - α^0), start with polynomial "1"
        generator[0] = 1;

        Span<byte> temp = stackalloc byte[generator.Length];

        // Multiply by (x - α^i) for i = 1 to eccWordCount - 1
        for (var i = 0; i < eccCount; i++)
        {
            // Multiply current generator by (x - α^i)
            temp.Clear();

            for (var j = 0; j <= i; j++)
            {
                var coeficient = generator[j];
                if (coeficient == 0) continue;

                // (x - α^i) expansion
                // - Multuply existing term by x: shift to temp[j]
                // - Multuply existing term by -α^i: add to temp[j+1]
                temp[j] ^= coeficient; // x^1 term
                temp[j + 1] ^= GaloisField.Multiply(coeficient, GaloisField.Exp[i]);
            }

            temp.CopyTo(generator);
        }
    }
}
