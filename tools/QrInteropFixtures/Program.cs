using QrInteropFixtures;

// Regenerates the committed fixture corpus under tests/SkiaSharp.QrCode.Tests/Fixtures/.
// Fixtures are produced by external (non-SkiaSharp.QrCode) encoders so the corpus can
// serve as an independent conformance oracle. See
// .github/docs/specs/qrcode-test-fixtures.md for the format and the oracle matrix.
//
// Usage: dotnet run --project tools/QrInteropFixtures -- regenerate

var command = args.Length == 0 ? "regenerate" : args[0];
if (command == "spot-check-microqr")
{
    // Decodes SkiaSharp.QrCode-generated Micro QR symbols with zxing-cpp.
    return MicroQRSpotCheck.Run();
}
if (command == "probe-creator")
{
    // Checks whether the pinned ZXingCpp native build can create Micro QR / rMQR.
    return CreatorProbe.Run();
}
if (command != "regenerate")
{
    Console.Error.WriteLine($"Unknown command '{command}'. Usage: dotnet run --project tools/QrInteropFixtures -- [regenerate|spot-check-microqr|probe-creator]");
    return 1;
}

var repoRoot = FindRepoRoot();
var fixturesBase = Path.Combine(repoRoot, "tests", "SkiaSharp.QrCode.Tests", "Fixtures");
var standardQrRoot = Path.Combine(fixturesBase, "StandardQr");

var generators = new IFixtureGenerator[]
{
    new ZXingNetFixtureGenerator(),
    // Future generators plug in here. They require external toolchains; see the
    // oracle matrix in .github/docs/specs/qrcode-test-fixtures.md before adding one.
};

var corpus = StandardQrCorpus.Cases;
var total = 0;

foreach (var generator in generators)
{
    if (!generator.IsAvailable)
    {
        Console.WriteLine($"skip: {generator.Name} (not available on this machine)");
        continue;
    }

    var generatorDir = Path.Combine(standardQrRoot, generator.Name);
    if (Directory.Exists(generatorDir))
        Directory.Delete(generatorDir, recursive: true);
    Directory.CreateDirectory(generatorDir);

    foreach (var caseDefinition in corpus)
    {
        var fixture = generator.Generate(caseDefinition);
        FixtureWriter.Write(generatorDir, fixture);
        total++;
        Console.WriteLine($"wrote: {generator.Name}/{fixture.Manifest.Id} (version {fixture.Manifest.Version}, {fixture.Manifest.ErrorCorrectionLevel}, {fixture.Manifest.Mode})");
    }
}

// Micro QR corpus: two independent external encoder lineages (libzint via the
// pinned ZXingCpp package, and the pinned qrtool prebuilt binary). Every fixture
// passes the zxing-cpp sanity gate (decode + metadata cross-check) before it is
// written, and the gate's reader supplies the manifest mask pattern.
var microQrRoot = Path.Combine(fixturesBase, "MicroQR");
var microGenerators = new IMicroQRFixtureGenerator[]
{
    new ZintMicroQRFixtureGenerator(),
    new QrtoolMicroQRFixtureGenerator(repoRoot),
};

foreach (var generator in microGenerators)
{
    if (!generator.IsAvailable)
    {
        Console.WriteLine($"skip: {generator.Name} (not available on this machine; see tools/QrInteropFixtures/get-qrtool.ps1 for the qrtool binary)");
        continue;
    }

    var generatorDir = Path.Combine(microQrRoot, generator.Name);
    if (Directory.Exists(generatorDir))
        Directory.Delete(generatorDir, recursive: true);
    Directory.CreateDirectory(generatorDir);

    foreach (var caseDefinition in MicroQRCorpus.Cases)
    {
        if (!generator.SupportsCase(caseDefinition))
        {
            Console.WriteLine($"skip: {generator.Name}/{caseDefinition.Id} (unsupported by this generator)");
            continue;
        }

        var fixture = generator.Generate(caseDefinition);
        var mask = MicroQRSanityGate.VerifyAndGetMask(fixture);
        fixture = fixture with { Manifest = fixture.Manifest with { MaskPattern = mask } };

        FixtureWriter.Write(generatorDir, fixture);
        total++;
        Console.WriteLine($"wrote: {generator.Name}/{fixture.Manifest.Id} (M{fixture.Manifest.Version}, {fixture.Manifest.ErrorCorrectionLevel}, {fixture.Manifest.Mode}, mask {mask})");
    }
}

Console.WriteLine($"done: {total} fixtures under {fixturesBase}");
return 0;

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "SkiaSharp.QrCode.slnx")))
            return dir.FullName;
        dir = dir.Parent;
    }

    throw new InvalidOperationException("Repository root (SkiaSharp.QrCode.slnx) not found above the tool output directory.");
}
