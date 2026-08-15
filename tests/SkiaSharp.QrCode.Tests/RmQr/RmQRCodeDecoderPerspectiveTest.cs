namespace SkiaSharp.QrCode.Tests;

/// <summary>
/// Tier-2 rMQR image decoding: mild perspective (keystone along either symbol
/// axis), including composition with rotation and mirroring. rMQR symbols are
/// wide, so the far end of the symbol is where the sub-finder anchored
/// refinement earns its keep; the envelope asserted here is the measured one.
/// </summary>
public class RmQRCodeDecoderPerspectiveTest
{
    [Test]
    [Arguments(RmQRVersion.R7x43, 0.02f)]
    [Arguments(RmQRVersion.R7x43, 0.04f)]
    [Arguments(RmQRVersion.R11x77, 0.02f)]
    [Arguments(RmQRVersion.R11x77, 0.04f)]
    [Arguments(RmQRVersion.R17x139, 0.02f)]
    [Arguments(RmQRVersion.R17x139, 0.04f)]
    public async Task Decode_KeystoneTopShrunk(RmQRVersion version, float tilt)
    {
        var content = ContentFor(version);
        using var bitmap = RenderKeystone(content, version, tilt, horizontal: false, rotateDegrees: 0);

        var success = RmQRCodeDecoder.TryDecode(bitmap, out var decoded, out var info);

        await Assert.That(success).IsTrue().Because($"version={version}, tilt={tilt:P0}, status={info.Status}");
        await Assert.That(decoded).IsEqualTo(content);
        await Assert.That(info.Version).IsEqualTo(version);
    }

    [Test]
    [Arguments(RmQRVersion.R7x43, 0.02f)]
    [Arguments(RmQRVersion.R7x43, 0.04f)]
    [Arguments(RmQRVersion.R11x77, 0.02f)]
    [Arguments(RmQRVersion.R11x77, 0.04f)]
    [Arguments(RmQRVersion.R17x139, 0.02f)]
    [Arguments(RmQRVersion.R17x139, 0.04f)]
    public async Task Decode_KeystoneLeftShrunk(RmQRVersion version, float tilt)
    {
        var content = ContentFor(version);
        using var bitmap = RenderKeystone(content, version, tilt, horizontal: true, rotateDegrees: 0);

        var success = RmQRCodeDecoder.TryDecode(bitmap, out var decoded, out var info);

        await Assert.That(success).IsTrue().Because($"version={version}, tilt={tilt:P0}, status={info.Status}");
        await Assert.That(decoded).IsEqualTo(content);
        await Assert.That(info.Version).IsEqualTo(version);
    }

    [Test]
    [Arguments(RmQRVersion.R7x43, 0.02f, 30)]
    [Arguments(RmQRVersion.R13x99, 0.02f, 30)]
    public async Task Decode_RotationPlusKeystone(RmQRVersion version, float tilt, int degrees)
    {
        var content = ContentFor(version);
        using var bitmap = RenderKeystone(content, version, tilt, horizontal: false, rotateDegrees: degrees);

        var success = RmQRCodeDecoder.TryDecode(bitmap, out var decoded, out var info);

        await Assert.That(success).IsTrue().Because($"version={version}, tilt={tilt:P0}, degrees={degrees}, status={info.Status}");
        await Assert.That(decoded).IsEqualTo(content);
    }

    [Test]
    public async Task Decode_MirrorPlusKeystone()
    {
        const string content = "RMQR IMAGE 123";
        using var source = RenderKeystone(content, RmQRVersion.R11x77, tilt: 0.02f, horizontal: false, rotateDegrees: 0);
        using var mirrored = new SKBitmap(new SKImageInfo(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(mirrored))
        {
            canvas.Clear(SKColors.White);
            canvas.Scale(-1, 1, source.Width / 2f, 0);
            canvas.DrawBitmap(source, 0, 0, SKSamplingOptions.Default);
        }

        var success = RmQRCodeDecoder.TryDecode(mirrored, out var decoded, out var info);

        await Assert.That(success).IsTrue().Because($"status={info.Status}");
        await Assert.That(decoded).IsEqualTo(content);
    }

    private static string ContentFor(RmQRVersion version)
        => version == RmQRVersion.R7x43 ? "RMQR 43" : "RMQR IMAGE 123";

    private static SKBitmap RenderKeystone(string content, RmQRVersion version, float tilt, bool horizontal, float rotateDegrees)
    {
        var qr = RmQRCodeGenerator.CreateRmQRCode(content, RmQREccLevel.M, version, quietZoneSize: 2);
        var widthPx = qr.Width * 8;
        var heightPx = qr.Height * 8;

        using var flat = new SKBitmap(new SKImageInfo(widthPx, heightPx, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(flat))
        {
            QRCodeRenderer.Render(canvas, SKRect.Create(0, 0, widthPx, heightPx), qr, SKColors.Black, SKColors.White);
            canvas.Flush();
        }

        var diagonal = (int)Math.Sqrt(widthPx * widthPx + heightPx * heightPx);
        var canvasPx = diagonal + 64;
        var result = new SKBitmap(new SKImageInfo(canvasPx, canvasPx, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(result))
        {
            canvas.Clear(SKColors.White);

            var left = (canvasPx - widthPx) / 2f;
            var top = (canvasPx - heightPx) / 2f;
            SKMatrix warp;
            if (horizontal)
            {
                // Left edge shrunk vertically: the finder-side end is farther away
                var shrink = tilt * heightPx;
                warp = SquareToQuad(
                    widthPx,
                    heightPx,
                    new SKPoint(left, top + shrink),
                    new SKPoint(left + widthPx, top),
                    new SKPoint(left + widthPx, top + heightPx),
                    new SKPoint(left, top + heightPx - shrink));
            }
            else
            {
                // Top edge shrunk horizontally (classic keystone)
                var shrink = tilt * widthPx;
                warp = SquareToQuad(
                    widthPx,
                    heightPx,
                    new SKPoint(left + shrink, top),
                    new SKPoint(left + widthPx - shrink, top),
                    new SKPoint(left + widthPx, top + heightPx),
                    new SKPoint(left, top + heightPx));
            }

            if (rotateDegrees != 0)
            {
                var rotation = SKMatrix.CreateRotationDegrees(rotateDegrees, canvasPx / 2f, canvasPx / 2f);
                warp = rotation.PreConcat(warp);
            }

            canvas.SetMatrix(warp);
            canvas.DrawBitmap(flat, 0, 0, SKSamplingOptions.Default);
            canvas.Flush();
        }

        return result;
    }

    private static SKMatrix SquareToQuad(float width, float height, SKPoint topLeft, SKPoint topRight, SKPoint bottomRight, SKPoint bottomLeft)
    {
        var dx3 = topLeft.X - topRight.X + bottomRight.X - bottomLeft.X;
        var dy3 = topLeft.Y - topRight.Y + bottomRight.Y - bottomLeft.Y;
        float a13, a23, a11, a21, a12, a22;
        if (dx3 == 0f && dy3 == 0f)
        {
            a11 = topRight.X - topLeft.X;
            a21 = bottomRight.X - topRight.X;
            a12 = topRight.Y - topLeft.Y;
            a22 = bottomRight.Y - topRight.Y;
            a13 = 0f;
            a23 = 0f;
        }
        else
        {
            var dx1 = topRight.X - bottomRight.X;
            var dx2 = bottomLeft.X - bottomRight.X;
            var dy1 = topRight.Y - bottomRight.Y;
            var dy2 = bottomLeft.Y - bottomRight.Y;
            var denominator = dx1 * dy2 - dx2 * dy1;
            a13 = (dx3 * dy2 - dx2 * dy3) / denominator;
            a23 = (dx1 * dy3 - dx3 * dy1) / denominator;
            a11 = topRight.X - topLeft.X + a13 * topRight.X;
            a21 = bottomLeft.X - topLeft.X + a23 * bottomLeft.X;
            a12 = topRight.Y - topLeft.Y + a13 * topRight.Y;
            a22 = bottomLeft.Y - topLeft.Y + a23 * bottomLeft.Y;
        }

        return new SKMatrix
        {
            ScaleX = a11 / width,
            SkewX = a21 / height,
            TransX = topLeft.X,
            SkewY = a12 / width,
            ScaleY = a22 / height,
            TransY = topLeft.Y,
            Persp0 = a13 / width,
            Persp1 = a23 / height,
            Persp2 = 1f,
        };
    }
}
