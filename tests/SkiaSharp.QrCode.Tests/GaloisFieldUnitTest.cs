using SkiaSharp.QrCode.Internals;
using Xunit;

namespace SkiaSharp.QrCode.Tests;

public class GaloisFieldUnitTest
{
    // exponent	value (exp[i])  note
    // -----------------------------
    // 0        0x01            a^0 = 1
    // 1        0x02            a^1 = 2
    // 2        0x04            a^2 = 4
    // 3        0x08            a^3 = 8
    // 4        0x10            a^4 = 16
    // 5        0x20            a^5 = 32
    // 6        0x40            a^6 = 64
    // 7        0x80            a^7 = 128
    // 8        0x1D            a^8 = 29 (reduction by irreducible polynomial)
    // 9        0x3A            a^9 = 58
    // ...      ...
    // 219      0xE2            a^219 = 226
    // ...      ...
    // 237      0xFF            a^237 = 255
    // ...      ...
    // 254      0x8D            a^254 = 141
    // 255      0x01            a^255 = 1 (period 255)

    [Fact]
    public void Multiply_KnownValues_CorrectResults()
    {
        Assert.Equal(0x3A, GaloisField.Multiply(0x10, 0x20)); // in galois field, exp[4 + 5] = exp[9] = 58 = 0x3A
        Assert.Equal(0x10, GaloisField.Multiply(0x10, 0x01)); // in galois field, exp[4 + 0] = exp[4] = 16 = 0x10
        Assert.Equal(0x13, GaloisField.Multiply(0x40, 0x1D)); // in galois field, exp[6 + 8] = exp[14] = 19 = 0x13
        Assert.Equal(0xE2, GaloisField.Multiply(0xFF, 0xFF)); // in galois field, exp[237 + 237 mod 255] = exp[219] = 226 = 0xE2
    }

    [Fact]
    public void Divide_KnownValues_CorrectResults()
    {
        Assert.Equal(0x36, GaloisField.Divide(0x8E, 0x20)); // in galois field, a^254 / a^5 = a^(254 - 5) = a^249 = 0x36
        Assert.Equal(0x10, GaloisField.Divide(0x3A, 0x20)); // in galois field, α^9 / α^5 = α^(9-5) = α^4 = exp[4] = 0x10
        Assert.Equal(0x02, GaloisField.Divide(0x1D, 0x80)); // in galois field, α^8 / α^7 = α^(8-7) = α^1 = exp[1] = 0x02
        Assert.Equal(0x01, GaloisField.Divide(0xFF, 0xFF)); // in galois field, a/a = 1 (a^0) = 0x01
    }

    [Fact]
    public void Multiply_WithZero_ReturnsZero_AndDoesNotTouchLogZero()
    {
        // Multiply by 0 should always return 0, and should not attempt to access log(0) which is undefined.
        Assert.Equal(0x00, GaloisField.Multiply(0x00, 0x53));
        Assert.Equal(0x00, GaloisField.Multiply(0x53, 0x00));
    }

    [Fact]
    public void Divide_ByZero_Throws()
    {
        // Division by 0 should throw exception
        Assert.Throws<DivideByZeroException>(() => GaloisField.Divide(0x10, 0x00)); // in galois field, a^i / a^0 = error
    }

    [Fact]
    public void ExpThenLog_AllExponents_AreConsistent()
    {
        // log(exp[i]) = i
        for (int i = 0; i < 255; i++)
        {
            byte v = GaloisField.Exp[i];
            Assert.Equal(i, GaloisField.Log[v]);
        }
    }

    [Fact]
    public void LogThenExp_AllValues_AreConsistent()
    {
        // exp(log[v]) = v, v≠0
        for (int v = 1; v <= 255; v++) // 0 is not in non-zero field
        {
            byte vb = (byte)v;
            int i = GaloisField.Log[vb];
            Assert.Equal(vb, GaloisField.Exp[i]);
        }
    }

    [Fact]
    public void Exp_IsPeriodicWith255()
    {
        // exp[i+255] = exp[i]
        for (int i = 0; i < 255; i++)
        {
            Assert.Equal(GaloisField.Exp[i], GaloisField.Exp[i + 255]);
        }
    }

    [Fact]
    public void Exp_FormsPermutationOfNonZeroElements()
    {
        // all non-zero element should be included exactly once in exp table
        var seen = new HashSet<byte>();
        for (int i = 0; i < 255; i++)
        {
            Assert.NotEqual((byte)0, GaloisField.Exp[i]); // 0 is not in non-zero field
            Assert.True(seen.Add(GaloisField.Exp[i])); // no duplicates
        }
        Assert.Equal(255, seen.Count);
    }
}
