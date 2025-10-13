using SkiaSharp.QrCode.Internals.BinaryEncoders;
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
    public void WriteBits_VariableBitCounts_CorrectAlignment(int value, int bits, byte[] expected)
    {
        Span<byte> buffer = stackalloc byte[1];
        var writer = new BitWriter(buffer);
        writer.Write(value, bits);

        Assert.Equal(expected[0], buffer[0]);
    }

    // Edge cases

    [Fact]
    public void RoundTrip_MaxBitCount_IdenticalData()
    {
        Span<byte> buffer = stackalloc byte[4];
        var writer = new BitWriter(buffer);

        writer.Write(int.MaxValue, 32); // 0x7FFFFFFF

        var reader = new BitReader(writer.GetData());
        var result = reader.Reads(32);

        Assert.Equal(int.MaxValue, result);
    }

    // multiple writes with different bit counts
    [Fact]
    public void RoundTrip_MultipleBitCounts_IdenticalData()
    {
        Span<byte> buffer = stackalloc byte[10];
        var writer = new BitWriter(buffer);

        writer.Write(0b101, 3);
        writer.Write(0b11111111, 8);
        writer.Write(0b1010101010101010, 16);

        var reader = new BitReader(writer.GetData());

        Assert.Equal(0b101, reader.Reads(3));
        Assert.Equal(0b11111111, reader.Reads(8));
        Assert.Equal(0b1010101010101010, reader.Reads(16));
    }

    // hasBits for last bit
    [Fact]
    public void BitReader_HasBits_CorrectlyIndicatesEndOfData()
    {
        var data = new byte[] { 0xFF };
        var reader = new BitReader(data);

        for (int i = 0; i < 8; i++)
        {
            Assert.True(reader.HasBits);
            reader.Read();
        }

        Assert.False(reader.HasBits);
    }
}
