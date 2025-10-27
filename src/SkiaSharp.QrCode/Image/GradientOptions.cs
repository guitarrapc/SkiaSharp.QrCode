namespace SkiaSharp.QrCode.Image;

/// <summary>
/// Defines gradient configuration for QR code rendering.
/// </summary>
/// <remarks>
/// <para>
/// This record configures linear gradients that are applied across the entire QR code.
/// Gradients can flow in various directions (horizontal, vertical, diagonal).
/// </para>
/// <para>
/// For simple two-color gradients, specify two colors in <see cref="Colors"/>.
/// For multi-color gradients, provide additional colors and optionally specify <see cref="ColorPositions"/>.
/// </para>
/// </remarks>
public record class GradientOptions
{
    public static readonly GradientOptions Default = new([SKColors.DarkOrange, SKColors.Firebrick], GradientDirection.TopLeftToBottomRight);

    /// <summary>
    /// Initializes a new instance of the GradientOptions record.
    /// </summary>
    /// <param name="colors">Gradient colors. At least 2 colors are required.</param>
    /// <param name="direction">Gradient direction.</param>
    /// <param name="colorPositions">Optional color stop positions (0.0 to 1.0). If null, colors are evenly distributed.</param>
    /// <exception cref="ArgumentNullException">Thrown when colors is null.</exception>
    /// <exception cref="ArgumentException">Thrown when colors has fewer than 2 elements.</exception>
    public GradientOptions(SKColor[] colors, GradientDirection direction = GradientDirection.LeftToRight, float[]? colorPositions = null)
    {
        if (colors is null)
            throw new ArgumentNullException(nameof(colors));
        if (colors.Length < 2)
            throw new ArgumentException("At least 2 colors are required for gradient", nameof(colors));
        if (colorPositions is not null && colorPositions.Length != colors.Length)
            throw new ArgumentException("Color positions length must match colors length", nameof(colorPositions));

        Colors = colors;
        Direction = direction;
        ColorPositions = colorPositions;
    }

    /// <summary>
    /// Gradient colors for multi-color gradients.
    /// </summary>
    /// <remarks>
    /// At least 2 colors are required. The gradient flows from the first color to the last color
    /// in the direction specified by <see cref="Direction"/>.
    /// </remarks>
    public SKColor[] Colors { get; init; }

    /// <summary>
    /// The gradient direction.
    /// </summary>
    /// <remarks>
    /// Determines the start and end points of the gradient across the QR code area.
    /// </remarks>
    public GradientDirection Direction { get; init; }

    /// <summary>
    /// Optional color stops (0.0 to 1.0) for the gradient.
    /// </summary>
    /// <remarks>
    /// <para>
    /// If null, colors are evenly distributed across the gradient.
    /// If specified, the array length must match <see cref="Colors"/> length.
    /// </para>
    /// <para>
    /// Values must be in ascending order from 0.0 (start) to 1.0 (end).
    /// For example: [0.0f, 0.3f, 1.0f] for three colors.
    /// </para>
    /// </remarks>
    public float[]? ColorPositions { get; init; }
}

/// <summary>
/// Defines the direction of gradient flow for QR code rendering.
/// </summary>
/// <remarks>
/// <para>
/// Linear gradients flow in a straight line across the entire QR code area.
/// The direction determines the start and end points of the gradient.
/// </para>
/// <para>
/// <b>Common Use Cases:</b>
/// </para>
/// <list type="bullet">
/// <item><description><see cref="LeftToRight"/> or <see cref="TopToBottom"/>: Simple horizontal/vertical gradients</description></item>
/// <item><description><see cref="TopLeftToBottomRight"/>: Diagonal gradients for dynamic appearance</description></item>
/// <item><description><see cref="None"/>: Use when solid color is desired (disables gradient)</description></item>
/// </list>
/// </remarks>
public enum GradientDirection
{
    /// <summary>
    /// No gradient. Use solid color specified in QR code rendering options.
    /// However, if solid is required, it's better to omit gradient options entirely.
    /// </summary>
    None = 0,
    /// <summary>
    /// Gradient flows from left edge to right edge horizontally.
    /// </summary>
    LeftToRight,
    /// <summary>
    /// Gradient flows from right edge to left edge horizontally.
    /// </summary>
    RightToLeft,
    /// <summary>
    /// Gradient flows from top edge to bottom edge vertically.
    /// </summary>
    TopToBottom,
    /// <summary>
    /// Gradient flows from bottom edge to top edge vertically.
    /// </summary>
    BottomToTop,
    /// <summary>
    /// Gradient flows diagonally from top-left corner to bottom-right corner.
    /// </summary>
    TopLeftToBottomRight,
    /// <summary>
    /// Gradient flows diagonally from top-right corner to bottom-left corner.
    /// </summary>
    TopRightToBottomLeft,
    /// <summary>
    /// Gradient flows diagonally from bottom-left corner to top-right corner.
    /// </summary>
    BottomLeftToTopRight,
    /// <summary>
    /// Gradient flows diagonally from bottom-right corner to top-left corner.
    /// </summary>
    BottomRightToTopLeft,
}

