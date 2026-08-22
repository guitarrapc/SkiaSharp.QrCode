using System.Diagnostics;

namespace SkiaSharp.QrCode.Internals.RmQr;

/// <summary>
/// One encoding-mode run of a planned rMQR bit stream: a contiguous slice of the
/// source text plus the value its character count indicator carries. Deliberately
/// small (8 bytes) because plans live in a caller-lent <see cref="Span{T}"/> on the
/// stack, and deliberately without any reference so it can never own storage.
/// </summary>
internal readonly struct RmQRSegment
{
    /// <summary>Character offset of the run in the source text.</summary>
    public readonly ushort Start;

    /// <summary>Character length of the run.</summary>
    public readonly ushort Length;

    /// <summary>
    /// Character count indicator value: digits for Numeric, characters for
    /// Alphanumeric, and the *encoded byte count* for Byte (Latin-1 = characters,
    /// UTF-8 = UTF-8 length). Kept alongside <see cref="Length"/> so the planner
    /// and the encoder agree on the bit budget without recomputing it.
    /// </summary>
    public readonly ushort UnitCount;

    /// <summary>Dense mode index (<see cref="RmQRConstants.GetModeIndex"/>): 0 Numeric, 1 Alphanumeric, 2 Byte.</summary>
    public readonly byte ModeIndex;

    public RmQRSegment(int modeIndex, int start, int length, int unitCount)
    {
        // The fields are packed to keep a plan on the stack, so the ranges the packing
        // assumes are asserted rather than left to a silent truncation.
        Debug.Assert((uint)modeIndex <= 2, "mode index must be Numeric, Alphanumeric or Byte");
        Debug.Assert((uint)start <= ushort.MaxValue && (uint)length <= ushort.MaxValue && (uint)unitCount <= ushort.MaxValue);
        ModeIndex = (byte)modeIndex;
        Start = (ushort)start;
        Length = (ushort)length;
        UnitCount = (ushort)unitCount;
    }

    public EncodingMode Mode => ModeIndex switch
    {
        0 => EncodingMode.Numeric,
        1 => EncodingMode.Alphanumeric,
        _ => EncodingMode.Byte,
    };
}
