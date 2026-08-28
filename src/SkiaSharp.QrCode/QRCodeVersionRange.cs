namespace SkiaSharp.QrCode;

/// <summary>
/// The range of Standard QR versions a generator may choose from: the smallest version
/// within the range that holds the content is used.
/// </summary>
/// <remarks>
/// <para>
/// A single fixed version is the degenerate case, <see cref="Exactly"/>, rather than a
/// separate setting. Carrying both a "requested version" and a range would admit
/// contradictions that could only be resolved by throwing, and buy nothing.
/// </para>
/// <code>
/// // the printed area is fixed, so the symbol must not be smaller than version 10
/// var options = new QRCodeGeneratorOptions { Version = QRCodeVersionRange.AtLeast(10) };
/// </code>
/// <para>
/// This applies to Standard QR and, as <see cref="MicroQRVersionRange"/>, to Micro QR,
/// because both are totally ordered by capacity. rMQR is not: R7x43, R9x43 and R7x59 have
/// no min/max relation, so it constrains its fit with
/// <see cref="RmQRFitStrategy"/> and <see cref="RmQRHeight"/> instead.
/// </para>
/// </remarks>
public readonly record struct QRCodeVersionRange
{
    /// <summary>The lowest version defined by ISO/IEC 18004.</summary>
    public const int MinVersion = 1;

    /// <summary>The highest version defined by ISO/IEC 18004.</summary>
    public const int MaxVersion = 40;

    // Bounds are stored normalised, with 0 meaning "at the natural limit", so that the
    // canonical form is unique: default(T), Any and Between(1, 40) are the same range and
    // must not compare unequal. Version 0 does not exist, so 0 is free as a sentinel.
    private readonly byte _min;
    private readonly byte _max;

    /// <summary>An inclusive range from <paramref name="min"/> to <paramref name="max"/>.</summary>
    /// <param name="min">The lowest permitted version (1-40).</param>
    /// <param name="max">The highest permitted version (1-40), inclusive.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when either bound is outside 1-40, or <paramref name="min"/> exceeds <paramref name="max"/>.</exception>
    /// <remarks>
    /// Both bounds are inclusive, unlike C#'s <see cref="System.Range"/>, whose end is
    /// exclusive. That difference is why this is its own type rather than a <c>1..40</c>
    /// expression: the language syntax would read as versions 1 to 40 and mean 1 to 39.
    /// </remarks>
    public QRCodeVersionRange(int min, int max)
    {
        ValidateBound(min, nameof(min));
        ValidateBound(max, nameof(max));
        if (min > max)
            throw new ArgumentOutOfRangeException(nameof(min), $"Version range minimum {min} must not exceed maximum {max}.");

        _min = (byte)(min == MinVersion ? 0 : min);
        _max = (byte)(max == MaxVersion ? 0 : max);
    }

    /// <summary>The lowest version the generator may choose (1 when unbounded below).</summary>
    public int Min => _min == 0 ? MinVersion : _min;

    /// <summary>The highest version the generator may choose (40 when unbounded above).</summary>
    public int Max => _max == 0 ? MaxVersion : _max;

    /// <summary>Whether this range imposes no constraint at all (the default).</summary>
    public bool IsAny => _min == 0 && _max == 0;

    /// <summary>Whether this range pins a single version.</summary>
    public bool IsExact => Min == Max;

    /// <summary>Every version, 1 to 40. The default, and identical to <c>default</c>.</summary>
    public static QRCodeVersionRange Any => default;

    /// <summary>Exactly <paramref name="version"/>, with no automatic selection.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="version"/> is outside 1-40.</exception>
    public static QRCodeVersionRange Exactly(int version) => new(version, version);

    /// <summary><paramref name="version"/> or larger.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="version"/> is outside 1-40.</exception>
    public static QRCodeVersionRange AtLeast(int version) => new(version, MaxVersion);

    /// <summary><paramref name="version"/> or smaller.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="version"/> is outside 1-40.</exception>
    public static QRCodeVersionRange AtMost(int version) => new(MinVersion, version);

    /// <inheritdoc cref="QRCodeVersionRange(int, int)"/>
    /// <summary>An inclusive range from <paramref name="min"/> to <paramref name="max"/>.</summary>
    public static QRCodeVersionRange Between(int min, int max) => new(min, max);

    /// <summary>A single version, as <see cref="Exactly"/>.</summary>
    /// <param name="version">The version to pin (1-40).</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="version"/> is outside 1-40.</exception>
    /// <remarks>
    /// Lets an option set read <c>Version = 15</c>, which is the common case. -1 is
    /// <em>not</em> accepted as a second spelling of <see cref="Any"/>: the whole point of
    /// this type is that automatic selection has a name rather than a sentinel, and a -1
    /// arriving through a variable would otherwise silently produce an automatically sized
    /// symbol where a pinned one was asked for. The <c>WithVersion(int)</c> builder method
    /// still accepts -1, because that is a released signature.
    /// </remarks>
    public static implicit operator QRCodeVersionRange(int version) => Exactly(version);

    /// <summary>A single version, or <see cref="Any"/> when there is none.</summary>
    /// <param name="version">The version to pin (1-40), or <c>null</c> for automatic selection.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="version"/> has a value outside 1-40.</exception>
    /// <remarks>
    /// This is what the old <c>requestedVersion: -1</c> convention was really for: letting a
    /// caller whose version is optional pass it straight through instead of branching on
    /// whether it was configured. <c>null</c> does that job without a magic number, so a
    /// mistyped or defaulted value still fails loudly while a genuinely absent one means
    /// automatic.
    /// </remarks>
    public static implicit operator QRCodeVersionRange(int? version) => version.HasValue ? Exactly(version.GetValueOrDefault()) : Any;

    /// <summary>Whether <paramref name="version"/> falls inside this range.</summary>
    public bool Contains(int version) => version >= Min && version <= Max;

    /// <inheritdoc/>
    public override string ToString() => IsExact ? $"v{Min}" : $"v{Min}-v{Max}";

    private static void ValidateBound(int version, string paramName)
    {
        if (version < MinVersion || version > MaxVersion)
            throw new ArgumentOutOfRangeException(paramName, $"Version must be {MinVersion}-{MaxVersion}, but was {version}");
    }
}
