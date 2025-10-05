using Xunit;

namespace SkiaSharp.QrCode.Tests;

public class QRCodeDataUnitTest
{
    [Theory]
    [InlineData(1, 21)]   // Version 1: 21×21 = 441 bits (441 % 8 = 1, padding = 7)
    [InlineData(2, 25)]   // Version 2: 25×25 = 625 bits (625 % 8 = 1, padding = 7)
    [InlineData(3, 29)]   // Version 3: 29×29 = 841 bits (841 % 8 = 1, padding = 7)
    [InlineData(10, 57)]  // Version 10: 57×57 = 3249 bits (3249 % 8 = 1, padding = 7)
    [InlineData(40, 177)] // Version 40: 177×177 = 31329 bits (31329 % 8 = 1, padding = 7)
    public void GetRawData_CorrectPadding_ByVersion(int version, int expectedSize)
    {
        // Create QR code data with specific version
        var qrCode = new QRCodeData(version);

        // Verify size matches expected
        Assert.Equal(expectedSize, qrCode.Size);

        // Get raw data
        var rawData = qrCode.GetRawData(QRCodeData.Compression.Uncompressed);

        // Verify byte alignment
        // Expected bytes = ceil((size * size) / 8) + header (4 bytes)
        var totalBits = expectedSize * expectedSize;
        var dataBytesCount = (totalBits + 7) / 8;  // Round up to nearest byte
        var expectedBytes = dataBytesCount + 4;     // +4 for header

        Assert.Equal(expectedBytes, rawData.Length);
    }

    [Theory]
    [InlineData(1, 0, 21)]   // Version 1, no quiet zone: 21×21
    [InlineData(1, 4, 29)]   // Version 1, quiet zone 4: 29×29
    [InlineData(2, 0, 25)]   // Version 2, no quiet zone: 25×25
    [InlineData(2, 4, 33)]   // Version 2, quiet zone 4: 33×33
    public void GetRawData_CorrectPadding_WithQuietZone(int version, int quietZone, int expectedSize)
    {
        // Create QR code
        var qrCode = new QRCodeData(version);

        // Add quiet zone if needed
        if (quietZone > 0)
        {
            var oldSize = qrCode.Size;
            var newSize = oldSize + quietZone * 2;
            var newMatrix = new bool[newSize, newSize];

            // Copy data to center
            for (int row = 0; row < oldSize; row++)
            {
                for (int col = 0; col < oldSize; col++)
                {
                    newMatrix[row + quietZone, col + quietZone] = qrCode[row, col];
                }
            }

            qrCode.SetModuleMatrix(newMatrix, quietZone);
        }

        // Verify size
        Assert.Equal(expectedSize, qrCode.Size);

        // Get raw data
        var rawData = qrCode.GetRawData(QRCodeData.Compression.Uncompressed);

        // Verify byte alignment
        var totalBits = expectedSize * expectedSize;
        var expectedBytes = (totalBits + 7) / 8 + 4;

        Assert.Equal(expectedBytes, rawData.Length);
    }

    /// <summary>
    /// Tests padding formula in isolation - verifies mathematical correctness.
    /// </summary>
    [Theory]
    [InlineData(0, 0)]    // Already aligned
    [InlineData(1, 7)]    // 1 bit → need 7 bits padding
    [InlineData(7, 1)]    // 7 bits → need 1 bit padding
    [InlineData(256, 0)]  // 256 bits (32 bytes) → no padding → Edge case
    [InlineData(257, 7)]  // 257 bits → 7 bits padding
    [InlineData(441, 7)]  // Version 1 QR (21×21)
    public void PaddingFormula_CalculatesCorrectBoundary(int totalBits, int expectedPadding)
    {
        var remainder = totalBits % 8;

        // Correct formula
        var boundary = (8 - remainder) % 8;

        Assert.Equal(expectedPadding, boundary);

        // Verify old formula fails for edge cases
        if (remainder == 0)
        {
            var oldBoundary = 8 - remainder;  // = 8 (wrong!)
            Assert.Equal(8, oldBoundary);
            Assert.NotEqual(boundary, oldBoundary);
        }
    }


    /// <summary>
    /// Round-trip test with VALID QR sizes only
    /// </summary>
    [Theory]
    [InlineData(21)]   // Version 1
    [InlineData(25)]   // Version 2
    [InlineData(29)]   // Version 3
    [InlineData(57)]   // Version 10
    public void Serialization_RoundTrip_PreservesData(int matrixSize)
    {
        var original = new QRCodeData(1);
        var customMatrix = new bool[matrixSize, matrixSize];

        for (int row = 0; row < matrixSize; row++)
        {
            for (int col = 0; col < matrixSize; col++)
            {
                customMatrix[row, col] = (row * matrixSize + col) % 7 == 0;
            }
        }

        original.SetModuleMatrix(customMatrix, quietZoneSize: 0);

        var rawData = original.GetRawData(QRCodeData.Compression.Uncompressed);
        var restored = new QRCodeData(rawData, QRCodeData.Compression.Uncompressed);

        Assert.Equal(matrixSize, restored.Size);

        for (int row = 0; row < matrixSize; row++)
        {
            for (int col = 0; col < matrixSize; col++)
            {
                var expected = (row * matrixSize + col) % 7 == 0;
                Assert.Equal(expected, restored[row, col]);
            }
        }
    }

    /// <summary>
    /// Verifies Version calculation is correct when using custom matrix sizes.
    /// </summary>
    [Fact]
    public void SetModuleMatrix_UpdatesVersion_Correctly()
    {
        var qrCode = new QRCodeData(1);

        // Test Version 5 size (37×37)
        var matrix = new bool[37, 37];
        qrCode.SetModuleMatrix(matrix, quietZoneSize: 0);

        Assert.Equal(5, qrCode.Version);
        Assert.Equal(37, qrCode.Size);
    }
}
