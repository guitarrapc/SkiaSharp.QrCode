#:sdk Microsoft.NET.Sdk
#:property TargetFramework=net10.0

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;

// Trims an XML documentation file down to the surface a consumer can actually reach.
//
//   dotnet run tools/filter_public_docs.cs -- <assembly> <documentation.xml>
//
// The library's build runs this on each target framework's compiler output before it is
// copied out of obj, so the file the package carries only describes callable API. It is
// wired up in src/SkiaSharp.QrCode/PublicDocumentation.targets.
//
// Why it exists. The compiler documents every member carrying a doc comment, internal and
// private ones included, and this assembly comments its internals heavily: left alone a
// sixth of the entries describe something no caller can name, and private helpers on public
// types spell internal type names into their signatures - EciModeExtensions and ModeSegment
// were both in a published file. A name rule cannot see either case: an internal type
// outside the Internals namespace and a private method on a public type both look like
// public API to a string match. Visibility has to come from the compiled assembly.
//
// Why metadata rather than reflection. Reading the tables takes no dependency (the
// framework ships System.Reflection.Metadata) and never loads the assembly, so nothing has
// to resolve SkiaSharp or lock a file the build is about to overwrite. Only names and
// accessibility flags are needed, so no signature is ever decoded.
//
// ShippedDocumentationTest guards the result in both directions: that nothing unreachable
// survives, and that no public type loses its documentation.

// The build compiles this script once up front by running it with nothing to do, so that the
// per-framework builds that follow only launch it. Without that they race to compile it into
// the shared cache under %TEMP%\dotnet\runfile and two clean builds in three fail on a locked
// file. There is no "dotnet build --file", which is why the warm-up is a run.
if (args is ["--warmup"]) return 0;

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: filter_public_docs <assembly> <documentation.xml>");
    return 2;
}

var assemblyPath = args[0];
var documentationPath = args[1];

XDocument document;
try
{
    document = XDocument.Load(documentationPath);
}
catch (System.Xml.XmlException e)
{
    // A half-written file (an interrupted build, a full disk) would fail every later build
    // the same way, because the compiler stays up to date and never regenerates it. Deleting
    // it is what makes the failure recoverable: the next build recompiles.
    File.Delete(documentationPath);
    Console.Error.WriteLine($"{documentationPath} was not valid XML ({e.Message}). It has been deleted; run the build again to regenerate it.");
    return 1;
}

using var stream = File.OpenRead(assemblyPath);
using var peReader = new PEReader(stream);
var reader = peReader.GetMetadataReader();

// Keys are "T:Full.Type.Name" and "Full.Type.Name.MemberName" - deliberately not the
// compiler's full documentation ID, which would mean re-encoding every parameter type.
// Overloads therefore share a key, so a private overload of a public name survives. That is
// the safe direction: this filter may keep too much, never too little.
var visible = new HashSet<string>(StringComparer.Ordinal);

foreach (var handle in reader.TypeDefinitions)
{
    var type = reader.GetTypeDefinition(handle);
    if (!IsVisibleType(type)) continue;

    var typeName = FullName(type);
    visible.Add($"T:{typeName}");

    foreach (var methodHandle in type.GetMethods())
    {
        var method = reader.GetMethodDefinition(methodHandle);
        if (!IsVisible(method.Attributes)) continue;
        var name = reader.GetString(method.Name);
        visible.Add($"{typeName}.{(name is ".ctor" or ".cctor" ? "#ctor" : name)}");
    }

    foreach (var fieldHandle in type.GetFields())
    {
        var field = reader.GetFieldDefinition(fieldHandle);
        if ((field.Attributes & FieldAttributes.FieldAccessMask) is FieldAttributes.Public or FieldAttributes.Family or FieldAttributes.FamORAssem)
            visible.Add($"{typeName}.{reader.GetString(field.Name)}");
    }

    // Properties and events carry no accessibility of their own; theirs is whatever their
    // accessors have, and one visible accessor is enough to make the member reachable.
    foreach (var propertyHandle in type.GetProperties())
    {
        var property = reader.GetPropertyDefinition(propertyHandle);
        var accessors = property.GetAccessors();
        if (IsVisibleAccessor(accessors.Getter) || IsVisibleAccessor(accessors.Setter))
            visible.Add($"{typeName}.{reader.GetString(property.Name)}");
    }

    foreach (var eventHandle in type.GetEvents())
    {
        var declared = reader.GetEventDefinition(eventHandle);
        var accessors = declared.GetAccessors();
        if (IsVisibleAccessor(accessors.Adder) || IsVisibleAccessor(accessors.Remover))
            visible.Add($"{typeName}.{reader.GetString(declared.Name)}");
    }
}

// An empty surface means the read went wrong, not that the library has no API. Saying so
// beats silently writing back a file with every entry removed.
if (visible.Count == 0)
{
    Console.Error.WriteLine($"Read no reachable surface out of {Path.GetFileName(assemblyPath)}; refusing to empty {Path.GetFileName(documentationPath)}.");
    return 1;
}

var members = document.Descendants("member").ToList();
var unreachable = members.Where(member => !visible.Contains(KeyOf((string?)member.Attribute("name")))).ToList();

// Only rewrite when there is something to remove, so a build that changed nothing leaves the
// file, and its timestamp, alone.
if (unreachable.Count > 0)
{
    foreach (var member in unreachable) member.Remove();
    document.Save(documentationPath);
}

Console.WriteLine($"{Path.GetFileName(documentationPath)}: kept {members.Count - unreachable.Count} reachable member(s), removed {unreachable.Count} unreachable one(s).");
return 0;

// "M:Ns.Type`1.Method``1(System.Int32@)~T" -> "Ns.Type`1.Method". Type arity is a single
// backtick and belongs to the metadata name, so only the method's double one is cut.
string KeyOf(string? id)
{
    if (id is null || id.Length < 3 || id[1] != ':') return "";
    var body = id[2..];
    var paren = body.IndexOf('(');
    if (paren >= 0) body = body[..paren];
    if (id[0] == 'T') return $"T:{body}";
    var arity = body.LastIndexOf("``", StringComparison.Ordinal);
    return arity >= 0 ? body[..arity] : body;
}

// Documentation IDs spell nesting with a dot, the same as a namespace.
string FullName(TypeDefinition type)
{
    var name = reader.GetString(type.Name);
    if (type.IsNested) return $"{FullName(reader.GetTypeDefinition(type.GetDeclaringType()))}.{name}";
    var space = reader.GetString(type.Namespace);
    return space.Length == 0 ? name : $"{space}.{name}";
}

// Reachable from outside the assembly. Nested protected counts: a consumer gets at those by
// deriving from the container, so their documentation belongs in the package.
bool IsVisibleType(TypeDefinition type)
{
    var visibility = type.Attributes & TypeAttributes.VisibilityMask;
    if (!type.IsNested) return visibility == TypeAttributes.Public;
    return visibility is TypeAttributes.NestedPublic or TypeAttributes.NestedFamily or TypeAttributes.NestedFamORAssem
        && IsVisibleType(reader.GetTypeDefinition(type.GetDeclaringType()));
}

// Protected and protected-internal are reachable by deriving. Internal and private-protected
// are not: this assembly's only friend is the test project.
bool IsVisible(MethodAttributes attributes)
    => (attributes & MethodAttributes.MemberAccessMask) is MethodAttributes.Public or MethodAttributes.Family or MethodAttributes.FamORAssem;

bool IsVisibleAccessor(MethodDefinitionHandle handle)
    => !handle.IsNil && IsVisible(reader.GetMethodDefinition(handle).Attributes);
