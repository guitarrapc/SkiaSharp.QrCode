using System.Reflection;

namespace SkiaSharp.QrCode.Tests;

/// <summary>
/// The shape of the sizing surface, as decided in plans/generator-api-options-plan.md
/// Phase 6: asking "does this fit, and how big is it" is a <c>Try</c> operation, because
/// "it does not fit" is an ordinary data-dependent answer rather than a defect.
/// </summary>
/// <remarks>
/// This is a shape test, not a behaviour test. It exists because the deleted throwing
/// overloads are exactly the kind of convenience someone re-adds in good faith, and
/// because the obsolete markers are the only record a caller sees of the 2.0.0 removal.
/// </remarks>
public class SizingSurfaceTest
{
    private static MethodInfo[] PublicStatic(Type generator, string name)
        => [.. generator.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name == name)];

    /// <summary>
    /// rMQR never shipped a throwing sizing method, so it does not carry one at all.
    /// Standard QR and Micro QR keep theirs only because 1.1.1 released them.
    /// </summary>
    [Test]
    public async Task RmQR_HasNoThrowingSizingMethod()
    {
        await Assert.That(PublicStatic(typeof(RmQRCodeGenerator), "GetRequiredBufferSize").Length).IsEqualTo(0);
    }

    /// <summary>
    /// One throwing overload survives per released symbology — the 1.1.1 parameter list —
    /// and it is obsolete. A second one would mean the options-surface overload came back.
    /// </summary>
    [Test]
    [Arguments(typeof(QRCodeGenerator))]
    [Arguments(typeof(MicroQRCodeGenerator))]
    public async Task ReleasedThrowingSizing_IsTheOnlyOne_AndIsObsolete(Type generator)
    {
        var gets = PublicStatic(generator, "GetRequiredBufferSize");
        await Assert.That(gets.Length).IsEqualTo(1);

        var obsolete = gets[0].GetCustomAttribute<ObsoleteAttribute>();
        await Assert.That(obsolete).IsNotNull();

        // A warning, not an error: this is a deprecation cycle, and callers must be able
        // to keep building while they migrate.
        await Assert.That(obsolete!.IsError).IsFalse();
        await Assert.That(obsolete.Message!).Contains("TryGetRequiredBufferSize");
        await Assert.That(obsolete.Message!).Contains("2.0.0");
    }

    /// <summary>
    /// Exactly one non-throwing sizing overload per symbology, and it takes the options
    /// struct. The parameter list <c>Try</c> overloads added in 1.2.0 were deleted before
    /// release rather than shipped and frozen.
    /// </summary>
    [Test]
    [Arguments(typeof(QRCodeGenerator), typeof(QRCodeGeneratorOptions))]
    [Arguments(typeof(MicroQRCodeGenerator), typeof(MicroQRCodeGeneratorOptions))]
    [Arguments(typeof(RmQRCodeGenerator), typeof(RmQRCodeGeneratorOptions))]
    public async Task NonThrowingSizing_IsExactlyOneOverload_TakingOptions(Type generator, Type optionsType)
    {
        var tries = PublicStatic(generator, "TryGetRequiredBufferSize");
        await Assert.That(tries.Length).IsEqualTo(1);

        var last = tries[0].GetParameters()[^1];
        await Assert.That(last.ParameterType).IsEqualTo(optionsType.MakeByRefType());

        // The replacement must not itself be deprecated.
        await Assert.That(tries[0].GetCustomAttribute<ObsoleteAttribute>()).IsNull();
    }

    /// <summary>
    /// The deprecated overloads keep the exceptions they shipped with in v1.1.1, which is
    /// what docs/migration.md promises anyone who upgrades without migrating yet.
    /// </summary>
    /// <remarks>
    /// The exact types were measured, not assumed, and the XML docs were wrong about them
    /// until a review caught it: Standard QR reports a content overflow as
    /// <see cref="InvalidOperationException"/> from its internal version scan, while Micro
    /// QR reports the same condition as <see cref="ArgumentException"/>. The two disagree,
    /// and that inconsistency is frozen for as long as the members exist.
    /// </remarks>
    [Test]
#pragma warning disable CS0618 // the released sizing overloads are the subject here
    public async Task ObsoleteSizing_KeepsItsReleasedExceptions()
    {
        var tooLong = new string('0', 8000);   // beyond Version 40 numeric capacity

        await Assert.That(() => QRCodeGenerator.GetRequiredBufferSize(tooLong.AsSpan(), ECCLevel.L)).Throws<InvalidOperationException>();
        await Assert.That(() => QRCodeGenerator.GetRequiredBufferSize("1".AsSpan(), ECCLevel.L, quietZoneSize: -1)).Throws<ArgumentOutOfRangeException>();

        await Assert.That(() => MicroQRCodeGenerator.GetRequiredBufferSize(tooLong.AsSpan(), MicroQREccLevel.L)).Throws<ArgumentException>();
        await Assert.That(() => MicroQRCodeGenerator.GetRequiredBufferSize("1".AsSpan(), MicroQREccLevel.L, quietZoneSize: -1)).Throws<ArgumentOutOfRangeException>();
    }
#pragma warning restore CS0618

    /// <summary>
    /// The replacement reports the same overflow as <c>false</c> on every symbology, which
    /// is the entire point of the change.
    /// </summary>
    [Test]
    public async Task NonThrowingSizing_ReportsOverflowAsFalse_OnEverySymbology()
    {
        var tooLong = new string('0', 8000);

        await Assert.That(QRCodeGenerator.TryGetRequiredBufferSize(tooLong.AsSpan(), ECCLevel.L, out var standard)).IsFalse();
        await Assert.That(standard).IsEqualTo(default(QRCodeCalculatedSize));

        await Assert.That(MicroQRCodeGenerator.TryGetRequiredBufferSize(tooLong.AsSpan(), MicroQREccLevel.L, out var micro)).IsFalse();
        await Assert.That(micro).IsEqualTo(default(MicroQRCodeCalculatedSize));

        await Assert.That(RmQRCodeGenerator.TryGetRequiredBufferSize(tooLong.AsSpan(), RmQREccLevel.M, out var rmqr)).IsFalse();
        await Assert.That(rmqr).IsEqualTo(default(RmQRCodeCalculatedSize));
    }
}
