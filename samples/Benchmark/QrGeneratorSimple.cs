[MemoryDiagnoser]
public class QrGeneratorSimple
{
    [Benchmark(Baseline = true)]
    [Arguments("https://exmaple.com/foobar", ECCLevel.L)]
    [Arguments("https://exmaple.com/foobar", ECCLevel.M)]
    [Arguments("https://exmaple.com/foobar", ECCLevel.Q)]
    [Arguments("Medium length text with some special characters !@#$%", ECCLevel.L)]
    [Arguments("Medium length text with some special characters !@#$%", ECCLevel.M)]
    [Arguments("Medium length text with some special characters !@#$%", ECCLevel.Q)]
    [Arguments("Very long text that will definitely require multiple string concatenations and cause significant memory allocations during the QR code generation process", ECCLevel.L)]
    [Arguments("Very long text that will definitely require multiple string concatenations and cause significant memory allocations during the QR code generation process", ECCLevel.M)]
    [Arguments("Very long text that will definitely require multiple string concatenations and cause significant memory allocations during the QR code generation process", ECCLevel.Q)]
    public QRCodeData Text(string text, ECCLevel level)
    {
        return QRCodeGenerator.CreateQrCode(text, level);
    }

    [Benchmark]
    [Arguments("https://exmaple.com/foobar", ECCLevel.L)]
    [Arguments("https://exmaple.com/foobar", ECCLevel.M)]
    [Arguments("https://exmaple.com/foobar", ECCLevel.Q)]
    [Arguments("Medium length text with some special characters !@#$%", ECCLevel.L)]
    [Arguments("Medium length text with some special characters !@#$%", ECCLevel.M)]
    [Arguments("Medium length text with some special characters !@#$%", ECCLevel.Q)]
    [Arguments("Very long text that will definitely require multiple string concatenations and cause significant memory allocations during the QR code generation process", ECCLevel.L)]
    [Arguments("Very long text that will definitely require multiple string concatenations and cause significant memory allocations during the QR code generation process", ECCLevel.M)]
    [Arguments("Very long text that will definitely require multiple string concatenations and cause significant memory allocations during the QR code generation process", ECCLevel.Q)]
    public QRCodeData Binary(string text, ECCLevel level)
    {
        return QRCodeGenerator.CreateQrCode(text.AsSpan(), level);
    }
}
