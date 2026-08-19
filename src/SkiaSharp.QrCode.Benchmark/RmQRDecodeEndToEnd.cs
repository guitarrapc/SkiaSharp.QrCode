/// <summary>
/// End-to-end rMQR matrix decoding through the public API (RmQRCodeDecoder):
/// module matrix (no quiet zone) → text. Baseline for the reference-shaped decoder;
/// span-destination variants must stay allocation-free.
///
/// Scenarios (same payloads as RmQREncodeEndToEnd):
///   Numeric_R7x43_M      : smallest symbol, single RS block
///   Alphanumeric_R11x59_M: mid symbol, single block
///   Byte_R17x139_M       : largest symbol, 4 RS blocks
///   *_Corrected          : the same symbols with correctable module damage, so the
///                          Berlekamp-Massey/Chien/Forney correction path runs rather
///                          than syndrome generation alone (the clean cases exit early)
/// </summary>
public class RmQRDecodeEndToEnd
{
    private byte[] _numericModules = default!;
    private byte[] _alphanumericModules = default!;
    private byte[] _byteModules = default!;
    private (int Width, int Height) _numericSize;
    private (int Width, int Height) _alphanumericSize;
    private (int Width, int Height) _byteSize;
    private byte[] _numericDamagedModules = default!;
    private byte[] _byteDamagedModules = default!;
    private byte[] _standardModules = default!;
    private int _standardSize;
    private char[] _chars = default!;
    private char[] _standardChars = default!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        (_numericModules, _numericSize) = Build("012345678901", RmQREccLevel.M, RmQRVersion.R7x43);
        (_alphanumericModules, _alphanumericSize) = Build("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 $%*+-.", RmQREccLevel.M, RmQRVersion.R11x59);
        (_byteModules, _byteSize) = Build(string.Concat(Enumerable.Repeat("the quick brown fox jumps over the lazy dog?! ", 4)).Substring(0, 150), RmQREccLevel.M, RmQRVersion.R17x139);
        _chars = new char[RmQRCodeDecoder.GetMaxDecodedLength(RmQRVersion.R17x139)];

        // Correctable damage: flip a few modules and keep only a corruption the decoder
        // still recovers, so the measurement covers correction rather than failure.
        _numericDamagedModules = Damage(_numericModules, _numericSize, flips: 2, seed: 17);
        _byteDamagedModules = Damage(_byteModules, _byteSize, flips: 6, seed: 23);

        var calculated = SkiaSharp.QrCode.QRCodeGenerator.GetRequiredBufferSize("012345678901", ECCLevel.L, quietZoneSize: 0);
        _standardModules = new byte[calculated.BufferSize];
        SkiaSharp.QrCode.QRCodeGenerator.CreateQrCode("012345678901", ECCLevel.L, _standardModules, quietZoneSize: 0);
        _standardSize = calculated.QrSize;
        _standardChars = new char[QRCodeDecoder.GetMaxDecodedLength(1)];
    }

    // Span destination (zero-allocation) variants

    [Benchmark(Baseline = true, Description = "RmQR_Numeric_R7x43_Decode (Span)")]
    public int RmQR_Numeric_R7x43_DecodeSpan()
    {
        RmQRCodeDecoder.TryDecode(_numericModules, _numericSize.Width, _numericSize.Height, _chars, out var written, out _);
        return written;
    }

    [Benchmark(Description = "RmQR_Alphanumeric_R11x59_Decode (Span)")]
    public int RmQR_Alphanumeric_R11x59_DecodeSpan()
    {
        RmQRCodeDecoder.TryDecode(_alphanumericModules, _alphanumericSize.Width, _alphanumericSize.Height, _chars, out var written, out _);
        return written;
    }

    [Benchmark(Description = "RmQR_Byte_R17x139_Decode (Span)")]
    public int RmQR_Byte_R17x139_DecodeSpan()
    {
        RmQRCodeDecoder.TryDecode(_byteModules, _byteSize.Width, _byteSize.Height, _chars, out var written, out _);
        return written;
    }

    // String-returning variants (allocate the result string only)

    [Benchmark]
    public string RmQR_Numeric_R7x43_Decode()
    {
        RmQRCodeDecoder.TryDecode(_numericModules, _numericSize.Width, _numericSize.Height, out var text, out _);
        return text;
    }

    [Benchmark]
    public string RmQR_Byte_R17x139_Decode()
    {
        RmQRCodeDecoder.TryDecode(_byteModules, _byteSize.Width, _byteSize.Height, out var text, out _);
        return text;
    }

    // Correctable damage: same symbols, modules flipped within RS capacity.

    [Benchmark(Description = "RmQR_Numeric_R7x43_Corrected_Decode (Span)")]
    public int RmQR_Numeric_R7x43_CorrectedDecodeSpan()
    {
        RmQRCodeDecoder.TryDecode(_numericDamagedModules, _numericSize.Width, _numericSize.Height, _chars, out var written, out _);
        return written;
    }

    [Benchmark(Description = "RmQR_Byte_R17x139_Corrected_Decode (Span)")]
    public int RmQR_Byte_R17x139_CorrectedDecodeSpan()
    {
        RmQRCodeDecoder.TryDecode(_byteDamagedModules, _byteSize.Width, _byteSize.Height, _chars, out var written, out _);
        return written;
    }

    // Standard QR version 1 with the same numeric payload, for scale reference.

    [Benchmark(Description = "StandardQr_Numeric_V1_Decode (Span)")]
    public int StandardQr_Numeric_V1_DecodeSpan()
    {
        QRCodeDecoder.TryDecode(_standardModules, _standardSize, _standardChars, out var written, out _);
        return written;
    }

    /// <summary>
    /// Flips <paramref name="flips"/> modules and verifies the symbol still decodes to
    /// the same text, so the scenario measures error correction and not the failure path.
    /// </summary>
    private static byte[] Damage(byte[] modules, (int Width, int Height) size, int flips, int seed)
    {
        RmQRCodeDecoder.TryDecode(modules, size.Width, size.Height, out var expected, out _);

        for (var attempt = 0; attempt < 64; attempt++)
        {
            var random = new Random(seed + attempt);
            var damaged = (byte[])modules.Clone();
            for (var i = 0; i < flips; i++)
            {
                var index = random.Next(damaged.Length);
                damaged[index] ^= 1;
            }

            if (RmQRCodeDecoder.TryDecode(damaged, size.Width, size.Height, out var text, out _) && text == expected)
                return damaged;
        }

        throw new InvalidOperationException($"No correctable {flips}-flip damage found for {size.Width}x{size.Height}.");
    }

    private static (byte[] modules, (int Width, int Height) size) Build(string content, RmQREccLevel eccLevel, RmQRVersion version)
    {
        var calculated = RmQRCodeGenerator.GetRequiredBufferSize(content.AsSpan(), eccLevel, version, quietZoneSize: 0);
        var buffer = new byte[calculated.BufferSize];
        RmQRCodeGenerator.CreateRmQRCode(content.AsSpan(), eccLevel, buffer, version, quietZoneSize: 0);
        return (buffer, (calculated.Width, calculated.Height));
    }
}
