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
    // 1D byte array (row-major order) representing the QR code modules
    // Alifned to 64-byte boundary for AVX-512 optimizations
    private byte[] _moduleData;
    private int _size;
    private int _actualDataLength; // actual length of data used in _moduleData

    /// <summary>
    /// Gets the size of the QR code matrix (modules per side).
    /// Includes quiet zone if added via <see cref="SetModuleMatrix"/>.
    /// </summary>
    public int Size => _size;

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
    /// Get the QR code version (1-40)
    /// </summary>
    public int Version { get; private set; }

    /// <summary>
    /// Initializes with the specified version.
    /// </summary>
    /// <param name="version">QR Code version number (1-40) used to determine matrix size</param>
    public QRCodeData(int version)
    {
        Version = version;
        _size = SizeFromVersion(version);
        _actualDataLength = _size * _size;
        _moduleData = new byte[_actualDataLength];
    }

    /// <summary>
    /// Creates a deep copy of an existing <see cref="QRCodeData"/> instance
    /// All module states are copied to ensure independence from the source.
    /// </summary>
    /// <param name="source">Source QR code data to copy</param>
    /// <remarks>
    /// This constructor is useful for creating temporary QR codes during mask pattern selection.
    /// The copied instance is completely independent and modifications do not affect the source.
    /// </remarks>
    public QRCodeData(QRCodeData source)
    {
        Version = source.Version;
        _size = source.Size;
        _actualDataLength = source._actualDataLength;
        _moduleData = new byte[_actualDataLength];

        // Copy matrix data (excluding any padding)
        Array.Copy(source._moduleData, _moduleData, source._actualDataLength);
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
        var sideLen = (int)bytes[3];
        if (sideLen < 21 || sideLen > 177)
            throw new InvalidDataException($"Invalid QR code size: {sideLen}.");

        // set version from size
        Version = VersionFromSize(sideLen);
        _size = sideLen;
        _actualDataLength = _size * _size;
        _moduleData = new byte[_actualDataLength];

        // unpack bits to bytes
        var totalBits = sideLen * sideLen;
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
    /// Resets the current instance's module matrix to match another <see cref="QRCodeData"/> instance.
    /// </summary>
    /// <param name="source">The source <see cref="QRCodeData"/> instance to copy the module matrix from.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ResetTo(ref QRCodeData source)
    {
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
    source._moduleData.AsSpan(0, source._actualDataLength).CopyTo(_moduleData);
#else
        Array.Copy(source._moduleData, _moduleData, source._actualDataLength);
#endif
    }

    /// <summary>
    /// Get row as byte span
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<byte> GetRow(int row)
    {
        return new ReadOnlySpan<byte>(_moduleData, row * _size, _size);
    }

    /// <summary>
    /// Get row as byte span
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Span<byte> GetRowMutable(int row)
    {
        return new Span<byte>(_moduleData, row * _size, _size);
    }

    /// <summary>
    /// Get entire dat as span
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ReadOnlySpan<byte> GetData()
    {
        return _moduleData.AsSpan();
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
        ReadOnlySpan<byte> _headerSignature = [0x51, 0x52, 0x52];

        // size calculations
        var totalBits = _size * _size;
        var paddingBits = GetPaddingBits(totalBits);
        var totalBitsWithPadding = totalBits + paddingBits;
        var dataBytes = totalBitsWithPadding / 8;
        var headerSize = _headerSignature.Length + 1; // signature length + 1 byte for size
        var totalSize = headerSize + dataBytes;

        var bytes = new byte[totalSize];

        // Write header - signature ("QRR") & raw size
        bytes[0] = _headerSignature[0];
        bytes[1] = _headerSignature[1];
        bytes[2] = _headerSignature[2];
        bytes[3] = (byte)_size;

        // Pack bits into bytes
        var bitIndex = 0;
        for (var byteIndex = 0; byteIndex < dataBytes; byteIndex++)
        {
            byte b = 0;
            for (var i = 7; i >= 0; i--)
            {
                if (bitIndex < totalBits)
                {
                    var row = bitIndex / _size;
                    var col = bitIndex % _size;
                    if (_moduleData[bitIndex] != 0)
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
    /// Updates the module matrix with a new two-dimensional boolean array.
    /// Automatically calculates and updates the version based on matrix size.
    /// </summary>
    /// <remarks>
    /// The expected size of the module matrix is determined by the version of the object. Ensure
    /// that the provided matrix has dimensions equal to the expected size before calling this method.
    /// </remarks>
    /// <param name="moduleData">New module data in row-major order.</param>
    /// <param name="size">Size of the module matrix (including quiet zone if present).</param>
    /// <param name="quietZoneSize">Quiet zone size in modules (0 if matrix doesn't include quiet zone).</param>
    internal void SetModuleMatrix(ReadOnlySpan<byte> moduleData, int size, int quietZoneSize)
    {
        var sizeWithoutQuietZone = size - (quietZoneSize * 2);

        // Calculate version from size (without quiet zone)
        var calculatedVersion = VersionFromSize(sizeWithoutQuietZone);
        if (calculatedVersion < 1 || calculatedVersion > 40)
        {
            throw new ArgumentException(
                $"Invalid matrix size. Size without quiet zone: {sizeWithoutQuietZone}, " +
                $"Calculated version: {calculatedVersion}. " +
                $"Version must be 1-40.",
                nameof(moduleData));
        }

        _size = size;
        _actualDataLength = _size * _size;

        if (_moduleData == null || _moduleData.Length < _actualDataLength)
        {
            _moduleData = new byte[_actualDataLength];
        }

        var copyLength = Math.Min(moduleData.Length, _actualDataLength);
        moduleData.Slice(0, copyLength).CopyTo(_moduleData.AsSpan(0, copyLength));

        Version = calculatedVersion;
    }

    internal void ResizeForQuietZone(int newSize, int quietZoneSize)
    {
        var sizeWitrhoutQuietZone = newSize - (quietZoneSize * 2);
        var calculatedVersion = VersionFromSize(sizeWitrhoutQuietZone);

        if (calculatedVersion < 1 || calculatedVersion > 40)
        {
            throw new ArgumentException($"Invalid veresion: {calculatedVersion}");
        }

        _size = newSize;
        _actualDataLength = newSize * newSize;

        // reuse buffer
        if (_moduleData == null || _moduleData.Length < _actualDataLength)
        {
            _moduleData = new byte[_actualDataLength];
        }
        else
        {
            Array.Clear(_moduleData, 0, _actualDataLength);
        }

        Version = calculatedVersion;
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
    /// Calculate size (without quiet zone) from version
    /// </summary>
    /// <param name="version"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int SizeFromVersion(int version) => 21 + (version - 1) * 4;

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
