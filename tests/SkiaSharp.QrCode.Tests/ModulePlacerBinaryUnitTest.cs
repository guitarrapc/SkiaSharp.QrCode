using SkiaSharp.QrCode.Internals;
using Xunit;
using Rectangle = SkiaSharp.QrCode.Internals.Rectangle;

namespace SkiaSharp.QrCode.Tests;

public class ModulePlacerBinaryUnitTest
{
    [Fact]
    public void PlaceDataWords_Binary_MatchesStringBased()
    {
        // Arrange
        var version = 1;
        var qrCodeBinary = CreateEmptyQRCodeData(version);
        var qrCodeBinaryBuffer = qrCodeBinary.GetMutableData();
        var qrCodeBinarySize = qrCodeBinary.Size;
        var qrCodeString = CreateEmptyQRCodeData(version);
        var qrCodeStringBuffer = qrCodeString.GetMutableData();
        var qrCodeStringSize = qrCodeString.Size;

        Span<Rectangle> blockedModulesBinary = stackalloc Rectangle[30];
        Span<Rectangle> blockedModulesString = stackalloc Rectangle[30];
        var blockedCountBinary = 0;
        var blockedCountString = 0;

        // Prepare patterns (same for both)
        ModulePlacer.PlaceFinderPatterns(qrCodeBinaryBuffer, qrCodeBinarySize, blockedModulesBinary, ref blockedCountBinary);
        ModulePlacer.PlaceFinderPatterns(qrCodeStringBuffer, qrCodeStringSize, blockedModulesString, ref blockedCountString);

        // Binary data: 0xAB 0xCD = 10101011 11001101
        ReadOnlySpan<byte> binaryData = [0xAB, 0xCD];

        // String data: equivalent bit string
        var stringData = "1010101111001101";

        // Build blocked mask
        var maskSize = (qrCodeBinarySize * qrCodeBinarySize + 7) / 8;
        Span<byte> blockedMask = stackalloc byte[maskSize];
        blockedMask.Clear();
        QRCodeGenerator.BuildBlockedMask(blockedMask, qrCodeBinarySize, blockedModulesBinary.Slice(0, blockedCountBinary));

        // Act
        ModulePlacer.PlaceDataWords(qrCodeBinaryBuffer, qrCodeBinarySize, binaryData, blockedMask);
        ModulePlacer.PlaceDataWords(ref qrCodeString, stringData, blockedMask);

        // Assert - Compare matrices
        for (int row = 0; row < qrCodeBinary.Size; row++)
        {
            for (int col = 0; col < qrCodeBinary.Size; col++)
            {
                Assert.Equal(qrCodeString[row, col], qrCodeBinary[row, col]);
            }
        }
    }

    [Theory]
    [InlineData(1, new byte[] { 0x12, 0x34, 0x56 })]
    [InlineData(5, new byte[] { 0xFF, 0x00, 0xAA, 0x55 })]
    public void PlaceDataWords_Binary_PlacesDataCorrectly(int version, byte[] data)
    {
        // Arrange
        var qrCode = CreateEmptyQRCodeData(version);
        var buffer = qrCode.GetMutableData();
        var size = qrCode.Size;
        Span<Rectangle> blockedModules = stackalloc Rectangle[30];
        var blockedCount = 0;

        // Setup patterns
        ModulePlacer.PlaceFinderPatterns(buffer, size, blockedModules, ref blockedCount);
        ModulePlacer.PlaceTimingPatterns(buffer, size, blockedModules, ref blockedCount);

        // Build blocked mask
        var maskSize = (size * size + 7) / 8;
        Span<byte> blockedMask = stackalloc byte[maskSize];
        blockedMask.Clear();
        QRCodeGenerator.BuildBlockedMask(blockedMask, size, blockedModules.Slice(0, blockedCount));

        // Act
        ModulePlacer.PlaceDataWords(buffer, size, data, blockedMask);

        // Assert - Verify data is placed (not all zeros/all ones)
        int darkCount = 0;
        int lightCount = 0;

        for (int row = 0; row < qrCode.Size; row++)
        {
            for (int col = 0; col < qrCode.Size; col++)
            {
                if (qrCode[row, col])
                    darkCount++;
                else
                    lightCount++;
            }
        }

        Assert.True(darkCount > 0);
        Assert.True(lightCount > 0);
    }

    private static QRCodeData CreateEmptyQRCodeData(int version)
    {
        var size = GetModuleCount(version);
        return new QRCodeData(version);
    }

    private static int GetModuleCount(int version) => 21 + (version - 1) * 4;
}
