using System.Text;

namespace SkiaSharp.QrCode.Internals.TextEncoders;

internal static class TextInterleaver
{
    /// <summary>
    /// Calculates the final interleaved data capacity including data codewords, ECC codewords, and remainder bits.
    /// </summary>
    /// <param name="blocks">Array of codeword blocks to interleave.</param>
    /// <param name="eccInfo">ECC information for the QR code version.</param>
    /// <param name="version">QR code version (1-40).</param>
    /// <returns></returns>
    public static string InterleaveCodewords(List<CodewordTextBlock> blocks, in ECCInfo eccInfo, int version)
    {
        var interleaveCapacity = CalculateInterleavedDataCapacity(eccInfo, version);
        var result = new StringBuilder(interleaveCapacity);
        var maxCodewordCount = Math.Max(eccInfo.CodewordsInGroup1, eccInfo.CodewordsInGroup2);

        // Interleave data codewords
        for (var i = 0; i < maxCodewordCount; i++)
        {
            foreach (var codeBlock in blocks)
            {
                if (codeBlock.CodeWords.Count > i)
                {
                    result.Append(codeBlock.CodeWords[i]);
                }
            }
        }
        // Interleave ECC codewords
        for (var i = 0; i < eccInfo.ECCPerBlock; i++)
        {
            foreach (var codeBlock in blocks)
            {
                if (codeBlock.EccWords.Count > i)
                {
                    result.Append(codeBlock.EccWords[i]);
                }
            }
        }
        // Add remainder bits
        var remainderBitsCount = QRCodeConstants.GetRemainderBits(version);
        if (remainderBitsCount > 0)
        {
            result.Append('0', remainderBitsCount);
        }

        return result.ToString();
    }

    /// <summary>
    /// Calculates the required interleaved data capacity in bits.
    /// </summary>
    /// <param name="eccInfo">ECC information for the QR code version.</param>
    /// <param name="version">QR code version (1-40).</param>
    /// <returns></returns>
    private static int CalculateInterleavedDataCapacity(in ECCInfo eccInfo, int version)
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
        // var dataCodewordsBits = 80 * 8 = 640 bits
        // var eccCodewordsBits = 18 * (2 + 2) * 8 = 576 bits
        // var remainderBits = GetRemainderBits(5) = 0 bits  // Version 5 has no module remainder bits
        //
        // Total = 640 + 576 + 0 = 1,216 bits (152 bytes)
        // -----------------------------------------------------

        var dataCodewordsBits = eccInfo.TotalDataCodewords * 8;
        var eccCodewordsBits = eccInfo.ECCPerBlock
            * (eccInfo.BlocksInGroup1 + eccInfo.BlocksInGroup2)
            * 8;
        var remainderBits = QRCodeConstants.GetRemainderBits(version);
        return dataCodewordsBits + eccCodewordsBits + remainderBits;
    }
}
