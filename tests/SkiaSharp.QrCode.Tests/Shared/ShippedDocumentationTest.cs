using System.Reflection;
using System.Xml.Linq;

namespace SkiaSharp.QrCode.Tests;

/// <summary>
/// The XML documentation file that ships beside the assembly, which is what gives consumers
/// IntelliSense. The build filters it down to the externally reachable surface before it
/// reaches the output directory, so what the test project receives is what NuGet packs.
/// </summary>
/// <remarks>
/// A shape test, not a behaviour test. The filter runs in an MSBuild target (see
/// <c>FilterNonPublicDocumentation</c> in SkiaSharp.QrCode.csproj) and nothing else in the
/// build would notice if it stopped working: the build still succeeds, the package still
/// validates, and the only symptom is internal design notes appearing in a published
/// artifact. The two membership tests are that target's guard rails, one per direction - it
/// must not stop removing enough, and it must never remove too much.
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
    /// Nothing in the shipped file may describe a member a caller cannot reach. This is the
    /// direction that regresses silently, and the reason the filter reads visibility out of
    /// the assembly rather than matching a namespace string: an internal type outside the
    /// <c>Internals</c> namespace (<c>EciModeExtensions</c> was one) and a private helper on a
    /// public type both look like public API to a name rule, and both were shipping - the
    /// second kind spelling internal type names into its signature.
    /// </summary>
    [Test]
    public async Task ShippedXml_DocumentsNothingACallerCannotReach()
    {
        var visible = VisibleDocIds();
        var unreachable = MemberIds().Where(name => !visible.Contains(name)).ToArray();

        await Assert.That(unreachable).IsEmpty()
            .Because($"the shipped XML describes {unreachable.Length} member(s) no caller can reach, starting with: {string.Join(", ", unreachable.Take(5))}");
    }

    /// <summary>
    /// The other direction: a filter that removed too much, or a public type that was never
    /// documented, both leave a consumer hovering a name and seeing nothing.
    /// </summary>
    /// <remarks>
    /// Types rather than every member: a type is documented here without exception, so the
    /// invariant is exact, and any filter broad enough to drop a member is broad enough to
    /// drop the types around it.
    /// </remarks>
    [Test]
    public async Task ShippedXml_KeepsDocumentationForEveryPublicType()
    {
        var documented = MemberIds().ToHashSet(StringComparer.Ordinal);

        var undocumented = VisibleTypes()
            .Select(t => $"T:{DocTypeName(t)}")
            .Where(id => !documented.Contains(id))
            .ToArray();

        await Assert.That(undocumented).IsEmpty()
            .Because($"{undocumented.Length} publicly reachable type(s) have no documentation, starting with: {string.Join(", ", undocumented.Take(5))}");
    }

    /// <summary>
    /// Types reachable from outside the assembly. Computed rather than taken from
    /// <c>GetExportedTypes</c>, which excludes nested protected types: those are reachable by
    /// deriving from their container, so their documentation belongs in the package.
    /// </summary>
    private static IEnumerable<Type> VisibleTypes() => typeof(QRCodeData).Assembly.GetTypes().Where(IsVisibleType);

    private static bool IsVisibleType(Type type) => type.IsNested
        ? (type.IsNestedPublic || type.IsNestedFamily || type.IsNestedFamORAssem) && IsVisibleType(type.DeclaringType!)
        : type.IsPublic;

    /// <summary>
    /// Documentation IDs for every externally reachable type and member. Protected and
    /// protected-internal count as reachable (a consumer gets at them by deriving); internal
    /// and private-protected do not, since this assembly's only friend is the test project.
    /// </summary>
    /// <remarks>
    /// The ID encoding below mirrors the one the filter uses in SkiaSharp.QrCode.csproj and
    /// the one in <c>tools/public_api.cs</c>. All three spell the same compiler rule (ECMA-334
    /// Annex E) and none can reference the others, so the duplication is deliberate: this copy
    /// is the independent check on the copy that does the removing.
    /// </remarks>
    private static HashSet<string> VisibleDocIds()
    {
        const BindingFlags declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var type in VisibleTypes())
        {
            ids.Add($"T:{DocTypeName(type)}");

            foreach (var field in type.GetFields(declared).Where(f => f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly))
                ids.Add($"F:{DocTypeName(type)}.{field.Name}");

            foreach (var evt in type.GetEvents(declared).Where(e => IsVisibleMethod(e.AddMethod) || IsVisibleMethod(e.RemoveMethod)))
                ids.Add($"E:{DocTypeName(type)}.{evt.Name}");

            foreach (var property in type.GetProperties(declared).Where(p => IsVisibleMethod(p.GetMethod) || IsVisibleMethod(p.SetMethod)))
            {
                var indexers = property.GetIndexParameters();
                var arguments = indexers.Length == 0 ? "" : $"({string.Join(",", indexers.Select(p => DocParam(p.ParameterType)))})";
                ids.Add($"P:{DocTypeName(type)}.{property.Name}{arguments}");
            }

            foreach (var method in type.GetMembers(declared).OfType<MethodBase>().Where(IsVisibleMethod))
                ids.Add(DocIdOfMethod(method));
        }

        return ids;
    }

    private static bool IsVisibleMethod(MethodBase? method) => method is not null && (method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly);

    private static string DocIdOfMethod(MethodBase method)
    {
        var name = method is ConstructorInfo ? "#ctor" : method.Name;
        var arity = method.IsGenericMethodDefinition ? $"``{method.GetGenericArguments().Length}" : "";
        var parameters = method.GetParameters();
        var arguments = parameters.Length == 0 ? "" : $"({string.Join(",", parameters.Select(p => DocParam(p.ParameterType)))})";
        var conversion = method is MethodInfo { Name: "op_Implicit" or "op_Explicit" } m ? $"~{DocParam(m.ReturnType)}" : "";
        return $"M:{DocTypeName(method.DeclaringType!)}.{name}{arity}{arguments}{conversion}";
    }

    private static string DocTypeName(Type type) => (type.FullName ?? type.Name).Replace('+', '.');

    private static string DocParam(Type type)
    {
        if (type.IsByRef) return $"{DocParam(type.GetElementType()!)}@";
        if (type.IsPointer) return $"{DocParam(type.GetElementType()!)}*";
        if (type.IsArray) return $"{DocParam(type.GetElementType()!)}[{new string(',', type.GetArrayRank() - 1)}]";
        if (type.IsGenericParameter) return type.DeclaringMethod is null ? $"`{type.GenericParameterPosition}" : $"``{type.GenericParameterPosition}";

        if (type.IsConstructedGenericType)
        {
            var bare = DocTypeName(type.GetGenericTypeDefinition());
            var tick = bare.IndexOf('`');
            if (tick >= 0) bare = bare[..tick];
            return $"{bare}{{{string.Join(",", type.GetGenericArguments().Select(DocParam))}}}";
        }

        return DocTypeName(type);
    }
}
