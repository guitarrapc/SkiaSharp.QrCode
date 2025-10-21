using System.Buffers;
using System.IO.Compression;
using System.Runtime.CompilerServices;

namespace SkiaSharp.QrCode;

/// <summary>
/// Represents QR code data as a 2D boolean matrix.
/// Supports serialization/deserialization with optional compression.
/// </summary>
/// <remarks>
/// QR code structure:<br/>
/// - Version: 1-40 (determines size: 21×21 to 177×177)<br/>
/// - Module matrix: 2D array of boolean values (dark/light)<br/>
/// - Serialization format: "QRR" header + size + bit-packed data<br/>
/// </remarks>
public class QRCodeData
{
    // =====================================================================
    // Memory Layout
    // =====================================================================
    //
    // QRCodeData manages a 1D byte array representing a 2D QR code matrix.
    // The array may include an optional quiet zone (white border).
    //
    // ┌─────────────────────────────────────────────────────────┐
    // │ Memory Layout (with QuietZone = 4)                      │
    // ├─────────────────────────────────────────────────────────┤
    // │ Version: 2                                              │
    // │ _baseSize: 25 (core QR modules, no quiet zone)          │
    // │ _quietZoneSize: 4 (border width)                        │
    // │ _size: 33 (25 + 4*2, including quiet zone)              │
    // │ _moduleData.Length: 1,089 bytes (33 × 33)               │
    // └─────────────────────────────────────────────────────────┘
    //
    // Visual Representation (33×33 with QuietZone=4):
    //
    //     0   1   2   3   4   5 ... 28  29  30  31  32
    //   ┌───┬───┬───┬───┬───┬───┬───┬───┬───┬───┬───┬───┐
    // 0 │ Q │ Q │ Q │ Q │ Q │ Q │...│ Q │ Q │ Q │ Q │ Q │ ← QuietZone (row 0-3)
    //   ├───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┤
    // 1 │ Q │ Q │ Q │ Q │ Q │ Q │...│ Q │ Q │ Q │ Q │ Q │
    //   ├───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┤
    // 2 │ Q │ Q │ Q │ Q │ Q │ Q │...│ Q │ Q │ Q │ Q │ Q │
    //   ├───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┤
    // 3 │ Q │ Q │ Q │ Q │ Q │ Q │...│ Q │ Q │ Q │ Q │ Q │
    //   ├───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┤
    // 4 │ Q │ Q │ Q │ Q │ C │ C │...│ C │ Q │ Q │ Q │ Q │ ← CoreData starts (col 4-28)
    //   ├───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┤
    // 5 │ Q │ Q │ Q │ Q │ C │ C │...│ C │ Q │ Q │ Q │ Q │
    //   ├───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┤
    //   │...│...│...│...│...│...│...│...│...│...│...│...│
    //   ├───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┤
    // 28│ Q │ Q │ Q │ Q │ C │ C │...│ C │ Q │ Q │ Q │ Q │ ← CoreData ends
    //   ├───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┤
    // 29│ Q │ Q │ Q │ Q │ Q │ Q │...│ Q │ Q │ Q │ Q │ Q │ ← QuietZone (row 29-32)
    //   ├───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┤
    // 30│ Q │ Q │ Q │ Q │ Q │ Q │...│ Q │ Q │ Q │ Q │ Q │
    //   ├───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┤
    // 31│ Q │ Q │ Q │ Q │ Q │ Q │...│ Q │ Q │ Q │ Q │ Q │
    //   ├───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┼───┤
    // 32│ Q │ Q │ Q │ Q │ Q │ Q │...│ Q │ Q │ Q │ Q │ Q │
    //   └───┴───┴───┴───┴───┴───┴───┴───┴───┴───┴───┴───┘
    //     ↑               ↑                       ↑       ↑
    //   col 0           col 4                   col 28  col 32
    //
    // Q = QuietZone (white border, value = 0)
    // C = CoreData (actual QR code modules)
    //
    // ┌─────────────────────────────────────────────────────────┐
    // │ 1D Array Mapping (Row-Major Order)                      │
    // ├─────────────────────────────────────────────────────────┤
    // │ Index = row × _size + col                               │
    // │                                                         │
    // │ Example: Access (row=5, col=6)                          │
    // │   → Index = 5 × 33 + 6 = 171                            │
    // │   → _moduleData[171]                                    │
    // │                                                         │
    // │ CoreData Offset Calculation:                            │
    // │   coreRow = row - _quietZoneSize                        │
    // │   coreCol = col - _quietZoneSize                        │
    // │   coreIndex = coreRow × _baseSize + coreCol             │
    // └─────────────────────────────────────────────────────────┘
    //
    // ┌─────────────────────────────────────────────────────────┐
    // │ Memory Layout (without QuietZone)                       │
    // ├─────────────────────────────────────────────────────────┤
    // │ Version: 2                                              │
    // │ _baseSize: 25                                           │
    // │ _quietZoneSize: 0                                       │
    // │ _size: 25 (same as _baseSize)                           │
    // │ _moduleData.Length: 625 bytes (25 × 25)                 │
    // └─────────────────────────────────────────────────────────┘
    //
    //     0   1   2   3   4 ... 20  21  22  23  24
    //   ┌───┬───┬───┬───┬───┬───┬───┬───┬───┬───┐
    // 0 │ C │ C │ C │ C │ C │...│ C │ C │ C │ C │ ← CoreData only
    //   ├───┼───┼───┼───┼───┼───┼───┼───┼───┼───┤
    // 1 │ C │ C │ C │ C │ C │...│ C │ C │ C │ C │
    //   ├───┼───┼───┼───┼───┼───┼───┼───┼───┼───┤
    //   │...│...│...│...│...│...│...│...│...│...│
    //   ├───┼───┼───┼───┼───┼───┼───┼───┼───┼───┤
    // 24│ C │ C │ C │ C │ C │...│ C │ C │ C │ C │
    //   └───┴───┴───┴───┴───┴───┴───┴───┴───┴───┘
    //
    // When _quietZoneSize = 0:
    //   - _size == _baseSize
    //   - No offset needed for CoreData access
    //   - Direct 1:1 mapping: _moduleData[row × _size + col]
    //
    // =====================================================================

    // =====================================================================
    // Serialization / Deserialization
    // =====================================================================
    //
    // Data Flow for Serialization/Deserialization
    //
    // ┌──────────────────────────────────────────────────┐
    // │ Serialization (GetRawData)                       │
    // ├──────────────────────────────────────────────────┤
    // │ _moduleData (with QuietZone)                     │
    // │   ↓ GetCoreData()                                │
    // │ coreData (without QuietZone)                     │
    // │   ↓ Pack bits                                    │
    // │ rawData (header + packed core bits)              │
    // └──────────────────────────────────────────────────┘
    //
    // ┌──────────────────────────────────────────────────┐
    // │ Deserialization (Constructor)                    │
    // ├──────────────────────────────────────────────────┤
    // │ rawData (header + packed core bits)              │
    // │   ↓ Unpack bits                                  │
    // │ coreData (without QuietZone)                     │
    // │   ↓ SetCoreData()                                │
    // │ _moduleData (with QuietZone)                     │
    // └──────────────────────────────────────────────────┘

    // 1D byte array (row-major order) representing the QR code modules including quiet zone if present
    // Aligned to 64-byte boundary for AVX-512 optimizations
    private byte[] _moduleData;
    private int _size; // module count per side (including quiet zone if present)
    private int _baseSize; // module count without quiet zone
    private int _quietZoneSize; // quiet zone size in modules

    /// <summary>
    /// Gets the size of the QR code matrix (modules per side).
    /// Includes quiet zone if added via <see cref="SetModuleMatrix"/>.
    /// </summary>
    public int Size => _size;

    /// <summary>
    /// Get the QR code version (1-40)
    /// </summary>
    public int Version { get; private set; }

    /// <summary>
    /// Gets or sets the module state at the specified position.
    /// </summary>
    /// <param name="row">Row index (0-based).</param>
    /// <param name="col">Column index (0-based).</param>
    /// <returns>True if module is dark/black, false if light/white.</returns>
    public bool this[int row, int col]
    {
        get => _moduleData[row * _size + col] != 0;
        internal set => _moduleData[row * _size + col] = value ? (byte)1 : (byte)0;
    }

    /// <summary>
    /// Initializes with the specified version.
    /// </summary>
    /// <param name="version">QR Code version number (1-40) used to determine matrix size</param>
    public QRCodeData(int version, int quietZoneSize)
    {
        Version = version;
        _baseSize = SizeFromVersion(version);
        _quietZoneSize = quietZoneSize;
        _size = _baseSize + (quietZoneSize * 2);
        _moduleData = new byte[_size * _size];
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="QRCodeData"/> class from serialized raw data.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This constructor deserializes QR code data that was previously serialized using <see cref="GetRawData"/>.
    /// The raw data contains only the core QR code modules (excluding quiet zone).
    /// </para>
    /// <para>
    /// Data format: "QRR" header (3 bytes) + base size (1 byte) + bit-packed module data
    /// </para>
    /// <para>
    /// The quiet zone (white border) can be added during deserialization by specifying the <paramref name="quietZoneSize"/> parameter. 
    /// </para>
    /// </remarks>
    /// <param name="rawData">The serialized QR code data. This data should be obtained from <see cref="GetRawData"/> and may be compressed based on the specified <paramref name="compressMode"/>.</param>
    /// <param name="compressMode">The compression mode used when the data was serialized. Must match the compression mode used in <see cref="GetRawData"/>.</param>
    /// <param name="quietZoneSize">
    /// The size of the quiet zone (white border) to add around the QR code matrix, in modules.
    /// This value is independent of the serialized data and can be different from the original quiet zone size used during serialization.
    /// Use 0 for no quiet zone, or typically 4 for standard QR codes.
    /// </param>
    /// <exception cref="InvalidDataException">Thrown if the decompressed data is invalid.</exception>
    /// <exception cref="InvalidOperationException">Thrown if the decompressed data does not contain enough bits to fully populate the QR code matrix.</exception>
    public QRCodeData(byte[] rawData, Compression compressMode, int quietZoneSize)
    {
        // Decompress
        var bytes = DecompressData(rawData, compressMode);

        // Validate minimum size
        if (bytes.Length < 4)
            throw new InvalidDataException($"Invalid QR code data: too short ({bytes.Length} bytes).");

        // Validate header
        if (bytes[0] != 0x51 || bytes[1] != 0x52 || bytes[2] != 0x52)
            throw new InvalidDataException("Invalid QR code data: header mismatch.");

        // Read and validate size
        var baseSizeFromFile = (int)bytes[3];
        if (baseSizeFromFile < 21 || baseSizeFromFile > 177)
            throw new InvalidDataException($"Invalid QR code size: {baseSizeFromFile}.");

        // set version from size
        Version = VersionFromSize(baseSizeFromFile);
        _baseSize = baseSizeFromFile;
        _quietZoneSize = quietZoneSize;
        _size = _baseSize + (_quietZoneSize * 2);
        var coreTotalBits = _baseSize * _baseSize; // core data size (without quiet zone)

        // Allocate full array (include quiet zone)
        _moduleData = new byte[_size * _size];

        // unpack bits to bytes
        Span<byte> coreData = stackalloc byte[coreTotalBits];
        var bitIndex = 0;
        for (var byteIndex = 4; byteIndex < bytes.Length && bitIndex < coreTotalBits; byteIndex++)
        {
            var b = bytes[byteIndex];
            for (int i = 7; i >= 0 && bitIndex < coreTotalBits; i--)
            {
                coreData[bitIndex] = (b & (1 << i)) != 0 ? (byte)1 : (byte)0;
                bitIndex++;
            }
        }

        if (bitIndex < coreTotalBits)
            throw new InvalidOperationException($"Insufficient data: expected {coreTotalBits} bits, got {bitIndex}.");

        // Copy core data to _moduleData with quiet zone offset
        SetCoreData(coreData);
    }

    /// <summary>
    /// Serializes the QR code data to a byte array with optional compression.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The serialized data contains only the core QR code modules (excluding quiet zone).
    /// The quiet zone can be added when deserializing via the 
    /// <see cref="QRCodeData(byte[], Compression, int)"/> constructor.
    /// </para>
    /// <para>
    /// Format: "QRR" header (3 bytes) + base size (1 byte) + bit-packed module data
    /// </para>
    /// </remarks>
    /// <param name="compressMode">
    /// The compression mode to apply to the serialized data.
    /// Use <see cref="Compression.Uncompressed"/> for faster serialization,
    /// or <see cref="Compression.GZip"/> / <see cref="Compression.Deflate"/> for smaller file sizes.
    /// </param>
    /// <returns>
    /// A byte array containing the serialized QR code data. This data can be stored to a file,
    /// transmitted over a network, or cached for later use.
    /// </returns>
    public byte[] GetRawData(Compression compressMode)
    {
        // "QRR"
        ReadOnlySpan<byte> _headerSignature = [0x51, 0x52, 0x52];

        // size calculations
        var totalBits = _baseSize * _baseSize; // only core data without quiet zone
        var paddingBits = GetPaddingBits(totalBits);
        var dataBytes = (totalBits + paddingBits) / 8;
        var headerSize = _headerSignature.Length + 1; // signature length + 1 byte for size
        var totalSize = headerSize + dataBytes;

        var bytes = ArrayPool<byte>.Shared.Rent(totalSize);
        try
        {
            // Write header - signature ("QRR") & raw size
            bytes[0] = _headerSignature[0];
            bytes[1] = _headerSignature[1];
            bytes[2] = _headerSignature[2];
            bytes[3] = (byte)_baseSize; // only core size without quiet zone

            // Get core data (without quiet zone)
            Span<byte> coreData = stackalloc byte[totalBits];
            GetCoreData(coreData);

            // Pack bits into bytes
            var bitIndex = 0;
            for (var byteIndex = 0; byteIndex < dataBytes; byteIndex++)
            {
                byte b = 0;
                for (var i = 7; i >= 0; i--)
                {
                    if (bitIndex < totalBits && coreData[bitIndex] != 0)
                    {
                        b |= (byte)(1 << i);
                    }
                    // else padding bits are 0
                    bitIndex++;
                }
                bytes[headerSize + byteIndex] = b;
            }

            // Compress stream
            var result = CompressData(bytes, totalSize, compressMode);

            return result;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes, clearArray: true);
        }
    }

    /// <summary>
    /// Gets the entire data as span
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ReadOnlySpan<byte> GetData() => _moduleData.AsSpan();

    /// <summary>
    /// Gets the entire data as a mutable span
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Span<byte> GetMutableData() => _moduleData.AsSpan();

    /// <summary>
    /// Gets the core data size (without quiet zone).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int GetCoreSize() => _baseSize;

    /// <summary>
    /// Copies core data (without quiet zone) to the destination buffer.
    /// </summary>
    /// <param name="destination">Destination buffer (must be at least baseSize * baseSize bytes)</param>
    /// <exception cref="ArgumentException"></exception>
    internal void GetCoreData(Span<byte> destination)
    {
        if (destination.Length < _baseSize * _baseSize)
            throw new ArgumentException($"Destination span size too small: expected at least {_baseSize * _baseSize} bytes (baseSize={_baseSize}), got {destination.Length} bytes");

        if (_quietZoneSize == 0)
        {
            // No quiet zone, copy directly
            _moduleData.AsSpan(0, _baseSize * _baseSize).CopyTo(destination);
        }
        else
        {
            // Copy core data excluding quiet zone
            for (var row = 0; row < _baseSize; row++)
            {
                var srcOffset = (row + _quietZoneSize) * _size + _quietZoneSize;
                var destOffset = row * _baseSize;
                _moduleData.AsSpan(srcOffset, _baseSize).CopyTo(destination.Slice(destOffset, _baseSize));
            }
        }
    }

    /// <summary>
    /// Sets the core data (without quiet zone) from the source buffer.
    /// </summary>
    /// <param name="source">Source buffer (must be at least baseSize * baseSize bytes)</param>
    /// <exception cref="ArgumentException"></exception>
    internal void SetCoreData(ReadOnlySpan<byte> source)
    {
        if (source.Length != _baseSize * _baseSize)
            throw new ArgumentException($"Source span size mismatch: expected {_baseSize * _baseSize} bytes (baseSize={_baseSize}), got {source.Length} bytes");

        if (_quietZoneSize == 0)
        {
            // No quiet zone, copy directly
            source.CopyTo(_moduleData);
        }
        else
        {
            // Copy core data excluding quiet zone
            for (var row = 0; row < _baseSize; row++)
            {
                var srcOffset = row * _baseSize;
                var destOffset = (row + _quietZoneSize) * _size + _quietZoneSize;
                source.Slice(srcOffset, _baseSize).CopyTo(_moduleData.AsSpan(destOffset, _baseSize));
            }
        }
    }

    /// <summary>
    /// Decompresses the given data using the specified compression mode.
    /// </summary>
    /// <param name="data"></param>
    /// <param name="mode"></param>
    /// <returns></returns>
    private static byte[] DecompressData(byte[] data, Compression mode)
    {
        if (mode == Compression.Uncompressed)
            return data;

        using var input = new MemoryStream(data);
        using var output = new MemoryStream();

        Stream decompressor = mode switch
        {
            Compression.Deflate => new DeflateStream(input, CompressionMode.Decompress),
            Compression.GZip => new GZipStream(input, CompressionMode.Decompress),
            _ => throw new ArgumentException($"Unsupported compression mode: {mode}")
        };

        using (decompressor)
        {
            decompressor.CopyTo(output);
        }

        return output.ToArray();
    }

    /// <summary>
    /// Compresses the given data using the specified compression mode.
    /// </summary>
    /// <param name="data">Source byte array (may be rented from ArrayPool).</param>
    /// <param name="length">Actual data length to compress.</param>
    /// <param name="mode">Compression mode.</param>
    /// <returns></returns>
    private static byte[] CompressData(byte[] data, int length, Compression mode)
    {
        if (mode == Compression.Uncompressed)
        {
            var result = new byte[length];
            Array.Copy(data, 0, result, 0, length);
            return result;
        }

        using var output = new MemoryStream(length);

        Stream compressor = mode switch
        {
            Compression.Deflate => new DeflateStream(output, CompressionMode.Compress),
            Compression.GZip => new GZipStream(output, CompressionMode.Compress),
            _ => throw new ArgumentException($"Unsupported compression mode: {mode}")
        };

        using (compressor)
        {
            compressor.Write(data, 0, length);
        }

        return output.ToArray();
    }

    /// <summary>
    /// Calculates the number of modules per side (excluding quiet zone) for a given QR code version.
    /// </summary>
    /// <param name="version">QR code version (must be in the range 1-40)</param>
    /// <returns>The number of modules per side for the specified QR code version.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int SizeFromVersion(int version) => 21 + (version - 1) * 4;

    /// <summary>
    /// Calculate version from size (without quiet zone)
    /// Formula: size = 21 + (version - 1) * 4
    /// Inverse: version = (size - 21) / 4 + 1
    /// </summary>
    /// <param name="sizeWithoutQuietZone"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int VersionFromSize(int sizeWithoutQuietZone) => (sizeWithoutQuietZone - 21) / 4 + 1;

    /// <summary>
    /// Calculate number of padding bits needed to align totalBits to next byte boundary.
    /// </summary>
    /// <param name="totalBits"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetPaddingBits(int totalBits)
    {
        var remainder = totalBits % 8;
        return (8 - remainder) % 8;
    }
}
