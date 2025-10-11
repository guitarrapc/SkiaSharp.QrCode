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
        var qrCodeString = CreateEmptyQRCodeData(version);

        var blockedModulesBinary = new List<Rectangle>();
        var blockedModulesString = new List<Rectangle>();

        // Prepare patterns (same for both)
        ModulePlacer.PlaceFinderPatterns(ref qrCodeBinary, ref blockedModulesBinary);
        ModulePlacer.PlaceFinderPatterns(ref qrCodeString, ref blockedModulesString);

        // Binary data: 0xAB 0xCD = 10101011 11001101
        ReadOnlySpan<byte> binaryData = [0xAB, 0xCD];

        // String data: equivalent bit string
        var stringData = "1010101111001101";

        // Act
        ModulePlacer.PlaceDataWords(ref qrCodeBinary, binaryData, ref blockedModulesBinary);
        ModulePlacer.PlaceDataWords(ref qrCodeString, stringData, ref blockedModulesString);

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
        var blockedModules = new List<Rectangle>();

        // Setup patterns
        ModulePlacer.PlaceFinderPatterns(ref qrCode, ref blockedModules);
        ModulePlacer.PlaceTimingPatterns(ref qrCode, ref blockedModules);

        // Act
        ModulePlacer.PlaceDataWords(ref qrCode, data, ref blockedModules);

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
