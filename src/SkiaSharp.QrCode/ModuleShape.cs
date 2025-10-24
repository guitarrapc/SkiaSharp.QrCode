namespace SkiaSharp.QrCode;

/// <summary>
/// Defines the shape of QR code modules
/// </summary>
public abstract class ModuleShape
{
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

public sealed class RoundedRectangleModuleShape : ModuleShape
{
    /// <summary>
    /// Gets the default instance.
    /// </summary>
    public static readonly RoundedRectangleModuleShape Default = new(0.3f);

    // Gets the corner radius as a percentage of the smaller dimension (width or height).
    private readonly float _cornerRadiusPercent;

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

public sealed class DiamondModuleShape : ModuleShape
{
    /// <summary>
    /// Gets the default instance.
    /// </summary>
    public static readonly DiamondModuleShape Default = new();

    // Enforce singleton pattern
    private DiamondModuleShape() { }

    /// <inheritdoc/>
    public override void Draw(SKCanvas canvas, SKRect rect, SKPaint paint)
    {
        var path = new SKPath();
        path.MoveTo(rect.MidX, rect.Top);
        path.LineTo(rect.Right, rect.MidY);
        path.LineTo(rect.MidX, rect.Bottom);
        path.LineTo(rect.Left, rect.MidY);
        path.Close();
        canvas.DrawPath(path, paint);
    }
}
