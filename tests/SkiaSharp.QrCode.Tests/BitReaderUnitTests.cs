using SkiaSharp.QrCode.Internals.BinaryEncoders;
using Xunit;

namespace SkiaSharp.QrCode.Tests;

public class BitReaderUnitTests
{
    [Fact]
    public void ReadBits_Singlebyte_CorrectPlacement()
    {
        Span<byte> data = [0b_10101011];
        var reader = new BitReader(data);

        Assert.True(reader.Read());   // 1
        Assert.False(reader.Read());  // 0
        Assert.True(reader.Read());   // 1
        Assert.False(reader.Read());  // 0
        Assert.True(reader.Read());   // 1
        Assert.False(reader.Read());  // 0
        Assert.True(reader.Read());   // 1
        Assert.True(reader.Read());   // 1
    }

    [Fact]
    public void ReadBits_AccessByteBoundary_CorrectPlacement()
    {
        Span<byte> data = [0b11110010];
        var reader = new BitReader(data);

        Assert.Equal(0b_1111, reader.Reads(4));
        Assert.Equal(0b_0010,reader.Reads(4));

    }

    [Fact]
    public void RoundTrip_WriteAndRead_IdenticalData()
    {
        Span<byte> buffer = stackalloc byte[10];
        var writer = new BitWriter(buffer);

        writer.Write(0b_1010, 4);
        writer.Write(0b_1100, 4);
        writer.Write(0b_1111, 4);

        var reader = new BitReader(writer.GetData());

        Assert.Equal(0b_1010, reader.Reads(4));
        Assert.Equal(0b_1100, reader.Reads(4));
        Assert.Equal(0b_1111, reader.Reads(4));
    }

    // parameter check
    [Theory]
    [InlineData(0)]    // bitCount = 0
    [InlineData(-1)]   // bitCount < 0
    [InlineData(33)]   // bitCount > 32
    public void Reads_InvalidBitCount_ThrowsArgumentOutOfRangeException(int bitCount)
    {
        var data = new byte[] { 0xFF };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            var reader = new BitReader(data);
            reader.Reads(bitCount);
        });
        Assert.Equal(nameof(bitCount), exception.ParamName);
    }

    // 32-bit checks
    [Fact]
    public void Reads_32Bits_ReturnsCorrectValue()
    {
        var data = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }; // all 1s
        var reader = new BitReader(data);

        var result = reader.Reads(32);

        Assert.Equal(-1, result); // 0xFFFFFFFF as signed int
    }

    // hasBits
    [Fact]
    public void Reads_1Bit_ReturnsCorrectValue()
    {
        var data = new byte[] { 0b10000000 };
        var reader = new BitReader(data);

        var result = reader.Reads(1);

        Assert.Equal(1, result);
    }
}
