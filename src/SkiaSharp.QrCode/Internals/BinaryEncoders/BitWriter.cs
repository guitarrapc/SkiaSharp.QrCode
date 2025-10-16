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

        // fast path for byte-aligned writes
        // If both current position and bitCount are byte-aligned (8bit boundary), we can write whole bytes at once
        if ((_bitPosition & 7) == 0 && (bitCount & 7) == 0)
        {
            WriteAlignedBytes(value, bitCount);
            return;
        }

        // slow path for unaligned writes
        WriteUnalignedBits(value, bitCount);
    }

    /// <summary>
    /// Writes whole bytes directly (MSB first) when both current position and bitCount are byte-aligned (8bit boundary)
    /// </summary>
    /// <param name="value"></param>
    /// <param name="bitCount"></param>
    /// <remarks>
    /// Target data is like Padding byte `0xEC, 0x11`, ECI Header (8bit), Character Count (8/16bit), etc.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteAlignedBytes(int value, int bitCount)
    {
        var byteCount = bitCount / 8;
        var byteIndex = _bitPosition / 8;

        // direct byte write (MSB first)
        for (var i = 0; i < byteCount; i++)
        {
            var byteValue = (byte)(value >> ((byteCount - 1 - i) * 8));
            _buffer[byteIndex + i] = byteValue;
        }
        _bitPosition += bitCount;
    }

    /// <summary>
    /// Writes specified number of bits from value (MSB first)
    /// </summary>
    /// <param name="value"></param>
    /// <param name="bitCount"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteUnalignedBits(int value, int bitCount)
    { 
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
