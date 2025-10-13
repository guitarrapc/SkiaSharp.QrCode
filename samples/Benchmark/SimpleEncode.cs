using SkiaSharp;

[MemoryDiagnoser]
public class SimpleEncode
{
    private string _textUrl = default!;
    private string _textUnicode = default!;
    ZXing.BarcodeWriter<SKBitmap> _zxingWriter_ascii = default!;
    ZXing.BarcodeWriter<SKBitmap> _zxingWriter_utf8 = default!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _textUrl = "https://example.com/user/repo?foo=value&bar=piyo";
        _textUnicode = "你好世界こんにちはПривет мир🎉🎊🎈Zürich";
        _zxingWriter_ascii = new()
        {
            Format = ZXing.BarcodeFormat.QR_CODE,
            Options = new ZXing.QrCode.QrCodeEncodingOptions()
            {
                DisableECI = true,
                CharacterSet = "ISO-8859-1"
            },
        };
        _zxingWriter_utf8 = new()
        {
            Format = ZXing.BarcodeFormat.QR_CODE,
            Options = new ZXing.QrCode.QrCodeEncodingOptions()
            {
                DisableECI = true,
                CharacterSet = "UTF8"
            },
        };
    }

    [Benchmark(Baseline = true)]
    public QRCodeData SkiaSharpQRCode_Url_Encode()
    {
        return SkiaSharp.QrCode.QRCodeGenerator.CreateQrCode(_textUrl.AsSpan(), ECCLevel.L);
    }

    [Benchmark]
    public QRCoder.QRCodeData QRCoder_Url_Encode()
    {
        return QRCoder.QRCodeGenerator.GenerateQrCode(_textUrl, QRCoder.QRCodeGenerator.ECCLevel.L);
    }

    [Benchmark]
    public ZXing.Common.BitMatrix ZXing_Url_Encode()
    {
        return _zxingWriter_ascii.Encode(_textUrl);
    }

    [Benchmark]
    public QRCodeData SkiaSharpQRCode_Unicode_Encode()
    {
        return SkiaSharp.QrCode.QRCodeGenerator.CreateQrCode(_textUnicode.AsSpan(), ECCLevel.L);
    }

    [Benchmark]
    public QRCoder.QRCodeData QRCoder_Unicode_Encode()
    {
        return QRCoder.QRCodeGenerator.GenerateQrCode(_textUnicode, QRCoder.QRCodeGenerator.ECCLevel.L);
    }

    [Benchmark]
    public ZXing.Common.BitMatrix ZXing_Unicode_Encode()
    {
        return _zxingWriter_utf8.Encode(_textUnicode);
    }
}
