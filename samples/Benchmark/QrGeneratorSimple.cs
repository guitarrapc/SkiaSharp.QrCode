[MemoryDiagnoser]
public class QrGeneratorSimple
{
    public IEnumerable<object[]> ByteTextDataSource()
    {
        ECCLevel[] eccLevels = [ECCLevel.L, ECCLevel.M, ECCLevel.Q];
        string[] texts = [
            "https://exmaple.com/foobar",
            "Medium length text with some special characters !@#$%",
            "Very long text that will definitely require multiple string concatenations and cause significant memory allocations during the QR code generation process",
            "Zorem ipsum dolor sit amet, consectetur adipiscing elit. Nullam et nunc placerat, pellentesque nisi volutpat, rhoncus massa. Pellentesque pellentesque, mi ut rutrum tincidunt, nisl felis rhoncus nisi, eu ultrices odio nulla finibus diam. Nam quis velit leo. Morbi ac tortor justo. Maecenas in lectus purus. Vestibulum varius porta congue. Nulla facilisi. Mauris feugiat tincidunt metus, vel dictum ex fermentum eget.             Aenean convallis ut libero nec laoreet. Pellentesque vel mi id odio dapibus aliquet quis ac dolor. Nunc molestie lacinia diam, vitae tincidunt nunc. Donec varius ornare lorem ac dictum. Integer at posuere neque, ut accumsan dui. Fusce nec nisi accumsan, aliquam justo ac, venenatis ex. Sed eget nunc diam. Pellentesque quis arcu volutpat, facilisis sapien vitae, pharetra tellus. Sed gravida lacus sed lacus consequat porttitor. Nam a orci interdum, sollicitudin enim id, facilisis nisl. Donec ultrices fringilla tempus. Nullam vestibulum faucibus ullamcorper est.",
        ];

        foreach (var ecc in eccLevels)
        {
            foreach (var text in texts)
            {
                yield return new object[] { text, ecc };
            }
        }
    }

    public IEnumerable<object[]> AlphanumericTextDataSource()
    {
        ECCLevel[] eccLevels = [ECCLevel.L, ECCLevel.M, ECCLevel.Q];
        string[] texts = [
            "012345678900123456789001234567890012345678900123456789001234567890",
            "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./",
        ];

        foreach (var ecc in eccLevels)
        {
            foreach (var text in texts)
            {
                yield return new object[] { text, ecc };
            }
        }
    }

    [Benchmark(Baseline = true)]
    [ArgumentsSource(nameof(ByteTextDataSource))]
    public QRCodeData TextByte(string text, ECCLevel ecc)
    {
        return QRCodeGenerator.CreateQrCode(text, ecc, quietZoneSize: 0);
    }

    [Benchmark]
    [ArgumentsSource(nameof(ByteTextDataSource))]
    public QRCodeData BinaryByte(string text, ECCLevel ecc)
    {
        return QRCodeGenerator.CreateQrCode(text.AsSpan(), ecc, quietZoneSize: 0);
    }

    //[Benchmark]
    //[ArgumentsSource(nameof(AlphanumericTextDataSource))]
    //public QRCodeData TextAlphanumeric(string text, ECCLevel ecc)
    //{
    //    return QRCodeGenerator.CreateQrCode(text, Ecc, quietZoneSize: 0);
    //}

    //[Benchmark]
    //[ArgumentsSource(nameof(AlphanumericTextDataSource))]
    //public QRCodeData BinaryAlphanumeric(string text, ECCLevel ecc)
    //{
    //    return QRCodeGenerator.CreateQrCode(text.AsSpan(), ecc, quietZoneSize: 0);
    //}
}
