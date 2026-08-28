namespace SkiaSharp.QrCode;

/// <summary>
/// The range of Micro QR versions a generator may choose from: the smallest version within
/// the range that holds the content is used.
/// </summary>
/// <remarks>
/// <para>
/// The Micro QR counterpart of <see cref="QRCodeVersionRange"/>, and it carries one extra
/// consideration. M1-M4 differ in which encoding modes and ECC levels they offer, not only
/// in capacity, so a range can exclude every usable version for two different reasons. A
/// range whose versions cannot carry the mode the content requires is a "does not fit"; a
/// range that offers the requested ECC level nowhere is a contradictory argument and
/// throws, because no content would make it work.
/// </para>
/// </remarks>
public readonly record struct MicroQRVersionRange
{
    /// <summary>The lowest Micro QR version, M1.</summary>
    public const MicroQRVersion MinVersion = MicroQRVersion.M1;

    /// <summary>The highest Micro QR version, M4.</summary>
    public const MicroQRVersion MaxVersion = MicroQRVersion.M4;

    // Normalised bounds with 0 meaning "at the natural limit", so default(T), Any and
    // Between(M1, M4) are one canonical value. MicroQRVersion starts at 1, so 0 is free.
    private readonly byte _min;
    private readonly byte _max;

    private MicroQRVersionRange(MicroQRVersion min, MicroQRVersion max)
    {
        _min = (byte)(min == MinVersion ? 0 : (int)min);
        _max = (byte)(max == MaxVersion ? 0 : (int)max);
    }

    /// <summary>The lowest version the generator may choose (M1 when unbounded below).</summary>
    public MicroQRVersion Min => _min == 0 ? MinVersion : (MicroQRVersion)_min;

    /// <summary>The highest version the generator may choose (M4 when unbounded above).</summary>
    public MicroQRVersion Max => _max == 0 ? MaxVersion : (MicroQRVersion)_max;

    /// <summary>Whether this range imposes no constraint at all (the default).</summary>
    public bool IsAny => _min == 0 && _max == 0;

    /// <summary>Whether this range pins a single version.</summary>
    public bool IsExact => Min == Max;

    /// <summary>Every version, M1 to M4. The default, and identical to <c>default</c>.</summary>
    public static MicroQRVersionRange Any => default;

    /// <summary>Exactly <paramref name="version"/>, with no automatic selection.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="version"/> is not M1-M4.</exception>
    public static MicroQRVersionRange Exactly(MicroQRVersion version)
    {
        ValidateBound(version, nameof(version));
        return new MicroQRVersionRange(version, version);
    }

    /// <summary><paramref name="version"/> or larger.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="version"/> is not M1-M4.</exception>
    public static MicroQRVersionRange AtLeast(MicroQRVersion version)
    {
        ValidateBound(version, nameof(version));
        return new MicroQRVersionRange(version, MaxVersion);
    }

    /// <summary><paramref name="version"/> or smaller.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="version"/> is not M1-M4.</exception>
    public static MicroQRVersionRange AtMost(MicroQRVersion version)
    {
        ValidateBound(version, nameof(version));
        return new MicroQRVersionRange(MinVersion, version);
    }

    /// <summary>An inclusive range from <paramref name="min"/> to <paramref name="max"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when either bound is not M1-M4, or <paramref name="min"/> exceeds <paramref name="max"/>.</exception>
    public static MicroQRVersionRange Between(MicroQRVersion min, MicroQRVersion max)
    {
        ValidateBound(min, nameof(min));
        ValidateBound(max, nameof(max));
        if (min > max)
            throw new ArgumentOutOfRangeException(nameof(min), $"Version range minimum {min} must not exceed maximum {max}.");

        return new MicroQRVersionRange(min, max);
    }

    /// <summary>Whether <paramref name="version"/> falls inside this range.</summary>
    public bool Contains(MicroQRVersion version) => version >= Min && version <= Max;

    /// <inheritdoc/>
    public override string ToString() => IsExact ? Min.ToString() : $"{Min}-{Max}";

    private static void ValidateBound(MicroQRVersion version, string paramName)
    {
        if (version < MinVersion || version > MaxVersion)
            throw new ArgumentOutOfRangeException(paramName, $"Micro QR version must be {MinVersion}-{MaxVersion}, but was {version}");
    }
}
