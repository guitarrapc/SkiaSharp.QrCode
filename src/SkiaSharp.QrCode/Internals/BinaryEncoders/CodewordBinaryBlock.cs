namespace SkiaSharp.QrCode.Internals.BinaryEncoders;

/// <summary>
/// Represents a codeword block in the interleaving process.
/// QR codes split data into multiple blocks for error correction.
/// </summary>
internal readonly struct CodewordBinaryBlock
{
    public int GroupNumber { get; }
    public int BlockNumber { get; }
    public ReadOnlyMemory<byte> DataBytes { get; }
    public ReadOnlyMemory<byte> EccWords { get; }

    public CodewordBinaryBlock(int groupNumber, int blockNumber, ReadOnlyMemory<byte> dataBytes, ReadOnlyMemory<byte> eccWords)
    {
        GroupNumber = groupNumber;
        BlockNumber = blockNumber;
        DataBytes = dataBytes;
        EccWords = eccWords;
    }
}
