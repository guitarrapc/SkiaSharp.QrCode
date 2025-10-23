using BenchmarkDotNet.Configs;
using System.Buffers;

[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class SimpleSerialize
{
    private QRCodeData _qrDataV10 = default!;
    private QRCodeData _qrDataV30 = default!;
    private QRCodeData _qrDataV60 = default!;

    private byte[] _rawDataV10 = default!;
    private byte[] _rawDataV30 = default!;
    private byte[] _rawDataV60 = default!;

    private ArrayBufferWriter<byte> _writerV10 = default!;
    private ArrayBufferWriter<byte> _writerV30 = default!;
    private ArrayBufferWriter<byte> _writerV60 = default!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _qrDataV10 = QRCodeGenerator.CreateQrCode("1234ABCDef", ECCLevel.L);
        _qrDataV30 = QRCodeGenerator.CreateQrCode("https://example.com/foobaravcd", ECCLevel.L);
        _qrDataV60 = QRCodeGenerator.CreateQrCode("https://example.com/foobar12345/path?id=123&foo=true&bar=ABC", ECCLevel.L);

        // Expand buffer to the max size needed
        _writerV10 = new ArrayBufferWriter<byte>(_qrDataV10.GetRawDataSize());
        _writerV30 = new ArrayBufferWriter<byte>(_qrDataV30.GetRawDataSize());
        _writerV60 = new ArrayBufferWriter<byte>(_qrDataV60.GetRawDataSize());

        _rawDataV10 = _qrDataV10.GetRawData();
        _rawDataV30 = _qrDataV30.GetRawData();
        _rawDataV60 = _qrDataV60.GetRawData();
    }

    [Benchmark]
    [BenchmarkCategory("Array")]
    public void Serialize_Array_V10()
    {
        _qrDataV10.GetRawData();
    }

    [Benchmark]
    [BenchmarkCategory("BufferWriter")]
    public void Serialize_BufferWriter_V10()
    {
        _writerV10.Clear();
        _qrDataV10.GetRawData(_writerV10);
    }

    [Benchmark]
    [BenchmarkCategory("Array")]
    public void Serialize_Array_V30()
    {
        _qrDataV30.GetRawData();
    }

    [Benchmark]
    [BenchmarkCategory("BufferWriter")]
    public void Serialize_BufferWriter_V30()
    {
        _writerV30.Clear();
        _qrDataV30.GetRawData(_writerV30);
    }

    [Benchmark]
    [BenchmarkCategory("Array")]
    public void Serialize_Array_V60()
    {
        _qrDataV60.GetRawData();
    }

    [Benchmark]
    [BenchmarkCategory("BufferWriter")]
    public void Serialize_BufferWriter_V60()
    {
        _writerV60.Clear();
        _qrDataV60.GetRawData(_writerV60);
    }

    [Benchmark]
    public void Deserialize_Span_V10()
    {
        _ = new QRCodeData(_rawDataV10.AsSpan(), quietZoneSize: 0);
    }

    [Benchmark]
    public void Deserialize_Array_V10()
    {
        _ = new QRCodeData(_rawDataV10, quietZoneSize: 0);
    }

    [Benchmark]
    public void Deserialize_Span_V30()
    {
        _ = new QRCodeData(_rawDataV30.AsSpan(), quietZoneSize: 0);
    }

    [Benchmark]
    public void Deserialize_Array_V30()
    {
        _ = new QRCodeData(_rawDataV30, quietZoneSize: 0);
    }

    [Benchmark]
    public void Deserialize_Span_V60()
    {
        _ = new QRCodeData(_rawDataV60.AsSpan(), quietZoneSize: 0);
    }

    [Benchmark]
    public void Deserialize_Array_V60()
    {
        _ = new QRCodeData(_rawDataV60, quietZoneSize: 0);
    }
}
