using SkiaSharp.QrCode.Internals;
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
}
