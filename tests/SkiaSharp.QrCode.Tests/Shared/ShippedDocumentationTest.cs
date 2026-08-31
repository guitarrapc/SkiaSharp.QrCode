using System.Reflection;
using System.Xml.Linq;

namespace SkiaSharp.QrCode.Tests;

/// <summary>
/// The XML documentation file that ships beside the assembly, which is what gives consumers
/// IntelliSense. The build strips the <c>Internals</c> namespace out of it before it reaches
/// the output directory, so what the test project receives is what NuGet packs.
/// </summary>
/// <remarks>
/// A shape test, not a behaviour test. The strip runs in an MSBuild target keyed on a
/// namespace string, and nothing else in the build would notice if it stopped matching:
/// the build still succeeds, the package still validates, and the only symptom is internal
/// design notes appearing in a published artifact.
/// </remarks>
public class ShippedDocumentationTest
{
    private static string DocumentationPath => Path.Combine(AppContext.BaseDirectory, "SkiaSharp.QrCode.xml");

    private static IEnumerable<string> MemberIds()
        => XDocument.Load(DocumentationPath)
            .Descendants("member")
            .Select(m => (string?)m.Attribute("name"))
            .Where(name => name is not null)!;

    /// <summary>
    /// Without this file a consumer sees bare signatures and no documentation at all, which
    /// is the state the library shipped in through 1.1.1.
    /// </summary>
    [Test]
    public async Task DocumentationFile_ShipsBesideTheAssembly()
    {
        await Assert.That(File.Exists(DocumentationPath)).IsTrue()
            .Because($"expected the XML documentation at {DocumentationPath}");
    }

    /// <summary>
    /// The strip is keyed on a namespace prefix, so it fails silently: a renamed namespace or
    /// a broken prefix removes nothing and the build stays green.
    /// </summary>
    [Test]
    public async Task Documentation_CarriesNoInternalsEntries()
    {
        var internals = MemberIds()
            .Where(name => name.Length > 2 && name[1] == ':'
                && name[2..].StartsWith("SkiaSharp.QrCode.Internals.", StringComparison.Ordinal))
            .ToArray();

        await Assert.That(internals).IsEmpty()
            .Because($"{internals.Length} Internals entries survived the strip, starting with: {string.Join(", ", internals.Take(3))}");
    }

    /// <summary>
    /// The other direction: a strip that removed too much, or a public type that was never
    /// documented, both leave a consumer hovering a name and seeing nothing.
    /// </summary>
    [Test]
    public async Task EveryExportedType_IsDocumented()
    {
        var documented = MemberIds()
            .Where(name => name.StartsWith("T:", StringComparison.Ordinal))
            .Select(name => name[2..])
            .ToHashSet(StringComparer.Ordinal);

        var missing = typeof(QRCodeData).Assembly.GetExportedTypes()
            .Select(t => (t.FullName ?? t.Name).Replace('+', '.'))
            .Where(name => !documented.Contains(name))
            .ToArray();

        await Assert.That(missing).IsEmpty()
            .Because($"exported but undocumented: {string.Join(", ", missing)}");
    }
}
