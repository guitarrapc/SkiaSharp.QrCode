using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace SkiaSharp.QrCode.Internals.BinaryEncoders;

/// <summary>
/// Writes bits to a byte buffer with precise bit-level control (MSB-first).
/// Used for QR code data encoding.
/// </summary>
/// <remarks>
/// Bits are staged in a 64-bit accumulator and stored as 32-bit big-endian words
/// once available, so up to 31 bits stay pending between writes. Buffer contents
/// are only guaranteed after <see cref="Flush"/> (called by <see cref="GetData"/>
/// and <see cref="WritePadBytes"/>); <see cref="BitPosition"/> and
/// <see cref="ByteCount"/> are always valid.
/// The caller guarantees the buffer is large enough for all writes; exceeding it
/// surfaces as an out-of-range exception from the underlying span access.
/// </remarks>
internal ref struct BitWriter
{
    private Span<byte> _buffer;
    private int _bytePosition;
    private ulong _accumulator;   // pending bits stored at the top (MSB-first)
    private int _accumulatorBits; // 0..31 between writes

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
    public int ByteCount => _bytePosition + (_accumulatorBits + 7) / 8;

    public BitWriter(Span<byte> buffer)
    {
        _buffer = buffer;
        _bytePosition = 0;
        _accumulator = 0;
        _accumulatorBits = 0;
        // No upfront clear: every byte in [0, ByteCount) is stored by
        // Write/Flush/WritePadBytes, so zero-initialization is redundant.
    }

    /// <summary>
    /// Writes specified number of bits from value (MSB of the value range first).
    /// </summary>
    /// <param name="value">Value whose low <paramref name="bitCount"/> bits are written.</param>
    /// <param name="bitCount">Number of bits to write (1-32).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(int value, int bitCount)
    {
        Debug.Assert(bitCount >= 1 && bitCount <= 32, "bitCount must be between 1 and 32");

        // stage: place the value's low bitCount bits directly below the pending bits
        // headroom: _accumulatorBits <= 31, bitCount <= 32 -> at most 63 bits pending
        var v = (ulong)(uint)value & ((1UL << bitCount) - 1);
        _accumulator |= v << (64 - _accumulatorBits - bitCount);
        _accumulatorBits += bitCount;

        // store a full 32-bit word once available
        if (_accumulatorBits >= 32)
        {
            BinaryPrimitives.WriteUInt32BigEndian(_buffer.Slice(_bytePosition), (uint)(_accumulator >> 32));
            _bytePosition += 4;
            _accumulator <<= 32;
            _accumulatorBits -= 32;
        }
    }

    /// <summary>
    /// Writes 64 bits at once (bulk path for byte-mode data).
    /// </summary>
    /// <param name="value">Big-endian bit sequence: the MSB is written first.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write64(ulong value)
    {
        // merge pending bits with the head of the value and store 8 bytes;
        // the displaced tail bits stay pending (count unchanged)
        var combined = _accumulator | (value >> _accumulatorBits);
        BinaryPrimitives.WriteUInt64BigEndian(_buffer.Slice(_bytePosition), combined);
        _bytePosition += 8;
        _accumulator = _accumulatorBits == 0 ? 0UL : value << (64 - _accumulatorBits);
    }

    /// <summary>
    /// Stores all pending bits to the buffer. Full bytes advance the position;
    /// a trailing partial byte is stored (low bits zero) without advancing.
    /// Idempotent; writing may continue afterwards.
    /// </summary>
    public void Flush()
    {
        while (_accumulatorBits >= 8)
        {
            _buffer[_bytePosition++] = (byte)(_accumulator >> 56);
            _accumulator <<= 8;
            _accumulatorBits -= 8;
        }
        if (_accumulatorBits > 0)
        {
            _buffer[_bytePosition] = (byte)(_accumulator >> 56);
        }
    }

    /// <summary>
    /// Writes alternating pad bytes (0xEC, 0x11, ...) directly to the buffer.
    /// Requires the current position to be byte-aligned.
    /// </summary>
    public void WritePadBytes(int count)
    {
        // drain pending full bytes so _bytePosition is the true write position
        while (_accumulatorBits >= 8)
        {
            _buffer[_bytePosition++] = (byte)(_accumulator >> 56);
            _accumulator <<= 8;
            _accumulatorBits -= 8;
        }
        Debug.Assert(_accumulatorBits == 0, "Pad fill requires byte alignment");

        var span = _buffer.Slice(_bytePosition, count);
        for (var i = 0; i < span.Length; i++)
        {
            span[i] = (i & 1) == 0 ? (byte)0xEC : (byte)0x11;
        }
        _bytePosition += count;
    }

    /// <summary>
    /// Gets the written data as a read-only span (flushes pending bits first).
    /// </summary>
    /// <returns></returns>
    public ReadOnlySpan<byte> GetData()
    {
        Flush();
        return _buffer.Slice(0, ByteCount);
    }
}
