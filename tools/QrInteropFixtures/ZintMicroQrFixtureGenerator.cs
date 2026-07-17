using System.Text;
using ZXingCpp;

namespace QrInteropFixtures;

/// <summary>
/// Micro QR fixture generator backed by libzint — zxing-cpp's writer is libzint
/// compiled into the pinned ZXingCpp native binary, so this is a zint-lineage
/// ENCODER (independent of both SkiaSharp.QrCode and the zxing-cpp reader used
/// as the sanity gate) with no external toolchain. Capability verified by
/// <c>probe-creator</c>.
/// </summary>
public sealed class ZintMicroQrFixtureGenerator : IMicroQrFixtureGenerator
{
    public string Name => "zint-libzint";

    public bool IsAvailable
    {
        get
        {
            try
            {
                using var barcode = new BarcodeCreator(BarcodeFormat.MicroQRCode).From("0");
                return barcode.IsValid;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>libzint (through the ZXingCpp wrapper) rejects non-ASCII input for Micro QR.</summary>
    public bool SupportsCase(MicroQrFixtureCaseDefinition caseDefinition) => !caseDefinition.Utf8;

    public GeneratedFixture Generate(MicroQrFixtureCaseDefinition caseDefinition)
    {
        // M1 has no selectable ECC level (error detection only) — pin the version
        // and let libzint apply the implicit level.
        var options = caseDefinition.ErrorCorrectionLevel == "ErrorDetectionOnly"
            ? $"version={caseDefinition.Version}"
            : $"version={caseDefinition.Version},ecLevel={caseDefinition.ErrorCorrectionLevel}";

        var creator = new BarcodeCreator(BarcodeFormat.MicroQRCode) { Options = options };
        using var barcode = creator.From(caseDefinition.PayloadText);

        // Scale 1 without quiet zones yields the module-exact symbol.
        using var image = barcode.ToImage(new WriterOptions { Scale = 1, AddQuietZones = false });
        var size = image.Width;
        var expectedSize = 9 + 2 * caseDefinition.Version;
        if (image.Height != size || size != expectedSize)
            throw new InvalidOperationException($"libzint produced a {image.Width}x{image.Height} symbol for case {caseDefinition.Id}, expected {expectedSize}x{expectedSize}.");

        var pixels = image.ToArray();
        var modules = new byte[size * size];
        for (var i = 0; i < modules.Length; i++)
        {
            modules[i] = pixels[i] < 128 ? (byte)1 : (byte)0; // luminance: dark < 128 -> module = 1
        }

        var manifest = new FixtureManifest
        {
            Id = caseDefinition.Id,
            Generator = Name,
            GeneratorVersion = $"ZXingCpp {typeof(BarcodeCreator).Assembly.GetName().Version?.ToString(3) ?? "unknown"} bundled libzint",
            SymbolType = "MicroQR",
            Version = caseDefinition.Version,
            Width = size,
            Height = size,
            ErrorCorrectionLevel = caseDefinition.ErrorCorrectionLevel,
            Mode = caseDefinition.Mode,
            MaskPattern = -1, // filled in by the sanity gate from the zxing-cpp reader
            PayloadText = caseDefinition.PayloadText,
            PayloadUtf8Hex = Convert.ToHexString(Encoding.UTF8.GetBytes(caseDefinition.PayloadText)),
            EciCharset = null, // Micro QR has no ECI
            QuietZoneModules = FixtureWriter.MicroQrQuietZoneModules,
            PixelsPerModule = FixtureWriter.PixelsPerModule,
        };

        return new GeneratedFixture(manifest, modules);
    }
}
