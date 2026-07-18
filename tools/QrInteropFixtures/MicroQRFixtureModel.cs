namespace QrInteropFixtures;

/// <summary>
/// Input definition of one Micro QR corpus case. Version and ECC are pinned per
/// case (the corpus enumerates every version × legal ECC combination), and the
/// expected mode is declared explicitly because external encoders choose their
/// own segmentation — the manifest mode is informational, not asserted.
/// </summary>
/// <param name="ErrorCorrectionLevel">"ErrorDetectionOnly" (M1), "L", "M" or "Q" — the MicroQREccLevel name.</param>
/// <param name="Version">Micro QR version 1-4 (M1-M4), requested from the generator.</param>
/// <param name="Mode">"Numeric", "Alphanumeric" or "Byte".</param>
public sealed record MicroQRFixtureCaseDefinition(string Id, string PayloadText, string ErrorCorrectionLevel, int Version, string Mode, bool Utf8 = false);

/// <summary>A Micro QR fixture generator backed by one external encoder implementation.</summary>
public interface IMicroQRFixtureGenerator
{
    /// <summary>Directory name under Fixtures/MicroQR/ (e.g. "zint-libzint").</summary>
    string Name { get; }

    /// <summary>False when the backing toolchain is not present on this machine.</summary>
    bool IsAvailable { get; }

    /// <summary>False when this generator cannot produce the case (e.g. libzint rejects UTF-8 payloads).</summary>
    bool SupportsCase(MicroQRFixtureCaseDefinition caseDefinition);

    GeneratedFixture Generate(MicroQRFixtureCaseDefinition caseDefinition);
}
