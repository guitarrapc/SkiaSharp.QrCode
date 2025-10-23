[MemoryDiagnoser]
public class SerializeSimple
{
    private QRCodeData _qrCode = default!;
    private byte[] _rawData = default!;

    public SerializeSimple()
    {
        var qr = QRCodeGenerator.CreateQrCode("https://example.com/foobar", ECCLevel.L);
        _qrCode = qr;
        _rawData = qr.GetRawData();
    }

    [Benchmark]
    public void Serialize()
    {
        _qrCode.GetRawData();
    }

    [Benchmark]
    public void Deserialize()
    {
        _ = new QRCodeData(_rawData, quietZoneSize: 0);
    }
}
