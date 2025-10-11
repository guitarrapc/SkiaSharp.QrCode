using System.Runtime.CompilerServices;

namespace SkiaSharp.QrCode.Internals.BinaryEncoders;

/// <summary>
/// Reads bits from a byte buffer with precise bit-level control.
/// Used for QR code data decoding/placement.
/// </summary>
internal ref struct BitReader
{
    private ReadOnlySpan<byte> _data;
    private int _bitPosition;

    /// <summary>
    /// Current bit position in the buffer.
    /// </summary>
    public int BitPosition => _bitPosition;

    /// <summary>
    /// Check if there are more bits to read
    /// </summary>
    public bool HasBits => _bitPosition < _data.Length * 8;

    public BitReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _bitPosition = 0;
    }

    /// <summary>
    /// Read a single bit from the buffer
    /// </summary>
    /// <param name="value"></param>
    /// <param name="bitCount"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Read()
    {
        // --------------------------------------------
        // if data is [0b10110000] & bit position is 0
        // --------------------------------------------
        // position => 0
        // byte => 0b10110000
        // bitoffset => 7
        // 1 << bitOffset(7) => 0b10000000
        // AND => 0b10000000 != 0 => true
        // --------------------------------------------
        // if data is [0b10110000] & bit position is 1
        // --------------------------------------------
        // position => 1
        // byte => 0b10110000
        // bitoffset => 6
        // 1 << bitOffset(6) => 0b01000000
        // AND => 0b00000000 != 0 => false
        // --------------------------------------------

        var byteIndex = _bitPosition / 8;
        var bitOffset = 7 - _bitPosition % 8; // 7 - (...) because we read MSB first
        var bit = (_data[byteIndex] & 1 << bitOffset) != 0;
        _bitPosition++;
        return bit;
    }

    /// <summary>
    /// Read multiple bits from the buffer and return as an integer
    /// </summary>
    /// <param name="bitCount"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Reads(int bitCount)
    {
        if (bitCount < 1 || bitCount > 32)
            throw new ArgumentOutOfRangeException(nameof(bitCount), "bitCount must be between 1 and 32");
        int result = 0;
        for (var i = 0; i < bitCount; i++)
        {
            result = result << 1 | (Read() ? 1 : 0);
        }
        return result;
    }
}
