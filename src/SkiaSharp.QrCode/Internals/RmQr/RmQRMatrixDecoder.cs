using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
internal static partial class RmQRMatrixDecoder
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
    /// on the fly. Every byte of <paramref name="stream"/> is written; modules past
    /// the codeword stream (remainder) are ignored.
    /// </summary>
    /// <remarks>
    /// Everything the walk derives from the version alone (which modules are function
    /// modules, their order, the mask bit at each one) is hoisted into lazily built
    /// per-version tables, exactly as the placer does for the encode direction. Two
    /// tiers consume those tables: a bit-plane kernel on x64 with AVX2 and fast BMI2
    /// (see RmQRMatrixDecoder.Simd.cs) and a portable table walk everywhere else.
    /// Measured 16-64x and 8-12x respectively over the per-module reference walk
    /// across R7x43..R17x139 (see the decoder kernel parity tests for equivalence).
    /// </remarks>
    private static void ExtractCodewords(ReadOnlySpan<byte> modules, int width, int height, RmQRVersion version, Span<byte> stream)
        => ExtractCodewords(modules, width, height, version, stream, forceScalar: false);

    /// <summary>Whether the bit-plane tier runs on this machine (parity tests skip it otherwise).</summary>
    internal static bool IsBitPlaneTierSupported =>
#if NET8_0_OR_GREATER
        System.Runtime.Intrinsics.X86.Avx2.IsSupported && HardwareCapabilities.HasFastPext;
#else
        false;
#endif

    /// <summary>Kernel-selecting entry; <paramref name="forceScalar"/> pins the portable tier for parity tests.</summary>
    internal static void ExtractCodewords(ReadOnlySpan<byte> modules, int width, int height, RmQRVersion version, Span<byte> stream, bool forceScalar)
    {
        var layout = GetExtractLayout(version);
#if NET8_0_OR_GREATER
        // The bit-plane kernel emits whole 32-bit words off a per-version pair table, so
        // its output length is fixed by the version rather than by the span it is handed.
        // The portable tier reads stream.Length and truncates. Only dispatch to the
        // kernel when those two agree; every production caller sizes the span exactly.
        if (!forceScalar && IsBitPlaneTierSupported && stream.Length == RmQRConstants.GetTotalCodewordCount(version))
        {
            ExtractCodewordsBitPlanes(modules, width, height, layout.Pairs, stream);
            return;
        }
#endif
        // The portable tier reads the geometry out of the walk-order table instead.
        ExtractCodewordsScalar(modules, layout.Order, stream);
    }

    /// <summary>
    /// Portable tier: one gather per stream bit through the walk-order table, the
    /// output byte accumulated in a register so each is stored once.
    /// </summary>
    private static void ExtractCodewordsScalar(ReadOnlySpan<byte> modules, ushort[] order, Span<byte> stream)
    {
        ref var o = ref MemoryMarshal.GetReference(order.AsSpan());
        ref var src = ref MemoryMarshal.GetReference(modules);
        ref var dst = ref MemoryMarshal.GetReference(stream);
        var codewords = stream.Length;

        nint i = 0;
        for (nint k = 0; k < codewords; k++, i += 8)
        {
            ref var p = ref Unsafe.Add(ref o, i);
            var b = Bit(ref src, p) << 7
                  | Bit(ref src, Unsafe.Add(ref p, 1)) << 6
                  | Bit(ref src, Unsafe.Add(ref p, 2)) << 5
                  | Bit(ref src, Unsafe.Add(ref p, 3)) << 4
                  | Bit(ref src, Unsafe.Add(ref p, 4)) << 3
                  | Bit(ref src, Unsafe.Add(ref p, 5)) << 2
                  | Bit(ref src, Unsafe.Add(ref p, 6)) << 1
                  | Bit(ref src, Unsafe.Add(ref p, 7));
            Unsafe.Add(ref dst, k) = (byte)b;
        }

        // Modules are "0 = light, non-zero = dark", so the test is != 0, not a bit test.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int Bit(ref byte src, ushort entry)
            => (Unsafe.Add(ref src, entry & CoreIndexMask) != 0 ? 1 : 0) ^ (entry >> MaskBitShift);
    }

    // ---------------------------------------------------------------
    // Per-version extraction tables (built once from the placer's own predicates, so
    // they are correct by construction; published with a volatile write - a benign
    // race builds identical tables twice). Memory per version: 2 bytes per stream bit
    // plus 24 bytes per column pair: about 5.2 KB for R17x139 (3,712 B of order plus
    // 69 column pairs), and about 69 KB if every one of the 32 versions were decoded.
    // ---------------------------------------------------------------

    /// <summary>Low bits of an <see cref="ExtractLayout.Order"/> entry: the core module index.</summary>
    private const int CoreIndexMask = 0x7FFF;

    /// <summary>Top bit of an <see cref="ExtractLayout.Order"/> entry: the data mask at that module.</summary>
    private const int MaskBitShift = 15;

    /// <summary>
    /// Column stride between the two bit planes. The widest symbol is 139 columns and the
    /// transpose stores 16 at a time, so the last store ends at 128 + 16 = 144; rounded
    /// to 160 so writing the last column group cannot run past the first plane into the
    /// second, with margin.
    /// </summary>
    private const int PlaneStride = 160;

    /// <summary>Everything the extraction walk derives from the version alone.</summary>
    private sealed class ExtractLayout
    {
        /// <summary>One entry per stream bit, in walk order: core index | mask bit &lt;&lt; 15.</summary>
        public readonly ushort[] Order;

        /// <summary>
        /// Column-pair descriptors for the bit-plane kernel, 6 words per pair:
        /// plane index | count &lt;&lt; 16, then extract/deposit for column <c>col</c>,
        /// extract/deposit for column <c>col-1</c>, then the data mask of the pair.
        /// </summary>
        public readonly uint[] Pairs;

        public ExtractLayout(ushort[] order, uint[] pairs)
        {
            Order = order;
            Pairs = pairs;
        }
    }

    private static readonly ExtractLayout?[] extractLayouts = new ExtractLayout?[RmQRConstants.VersionCount + 1];

    private static ExtractLayout GetExtractLayout(RmQRVersion version)
    {
        ref var slot = ref extractLayouts[(int)version];
        var layout = Volatile.Read(ref slot);
        if (layout is not null) return layout;
        layout = BuildExtractLayout(version);
        Volatile.Write(ref slot, layout);
        return layout;
    }

    /// <summary>
    /// Replays the reference walk once per version and records it two ways.
    /// <para>
    /// The bit-plane form needs the column planes to be readable by PEXT: plane bit
    /// <c>row-1</c> holds row <c>row</c> for an upward pair, and plane bit
    /// <c>height-2-row</c> for a downward one (the row-reversed copy). With that
    /// layout the plane bit index and the deposit position both fall as the walk
    /// advances, so PEXT's packing order matches PDEP's scatter order and one pair of
    /// masks serves both walk directions. The walk is truncated to the codeword
    /// stream here, so neither kernel needs a remainder check.
    /// </para>
    /// </summary>
    private static ExtractLayout BuildExtractLayout(RmQRVersion version)
    {
        var height = RmQRConstants.GetHeight(version);
        var width = RmQRConstants.GetWidth(version);
        var bitCount = RmQRConstants.GetTotalCodewordCount(version) * 8;

        var order = new ushort[bitCount];
        var pairs = new List<uint>();
        var walkPositions = new List<(int Row, int Col)>();
        var walked = 0;
        var upward = true;

        for (var col = width - 2; col >= 1 && walked < bitCount; col -= 2)
        {
            walkPositions.Clear();
            for (var step = 0; step < height; step++)
            {
                var row = upward ? height - 1 - step : step;
                for (var c = col; c >= col - 1; c--)
                {
                    if (!RmQRModulePlacer.IsFunctionModule(version, row, c))
                        walkPositions.Add((row, c));
                }
            }

            var count = Math.Min(walkPositions.Count, bitCount - walked);
            uint extractRight = 0, depositRight = 0, extractLeft = 0, depositLeft = 0, mask = 0;
            for (var p = 0; p < count; p++)
            {
                var (row, c) = walkPositions[p];
                var planeBit = upward ? row - 1 : height - 2 - row;
                var depositBit = count - 1 - p; // the walk fills the pair's field MSB first
                var masked = RmQRModulePlacer.GetMaskBit(row, c);

                if (c == col)
                {
                    extractRight |= 1u << planeBit;
                    depositRight |= 1u << depositBit;
                }
                else
                {
                    extractLeft |= 1u << planeBit;
                    depositLeft |= 1u << depositBit;
                }
                if (masked)
                    mask |= 1u << depositBit;

                order[walked + p] = (ushort)((row * width + c) | (masked ? 1 << MaskBitShift : 0));
            }

            pairs.Add((uint)(col + (upward ? 0 : PlaneStride)) | ((uint)count << 16));
            pairs.Add(extractRight);
            pairs.Add(depositRight);
            pairs.Add(extractLeft);
            pairs.Add(depositLeft);
            pairs.Add(mask);

            walked += count;
            upward = !upward;
        }

        return new ExtractLayout(order, pairs.ToArray());
    }
}
