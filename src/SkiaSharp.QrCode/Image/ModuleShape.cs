namespace SkiaSharp.QrCode.Image;

/// <summary>
/// Defines the shape of QR code modules
/// </summary>
public abstract class ModuleShape
{
    /// <summary>
    /// Gets whether this shape requires antialiasing for smooth rendering.
    /// </summary>
    public abstract bool RequiresAntialiasing { get; }

    /// <summary>
    /// Draw a module at the specified location.
    /// </summary>
    /// <param name="canvas">The canvas to render on.</param>
    /// <param name="rect">The rectangular area for the module.</param>
    /// <param name="paint">The paint to use for drawing.</param>
    public abstract void Draw(SKCanvas canvas, SKRect rect, SKPaint paint);
}

/// <summary>
/// Draw modules as rectangles.
/// </summary>
public sealed class RectangleModuleShape : ModuleShape
{
    /// <summary>
    /// Gets the default instance.
    /// </summary>
    public static readonly RectangleModuleShape Default = new();

    /// <summary>
    /// Antialiasing disabled to prevent gray borders between modules.
    /// </summary>
    public override bool RequiresAntialiasing => false;

    // Enforce singleton pattern
    private RectangleModuleShape() { }

    /// <inheritdoc/>
    public override void Draw(SKCanvas canvas, SKRect rect, SKPaint paint)
    {
        canvas.DrawRect(rect, paint);
    }
}

/// <summary>
/// Draws modules as circles.
/// </summary>
public sealed class CircleModuleShape : ModuleShape
{
    /// <summary>
    /// Gets the default instance.
    /// </summary>
    public static readonly CircleModuleShape Default = new();

    /// <summary>
    /// Requires antialiasing to prevent jagged edges on curves.
    /// </summary>
    public override bool RequiresAntialiasing => true;

    // Enforce singleton pattern
    private CircleModuleShape() { }

    /// <inheritdoc/>
    public override void Draw(SKCanvas canvas, SKRect rect, SKPaint paint)
    {
        var radius = Math.Min(rect.Width, rect.Height) / 2;
        var centerX = rect.MidX;
        var centerY = rect.MidY;
        canvas.DrawCircle(centerX, centerY, radius, paint);
    }
}

/// <summary>
/// Draws modules as rounded rectangles.
/// </summary>
public sealed class RoundedRectangleModuleShape : ModuleShape
{
    /// <summary>
    /// Gets the default instance.
    /// </summary>
    public static readonly RoundedRectangleModuleShape Default = new(0.3f);

    // Gets the corner radius as a percentage of the smaller dimension (width or height).
    private readonly float _cornerRadiusPercent;

    /// <summary>
    /// Requires antialiasing to prevent jagged edges on curves.
    /// </summary>
    public override bool RequiresAntialiasing => true;

    /// <summary>
    /// Initializes a new instance with the specified corner radius.
    /// </summary>
    /// <param name="cornerRadiusPercent">The corner radius as a percentage of the module size (0.0 to 1.0).</param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public RoundedRectangleModuleShape(float cornerRadiusPercent = 0.3f)
    {
        if (cornerRadiusPercent < 0 || cornerRadiusPercent > 1)
            throw new ArgumentOutOfRangeException(nameof(cornerRadiusPercent), "Corner radius percent must be between 0 and 1.");

        _cornerRadiusPercent = cornerRadiusPercent;
    }

    /// <inheritdoc/>
    public override void Draw(SKCanvas canvas, SKRect rect, SKPaint paint)
    {
        var radius = Math.Min(rect.Width, rect.Height) * _cornerRadiusPercent;
        canvas.DrawRoundRect(rect, radius, radius, paint);
    }
}
