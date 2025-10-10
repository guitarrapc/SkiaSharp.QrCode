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
        // Arrange
        var eccInfo = new ECCInfo(1, ECCLevel.M, 6, 2, 2, 3, 0, 0);

        // Block 1: D1, D2, D3 | E1, E2
        var block1Data = new byte[] { 0xD1, 0xD2, 0xD3 };
        var block1Ecc = new byte[] { 0xE1, 0xE2 };

        // Block 2: D4, D5, D6 | E3, E4
        var block2Data = new byte[] { 0xD4, 0xD5, 0xD6 };
        var block2Ecc = new byte[] { 0xE3, 0xE4 };

        Span<CodewordBinaryBlock> blocks = stackalloc CodewordBinaryBlock[2];
        blocks[0] = new CodewordBinaryBlock(1, 0, block1Data, block1Ecc);
        blocks[1] = new CodewordBinaryBlock(1, 1, block2Data, block2Ecc);

        var size = BinaryInterleaver.CalculateInterleavedSize(eccInfo, 1);
        Span<byte> output = stackalloc byte[size];

        // Act
        BinaryInterleaver.InterleaveCodewords(blocks, output, 1, eccInfo);

        // Assert
        // Expected: D1, D4, D2, D5, D3, D6 | E1, E3, E2, E4
        Assert.Equal(0xD1, output[0]);
        Assert.Equal(0xD4, output[1]);
        Assert.Equal(0xD2, output[2]);
        Assert.Equal(0xD5, output[3]);
        Assert.Equal(0xD3, output[4]);
        Assert.Equal(0xD6, output[5]);
        Assert.Equal(0xE1, output[6]);
        Assert.Equal(0xE3, output[7]);
        Assert.Equal(0xE2, output[8]);
        Assert.Equal(0xE4, output[9]);
    }

    [Fact]
    public void InterleaveCodewords_UnequalBlockSizes_CorrectPadding()
    {
        // Arrange - Group 1 (3 bytes), Group 2 (4 bytes)
        var eccInfo = new ECCInfo(5, ECCLevel.H, 7, 2, 1, 3, 1, 4);
    
        var block1Data = new byte[] { 0x01, 0x02, 0x03 };
        var block1Ecc = new byte[] { 0xE1, 0xE2 };

        var block2Data = new byte[] { 0x04, 0x05, 0x06, 0x07 };
        var block2Ecc = new byte[] { 0xE3, 0xE4 };

        Span<CodewordBinaryBlock> blocks = stackalloc CodewordBinaryBlock[2];
        blocks[0] = new CodewordBinaryBlock(1, 0, block1Data, block1Ecc);
        blocks[1] = new CodewordBinaryBlock(2, 0, block2Data, block2Ecc);

        var size = BinaryInterleaver.CalculateInterleavedSize(eccInfo, 5);
        Span<byte> output = stackalloc byte[size];

        // Act
        BinaryInterleaver.InterleaveCodewords(blocks, output, eccInfo, 5);

        // Assert
        // Expected: 01, 04, 02, 05, 03, 06, 07 | E1, E3, E2, E4
        Assert.Equal(0x01, output[0]);
        Assert.Equal(0x04, output[1]);
        Assert.Equal(0x02, output[2]);
        Assert.Equal(0x05, output[3]);
        Assert.Equal(0x03, output[4]);
        Assert.Equal(0x06, output[5]);
        Assert.Equal(0x07, output[6]); // Only from block 2
        Assert.Equal(0xE1, output[7]);
        Assert.Equal(0xE3, output[8]);
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
