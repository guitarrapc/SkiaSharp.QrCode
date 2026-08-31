using System.Reflection;

namespace SkiaSharp.QrCode.Tests;

/// <summary>
/// <see cref="Compression"/> names a serialization feature that no longer exists:
/// docs/migration.md has told callers since 1.0.0 to compress the bytes from
/// <c>GetRawData()</c> themselves. The enum outlived the feature it described, and
/// survives 1.2.0 only because 1.1.1 exported it, so it is deprecated until 2.0.0.
/// </summary>
/// <remarks>
/// A shape test, not a behaviour test. It pins the two facts that make the deprecation
/// honest: the marker is the only notice a 1.1.1 caller gets before the removal, and the
/// enum is safe to remove precisely because nothing in the assembly consumes it. The
/// second half is what would have caught this type years ago.
/// </remarks>
public class VestigialSurfaceTest
{
#pragma warning disable CS0618 // Compression is the subject of these tests, not a caller of it

    /// <summary>
    /// Deprecated rather than deleted: 1.2.0 is a minor release, and package validation
    /// against the 1.1.1 baseline holds us to that.
    /// </summary>
    [Test]
    public async Task Compression_IsObsolete_AndScheduledForRemoval()
    {
        var obsolete = typeof(Compression).GetCustomAttribute<ObsoleteAttribute>();
        await Assert.That(obsolete).IsNotNull();

        // A warning, not an error: a caller who named the type must still be able to
        // build while they migrate.
        await Assert.That(obsolete!.IsError).IsFalse();
        await Assert.That(obsolete.Message!).Contains("2.0.0");
    }

    /// <summary>
    /// The justification for removing it. If anything ever consumes this enum again the
    /// deprecation is wrong, and this test says so before the 2.0.0 removal does.
    /// </summary>
    [Test]
    public async Task Compression_IsReferencedByNothing_InTheAssembly()
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        var users = new List<string>();
        foreach (var type in typeof(Compression).Assembly.GetTypes())
        {
            // The enum declares its own members as Compression; everything else is a user.
            if (type == typeof(Compression)) continue;

            foreach (var method in type.GetMethods(all))
            {
                if (Mentions(method.ReturnType) || method.GetParameters().Any(p => Mentions(p.ParameterType)))
                    users.Add($"{type.FullName}.{method.Name}");
            }
            foreach (var ctor in type.GetConstructors(all))
            {
                if (ctor.GetParameters().Any(p => Mentions(p.ParameterType)))
                    users.Add($"{type.FullName}..ctor");
            }
            foreach (var property in type.GetProperties(all))
            {
                if (Mentions(property.PropertyType)) users.Add($"{type.FullName}.{property.Name}");
            }
            foreach (var field in type.GetFields(all))
            {
                if (Mentions(field.FieldType)) users.Add($"{type.FullName}.{field.Name}");
            }
        }

        await Assert.That(users).IsEmpty()
            .Because($"Compression is deprecated as unused, but is referenced by: {string.Join(", ", users)}");
    }

    private static bool Mentions(Type type)
    {
        var bare = type.HasElementType ? type.GetElementType()! : type;
        if (bare == typeof(Compression)) return true;
        return bare.IsGenericType && bare.GetGenericArguments().Any(Mentions);
    }

    /// <summary>
    /// An exported type with nothing a caller can touch is not an API, it is an accident:
    /// a helper class whose members all went internal, or a type left behind by a feature
    /// that was removed. <c>EciModeExtensions</c> was exactly that, and shipped that way
    /// through 1.1.1 because nothing looked for it.
    /// </summary>
    /// <remarks>
    /// Enums always carry their members and delegates their signature, so no exemption
    /// list is needed. If a genuinely empty marker type is ever wanted, this test is the
    /// place to say so out loud.
    /// </remarks>
    [Test]
    public async Task NoExportedType_IsEmpty()
    {
        const BindingFlags visible = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        static bool Callable(MethodBase? m) => m is not null && (m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly);

        var empty = new List<string>();
        foreach (var type in typeof(Compression).Assembly.GetExportedTypes())
        {
            var members =
                type.GetFields(visible).Count(f => f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly)
                + type.GetConstructors(visible).Count(Callable)
                + type.GetMethods(visible).Count(m => Callable(m) && !m.Name.Contains('<'))
                + type.GetProperties(visible).Count(p => Callable(p.GetMethod) || Callable(p.SetMethod))
                + type.GetEvents(visible).Count(e => Callable(e.AddMethod))
                + type.GetNestedTypes(BindingFlags.Public).Length;

            if (members == 0) empty.Add(type.FullName!);
        }

        await Assert.That(empty).IsEmpty()
            .Because($"exported but unusable from outside the assembly: {string.Join(", ", empty)}");
    }

#pragma warning restore CS0618
}
