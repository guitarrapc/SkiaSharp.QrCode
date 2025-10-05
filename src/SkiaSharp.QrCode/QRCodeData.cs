using System.IO.Compression;

namespace SkiaSharp.QrCode;

public class QRCodeData : IDisposable
{
    private static readonly byte[] _headerSignature = [0x51, 0x52, 0x52]; // "QRR"
    /// <summary>
    /// Get the QR code version (1-40)
    /// </summary>
    public int Version { get; private set; }

    private bool[,] _moduleMatrix;
    /// <summary>
    /// Internal direct access to the module matrix for bulk operations.
    /// </summary>
    internal bool[,] ModuleMatrixInternal => _moduleMatrix;

    /// <summary>
    /// Get the size of the QR code module matrix (width and height in modules)
    /// </summary>
    public int Size => _moduleMatrix.GetLength(0);

    public bool this[int row, int col]
    {
        get => _moduleMatrix[row, col];
        internal set => _moduleMatrix[row, col] = value;
    }

    public QRCodeData(int version)
    {
        Version = version;
        var size = ModulesPerSideFromVersion(version);
        _moduleMatrix = new bool[size, size];
    }

    public QRCodeData(byte[] rawData, Compression compressMode)
    {
        var bytes = DecompressData(rawData, compressMode);

        // validate header
        if (bytes[0] != 0x51 || bytes[1] != 0x52 || bytes[2] != 0x52)
            throw new Exception("Invalid raw data file. Filetype doesn't match \"QRR\".");

        // read size from header
        var sideLen = (int)bytes[3];

        // set version from size
        Version = QRCodeVersionFromModulesPerSide(sideLen);

        // unpack
        var totalBits = sideLen * sideLen;
        var modules = new Queue<bool>(totalBits);
        for (int byteIndex = 4; byteIndex < bytes.Length; byteIndex++)
        {
            var b = bytes[byteIndex];
            for (int i = 7; i >= 0; i--)
            {
                modules.Enqueue((b & (1 << i)) != 0);
            }
        }

        // Build module matrix
        _moduleMatrix = new bool[sideLen, sideLen];
        for (int y = 0; y < sideLen; y++)
        {
            for (int x = 0; x < sideLen; x++)
            {
                if (modules.Count == 0)
                {
                    throw new InvalidOperationException($"Insufficient data: expected {totalBits} bits, "
                        + $"but only {y * sideLen + x} bits available.");
                }
                _moduleMatrix[y, x] = modules.Dequeue();
            }
        }

        // Version will be calculated from size
        static int QRCodeVersionFromModulesPerSide(int modulesPerSide)
        {
            return (modulesPerSide - 21 - 8) / 4 + 1;
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

        // Padding to byte boundary
        // Formula: (8 - remainder) % 8
        // - If remainder = 0 (already aligned): (8 - 0) % 8 = 0 (no padding)
        // - If remainder = 1-7: (8 - 1-7) % 8 = 7-1 (add padding to align)
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

        static int GetPaddingBits(int totalBits)
        {
            var remainder = totalBits % 8;
            return (8 - remainder) % 8;
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
    private static int ModulesPerSideFromVersion(int version)
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
    private static int VersionFromSize(int sizeWithoutQuietZone)
    {
        return (sizeWithoutQuietZone - 21) / 4 + 1;
    }

    public void Dispose()
    {
        Version = 0;
    }

    public enum Compression
    {
        Uncompressed,
        Deflate,
        GZip
    }
}
