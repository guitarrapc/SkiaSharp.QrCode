using ZXingCpp;

namespace QRInteropFixtures;

/// <summary>
/// Diagnostic: checks whether the pinned ZXingCpp native build can CREATE
/// Micro QR / rMQR symbols (zxing-cpp's writer is backed by libzint — if this
/// works, a zint-lineage encoder oracle is available with no extra toolchain).
/// </summary>
public static class CreatorProbe
{
    public static int Run()
    {
        Probe(BarcodeFormat.MicroQRCode, "01234567");
        Probe(BarcodeFormat.RMQRCode, "0123456789");
        Probe(BarcodeFormat.QRCode, "01234567");
        return 0;
    }

    private static void Probe(BarcodeFormat format, string text)
    {
        try
        {
            var creator = new BarcodeCreator(format);
            using var barcode = creator.From(text);

            // Round-trip through the reader to prove the created symbol is real.
            using var image = barcode.ToImage(new WriterOptions { Scale = 4 });
            var imageView = new ImageView(image.ToArray(), image.Width, image.Height, image.Format);
            var results = new BarcodeReader { Formats = format, TryHarder = true }.From(imageView);
            var roundTrip = results.Length == 1 && results[0].Text == text ? "round-trip OK" : "ROUND-TRIP FAILED";
            Console.WriteLine($"{format}: created ({barcode.Position}), {roundTrip}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{format}: NOT AVAILABLE ({ex.GetType().Name}: {ex.Message})");
        }
    }
}
