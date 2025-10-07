using SkiaSharp.QrCode.Internals;
using Xunit;

namespace SkiaSharp.QrCode.Tests;

public class BitWriterUnitTests
{
    [Fact]
    public void WriteBits_Singlebyte_CorrectPlacement()
            {
        Span<byte> buffer = stackalloc byte[1];
        var writer = new BitWriter(buffer);

        writer.Write(0b_10101010, 8);

        Assert.Equal(0xAA, buffer[0]);
    }

    [Fact]
    public void WriteBits_AccessByteBoundary_CorrectPlacement()
    {
        Span<byte> buffer = stackalloc byte[2];
        var writer = new BitWriter(buffer);

        writer.Write(0b1111, 4);
        writer.Write(0b0000, 4);
        writer.Write(0b1010, 4);

        Assert.Equal(0xF0, buffer[0]);
        Assert.Equal(0xA0, buffer[1]);
    }

    [Theory]    
    [InlineData(0b_1, 1, new byte[] { 0b_10000000 })]
    [InlineData(0b_11, 2, new byte[] { 0b_11000000 })]
    [InlineData(0b_111, 3, new byte[] { 0b_11100000 })]
    [InlineData(0b_1111, 4, new byte[] { 0b_11110000 })]
    [InlineData(0b_11111, 5, new byte[] { 0b_11111000 })]
    [InlineData(0b_111111, 6, new byte[] { 0b_11111100 })]
    [InlineData(0b_1111111, 7, new byte[] { 0b_11111110 })]
    [InlineData(0b_11111111, 8, new byte[] { 0b_11111111 })]
    public void WriteBits_VariableBitCounts_CorrectAlifnment(int value, int bits, byte[] expected)
    {
        Span<byte> buffer = stackalloc byte[1];
        var writer = new BitWriter(buffer);
        writer.Write(value, bits);

        Assert.Equal(expected[0], buffer[0]);
    }
}
