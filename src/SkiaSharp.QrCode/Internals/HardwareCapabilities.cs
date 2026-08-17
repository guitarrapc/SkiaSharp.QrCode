#if NET8_0_OR_GREATER
using System.Runtime.Intrinsics.X86;
#endif

namespace SkiaSharp.QrCode.Internals;

/// <summary>
/// Runtime CPU properties that the <c>IsSupported</c> flags of
/// <see cref="System.Runtime.Intrinsics"/> do not express.
/// </summary>
internal static class HardwareCapabilities
{
#if NET8_0_OR_GREATER
    /// <summary>
    /// PDEP/PEXT are microcoded on AMD before Zen 3 (hundreds of cycles per
    /// instruction), which would turn a BMI2 kernel into a large regression there:
    /// true only on non-AMD vendors, or AMD family 0x19 (Zen 3) and later.
    /// </summary>
    internal static readonly bool HasFastPext = DetectFastPext();

    private static bool DetectFastPext()
    {
        if (!Bmi2.X64.IsSupported)
        {
            return false;
        }
        var (_, ebx, ecx, edx) = X86Base.CpuId(0, 0);
        var isAmd = ebx == 0x68747541 && edx == 0x69746E65 && ecx == 0x444D4163; // "AuthenticAMD"
        if (!isAmd)
        {
            return true;
        }
        var (eax, _, _, _) = X86Base.CpuId(1, 0);
        var baseFamily = (eax >> 8) & 0xF;
        var family = baseFamily == 0xF ? baseFamily + ((eax >> 20) & 0xFF) : baseFamily;
        return family >= 0x19;
    }
#else
    internal const bool HasFastPext = false;
#endif
}
