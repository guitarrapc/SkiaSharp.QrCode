using static SkiaSharp.QrCode.Internals.QRCodeConstants;

namespace SkiaSharp.QrCode.Internals.BinaryEncoders;

internal static class BinaryInterleaver
{
    /// <summary>
    /// Calculates the final interleaved data capacity including data codewords, ECC codewords, and remainder bits.
    /// </summary>
    /// <param name="data">Array of data codewords to interleave.</param>
    /// <param name="ecc">Array of ECC codewords to interleave.</param>
    /// <param name="output">Output buffer for interleaved bits.</param>
    /// <param name="version">QR code version (1-40).</param>
    /// <param name="eccInfo">ECC information for the QR code version.</param>
    /// <returns></returns>
    public static void InterleaveCodewords(ReadOnlySpan<byte> data, ReadOnlySpan<byte> ecc, Span<byte> output, int version, in ECCInfo eccInfo)
    {
        var outputIndex = 0;
        var totalBlocks = eccInfo.BlocksInGroup1 + eccInfo.BlocksInGroup2;
        var maxCodewordCount = Math.Max(eccInfo.CodewordsInGroup1, eccInfo.CodewordsInGroup2);

        // Interleave data codewords
        for (var i = 0; i < maxCodewordCount; i++)
        {
            // Group 1 blocks
            for (var blockIndex = 0; blockIndex < eccInfo.BlocksInGroup1; blockIndex++)
            {
                if (i < eccInfo.CodewordsInGroup1)
                {
                    var dataIndex = blockIndex * eccInfo.CodewordsInGroup1 + i;
                    output[outputIndex++] = data[dataIndex];
                }
            }

            // Group 2 blocks
            var group2Offset = eccInfo.BlocksInGroup1 * eccInfo.CodewordsInGroup1;
            for (var blockIndex = 0; blockIndex < eccInfo.BlocksInGroup2; blockIndex++)
            {
                if (i < eccInfo.CodewordsInGroup2)
                {
                    var dataOffset = group2Offset + blockIndex * eccInfo.CodewordsInGroup2 + i;
                    output[outputIndex++] = data[dataOffset];
                }
            }
        }

        // Interleave ECC codewords
        for (var i = 0; i < eccInfo.ECCPerBlock; i++)
        {
            for (var blockIndex = 0; blockIndex < totalBlocks; blockIndex++)
            {
                var eccOffset = blockIndex * eccInfo.ECCPerBlock;
                output[outputIndex] = ecc[eccOffset + i];
                outputIndex++;
            }
        }

        // Remainder bits are already zeroed in the output buffer. No need to explicitly write remainder bits.
    }

    /// <summary>
    /// Calculates the required buffer size for interleaved data.
    /// </summary>
    /// <param name="eccInfo">ECC information for the QR code version.</param>
    /// <param name="version">QR code version (1-40).</param>
    /// <remarks>
    /// Buffer includes:
    /// - Data codewords (TotalDataCodewords bytes)
    /// - ECC codewords (ECCPerBlock * TotalBlocks bytes)
    /// - Remainder bits space (0-7 bits, rounded up to nearlest byte)
    /// </remarks>
    /// <returns></returns>
    public static int CalculateInterleavedSize(in ECCInfo eccInfo, int version)
    {
        // -----------------------------------------------------
        // QR Code Interleaved data structure (ISO/IEC 18004):
        // -----------------------------------------------------
        //
        // ┌──────────────────────────────────────────────────┐
        // │ Interleaved Data Codewords                       │
        // │ (TotalDataCodewords × 8 bits)                    │
        // ├──────────────────────────────────────────────────┤
        // │ Interleaved ECC Codewords                        │
        // │ (ECCPerBlock × TotalBlocks × 8 bits)             │
        // ├──────────────────────────────────────────────────┤
        // │ Remainder Bits                                   │
        // │ (0-7 bits, version dependent)                    │
        // └──────────────────────────────────────────────────┘
        //
        // -----------------------------------------------------
        // // ECCInfo for Version 5, Q
        // eccInfo = {
        //     Version = 5,
        //     ErrorCorrectionLevel = Q,
        //     TotalDataCodewords = 80,       // Data capacity
        //     ECCPerBlock = 18,              // ECC Word count per block
        //     BlocksInGroup1 = 2,            // Group 1 block count
        //     CodewordsInGroup1 = 15,        // Group 1 data word count per block
        //     BlocksInGroup2 = 2,            // Group 2 block count
        //     CodewordsInGroup2 = 16,        // Group 2 data word count per block
        // };
        //
        // // Calculation:
        // var dataCodewordsBits = 80 = 80 bytes
        // var eccCodewordsBits = 18 * (2 + 2) = 72 bytes
        // var remainderBits = GetRemainderBits(5) = 0 bits  // Version 5 has no module remainder bits
        //
        // Total = 80 + 72 + 0 = 152 bytes
        // -----------------------------------------------------

        var totalDataBytes = eccInfo.TotalDataCodewords;
        var totalEccBytes = (eccInfo.BlocksInGroup1 + eccInfo.BlocksInGroup2) * eccInfo.ECCPerBlock;
        var remainderBits = GetRemainderBits(version);

        // Calculate total size in bits, then round up to bytes
        var totalBits = totalDataBytes * 8 + totalEccBytes * 8 + remainderBits;
        return (totalBits + 7) / 8;
    }

}
