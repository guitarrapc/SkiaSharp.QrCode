using Xunit;

namespace SkiaSharp.QrCode.Tests;

/// <summary>
/// Encode → decode round-trip tests: every QR produced by <see cref="QRCodeGenerator"/>
/// must decode back to the original text with the library's own decoder.
/// </summary>
public class QRCodeDecoderRoundTripTest
{
    [Theory]
    [InlineData("0123456789", ECCLevel.L)]
    [InlineData("0123456789012345678901234567890123456789", ECCLevel.H)]
    [InlineData("1", ECCLevel.M)]
    [InlineData("12", ECCLevel.Q)]
    public void RoundTrip_Numeric(string content, ECCLevel eccLevel)
        => AssertRoundTrip(content, eccLevel);

    [Theory]
    [InlineData("HELLO WORLD", ECCLevel.L)]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:", ECCLevel.M)]
    [InlineData("A", ECCLevel.Q)]
    [InlineData("AC-42", ECCLevel.H)]
    public void RoundTrip_Alphanumeric(string content, ECCLevel eccLevel)
        => AssertRoundTrip(content, eccLevel);

    [Theory]
    [InlineData("Hello, World!", ECCLevel.L)]
    [InlineData("hello lowercase", ECCLevel.M)]
    [InlineData("https://example.com/path?query=value&x=1", ECCLevel.Q)]
    [InlineData("Café Zürich Résumé", ECCLevel.H)]
    public void RoundTrip_Byte(string content, ECCLevel eccLevel)
        => AssertRoundTrip(content, eccLevel);

    [Theory]
    [InlineData("こんにちは世界", ECCLevel.L)]
    [InlineData("你好世界", ECCLevel.M)]
    [InlineData("Привет мир", ECCLevel.Q)]
    [InlineData("🎉🎊🎈 emoji", ECCLevel.H)]
    public void RoundTrip_Utf8(string content, ECCLevel eccLevel)
        => AssertRoundTrip(content, eccLevel, EciMode.Utf8);

    [Theory]
    [InlineData("Café", ECCLevel.L)]
    [InlineData("Zürich", ECCLevel.M)]
    [InlineData("Naïve Résumé", ECCLevel.H)]
    public void RoundTrip_Iso8859_1_Eci(string content, ECCLevel eccLevel)
        => AssertRoundTrip(content, eccLevel, EciMode.Iso8859_1);

    [Fact]
    public void RoundTrip_Utf8WithBom()
    {
        var content = "BOM roundtrip 日本語";
        var qr = QRCodeGenerator.CreateQrCode(content, ECCLevel.M, utf8BOM: true, eciMode: EciMode.Utf8);

        Assert.True(QRCodeDecoder.TryDecode(qr, out var decoded, out var info));
        Assert.Equal(content, decoded);
        Assert.Equal(QRCodeDecodeStatus.Success, info.Status);
    }

    [Fact]
    public void RoundTrip_AllVersions()
    {
        // Force every version; content sized to fit version 1 at ECC H
        var content = "V17!";
        for (var version = 1; version <= 40; version++)
        {
            foreach (var eccLevel in new[] { ECCLevel.L, ECCLevel.M, ECCLevel.Q, ECCLevel.H })
            {
                var qr = QRCodeGenerator.CreateQrCode(content, eccLevel, requestedVersion: version);

                Assert.True(QRCodeDecoder.TryDecode(qr, out var decoded, out var info), $"version={version}, ecc={eccLevel}, status={info.Status}");
                Assert.Equal(content, decoded);
                Assert.Equal(version, info.Version);
                Assert.Equal(eccLevel, info.EccLevel);
                Assert.InRange(info.MaskPattern, 0, 7);
                Assert.Equal(0, info.ErrorsCorrected);
            }
        }
    }

    [Fact]
    public void RoundTrip_VariousContents_CoversMultipleMaskPatterns()
    {
        // Mask selection depends on content; a spread of contents exercises
        // several of the 8 patterns through the decoder's unmasking path.
        var observedMasks = new HashSet<int>();
        for (var i = 0; i < 64; i++)
        {
            var content = $"mask-coverage-{i}-{new string((char)('a' + i % 26), i % 7 + 1)}";
            var qr = QRCodeGenerator.CreateQrCode(content, ECCLevel.M);

            Assert.True(QRCodeDecoder.TryDecode(qr, out var decoded, out var info));
            Assert.Equal(content, decoded);
            observedMasks.Add(info.MaskPattern);
        }

        // Not all 8 are guaranteed, but a healthy spread proves per-pattern unmasking
        Assert.True(observedMasks.Count >= 4, $"expected >= 4 distinct masks, observed: {string.Join(", ", observedMasks)}");
    }

    [Fact]
    public void RoundTrip_SpanMatrix_WithQuietZone()
    {
        var content = "span with quiet zone";
        var calculated = QRCodeGenerator.GetRequiredBufferSize(content, ECCLevel.M);
        var buffer = new byte[calculated.BufferSize];
        var written = QRCodeGenerator.CreateQrCode(content, ECCLevel.M, buffer);

        Assert.True(QRCodeDecoder.TryDecode(buffer.AsSpan(0, written), calculated.QrSize, out var decoded, out var info));
        Assert.Equal(content, decoded);
        Assert.Equal(QRCodeDecodeStatus.Success, info.Status);
    }

    [Fact]
    public void RoundTrip_SpanMatrix_WithoutQuietZone()
    {
        var content = "span without quiet zone";
        var calculated = QRCodeGenerator.GetRequiredBufferSize(content, ECCLevel.M, quietZoneSize: 0);
        var buffer = new byte[calculated.BufferSize];
        var written = QRCodeGenerator.CreateQrCode(content, ECCLevel.M, buffer, quietZoneSize: 0);

        Assert.True(QRCodeDecoder.TryDecode(buffer.AsSpan(0, written), calculated.QrSize, out var decoded, out _));
        Assert.Equal(content, decoded);
    }

    [Fact]
    public void RoundTrip_CharSpanDestination_NoStringAllocation()
    {
        var content = "char span destination";
        var calculated = QRCodeGenerator.GetRequiredBufferSize(content, ECCLevel.M, quietZoneSize: 0);
        var buffer = new byte[calculated.BufferSize];
        QRCodeGenerator.CreateQrCode(content, ECCLevel.M, buffer, quietZoneSize: 0);

        Span<char> destination = stackalloc char[QRCodeDecoder.GetMaxDecodedLength(calculated.Version)];
        Assert.True(QRCodeDecoder.TryDecode(buffer.AsSpan(0, calculated.BufferSize), calculated.QrSize, destination, out var charsWritten, out _));
        Assert.Equal(content, destination.Slice(0, charsWritten).ToString());
    }

#if !DEBUG
    [Fact]
    public void Decode_CharSpanDestination_IsAllocationFree()
    {
        // Steady-state decode (span in, span out, no quiet zone) must not allocate.
        // Debug builds heap-allocate stackalloc initializers (see repo notes), so
        // this assertion is Release-only.
        var content = "0123456789";
        var calculated = QRCodeGenerator.GetRequiredBufferSize(content, ECCLevel.M, quietZoneSize: 0);
        var buffer = new byte[calculated.BufferSize];
        QRCodeGenerator.CreateQrCode(content, ECCLevel.M, buffer, quietZoneSize: 0);
        var destination = new char[QRCodeDecoder.GetMaxDecodedLength(calculated.Version)];

        // Warm-up: blocked-mask cache, ArrayPool buckets, JIT
        for (var i = 0; i < 3; i++)
        {
            QRCodeDecoder.TryDecode(buffer.AsSpan(0, calculated.BufferSize), calculated.QrSize, destination, out _, out _);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 16; i++)
        {
            QRCodeDecoder.TryDecode(buffer.AsSpan(0, calculated.BufferSize), calculated.QrSize, destination, out _, out _);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }
#endif

    [Fact]
    public void Decode_AllLightMatrix_ReturnsInvalidMatrix()
    {
        var modules = new byte[25 * 25];

        Assert.False(QRCodeDecoder.TryDecode(modules, 25, out var text, out var info));
        Assert.Equal(string.Empty, text);
        Assert.Equal(QRCodeDecodeStatus.InvalidMatrix, info.Status);
    }

    [Fact]
    public void Decode_InvalidSize_ReturnsInvalidMatrix()
    {
        // 20x20 all-dark: bounding box is 20, not a valid QR dimension
        var modules = new byte[20 * 20];
        modules.AsSpan().Fill(1);

        Assert.False(QRCodeDecoder.TryDecode(modules, 20, out _, out var info));
        Assert.Equal(QRCodeDecodeStatus.InvalidMatrix, info.Status);
    }

    [Fact]
    public void Decode_CorruptedFormatInformation_ReturnsFormatInvalid()
    {
        var qr = QRCodeGenerator.CreateQrCode("format corruption", ECCLevel.M, quietZoneSize: 0);
        var size = qr.Size;
        var modules = new byte[size * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                modules[y * size + x] = qr[y, x] ? (byte)1 : (byte)0;
            }
        }

        // Find a 15-bit pattern with Hamming distance > 3 from every valid masked
        // format pattern, then write it into both format copies. BCH(15,5) corrects
        // at most 3 bit errors, so this must be rejected.
        var farPattern = FindPatternFarFromAllFormats();
        WriteFormatPattern(modules, size, farPattern);

        Assert.False(QRCodeDecoder.TryDecode(modules, size, out _, out var info));
        Assert.Equal(QRCodeDecodeStatus.FormatInformationInvalid, info.Status);
    }

    private static ushort FindPatternFarFromAllFormats()
    {
        for (var pattern = 0; pattern < 0x8000; pattern++)
        {
            var minDistance = int.MaxValue;
            for (var level = 0; level < 4; level++)
            {
                for (var mask = 0; mask < 8; mask++)
                {
                    var candidate = Internals.QRCodeConstants.GetFormatBits((ECCLevel)level, mask);
                    var distance = CountBits((ushort)(pattern ^ candidate));
                    if (distance < minDistance)
                        minDistance = distance;
                }
            }
            if (minDistance > 3)
                return (ushort)pattern;
        }
        throw new InvalidOperationException("no pattern found beyond correction distance");

        static int CountBits(ushort v)
        {
            var count = 0;
            while (v != 0)
            {
                count += v & 1;
                v >>= 1;
            }
            return count;
        }
    }

    private static void WriteFormatPattern(byte[] modules, int size, ushort pattern)
    {
        // Same positions as ModulePlacer.PlaceFormat (bit i, LSB first)
        var positions = new (int x1, int y1, int x2, int y2)[]
        {
            ( 8, 0, size - 1, 8 ),
            ( 8, 1, size - 2, 8 ),
            ( 8, 2, size - 3, 8 ),
            ( 8, 3, size - 4, 8 ),
            ( 8, 4, size - 5, 8 ),
            ( 8, 5, size - 6, 8 ),
            ( 8, 7, size - 7, 8 ),
            ( 8, 8, size - 8, 8 ),
            ( 7, 8, 8, size - 7 ),
            ( 5, 8, 8, size - 6 ),
            ( 4, 8, 8, size - 5 ),
            ( 3, 8, 8, size - 4 ),
            ( 2, 8, 8, size - 3 ),
            ( 1, 8, 8, size - 2 ),
            ( 0, 8, 8, size - 1 ),
        };

        for (var i = 0; i < 15; i++)
        {
            var bit = (byte)((pattern >> i) & 1);
            var (x1, y1, x2, y2) = positions[i];
            modules[y1 * size + x1] = bit;
            modules[y2 * size + x2] = bit;
        }
    }

    [Fact]
    public void Decode_FewFlippedModules_IsCorrectedByEcc()
    {
        // Version 1-M has a single block with 10 ECC codewords → corrects 5 codewords.
        var content = "ECCFIX";
        var qr = QRCodeGenerator.CreateQrCode(content, ECCLevel.M, requestedVersion: 1, quietZoneSize: 0);
        var size = qr.Size;
        var modules = new byte[size * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                modules[y * size + x] = qr[y, x] ? (byte)1 : (byte)0;
            }
        }

        // Flip 3 modules inside the data region (away from function patterns:
        // rows 9-12, columns 9-12 are data area in version 1).
        modules[9 * size + 9] ^= 1;
        modules[10 * size + 11] ^= 1;
        modules[12 * size + 10] ^= 1;

        Assert.True(QRCodeDecoder.TryDecode(modules, size, out var decoded, out var info));
        Assert.Equal(content, decoded);
        Assert.True(info.ErrorsCorrected > 0, "expected ECC to correct at least one codeword");
    }

    [Fact]
    public void Decode_HeavilyCorruptedData_ReturnsDataUncorrectable()
    {
        // Version 1-L corrects only 3 codewords; flipping a large scattered set
        // of data modules must exceed capacity and fail (not misdecode).
        var qr = QRCodeGenerator.CreateQrCode("FAIL", ECCLevel.L, requestedVersion: 1, quietZoneSize: 0);
        var size = qr.Size;
        var modules = new byte[size * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                modules[y * size + x] = qr[y, x] ? (byte)1 : (byte)0;
            }
        }

        for (var y = 9; y <= 12; y++)
        {
            for (var x = 0; x < size; x++)
            {
                if (x == 6)
                    continue; // timing column
                modules[y * size + x] ^= 1;
            }
        }

        Assert.False(QRCodeDecoder.TryDecode(modules, size, out _, out var info));
        Assert.Equal(QRCodeDecodeStatus.DataUncorrectable, info.Status);
    }

    [Fact]
    public void TryDecode_NullData_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => QRCodeDecoder.TryDecode((QRCodeData)null!, out _));
    }

    [Fact]
    public void GetMaxDecodedLength_InvalidVersion_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => QRCodeDecoder.GetMaxDecodedLength(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => QRCodeDecoder.GetMaxDecodedLength(41));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(10)]
    public void RoundTrip_QuietZoneSizes(int quietZoneSize)
    {
        var content = "quiet zone variations";
        var qr = QRCodeGenerator.CreateQrCode(content, ECCLevel.M, quietZoneSize: quietZoneSize);

        Assert.True(QRCodeDecoder.TryDecode(qr, out var decoded, out _));
        Assert.Equal(content, decoded);
    }

    private static void AssertRoundTrip(string content, ECCLevel eccLevel, EciMode eciMode = EciMode.Default)
    {
        var qr = QRCodeGenerator.CreateQrCode(content, eccLevel, eciMode: eciMode);

        Assert.True(QRCodeDecoder.TryDecode(qr, out var decoded, out var info), $"decode failed: status={info.Status}, version={info.Version}");
        Assert.Equal(content, decoded);
        Assert.Equal(QRCodeDecodeStatus.Success, info.Status);
        Assert.Equal(eccLevel, info.EccLevel);
    }
}
