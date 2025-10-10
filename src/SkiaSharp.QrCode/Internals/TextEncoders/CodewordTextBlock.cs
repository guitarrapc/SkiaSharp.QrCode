namespace SkiaSharp.QrCode.Internals.TextEncoders;

/// <summary>
/// Represents a codeword block in the interleaving process.
/// QR codes split data into multiple blocks for error correction.
/// </summary>
internal readonly record struct CodewordTextBlock(int GroupNumber, int BlockNumber, string BitString, IReadOnlyList<string> CodeWords, IReadOnlyList<string> ECCWords);
