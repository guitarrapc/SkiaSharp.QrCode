using System.Text;
using Xunit;
using SkiaSharp.QrCode.Internals;
using static SkiaSharp.QrCode.QRCodeGenerator;

namespace SkiaSharp.QrCode.Tests;

// NOTE: Actual QR Code size is always same for same version and ECC level,
//
// Version 2, ECC-L, Numeric mode (Minimum data for 42 digits)
// Total capacity: 44 bytes = 352 bits
// ┌──────────────────────────────────────────┐
// │ 1. Mode indicator: 4 bits                │
// │ 2. Character count: 10 bits              │
// │ 3. Data (42 digits): ~140 bits           │
// │ 4. Terminator: 4 bits                    │
// │ 5. Byte alignment: 0-7 bits              │
// │ 6. Padding: ~190 bits (0xEC 0x11...)     │
// │ 7. ECC: Fixed length                     │
// ├──────────────────────────────────────────┤
// │ Total: 352 bits (44 bytes)               │
// │ Matrix size: 25 × 25 (Version 2)         │
// │ With quiet zone: 33 × 33                │
// └──────────────────────────────────────────┘
//
// Version 2, ECC-L, Numeric mode (Maximum data for 77 digits)
// Total capacity: 44 bytes = 352 bits (Same as minimum data for same version)
//
// ┌──────────────────────────────────────────┐
// │ 1. Mode indicator: 4 bits                │
// │ 2. Character count: 10 bits              │
// │ 3. Data (77 digits): ~257 bits           │
// │ 4. Terminator: 4 bits                    │
// │ 5. Byte alignment: 0-7 bits              │
// │ 6. Padding: ~70 bits (0xEC 0x11...)      │
// │ 7. ECC: Fixed length                     │
// ├──────────────────────────────────────────┤
// │ Total: 352 bits (44 bytes)               │
// │ Matrix size: 25 × 25 (Version 2)         │
// │ With quiet zone: 33 × 33                 │
// └──────────────────────────────────────────┘

public class QRCodeGeneratorUnitTest
{
    [Theory]
    [InlineData(0)]
    [InlineData(41)]
    internal void CalculateMaxBitStringLength_InvalidVersionShouldFail(int version)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => CalculateMaxBitStringLength(version, ECCLevel.L, EncodingMode.Alphanumeric));
        Assert.Contains($"Version must be 1-40, but was {version}", ex.Message);
    }

    [Theory]
    [InlineData(1, ECCLevel.L, EncodingMode.Numeric, 152)]         // 19 × 8
    [InlineData(1, ECCLevel.M, EncodingMode.Alphanumeric, 128)]    // 16 × 8
    [InlineData(1, ECCLevel.Q, EncodingMode.Byte, 104)]            // 13 × 8
    [InlineData(1, ECCLevel.H, EncodingMode.Byte, 72)]             // 9 × 8
    [InlineData(40, ECCLevel.L, EncodingMode.Numeric, 23648)]      // 2956 × 8
    [InlineData(40, ECCLevel.M, EncodingMode.Alphanumeric, 18672)] // 2334 × 8
    [InlineData(40, ECCLevel.Q, EncodingMode.Byte, 13328)]         // 1666 × 8
    [InlineData(40, ECCLevel.H, EncodingMode.Byte, 10208)]         // 1276 × 8
    internal void CalculateMaxBitStringLength_ReturnsCapacityWithBuffer(int version, ECCLevel eccLevel, EncodingMode encoding, int expected)
    {
        var actual = CalculateMaxBitStringLength(version, eccLevel, encoding);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CalculateMaxBitStringLength_IsIndependentOfInputText()
    {
        // Capacity doesn't depend on input text (always padded to full capacity)
        var capacity1 = CalculateMaxBitStringLength(1, ECCLevel.L, EncodingMode.Byte);
        var capacity2 = CalculateMaxBitStringLength(1, ECCLevel.L, EncodingMode.Byte);

        Assert.Equal(capacity1, capacity2);
    }

    // Encoding Mode Tests - Numeric

    // QR Code Data Encoding Overview
    // ┌─────────────────────────────────────────────────────────┐
    // │ Version 1, ECC Level H, Numeric Mode                    │
    // │ Total Capacity (ECCInfo.TotalDataCodewords)             │
    // │ = 9 bytes = 72 bits                                     │
    // ├─────────────────────────────────────────────────────────┤
    // │ 1. Mode indicator (Numeric)                             │
    // │    = 4 bits (0001)                                      │
    // ├─────────────────────────────────────────────────────────┤
    // │ 2. Character count indicator (Version 1-9)              │
    // │    = 10 bits (max 1023 digits)                          │
    // ├─────────────────────────────────────────────────────────┤
    // │ 3. Data (17 digits)                                     │
    // │    = 5 groups × 10 bits + 1 group × 4 bits              │
    // │    = 50 + 4 = 54 bits                                   │
    // │    (3 digits per group: 000-999 → 10 bits)              │
    // │    (Last 2 digits: 00-99 → 7 bits, but padded to 4)     │
    // ├─────────────────────────────────────────────────────────┤
    // │ 4. Terminator                                           │
    // │    = min(72 - 4 - 10 - 54, 4) = 4 bits (0000)           │
    // ├─────────────────────────────────────────────────────────┤
    // │ Total: 4 + 10 + 54 + 4 = 72 bits                        │
    // └─────────────────────────────────────────────────────────┘


    [Theory]
    [InlineData("0123456789", ECCLevel.M)]
    [InlineData("123456789012345678901234567890123", ECCLevel.M)]
    public void CreateQrCode_Numeric_ProducesValidQr(string text, ECCLevel eccLevel)
    {
        var qr = QRCodeGenerator.CreateQrCode(text, eccLevel);

        Assert.True(qr.Size >= 21); // Min size = 21x21
    }

    [Theory]
    [InlineData(ECCLevel.L, 41, 1)]   // V1-L max
    [InlineData(ECCLevel.M, 34, 1)]   // V1-M max
    [InlineData(ECCLevel.H, 17, 1)]   // V1-H max
    [InlineData(ECCLevel.L, 42, 2)]   // V1-L max + 1 → should upgrade to V2
    [InlineData(ECCLevel.M, 35, 2)]   // V1-M max + 1 → should upgrade to V2
    [InlineData(ECCLevel.H, 18, 2)]   // V1-H max + 1 → should upgrade to V2
    [InlineData(ECCLevel.L, 77, 2)]   // V2-L max
    [InlineData(ECCLevel.M, 48, 2)]   // V2-M max
    [InlineData(ECCLevel.H, 34, 2)]   // V2-H max
    public void CreateQrCode_Numeric_Versions_MaxCapacity(ECCLevel eccLevel, int maxChars, int expectedVersion)
    {
        var expectedSize = CalculateSize(expectedVersion);

        var text = new string('1', maxChars);

        var qr = QRCodeGenerator.CreateQrCode(text, eccLevel);
        var version = CalculateVersion(qr.Size);
        var actualSize = qr.Size;

        Assert.Equal(expectedVersion, version);
        Assert.Equal(expectedSize, actualSize);
    }

    // Encoding Mode Tests - Alphanumeric (ASCII)

    // QR Code Data Encoding Overview
    // ┌─────────────────────────────────────────────────────────┐
    // │ Version 1, ECC Level H, Alphanumeric Mode               │
    // │ Total Capacity (ECCInfo.TotalDataCodewords)             │
    // │ = 9 bytes = 72 bits                                     │
    // ├─────────────────────────────────────────────────────────┤
    // │ 1. Mode indicator (Alphanumeric)                        │
    // │    = 4 bits (0010)                                      │
    // ├─────────────────────────────────────────────────────────┤
    // │ 2. Character count indicator (Version 1-9)              │
    // │    = 9 bits (max 511 chars)                             │
    // ├─────────────────────────────────────────────────────────┤
    // │ 3. Data (10 chars)                                      │
    // │    = 5 groups × 11 bits = 55 bits                       │
    // │    (2 chars per group: 00-44 → 11 bits)                 │
    // ├─────────────────────────────────────────────────────────┤
    // │ 4. Terminator                                           │
    // │    = min(72 - 4 - 9 - 55, 4) = 4 bits (0000)            │
    // ├─────────────────────────────────────────────────────────┤
    // │ Total: 4 + 9 + 55 + 4 = 72 bits                         │
    // └─────────────────────────────────────────────────────────┘

    [Theory]
    [InlineData("HELLO WORLD", ECCLevel.M)]
    [InlineData("ABC-123 $%*+-./:", ECCLevel.Q)]
    public void CreateQrCode_Alphanumeric_ProducesValidQr(string text, ECCLevel eccLevel)
    {
        var qr = QRCodeGenerator.CreateQrCode(text, eccLevel);

        Assert.True(qr.Size >= 21);
    }

    [Theory]
    [InlineData(ECCLevel.L, 25, 1)]   // V1-L max
    [InlineData(ECCLevel.M, 20, 1)]   // V1-M max
    [InlineData(ECCLevel.H, 10, 1)]   // V1-H max
    [InlineData(ECCLevel.L, 26, 2)]   // V1-L max + 1 → should upgrade to V2
    [InlineData(ECCLevel.M, 21, 2)]   // V1-M max + 1 → should upgrade to V2
    [InlineData(ECCLevel.H, 11, 2)]   // V1-H max + 1 → should upgrade to V2
    [InlineData(ECCLevel.L, 47, 2)]   // V2-L max
    [InlineData(ECCLevel.M, 38, 2)]   // V2-M max
    [InlineData(ECCLevel.H, 20, 2)]   // V2-H max
    public void CreateQrCode_Alphanumeric_Versions_MaxCapacity(ECCLevel eccLevel, int maxChars, int expectedVersion)
    {
        var expectedSize = CalculateSize(expectedVersion);

        var text = new string('A', maxChars);

        var qr = QRCodeGenerator.CreateQrCode(text, eccLevel);
        var version = CalculateVersion(qr.Size);
        var actualSize = qr.Size;

        Assert.Equal(expectedVersion, version);
        Assert.Equal(expectedSize, actualSize);
    }

    // Encoding Mode Test - Byte (UTF-8)

    // QR Code Data Encoding Overview
    // ┌─────────────────────────────────────────────────────────┐
    // │ Version 1, ECC Level H, Byte Mode (with UTF-8 ECI)      │
    // │ Total Capacity (ECCInfo.TotalDataCodewords)             │
    // │ = 9 bytes = 72 bits                                     │
    // ├─────────────────────────────────────────────────────────┤
    // │ 1. ECI header (UTF-8)                                   │
    // │    = 4 bits (0111) + 8 bits (26 = UTF-8) = 12 bits      │
    // ├─────────────────────────────────────────────────────────┤
    // │ 2. Mode indicator (Byte)                                │
    // │    = 4 bits (0100)                                      │
    // ├─────────────────────────────────────────────────────────┤
    // │ 3. Character count indicator (Version 1-9)              │
    // │    = 8 bits (max 255 bytes)                             │
    // ├─────────────────────────────────────────────────────────┤
    // │ 4. Data (7 bytes ASCII = "aaaaaaa")                     │
    // │    = 7 bytes × 8 bits = 56 bits                         │
    // ├─────────────────────────────────────────────────────────┤
    // │ 5. Terminator                                           │
    // │    = min(72 - 12 - 4 - 8 - 56, 4) = 0 bits (full)       │
    // ├─────────────────────────────────────────────────────────┤
    // │ Total: 12 + 4 + 8 + 56 + 0 = 80 bits                    │
    // │ !! Exceeds 72 bits → Upgrades to Version 2              │
    // │                                                         │
    // │ Without ECI header (EciMode.Default):                   │
    // │ Total: 4 + 8 + 56 + 4 = 72 bits                         │
    // └─────────────────────────────────────────────────────────┘

    [Theory]
    [InlineData("hello world", EciMode.Utf8)]
    [InlineData("こんにちは", EciMode.Utf8)]
    [InlineData("🎉", EciMode.Utf8)]
    public void CreateQrCode_Utf8_ProducesValidQr(string text, EciMode eciMode)
    {
        var qr = QRCodeGenerator.CreateQrCode(text, ECCLevel.M, eciMode: eciMode);

        Assert.True(qr.Size >= 21);
    }

    [Theory]
    [InlineData(ECCLevel.L, 16, 1)]   // V1-L: 16 bytes → V1 (21+8=29)
    [InlineData(ECCLevel.M, 13, 1)]   // V1-M: 13 bytes → V1 (21+8=29)
    [InlineData(ECCLevel.H, 6, 1)]    // V1-H: 6 bytes → V1 (21+8=29)
    [InlineData(ECCLevel.L, 17, 2)]   // V1-L max + 1 → should upgrade to V2
    [InlineData(ECCLevel.M, 14, 2)]   // V1-M max + 1 → should upgrade to V2
    [InlineData(ECCLevel.H, 7, 2)]   // V1-H max + 1 → should upgrade to V2
    [InlineData(ECCLevel.L, 31, 2)]   // V2-L max
    [InlineData(ECCLevel.M, 25, 2)]   // V2-M max
    [InlineData(ECCLevel.H, 13, 2)]   // V2-H max
    public void CreateQrCode_Utf8_Version1MaxCapacity(ECCLevel eccLevel, int maxBytes, int expectedVersion)
    {
        var expectedSize = CalculateSize(expectedVersion);

        var text = new string('a', maxBytes); // ASCII = 1 byte each

        var qr = QRCodeGenerator.CreateQrCode(text, eccLevel, eciMode: EciMode.Utf8);
        var version = CalculateVersion(qr.Size);
        var actualSize = qr.Size;

        Assert.Equal(expectedVersion, version);
        Assert.Equal(expectedSize, actualSize);
    }

    // ECI Mode Tests

    [Theory]
    [InlineData("Café", EciMode.Iso8859_1)]
    [InlineData("Café", EciMode.Utf8)]
    public void CreateQrCode_DifferentEci_ProducesValidQr(string text, EciMode eciMode)
    {
        var qr = QRCodeGenerator.CreateQrCode(text, ECCLevel.M, eciMode: eciMode);

        Assert.True(qr.Size > 0);
    }

    [Fact]
    public void CreateQrCode_DifferentEci_ProducesDifferentQr()
    {
        var qrDefault = QRCodeGenerator.CreateQrCode("HELLO", ECCLevel.M, eciMode: EciMode.Default);
        var qrIso = QRCodeGenerator.CreateQrCode("HELLO", ECCLevel.M, eciMode: EciMode.Iso8859_1);
        var qrUtf8 = QRCodeGenerator.CreateQrCode("HELLO", ECCLevel.M, eciMode: EciMode.Utf8);

        // Different ECI headers → different QR codes
        Assert.NotEqual(SerializeMatrix(qrDefault), SerializeMatrix(qrIso));
        Assert.NotEqual(SerializeMatrix(qrIso), SerializeMatrix(qrUtf8));
        Assert.NotEqual(SerializeMatrix(qrDefault), SerializeMatrix(qrUtf8));
    }

    [Theory]
    [InlineData("Zürich", ECCLevel.H, EciMode.Utf8, 2)]  // 修正後: Version 2
    [InlineData("Zürich", ECCLevel.M, EciMode.Utf8, 1)]  // Version 1 で OK
    [InlineData("Zürich", ECCLevel.L, EciMode.Utf8, 1)]  // Version 1 で OK
    [InlineData("café", ECCLevel.H, EciMode.Utf8, 1)]    // Version 1 で OK
    public void CreateQrCode_VersionSelection_IsCorrect(string content, ECCLevel eccLevel, EciMode eciMode, int expectedVersion)
    {
        var qr = QRCodeGenerator.CreateQrCode(content, eccLevel, eciMode: eciMode);

        Assert.Equal(expectedVersion, qr.Version);
    }

    // UTF-8 BOM Tests

    [Theory]
    [InlineData("hello", ECCLevel.H, true, 2)]   // With BOM: 24 bits overhead → Version 2
    [InlineData("hello", ECCLevel.H, false, 1)]  // Without BOM → Version 1
    [InlineData("test", ECCLevel.M, true, 1)]    // With BOM but still fits in Version 1
    [InlineData("test", ECCLevel.M, false, 1)]   // Without BOM → Version 1
    public void CreateQrCode_Utf8BOM_VersionSelection(string text, ECCLevel eccLevel, bool utf8BOM, int expectedVersion)
    {
        var qr = QRCodeGenerator.CreateQrCode(text.AsSpan(), eccLevel, utf8BOM: utf8BOM, eciMode: EciMode.Utf8);

        Assert.Equal(expectedVersion, qr.Version);
    }

    [Fact]
    public void CreateQrCode_Utf8BOM_ProducesDifferentQr()
    {
        var qrWithBOM = QRCodeGenerator.CreateQrCode("こんにちは".AsSpan(), ECCLevel.M, utf8BOM: true, eciMode: EciMode.Utf8);
        var qrWithoutBOM = QRCodeGenerator.CreateQrCode("こんにちは".AsSpan(), ECCLevel.M, utf8BOM: false, eciMode: EciMode.Utf8);

        // UTF-8 BOM adds 24 bits → different QR codes
        Assert.NotEqual(SerializeMatrix(qrWithBOM), SerializeMatrix(qrWithoutBOM));
    }

    [Theory]
    [InlineData(ECCLevel.H, 6, false, 1)]    // V1-H: 6 bytes without BOM → V1
    [InlineData(ECCLevel.H, 7, false, 2)]    // V1-H: 7 bytes without BOM → V2 (exceeds capacity)
    [InlineData(ECCLevel.H, 3, true, 1)]    // V1-H: 3 bytes with BOM (3 + 3 BOM = 6 bytes) → V1
    [InlineData(ECCLevel.H, 4, true, 2)]    // V1-H: 4 bytes with BOM (4 + 3 BOM = 7 bytes) → V2
    public void CreateQrCode_Utf8BOM_MaxCapacity(ECCLevel eccLevel, int textLength, bool utf8BOM, int expectedVersion)
    {
        var text = new string('a', textLength);

        var qr = QRCodeGenerator.CreateQrCode(text.AsSpan(), eccLevel, utf8BOM: utf8BOM, eciMode: EciMode.Utf8);

        Assert.Equal(expectedVersion, qr.Version);
    }

    [Theory]
    [InlineData("HELLO", ECCLevel.M, true)]
    [InlineData("こんにちは", ECCLevel.Q, true)]
    [InlineData("🎉", ECCLevel.H, true)]
    public void CreateQrCode_Span_Utf8BOM_MatchesStringImplementation(string text, ECCLevel level, bool utf8BOM)
    {
        var qrString = QRCodeGenerator.CreateQrCode(text, level, utf8BOM: utf8BOM, eciMode: EciMode.Utf8);
        var qrSpan = QRCodeGenerator.CreateQrCode(text.AsSpan(), level, utf8BOM: utf8BOM, eciMode: EciMode.Utf8);

        // Compare sizes
        Assert.Equal(qrString.Size, qrSpan.Size);

        // Compare every module
        for (int y = 0; y < qrString.Size; y++)
        {
            for (int x = 0; x < qrString.Size; x++)
            {
                Assert.Equal(qrString[y, x], qrSpan[y, x]);
            }
        }
    }

    // Maximum Capacity Tests

    [Theory]
    [InlineData(ECCLevel.L, 7089)]  // V40-L numeric max
    [InlineData(ECCLevel.H, 3057)]  // V40-H numeric max
    public void CreateQrCode_MaxNumeric_FitsInVersion40(ECCLevel eccLevel, int maxChars)
    {
        var text = new string('1', maxChars);

        var qr = QRCodeGenerator.CreateQrCode(text, eccLevel);
        var version = CalculateVersion(qr.Size);

        Assert.Equal(40, version);
    }

    [Theory]
    [InlineData(ECCLevel.L, 7089)]  // V40-L numeric max
    [InlineData(ECCLevel.H, 3057)]  // V40-H numeric max
    public void CreateQrCode_Span_MaxNumeric_FitsInVersion40(ECCLevel eccLevel, int maxChars)
    {
        var text = new string('1', maxChars);

        var qr = QRCodeGenerator.CreateQrCode(text.AsSpan(), eccLevel);
        var version = CalculateVersion(qr.Size);

        Assert.Equal(40, version);
    }

    [Theory]
    [InlineData(ECCLevel.L, 4296)]  // V40-L alphanumeric max
    [InlineData(ECCLevel.H, 1852)]  // V40-H alphanumeric max
    public void CreateQrCode_MaxAlphanumeric_FitsInVersion40(ECCLevel eccLevel, int maxChars)
    {
        var text = new string('A', maxChars);

        var qr = QRCodeGenerator.CreateQrCode(text, eccLevel);
        var version = CalculateVersion(qr.Size);

        Assert.Equal(40, version);
    }

    [Theory]
    [InlineData(ECCLevel.L, 4296)]  // V40-L alphanumeric max
    [InlineData(ECCLevel.H, 1852)]  // V40-H alphanumeric max
    public void CreateQrCode_Span_MaxAlphanumeric_FitsInVersion40(ECCLevel eccLevel, int maxChars)
    {
        var text = new string('A', maxChars);

        var qr = QRCodeGenerator.CreateQrCode(text.AsSpan(), eccLevel);
        var version = CalculateVersion(qr.Size);

        Assert.Equal(40, version);
    }

    [Theory]
    [InlineData(ECCLevel.L, 984)]  // V40-L byte max
    [InlineData(ECCLevel.H, 424)]  // V40-H byte max
    public void CreateQrCode_MaxByte_FitsInVersion40(ECCLevel eccLevel, int maxChars)
    {
        var text = new string('あ', maxChars);

        var qr = QRCodeGenerator.CreateQrCode(text, eccLevel);
        var version = CalculateVersion(qr.Size);

        Assert.Equal(40, version);
    }

    [Theory]
    [InlineData(ECCLevel.L, 984)]  // V40-L byte max
    [InlineData(ECCLevel.H, 424)]  // V40-H byte max
    public void CreateQrCode_Span_MaxByte_FitsInVersion40(ECCLevel eccLevel, int maxChars)
    {
        var text = new string('あ', maxChars);

        var qr = QRCodeGenerator.CreateQrCode(text.AsSpan(), eccLevel);
        var version = CalculateVersion(qr.Size);

        Assert.Equal(40, version);
    }

    [Fact]
    public void CreateQrCode_ExceedsMaxCapacity_Throws()
    {
        var tooLarge = new string('1', 7090); // V40-L max + 1

        Assert.Throws<InvalidOperationException>(() => QRCodeGenerator.CreateQrCode(tooLarge, ECCLevel.L));
    }

    // Consistency Tests

    [Fact]
    public void CreateQrCode_SameInput_ProducesSameOutput()
    {
        var qr1 = QRCodeGenerator.CreateQrCode("HELLO WORLD", ECCLevel.M);
        var qr2 = QRCodeGenerator.CreateQrCode("HELLO WORLD", ECCLevel.M);

        Assert.Equal(SerializeMatrix(qr1), SerializeMatrix(qr2));
    }

    // Text and Binary consistency

    [Theory]
    [InlineData("HELLO WORLD", ECCLevel.Q, 1)]
    [InlineData("https://example.com", ECCLevel.M, -1)]
    [InlineData("0123456789", ECCLevel.H, 5)]
    public void CreateQrCodeBinary_MatchesTextImplementation(string text, ECCLevel level, int version)
    {
        var qrText = QRCodeGenerator.CreateQrCode(text, level, requestedVersion: version);
        var qrBinary = QRCodeGenerator.CreateQrCode(text.AsSpan(), level, requestedVersion: version);

        // Compare sizes
        Assert.Equal(qrText.Size, qrBinary.Size);

        // Compare every module
        for (int y = 0; y < qrText.Size; y++)
        {
            for (int x = 0; x < qrText.Size; x++)
            {
                Assert.Equal(qrText[y, x], qrBinary[y, x]);
            }
        }
    }

    // Helpers

    /// <summary>
    /// Calculates QR code version from module matrix size.
    /// Handles quiet zone automatically.
    /// </summary>
    /// <param name="matrixSize">Total size of module matrix (including quiet zone).</param>
    /// <param name="quietZoneSize">Size of quiet zone in modules.</param>
    /// <returns>QR code version (1-40).</returns>
    private static int CalculateVersion(int matrixSize, int quietZoneSize = 4)
    {
        // Remove quiet zone
        var sizeWithoutQuietZone = matrixSize - (quietZoneSize * 2);

        // Formula: size = 21 + (version - 1) * 4
        // Inverse: version = (size - 21) / 4 + 1
        return (sizeWithoutQuietZone - 21) / 4 + 1;
    }

    /// <summary>
    /// Calculate QR code size from version.
    /// </summary>
    /// <param name="version"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    private static int CalculateSize(int version, int quietZoneSize = 4)
    {
        if (version is < 1 or > 40)
            throw new ArgumentOutOfRangeException(nameof(version), $"Version must be -1 (auto) or 1-40, got {version}");

        // Formula: size = 21 + (version - 1) * 4
        var sizeWithoutQuietSone = 21 + (version - 1) * 4;
        return sizeWithoutQuietSone + (quietZoneSize * 2);
    }

    private static string SerializeMatrix(QRCodeData qrCode)
    {
        var size = qrCode.Size;
        var sb = new StringBuilder(size * size);
        for (var row = 0; row < size; row++)
        {
            for (var col = 0; col < size; col++)
            {
                sb.Append(qrCode[row, col] ? '1' : '0');
            }
        }
        return sb.ToString();
    }
}
