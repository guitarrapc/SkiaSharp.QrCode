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
    return MicroQrSpotCheck.Run();
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
var fixtureRoot = Path.Combine(repoRoot, "tests", "SkiaSharp.QrCode.Tests", "Fixtures", "StandardQr");

var generators = new IFixtureGenerator[]
{
    new ZXingNetFixtureGenerator(),
    // Future generators (zint CLI, qrcode rust crates, zxing-cpp) plug in here.
    // They require external toolchains; see the oracle matrix in
    // .github/docs/specs/qrcode-test-fixtures.md before adding one.
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

    var generatorDir = Path.Combine(fixtureRoot, generator.Name);
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

Console.WriteLine($"done: {total} fixtures under {fixtureRoot}");
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
