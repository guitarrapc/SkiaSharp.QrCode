[MemoryDiagnoser]
public class SerializeSimple
{
    private QRCodeData _qrCode = default!;

    public IEnumerable<object[]> ByteTextDataSource()
    {
        var compressions = new Compression[] { Compression.Uncompressed, Compression.Deflate, Compression.GZip };
        var qrCode = QRCodeGenerator.CreateQrCode("https://example.com/foobar", ECCLevel.L);

        foreach (var compression in compressions)
        {
            yield return new object[] { qrCode.GetRawData(compressMode: compression), compression };
        }
    }

    public SerializeSimple()
    {
        _qrCode = QRCodeGenerator.CreateQrCode("https://example.com/foobar", ECCLevel.L);
    }

    [Benchmark]
    [Arguments(Compression.Uncompressed)]
    [Arguments(Compression.Deflate)]
    [Arguments(Compression.GZip)]
    public void Serialize(Compression compression)
    {
        _qrCode.GetRawData(compression);
    }

    [Benchmark]
    [ArgumentsSource(nameof(ByteTextDataSource))]
    public void Deserialize(byte[] rawData, Compression compression)
    {
        _ = new QRCodeData(rawData, compression, quietZoneSize: 0);
    }
}
