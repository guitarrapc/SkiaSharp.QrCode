[MemoryDiagnoser]
[ThreadingDiagnoser]
public class QrGeneratorSimple
{
    private QRCodeGenerator _generator;

    public QrGeneratorSimple()
    {
        _generator = new QRCodeGenerator();
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
    public QRCodeData MeasureAllocations(string text, ECCLevel level)
    {
        return _generator.CreateQrCode(text, level);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _generator?.Dispose();
    }
}
