using System.IO.Compression;
using System.Runtime.CompilerServices;

namespace SkiaSharp.QrCode;

/// <summary>
/// Represents QR code data as a 2D boolean matrix.
/// Supports serialization/deserialization with optional compression.
/// </summary>
/// <remarks>
/// QR code structure:
/// - Version: 1-40 (determines size: 21×21 to 177×177)
/// - Module matrix: 2D array of boolean values (dark/light)
/// - Serialization format: "QRR" header + size + bit-packed data
/// </remarks>
public class QRCodeData
{
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
    /// Initializes using the specified raw data and compression
    /// mode.
    /// </summary>
    /// <remarks>
    /// This constructor processes the provided raw data to initialize the QR code's module matrix
    /// and determine its version. The raw data is expected to follow the QR code format, including a valid header and
    /// size information.
    /// </remarks>
    /// <param name="rawData">The raw byte array representing the QR code data. This data may be compressed based on the specified <paramref name="compressMode"/>.</param>
    /// <param name="compressMode">The compression mode used to encode the <paramref name="rawData"/>. Determines how the data will be decompressed.</param>
    /// <exception cref="InvalidDataException">Thrown if the decompressed data is invalid.</exception>
    /// <exception cref="InvalidOperationException">Thrown if the decompressed data does not contain enough bits to fully populate the QR code matrix.</exception>
    public QRCodeData(byte[] rawData, Compression compressMode)
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
        _quietZoneSize = 0; // assume no quiet zone in file
        _size = _baseSize;
        var totalBits = _size * _size;
        _moduleData = new byte[totalBits];

        // unpack bits to bytes
        var bitIndex = 0;
        for (var byteIndex = 4; byteIndex < bytes.Length && bitIndex < totalBits; byteIndex++)
        {
            var b = bytes[byteIndex];
            for (int i = 7; i >= 0 && bitIndex < totalBits; i--)
            {
                _moduleData[bitIndex] = (b & (1 << i)) != 0 ? (byte)1 : (byte)0;
                bitIndex++;
            }
        }

        if (bitIndex < totalBits)
            throw new InvalidOperationException($"Insufficient data: expected {totalBits} bits, got {bitIndex}.");
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
            throw new ArgumentException($"Destination span is too small for core data, need {_baseSize * _baseSize}, got {destination.Length}");

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
            throw new ArgumentException($"Source span is too small for core data, need {_baseSize * _baseSize}, got {source.Length}");

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
    /// Generates a raw byte array representation of the data, with optional compression.
    /// </summary>
    /// <remarks>
    /// The raw data includes a header followed by the encoded data. The header consists of a
    /// signature and the row size. The data is padded to ensure alignment to the nearest byte boundary. Compression, if
    /// specified, is applied to the entire data stream after it is constructed.
    /// </remarks>
    /// <param name="compressMode"></param>
    internal byte[] GetRawData(Compression compressMode)
    {
        // "QRR"
        ReadOnlySpan<byte> _headerSignature = [ 0x51, 0x52, 0x52 ];

        // size calculations
        var totalBits = _baseSize * _baseSize; // only core data without quiet zone
        var paddingBits = GetPaddingBits(totalBits);
        var dataBytes = (totalBits + paddingBits) / 8;
        var headerSize = _headerSignature.Length + 1; // signature length + 1 byte for size
        var totalSize = headerSize + dataBytes;

        var bytes = new byte[totalSize];

        // Write header - signature ("QRR") & raw size
        bytes[0] = _headerSignature[0];
        bytes[1] = _headerSignature[1];
        bytes[2] = _headerSignature[2];
        bytes[3] = (byte)_baseSize; // only core size without quiet zone

        // Pack bits into bytes
        var bitIndex = 0;
        for (var byteIndex = 0; byteIndex < dataBytes; byteIndex++)
        {
            byte b = 0;
            for (var i = 7; i >= 0; i--)
            {
                if (bitIndex < totalBits)
                {
                    var coreRow = bitIndex / _baseSize;
                    var coreCol = bitIndex % _baseSize;

                    // Apply quiet zone offset
                    var actualRow = coreRow + _quietZoneSize;
                    var actualCol = coreCol + _quietZoneSize;
                    var actualIndex = actualRow * _size + actualCol;

                    if (_moduleData[actualIndex] != 0)
                    {
                        b |= (byte)(1 << i);
                    }
                }
                // else padding bits are 0
                bitIndex++;
            }
            bytes[headerSize + byteIndex] = b;
        }

        // Compress stream
        return CompressData(bytes, compressMode);
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
    /// <param name="data"></param>
    /// <param name="mode"></param>
    /// <returns></returns>
    private static byte[] CompressData(byte[] data, Compression mode)
    {
        if (mode == Compression.Uncompressed)
            return data;

        using var output = new MemoryStream();

        Stream compressor = mode switch
        {
            Compression.Deflate => new DeflateStream(output, CompressionMode.Compress),
            Compression.GZip => new GZipStream(output, CompressionMode.Compress),
            _ => throw new ArgumentException($"Unsupported compression mode: {mode}")
        };

        using (compressor)
        {
            compressor.Write(data, 0, data.Length);
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
