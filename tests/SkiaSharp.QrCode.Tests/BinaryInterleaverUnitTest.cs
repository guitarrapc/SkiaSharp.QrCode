using SkiaSharp.QrCode.Internals.BinaryEncoders;
using Xunit;
using static SkiaSharp.QrCode.Internals.QRCodeConstants;

namespace SkiaSharp.QrCode.Tests;
public class BinaryInterleaverUnitTest
{
    // Basic Interleaving

    [Fact]
    internal void InterleaveCodewords_TwoBlocks_CorrectOrder()
    {
        var version = 1;
        // Arrange
        var eccInfo = new ECCInfo(version, ECCLevel.M, 6, 2, 2, 3, 0, 0);

        // data: Block1 (D1,D2,D3) + Block2 (D4,D5,D6)
        ReadOnlySpan<byte> allData = [0xD1, 0xD2, 0xD3, 0xD4, 0xD5, 0xD6];

        // ecc: Block1 (E1,E2) + Block2 (E3,E4)
        ReadOnlySpan<byte> allEcc = [0xE1, 0xE2, 0xE3, 0xE4];

        var size = BinaryInterleaver.CalculateInterleavedSize(eccInfo, version);
        Span<byte> output = stackalloc byte[size];

        // Act
        BinaryInterleaver.InterleaveCodewords(allData, allEcc, output, version, eccInfo);

        // Assert
        // Expected: D1, D4, D2, D5, D3, D6 | E1, E3, E2, E4
        Assert.Equal(0xD1, output[0]); // Block 0, byte 0
        Assert.Equal(0xD4, output[1]); // Block 1, byte 0
        Assert.Equal(0xD2, output[2]); // Block 0, byte 1
        Assert.Equal(0xD5, output[3]); // Block 1, byte 1
        Assert.Equal(0xD3, output[4]); // Block 0, byte 2
        Assert.Equal(0xD6, output[5]); // Block 1, byte 2
        Assert.Equal(0xE1, output[6]); // ECC Block 0, byte 0
        Assert.Equal(0xE3, output[7]); // ECC Block 1, byte 0
        Assert.Equal(0xE2, output[8]); // ECC Block 0, byte 1
        Assert.Equal(0xE4, output[9]); // ECC Block 1, byte 1
    }

    [Fact]
    public void InterleaveCodewords_UnequalBlockSizes_CorrectPadding()
    {
        var version = 5;
        // Arrange - Group 1 (3 bytes), Group 2 (4 bytes)
        var eccInfo = new ECCInfo(version, ECCLevel.H, 7, 2, 1, 3, 1, 4);

        // data: Group1 Block0 (3 bytes) + Group2 Block0 (4 bytes)
        ReadOnlySpan<byte> allData = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07];

        // ECC: Block0 (2 bytes) + Block1 (2 bytes)
        ReadOnlySpan<byte> allEcc = [0xE1, 0xE2, 0xE3, 0xE4];

        var size = BinaryInterleaver.CalculateInterleavedSize(eccInfo, version);
        Span<byte> output = stackalloc byte[size];

        // Act
        BinaryInterleaver.InterleaveCodewords(allData, allEcc, output, version, eccInfo);

        // Assert
        // Expected: 01, 04, 02, 05, 03, 06, 07 | E1, E3, E2, E4
        Assert.Equal(0x01, output[0]); //block 1
        Assert.Equal(0x04, output[1]);
        Assert.Equal(0x02, output[2]);
        Assert.Equal(0x05, output[3]);
        Assert.Equal(0x03, output[4]);
        Assert.Equal(0x06, output[5]);
        Assert.Equal(0x07, output[6]);
        Assert.Equal(0xE1, output[7]); //block 2
        Assert.Equal(0xE3, output[8]);
        Assert.Equal(0xE2, output[9]);
        Assert.Equal(0xE4, output[10]);
    }

    [Fact]
    public void InterleaveCodewords_SingleBlock_NoInterleaving()
    {
        var version = 1;
        // Arrange: 1 block（no interleaves）
        var eccInfo = GetEccInfo(version, ECCLevel.L);

        Span<byte> allData = stackalloc byte[19];
        for (int i = 0; i < 19; i++)
        {
            allData[i] = (byte)(i + 1);
        }

        Span<byte> allEcc = stackalloc byte[7];
        for (int i = 0; i < 7; i++)
        {
            allEcc[i] = (byte)(0xE0 + i);
        }

        var size = BinaryInterleaver.CalculateInterleavedSize(eccInfo, version);
        Span<byte> output = stackalloc byte[size];

        // Act
        BinaryInterleaver.InterleaveCodewords(allData, allEcc, output, version, eccInfo);

        // Assert - No interleaving, data and ECC remain in original order
        for (int i = 0; i < 19; i++)
        {
            Assert.Equal(allData[i], output[i]);
        }
        for (int i = 0; i < 7; i++)
        {
            Assert.Equal(allEcc[i], output[19 + i]);
        }
    }

    [Fact]
    public void InterleaveCodewords_MaximumBlocks_Version40()
    {
        var version = 40;
        // Arrange: Version 40 (maximum 30 blocks)
        var eccInfo = GetEccInfo(version, ECCLevel.H);

        Span<byte> allData = stackalloc byte[eccInfo.TotalDataCodewords];
        Span<byte> allEcc = stackalloc byte[(eccInfo.BlocksInGroup1 + eccInfo.BlocksInGroup2) * eccInfo.ECCPerBlock];

        // Generate data
        for (int i = 0; i < allData.Length; i++)
        {
            allData[i] = (byte)((i + 1) % 256); // 1,2,3,...255,0,1,2...
        }
        for (int i = 0; i < allEcc.Length; i++)
        {
            allEcc[i] = (byte)((i + 100) % 256);
        }

        var size = BinaryInterleaver.CalculateInterleavedSize(eccInfo, version);
        Span<byte> output = stackalloc byte[size];

        // Act
        BinaryInterleaver.InterleaveCodewords(allData, allEcc, output, version, eccInfo);

        // Assert
        Assert.Equal(size, output.Length);
        Assert.NotEqual(0, output[0]);
    }

    [Theory]
    [InlineData(1, ECCLevel.L)]
    [InlineData(10, ECCLevel.M)]
    [InlineData(20, ECCLevel.Q)]
    [InlineData(40, ECCLevel.H)]
    public void CalculateInterleavedSize_AllVersions_CorrectSize(int version, ECCLevel level)
    {
        // Arrange
        var eccInfo = GetEccInfo(version, level);

        // Act
        var size = BinaryInterleaver.CalculateInterleavedSize(eccInfo, version);

        // Assert
        int expectedDataBytes = eccInfo.TotalDataCodewords;
        int expectedEccBytes = (eccInfo.BlocksInGroup1 + eccInfo.BlocksInGroup2) * eccInfo.ECCPerBlock;
        int expectedSize = expectedDataBytes + expectedEccBytes;

        Assert.True(size >= expectedSize); // concider Remainder bits
        Assert.True(size <= expectedSize + 1); // maximum added 1 byte
    }

    // Comparison with Text-based Interleaver

    [Theory]
    [InlineData(1, ECCLevel.M)]
    [InlineData(5, ECCLevel.H)]
    [InlineData(10, ECCLevel.Q)]
    public void InterleaveCodewords_MatchesTextBasedInterleaver(int version, ECCLevel eccLevel)
    {
        // This test will be implemented after integrating with QRCodeGenerator
        // It will compare byte-based interleaving with existing string-based interleaving
    }
}
