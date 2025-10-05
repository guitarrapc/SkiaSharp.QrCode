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
public class QRCodeData : IDisposable
{
    private static readonly byte[] _headerSignature = [0x51, 0x52, 0x52]; // "QRR"

    /// <summary>
    /// Get the QR code version (1-40)
    /// </summary>
    public int Version { get; private set; }

    private bool[,] _moduleMatrix;
    /// <summary>
    /// Gets the size of the QR code matrix (modules per side).
    /// Includes quiet zone if added via <see cref="SetModuleMatrix"/>.
    /// </summary>
    public int Size => _moduleMatrix.GetLength(0);

    /// <summary>
    /// Gets or sets the module state at the specified position.
    /// </summary>
    /// <param name="row">Row index (0-based).</param>
    /// <param name="col">Column index (0-based).</param>
    /// <returns>True if module is dark/black, false if light/white.</returns>
    public bool this[int row, int col]
    {
        get => _moduleMatrix[row, col];
        internal set => _moduleMatrix[row, col] = value;
    }

    public QRCodeData(int version)
    {
        Version = version;
        var size = SizeFromVersion(version);
        _moduleMatrix = new bool[size, size];
    }

    public QRCodeData(byte[] rawData, Compression compressMode)
    {
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

        // unpack
        var totalBits = sideLen * sideLen;
        _moduleMatrix = new bool[sideLen, sideLen];
        var bitIndex = 0;
        for (var byteIndex = 4; byteIndex < bytes.Length && bitIndex < totalBits; byteIndex++)
        {
            var b = bytes[byteIndex];
            for (int i = 7; i >= 0 && bitIndex < totalBits; i--)
            {
                var y = bitIndex / sideLen;
                var x = bitIndex % sideLen;
                _moduleMatrix[y, x] = (b & (1 << i)) != 0;
                bitIndex++;
            }
        }

        if (bitIndex < totalBits)
        {
            throw new InvalidOperationException($"Insufficient data: expected {totalBits} bits, got {bitIndex}.");
        }
    }

    /// <summary>
    /// Updates the module matrix with a new two-dimensional boolean array.
    /// Automatically calculates and updates the version based on matrix size.
    /// </summary>
    /// <remarks>
    /// The expected size of the module matrix is determined by the version of the object. Ensure
    /// that the provided matrix has dimensions equal to the expected size before calling this method.
    /// </remarks>
    /// <param name="moduleMatrix">New module matrix.</param>
    /// <param name="quietZoneSize">Quiet zone size in modules (0 if matrix doesn't include quiet zone).</param>
    public void SetModuleMatrix(bool[,] moduleMatrix, int quietZoneSize)
    {
        var totalSize = moduleMatrix.GetLength(0);
        var sizeWithoutQuietZone = totalSize - (quietZoneSize * 2);

        // Calculate version from size (without quiet zone)
        var calculatedVersion = VersionFromSize(sizeWithoutQuietZone);

        if (calculatedVersion < 1 || calculatedVersion > 40)
        {
            throw new ArgumentException(
                $"Invalid matrix size. Size without quiet zone: {sizeWithoutQuietZone}, " +
                $"Calculated version: {calculatedVersion}. " +
                $"Version must be 1-40.",
                nameof(moduleMatrix));
        }

        _moduleMatrix = moduleMatrix;
        Version = calculatedVersion;
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
    public byte[] GetRawData(Compression compressMode)
    {
        var bytes = new List<byte>();

        // Add header - signature ("QRR") & raw size
        bytes.AddRange(_headerSignature);
        bytes.Add((byte)Size);

        // Build data queue
        var dataQueue = new Queue<int>();
        var size = Size;
        for (var row = 0; row < size; row++)
        {
            for (var col = 0; col < size; col++)
            {
                dataQueue.Enqueue(_moduleMatrix[row, col] ? 1 : 0);
            }
        }

        // Padding
        var totalBits = size * size;
        var paddingBits = GetPaddingBits(totalBits);
        for (int i = 0; i < paddingBits; i++)
        {
            dataQueue.Enqueue(0);
        }

        // pack bits into bytes
        while (dataQueue.Count > 0)
        {
            byte b = 0;
            for (int i = 7; i >= 0; i--)
            {
                b += (byte)(dataQueue.Dequeue() << i);
            }
            bytes.Add(b);
        }
        var rawData = bytes.ToArray();

        // Compress stream
        return CompressData(rawData, compressMode);
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
    private static int SizeFromVersion(int version)
    {
        return 21 + (version - 1) * 4;
    }

    /// <summary>
    /// Calculate version from size (without quiet zone)
    /// Formula: size = 21 + (version - 1) * 4
    /// Inverse: version = (size - 21) / 4 + 1
    /// </summary>
    /// <param name="sizeWithoutQuietZone"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int VersionFromSize(int sizeWithoutQuietZone)
    {
        return (sizeWithoutQuietZone - 21) / 4 + 1;
    }

    /// <summary>
    /// Calculate number of padding bits needed to align totalBits to next byte boundary.
    /// </summary>
    /// <param name="totalBits"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int GetPaddingBits(int totalBits)
    {
        var remainder = totalBits % 8;
        return (8 - remainder) % 8;
    }

    public void Dispose()
    {
        // will be removed in future, or remain.
    }

    public enum Compression
    {
        Uncompressed,
        Deflate,
        GZip
    }
}
