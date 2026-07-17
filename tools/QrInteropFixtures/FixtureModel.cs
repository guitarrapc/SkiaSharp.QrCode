using System.Text;
using System.Text.Json;

namespace QrInteropFixtures;

/// <summary>Input definition of one corpus case, independent of any generator.</summary>
public sealed record FixtureCaseDefinition(string Id, string PayloadText, string ErrorCorrectionLevel, bool Utf8 = false);

/// <summary>
/// Manifest written as case-name.json. Field set matches the schema documented in
/// .github/docs/specs/qrcode-test-fixtures.md and the FixtureManifest loader in the
/// test project.
/// </summary>
public sealed record FixtureManifest
{
    public required string Id { get; init; }
    public required string Generator { get; init; }
    public required string GeneratorVersion { get; init; }
    public required string SymbolType { get; init; }
    public required int Version { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required string ErrorCorrectionLevel { get; init; }
    public required string Mode { get; init; }
    public int MaskPattern { get; init; } = -1;
    public required string PayloadText { get; init; }
    public required string PayloadUtf8Hex { get; init; }
    public string? EciCharset { get; init; }
    public int QuietZoneModules { get; init; }
    public int PixelsPerModule { get; init; }
}

/// <summary>One generated fixture: manifest plus the core module matrix (byte 0/1, row-major).</summary>
public sealed record GeneratedFixture(FixtureManifest Manifest, byte[] Modules);

/// <summary>A fixture generator backed by one external encoder implementation.</summary>
public interface IFixtureGenerator
{
    /// <summary>Directory name under Fixtures/StandardQr/ (e.g. "zxing-net").</summary>
    string Name { get; }

    /// <summary>False when the backing toolchain is not present on this machine.</summary>
    bool IsAvailable { get; }

    GeneratedFixture Generate(FixtureCaseDefinition caseDefinition);
}

/// <summary>Writes the three fixture files (json / matrix.txt / png) for one case.</summary>
public static class FixtureWriter
{
    public const int QuietZoneModules = 4;
    public const int MicroQrQuietZoneModules = 2; // ISO/IEC 18004: Micro QR quiet zone is 2 modules
    public const int PixelsPerModule = 8;

    private static readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static void Write(string generatorDir, GeneratedFixture fixture)
    {
        var basePath = Path.Combine(generatorDir, fixture.Manifest.Id);

        File.WriteAllText(basePath + ".json", JsonSerializer.Serialize(fixture.Manifest, jsonOptions) + "\n");
        File.WriteAllText(basePath + ".matrix.txt", RenderMatrixText(fixture));
        File.WriteAllBytes(basePath + ".png", PngRenderer.Render(fixture.Modules, fixture.Manifest.Width, fixture.Manifest.QuietZoneModules, fixture.Manifest.PixelsPerModule));
    }

    private static string RenderMatrixText(GeneratedFixture fixture)
    {
        var size = fixture.Manifest.Width;
        var sb = new StringBuilder(size * (size + 1));
        for (var row = 0; row < size; row++)
        {
            for (var col = 0; col < size; col++)
            {
                sb.Append(fixture.Modules[row * size + col] != 0 ? '1' : '0');
            }
            sb.Append('\n');
        }

        return sb.ToString();
    }
}
