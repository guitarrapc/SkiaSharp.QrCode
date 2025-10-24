namespace SkiaSharp.QrCode.Image;

public class GradientOptions
{
    /// <summary>
    /// The start color of the gradient.
    /// </summary>
    public SKColor StartColor { get; set; } = SKColors.Black;
    /// <summary>
    /// The end color of the gradient.
    /// </summary>
    public SKColor EndColor { get; set; } = SKColors.Black;
    /// <summary>
    /// The gradient direction.
    /// </summary>
    public GradientDirection Direction { get; set; }
    /// <summary>
    /// Optional color stops (0.0 to 1.0) for the gradient.
    /// </summary>
    public float[]? ColorPositions { get; set; }
    /// <summary>
    /// Additional gradient colors for multi-color gradients.
    /// </summary>
    public SKColor[]? Colors { get; set; }
}

public enum GradientDirection
{
    /// <summary>
    /// No gradient (solid color).
    /// </summary>
    None = 0,
    /// <summary>
    /// Left to Right linear gradient.
    /// </summary>
    LeftToRight,
    /// <summary>
    /// Right to Left linear gradient. 
    /// </summary>
    RightToLeft,
    /// <summary>
    /// Top to Bottom linear gradient. 
    /// </summary>
    TopToBottom,
    /// <summary>
    /// Bottom to Top linear gradient.
    /// </summary>
    BottomToTop,
    /// <summary>
    /// Top-Left to Bottom-Right linear gradient.
    /// </summary>
    TopLeftToBottomRight,
    /// <summary>
    /// Top-Right to Bottom-Left linear gradient. 
    /// </summary>
    TopRightToBottomLeft,
    /// <summary>
    /// Bottom-Left to Top-Right linear gradient. 
    /// </summary>
    BottomLeftToTopRight,
    /// <summary>
    /// Bottom-Right to Top-Left linear gradient.
    /// </summary>
    BottomRightToTopLeft,
}

