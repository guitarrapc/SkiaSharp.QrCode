namespace SkiaSharp.QrCode.Internals;

/// <summary>
/// Uniform read view over a square symbol matrix (core modules + virtual quiet
/// zone), letting the rendering loops in <see cref="QRCodeRenderer"/> serve all
/// square symbologies through struct specialization (no virtual dispatch).
/// </summary>
internal interface IModuleMatrixView
{
    /// <summary>Matrix side length in modules, including the quiet zone.</summary>
    int Size { get; }

    /// <summary>Core matrix side length (quiet zone excluded).</summary>
    int CoreSize { get; }

    /// <summary>Reads a core module (caller guarantees bounds).</summary>
    bool GetCoreModule(int coreRow, int coreCol);

    /// <summary>Whether the core module belongs to a finder pattern.</summary>
    bool IsFinderPattern(int coreRow, int coreCol);
}

internal readonly struct StandardQrMatrixView(QRCodeData data) : IModuleMatrixView
{
    public int Size => data.Size;
    public int CoreSize => data.GetCoreSize();
    public bool GetCoreModule(int coreRow, int coreCol) => data.GetCoreModule(coreRow, coreCol);
    public bool IsFinderPattern(int coreRow, int coreCol) => data.IsFinderPattern(coreRow, coreCol);
}

internal readonly struct MicroQRMatrixView(MicroQRCodeData data) : IModuleMatrixView
{
    public int Size => data.Size;
    public int CoreSize => data.GetCoreSize();
    public bool GetCoreModule(int coreRow, int coreCol) => data.GetCoreModule(coreRow, coreCol);
    // Micro QR rendering never styles finder patterns separately, so the draw
    // loops are always called with finder skipping disabled.
    public bool IsFinderPattern(int coreRow, int coreCol) => false;
}
