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

    private QRCodeVersionRange(int min, int max)
    {
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
    public static QRCodeVersionRange Exactly(int version)
    {
        ValidateBound(version, nameof(version));
        return new QRCodeVersionRange(version, version);
    }

    /// <summary><paramref name="version"/> or larger.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="version"/> is outside 1-40.</exception>
    public static QRCodeVersionRange AtLeast(int version)
    {
        ValidateBound(version, nameof(version));
        return new QRCodeVersionRange(version, MaxVersion);
    }

    /// <summary><paramref name="version"/> or smaller.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="version"/> is outside 1-40.</exception>
    public static QRCodeVersionRange AtMost(int version)
    {
        ValidateBound(version, nameof(version));
        return new QRCodeVersionRange(MinVersion, version);
    }

    /// <summary>An inclusive range from <paramref name="min"/> to <paramref name="max"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when either bound is outside 1-40, or <paramref name="min"/> exceeds <paramref name="max"/>.</exception>
    public static QRCodeVersionRange Between(int min, int max)
    {
        ValidateBound(min, nameof(min));
        ValidateBound(max, nameof(max));
        if (min > max)
            throw new ArgumentOutOfRangeException(nameof(min), $"Version range minimum {min} must not exceed maximum {max}.");

        return new QRCodeVersionRange(min, max);
    }

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
