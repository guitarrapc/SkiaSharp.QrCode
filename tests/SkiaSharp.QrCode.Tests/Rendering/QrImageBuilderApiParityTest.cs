using System.Reflection;
using SkiaSharp.QrCode.Image;

namespace SkiaSharp.QrCode.Tests;

/// <summary>
/// Guards the intended 1:1 correspondence between the symbology-specific image
/// builders: every public member of <see cref="QRCodeImageBuilder"/> must exist on
/// <see cref="MicroQRCodeImageBuilder"/> with the symbology types swapped
/// (QRCodeData ⇔ MicroQRCodeData, ECCLevel ⇔ MicroQREccLevel, int version ⇔
/// MicroQRVersion), and vice versa — except for the documented Standard QR-only
/// options. Adding an output method or static helper to one builder without the
/// other fails here. Extend the map when the rMQR builder lands.
/// </summary>
public class QrImageBuilderApiParityTest
{
    /// <summary>
    /// Standard QR-only fluent options: Micro QR has a single finder pattern, no
    /// ECC headroom for overlays, and no ECI mode — these members intentionally
    /// have no Micro QR counterpart (decision recorded in specs/microqr-spec-map.md).
    /// </summary>
    private static readonly string[] standardOnlyMembers =
    [
        "WithIcon",
        "WithFinderPatternShape",
        "WithEciMode",
    ];

    [Test]
    public async Task PublicSurface_CorrespondsOneToOne_ModuloDocumentedDifferences()
    {
        var standard = NormalizedSignatures(typeof(QRCodeImageBuilder))
            .Where(s => !standardOnlyMembers.Any(m => s.Contains($" {m}(")))
            .ToHashSet();
        var micro = NormalizedSignatures(typeof(MicroQRCodeImageBuilder)).ToHashSet();

        var missingOnMicro = standard.Except(micro).OrderBy(s => s).ToArray();
        var missingOnStandard = micro.Except(standard).OrderBy(s => s).ToArray();

        if (missingOnMicro.Length > 0 || missingOnStandard.Length > 0)
        {
            Assert.Fail(
                "Image builder surfaces drifted apart.\n" +
                $"Missing on MicroQRCodeImageBuilder:\n  {string.Join("\n  ", missingOnMicro)}\n" +
                $"Missing on QRCodeImageBuilder:\n  {string.Join("\n  ", missingOnStandard)}");
        }

        // Sanity: the normalization must leave a substantial shared surface
        await Assert.That(standard.Count).IsGreaterThan(20);
    }

    [Test]
    public async Task StandardOnlyMembers_ExistOnStandard_AndNotOnMicro()
    {
        foreach (var name in standardOnlyMembers)
        {
            await Assert.That(typeof(QRCodeImageBuilder).GetMethods().Any(m => m.Name == name)).IsTrue();
            await Assert.That(typeof(MicroQRCodeImageBuilder).GetMethods().Any(m => m.Name == name)).IsFalse();
        }
    }

    /// <summary>
    /// Public constructors and methods (declared + inherited, static + instance) as
    /// normalized signature strings with symbology-specific types canonicalized.
    /// </summary>
    private static IEnumerable<string> NormalizedSignatures(Type builder)
    {
        foreach (var ctor in builder.GetConstructors())
        {
            yield return $"ctor({ParameterList(ctor.GetParameters(), ctor.Name)})";
        }

        var methods = builder.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(m => m.DeclaringType != typeof(object));
        foreach (var method in methods)
        {
            yield return $"{Normalize(method.ReturnType)} {method.Name}({ParameterList(method.GetParameters(), method.Name)})";
        }
    }

    private static string ParameterList(ParameterInfo[] parameters, string memberName)
        => string.Join(",", parameters.Select(p => NormalizeParameter(p.ParameterType, memberName)));

    /// <summary>
    /// WithVersion legitimately differs in parameter type (int sentinel vs
    /// MicroQRVersion enum); both canonicalize to VERSION for that member only.
    /// </summary>
    private static string NormalizeParameter(Type type, string memberName)
    {
        if (memberName == "WithVersion" && (type == typeof(int) || type == typeof(MicroQRVersion)))
            return "VERSION";
        return Normalize(type);
    }

    private static string Normalize(Type type)
    {
        if (type == typeof(QRCodeData) || type == typeof(MicroQRCodeData))
            return "SYMBOL_DATA";
        if (type == typeof(ECCLevel) || type == typeof(MicroQREccLevel))
            return "ECC";
        if (type == typeof(QRCodeImageBuilder) || type == typeof(MicroQRCodeImageBuilder))
            return "SELF";
        return type.FullName ?? type.Name;
    }
}
