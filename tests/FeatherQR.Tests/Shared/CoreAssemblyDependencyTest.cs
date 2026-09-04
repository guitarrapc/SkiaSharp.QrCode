using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;

namespace FeatherQR.Tests;

/// <summary>
/// The split's regression guard: the core assembly references no SkiaSharp, on every target
/// framework it ships for. A single <c>using SkiaSharp;</c> that slips into the core would
/// pass every functional test (the test host has SkiaSharp loaded) and reach consumers as a
/// native dependency they installed FeatherQR to avoid.
/// </summary>
/// <remarks>
/// Read from metadata, not from the loaded assembly: the loaded one is the test host's
/// framework only, and a framework-conditional reference (<c>#if NETSTANDARD2_0</c>) would
/// hide there. The four builds are found beside the core project, which <c>dotnet build</c>
/// of the solution produces before <c>dotnet test</c> runs.
/// </remarks>
public class CoreAssemblyDependencyTest
{
    private const string CoreAssemblyName = "FeatherQR";

    /// <summary>The build of the core assembly for one target framework, as it sits in the project's output.</summary>
    public static IEnumerable<Func<string>> CoreTargetFrameworks()
    {
        var project = Path.Combine(RepositoryRoot(), "src", CoreAssemblyName, CoreAssemblyName + ".csproj");
        var frameworks = XDocument.Load(project)
            .Descendants("TargetFrameworks")
            .Select(e => e.Value)
            .Single()
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var tfm in frameworks)
            yield return () => tfm;
    }

    /// <summary>
    /// The one loaded into this test host. Cheap, unconditional, and the one that runs even
    /// when only the test project was built.
    /// </summary>
    [Test]
    public async Task LoadedCoreAssembly_HasNoSkiaSharpReference()
    {
        var references = typeof(QRCodeData).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToArray();

        await Assert.That(references.Where(IsSkiaSharp)).IsEmpty()
            .Because($"the core assembly must not depend on SkiaSharp; it references: {string.Join(", ", references)}");
    }

    /// <summary>
    /// Every target framework's build, read from its metadata tables.
    /// </summary>
    [Test]
    [MethodDataSource(nameof(CoreTargetFrameworks))]
    public async Task CoreAssembly_HasNoSkiaSharpReference_OnEveryTargetFramework(string targetFramework)
    {
        var path = FindCoreBuild(targetFramework);
        await Assert.That(path).IsNotNull()
            .Because($"no {CoreAssemblyName}.dll for {targetFramework} under src/{CoreAssemblyName}/bin; build the solution (dotnet build) before running the tests");

        using var stream = File.OpenRead(path!);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var references = metadata.AssemblyReferences
            .Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))
            .ToArray();

        await Assert.That(references.Where(IsSkiaSharp)).IsEmpty()
            .Because($"{Path.GetFileName(path)} ({targetFramework}) must not depend on SkiaSharp; it references: {string.Join(", ", references)}");
    }

    private static bool IsSkiaSharp(string assemblyName)
        => assemblyName.Equals("SkiaSharp", StringComparison.OrdinalIgnoreCase)
        || assemblyName.StartsWith("SkiaSharp.", StringComparison.OrdinalIgnoreCase);

    /// <summary>Prefers the configuration this test host was built with, then any other.</summary>
    private static string? FindCoreBuild(string targetFramework)
    {
        var bin = Path.Combine(RepositoryRoot(), "src", CoreAssemblyName, "bin");
        if (!Directory.Exists(bin))
            return null;

        var configuration = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)))) ?? "";
        var candidates = Directory.EnumerateDirectories(bin)
            .OrderBy(dir => string.Equals(Path.GetFileName(dir), configuration, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .Select(dir => Path.Combine(dir, targetFramework, CoreAssemblyName + ".dll"));
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root (Directory.Build.props) not found above " + AppContext.BaseDirectory);
    }
}
