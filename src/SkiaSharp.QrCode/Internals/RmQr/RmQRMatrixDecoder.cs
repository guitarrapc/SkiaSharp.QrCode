using SkiaSharp.QrCode.Internals.BinaryDecoders;

namespace SkiaSharp.QrCode.Internals.RmQr;

/// <summary>
/// rMQR matrix → payload (ISO/IEC 23941, the inverse of the encode pipeline): the
/// version comes from the physical width × height, the two format-information
/// copies name the ECC level (only copies naming that version count, the closer
/// wins), then inverse zigzag + fixed unmask (reusing
/// the placer's own predicate and mask so both sides always agree), block
/// deinterleave, per-block Reed-Solomon correction (full RS strength ⌊ecc/2⌋ per
/// block, as the Standard QR decoder), and the bit-stream decode. Allocation-free:
/// fixed stack budgets sized by the largest version.
/// </summary>
/// <remarks>
/// Misdecode-protection codewords: ISO/IEC 18004 reserves p codewords for Micro QR
/// so decoders must correct fewer than ⌊ecc/2⌋ errors; whether ISO/IEC 23941 does
/// the same for rMQR could not be confirmed from the specification text (not
/// available in this repository). This decoder mirrors the Standard QR decoder
/// (no cap); revisit when the specification text or a reader-conformance oracle
/// settles the question (recorded in the spec map).
/// </remarks>
internal static class RmQRMatrixDecoder
{
    internal const int MaxTotalCodewords = 232;  // R17x139
    internal const int MaxDataCodewords = 152;   // R17x139-M
    internal const int MaxBlockCodewords = 74;  // R15x59-M: 48 data + 26 ECC in one block (pinned by RmQRCodeDecoderRoundTripTest)

    public static QRCodeDecodeStatus DecodeMatrix(ReadOnlySpan<byte> modules, int width, int height, Span<char> destination, out int charsWritten, out RmQRCodeDecodeInfo info)
    {
        charsWritten = 0;

        // 1. Version from the physical dimensions
        if (!RmQRConstants.TryGetVersion(height, width, out var version) || modules.Length < width * height)
        {
            info = new RmQRCodeDecodeInfo(QRCodeDecodeStatus.InvalidMatrix, 0, default, 0);
            return QRCodeDecodeStatus.InvalidMatrix;
        }

        // 2. Format information (both copies) → ECC; only copies that agree with the
        //    dimension-derived version count, so a copy miscorrected toward another
        //    version's word cannot veto the valid one.
        ReadFormatCopies(modules, width, height, out var finderSideRaw, out var subFinderSideRaw);
        if (!RmQRFormatInformationDecoder.TryDecode(finderSideRaw, subFinderSideRaw, version, out var eccLevel, out _))
        {
            info = new RmQRCodeDecodeInfo(QRCodeDecodeStatus.FormatInformationInvalid, version, default, 0);
            return QRCodeDecodeStatus.FormatInformationInvalid;
        }

        var eccInfo = RmQRConstants.GetEccInfo(version, eccLevel);
        var totalCodewords = RmQRConstants.GetTotalCodewordCount(version);
        var blocks = eccInfo.BlocksInGroup1 + eccInfo.BlocksInGroup2;

        // 3. Extract the interleaved codeword stream (inverse zigzag + unmask)
        Span<byte> stream = stackalloc byte[MaxTotalCodewords];
        stream = stream.Slice(0, totalCodewords);
        stream.Clear();
        ExtractCodewords(modules, width, height, version, stream);

        // 4. Deinterleave block by block, correct, and collect the data codewords
        Span<byte> block = stackalloc byte[MaxBlockCodewords];
        Span<byte> data = stackalloc byte[MaxDataCodewords];
        data = data.Slice(0, eccInfo.TotalDataCodewords);
        var errorsCorrected = 0;
        var dataOffset = 0;
        for (var b = 0; b < blocks; b++)
        {
            var dataLength = b < eccInfo.BlocksInGroup1 ? eccInfo.CodewordsInGroup1 : eccInfo.CodewordsInGroup2;
            var blockSpan = block.Slice(0, dataLength + eccInfo.ECCPerBlock);

            // Data codewords: round-robin rows across blocks; the extra codeword of the
            // long blocks follows all short-length rows.
            for (var k = 0; k < dataLength; k++)
            {
                var index = k < eccInfo.CodewordsInGroup1
                    ? k * blocks + b
                    : eccInfo.CodewordsInGroup1 * blocks + (b - eccInfo.BlocksInGroup1);
                blockSpan[k] = stream[index];
            }
            // ECC codewords: round-robin after all data.
            for (var e = 0; e < eccInfo.ECCPerBlock; e++)
            {
                blockSpan[dataLength + e] = stream[eccInfo.TotalDataCodewords + e * blocks + b];
            }

            if (!EccBinaryDecoder.TryCorrect(blockSpan, eccInfo.ECCPerBlock, out var blockErrors))
            {
                info = new RmQRCodeDecodeInfo(QRCodeDecodeStatus.DataUncorrectable, version, eccLevel, errorsCorrected + blockErrors);
                return QRCodeDecodeStatus.DataUncorrectable;
            }
            errorsCorrected += blockErrors;

            blockSpan.Slice(0, dataLength).CopyTo(data.Slice(dataOffset, dataLength));
            dataOffset += dataLength;
        }

        // 5. Bit stream → text
        var status = RmQRBinaryDecoder.DecodeBitStream(data, eccInfo.TotalDataCodewords * 8, version, destination, out charsWritten);
        info = new RmQRCodeDecodeInfo(status, version, eccLevel, errorsCorrected);
        return status;
    }

    /// <summary>
    /// Upper bound on decoded characters for a version across ECC levels and modes:
    /// numeric packs 3 digits into 10 bits, so one data codeword (8 bits) yields at
    /// most 2.4 characters; 3× the M-level data codewords is a safe bound.
    /// </summary>
    public static int GetMaxCharCount(RmQRVersion version)
        => RmQRConstants.GetDataCodewordCount(version, RmQREccLevel.M) * 3;

    /// <summary>Reads both 18-bit copies (positions mirror <see cref="RmQRModulePlacer.PlaceFormat"/> exactly).</summary>
    private static void ReadFormatCopies(ReadOnlySpan<byte> modules, int width, int height, out int finderSide, out int subFinderSide)
    {
        finderSide = 0;
        subFinderSide = 0;
        for (var c = 0; c < 3; c++)
        {
            for (var r = 0; r < 5; r++)
            {
                var bit = c * 5 + r;
                if (modules[(r + 1) * width + (c + 8)] != 0)
                    finderSide |= 1 << bit;
                if (modules[(height - 6 + r) * width + (width - 8 + c)] != 0)
                    subFinderSide |= 1 << bit;
            }
        }
        for (var k = 0; k < 3; k++)
        {
            if (modules[(k + 1) * width + 11] != 0)
                finderSide |= 1 << (15 + k);
            if (modules[(height - 6) * width + (width - 5 + k)] != 0)
                subFinderSide |= 1 << (15 + k);
        }
    }

    /// <summary>
    /// Inverse of <see cref="RmQRModulePlacer.PlaceData"/>: the same walk, unmasked
    /// on the fly; bits beyond the codeword stream (remainder) are ignored.
    /// </summary>
    private static void ExtractCodewords(ReadOnlySpan<byte> modules, int width, int height, RmQRVersion version, Span<byte> stream)
    {
        var totalBits = stream.Length * 8;
        var bitIndex = 0;
        var upward = true;
        for (var col = width - 2; col >= 1; col -= 2)
        {
            for (var step = 0; step < height; step++)
            {
                var row = upward ? height - 1 - step : step;
                var rowOffset = row * width;
                for (var c = col; c >= col - 1; c--)
                {
                    if (RmQRModulePlacer.IsFunctionModule(version, row, c))
                        continue;
                    if (bitIndex >= totalBits)
                        return;

                    var dark = modules[rowOffset + c] != 0;
                    if (RmQRModulePlacer.GetMaskBit(row, c))
                        dark = !dark;
                    if (dark)
                        stream[bitIndex >> 3] |= (byte)(0x80 >> (bitIndex & 7));
                    bitIndex++;
                }
            }
            upward = !upward;
        }
    }
}
