using BenchmarkDotNet.Configs;
using SkiaSharp;

[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class SimpleEncode
{
    private string _textNumber = default!;
    private string _textAlphanumeric = default!;
    private string _textUrl = default!;
    private string _textUnicode = default!;
    private string _textWifi = default!;
    private byte[] _spanDestination = default!;
    ZXing.BarcodeWriter<SKBitmap> _zxingWriter_ascii = default!;
    ZXing.BarcodeWriter<SKBitmap> _zxingWriter_utf8 = default!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _textNumber = "0123456789012345678901234567890123456789";
        _textAlphanumeric = "0123456789ABCDEFG0123456789HIJKLMN";
        _textUrl = "https://example.com/user/repo?foo=value&bar=piyo";
        _textUnicode = "FooBar你好世界こんにちはПривет мир🎉🎊🎈Zürich";
        _textWifi = "WIFI:S:foobar-wifi;T:WPA;P:test123;H:false;;";
        // Shared destination for the span-based zero-allocation benchmarks,
        // sized for the largest of the five payloads.
        var maxBufferSize = new[] { _textNumber, _textAlphanumeric, _textUrl, _textUnicode, _textWifi }
            .Max(text => SkiaSharp.QrCode.QRCodeGenerator.GetRequiredBufferSize(text.AsSpan(), ECCLevel.L).BufferSize);
        _spanDestination = new byte[maxBufferSize];
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

    // SkiaSharp.QrCode

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("SkiaSharp.QrCode")]
    public QRCodeData SkiaSharpQrCode_Number_Encode()
    {
        return SkiaSharp.QrCode.QRCodeGenerator.CreateQrCode(_textNumber.AsSpan(), ECCLevel.L);
    }

    [Benchmark]
    [BenchmarkCategory("SkiaSharp.QrCode")]
    public QRCodeData SkiaSharpQrCode_Alphanumeric_Encode()
    {
        return SkiaSharp.QrCode.QRCodeGenerator.CreateQrCode(_textAlphanumeric.AsSpan(), ECCLevel.L);
    }

    [Benchmark]
    [BenchmarkCategory("SkiaSharp.QrCode")]
    public QRCodeData SkiaSharpQrCode_Url_Encode()
    {
        return SkiaSharp.QrCode.QRCodeGenerator.CreateQrCode(_textUrl.AsSpan(), ECCLevel.L);
    }

    [Benchmark]
    [BenchmarkCategory("SkiaSharp.QrCode")]
    public QRCodeData SkiaSharpQrCode_Unicode_Encode()
    {
        return SkiaSharp.QrCode.QRCodeGenerator.CreateQrCode(_textUnicode.AsSpan(), ECCLevel.L);
    }

    [Benchmark]
    [BenchmarkCategory("SkiaSharp.QrCode")]
    public QRCodeData SkiaSharpQrCode_Wifi_Encode()
    {
        return SkiaSharp.QrCode.QRCodeGenerator.CreateQrCode(_textWifi.AsSpan(), ECCLevel.L);
    }

    // Span-destination (zero-allocation) variants

    [Benchmark(Description = "SkiaSharpQrCode_Number_Encode (Span)")]
    [BenchmarkCategory("SkiaSharp.QrCode")]
    public int SkiaSharpQrCode_Number_EncodeSpan()
    {
        return SkiaSharp.QrCode.QRCodeGenerator.CreateQrCode(_textNumber.AsSpan(), ECCLevel.L, _spanDestination);
    }

    [Benchmark(Description = "SkiaSharpQrCode_Alphanumeric_Encode (Span)")]
    [BenchmarkCategory("SkiaSharp.QrCode")]
    public int SkiaSharpQrCode_Alphanumeric_EncodeSpan()
    {
        return SkiaSharp.QrCode.QRCodeGenerator.CreateQrCode(_textAlphanumeric.AsSpan(), ECCLevel.L, _spanDestination);
    }

    [Benchmark(Description = "SkiaSharpQrCode_Url_Encode (Span)")]
    [BenchmarkCategory("SkiaSharp.QrCode")]
    public int SkiaSharpQrCode_Url_EncodeSpan()
    {
        return SkiaSharp.QrCode.QRCodeGenerator.CreateQrCode(_textUrl.AsSpan(), ECCLevel.L, _spanDestination);
    }

    [Benchmark(Description = "SkiaSharpQrCode_Unicode_Encode (Span)")]
    [BenchmarkCategory("SkiaSharp.QrCode")]
    public int SkiaSharpQrCode_Unicode_EncodeSpan()
    {
        return SkiaSharp.QrCode.QRCodeGenerator.CreateQrCode(_textUnicode.AsSpan(), ECCLevel.L, _spanDestination);
    }

    [Benchmark(Description = "SkiaSharpQrCode_Wifi_Encode (Span)")]
    [BenchmarkCategory("SkiaSharp.QrCode")]
    public int SkiaSharpQrCode_Wifi_EncodeSpan()
    {
        return SkiaSharp.QrCode.QRCodeGenerator.CreateQrCode(_textWifi.AsSpan(), ECCLevel.L, _spanDestination);
    }

    // Net.Codecrete.QrCodeGenerator

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Net.Codecrete.QrCodeGenerator")]
    public Net.Codecrete.QrCodeGenerator.QrCode NetCodecreteQrCodeGenerator_Number_Encode()
    {
        return Net.Codecrete.QrCodeGenerator.QrCode.EncodeText(_textNumber, Net.Codecrete.QrCodeGenerator.QrCode.Ecc.Low);
    }

    [Benchmark]
    [BenchmarkCategory("Net.Codecrete.QrCodeGenerator")]
    public Net.Codecrete.QrCodeGenerator.QrCode NetCodecreteQrCodeGenerator_Alphanumeric_Encode()
    {
        return Net.Codecrete.QrCodeGenerator.QrCode.EncodeText(_textAlphanumeric, Net.Codecrete.QrCodeGenerator.QrCode.Ecc.Low);
    }

    [Benchmark]
    [BenchmarkCategory("Net.Codecrete.QrCodeGenerator")]
    public Net.Codecrete.QrCodeGenerator.QrCode NetCodecreteQrCodeGenerator_Url_Encode()
    {
        return Net.Codecrete.QrCodeGenerator.QrCode.EncodeText(_textUrl, Net.Codecrete.QrCodeGenerator.QrCode.Ecc.Low);
    }

    [Benchmark]
    [BenchmarkCategory("Net.Codecrete.QrCodeGenerator")]
    public Net.Codecrete.QrCodeGenerator.QrCode NetCodecreteQrCodeGenerator_Unicode_Encode()
    {
        return Net.Codecrete.QrCodeGenerator.QrCode.EncodeText(_textUnicode, Net.Codecrete.QrCodeGenerator.QrCode.Ecc.Low);
    }

    [Benchmark]
    [BenchmarkCategory("Net.Codecrete.QrCodeGenerator")]
    public Net.Codecrete.QrCodeGenerator.QrCode NetCodecreteQrCodeGenerator_Wifi_Encode()
    {
        return Net.Codecrete.QrCodeGenerator.QrCode.EncodeText(_textWifi, Net.Codecrete.QrCodeGenerator.QrCode.Ecc.Low);
    }

    // QRCoder

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("QRCoder")]
    public QRCoder.QRCodeData QRCoder_Number_Encode()
    {
        return QRCoder.QRCodeGenerator.GenerateQrCode(_textNumber, QRCoder.QRCodeGenerator.ECCLevel.L);
    }

    [Benchmark]
    [BenchmarkCategory("QRCoder")]
    public QRCoder.QRCodeData QRCoder_Alphanumeric_Encode()
    {
        return QRCoder.QRCodeGenerator.GenerateQrCode(_textAlphanumeric, QRCoder.QRCodeGenerator.ECCLevel.L);
    }

    [Benchmark]
    [BenchmarkCategory("QRCoder")]
    public QRCoder.QRCodeData QRCoder_Url_Encode()
    {
        return QRCoder.QRCodeGenerator.GenerateQrCode(_textUrl, QRCoder.QRCodeGenerator.ECCLevel.L);
    }

    [Benchmark]
    [BenchmarkCategory("QRCoder")]
    public QRCoder.QRCodeData QRCoder_Unicode_Encode()
    {
        return QRCoder.QRCodeGenerator.GenerateQrCode(_textUnicode, QRCoder.QRCodeGenerator.ECCLevel.L);
    }

    [Benchmark]
    [BenchmarkCategory("QRCoder")]
    public QRCoder.QRCodeData QRCoder_Wifi_Encode()
    {
        return QRCoder.QRCodeGenerator.GenerateQrCode(_textWifi, QRCoder.QRCodeGenerator.ECCLevel.L);
    }

    // Zxing

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Zxing")]
    public ZXing.Common.BitMatrix ZXing_Number_Encode()
    {
        return _zxingWriter_ascii.Encode(_textNumber);
    }

    [Benchmark]
    [BenchmarkCategory("Zxing")]
    public ZXing.Common.BitMatrix ZXing_Alphanumeric_Encode()
    {
        return _zxingWriter_ascii.Encode(_textAlphanumeric);
    }

    [Benchmark]
    [BenchmarkCategory("Zxing")]
    public ZXing.Common.BitMatrix ZXing_Url_Encode()
    {
        return _zxingWriter_ascii.Encode(_textUrl);
    }

    [Benchmark]
    [BenchmarkCategory("Zxing")]
    public ZXing.Common.BitMatrix ZXing_Unicode_Encode()
    {
        return _zxingWriter_utf8.Encode(_textUnicode);
    }

    [Benchmark]
    [BenchmarkCategory("Zxing")]
    public ZXing.Common.BitMatrix ZXing_Wifi_Encode()
    {
        return _zxingWriter_utf8.Encode(_textWifi);
    }
}
