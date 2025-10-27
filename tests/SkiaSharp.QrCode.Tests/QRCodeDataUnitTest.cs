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
        var qrCode = new QRCodeData(version, quietZoneSize: 0);

        // Verify size matches expected
        Assert.Equal(expectedSize, qrCode.Size);

        // Get raw data
        var rawData = qrCode.GetRawData();

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
        var qrCode = new QRCodeData(version, quietZoneSize: quietZone);

        // Verify size
        Assert.Equal(expectedSize, qrCode.Size);

        // Get raw data
        var rawData = qrCode.GetRawData();

        // Verify byte alignment
        var baseSize = QRCodeData.SizeFromVersion(version);
        var totalBits = baseSize * baseSize;
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

    // SetCoreData

    /// <summary>
    /// Tests that QuietZone area remains white (false) after SetCoreData
    /// </summary>
    [Theory]
    [InlineData(2, 4)]
    [InlineData(5, 2)]
    [InlineData(10, 8)]
    public void SetCoreData_PreservesQuietZone(int version, int quietZoneSize)
    {
        var baseSize = QRCodeData.SizeFromVersion(version);
        var fullSize = baseSize + (quietZoneSize * 2);
        var qrCode = new QRCodeData(version, quietZoneSize: quietZoneSize);

        // Create core data with pattern
        var coreData = new byte[baseSize * baseSize];
        for (int i = 0; i < coreData.Length; i++)
        {
            coreData[i] = (byte)(i % 5 == 0 ? 1 : 0);
        }

        // Set core data
        qrCode.SetCoreData(coreData);

        // Verify QuietZone remains white
        for (int i = 0; i < fullSize; i++)
        {
            // Top and bottom
            for (int row = 0; row < quietZoneSize; row++)
            {
                Assert.False(qrCode[row, i], $"Top QuietZone corrupted at ({row}, {i})");
                Assert.False(qrCode[fullSize - 1 - row, i], $"Bottom QuietZone corrupted at ({fullSize - 1 - row}, {i})");
            }
            // Left and right
            for (int col = 0; col < quietZoneSize; col++)
            {
                Assert.False(qrCode[i, col], $"Left QuietZone corrupted at ({i}, {col})");
                Assert.False(qrCode[i, fullSize - 1 - col], $"Right QuietZone corrupted at ({i}, {fullSize - 1 - col})");
            }
        }

        // Verify core data was set correctly
        var retrievedCoreData = new byte[baseSize * baseSize];
        qrCode.GetCoreData(retrievedCoreData);
        Assert.Equal(coreData, retrievedCoreData);
    }

    // Serialization Tests

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
        var quietZoneSize = 0;
        // Use (row * matrixSize + col) % 7 to create a pattern that is not too dense.
        // 7 is not 8's multiple, it means it won't dup with byte border.
        // see remarks.
        var pattern = 7;
        var version = (matrixSize - 21) / 4 + 1;

        var original = new QRCodeData(version, quietZoneSize: quietZoneSize);

        // Verify size matched
        Assert.Equal(matrixSize, original.Size);

        // Get core buffer and fill pattern
        var coreSize = original.GetCoreSize();
        Span<byte> coreBuffer = stackalloc byte[coreSize * coreSize];

        for (int i = 0; i < coreBuffer.Length; i++)
        {
            coreBuffer[i] = (byte)(i % pattern == 0 ? 1 : 0);
        }

        // Set core data
        original.SetCoreData(coreBuffer);

        // Serialize and Deserialize
        var rawData = original.GetRawData();
        var restored = new QRCodeData(rawData, quietZoneSize: quietZoneSize);

        Assert.Equal(matrixSize, restored.Size);

        // Verify pattern
        for (int row = 0; row < matrixSize; row++)
        {
            for (int col = 0; col < matrixSize; col++)
            {
                var index = row * matrixSize + col;
                var expected = index % pattern == 0;
                Assert.Equal(expected, restored[row, col]);
            }
        }
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
    [Fact]
    public void Serialization_RoundTrip()
    {
        var quietZoneSize = 0;
        // Use (row + col) % 3 to create a pattern that is not too dense.
        // 3 is not 8's multiple, it means it won't dup with byte border.
        // see remarks.
        var pattern = 3;

        // Arrange
        var original = new QRCodeData(5, quietZoneSize: quietZoneSize);  // Version 5: 37×37
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
        var rawData = original.GetRawData();
        var restored = new QRCodeData(rawData, quietZoneSize);

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
    /// Tests serialization/deserialization with QuietZone
    /// Verifies that QuietZone is properly excluded from serialized data and can be restored
    /// </summary>
    [Theory]
    [InlineData(1, 4)]   // Version 1, QuietZone 4
    [InlineData(2, 2)]   // Version 2, QuietZone 2
    [InlineData(5, 4)]   // Version 5, QuietZone 4
    [InlineData(10, 8)]  // Version 10, QuietZone 8
    public void Serialization_WithQuietZone_PreservesOnlyCoreData(int version, int quietZoneSize)
    {
        var baseSize = QRCodeData.SizeFromVersion(version);
        var fullSize = baseSize + (quietZoneSize * 2);

        // Create QR code with QuietZone
        var original = new QRCodeData(version, quietZoneSize: quietZoneSize);
        Assert.Equal(fullSize, original.Size);

        // Set pattern in core data area only
        for (int row = quietZoneSize; row < fullSize - quietZoneSize; row++)
        {
            for (int col = quietZoneSize; col < fullSize - quietZoneSize; col++)
            {
                var coreRow = row - quietZoneSize;
                var coreCol = col - quietZoneSize;
                original[row, col] = (coreRow + coreCol) % 3 == 0;
            }
        }

        // Verify QuietZone is white (false/0)
        for (int i = 0; i < fullSize; i++)
        {
            // Top and bottom rows
            for (int row = 0; row < quietZoneSize; row++)
            {
                Assert.False(original[row, i], $"Top QuietZone should be white at ({row}, {i})");
                Assert.False(original[fullSize - 1 - row, i], $"Bottom QuietZone should be white at ({fullSize - 1 - row}, {i})");
            }
            // Left and right columns
            for (int col = 0; col < quietZoneSize; col++)
            {
                Assert.False(original[i, col], $"Left QuietZone should be white at ({i}, {col})");
                Assert.False(original[i, fullSize - 1 - col], $"Right QuietZone should be white at ({i}, {fullSize - 1 - col})");
            }
        }

        // Serialize and deserialize with same QuietZone size
        var rawData = original.GetRawData();
        var restored = new QRCodeData(rawData, quietZoneSize: quietZoneSize);

        Assert.Equal(original.Version, restored.Version);
        Assert.Equal(original.Size, restored.Size);

        // Verify core data is preserved
        for (int row = quietZoneSize; row < fullSize - quietZoneSize; row++)
        {
            for (int col = quietZoneSize; col < fullSize - quietZoneSize; col++)
            {
                Assert.Equal(original[row, col], restored[row, col]);
            }
        }

        // Verify QuietZone is restored as white
        for (int i = 0; i < fullSize; i++)
        {
            for (int row = 0; row < quietZoneSize; row++)
            {
                Assert.False(restored[row, i]);
                Assert.False(restored[fullSize - 1 - row, i]);
            }
            for (int col = 0; col < quietZoneSize; col++)
            {
                Assert.False(restored[i, col]);
                Assert.False(restored[i, fullSize - 1 - col]);
            }
        }
    }

    /// <summary>
    /// Tests that different QuietZone sizes can be specified during deserialization
    /// Serialized data contains only core data, so any QuietZone size can be applied
    /// </summary>
    [Theory]
    [InlineData(2, 0, 4)]   // Serialize with no QuietZone, deserialize with QuietZone 4
    [InlineData(2, 4, 0)]   // Serialize with QuietZone 4, deserialize with no QuietZone
    [InlineData(2, 2, 8)]   // Serialize with QuietZone 2, deserialize with QuietZone 8
    [InlineData(5, 4, 2)]   // Serialize with QuietZone 4, deserialize with QuietZone 2
    public void Serialization_DifferentQuietZoneSizes_WorksCorrectly(int version, int serializeQuietZone, int deserializeQuietZone)
    {
        var baseSize = QRCodeData.SizeFromVersion(version);

        // Create with first QuietZone size
        var original = new QRCodeData(version, quietZoneSize: serializeQuietZone);
        var originalFullSize = baseSize + (serializeQuietZone * 2);

        // Set pattern in core area
        for (int row = serializeQuietZone; row < originalFullSize - serializeQuietZone; row++)
        {
            for (int col = serializeQuietZone; col < originalFullSize - serializeQuietZone; col++)
            {
                var coreRow = row - serializeQuietZone;
                var coreCol = col - serializeQuietZone;
                original[row, col] = (coreRow * baseSize + coreCol) % 7 == 0;
            }
        }

        // Serialize and deserialize with different QuietZone size
        var rawData = original.GetRawData();
        var restored = new QRCodeData(rawData, quietZoneSize: deserializeQuietZone);

        var restoredFullSize = baseSize + (deserializeQuietZone * 2);

        Assert.Equal(version, restored.Version);
        Assert.Equal(restoredFullSize, restored.Size);

        // Verify core data matches
        for (int coreRow = 0; coreRow < baseSize; coreRow++)
        {
            for (int coreCol = 0; coreCol < baseSize; coreCol++)
            {
                var originalRow = coreRow + serializeQuietZone;
                var originalCol = coreCol + serializeQuietZone;
                var restoredRow = coreRow + deserializeQuietZone;
                var restoredCol = coreCol + deserializeQuietZone;

                Assert.Equal(original[originalRow, originalCol], restored[restoredRow, restoredCol]);
            }
        }
    }

    // IBufferWriter<byte> Test

    [Theory]
    [InlineData(1, 0)]   // Version 1, no quiet zone
    [InlineData(1, 4)]   // Version 1, with quiet zone
    [InlineData(10, 0)]  // Version 10, no quiet zone
    [InlineData(10, 4)]  // Version 10, with quiet zone
    [InlineData(40, 0)]  // Version 40 (max), no quiet zone
    [InlineData(40, 4)]  // Version 40 (max), with quiet zone
    public void GetRawData_ArrayAndBufferWriter_ProduceSameResult(int version, int quietZoneSize)
    {
        // Arrange
        var qrData = CreateTestQRCode(version, quietZoneSize);

        // Act
        var arrayResult = qrData.GetRawData();

        var writer = new System.Buffers.ArrayBufferWriter<byte>();
        var bytesWritten = qrData.GetRawData(writer);

        // Assert
        Assert.Equal(arrayResult.Length, bytesWritten);
        Assert.Equal(arrayResult, writer.WrittenSpan.ToArray());
    }

    [Fact]
    public void GetRawData_BufferWriter_AdvancesCorrectly()
    {
        // Arrange
        var qrData = CreateTestQRCode(1, 0);
        var writer = new System.Buffers.ArrayBufferWriter<byte>();

        // Act
        var bytesWritten = qrData.GetRawData(writer);

        // Assert
        Assert.Equal(bytesWritten, writer.WrittenCount);
        Assert.True(writer.WrittenCount > 0);

        // Verify header
        var data = writer.WrittenSpan;
        Assert.Equal(0x51, data[0]); // 'Q'
        Assert.Equal(0x52, data[1]); // 'R'
        Assert.Equal(0x52, data[2]); // 'R'
    }

    [Fact]
    public void GetRawData_BufferWriter_CanBeCalledMultipleTimes()
    {
        // Arrange
        var qrData = CreateTestQRCode(1, 0);
        var writer = new System.Buffers.ArrayBufferWriter<byte>();

        // Act
        var bytesWritten1 = qrData.GetRawData(writer);
        var data1 = writer.WrittenSpan.ToArray();

        writer.Clear();

        var bytesWritten2 = qrData.GetRawData(writer);
        var data2 = writer.WrittenSpan.ToArray();

        // Assert
        Assert.Equal(bytesWritten1, bytesWritten2);
        Assert.Equal(data1, data2);
    }

    [Fact]
    public void GetRawDataSize_ReturnsCorrectSize()
    {
        // Arrange
        var qrData = CreateTestQRCode(1, 0);

        // Act
        var expectedSize = qrData.GetRawDataSize();
        var actualData = qrData.GetRawData();

        // Assert
        Assert.Equal(expectedSize, actualData.Length);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(40)]
    public void Roundtrip_WithBufferWriter_PreservesData(int version)
    {
        // Arrange
        var original = CreateTestQRCode(version, quietZoneSize: 4);

        // Act - Serialize
        var writer = new System.Buffers.ArrayBufferWriter<byte>();
        original.GetRawData(writer);
        var serialized = writer.WrittenSpan.ToArray();

        // Act - Deserialize
        var restored = new QRCodeData(serialized, quietZoneSize: 4);

        // Assert
        Assert.Equal(original.Version, restored.Version);
        Assert.Equal(original.Size, restored.Size);
        AssertQRCodeDataEqual(original, restored);
    }

    [Fact]
    public void GetRawData_BufferWriter_WorksWithCustomWriter()
    {
        // Arrange
        var qrData = CreateTestQRCode(1, 0);
        var customWriter = new TestBufferWriter();

        // Act
        var bytesWritten = qrData.GetRawData(customWriter);

        // Assert
        Assert.Equal(bytesWritten, customWriter.WrittenCount);
        Assert.True(customWriter.AdvanceCalled, "Advance() must be called");
    }

    [Theory]
    [InlineData(1, 0, 4)]   // Serialize with no QuietZone, deserialize with QuietZone 4
    [InlineData(2, 4, 0)]   // Serialize with QuietZone 4, deserialize with no QuietZone
    [InlineData(5, 4, 2)]   // Different QuietZone sizes
    public void BufferWriter_DifferentQuietZoneSizes_WorksCorrectly(int version, int serializeQuietZone, int deserializeQuietZone)
    {
        var baseSize = QRCodeData.SizeFromVersion(version);

        // Create with first QuietZone size
        var original = new QRCodeData(version, quietZoneSize: serializeQuietZone);
        var originalFullSize = baseSize + (serializeQuietZone * 2);

        // Set pattern in core area
        for (int row = serializeQuietZone; row < originalFullSize - serializeQuietZone; row++)
        {
            for (int col = serializeQuietZone; col < originalFullSize - serializeQuietZone; col++)
            {
                var coreRow = row - serializeQuietZone;
                var coreCol = col - serializeQuietZone;
                original[row, col] = (coreRow * baseSize + coreCol) % 7 == 0;
            }
        }

        // Serialize with BufferWriter and deserialize
        var writer = new System.Buffers.ArrayBufferWriter<byte>();
        original.GetRawData(writer);
        var rawData = writer.WrittenSpan.ToArray();

        var restored = new QRCodeData(rawData, quietZoneSize: deserializeQuietZone);

        var restoredFullSize = baseSize + (deserializeQuietZone * 2);

        Assert.Equal(version, restored.Version);
        Assert.Equal(restoredFullSize, restored.Size);

        // Verify core data matches
        for (int coreRow = 0; coreRow < baseSize; coreRow++)
        {
            for (int coreCol = 0; coreCol < baseSize; coreCol++)
            {
                var originalRow = coreRow + serializeQuietZone;
                var originalCol = coreCol + serializeQuietZone;
                var restoredRow = coreRow + deserializeQuietZone;
                var restoredCol = coreCol + deserializeQuietZone;

                Assert.Equal(original[originalRow, originalCol], restored[restoredRow, restoredCol]);
            }
        }
    }

    [Fact]
    public void GetRawData_BufferWriter_ReusesBufferEfficiently()
    {
        // Arrange
        var qrData1 = CreateTestQRCode(1, 0);
        var qrData2 = CreateTestQRCode(2, 0);
        var writer = new System.Buffers.ArrayBufferWriter<byte>();

        // Act - First write
        var bytes1 = qrData1.GetRawData(writer);
        var data1 = writer.WrittenSpan.ToArray();

        writer.Clear();

        // Act - Second write (buffer reused)
        var bytes2 = qrData2.GetRawData(writer);
        var data2 = writer.WrittenSpan.ToArray();

        // Assert
        Assert.NotEqual(bytes1, bytes2); // Different sizes
        Assert.NotEqual(data1, data2); // Different data

        // Both should produce valid QR codes
        var restored1 = new QRCodeData(data1, quietZoneSize: 0);
        var restored2 = new QRCodeData(data2, quietZoneSize: 0);

        Assert.Equal(1, restored1.Version);
        Assert.Equal(2, restored2.Version);
    }

    // helpers

    private static QRCodeData CreateTestQRCode(int version, int quietZoneSize)
    {
        var qrData = new QRCodeData(version, quietZoneSize);

        // Fill with test pattern
        var coreSize = QRCodeData.SizeFromVersion(version);
        var fullSize = coreSize + (quietZoneSize * 2);

        for (var row = quietZoneSize; row < fullSize - quietZoneSize; row++)
        {
            for (var col = quietZoneSize; col < fullSize - quietZoneSize; col++)
            {
                // Checkerboard-like pattern using prime number
                var coreRow = row - quietZoneSize;
                var coreCol = col - quietZoneSize;
                var index = coreRow * coreSize + coreCol;
                var isDark = index % 7 == 0; // Prime number to avoid alignment with byte boundaries
                qrData[row, col] = isDark;
            }
        }

        return qrData;
    }

    private static void AssertQRCodeDataEqual(QRCodeData expected, QRCodeData actual)
    {
        Assert.Equal(expected.Version, actual.Version);
        Assert.Equal(expected.Size, actual.Size);

        for (var row = 0; row < expected.Size; row++)
        {
            for (var col = 0; col < expected.Size; col++)
            {
                Assert.Equal(expected[row, col], actual[row, col]);
            }
        }
    }

    // Custom test writer to verify Advance() is called
    private class TestBufferWriter : System.Buffers.IBufferWriter<byte>
    {
        private readonly System.Buffers.ArrayBufferWriter<byte> _inner = new();
        public bool AdvanceCalled { get; private set; }
        public int WrittenCount => _inner.WrittenCount;

        public void Advance(int count)
        {
            AdvanceCalled = true;
            _inner.Advance(count);
        }

        public Memory<byte> GetMemory(int sizeHint = 0) => _inner.GetMemory(sizeHint);
        public Span<byte> GetSpan(int sizeHint = 0) => _inner.GetSpan(sizeHint);
    }
}
