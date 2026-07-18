namespace SkiaSharp.QrCode.Internals.BinaryDecoders;

/// <summary>
/// Reed-Solomon error correction decoder for QR code decoding.
/// </summary>
/// <remarks>
/// Inverse of <see cref="BinaryEncoders.EccBinaryEncoder"/>. The encoder builds its
/// generator polynomial as G(x) = (x-α^0)(x-α^1)...(x-α^(n-1)), so the decoder
/// evaluates syndromes at the same consecutive roots starting at α^0 (b = 0).
/// Implements the classical pipeline over GF(256) with primitive polynomial 0x11D:
/// <code>
/// 1. Syndromes:        S_i = R(α^i) for i = 0..eccCount-1 (all zero → no errors)
/// 2. Berlekamp-Massey: error locator polynomial Λ(x) with deg(Λ) = L errors
/// 3. Chien search:     roots of Λ(x) are X_k^-1 → error positions
/// 4. Forney:           magnitudes e_k = X_k·Ω(X_k^-1)/Λ'(X_k^-1)  (b = 0)
/// </code>
/// Corrects up to ⌊eccCount/2⌋ byte errors per block, in place.
/// <para>
/// The syndrome pass (the only cost clean blocks pay, and the dominant cost of the
/// verification pass) dispatches to a GFNI kernel on net10.0+ x64 (see
/// EccBinaryDecoder.Simd.cs) with a scalar log-domain fallback everywhere else.
/// All kernels produce byte-identical output; see the decoder kernel parity tests.
/// </para>
/// </remarks>
internal static partial class EccBinaryDecoder
{
    // QR codes use at most 30 ECC codewords per block (ISO/IEC 18004), so all
    // intermediate polynomials fit in fixed stackalloc buffers.
    private const int MaxEccPerBlock = 30;
    private const int MaxErrors = MaxEccPerBlock / 2;

    /// <summary>
    /// Detects and corrects codeword errors in a single Reed-Solomon block, in place.
    /// </summary>
    /// <param name="codeword">Block codewords (data followed by ECC). Corrected in place.</param>
    /// <param name="eccCount">Number of ECC codewords at the tail of <paramref name="codeword"/> (1-30).</param>
    /// <param name="errorsCorrected">Number of byte errors corrected (0 when the block was clean).</param>
    /// <returns>
    /// True when the block is clean or was fully corrected; false when the block
    /// contains more errors than the code can correct.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    /// <exception cref="ArgumentException"></exception>
    public static bool TryCorrect(Span<byte> codeword, int eccCount, out int errorsCorrected)
    {
        if (eccCount < 1 || eccCount > MaxEccPerBlock)
            throw new ArgumentOutOfRangeException(nameof(eccCount), $"ECC count must be 1-{MaxEccPerBlock}, got {eccCount}");
        if (codeword.Length <= eccCount || codeword.Length > 255)
            throw new ArgumentException($"Codeword length must be in ({eccCount}, 255], got {codeword.Length}", nameof(codeword));

        errorsCorrected = 0;

        // 1. Syndromes: S_i = R(α^i) where codeword[0] is the highest-degree
        // coefficient (the same polynomial orientation the encoder divides in).
        Span<byte> syndromes = stackalloc byte[MaxEccPerBlock];
        syndromes = syndromes.Slice(0, eccCount);
        var hasError = ComputeSyndromes(codeword, eccCount, syndromes);

        if (!hasError)
            return true;

        // 2. Berlekamp-Massey: find the minimal LFSR Λ(x) generating the syndromes.
        // Λ(x) = 1 + Λ_1·x + ... + Λ_L·x^L, roots at X_k^-1 for each error locator X_k.
        Span<byte> lambda = stackalloc byte[MaxEccPerBlock + 1];
        Span<byte> prev = stackalloc byte[MaxEccPerBlock + 1]; // B(x): copy of Λ at the last length change
        Span<byte> temp = stackalloc byte[MaxEccPerBlock + 1];
        lambda.Clear();
        prev.Clear();
        lambda[0] = 1;
        prev[0] = 1;
        var errorCountEstimate = 0; // L: current LFSR length
        var gap = 1;                // m: steps since B(x) was updated
        byte lastDiscrepancy = 1;   // b: discrepancy at the last length change

        for (var n = 0; n < eccCount; n++)
        {
            // Discrepancy δ = S_n + Σ_{i=1..L} Λ_i·S_(n-i)
            var delta = syndromes[n];
            for (var i = 1; i <= errorCountEstimate; i++)
            {
                delta ^= GaloisField.Multiply(lambda[i], syndromes[n - i]);
            }

            if (delta == 0)
            {
                gap++;
                continue;
            }

            var coefficient = GaloisField.Divide(delta, lastDiscrepancy);
            if (2 * errorCountEstimate <= n)
            {
                // Length change: Λ(x) ← Λ(x) - (δ/b)·x^m·B(x), then B(x) ← old Λ(x)
                lambda.CopyTo(temp);
                for (var i = 0; i + gap <= MaxEccPerBlock; i++)
                {
                    lambda[i + gap] ^= GaloisField.Multiply(coefficient, prev[i]);
                }
                errorCountEstimate = n + 1 - errorCountEstimate;
                temp.CopyTo(prev);
                lastDiscrepancy = delta;
                gap = 1;
            }
            else
            {
                // Same length: Λ(x) ← Λ(x) - (δ/b)·x^m·B(x)
                for (var i = 0; i + gap <= MaxEccPerBlock; i++)
                {
                    lambda[i + gap] ^= GaloisField.Multiply(coefficient, prev[i]);
                }
                gap++;
            }
        }

        // More errors than the code can correct
        if (2 * errorCountEstimate > eccCount)
            return false;

        // 3. Chien search: codeword[idx] is the coefficient of x^(n-1-idx), so an
        // error at idx has locator X = α^(n-1-idx). Test every position p by
        // evaluating Λ(α^-p); a zero means an error at idx = n-1-p.
        Span<int> errorIndexes = stackalloc int[MaxErrors];
        Span<byte> errorLocators = stackalloc byte[MaxErrors];        // X_k = α^p
        Span<byte> errorLocatorInverses = stackalloc byte[MaxErrors]; // X_k^-1 = α^-p
        var errorCount = 0;
        var length = codeword.Length;
        for (var p = 0; p < length; p++)
        {
            var xInverse = GaloisField.Exp[(255 - p) % 255]; // α^-p
            // Horner evaluation of Λ(xInverse), degree L
            byte value = lambda[errorCountEstimate];
            for (var i = errorCountEstimate - 1; i >= 0; i--)
            {
                value = (byte)(GaloisField.Multiply(value, xInverse) ^ lambda[i]);
            }

            if (value != 0)
                continue;

            if (errorCount == errorCountEstimate)
                return false; // more roots than deg(Λ), inconsistent, uncorrectable

            errorIndexes[errorCount] = length - 1 - p;
            errorLocators[errorCount] = GaloisField.Exp[p % 255];
            errorLocatorInverses[errorCount] = xInverse;
            errorCount++;
        }

        // Every root of Λ must lie inside the codeword; a deficit means errors
        // at positions that do not exist (degree mismatch), uncorrectable.
        if (errorCount != errorCountEstimate)
            return false;

        // 4. Forney: Ω(x) = S(x)·Λ(x) mod x^eccCount has degree < L when the error
        // count is within capacity, so only the first L coefficients are needed.
        Span<byte> omega = stackalloc byte[MaxErrors];
        for (var i = 0; i < errorCount; i++)
        {
            byte value = 0;
            var maxJ = Math.Min(i, errorCountEstimate);
            for (var j = 0; j <= maxJ; j++)
            {
                value ^= GaloisField.Multiply(syndromes[i - j], lambda[j]);
            }
            omega[i] = value;
        }

        for (var k = 0; k < errorCount; k++)
        {
            var xInverse = errorLocatorInverses[k];

            // Ω(X_k^-1) via Horner, degree L-1
            byte omegaValue = 0;
            for (var i = errorCount - 1; i >= 0; i--)
            {
                omegaValue = (byte)(GaloisField.Multiply(omegaValue, xInverse) ^ omega[i]);
            }

            // Formal derivative in GF(2^8) keeps only odd-power terms:
            // Λ'(x) = Λ_1 + Λ_3·x^2 + Λ_5·x^4 + ...
            byte denominator = 0;
            var xInverseSquared = GaloisField.Multiply(xInverse, xInverse);
            byte xPower = 1; // xInverse^(i-1) for i = 1, 3, 5, ...
            for (var i = 1; i <= errorCountEstimate; i += 2)
            {
                denominator ^= GaloisField.Multiply(lambda[i], xPower);
                xPower = GaloisField.Multiply(xPower, xInverseSquared);
            }

            if (denominator == 0)
                return false;

            // e_k = X_k^(1-b)·Ω(X_k^-1)/Λ'(X_k^-1); with b = 0 the X_k factor remains.
            var magnitude = GaloisField.Multiply(errorLocators[k], GaloisField.Divide(omegaValue, denominator));
            codeword[errorIndexes[k]] ^= magnitude;
        }

        // Verify the correction: recomputed syndromes must all be zero. This guards
        // against silent miscorrection (worse than failing) at the cost of one extra
        // syndrome pass, taken only on blocks that actually had errors.
        Span<byte> check = stackalloc byte[MaxEccPerBlock];
        if (ComputeSyndromes(codeword, eccCount, check.Slice(0, eccCount)))
            return false;

        errorsCorrected = errorCount;
        return true;
    }

    /// <summary>
    /// Computes the syndromes S_i = R(α^i) for i = 0..eccCount-1 via Horner
    /// evaluation: v = (((c_0·x + c_1)·x + c_2)·x + ...) at x = α^i.
    /// Returns true when any syndrome is non-zero (the block has errors).
    /// </summary>
    /// <remarks>
    /// Dispatches to the GFNI kernel (all accumulators in one vector register, one
    /// multiply per data byte for every syndrome at once) when available; the scalar
    /// path keeps the Horner multiply in log domain, the multiplier's log is the
    /// constant i, so each step is one zero-check + one log load + one exp load
    /// (measured 0.84-0.90x of the GaloisField.Multiply form).
    /// </remarks>
    private static bool ComputeSyndromes(ReadOnlySpan<byte> codeword, int eccCount, Span<byte> syndromes)
    {
#if NET10_0_OR_GREATER
        if (System.Runtime.Intrinsics.X86.Gfni.V256.IsSupported)
        {
            return ComputeSyndromesGfni(codeword, eccCount, syndromes);
        }
#endif
        return ComputeSyndromesScalar(codeword, eccCount, syndromes);
    }

    internal static bool ComputeSyndromesScalar(ReadOnlySpan<byte> codeword, int eccCount, Span<byte> syndromes)
    {
        var exp = GaloisField.Exp;
        var log = GaloisField.Log;

        var hasError = false;
        for (var i = 0; i < eccCount; i++)
        {
            byte value = 0;
            for (var j = 0; j < codeword.Length; j++)
            {
                // value·α^i = exp[log[value] + i]; exp is 512 entries so no % 255
                value = (byte)((value == 0 ? (byte)0 : exp[log[value] + i]) ^ codeword[j]);
            }
            syndromes[i] = value;
            hasError |= value != 0;
        }
        return hasError;
    }
}
