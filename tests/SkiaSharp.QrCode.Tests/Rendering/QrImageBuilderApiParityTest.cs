using System.Reflection;
using SkiaSharp.QrCode.Image;

namespace SkiaSharp.QrCode.Tests;

/// <summary>
/// Guards the intended 1:1 correspondence between the symbology-specific image
/// builders: every public member of <see cref="QRCodeImageBuilder"/> must exist on
/// <see cref="MicroQRCodeImageBuilder"/> and <see cref="RmQRCodeImageBuilder"/> with
/// the symbology types swapped (QRCodeData ⇔ MicroQRCodeData ⇔ RmQRCodeData,
/// ECCLevel ⇔ MicroQREccLevel ⇔ RmQREccLevel, int version ⇔ MicroQRVersion ⇔
/// RmQRVersion), and vice versa, except for the documented per-symbology options.
/// Adding an output method or static helper to one builder without the others
/// fails here.
/// </summary>
public class QrImageBuilderApiParityTest
{
    /// <summary>
    /// Standard QR-only fluent options: Micro QR and rMQR have a single finder
    /// pattern and no ECC headroom for overlays.
    /// </summary>
    private static readonly string[] standardOnlyMembers =
    [
        "WithIcon",
        "WithFinderPatternShape",
    ];

    /// <summary>
    /// rMQR-only fluent options: version fit is two-dimensional (design record
    /// specs/rmqr-encoder.md), so the rMQR builder alone exposes the fit strategy and
    /// the fixed-height constraint; and the symbol is rectangular, so the width-only
    /// sizing rule (height from the aspect ratio) is a builder option of its own.
    /// Mixed-mode segmentation is rMQR-only for the same reason: rMQR capacities are
    /// small enough that splitting the content moves the symbol a version or more.
    /// </summary>
    private static readonly string[] rmqrOnlyMembers =
    [
        "WithFitStrategy",
        "WithHeight",
        "WithWidth",
        "WithSegmentation",
    ];

    [Test]
    public async Task PublicSurface_CorrespondsOneToOne_ModuloDocumentedDifferences()
    {
        var standard = NormalizedSignatures(typeof(QRCodeImageBuilder))
            .Where(s => !standardOnlyMembers.Any(m => s.Contains($" {m}(")))
            .ToHashSet();
        var micro = NormalizedSignatures(typeof(MicroQRCodeImageBuilder)).ToHashSet();
        var rmqr = NormalizedSignatures(typeof(RmQRCodeImageBuilder))
            .Where(s => !rmqrOnlyMembers.Any(m => s.Contains($" {m}(")))
            .ToHashSet();

        var failures = new List<string>();
        Compare("MicroQRCodeImageBuilder", micro, standard.Where(s => !s.Contains(" WithEciMode(")));
        Compare("RmQRCodeImageBuilder", rmqr);
        if (failures.Count > 0)
            Assert.Fail("Image builder surfaces drifted apart.\n" + string.Join("\n", failures));

        // Sanity: the normalization must leave a substantial shared surface
        await Assert.That(standard.Count).IsGreaterThan(20);

        void Compare(string name, HashSet<string> other, IEnumerable<string>? expected = null)
        {
            var expectedSet = (expected ?? standard).ToHashSet();
            var missingOnOther = expectedSet.Except(other).OrderBy(s => s).ToArray();
            var missingOnStandard = other.Except(expectedSet).OrderBy(s => s).ToArray();
            if (missingOnOther.Length > 0 || missingOnStandard.Length > 0)
            {
                failures.Add(
                    $"Missing on {name}:\n  {string.Join("\n  ", missingOnOther)}\n" +
                    $"Missing on QRCodeImageBuilder (present on {name}):\n  {string.Join("\n  ", missingOnStandard)}");
            }
        }
    }

    [Test]
    public async Task EciMode_ExistsOnSymbologiesThatDefineEci()
    {
        await Assert.That(typeof(QRCodeImageBuilder).GetMethods().Any(m => m.Name == "WithEciMode")).IsTrue();
        await Assert.That(typeof(RmQRCodeImageBuilder).GetMethods().Any(m => m.Name == "WithEciMode")).IsTrue();
        await Assert.That(typeof(MicroQRCodeImageBuilder).GetMethods().Any(m => m.Name == "WithEciMode")).IsFalse();
    }

    [Test]
    public async Task StandardOnlyMembers_ExistOnStandard_AndNotOnOthers()
    {
        foreach (var name in standardOnlyMembers)
        {
            await Assert.That(typeof(QRCodeImageBuilder).GetMethods().Any(m => m.Name == name)).IsTrue();
            await Assert.That(typeof(MicroQRCodeImageBuilder).GetMethods().Any(m => m.Name == name)).IsFalse();
            await Assert.That(typeof(RmQRCodeImageBuilder).GetMethods().Any(m => m.Name == name)).IsFalse();
        }
    }

    [Test]
    public async Task RmqrOnlyMembers_ExistOnRmqr_AndNotOnOthers()
    {
        foreach (var name in rmqrOnlyMembers)
        {
            await Assert.That(typeof(RmQRCodeImageBuilder).GetMethods().Any(m => m.Name == name)).IsTrue();
            await Assert.That(typeof(QRCodeImageBuilder).GetMethods().Any(m => m.Name == name)).IsFalse();
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
    /// WithVersion legitimately differs in parameter type (int sentinel vs the
    /// symbology version enums); all canonicalize to VERSION for that member only.
    /// </summary>
    private static string NormalizeParameter(Type type, string memberName)
    {
        if (memberName == "WithVersion" && (type == typeof(int) || type == typeof(MicroQRVersion) || type == typeof(RmQRVersion)))
            return "VERSION";
        return Normalize(type);
    }

    private static string Normalize(Type type)
    {
        if (type == typeof(QRCodeData) || type == typeof(MicroQRCodeData) || type == typeof(RmQRCodeData))
            return "SYMBOL_DATA";
        if (type == typeof(ECCLevel) || type == typeof(MicroQREccLevel) || type == typeof(RmQREccLevel))
            return "ECC";
        if (type == typeof(QRCodeImageBuilder) || type == typeof(MicroQRCodeImageBuilder) || type == typeof(RmQRCodeImageBuilder))
            return "SELF";
        return type.FullName ?? type.Name;
    }
}
