using SkiaSharp.QrCode.Image;

/// <summary>
/// End-to-end PNG image generation through the public Micro QR API
/// (MicroQrCodeImageBuilder.GetPngBytes). MicroQrCodeData is pre-generated in
/// setup so the measurement covers the Skia render + PNG encode path only, not
/// the Micro QR encoding itself.
///
/// Micro QR matrices are tiny (11-17 core modules), so per-image overhead
/// dominates; scenarios cover the smallest and largest versions at the default
/// 512px output plus a small 128px output typical for inline display.
/// </summary>
public class MicroQrImageEndToEnd
{
    private MicroQrCodeData _m2 = default!;
    private MicroQrCodeData _m4 = default!;
    private byte[] _m4Luminance = default!;
    private int _m4ImageSide;
    private char[] _decodeBuffer = default!;

    [GlobalSetup]
    public void Setup()
    {
        _m2 = MicroQrCodeGenerator.CreateMicroQrCode("12345", MicroQrEccLevel.L); // M2, 13x13 core
        _m4 = MicroQrCodeGenerator.CreateMicroQrCode("MICRO QR M4 BENCH", MicroQrEccLevel.M); // M4, 17x17 core

        // Grayscale of an 8px/module render for the image-decode scenario
        using var bitmap = new MicroQrCodeImageBuilder(_m4).WithModulePixelSize(8).ToBitmap();
        _m4ImageSide = bitmap.Width;
        _m4Luminance = new byte[bitmap.Width * bitmap.Height];
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                _m4Luminance[y * bitmap.Width + x] = bitmap.GetPixel(x, y).Red;
            }
        }
        _decodeBuffer = new char[MicroQrCodeDecoder.GetMaxDecodedLength(MicroQrVersion.M4)];
    }

    [Benchmark]
    public byte[] M2_512px() => MicroQrCodeImageBuilder.GetPngBytes(_m2, 512);

    [Benchmark]
    public byte[] M4_512px() => MicroQrCodeImageBuilder.GetPngBytes(_m4, 512);

    [Benchmark]
    public byte[] M4_128px() => MicroQrCodeImageBuilder.GetPngBytes(_m4, 128);

    [Benchmark]
    public bool M4_ImageDecode_Span() => MicroQrCodeDecoder.TryDecodeImage(_m4Luminance, _m4ImageSide, _m4ImageSide, _decodeBuffer, out _, out _);
}
