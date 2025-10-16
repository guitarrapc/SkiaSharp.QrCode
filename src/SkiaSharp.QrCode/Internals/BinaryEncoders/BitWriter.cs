using System.Runtime.CompilerServices;

namespace SkiaSharp.QrCode.Internals.BinaryEncoders;

/// <summary>
/// Writes bit to a byte buffer with precise bit-level control.
/// Used for QR code data encoding.
/// </summary>
internal ref struct BitWriter
{
    private Span<byte> _buffer;
    private int _bytePosition;
    private byte _accumulator;
    private int _accumulatorBits;

    /// <summary>
    /// Current bit position in the buffer.
    /// </summary>
    public int BitPosition => _bytePosition * 8 + _accumulatorBits;

    /// <summary>
    /// Number of bytes written (rounded up to nearest byte)
    /// </summary>
    /// <remarks>
    /// If written 9 bits, this will be 2
    /// </remarks>
    public int ByteCount => _bytePosition + (_accumulatorBits > 0 ? 1 : 0);

    public BitWriter(Span<byte> buffer)
    {
        _buffer = buffer;
        _bytePosition = 0;
        _accumulator = 0;
        _accumulatorBits = 0;
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

        if (bitCount < 1 || bitCount > 32)
            throw new ArgumentOutOfRangeException(nameof(bitCount), "bitCount must be between 1 and 32");

        if (BitPosition + bitCount > _buffer.Length * 8)
            throw new InvalidOperationException($"Buffer overflow: trying to write {bitCount} bits at position {BitPosition}, buffer size: {_buffer.Length * 8} bits");

        // 16 bits over, split into two writes
        if (bitCount > 16)
        {
            var highBits = bitCount - 16;
            Write(value >> 16, highBits);
            Write(value & 0xFFFF, 16);
            return;
        }

        // 8 bits over and accumulator is empty, split into two writes
        if (bitCount > 8 && _accumulatorBits == 0)
        {
            var highBits = bitCount - 8;
            WriteInternal(value >> 8, highBits);
            WriteInternal(value & 0xFF, 8);
        }
        else
        {
            WriteInternal(value, bitCount);
        }

        // if there are remaining bits in the accumulator, flush them to the buffer
        if (_accumulatorBits > 0)
        {
            _buffer[_bytePosition] = _accumulator;
        }
    }

    /// <summary>
    /// Internal write logic that assumes bitCount <= 16 and fits in the buffer
    /// </summary>
    /// <param name="value"></param>
    /// <param name="bitCount"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteInternal(int value, int bitCount)
    {
        var remainingBits = bitCount;
        while (remainingBits > 0)
        {
            // decide how many bits to write in this iteration
            var bitsToWrite = Math.Min(remainingBits, 8 - _accumulatorBits);

            // extract the bits to write (from MSB)
            var shift = remainingBits - bitsToWrite;
            var mask = (1 << bitsToWrite) - 1;
            var bits = (value >> shift) & mask;

            // add to accumulator
            _accumulator |= (byte)(bits << (8 - _accumulatorBits - bitsToWrite));
            _accumulatorBits += bitsToWrite;
            remainingBits -= bitsToWrite;

            // if accumulator is full, write to buffer
            if (_accumulatorBits == 8)
            {
                _buffer[_bytePosition++] = _accumulator;
                _accumulator = 0;
                _accumulatorBits = 0;
            }
        }
    }

    /// <summary>
    /// Gets the written data as a read-only span
    /// </summary>
    /// <returns></returns>
    public ReadOnlySpan<byte> GetData() => _buffer[..ByteCount];
}
