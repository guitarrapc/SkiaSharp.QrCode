#:sdk Microsoft.NET.Sdk
#:property TargetFramework=net10.0
#:project ../src/SkiaSharp.QrCode/SkiaSharp.QrCode.csproj
#:property EnableAotAnalyzer=false
#:property EnableTrimAnalyzer=false
#:property EnableSingleFileAnalyzer=false

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

// Prints the exported surface of SkiaSharp.QrCode as a sorted, diffable listing.
//
//   dotnet run tools/public_api.cs                 to stdout
//   dotnet run tools/public_api.cs -- -o api.txt   to a file
//
// This is a viewer, not a gate. Nothing in CI runs it and nothing fails when the surface
// changes: breaking changes are already caught by package validation against the released
// baseline, which is the only surface check worth failing a build over.
//
// One target framework is enough. No exported type has a framework-conditional member -
// the conditional compilation in the library all sits on internal types - so net10.0
// represents netstandard2.0, netstandard2.1 and net8.0 as well.

var outputPath = GetOutputPath(args);
var assembly = typeof(SkiaSharp.QrCode.QRCodeData).Assembly;
var nullability = new NullabilityInfoContext();
var sb = new StringBuilder();

var keywords = new Dictionary<Type, string>
{
    [typeof(void)] = "void", [typeof(bool)] = "bool", [typeof(byte)] = "byte", [typeof(sbyte)] = "sbyte",
    [typeof(char)] = "char", [typeof(short)] = "short", [typeof(ushort)] = "ushort", [typeof(int)] = "int",
    [typeof(uint)] = "uint", [typeof(long)] = "long", [typeof(ulong)] = "ulong", [typeof(float)] = "float",
    [typeof(double)] = "double", [typeof(decimal)] = "decimal", [typeof(string)] = "string", [typeof(object)] = "object",
};

var types = assembly.GetExportedTypes()
    .OrderBy(t => t.Namespace, StringComparer.Ordinal)
    .ThenBy(FullName, StringComparer.Ordinal)
    .ToArray();

sb.AppendLine($"// {assembly.GetName().Name} {assembly.GetName().Version}");
sb.AppendLine($"// {types.Length} exported types");

foreach (var byNamespace in types.GroupBy(t => t.Namespace).OrderBy(g => g.Key, StringComparer.Ordinal))
{
    sb.AppendLine();
    sb.AppendLine($"namespace {byNamespace.Key}");
    sb.AppendLine("{");
    foreach (var type in byNamespace)
    {
        WriteType(type);
    }
    sb.AppendLine("}");
}

if (outputPath is null)
{
    Console.Out.Write(sb.ToString());
}
else
{
    File.WriteAllText(outputPath, sb.ToString());
    Console.Error.WriteLine($"{types.Length} exported types written to {outputPath}");
}
return 0;

void WriteType(Type type)
{
    sb.AppendLine();
    if (IsObsolete(type)) sb.AppendLine("    [Obsolete]");
    sb.AppendLine($"    {TypeHeader(type)}");
    sb.AppendLine("    {");

    if (type.IsEnum)
    {
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            sb.AppendLine($"        {field.Name} = {Convert.ToInt64(field.GetRawConstantValue())},");
        }
    }
    else
    {
        foreach (var member in Members(type))
        {
            sb.AppendLine($"        {member}");
        }
    }

    sb.AppendLine("    }");
}

// Members of one type, sorted so the listing is stable across builds. Reflection makes no
// promise about declaration order, so an unsorted dump would churn on every rebuild and
// the diff would stop meaning anything.
IEnumerable<string> Members(Type type)
{
    const BindingFlags Scope = BindingFlags.Public | BindingFlags.NonPublic
        | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    var lines = new List<(int Rank, string Text)>();

    foreach (var field in type.GetFields(Scope))
    {
        if (!Visible(field.IsPublic, field.IsFamily, field.IsFamilyOrAssembly) || Generated(field)) continue;
        var modifier = field.IsLiteral ? "const" : field.IsStatic ? "static" : null;
        var readOnly = field.IsInitOnly ? "readonly" : null;
        lines.Add((0, Line(field, Access(field.IsPublic), modifier, readOnly, TypeName(field.FieldType, FieldNullability(field)), field.Name) + ";"));
    }

    foreach (var ctor in type.GetConstructors(Scope))
    {
        if (!Visible(ctor.IsPublic, ctor.IsFamily, ctor.IsFamilyOrAssembly) || Generated(ctor)) continue;
        lines.Add((1, Line(ctor, Access(ctor.IsPublic), null, null, null, $"{BareName(type)}({Parameters(ctor)})") + ";"));
    }

    foreach (var property in type.GetProperties(Scope))
    {
        if (Generated(property)) continue;
        var getter = property.GetMethod;
        var setter = property.SetMethod;
        var readable = getter is not null && Visible(getter.IsPublic, getter.IsFamily, getter.IsFamilyOrAssembly);
        var writable = setter is not null && Visible(setter.IsPublic, setter.IsFamily, setter.IsFamilyOrAssembly);
        if (!readable && !writable) continue;

        var accessors = new StringBuilder("{ ");
        if (readable) accessors.Append("get; ");
        if (writable) accessors.Append(IsInitOnly(setter!) ? "init; " : "set; ");
        accessors.Append('}');

        var anchor = readable ? getter! : setter!;
        var name = property.GetIndexParameters().Length > 0 ? $"this[{Parameters(anchor)}]" : property.Name;
        lines.Add((2, Line(property, Access(anchor.IsPublic), anchor.IsStatic ? "static" : null, null,
            TypeName(property.PropertyType, PropertyNullability(property)), $"{name} {accessors}")));
    }

    foreach (var evt in type.GetEvents(Scope))
    {
        if (evt.AddMethod is not { } add || !Visible(add.IsPublic, add.IsFamily, add.IsFamilyOrAssembly)) continue;
        lines.Add((3, Line(evt, Access(add.IsPublic), add.IsStatic ? "static" : null, null,
            TypeName(evt.EventHandlerType!, null), $"{evt.Name} {{ add; remove; }}")));
    }

    foreach (var method in type.GetMethods(Scope))
    {
        if (!Visible(method.IsPublic, method.IsFamily, method.IsFamilyOrAssembly) || Generated(method) || IsAccessor(method)) continue;
        var modifier = method.IsStatic ? "static"
            : method.IsAbstract ? "abstract"
            : method.GetBaseDefinition() != method ? "override"
            : method.IsVirtual && !method.IsFinal ? "virtual"
            : null;
        var name = $"{method.Name}{GenericSuffix(method.GetGenericArguments())}({Parameters(method)})";
        lines.Add((4, Line(method, Access(method.IsPublic), modifier, null,
            TypeName(method.ReturnType, ParameterNullability(method.ReturnParameter)), name) + ";"));
    }

    return lines.OrderBy(l => l.Rank).ThenBy(l => l.Text, StringComparer.Ordinal).Select(l => l.Text);
}

string Line(MemberInfo member, string access, string? modifier, string? readOnly, string? returnType, string name)
{
    var parts = new[] { access, modifier, readOnly, returnType, name }.Where(p => !string.IsNullOrEmpty(p));
    return (IsObsolete(member) ? "[Obsolete] " : "") + string.Join(" ", parts);
}

string TypeHeader(Type type)
{
    var kind = type.IsEnum ? "enum"
        : type.IsInterface ? "interface"
        : typeof(Delegate).IsAssignableFrom(type) ? "delegate"
        : type.IsValueType ? "struct"
        : "class";

    var modifiers = new List<string> { type.IsPublic || type.IsNestedPublic ? "public" : "protected" };
    if (type is { IsAbstract: true, IsSealed: true, IsEnum: false, IsInterface: false }) modifiers.Add("static");
    else if (type.IsAbstract && !type.IsInterface) modifiers.Add("abstract");
    else if (type.IsSealed && !type.IsEnum && !type.IsValueType) modifiers.Add("sealed");
    if (type.IsValueType && !type.IsEnum && TypeHasAttribute(type, "IsReadOnlyAttribute")) modifiers.Add("readonly");
    modifiers.Add(kind);

    var header = $"{string.Join(" ", modifiers)} {BareName(type)}{GenericSuffix(type.GetGenericArguments())}";

    var bases = new List<string>();
    if (type is { IsClass: true, BaseType: not null } && type.BaseType != typeof(object) && !typeof(Delegate).IsAssignableFrom(type))
    {
        bases.Add(TypeName(type.BaseType, null));
    }

    // Interfaces the base type already carries are noise. Only what this type adds is surface.
    var inherited = type.BaseType?.GetInterfaces() ?? [];
    bases.AddRange(type.GetInterfaces().Except(inherited).Select(i => TypeName(i, null)).OrderBy(n => n, StringComparer.Ordinal));

    if (type.IsEnum) bases.Add(TypeName(Enum.GetUnderlyingType(type), null));

    return bases.Count > 0 ? $"{header} : {string.Join(", ", bases)}" : header;
}

string Parameters(MethodBase method) => string.Join(", ", method.GetParameters().Select(p =>
{
    var modifier = p.ParameterType.IsByRef
        ? p.IsOut ? "out " : p.IsIn ? "in " : "ref "
        : ParameterHasAttribute(p, "ParamArrayAttribute") ? "params " : "";
    var value = p.HasDefaultValue ? $" = {Literal(p.RawDefaultValue)}" : "";
    return $"{modifier}{TypeName(p.ParameterType, ParameterNullability(p))} {p.Name}{value}";
}));

// Friendly C# spelling. Reflection renders a generic as Span'1[Byte], which is unreadable
// in a listing meant to be skimmed and compared by eye.
string TypeName(Type type, NullabilityInfo? info)
{
    var suffix = info is { ReadState: NullabilityState.Nullable } && Nullable.GetUnderlyingType(Bare(type)) is null ? "?" : "";
    return Core(type) + suffix;

    static Type Bare(Type t) => t.IsByRef ? t.GetElementType()! : t;

    string Core(Type t)
    {
        if (t.IsByRef) return Core(t.GetElementType()!);
        if (t.IsArray) return $"{Core(t.GetElementType()!)}[{new string(',', t.GetArrayRank() - 1)}]";
        if (t.IsPointer) return $"{Core(t.GetElementType()!)}*";

        if (Nullable.GetUnderlyingType(t) is { } underlying) return $"{Core(underlying)}?";
        if (keywords.TryGetValue(t, out var keyword)) return keyword;
        if (t.IsGenericType) return $"{BareName(t)}<{string.Join(", ", t.GetGenericArguments().Select(Core))}>";
        return BareName(t);
    }
}

NullabilityInfo? ParameterNullability(ParameterInfo p) { try { return nullability.Create(p); } catch { return null; } }
NullabilityInfo? PropertyNullability(PropertyInfo p) { try { return nullability.Create(p); } catch { return null; } }
NullabilityInfo? FieldNullability(FieldInfo f) { try { return nullability.Create(f); } catch { return null; } }

string GenericSuffix(Type[] arguments) => arguments.Length == 0 ? "" : $"<{string.Join(", ", arguments.Select(a => a.Name))}>";

// Nested types read as Outer.Inner rather than reflection's Outer+Inner.
string BareName(Type type)
{
    var name = type.Name;
    var tick = name.IndexOf('`');
    if (tick >= 0) name = name[..tick];
    return type.IsNested ? $"{BareName(type.DeclaringType!)}.{name}" : name;
}

string FullName(Type type) => $"{type.Namespace}.{BareName(type)}";

// private protected is not surface. public and protected are.
bool Visible(bool isPublic, bool isFamily, bool isFamilyOrAssembly) => isPublic || isFamily || isFamilyOrAssembly;

string Access(bool isPublic) => isPublic ? "public" : "protected";

bool Generated(MemberInfo member) =>
    member.Name.Contains('<') || member.GetCustomAttributes(typeof(CompilerGeneratedAttribute), false).Length > 0;

bool IsAccessor(MethodInfo method) => method.IsSpecialName && !method.Name.StartsWith("op_", StringComparison.Ordinal);

bool IsInitOnly(MethodInfo setter) =>
    setter.ReturnParameter.GetRequiredCustomModifiers().Any(m => m.Name == "IsExternalInit");

bool IsObsolete(MemberInfo member) => member.GetCustomAttributesData().Any(a => a.AttributeType == typeof(ObsoleteAttribute));

bool TypeHasAttribute(MemberInfo member, string name) => member.GetCustomAttributesData().Any(a => a.AttributeType.Name == name);

bool ParameterHasAttribute(ParameterInfo parameter, string name) => parameter.GetCustomAttributesData().Any(a => a.AttributeType.Name == name);

static string Literal(object? value) => value switch
{
    null => "null",
    string s => $"\"{s}\"",
    bool b => b ? "true" : "false",
    _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "?",
};

static string? GetOutputPath(string[] args)
{
    var index = Array.IndexOf(args, "-o");
    if (index < 0) index = Array.IndexOf(args, "--output");
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}
