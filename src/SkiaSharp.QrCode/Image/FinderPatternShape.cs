namespace SkiaSharp.QrCode.Image;

/// <summary>
/// Defines the shape of QR code finder pattern (position detection patterns).
/// </summary>
public abstract class FinderPatternShape
{
    /// <summary>
    /// Draw a finder pattern at the specified location.
    /// </summary>
    /// <param name="canvas">The canvas to render on.</param>
    /// <param name="rect">The rectangular area for the finder pattern (7x7 modules).</param>
    /// <param name="paint">The paint to use for drawing.</param>
    public abstract void Draw(SKCanvas canvas, SKRect rect, SKPaint paint);
}

/// <summary>
/// Standard QR code finder pattern (three nested squares, 7x7, 5x5, 3x3).
/// </summary>
public sealed class RectangleFinderPatternShape : FinderPatternShape
{
    /// <summary>
    /// Gets the default instance.
    /// </summary>
    public static readonly RectangleFinderPatternShape Default = new();

    // Enforce singleton pattern
    private RectangleFinderPatternShape() { }

    /// <inheritdoc/>
    public override void Draw(SKCanvas canvas, SKRect rect, SKPaint paint)
    {
        var moduleSize = rect.Width / 7f;

        // Draw outer ring (7×7)
        canvas.DrawRect(rect, paint);

        // Draw white ring (5×5)
        using var whitePaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };
        var innerRect = SKRect.Create(
            rect.Left + moduleSize,
            rect.Top + moduleSize,
            moduleSize * 5,
            moduleSize * 5);
        canvas.DrawRect(innerRect, whitePaint);

        // Draw black center (3×3)
        var centerRect = SKRect.Create(
            rect.Left + moduleSize * 2,
            rect.Top + moduleSize * 2,
            moduleSize * 3,
            moduleSize * 3);
        canvas.DrawRect(centerRect, paint);
    }
}

/// <summary>
/// Circular finder pattern. (three nested circles, 7x7, 5x5, 3x3)
/// </summary>
public sealed class CircleFinderPatternShape : FinderPatternShape
{
    /// <summary>
    /// Gets the default instance.
    /// </summary>
    public static readonly CircleFinderPatternShape Default = new();

    // Enforce singleton pattern
    private CircleFinderPatternShape() { }

    /// <inheritdoc/>
    public override void Draw(SKCanvas canvas, SKRect rect, SKPaint paint)
    {
        var center = new SKPoint(rect.MidX, rect.MidY);
        var radius = Math.Min(rect.Width, rect.Height) / 2f;

        // Draw outer ring (7x7)
        canvas.DrawCircle(center, radius, paint);
        
        // Draw white ring (5x5)
        using var whitePaint = new SKPaint() { Color = SKColors.White, Style = SKPaintStyle.Fill };
        canvas.DrawCircle(center, radius * (5f / 7f), whitePaint);

        // Draw black center (3x3)
        canvas.DrawCircle(center, radius * (3f / 7f), paint);
    }
}

/// <summary>
/// Rounded rectangle outer with circular center finder pattern.
/// Three nested shapes: outer rounded rectangle (7×7), middle rounded rectangle (5×5), inner circle (3×3).
/// </summary>
public sealed class RoundedRectangleFinderPatternShape : FinderPatternShape
{
    public static readonly RoundedRectangleFinderPatternShape Default = new();

    private readonly float _cornerRadiusPercent;

    /// <summary>
    /// Initializes a new instance with the specified corner radius.
    /// </summary>
    /// <param name="cornerRadiusPercent">The corner radius as a percentage of the module size (0.0 to 1.0).</param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public RoundedRectangleFinderPatternShape(float cornerRadiusPercent = 0.2f)
    {
        if (cornerRadiusPercent < 0 || cornerRadiusPercent > 1)
            throw new ArgumentOutOfRangeException(nameof(cornerRadiusPercent), "Corner radius percent must be between 0 and 1.");
        _cornerRadiusPercent = cornerRadiusPercent;
    }

    /// <inheritdoc/>
    public override void Draw(SKCanvas canvas, SKRect rect, SKPaint paint)
    {
        var moduleSize = rect.Width / 7f;
        var radius = Math.Min(rect.Width, rect.Height) * _cornerRadiusPercent;

        // Draw outer rounded rectangle (7×7)
        canvas.DrawRoundRect(rect, radius, radius, paint);

        // Draw white ring (5×5)
        using var whitePaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };
        var innerRect = SKRect.Create(
            rect.Left + moduleSize,
            rect.Top + moduleSize,
            moduleSize * 5,
            moduleSize * 5);
        canvas.DrawRoundRect(innerRect, radius * 0.8f, radius * 0.8f, whitePaint);

        // Draw black center (3×3)
        var centerRect = SKRect.Create(
            rect.Left + moduleSize * 2,
            rect.Top + moduleSize * 2,
            moduleSize * 3,
            moduleSize * 3);
        canvas.DrawRoundRect(centerRect, radius * 0.6f, radius * 0.6f, paint);
    }
}

/// <summary>
/// Rounded rectangle outer with circular center finder pattern. (outer rounded rectangle 7x7, middle rounded rectangle 5x5, inner circle 3x3)
/// </summary>
public sealed class RoundedRectangleCircleFinderPatternShape : FinderPatternShape
{
    public static readonly RoundedRectangleCircleFinderPatternShape Default = new();

    private readonly float _cornerRadiusPercent;

    /// <summary>
    /// Initializes a new instance with the specified corner radius.
    /// </summary>
    /// <param name="cornerRadiusPercent">The corner radius as a percentage of the module size (0.0 to 1.0).</param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public RoundedRectangleCircleFinderPatternShape(float cornerRadiusPercent = 0.3f)
    {
        if (cornerRadiusPercent < 0 || cornerRadiusPercent > 1)
            throw new ArgumentOutOfRangeException(nameof(cornerRadiusPercent), "Corner radius percent must be between 0 and 1.");
        _cornerRadiusPercent = cornerRadiusPercent;
    }

    /// <inheritdoc/>
    public override void Draw(SKCanvas canvas, SKRect rect, SKPaint paint)
    {
        var center = new SKPoint(rect.MidX, rect.MidY);
        var moduleSize = rect.Width / 7f;

        // Corner radius for rounded rectangle
        var cornerRadius = Math.Min(rect.Width, rect.Height) * _cornerRadiusPercent;

        // Base radius for circles (half of the rect size)
        var baseRadius = Math.Min(rect.Width, rect.Height) / 2f;

        // Draw outer rounded rectangle (7×7)
        canvas.DrawRoundRect(rect, cornerRadius, cornerRadius, paint);

        // Draw white ring (5×5)
        using var whitePaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };
        var innerRect = SKRect.Create(
            rect.Left + moduleSize,
            rect.Top + moduleSize,
            moduleSize * 5,
            moduleSize * 5);
        canvas.DrawRoundRect(innerRect, cornerRadius * 0.7f, cornerRadius * 0.7f, whitePaint);

        // Draw black center (3×3) - 3/7 of the base radius
        canvas.DrawCircle(center, baseRadius * (3f / 7f), paint);
    }
}
