using Xunit;

namespace SkiaSharp.QrCode.Tests;

public class QRCodeGeneratorVersionBoundaryTest
{
    /// <summary>
    /// Tests exact version boundaries using actual measured capacities.
    /// These values come from DiagnoseActualCapacities_AllModes output.
    /// </summary>
    [Theory]
    // Version 1 boundaries (Numeric mode)
    [InlineData(ECCLevel.L, 41, 1)]
    [InlineData(ECCLevel.M, 34, 1)]
    [InlineData(ECCLevel.Q, 27, 1)]
    [InlineData(ECCLevel.H, 17, 1)]
    // Version 2 boundaries (Numeric mode)
    [InlineData(ECCLevel.L, 77, 2)]
    [InlineData(ECCLevel.M, 63, 2)]
    [InlineData(ECCLevel.Q, 48, 2)]
    [InlineData(ECCLevel.H, 34, 2)]
    // Version 10 boundaries (Numeric mode)
    [InlineData(ECCLevel.L, 652, 10)]
    [InlineData(ECCLevel.M, 513, 10)]
    [InlineData(ECCLevel.Q, 364, 10)]
    [InlineData(ECCLevel.H, 288, 10)]
    // Version 15 boundaries (Numeric mode)
    [InlineData(ECCLevel.L, 1250, 15)]
    [InlineData(ECCLevel.M, 991, 15)]
    [InlineData(ECCLevel.Q, 703, 15)]
    [InlineData(ECCLevel.H, 530, 15)]
    // Version 16 boundaries (Numeric mode) - Stack allocation limit
    [InlineData(ECCLevel.L, 1408, 16)]
    [InlineData(ECCLevel.M, 1082, 16)]
    [InlineData(ECCLevel.Q, 775, 16)]
    [InlineData(ECCLevel.H, 602, 16)]
    // Version 17 boundaries (Numeric mode) - Heap allocation starts
    [InlineData(ECCLevel.L, 1548, 17)]
    [InlineData(ECCLevel.M, 1212, 17)]
    [InlineData(ECCLevel.Q, 876, 17)]
    [InlineData(ECCLevel.H, 674, 17)]
    // Version 20 boundaries (Numeric mode)
    [InlineData(ECCLevel.L, 2061, 20)]
    [InlineData(ECCLevel.M, 1600, 20)]
    [InlineData(ECCLevel.Q, 1159, 20)]
    [InlineData(ECCLevel.H, 919, 20)]
    // Version 30 boundaries (Numeric mode)
    [InlineData(ECCLevel.L, 4158, 30)]
    [InlineData(ECCLevel.M, 3289, 30)]
    [InlineData(ECCLevel.Q, 2358, 30)]
    [InlineData(ECCLevel.H, 1782, 30)]
    // Version 40 boundaries (Numeric mode) - Maximum capacity
    [InlineData(ECCLevel.L, 7089, 40)]
    [InlineData(ECCLevel.M, 5596, 40)]
    [InlineData(ECCLevel.Q, 3993, 40)]
    [InlineData(ECCLevel.H, 3057, 40)]
    public void CreateQrCode_ExactBoundary_Numeric(ECCLevel eccLevel, int maxChars, int expectedVersion)
    {
        // Arrange
        var plainText = new string('1', maxChars); // Numeric mode

        // Act
        var qrCode = QRCodeGenerator.CreateQrCode(plainText, eccLevel);

        // Assert
        Assert.Equal(expectedVersion, qrCode.Version);
        Assert.True(qrCode.Size > 0);
    }

    /// <summary>
    /// Tests exact version boundaries for Alphanumeric mode.
    /// </summary>
    [Theory]
    // Version 1 boundaries (Alphanumeric mode)
    [InlineData(ECCLevel.L, 25, 1)]
    [InlineData(ECCLevel.M, 20, 1)]
    [InlineData(ECCLevel.Q, 16, 1)]
    [InlineData(ECCLevel.H, 10, 1)]
    // Version 10 boundaries (Alphanumeric mode)
    [InlineData(ECCLevel.L, 395, 10)]
    [InlineData(ECCLevel.M, 311, 10)]
    [InlineData(ECCLevel.Q, 221, 10)]
    [InlineData(ECCLevel.H, 174, 10)]
    // Version 16 boundaries (Alphanumeric mode) - Stack allocation limit
    [InlineData(ECCLevel.L, 854, 16)]
    [InlineData(ECCLevel.M, 656, 16)]
    [InlineData(ECCLevel.Q, 470, 16)]
    [InlineData(ECCLevel.H, 365, 16)]
    // Version 17 boundaries (Alphanumeric mode) - Heap allocation starts
    [InlineData(ECCLevel.L, 938, 17)]
    [InlineData(ECCLevel.M, 734, 17)]
    [InlineData(ECCLevel.Q, 531, 17)]
    [InlineData(ECCLevel.H, 408, 17)]
    // Version 40 boundaries (Alphanumeric mode)
    [InlineData(ECCLevel.L, 4296, 40)]
    [InlineData(ECCLevel.M, 3391, 40)]
    [InlineData(ECCLevel.Q, 2420, 40)]
    [InlineData(ECCLevel.H, 1852, 40)]
    public void CreateQrCode_ExactBoundary_Alphanumeric(ECCLevel eccLevel, int maxChars, int expectedVersion)
    {
        // Arrange
        var plainText = new string('A', maxChars); // Alphanumeric mode

        // Act
        var qrCode = QRCodeGenerator.CreateQrCode(plainText, eccLevel);

        // Assert
        Assert.Equal(expectedVersion, qrCode.Version);
    }

    /// <summary>
    /// Tests exact version boundaries for Byte mode (UTF-8 multi-byte characters).
    /// </summary>
    [Theory]
    // Version 1 boundaries (Byte mode, UTF-8 'あ')
    [InlineData(ECCLevel.L, 5, 1)]
    [InlineData(ECCLevel.M, 4, 1)]
    [InlineData(ECCLevel.Q, 3, 1)]
    [InlineData(ECCLevel.H, 2, 1)]
    // Version 10 boundaries (Byte mode, UTF-8 'あ')
    [InlineData(ECCLevel.L, 90, 10)]
    [InlineData(ECCLevel.M, 70, 10)]
    [InlineData(ECCLevel.Q, 50, 10)]
    [InlineData(ECCLevel.H, 39, 10)]
    // Version 16 boundaries (Byte mode, UTF-8 'あ') - Stack allocation limit
    [InlineData(ECCLevel.L, 195, 16)]
    [InlineData(ECCLevel.M, 149, 16)]
    [InlineData(ECCLevel.Q, 107, 16)]
    [InlineData(ECCLevel.H, 83, 16)]
    // Version 17 boundaries (Byte mode, UTF-8 'あ') - Heap allocation starts
    [InlineData(ECCLevel.L, 214, 17)]
    [InlineData(ECCLevel.M, 167, 17)]
    [InlineData(ECCLevel.Q, 121, 17)]
    [InlineData(ECCLevel.H, 93, 17)]
    // Version 40 boundaries (Byte mode, UTF-8 'あ')
    [InlineData(ECCLevel.L, 984, 40)]
    [InlineData(ECCLevel.M, 776, 40)]
    [InlineData(ECCLevel.Q, 554, 40)]
    [InlineData(ECCLevel.H, 424, 40)]
    public void CreateQrCode_ExactBoundary_ByteUtf8(ECCLevel eccLevel, int maxChars, int expectedVersion)
    {
        // Arrange
        var plainText = new string('あ', maxChars); // UTF-8 multi-byte (3 bytes per char)

        // Act
        var qrCode = QRCodeGenerator.CreateQrCode(plainText, eccLevel);

        // Assert
        Assert.Equal(expectedVersion, qrCode.Version);
    }

    /// <summary>
    /// Tests that exceeding capacity by 1 character bumps to next version.
    /// </summary>
    [Theory]
    // Numeric mode overflow tests
    [InlineData(ECCLevel.L, 42, 2)]    // v1 max=41, 42 should be v2
    [InlineData(ECCLevel.L, 78, 3)]    // v2 max=77, 78 should be v3
    [InlineData(ECCLevel.L, 1409, 17)] // v16 max=1408, 1409 should be v17
    [InlineData(ECCLevel.L, 7090, -1)] // v40 max=7089, 7090 should fail or stay v40
    [InlineData(ECCLevel.M, 5597, -1)] // v40 max=7089, 7090 should fail or stay v40
    [InlineData(ECCLevel.Q, 3994, -1)] // v40 max=7089, 7090 should fail or stay v40
    [InlineData(ECCLevel.H, 3058, -1)] // v40 max=7089, 7090 should fail or stay v40
    public void CreateQrCode_OverflowBoundary_Numeric(ECCLevel eccLevel, int charCount, int expectedNextVersion)
    {
        // Arrange
        var plainText = new string('1', charCount);

        // Act & Assert
        if (expectedNextVersion == -1)
        {
            // Data too long for max version
            Assert.Throws<InvalidOperationException>(() => QRCodeGenerator.CreateQrCode(plainText, eccLevel));
        }
        else
        {
            var qrCode = QRCodeGenerator.CreateQrCode(plainText, eccLevel);
            Assert.Equal(expectedNextVersion, qrCode.Version);
        }
    }

    /// <summary>
    /// Tests version selection with forced version parameter.
    /// This bypasses automatic version selection logic.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(15)]
    [InlineData(16)]  // Stack allocation boundary
    [InlineData(17)]  // Heap allocation boundary
    [InlineData(20)]
    [InlineData(25)]
    [InlineData(30)]
    [InlineData(35)]
    [InlineData(40)]
    public void CreateQrCode_ForcedVersion_GeneratesCorrectly(int requestedVersion)
    {
        // Arrange
        var plainText = "TEST";

        // Act
        var qrCode = QRCodeGenerator.CreateQrCode(plainText, ECCLevel.L, requestedVersion: requestedVersion);

        // Assert
        Assert.Equal(requestedVersion, qrCode.Version);
        var expectedSize = 21 + (requestedVersion - 1) * 4 + 8; // Core size + quiet zone
        Assert.Equal(expectedSize, qrCode.Size);
    }

    /// <summary>
    /// Stress test: Generate all versions from 1 to 40 using requestedVersion.
    /// </summary>
    [Fact]
    public void CreateQrCode_AllVersions_WithRequestedVersion_GenerateSuccessfully()
    {
        // Act & Assert
        for (int version = 1; version <= 40; version++)
        {
            var plainText = "1";
            var qrCode = QRCodeGenerator.CreateQrCode(plainText, ECCLevel.L, requestedVersion: version);

            Assert.Equal(version, qrCode.Version);
            var expectedSize = 21 + (version - 1) * 4 + 8;
            Assert.Equal(expectedSize, qrCode.Size);
        }
    }

    /// <summary>
    /// Tests critical version boundaries (v15/v16/v17) for stack/heap allocation transition.
    /// </summary>
    [Theory]
    [InlineData(ECCLevel.L, 1250, 15)]  // v15 max (stack)
    [InlineData(ECCLevel.L, 1251, 16)]  // v15 overflow → v16 (still stack)
    [InlineData(ECCLevel.L, 1408, 16)]  // v16 max (stack limit)
    [InlineData(ECCLevel.L, 1409, 17)]  // v16 overflow → v17 (heap starts)
    [InlineData(ECCLevel.L, 1548, 17)]  // v17 max (heap)
    [InlineData(ECCLevel.M, 991, 15)]   // v15 max (M)
    [InlineData(ECCLevel.M, 992, 16)]   // v15 overflow (M)
    [InlineData(ECCLevel.M, 1082, 16)]  // v16 max (M)
    [InlineData(ECCLevel.M, 1083, 17)]  // v16 overflow (M)
    public void CreateQrCode_StackHeapBoundary_Numeric(ECCLevel eccLevel, int charCount, int expectedVersion)
    {
        // Arrange
        var plainText = new string('1', charCount);

        // Act
        var qrCode = QRCodeGenerator.CreateQrCode(plainText, eccLevel);

        // Assert
        Assert.Equal(expectedVersion, qrCode.Version);
    }

    /// <summary>
    /// Tests automatic version selection for various data sizes (with tolerance).
    /// Kept for backward compatibility and fuzzy testing.
    /// </summary>
    [Theory]
    [InlineData(10, 1, 2)]      // Small data -> v1 or v2
    [InlineData(50, 2, 3)]      // Small-medium -> v2 or v3
    [InlineData(200, 5, 7)]     // Medium -> v5-v7
    [InlineData(500, 9, 11)]    // Large -> v9-v11
    [InlineData(1000, 13, 15)]  // Very large -> v13-v15
    [InlineData(2000, 20, 22)]  // Huge -> v20-v22
    [InlineData(5000, 32, 35)]  // Maximum -> v32-v35
    public void CreateQrCode_AutoVersionSelection_InRange(int charCount, int minVersion, int maxVersion)
    {
        // Arrange
        var plainText = new string('1', charCount);

        // Act
        var qrCode = QRCodeGenerator.CreateQrCode(plainText, ECCLevel.L);

        // Assert
        Assert.InRange(qrCode.Version, minVersion, maxVersion);
    }

    /// <summary>
    /// Diagnostic test: Discovers actual capacity boundaries for documentation.
    /// This test always passes but outputs useful capacity information.
    /// </summary>
    [Fact(Skip = "Diagnostic test - run manually to discover actual capacities")]
    public void DiagnoseActualCapacities_AllModes()
    {
        var results = new System.Text.StringBuilder();
        results.AppendLine("# Actual QR Code Capacities (including overhead)");
        results.AppendLine();
        results.AppendLine("**Test Characters:**");
        results.AppendLine("- Numeric: `'1'` (digit)");
        results.AppendLine("- Alphanumeric: `'A'` (uppercase letter)");
        results.AppendLine("- Byte (UTF-8): `'あ'` (hiragana, 3 bytes)");
        results.AppendLine();

        foreach (var eccLevel in new[] { ECCLevel.L, ECCLevel.M, ECCLevel.Q, ECCLevel.H })
        {
            results.AppendLine($"## ECC Level: {eccLevel}");
            results.AppendLine();
            results.AppendLine("| Version | Numeric | Alphanumeric | Byte (UTF-8 Multi-byte*) |");
            results.AppendLine("|---------|---------|--------------|------|");

            for (int version = 1; version <= 40; version++)
            {
                int numericCap = FindMaxCapacity(version, eccLevel, '1');
                int alphaCap = FindMaxCapacity(version, eccLevel, 'A');
                int byteCap = FindMaxCapacity(version, eccLevel, 'あ'); // Multi-byte

                results.AppendLine($"| {version,2} | {numericCap,7} | {alphaCap,12} | {byteCap,4} |");
            }

            results.AppendLine();
        }

        var output = results.ToString();
        System.IO.File.WriteAllText("actual_qr_capacities.md", output);
        Assert.True(true, "Capacity report written to actual_qr_capacities.md");
    }

    private static int FindMaxCapacity(int targetVersion, ECCLevel eccLevel, char fillChar)
    {
        int low = 1, high = 10000;
        int maxCapacity = 0;

        while (low <= high)
        {
            int mid = (low + high) / 2;
            var text = new string(fillChar, mid);

            try
            {
                var qr = QRCodeGenerator.CreateQrCode(text, eccLevel);

                if (qr.Version == targetVersion)
                {
                    maxCapacity = mid;
                    low = mid + 1;
                }
                else if (qr.Version < targetVersion)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }
            catch
            {
                high = mid - 1;
            }
        }

        return maxCapacity;
    }
}
