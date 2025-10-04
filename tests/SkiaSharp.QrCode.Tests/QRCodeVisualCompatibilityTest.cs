using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace SkiaSharp.QrCode.Tests;

/// <summary>
/// Visual compatibility test using pixel compatison instead of binary comparison.
/// </summary>
public class QRCodeVisualCompatibilityTest
{
    private static readonly string directoryName = "testdata/pixels";

    public QRCodeVisualCompatibilityTest()
    {
        if (!Directory.Exists(directoryName))
        {
            Directory.CreateDirectory(directoryName);
        }
    }

    /// <summary>
    /// Generate HTML report of all golden files for visual inspection.
    /// </summary>
    //[Fact(Skip = "Manual execution only")]
    [Fact]
    public void GenerateGoldenFilesReport()
    {
        VisualCompatibilityTestHelper.GenerateGoldenFilesReport(directoryName);
    }

    // tests

    [Theory]
    [InlineData("0123456789", ECCLevel.L)]
    [InlineData("HELLO WORLD", ECCLevel.M)]
    [InlineData("ABC-123", ECCLevel.Q)]
    [InlineData("Test123", ECCLevel.H)]
    [InlineData("Hello, World!", ECCLevel.L)]
    [InlineData("こんにちは", ECCLevel.M)]
    [InlineData("你好世界", ECCLevel.Q)]
    [InlineData("Привет мир", ECCLevel.H)]
    [InlineData("🎉🎊🎈", ECCLevel.L)]
    [InlineData("café", ECCLevel.M)]
    [InlineData("Ñoño", ECCLevel.Q)]
    public void CreateQrCode_Default_PixelsMatchSample(string content, ECCLevel eccLevel)
    {
        AssertPixelsMatchSample(content, eccLevel, EciMode.Default);
    }

    [Theory]
    [InlineData("0123456789", ECCLevel.L)]
    [InlineData("HELLO WORLD", ECCLevel.M)]
    [InlineData("ABC-123", ECCLevel.Q)]
    [InlineData("Test123", ECCLevel.H)]
    [InlineData("Hello, World!", ECCLevel.L)]
    [InlineData("こんにちは", ECCLevel.M)]
    [InlineData("你好世界", ECCLevel.Q)]
    [InlineData("Привет мир", ECCLevel.H)]
    [InlineData("🎉🎊🎈", ECCLevel.L)]
    [InlineData("café", ECCLevel.M)]
    [InlineData("Ñoño", ECCLevel.Q)]
    public void CreateQrCode_Utf8_PixelsMatchSample(string content, ECCLevel eccLevel)
    {
        AssertPixelsMatchSample(content, eccLevel, EciMode.Utf8);
    }

    [Theory]
    [InlineData("Café", ECCLevel.L)]
    [InlineData("Résumé", ECCLevel.M)]
    [InlineData("Naïve", ECCLevel.Q)]
    [InlineData("Zürich", ECCLevel.H)]
    public void CreateQrCode_Iso8859_1_PixelsMatchSample(string content, ECCLevel eccLevel)
    {
        AssertPixelsMatchSample(content, eccLevel, EciMode.Iso8859_1);
    }

    // edge cases

    [Theory]
    [InlineData("", ECCLevel.L, EciMode.Default)]
    [InlineData("", ECCLevel.M, EciMode.Utf8)]
    [InlineData("", ECCLevel.Q, EciMode.Iso8859_1)]
    [InlineData("", ECCLevel.H, EciMode.Default)]
    public void CreateQrCode_EmptyString_PixelsMatchTest(string content, ECCLevel eccLevel, EciMode eciMode)
    {
        AssertPixelsMatchSample(content, eccLevel, eciMode);
    }

    [Theory]
    [InlineData("A", ECCLevel.M, EciMode.Default)]  // Single character
    [InlineData("0", ECCLevel.Q, EciMode.Default)]  // Single digit
    [InlineData(" ", ECCLevel.Q, EciMode.Default)]  // Single space
    [InlineData("\t", ECCLevel.H, EciMode.Default)] // Tab
    [InlineData("\n", ECCLevel.L, EciMode.Utf8)]    // Newline
    public void CreateQrCode_EdgeCases_PixelsMatchSample(string content, ECCLevel eccLevel, EciMode eciMode)
    {
        AssertPixelsMatchSample(content, eccLevel, eciMode);
    }

    [Theory]
    [InlineData(ECCLevel.L)] // Version 1 max
    [InlineData(ECCLevel.M)]
    [InlineData(ECCLevel.Q)]
    [InlineData(ECCLevel.H)]
    public void CreateQrCode_MaxCapacity_Version1_PixelsMatchSample(ECCLevel eccLevel)
    {
        // Max capacity for Version 1 (numeric mode)
        var maxChars = eccLevel switch
        {
            ECCLevel.L => 41,
            ECCLevel.M => 34,
            ECCLevel.Q => 27,
            ECCLevel.H => 17,
            _ => throw new ArgumentOutOfRangeException()
        };

        var content = new string('1', maxChars);
        AssertPixelsMatchSample(content, eccLevel, EciMode.Default);
    }

    // Version Boundary Tests

    [Theory]
    [MemberData(nameof(GetVersionBoundaryTestCases))]
    public void CreateQrCode_VersionBoundaries_PixelsMatchSample(string content, ECCLevel eccLevel, EciMode eciMode, int expectedVersion)
    {
        using var generator = new QRCodeGenerator();
        var qr = generator.CreateQrCode(content, eccLevel, eciMode: eciMode);

        // Verify version is as expected
        Assert.Equal(expectedVersion, qr.Version);

        // Verify pixels match golden
        AssertPixelsMatchSample(content, eccLevel, eciMode);
    }

    // generator

    public static IEnumerable<object[]> GetVersionBoundaryTestCases()
    {
        // Default mode (no ECI header)
        // Numeric mode
        // V1-L: 152 bits - 4 (mode) - 10 (count) = 138 bits → 41 digits
        yield return new object[] { new string('1', 41), ECCLevel.L, EciMode.Default, 1 };  // V1-L max
        yield return new object[] { new string('1', 42), ECCLevel.L, EciMode.Default, 2 };  // V2-L min

        // V1-M: 128 bits - 4 - 10 = 114 bits → 34 digits  
        yield return new object[] { new string('1', 34), ECCLevel.M, EciMode.Default, 1 };  // V1-M max
        yield return new object[] { new string('1', 35), ECCLevel.M, EciMode.Default, 2 };  // V2-M min

        // V1-H: 72 bits - 4 - 10 = 58 bits → 17 digits
        yield return new object[] { new string('1', 17), ECCLevel.H, EciMode.Default, 1 };  // V1-H max
        yield return new object[] { new string('1', 18), ECCLevel.H, EciMode.Default, 2 };  // V2-H min

        //Alphanumeric mode
        // V1-L: 152 bits - 4 - 9 = 139 bits → 25 chars
        yield return new object[] { new string('A', 25), ECCLevel.L, EciMode.Default, 1 };  // V1-L max
        yield return new object[] { new string('A', 26), ECCLevel.L, EciMode.Default, 2 };  // V2-L min

        // V1-M: 128 bits - 4 - 9 = 115 bits → 20 chars
        yield return new object[] { new string('A', 20), ECCLevel.M, EciMode.Default, 1 };  // V1-M max
        yield return new object[] { new string('A', 21), ECCLevel.M, EciMode.Default, 2 };  // V2-M min

        //  V1-H: 72 bits - 4 - 9 = 59 bits → 10 chars
        yield return new object[] { new string('A', 10), ECCLevel.H, EciMode.Default, 1 };  // V1-H max
        yield return new object[] { new string('A', 11), ECCLevel.H, EciMode.Default, 2 };  // V2-H min

        // Encoding with ECI header (12 bits)
        foreach (var eci in new[] { EciMode.Iso8859_1, EciMode.Utf8 })
        {
            // Numeric mode
            // V1-L: 152 bits - 12 (ECI) - 4 (mode) - 10 (count) = 126 bits → 37 digits
            yield return new object[] { new string('1', 37), ECCLevel.L, eci, 1 };  // V1-L max
            yield return new object[] { new string('1', 38), ECCLevel.L, eci, 2 };  // V2-L min

            // V1-M: 128 bits - 12 - 4 - 10 = 102 bits → 30 digits  
            yield return new object[] { new string('1', 30), ECCLevel.M, eci, 1 };  // V1-M max
            yield return new object[] { new string('1', 31), ECCLevel.M, eci, 2 };  // V2-M min

            // V1-H: 72 bits - 12 - 4 - 10 = 46 bits → 13 digits
            yield return new object[] { new string('1', 13), ECCLevel.H, eci, 1 };  // V1-H max
            yield return new object[] { new string('1', 14), ECCLevel.H, eci, 2 };  // V2-H min

            //Alphanumeric mode
            // V1-L: 152 bits - 12 - 4 - 9 = 127 bits → 23 chars
            yield return new object[] { new string('A', 23), ECCLevel.L, eci, 1 };  // V1-L max
            yield return new object[] { new string('A', 24), ECCLevel.L, eci, 2 };  // V2-L min

            // V1-M: 128 bits - 12 - 4 - 9 = 103 bits → 18 chars
            yield return new object[] { new string('A', 18), ECCLevel.M, eci, 1 };  // V1-M max
            yield return new object[] { new string('A', 19), ECCLevel.M, eci, 2 };  // V2-M min

            //  V1-H: 72 bits - 12 - 4 - 9 = 47 bits → 8 chars
            yield return new object[] { new string('A', 8), ECCLevel.H, eci, 1 };  // V1-H max
            yield return new object[] { new string('A', 9), ECCLevel.H, eci, 2 };  // V2-H min
        }
    }

    // helpers

    /// <summary>
    /// Assert that generated QR code pixels match sample file.
    /// </summary>
    private void AssertPixelsMatchSample(string content, ECCLevel eccLevel, EciMode eciMode)
    {
        using var generator = new QRCodeGenerator();
        var qr = generator.CreateQrCode(content, eccLevel, eciMode: eciMode);

        // Render to pixel array
        var actualPixels = RenderToPixelArray(qr);
        var samplePath = GetSampleFilePath(content, eccLevel, eciMode);

        if (!File.Exists(samplePath))
        {
            // First run: save as sample
            SavePixelData(samplePath, actualPixels, qr.ModuleMatrix.Count);
            Assert.True(true, $"Sample file created: {samplePath}");
        }
        else
        {
            // Load and compare with sample
            var (expectedPixels, expectedSize) = VisualCompatibilityTestHelper.LoadPixelData(samplePath);

            if (!actualPixels.SequenceEqual(expectedPixels))
            {
                // Save actual for comparison
                var actualPath = samplePath.Replace(".pixels", ".actual.pixels");
                SavePixelData(actualPath, actualPixels, qr.ModuleMatrix.Count);

                // Calculate difference percentage
                var diffCount = actualPixels.Zip(expectedPixels, (a, e) => a != e ? 1 : 0).Sum();
                var diffPercent = (diffCount * 100.0) / actualPixels.Length;

                Assert.Fail($"QR code pixels changed!\n" +
                           $"Content: {content}\n" +
                           $"ECC Level: {eccLevel}\n" +
                           $"ECI Mode: {eciMode}\n" +
                           $"Path: {samplePath}\n" +
                           $"Actual: {actualPath}\n" +
                           $"Size: {qr.ModuleMatrix.Count}x{qr.ModuleMatrix.Count}\n" +
                           $"Difference: {diffCount}/{actualPixels.Length} pixels ({diffPercent:F2}%)\n" +
                           $"Run visual diff tool to compare.");
            }

            // Also verify size matches
            Assert.Equal(expectedSize, qr.ModuleMatrix.Count);
        }
    }

    /// <summary>
    /// Render QR code to pixel array (black=0, white=255).
    /// </summary>
    private static byte[] RenderToPixelArray(QRCodeData qr)
    {
        var size = qr.ModuleMatrix.Count;
        var pixels = new byte[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                pixels[y * size + x] = qr.ModuleMatrix[y][x] ? (byte)0 : (byte)255;
            }
        }

        return pixels;
    }

    /// <summary>
    /// Save pixel data with metadata.
    /// Format: [4 bytes size][pixel data]
    /// </summary>
    private static void SavePixelData(string path, byte[] pixels, int size)
    {
        using var fs = File.Create(path);
        using var writer = new BinaryWriter(fs);

        // Write metadata
        writer.Write(size); // 4 bytes: matrix size
        writer.Write(pixels.Length); // 4 bytes: pixel count

        // Write pixel data
        writer.Write(pixels);
    }

    /// <summary>
    /// Get sample file path for test case.
    /// </summary>
    private static string GetSampleFilePath(string content, ECCLevel eccLevel, EciMode eciMode)
    {
        // Sanitize content for filename
        var safeContent = SanitizeForFilename(content);

        // Truncate if too long
        if (safeContent.Length > 50)
        {
            safeContent = safeContent.Substring(0, 50) + "_" + GetContentHash(content);
        }

        var filename = $"{safeContent}_ecc{eccLevel}_eci{(int)eciMode}.pixels";
        return Path.Combine(directoryName, filename);
    }

    /// <summary>
    /// Sanitize string for use in filename.
    /// </summary>
    private static string SanitizeForFilename(string input)
    {
        if (string.IsNullOrEmpty(input))
            return "empty";

        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("_", input.Split(invalid, StringSplitOptions.RemoveEmptyEntries));

        // Replace spaces and special chars
        sanitized = sanitized
            .Replace(" ", "_")
            .Replace(".", "_")
            .Replace(",", "_")
            .Replace("!", "_")
            .Replace("?", "_");

        return string.IsNullOrWhiteSpace(sanitized) ? "special" : sanitized;
    }

    /// <summary>
    /// Get hash of content for unique identification.
    /// </summary>
    private static string GetContentHash(string content)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = sha.ComputeHash(bytes);
        return BitConverter.ToString(hash)
            .Replace("-", "")
            .Substring(0, 8)
            .ToLower();
    }
}
