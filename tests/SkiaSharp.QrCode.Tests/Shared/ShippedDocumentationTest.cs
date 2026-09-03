using System.Xml.Linq;

namespace SkiaSharp.QrCode.Tests;

/// <summary>
/// The XML documentation file that ships beside the assembly, which is what gives consumers
/// IntelliSense.
/// </summary>
/// <remarks>
/// Trimming it to the reachable surface is tools/filter_public_docs.cs, which the workflows
/// run after the build and which checks its own work. Only what holds on an untrimmed file
/// belongs here, or a local build would be red by default.
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
    /// Every publicly reachable type is documented. Undocumented, or dropped by an over-eager
    /// trim, both leave a consumer hovering a name and seeing nothing.
    /// </summary>
    /// <remarks>
    /// Nested protected types count, so this walks the assembly rather than calling
    /// <c>GetExportedTypes</c>, which leaves them out.
    /// </remarks>
    [Test]
    public async Task EveryPublicType_IsDocumented()
    {
        var documented = MemberIds().ToHashSet(StringComparer.Ordinal);

        var undocumented = typeof(QRCodeData).Assembly.GetTypes()
            .Where(IsVisibleType)
            .Select(type => $"T:{(type.FullName ?? type.Name).Replace('+', '.')}")
            .Where(id => !documented.Contains(id))
            .ToArray();

        await Assert.That(undocumented).IsEmpty()
            .Because($"{undocumented.Length} publicly reachable type(s) have no documentation, starting with: {string.Join(", ", undocumented.Take(5))}");
    }

    private static bool IsVisibleType(Type type) => type.IsNested
        ? (type.IsNestedPublic || type.IsNestedFamily || type.IsNestedFamORAssem) && IsVisibleType(type.DeclaringType!)
        : type.IsPublic;
}
