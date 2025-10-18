using System.Buffers;
using System.Runtime.CompilerServices;

namespace SkiaSharp.QrCode.Internals;

/// <summary>
/// Static utility class for placing QR code modules (patterns and data).
/// Handles all matrix manipulation operations during QR code generation.
/// </summary>
internal static class ModulePlacer
{
    /// <summary>
    /// Adds white border (quiet zone) around the QR code.
    /// Required by ISO/IEC 18004 standard (minimum 4 modules width).
    /// </summary>
    /// <param name="source">QR code data to copy from.</param>
    /// <param name="destination">Destination buffer for the new QR code with quiet zone. Must be large enough to hold (oldSize + 2 * quietZoneSize) squared elements.</param>
    /// <param name="oldSize">Original QR code size in modules.</param>
    /// <param name="quietZoneSize">Quiet zone width in modules (default: 4).</param>
    public static void AddQuietZone(ReadOnlySpan<byte> source, Span<byte> destination, int oldSize, int quietZoneSize)
    {
        if (quietZoneSize <= 0)
            return;

        if (destination.Length < source.Length + quietZoneSize * 2)
            throw new ArgumentException("Destination buffer is too small.");

        var newSize = oldSize + quietZoneSize * 2;

        // Copy existing data to center of new matrix
        for (var row = 0; row < oldSize; row++)
        {
            var srcOffset = row * oldSize;
            var destOffset = (row + quietZoneSize) * newSize + quietZoneSize;
            source.Slice(srcOffset, oldSize).CopyTo(destination.Slice(destOffset, oldSize));
        }
    }

    /// <summary>
    /// Applies mask pattern to data area and selects optimal pattern.
    /// Tests all 8 mask patterns and selects one with lowest penalty score.
    /// </summary>
    /// <returns>Selected mask pattern number (0-7).</returns>
    public static int MaskCode(Span<byte> buffer, int size, int version, ReadOnlySpan<byte> blockedMask, ECCLevel eccLevel)
    {
        var dataLength = size * size;
        var bestPatternIndex = 0;

        // Select stack or heap allocation based on size
        const int StackAlloc_threshold = 2048;

        if (dataLength <= StackAlloc_threshold)
        {
            Span<byte> tempBuffer = stackalloc byte[dataLength];
            bestPatternIndex = EvaluateMaskPatterns(buffer, tempBuffer, version, size, blockedMask, eccLevel);
        }
        else
        {
            var rentBuffer = ArrayPool<byte>.Shared.Rent(dataLength);
            try
            {
                bestPatternIndex = EvaluateMaskPatterns(buffer, rentBuffer.AsSpan()[..dataLength], version, size, blockedMask, eccLevel);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rentBuffer, clearArray: true);
            }
        }

        // Apply mask to original QR code
        ApplyMaskToDataAreaInPlace(buffer, size, bestPatternIndex, blockedMask);

        return bestPatternIndex;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int EvaluateMaskPatterns(ReadOnlySpan<byte> originalData, Span<byte> tempBuffer, int version, int size, ReadOnlySpan<byte> blockedMask, ECCLevel eccLevel)
    {
        var bestPatternIndex = 0;
        var bestScore = int.MaxValue;

        // Test all 8 patterns
        for (var patternIndex = 0; patternIndex < 8; patternIndex++)
        {
            // Reset to original state
            originalData.CopyTo(tempBuffer);

            // Apply mask pattern to data area only
            ApplyMaskToDataAreaInPlace(tempBuffer, size, patternIndex, blockedMask);

            // Apply format and version information
            var formatBits = QRCodeConstants.GetFormatBits(eccLevel, patternIndex);
            PlaceFormat(tempBuffer, size, formatBits);

            if (version >= 7)
            {
                var versionBits = QRCodeConstants.GetVersionBits(version);
                PlaceVersion(tempBuffer, size, versionBits);
            }

            // Calculate score
            var score = CalculateScore(tempBuffer, size);
            if (score < bestScore)
            {
                bestPatternIndex = patternIndex;
                bestScore = score;
            }
        }

        return bestPatternIndex;
    }

    /// <summary>
    /// Places encoded data and ECC words into the QR code matrix.
    /// Fills modules in zigzag pattern from bottom-right to top-left.
    /// </summary>
    /// <param name="buffer">QR code data structure to populate.</param>
    /// <param name="interleavedData">Interleaved data and ECC bytes.</param>
    /// <param name="blockedMask">blocked mask bytes.</param>
    /// <remarks>
    /// Bits are read MSG-first (most significant bit first) from the byte array.
    ///
    /// Data placement pattern (ISO/IEC 18004 Section 7.7.3):
    /// - Start from bottom-right corner
    /// - Move upwards in a 2-column strips (zigzag pattern).
    /// - Alternate direction at top/bottom edges.
    /// - Skip timing pattern column (column 6)
    /// - Fill out non-blocked modules
    /// </remarks>
    public static void PlaceDataWords(Span<byte> buffer, int size, ReadOnlySpan<byte> interleavedData, ReadOnlySpan<byte> blockedMask)
    {
        var bitPos = 0;
        var totalBits = interleavedData.Length * 8;
        var up = true;

        for (var x = size - 1; x >= 0; x -= 2)
        {
            // Skip timing pattern column
            if (x == 6)
                x--;

            // Process each row in zigzag pattern
            for (var yMod = 1; yMod <= size; yMod++)
            {
                // zigzag direction
                var y = up ? size - yMod : yMod - 1;

                // Process 2 columns (x and x-1)
                for (var xOffset = 0; xOffset < 2; xOffset++)
                {
                    var xModule = x - xOffset;
                    var bitIndex = y * size + xModule;

                    if (!IsModuleBlocked(blockedMask, bitIndex) && bitPos < totalBits)
                    {
                        var byteIndex = bitPos >> 3;
                        var bitMask = 1 << (7 - (bitPos & 7)); // MSB first
                        var module = (interleavedData[byteIndex] & bitMask) != 0 ? (byte)1 : (byte)0;
                        buffer[bitIndex] = module;
                        bitPos++;
                    }
                }
            }

            // Alternate direction
            up = !up;
        }
    }

    /// <summary>
    /// Places encoded data and ECC words into the QR code matrix.
    /// Fills modules in zigzag pattern from bottom-right to top-left.
    /// </summary>
    /// <param name="qrCode">QR code data structure to populate.</param>
    /// <param name="data">Interleaved data and ECC bytes string.</param>
    /// <param name="blockedModules">List of reserved module areas.</param>
    public static void PlaceDataWords(ref QRCodeData qrCode, string data, ReadOnlySpan<Rectangle> blockedModules)
    {
        var size = qrCode.Size;
        var up = true;
        var bitIndex = 0;

        for (var x = size - 1; x >= 0; x = x - 2)
        {
            // Skip timing pattern column
            if (x == 6)
                x--;

            for (var yMod = 1; yMod <= size; yMod++)
            {
                int y = up ? size - yMod : yMod - 1;

                // Process 2 columns (x and x-1)
                for (var xOffset = 0; xOffset < 2; xOffset++)
                {
                    var xModule = x - xOffset;

                    // Skip blocked module (finder patterns, timing patterns, format/version info, etc.)
                    if (IsPointBlocked(xModule, y, blockedModules))
                        continue;

                    // Place bit if available
                    if (bitIndex < data.Length)
                    {
                        qrCode[y, xModule] = data[bitIndex] != '0';
                        bitIndex++;
                    }
                    else
                    {
                        qrCode[y, xModule] = false;
                    }
                }
            }

            // alternate direction
            up = !up;
        }
    }

    /// <summary>
    /// Reserves separator areas (white borders) around finder patterns.
    /// 1-module wide white border separates finder patterns from data area.
    /// </summary>
    public static void ReserveSeparatorAreas(int size, Span<Rectangle> blockedModules, ref int blockedCount)
    {
        blockedModules[blockedCount++] = new Rectangle(7, 0, 1, 8);
        blockedModules[blockedCount++] = new Rectangle(0, 7, 7, 1);
        blockedModules[blockedCount++] = new Rectangle(0, size - 8, 8, 1);
        blockedModules[blockedCount++] = new Rectangle(7, size - 7, 1, 7);
        blockedModules[blockedCount++] = new Rectangle(size - 8, 0, 1, 8);
        blockedModules[blockedCount++] = new Rectangle(size - 7, 7, 7, 1);
    }

    /// <summary>
    /// Reserves areas for format and version information.
    /// These areas are filled later with actual format/version data.
    /// </summary>
    public static void ReserveVersionAreas(int size, int version, Span<Rectangle> blockedModules, ref int blockedCount)
    {
        blockedModules[blockedCount++] = new Rectangle(8, 0, 1, 6);
        blockedModules[blockedCount++] = new Rectangle(8, 7, 1, 1);
        blockedModules[blockedCount++] = new Rectangle(0, 8, 6, 1);
        blockedModules[blockedCount++] = new Rectangle(7, 8, 2, 1);
        blockedModules[blockedCount++] = new Rectangle(size - 8, 8, 8, 1);
        blockedModules[blockedCount++] = new Rectangle(8, size - 7, 1, 7);

        if (version >= 7)
        {
            blockedModules[blockedCount++] = new Rectangle(size - 11, 0, 3, 6);
            blockedModules[blockedCount++] = new Rectangle(0, size - 11, 6, 3);
        }
    }

    /// <summary>
    /// Places three finder patterns (position detection patterns).
    /// 7×7 patterns located at top-left, top-right, and bottom-left corners.
    /// </summary>
    public static void PlaceFinderPatterns(Span<byte> buffer, int size, Span<Rectangle> blockedModules, ref int blockedCount)
    {
        ReadOnlySpan<Point> locations = stackalloc Point[3]
        {
            new Point(0, 0),
            new Point(size - 7, 0),
            new Point(0, size - 7),
        };

        foreach (var location in locations)
        {
            for (var x = 0; x < 7; x++)
            {
                for (var y = 0; y < 7; y++)
                {
                    if (!(((x == 1 || x == 5) && y > 0 && y < 6) || (x > 0 && x < 6 && (y == 1 || y == 5))))
                    {
                        var row = y + location.Y;
                        var col = x + location.X;
                        buffer[row * size + col] = 1;
                    }
                }
            }
            blockedModules[blockedCount++] = new Rectangle(location.X, location.Y, 7, 7);
        }
    }

    /// <summary>
    /// Places alignment patterns throughout the QR code.
    /// Number and positions vary by version (version 2+).
    /// 5×5 patterns help with image recognition and distortion correction.
    /// </summary>
    public static void PlaceAlignmentPatterns(Span<byte> buffer, int size, List<Point> alignmentPatternLocations, Span<Rectangle> blockedModules, ref int blockedCount)
    {
        foreach (var loc in alignmentPatternLocations)
        {
            var alignmentPatternRect = new Rectangle(loc.X, loc.Y, 5, 5);
            var blocked = false;
            foreach (var blockedRect in blockedModules)
            {
                if (Intersects(alignmentPatternRect, blockedRect))
                {
                    blocked = true;
                    break;
                }
            }
            if (blocked)
            {
                continue;
            }

            for (var x = 0; x < 5; x++)
            {
                for (var y = 0; y < 5; y++)
                {
                    if (y == 0 || y == 4 || x == 0 || x == 4 || (x == 2 && y == 2))
                    {
                        var row = loc.Y + y;
                        var col = loc.X + x;
                        buffer[row * size + col] = 1;
                    }
                }
            }
            blockedModules[blockedCount++] = new Rectangle(loc.X, loc.Y, 5, 5);
        }
    }

    /// <summary>
    /// Places timing patterns (alternating dark/light modules).
    /// Horizontal and vertical lines at row 6 and column 6.
    /// Used for module coordinate mapping during decoding.
    /// </summary>
    public static void PlaceTimingPatterns(Span<byte> buffer, int size, Span<Rectangle> blockedModules, ref int blockedCount)
    {
        for (var i = 8; i < size - 8; i++)
        {
            if (i % 2 == 0)
            {
                buffer[6 * size + i] = 1; // row 6
                buffer[i * size + 6] = 1; // col 6
            }
        }
        blockedModules[blockedCount++] = new Rectangle(6, 8, 1, size - 16);
        blockedModules[blockedCount++] = new Rectangle(8, 6, size - 16, 1);
    }

    /// <summary>
    /// Places the dark module (always dark/black).
    /// Located at position (8, 4*version + 9).
    /// Required by QR code specification for all versions.
    /// </summary>
    public static void PlaceDarkModule(Span<byte> buffer, int size, int version, Span<Rectangle> blockedModules, ref int blockedCount)
    {
        buffer[(4 * version + 9) * size + 8] = 1;
        blockedModules[blockedCount++] = new Rectangle(8, 4 * version + 9, 1, 1);
    }

    /// <summary>
    /// Places format information patterns around finder patterns.
    /// Contains error correction level and mask pattern information.
    /// Two identical 15-bit sequences for redundancy.
    /// </summary>
    /// <param name="buffer">QR code matrix buffer.</param>
    /// <param name="formatBits">15-bit format information (LSB first).</param>
    /// <remarks>
    /// Places two identical copies for redundancy (ISO/IEC 18004 Section 7.9):
    /// <code>
    /// - Copy 1: Around top-left and top-right finder patterns
    /// - Copy 2: Around top-left and bottom-left finder patterns
    /// </code>
    /// Bits are placed from LSB (bit 0) to MSB (bit 14).
    /// </remarks>
    public static void PlaceFormat(Span<byte> buffer, int size, ushort formatBits)
    {
        // Stack allocation: 15 tuples × 4 ints × 4 bytes = 240 bytes
        ReadOnlySpan<(int x1, int y1, int x2, int y2)> positions = stackalloc (int, int, int, int)[15]
        {
            ( 8, 0, size - 1, 8 ),
            ( 8, 1, size - 2, 8 ),
            ( 8, 2, size - 3, 8 ),
            ( 8, 3, size - 4, 8 ),
            ( 8, 4, size - 5, 8 ),
            ( 8, 5, size - 6, 8 ),
            ( 8, 7, size - 7, 8 ),
            ( 8, 8, size - 8, 8 ),
            ( 7, 8, 8, size - 7 ),
            ( 5, 8, 8, size - 6 ),
            ( 4, 8, 8, size - 5 ),
            ( 3, 8, 8, size - 4 ),
            ( 2, 8, 8, size - 3 ),
            ( 1, 8, 8, size - 2 ),
            ( 0, 8, 8, size - 1 ),
        };

        for (var i = 0; i < 15; i++)
        {
            var bit = (byte)((formatBits & (1 << i)) != 0 ? 1 : 0);
            var (x1, y1, x2, y2) = positions[i];
            buffer[y1 * size + x1] = bit;
            buffer[y2 * size + x2] = bit;
        }
    }

    /// <summary>
    /// Places version information patterns (version 7+ only).
    /// Two identical 3×6 patterns placed at top-right and bottom-left corners.
    /// </summary>
    /// <param name="buffer">QR code matrix buffer</param>
    /// <param name="versionBits">18-bit version information (LSB first)</param>
    /// <remarks>
    /// Places two identical 3×6 patterns (ISO/IEC 18004 Section 7.10):
    /// <code>
    /// - Pattern 1: Bottom-left corner (vertical)
    /// - Pattern 2: Top-right corner (horizontal)
    /// </code>
    /// Bits are placed from LSB (bit 0) to MSB (bit 17) in reading order.
    /// </remarks>
    public static void PlaceVersion(Span<byte> buffer, int size, uint versionBits)
    {
        for (var x = 0; x < 6; x++)
        {
            for (var y = 0; y < 3; y++)
            {
                var bitIndex = x * 3 + y;
                var bit = (byte)((versionBits & (1 << bitIndex)) != 0 ? 1 : 0);
                buffer[(y + size - 11) * size + x] = bit;
                buffer[x * size + (y + size - 11)] = bit;
            }
        }
    }

    /// <summary>
    /// Checks if two rectangles intersect.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool Intersects(Rectangle r1, Rectangle r2)
    {
        return r2.X < r1.X + r1.Width
            && r1.X < r2.X + r2.Width
            && r2.Y < r1.Y + r1.Height
            && r1.Y < r2.Y + r2.Height;
    }

    /// <summary>
    /// Checks if a single point (module) is blocked.
    /// Optimized for 1x1 point checks without Rectangle allocations.
    /// </summary>
    /// <param name="x">Column position</param>
    /// <param name="y">Row position</param>
    /// <param name="blockedModules">List of reserved module areas</param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsPointBlocked(int x, int y, ReadOnlySpan<Rectangle> blockedModules)
    {
        foreach (var rect in blockedModules)
        {
            if (x >= rect.X && x < rect.X + rect.Width &&
                y >= rect.Y && y < rect.Y + rect.Height)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Checks if a rectangle overlaps with any blocked module area.
    /// </summary>
    /// <param name="r1"></param>
    /// <param name="blockedModules"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsBlocked(Rectangle r1, ReadOnlySpan<Rectangle> blockedModules)
    {
        // Tried HashSet pattern for O(1) lookup, but it was slower than this simple loop O(n) and has memory overhead.
        // Keep this implementation for now.
        foreach (var blockedMod in blockedModules)
        {
            if (Intersects(blockedMod, r1))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Check if a module at given bit index is blocked using a bitmask.
    /// </summary>
    /// <param name="mask">Bitmask buffer</param>
    /// <param name="bitIndex">Linear index of module</param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsModuleBlocked(ReadOnlySpan<byte> mask, int bitIndex)
    {
        // Performance: O(1) constant-time lookup using bitwise operations.
        // 
        // Bit extraction:
        // - Byte index = bitIndex >> 3 (equivalent to bitIndex / 8)
        // - Bit position = bitIndex &amp; 7 (equivalent to bitIndex % 8)
        // - Mask = 1 &lt;&lt; bit_position
        // - Result = (byte &amp; mask) != 0
        // 
        // Example (bitIndex = 10):
        // - Byte index: 10 >> 3 = 1 (second byte)
        // - Bit position: 10 &amp; 7 = 2 (third bit from LSB)
        // - Mask: 1 &lt;&lt; 2 = 0b00000100

        return (mask[bitIndex >> 3] & (1 << (bitIndex & 7))) != 0;
    }

    /// <summary>
    /// Apply the mask pattern to the data area only in-place, skipping blocked modules.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ApplyMaskToDataAreaInPlace(Span<byte> buffer, int size, int patternIndex, ReadOnlySpan<byte> blockedMask)
    {
        for (var row = 0; row < size; row++)
        {
            var rowOffset = row * size;
            for (var col = 0; col < size; col++)
            {
                var bitIndex = rowOffset + col;
                if (!IsModuleBlocked(blockedMask, bitIndex))
                {
                    buffer[bitIndex] = (byte)(buffer[bitIndex] ^ (MaskPattern.Apply(patternIndex, col, row) ? 1 : 0));
                }
            }
        }
    }

    // ---------------------------------
    // Basic penalty calculations for reference.
    // ---------------------------------
    // // Penalty 1: Consecutive modules
    // for (var y = 0; y < size; y++)
    // {
    //     var modInRow = 0;
    //     var modInColumn = 0;
    //     var lastValRow = qrCode[y, 0];
    //     var lastValColumn = qrCode[0, y];
    //     for (var x = 0; x < size; x++)
    //     {
    //         if (qrCode[y, x] == lastValRow)
    //         {
    //             modInRow++;
    //         }
    //         else
    //         {
    //             modInRow = 1;
    //         }
    //         if (modInRow == 5)
    //         {
    //             score1 += 3;
    //         }
    //         else if (modInRow > 5)
    //         {
    //             score1++;
    //         }
    //         lastValRow = qrCode[y, x];
    //
    //         if (qrCode[x, y] == lastValColumn)
    //         {
    //             modInColumn++;
    //         }
    //         else
    //         {
    //             modInColumn = 1;
    //         }
    //         if (modInColumn == 5)
    //         {
    //             score1 += 3;
    //         }
    //         else if (modInColumn > 5)
    //         {
    //             score1++;
    //         }
    //         lastValColumn = qrCode[x, y];
    //     }
    // }
    //
    // // Penalty 2: Block patterns
    // for (var y = 0; y < size - 1; y++)
    // {
    //     for (var x = 0; x < size - 1; x++)
    //     {
    //         if (qrCode[y, x] == qrCode[y, x + 1] &&
    //             qrCode[y, x] == qrCode[y + 1, x] &&
    //             qrCode[y, x] == qrCode[y + 1, x + 1])
    //         {
    //             score2 += 3;
    //         }
    //     }
    // }
    //
    // // Penalty 3: Finder-like patterns (with sliding 11-bit window)
    // // pattern: 1011101 (dark-light-dark-dark-dark-light-dark)
    // const uint PATTERN_FORWARD = 0b_0000_1011101; // 4 light modules + pattern
    // const uint PATTERN_BACKWARD = 0b_1011101_0000; // pattern + 4 light modules
    // const uint MASK_11BIT = 0b_111_1111_1111; // guarantee 11 bits
    // for (var y = 0; y<size; y++)
    // {
    //     var rowOffset = y * size;
    //     uint rowBits = 0;
    //     uint colBits = 0;
    //     for (var x = 0; x<size; x++)
    //     {
    //         // Build row/col bits (shift left and OR with current bit == add new bit)
    //         rowBits = ((rowBits << 1) | (buffer[rowOffset + x] != 0 ? 1u : 0u)) & MASK_11BIT;
    //         colBits = ((colBits << 1) | (buffer[x * size + y] != 0 ? 1u : 0u)) & MASK_11BIT;
    // 
    //         // 11 bits ready, check for pattern
    //         if (x >= 10)
    //         {
    //             // Check row bits
    //             if (rowBits == PATTERN_FORWARD || rowBits == PATTERN_BACKWARD)
    //                 score3 += 40;
    // 
    //             // Check column bits
    //             if (colBits == PATTERN_FORWARD || colBits == PATTERN_BACKWARD)
    //                 score3 += 40;
    //         }
    //     }
    // }
    //
    // // Penalty 4: Dark/light balance
    // double blackModules = 0;
    // for (var row = 0; row < size; row++)
    // {
    //     for (var col = 0; col < size; col++)
    //     {
    //         if (qrCode[row, col])
    //         {
    //             blackModules++;
    //         }
    //     }
    // }
    // 
    // // Calculate percentage of dark modules
    // var percent = (blackModules / (size * size)) * 100;
    // 
    // // ISO/IEC 18004:2015 Section 8.8.2: Score = (|percentage - 50| / 5) × 10
    // // Find closest multiple of 5 to the percentage
    // var prevMultipleOf5 = Math.Abs((int)Math.Floor(percent / 5) * 5 - 50) / 5;
    // var nextMultipleOf5 = Math.Abs((int)Math.Floor(percent / 5) * 5 - 45) / 5;
    // score4 = Math.Min(prevMultipleOf5, nextMultipleOf5) * 10;

    /// <summary>
    /// Calculates penalty score for a masked QR code.
    /// Lower score = better readability and scanning reliability.
    /// Applies 4 penalty rules from ISO/IEC 18004 Section 8.8.2:
    ///
    /// Rule 1 (Consecutive modules):
    ///   - 5 consecutive modules: +3 points
    ///   - Each additional consecutive module: +1 point
    ///   - Applied to both rows and columns
    ///
    /// Rule 2 (Block patterns):
    ///   - Each 2×2 block of same color: +3 points
    ///
    /// Rule 3 (Finder-like patterns):
    ///   - Pattern "1:1:3:1:1 ratio with 4 light modules on either side": +40 points
    ///   - Helps avoid false positives during QR code detection
    ///
    /// Rule 4 (Balance):
    ///   - Deviation from 50% dark modules
    ///   - Score = (|percentage - 50| / 5) × 10
    ///   - Encourages even distribution of dark/light modules
    /// </summary>
    private static int CalculateScore(ReadOnlySpan<byte> buffer, int size)
    {
        // Penalty3 pattern: 1011101 (dark-light-dark-dark-dark-light-dark)
        const uint PATTERN_FORWARD = 0b_0000_1011101; // 4 light modules + pattern
        const uint PATTERN_BACKWARD = 0b_1011101_0000; // pattern + 4 light modules
        const uint MASK_11BIT = 0b_111_1111_1111; // guarantee 11 bits

        var score1 = 0;
        var score2 = 0;
        var score3 = 0;
        var blackModules = 0; // dark modules count for Penalty 4

        // Scan Penalty 1, 2, 3, 4 in single pass
        for (var y = 0; y < size; y++)
        {
            var rowOffset = y * size;
            var modInRow = 0;
            var lastValRow = buffer[rowOffset] != 0;
            // for Penalty 3
            uint rowBits = 0;

            for (var x = 0; x < size; x++)
            {
                var index = rowOffset + x;
                var current = buffer[index] != 0;

                // Penalty 4: Count black modules
                if (current) blackModules++;

                // Penalty 1: row direction
                if (current == lastValRow)
                {
                    modInRow++;
                    if (modInRow == 5)
                    {
                        score1 += 3;
                    }
                    else if (modInRow > 5)
                    {
                        score1++;
                    }
                }
                else
                {
                    modInRow = 1;
                    lastValRow = current;
                }

                // Penalty 2: 2x2 block patterns
                if (x < size - 1 && y < size - 1)
                {
                    var right = buffer[index + 1] != 0;
                    var bottom = buffer[index + size] != 0;
                    var diag = buffer[index + size + 1] != 0;
                    if (current == right && current == bottom && current == diag)
                    {
                        score2 += 3;
                    }
                }

                // Penalty 3: row direction (11-bit sliding window)
                // Build row bits (shift left and OR with current bit == add new bit)
                rowBits = ((rowBits << 1) | (current ? 1u : 0u)) & MASK_11BIT;
                // 11 bits ready, check for pattern
                if (x >= 10)
                {
                    if (rowBits == PATTERN_FORWARD || rowBits == PATTERN_BACKWARD)
                        score3 += 40;
                }
            }
        }

        // Penalty 1 & 3: col direction (avoid transposing in previous loop)
        for (var x = 0; x < size; x++)
        {
            var modInColumn = 0;
            var lastValColumn = buffer[x] != 0;
            uint colBits = 0;

            for (var y = 0; y < size; y++)
            {
                var current = buffer[y * size + x] != 0;

                // Penalty 1
                if (current == lastValColumn)
                {
                    modInColumn++;
                    if (modInColumn == 5)
                    {
                        score1 += 3;
                    }
                    else if (modInColumn > 5)
                    {
                        score1++;
                    }
                }
                else
                {
                    modInColumn = 1;
                    lastValColumn = current;
                }

                // Penalty 3: col direction (11-bit sliding window)
                colBits = ((colBits << 1) | (current ? 1u : 0u)) & MASK_11BIT;
                // 11 bits ready, check for pattern
                if (y >= 10)
                {
                    if (colBits == PATTERN_FORWARD || colBits == PATTERN_BACKWARD)
                        score3 += 40;
                }
            }
        }

        // Penalty 4: Dark/light balance
        var percent = (blackModules / (double)(size * size)) * 100;
        var prevMultipleOf5 = Math.Abs((int)Math.Floor(percent / 5) * 5 - 50) / 5;
        var nextMultipleOf5 = Math.Abs((int)Math.Floor(percent / 5) * 5 - 45) / 5;
        var score4 = Math.Min(prevMultipleOf5, nextMultipleOf5) * 10;

        return score1 + score2 + score3 + score4;
    }

    // private class/strusts

    /// <summary>
    /// Mask pattern implementations and scoring algorithm.
    /// ISO/IEC 18004 defines 8 mask patterns and 4 penalty rules.
    /// </summary>
    private static class MaskPattern
    {
        /// <summary>
        /// Applies the specified mask pattern to a module
        /// </summary>
        /// <param name="patternIndex">Pattern number (0-7)</param>
        /// <param name="x">Math horizontal position = column</param>
        /// <param name="y">Math vertical position = row</param>
        /// <returns>True if the module is dark, false if it is light</returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public static bool Apply(int patternIndex, int x, int y)
        {
            return patternIndex switch
            {
                0 => Pattern1(x, y),
                1 => Pattern2(x, y),
                2 => Pattern3(x, y),
                3 => Pattern4(x, y),
                4 => Pattern5(x, y),
                5 => Pattern6(x, y),
                6 => Pattern7(x, y),
                7 => Pattern8(x, y),
                _ => throw new ArgumentOutOfRangeException(nameof(patternIndex), "Mask pattern index must be between 0 and 7."),
            };
        }

        /// <summary>
        /// Creates a checkerboard pattern.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Pattern1(int x, int y) => (x + y) % 2 == 0;

        /// <summary>
        /// Creates horizontal stripes.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Pattern2(int x, int y) => y % 2 == 0;

        /// <summary>
        /// Creates vertical stripes (wider than pattern 1).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Pattern3(int x, int y) => x % 3 == 0;

        /// <summary>
        /// Creates diagonal stripes (wider than pattern 0).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Pattern4(int x, int y) => (x + y) % 3 == 0;

        /// <summary>
        /// Creates a combination of horizontal and vertical patterns.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Pattern5(int x, int y) => ((int)(Math.Floor(y / 2d) + Math.Floor(x / 3d)) % 2) == 0;

        /// <summary>
        /// Creates a complex grid pattern.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Pattern6(int x, int y) => ((x * y) % 2) + ((x * y) % 3) == 0;

        /// <summary>
        /// Creates an alternating complex pattern.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Pattern7(int x, int y) => (((x * y) % 2) + ((x * y) % 3)) % 2 == 0;

        /// <summary>
        /// Creates a combination of checkerboard and grid patterns.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Pattern8(int x, int y) => (((x + y) % 2) + ((x * y) % 3)) % 2 == 0;
    }
}
