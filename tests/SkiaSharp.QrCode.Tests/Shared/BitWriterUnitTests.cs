using SkiaSharp.QrCode.Internals.BinaryEncoders;

namespace SkiaSharp.QrCode.Tests;

public class BitWriterUnitTests
{
    [Test]
    public async Task WriteBits_Singlebyte_CorrectPlacement()
    {
        byte[] buffer = new byte[1];
        var writer = new BitWriter(buffer);

        writer.Write(0b_10101010, 8);
        var data = writer.GetData();

        await Assert.That(data[0]).IsEqualTo((byte)0xAA);
    }

    [Test]
    public async Task WriteBits_AccessByteBoundary_CorrectPlacement()
    {
        byte[] buffer = new byte[2];
        var writer = new BitWriter(buffer);

        writer.Write(0b1111, 4);
        writer.Write(0b0000, 4);
        writer.Write(0b1010, 4);
        var data = writer.GetData().ToArray();

        await Assert.That(data[0]).IsEqualTo((byte)0xF0);
        await Assert.That(data[1]).IsEqualTo((byte)0xA0);
    }

    [Test]
    [Arguments(0b_1, 1, new byte[] { 0b_10000000 })]
    [Arguments(0b_11, 2, new byte[] { 0b_11000000 })]
    [Arguments(0b_111, 3, new byte[] { 0b_11100000 })]
    [Arguments(0b_1111, 4, new byte[] { 0b_11110000 })]
    [Arguments(0b_11111, 5, new byte[] { 0b_11111000 })]
    [Arguments(0b_111111, 6, new byte[] { 0b_11111100 })]
    [Arguments(0b_1111111, 7, new byte[] { 0b_11111110 })]
    [Arguments(0b_11111111, 8, new byte[] { 0b_11111111 })]
    public async Task WriteBits_VariableBitCounts_CorrectAlignment(int value, int bits, byte[] expected)
    {
        byte[] buffer = new byte[1];
        var writer = new BitWriter(buffer);
        writer.Write(value, bits);
        var data = writer.GetData();

        await Assert.That(data[0]).IsEqualTo(expected[0]);
    }

    // Edge cases

    [Test]
    public async Task RoundTrip_MaxBitCount_IdenticalData()
    {
        byte[] buffer = new byte[4];
        var writer = new BitWriter(buffer);

        writer.Write(int.MaxValue, 32); // 0x7FFFFFFF

        var reader = new BitReader(writer.GetData());
        var result = reader.Reads(32);

        await Assert.That(result).IsEqualTo(int.MaxValue);
    }

    // multiple writes with different bit counts
    [Test]
    public async Task RoundTrip_MultipleBitCounts_IdenticalData()
    {
        byte[] buffer = new byte[10];
        var writer = new BitWriter(buffer);

        writer.Write(0b101, 3);
        writer.Write(0b11111111, 8);
        writer.Write(0b1010101010101010, 16);

        var reader = new BitReader(writer.GetData());

        var read3 = reader.Reads(3);
        var read8 = reader.Reads(8);
        var read16 = reader.Reads(16);

        await Assert.That(read3).IsEqualTo(0b101);
        await Assert.That(read8).IsEqualTo(0b11111111);
        await Assert.That(read16).IsEqualTo(0b1010101010101010);
    }

    // hasBits for last bit
    [Test]
    public async Task BitReader_HasBits_CorrectlyIndicatesEndOfData()
    {
        var data = new byte[] { 0xFF };
        var reader = new BitReader(data);
        var hasBitsWhileReading = new bool[8];
        for (var i = 0; i < 8; i++)
        {
            hasBitsWhileReading[i] = reader.HasBits;
            reader.Read();
        }

        var hasBitsAfterReads = reader.HasBits;

        foreach (var hasBits in hasBitsWhileReading)
        {
            await Assert.That(hasBits).IsTrue();
        }

        await Assert.That(hasBitsAfterReads).IsFalse();
    }
}
