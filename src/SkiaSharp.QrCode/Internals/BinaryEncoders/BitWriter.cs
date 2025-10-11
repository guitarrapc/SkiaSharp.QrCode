using System.Runtime.CompilerServices;

namespace SkiaSharp.QrCode.Internals.BinaryEncoders;

/// <summary>
/// Writes bit to a byte buffer with precise bit-level control.
/// Used for QR code data encoding.
/// </summary>
internal ref struct BitWriter
{
    private Span<byte> _buffer;
    private int _bitPosition;

    /// <summary>
    /// Current bit position in the buffer.
    /// </summary>
    public int BitPosition => _bitPosition;

    /// <summary>
    /// Number of bytes written (rounded up to nearest byte)
    /// </summary>
    /// <remarks>
    /// If written 9 bits, this will be 2
    /// </remarks>
    public int ByteCount => (_bitPosition + 7) / 8;

    public BitWriter(Span<byte> buffer)
    {
        _buffer = buffer;
        _bitPosition = 0;
        buffer.Clear(); // 0 initialization
    }

    /// <summary>
    /// Writes specified number of bits from value (LSB first)
    /// </summary>
    /// <param name="value"></param>
    /// <param name="bitCount"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(int value, int bitCount)
    {
        // --------------------------------------------
        // if Write(0b101, 3) & bit position is 0
        // --------------------------------------------
        // step 1: (i = 2), (bit = (0b101 >> 2) & 1 => 1), (byteIndex = 0), (bitIndex = 7 - (0 % 8) => 7), (_buffer[0] after write => 10000000), (_bitPosition = 1)
        // step 2: (i = 1), (bit = (0b101 >> 1) & 1 => 0), (byteIndex = 0), (bitIndex = 7 - (1 % 8) => 6), (_buffer[0] after write => 10000000), (_bitPosition = 2)
        // step 3: (i = 0), (bit = (0b101 >> 0) & 1 => 1), (byteIndex = 0), (bitIndex = 7 - (2 % 8) => 5), (_buffer[0] after write => 10100000), (_bitPosition = 3)
        // Result: _buffer[0] = 0b10100000
        // --------------------------------------------

        if (_bitPosition + bitCount > _buffer.Length * 8)
            throw new InvalidOperationException($"Buffer overflow: trying to write {bitCount} bits at position {_bitPosition}, buffer size: {_buffer.Length * 8} bits");

        // Because QR requires writing MSB first, we need to start from the highest bit
        for (var i = bitCount - 1; i >= 0; i--)
        {
            var bit = value >> i & 1;
            var byteIndex = _bitPosition / 8;
            var bitIndex = 7 - _bitPosition % 8; // 7 - (...) because we read MSB first
            if (bit == 1)
            {
                _buffer[byteIndex] |= (byte)(1 << bitIndex);
            }

            _bitPosition++;
        }
    }

    /// <summary>
    /// Gets the written data as a read-only span
    /// </summary>
    /// <returns></returns>
    public ReadOnlySpan<byte> GetData() => _buffer.Slice(0, ByteCount);
}
