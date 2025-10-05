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
        var rawData = qrCode.GetRawData(Compression.Uncompressed);

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
        var rawData = qrCode.GetRawData(Compression.Uncompressed);

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
    /// <remarks>
    /// if matrixSize is 21 (Version 1), then the pattern is:
    /// ---------------------------------------------
    /// (row, col) → 1D index → pattern
    /// ---------------------------------------------
    /// (0, 0) → 0 * 21 + 0 = 0   → 0 % 7 = 0 → true  ✅
    /// (0, 1) → 0 * 21 + 1 = 1   → 1 % 7 = 1 → false
    /// (0, 2) → 0 * 21 + 2 = 2   → 2 % 7 = 2 → false
    /// ...
    /// (0, 6) → 0 * 21 + 6 = 6   → 6 % 7 = 6 → false
    /// (0, 7) → 0 * 21 + 7 = 7   → 7 % 7 = 0 → true  ✅
    /// (0, 14) → 0 * 21 + 14 = 14 → 14 % 7 = 0 → true ✅
    /// (1, 6) → 1 * 21 + 6 = 27  → 27 % 7 = 6 → false
    /// (1, 7) → 1 * 21 + 7 = 28  → 28 % 7 = 0 → true  ✅
    ///
    /// ---------------------------------------------
    /// Visualize
    /// ---------------------------------------------
    /// ■ □ □ □ □ □ □ ■ □ □ □ □ □ □ ■ □ □ □ □ □ □
    /// □ ■ □ □ □ □ □ □ ■ □ □ □ □ □ □ ■ □ □ □ □ □
    /// □ □ ■ □ □ □ □ □ □ ■ □ □ □ □ □ □ ■ □ □ □ □
    /// □ □ □ ■ □ □ □ □ □ □ ■ □ □ □ □ □ □ ■ □ □ □
    /// ...
    /// </remarks>
    [Theory]
    [InlineData(21)]   // Version 1
    [InlineData(25)]   // Version 2
    [InlineData(29)]   // Version 3
    [InlineData(57)]   // Version 10
    public void Serialization_RoundTrip_PreservesData(int matrixSize)
    {
        // Use (row * matrixSize + col) % 7 to create a pattern that is not too dense.
        // 7 is not 8's multiple, it means it won't dup with byte border.
        // see remarks.
        var pattern = 7;

        var original = new QRCodeData(1);
        var customMatrix = new bool[matrixSize, matrixSize];

        for (int row = 0; row < matrixSize; row++)
        {
            for (int col = 0; col < matrixSize; col++)
            {
                customMatrix[row, col] = (row * matrixSize + col) % pattern == 0;
            }
        }

        original.SetModuleMatrix(customMatrix, quietZoneSize: 0);

        var rawData = original.GetRawData(Compression.Uncompressed);
        var restored = new QRCodeData(rawData, Compression.Uncompressed);

        Assert.Equal(matrixSize, restored.Size);

        for (int row = 0; row < matrixSize; row++)
        {
            for (int col = 0; col < matrixSize; col++)
            {
                var expected = (row * matrixSize + col) % pattern == 0;
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

    /// <summary>
    /// Tests serialization/deserialization with all compression modes
    /// </summary>
    /// <remarks>
    /// if matrixSize is 37 (Version 5), then the pattern is:
    /// ---------------------------------------------
    /// (row, col) → sum → pattern
    /// ---------------------------------------------
    /// (0, 0) → 0 + 0 = 0 → 0 % 3 = 0 → true  ✅
    /// (0, 1) → 0 + 1 = 1 → 1 % 3 = 1 → false
    /// (0, 2) → 0 + 2 = 2 → 2 % 3 = 2 → false
    /// (0, 3) → 0 + 3 = 3 → 3 % 3 = 0 → true  ✅
    /// (1, 0) → 1 + 0 = 1 → 1 % 3 = 1 → false
    /// (1, 1) → 1 + 1 = 2 → 2 % 3 = 2 → false
    /// (1, 2) → 1 + 2 = 3 → 3 % 3 = 0 → true  ✅
    /// (2, 1) → 2 + 1 = 3 → 3 % 3 = 0 → true  ✅
    /// 
    /// ---------------------------------------------
    /// Visualize
    /// ---------------------------------------------
    /// ■ □ □ ■ □ □ ■ □ □
    /// □ □ ■ □ □ ■ □ □ ■
    /// □ ■ □ □ ■ □ □ ■ □
    /// ■ □ □ ■ □ □ ■ □ □
    /// □ □ ■ □ □ ■ □ □ ■
    /// □ ■ □ □ ■ □ □ ■ □
    /// ■ □ □ ■ □ □ ■ □ □
    /// □ □ ■ □ □ ■ □ □ ■
    /// □ ■ □ □ ■ □ □ ■ □
    /// </remarks>
    [Theory]
    [InlineData(Compression.Uncompressed)]
    [InlineData(Compression.Deflate)]
    [InlineData(Compression.GZip)]
    public void Serialization_RoundTrip_AllCompressionModes(Compression compression)
    {
        // Use (row + col) % 3 to create a pattern that is not too dense.
        // 3 is not 8's multiple, it means it won't dup with byte border.
        // see remarks.
        var pattern = 3;

        // Arrange
        var original = new QRCodeData(5);  // Version 5: 37×37
        var size = original.Size;

        // Set recognizable pattern
        for (int row = 0; row < size; row++)
        {
            for (int col = 0; col < size; col++)
            {
                original[row, col] = (row + col) % pattern == 0;
            }
        }

        // Act
        var rawData = original.GetRawData(compression);
        var restored = new QRCodeData(rawData, compression);

        // Assert
        Assert.Equal(original.Version, restored.Version);
        Assert.Equal(original.Size, restored.Size);

        for (int row = 0; row < size; row++)
        {
            for (int col = 0; col < size; col++)
            {
                Assert.Equal(original[row, col], restored[row, col]);
            }
        }
    }

    /// <summary>
    /// Verifies that compression actually reduces data size
    /// </summary>
    [Theory]
    [InlineData(1)]   // Version 1: small QR code
    [InlineData(10)]  // Version 10: medium QR code
    [InlineData(20)]  // Version 20: large QR code
    public void Compression_ReducesDataSize(int version)
    {
        // Arrange
        var qrCode = new QRCodeData(version);
        var size = qrCode.Size;

        // Set pattern with repetition (compressible)
        for (int row = 0; row < size; row++)
        {
            for (int col = 0; col < size; col++)
            {
                qrCode[row, col] = (row / 4 + col / 4) % 2 == 0;
            }
        }

        // Act
        var uncompressed = qrCode.GetRawData(Compression.Uncompressed);
        var deflate = qrCode.GetRawData(Compression.Deflate);
        var gzip = qrCode.GetRawData(Compression.GZip);

        // Assert
        // Compressed data should be smaller than uncompressed (for repetitive patterns)
        Assert.True(deflate.Length < uncompressed.Length,
            $"Deflate ({deflate.Length} bytes) should be smaller than uncompressed ({uncompressed.Length} bytes)");
        Assert.True(gzip.Length < uncompressed.Length,
            $"GZip ({gzip.Length} bytes) should be smaller than uncompressed ({uncompressed.Length} bytes)");
    }

    /// <summary>
    /// Verifies that compression works with random (non-compressible) data
    /// </summary>
    [Fact]
    public void Compression_WorksWithRandomData()
    {
        // Arrange
        var qrCode = new QRCodeData(5);
        var size = qrCode.Size;
        var random = new Random(42);  // Fixed seed for reproducibility

        // Set random pattern (not compressible)
        for (int row = 0; row < size; row++)
        {
            for (int col = 0; col < size; col++)
            {
                qrCode[row, col] = random.Next(2) == 1;
            }
        }

        // Act & Assert - should not throw
        var uncompressed = qrCode.GetRawData(Compression.Uncompressed);
        var deflate = qrCode.GetRawData(Compression.Deflate);
        var gzip = qrCode.GetRawData(Compression.GZip);

        // All should deserialize correctly
        var restored1 = new QRCodeData(uncompressed, Compression.Uncompressed);
        var restored2 = new QRCodeData(deflate, Compression.Deflate);
        var restored3 = new QRCodeData(gzip, Compression.GZip);

        Assert.Equal(size, restored1.Size);
        Assert.Equal(size, restored2.Size);
        Assert.Equal(size, restored3.Size);
    }

    /// <summary>
    /// Tests that wrong compression mode throws exception
    /// </summary>
    [Fact]
    public void Deserialization_WrongCompressionMode_ThrowsException()
    {
        // Arrange
        var qrCode = new QRCodeData(1);
        var compressedData = qrCode.GetRawData(Compression.Deflate);

        // Act & Assert - trying to decompress with wrong mode should fail
        Assert.ThrowsAny<Exception>(() =>
            new QRCodeData(compressedData, Compression.Uncompressed));
    }

    /// <summary>
    /// Tests that corrupted compressed data throws exception
    /// </summary>
    [Theory]
    [InlineData(Compression.Deflate)]
    [InlineData(Compression.GZip)]
    public void Deserialization_CorruptedData_ThrowsException(Compression compression)
    {
        // Arrange
        var qrCode = new QRCodeData(1);
        var compressedData = qrCode.GetRawData(compression);

        // Corrupt the data (change middle bytes)
        var corrupted = new byte[compressedData.Length];
        Array.Copy(compressedData, corrupted, compressedData.Length);
        corrupted[compressedData.Length / 2] ^= 0xFF;  // Flip bits in the middle

        // Act & Assert
        Assert.ThrowsAny<Exception>(() => new QRCodeData(corrupted, compression));
    }

    /// <summary>
    /// Verifies compression with different QR code sizes
    /// </summary>
    [Theory]
    [InlineData(1, Compression.Deflate)]
    [InlineData(1, Compression.GZip)]
    [InlineData(10, Compression.Deflate)]
    [InlineData(10, Compression.GZip)]
    [InlineData(40, Compression.Deflate)]
    [InlineData(40, Compression.GZip)]
    public void Compression_VariousSizesAndModes(int version, Compression compression)
    {
        // Arrange
        var original = new QRCodeData(version);
        var size = original.Size;

        // Set checkerboard pattern
        for (int row = 0; row < size; row++)
        {
            for (int col = 0; col < size; col++)
            {
                original[row, col] = (row + col) % 2 == 0;
            }
        }

        // Act
        var rawData = original.GetRawData(compression);
        var restored = new QRCodeData(rawData, compression);

        // Assert
        Assert.Equal(original.Version, restored.Version);
        Assert.Equal(original.Size, restored.Size);

        // Verify data integrity
        for (int row = 0; row < size; row++)
        {
            for (int col = 0; col < size; col++)
            {
                Assert.Equal(original[row, col], restored[row, col]);
            }
        }
    }

    /// <summary>
    /// Tests compression with all black and all white patterns
    /// </summary>
    [Theory]
    [InlineData(true, Compression.Deflate)]   // All black
    [InlineData(false, Compression.Deflate)]  // All white
    [InlineData(true, Compression.GZip)]      // All black
    [InlineData(false, Compression.GZip)]     // All white
    public void Compression_UniformPatterns(bool value, Compression compression)
    {
        // Arrange
        var qrCode = new QRCodeData(5);
        var size = qrCode.Size;

        // Fill with uniform value (highly compressible)
        for (int row = 0; row < size; row++)
        {
            for (int col = 0; col < size; col++)
            {
                qrCode[row, col] = value;
            }
        }

        // Act
        var uncompressed = qrCode.GetRawData(Compression.Uncompressed);
        var compressed = qrCode.GetRawData(compression);
        var restored = new QRCodeData(compressed, compression);

        // Assert
        // Uniform pattern should compress very well
        Assert.True(compressed.Length < uncompressed.Length, $"Compressed ({compressed.Length} bytes) should be much smaller than uncompressed ({uncompressed.Length} bytes)");

        // Verify data integrity
        for (int row = 0; row < size; row++)
        {
            for (int col = 0; col < size; col++)
            {
                Assert.Equal(value, restored[row, col]);
            }
        }
    }
}
