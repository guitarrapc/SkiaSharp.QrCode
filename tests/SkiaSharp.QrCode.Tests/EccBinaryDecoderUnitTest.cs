using SkiaSharp.QrCode.Internals.BinaryDecoders;
using SkiaSharp.QrCode.Internals.BinaryEncoders;
using Xunit;

namespace SkiaSharp.QrCode.Tests;

public class EccBinaryDecoderUnitTest
{
    [Fact]
    public void TryCorrect_CleanBlock_ReportsNoErrors()
    {
        // Arrange - ISO/IEC 18004 Annex I example
        var codeword = BuildCodeword([64, 86, 134, 86], 10);

        // Act
        var result = EccBinaryDecoder.TryCorrect(codeword, 10, out var errorsCorrected);

        // Assert
        Assert.True(result);
        Assert.Equal(0, errorsCorrected);
        Assert.Equal(new byte[] { 64, 86, 134, 86 }, codeword.AsSpan(0, 4).ToArray());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void TryCorrect_ErrorsWithinCapacity_Corrects(int errorCount)
    {
        // Arrange - 10 ECC codewords correct up to 5 errors
        byte[] data = [64, 86, 134, 86, 242, 7, 118, 134];
        var codeword = BuildCodeword(data, 10);
        var expected = (byte[])codeword.Clone();

        // Corrupt distinct positions deterministically
        var random = new Random(42);
        CorruptDistinctPositions(codeword, errorCount, random);

        // Act
        var result = EccBinaryDecoder.TryCorrect(codeword, 10, out var errorsCorrected);

        // Assert
        Assert.True(result);
        Assert.Equal(errorCount, errorsCorrected);
        Assert.Equal(expected, codeword);
    }

    [Fact]
    public void TryCorrect_ErrorsBeyondCapacity_Fails()
    {
        // Arrange - 10 ECC codewords, capacity is 5; inject 6 errors
        byte[] data = [64, 86, 134, 86, 242, 7, 118, 134];
        var codeword = BuildCodeword(data, 10);

        var random = new Random(42);
        CorruptDistinctPositions(codeword, 6, random);

        // Act
        var result = EccBinaryDecoder.TryCorrect(codeword, 10, out _);

        // Assert - must fail, never silently miscorrect to the original
        Assert.False(result);
    }

    [Theory]
    [InlineData(7)]   // ECC level L (version 1)
    [InlineData(16)]  // M
    [InlineData(22)]  // Q
    [InlineData(30)]  // H (max per QR spec)
    public void TryCorrect_EveryQrEccCount_RoundTrips(int eccCount)
    {
        // Arrange
        var data = new byte[20];
        var random = new Random(1234);
        random.NextBytes(data);
        var codeword = BuildCodeword(data, eccCount);
        var expected = (byte[])codeword.Clone();

        var capacity = eccCount / 2;
        CorruptDistinctPositions(codeword, capacity, random);

        // Act
        var result = EccBinaryDecoder.TryCorrect(codeword, eccCount, out var errorsCorrected);

        // Assert
        Assert.True(result);
        Assert.Equal(capacity, errorsCorrected);
        Assert.Equal(expected, codeword);
    }

    [Fact]
    public void TryCorrect_RandomizedManyRounds_AlwaysRecoversWithinCapacity()
    {
        // Arrange - fixed seed for reproducibility
        var random = new Random(20260711);

        for (var round = 0; round < 500; round++)
        {
            var dataLength = random.Next(4, 100);
            var eccCount = random.Next(2, 31);
            var data = new byte[dataLength];
            random.NextBytes(data);

            var codeword = BuildCodeword(data, eccCount);
            var expected = (byte[])codeword.Clone();

            var errors = random.Next(0, eccCount / 2 + 1);
            CorruptDistinctPositions(codeword, errors, random);

            // Act
            var result = EccBinaryDecoder.TryCorrect(codeword, eccCount, out var errorsCorrected);

            // Assert
            Assert.True(result, $"round {round}: dataLength={dataLength}, eccCount={eccCount}, errors={errors}");
            Assert.Equal(errors, errorsCorrected);
            Assert.Equal(expected, codeword);
        }
    }

    [Fact]
    public void TryCorrect_InvalidArguments_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EccBinaryDecoder.TryCorrect(new byte[10], 0, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => EccBinaryDecoder.TryCorrect(new byte[40], 31, out _));
        Assert.Throws<ArgumentException>(() => EccBinaryDecoder.TryCorrect(new byte[10], 10, out _)); // no data codewords
    }

    private static byte[] BuildCodeword(byte[] data, int eccCount)
    {
        var codeword = new byte[data.Length + eccCount];
        data.CopyTo(codeword, 0);
        EccBinaryEncoder.CalculateECC(data, codeword.AsSpan(data.Length), eccCount);
        return codeword;
    }

    private static void CorruptDistinctPositions(byte[] codeword, int errorCount, Random random)
    {
        var positions = new HashSet<int>();
        while (positions.Count < errorCount)
        {
            positions.Add(random.Next(codeword.Length));
        }
        foreach (var position in positions)
        {
            byte garbage;
            do
            {
                garbage = (byte)random.Next(256);
            } while (garbage == codeword[position]);
            codeword[position] = garbage;
        }
    }
}
