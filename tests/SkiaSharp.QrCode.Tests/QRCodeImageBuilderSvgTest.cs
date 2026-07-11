using System.Buffers;
using System.Text;
using System.Xml.Linq;
using SkiaSharp.QrCode.Image;
using Xunit;

namespace SkiaSharp.QrCode.Tests;

/// <summary>
/// SVG output goes through <see cref="SKSvgCanvas"/> instead of a raster surface,
/// sharing the same layout and render pipeline as raster output. These tests pin
/// the SVG document structure (viewport, background, modules) and verify that
/// builder options (colors, gradients, icons, layout) are reflected in the output.
/// </summary>
public class QRCodeImageBuilderSvgTest
{
    private const string TestContent = "https://example.com/svg-test";

    [Fact]
    public void SaveToSvg_ProducesWellFormedSvgDocument()
    {
        using var stream = new MemoryStream();
        new QRCodeImageBuilder(TestContent)
            .WithSize(512, 512)
            .SaveToSvg(stream);

        var doc = ParseSvg(stream.ToArray());
        Assert.Equal("svg", doc.Root!.Name.LocalName);
        Assert.Equal("512", doc.Root.Attribute("width")?.Value);
        Assert.Equal("512", doc.Root.Attribute("height")?.Value);

        // Background rect plus dark module rects must be present.
        var ns = doc.Root.Name.Namespace;
        var rects = doc.Descendants(ns + "rect").ToArray();
        Assert.True(rects.Length > 1, $"Expected background + module rects, got {rects.Length}.");
        Assert.Equal("white", rects[0].Attribute("fill")?.Value);
    }

    [Fact]
    public void SaveToSvg_RootHasViewBox_SoDocumentScalesWhenEmbedded()
    {
        // Without viewBox, an SVG embedded at a different size (img/CSS) keeps its
        // content at original coordinates instead of scaling.
        var svg = new QRCodeImageBuilder(TestContent)
            .WithSize(512, 512)
            .ToSvgString();

        var doc = XDocument.Parse(svg);
        Assert.Equal("0 0 512 512", doc.Root!.Attribute("viewBox")?.Value);
    }

    [Fact]
    public void SaveToSvg_PlainRectModules_UseCrispEdges()
    {
        // Default rect modules get shape-rendering=crispEdges to avoid antialiasing
        // seams between adjacent modules at non-integer display sizes.
        var svg = new QRCodeImageBuilder(TestContent)
            .WithSize(512, 512)
            .ToSvgString();

        var doc = XDocument.Parse(svg);
        Assert.Equal("crispEdges", doc.Root!.Attribute("shape-rendering")?.Value);
    }

    [Fact]
    public void SaveToSvg_CustomShapes_KeepAntialiasing()
    {
        // Curved shapes need antialiasing; crispEdges would render them jagged.
        var svg = new QRCodeImageBuilder(TestContent)
            .WithSize(512, 512)
            .WithModuleShape(CircleModuleShape.Default, sizePercent: 0.9f)
            .ToSvgString();

        var doc = XDocument.Parse(svg);
        Assert.Null(doc.Root!.Attribute("shape-rendering"));
        Assert.NotNull(doc.Root.Attribute("viewBox"));
    }

    [Fact]
    public void SaveToSvg_CustomFinderPattern_KeepsAntialiasing()
    {
        var svg = new QRCodeImageBuilder(TestContent)
            .WithSize(512, 512)
            .WithFinderPatternShape(RoundedRectangleFinderPatternShape.Default)
            .ToSvgString();

        var doc = XDocument.Parse(svg);
        Assert.Null(doc.Root!.Attribute("shape-rendering"));
    }

    [Fact]
    public void SaveToSvg_BuiltInIconShape_UsesCrispEdges()
    {
        // Built-in icon shapes draw rectangles, bitmaps, and text only, none of
        // which degrade under crispEdges — the QR modules stay seam-free.
        using var bitmap = new SKBitmap(32, 32);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Red);
        }
        var icon = IconData.FromImage(bitmap, iconSizePercent: 15, iconBorderWidth: 4);

        var svg = new QRCodeImageBuilder(TestContent)
            .WithSize(512, 512)
            .WithErrorCorrection(ECCLevel.H)
            .WithIcon(icon)
            .ToSvgString();

        var doc = XDocument.Parse(svg);
        Assert.Equal("crispEdges", doc.Root!.Attribute("shape-rendering")?.Value);
    }

    [Fact]
    public void SaveToSvg_BufferWriter_MatchesStreamOutput()
    {
        var builder = new QRCodeImageBuilder(TestContent).WithSize(256, 256);

        using var stream = new MemoryStream();
        builder.SaveToSvg(stream);

        var writer = new ArrayBufferWriter<byte>();
        builder.SaveToSvg(writer);

        Assert.Equal(stream.ToArray(), writer.WrittenSpan.ToArray());
    }

    [Fact]
    public void SaveToSvg_BufferWriter_Null_Throws()
    {
        var builder = new QRCodeImageBuilder(TestContent).WithSize(256, 256);

        Assert.Throws<ArgumentNullException>(() => builder.SaveToSvg((IBufferWriter<byte>)null!));
    }

    [Fact]
    public void GetSvgString_Content_MatchesToSvgString()
    {
        var expected = new QRCodeImageBuilder(TestContent)
            .WithSize(512, 512)
            .WithErrorCorrection(ECCLevel.H)
            .ToSvgString();

        Assert.Equal(expected, QRCodeImageBuilder.GetSvgString(TestContent, ECCLevel.H, size: 512));
    }

    [Fact]
    public void WriteSvg_Content_MatchesGetSvgBytes()
    {
        var writer = new ArrayBufferWriter<byte>();
        QRCodeImageBuilder.WriteSvg(TestContent, writer, ECCLevel.M, size: 256);

        Assert.Equal(QRCodeImageBuilder.GetSvgBytes(TestContent, ECCLevel.M, size: 256), writer.WrittenSpan.ToArray());
    }

    [Fact]
    public void SaveToSvg_LeavesStreamOpen()
    {
        using var stream = new MemoryStream();
        new QRCodeImageBuilder(TestContent)
            .WithSize(256, 256)
            .SaveToSvg(stream);

        Assert.True(stream.CanWrite);
        Assert.True(stream.Length > 0);
    }

    [Fact]
    public void SaveToSvg_InvalidStream_Throws()
    {
        var builder = new QRCodeImageBuilder(TestContent).WithSize(256, 256);

        Assert.Throws<ArgumentNullException>(() => builder.SaveToSvg((Stream)null!));

        using var readOnly = new MemoryStream([0x00], writable: false);
        Assert.Throws<ArgumentException>(() => builder.SaveToSvg(readOnly));
    }

    [Fact]
    public void ToSvgString_MatchesSaveToSvgOutput()
    {
        var builder = new QRCodeImageBuilder(TestContent).WithSize(256, 256);

        using var stream = new MemoryStream();
        builder.SaveToSvg(stream);

        Assert.Equal(Encoding.UTF8.GetString(stream.ToArray()), builder.ToSvgString());
    }

    [Fact]
    public void ToSvgString_IsDeterministic()
    {
        var builder = new QRCodeImageBuilder(TestContent)
            .WithSize(256, 256)
            .WithGradient(new GradientOptions([SKColors.Blue, SKColors.Purple], GradientDirection.TopLeftToBottomRight));

        Assert.Equal(builder.ToSvgString(), builder.ToSvgString());
    }

    [Fact]
    public void GetSvgBytes_Content_ProducesSvg()
    {
        var bytes = QRCodeImageBuilder.GetSvgBytes(TestContent, ECCLevel.H, size: 300);

        var doc = ParseSvg(bytes);
        Assert.Equal("300", doc.Root!.Attribute("width")?.Value);
    }

    [Fact]
    public void GetSvgBytes_QrCodeData_ProducesSvg()
    {
        var qr = QRCodeGenerator.CreateQrCode(TestContent, ECCLevel.M);
        var bytes = QRCodeImageBuilder.GetSvgBytes(qr, size: 300);

        var doc = ParseSvg(bytes);
        Assert.Equal("300", doc.Root!.Attribute("width")?.Value);
    }

    [Fact]
    public void SaveSvg_Content_WritesToStream()
    {
        using var stream = new MemoryStream();
        QRCodeImageBuilder.SaveSvg(TestContent, stream, ECCLevel.M, size: 256);

        var doc = ParseSvg(stream.ToArray());
        Assert.Equal("256", doc.Root!.Attribute("width")?.Value);
    }

    [Fact]
    public void SaveSvg_QrCodeData_WritesToStream()
    {
        var qr = QRCodeGenerator.CreateQrCode(TestContent, ECCLevel.M);
        using var stream = new MemoryStream();
        QRCodeImageBuilder.SaveSvg(qr, stream, size: 256);

        var doc = ParseSvg(stream.ToArray());
        Assert.Equal("256", doc.Root!.Attribute("width")?.Value);
    }

    [Fact]
    public void WithColors_AreReflectedInSvg()
    {
        // Use colors without SVG named-color aliases; Skia writes named colors
        // (e.g. #000080 becomes "navy") when an exact match exists.
        var svg = new QRCodeImageBuilder(TestContent)
            .WithSize(256, 256)
            .WithColors(codeColor: SKColor.Parse("123456"), backgroundColor: SKColor.Parse("FEDCBA"))
            .ToSvgString();

        Assert.Contains("#123456", svg);
        Assert.Contains("#FEDCBA", svg);
    }

    [Fact]
    public void WithGradient_EmitsLinearGradient()
    {
        var svg = new QRCodeImageBuilder(TestContent)
            .WithSize(256, 256)
            .WithGradient(new GradientOptions([SKColors.Blue, SKColors.Purple], GradientDirection.TopLeftToBottomRight))
            .ToSvgString();

        var doc = XDocument.Parse(svg);
        var ns = doc.Root!.Name.Namespace;
        Assert.NotEmpty(doc.Descendants(ns + "linearGradient"));
    }

    [Fact]
    public void WithIcon_EmbedsImageAsDataUri()
    {
        using var bitmap = new SKBitmap(32, 32);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Red);
        }
        var icon = IconData.FromImage(bitmap, iconSizePercent: 15, iconBorderWidth: 4);

        var svg = new QRCodeImageBuilder(TestContent)
            .WithSize(256, 256)
            .WithErrorCorrection(ECCLevel.H)
            .WithIcon(icon)
            .ToSvgString();

        var doc = XDocument.Parse(svg);
        var ns = doc.Root!.Name.Namespace;
        Assert.NotEmpty(doc.Descendants(ns + "image"));
        Assert.Contains("base64", svg);
    }

    [Fact]
    public void WithModulePixelSize_SetsViewportToMatrixTimesPixelSize()
    {
        const int modulePixelSize = 10;
        var qr = QRCodeGenerator.CreateQrCode(TestContent, ECCLevel.M);

        var svg = new QRCodeImageBuilder(qr)
            .WithModulePixelSize(modulePixelSize)
            .ToSvgString();

        var doc = XDocument.Parse(svg);
        var expected = (qr.Size * modulePixelSize).ToString();
        Assert.Equal(expected, doc.Root!.Attribute("width")?.Value);
        Assert.Equal(expected, doc.Root.Attribute("height")?.Value);
    }

    [Fact]
    public void WithModulePixelSize_AndCanvas_PadsWithClearColor()
    {
        const int modulePixelSize = 4;
        var qr = QRCodeGenerator.CreateQrCode(TestContent, ECCLevel.M);
        var contentSide = qr.Size * modulePixelSize;
        var canvasSide = contentSide + 40;

        var svg = new QRCodeImageBuilder(qr)
            .WithModulePixelSize(modulePixelSize)
            .WithSize(canvasSide, canvasSide)
            .WithColors(clearColor: SKColor.Parse("102030"))
            .ToSvgString();

        var doc = XDocument.Parse(svg);
        var ns = doc.Root!.Name.Namespace;
        var rects = doc.Descendants(ns + "rect").ToArray();

        // First rect is the full-canvas clear, second is the QR background offset by the pad.
        Assert.Equal("#102030", rects[0].Attribute("fill")?.Value);
        Assert.Equal(canvasSide.ToString(), rects[0].Attribute("width")?.Value);
        Assert.Equal("white", rects[1].Attribute("fill")?.Value);
        Assert.Equal("20", rects[1].Attribute("x")?.Value);
        Assert.Equal(contentSide.ToString(), rects[1].Attribute("width")?.Value);
    }

    private static XDocument ParseSvg(byte[] utf8Bytes)
        => XDocument.Parse(Encoding.UTF8.GetString(utf8Bytes));
}
