using SkiaSharp.QrCode.Internals.ImageDecoders;
using Xunit;

namespace SkiaSharp.QrCode.Tests;

public class PerspectiveTransformUnitTest
{
    [Fact]
    public void QuadToQuad_MapsCorrespondencePoints()
    {
        // Map a square onto a keystone quad; all four correspondences must hold.
        var transform = PerspectiveTransform.QuadrilateralToQuadrilateral(
            0, 0, 10, 0, 10, 10, 0, 10,
            2, 1, 11, 3, 9, 12, 1, 9);

        AssertMaps(transform, 0, 0, 2, 1);
        AssertMaps(transform, 10, 0, 11, 3);
        AssertMaps(transform, 10, 10, 9, 12);
        AssertMaps(transform, 0, 10, 1, 9);
    }

    [Fact]
    public void QuadToQuad_AffineCase_MapsMidpoints()
    {
        // Pure affine mapping (parallelogram): midpoints must map to midpoints
        var transform = PerspectiveTransform.QuadrilateralToQuadrilateral(
            0, 0, 4, 0, 4, 4, 0, 4,
            10, 20, 18, 22, 20, 30, 12, 28);

        AssertMaps(transform, 2, 2, 15, 25);
        AssertMaps(transform, 0, 2, 11, 24);
    }

    [Fact]
    public void QuadToQuad_Identity_IsExact()
    {
        var transform = PerspectiveTransform.QuadrilateralToQuadrilateral(
            3.5f, 3.5f, 21.5f, 3.5f, 18.5f, 18.5f, 3.5f, 21.5f,
            3.5f, 3.5f, 21.5f, 3.5f, 18.5f, 18.5f, 3.5f, 21.5f);

        AssertMaps(transform, 12.5f, 7.25f, 12.5f, 7.25f);
    }

    [Fact]
    public void QuadToQuad_PerspectiveRoundTrip()
    {
        // Forward then inverse mapping returns the original points
        var forward = PerspectiveTransform.QuadrilateralToQuadrilateral(
            0, 0, 100, 0, 100, 100, 0, 100,
            10, 15, 105, 5, 95, 110, 5, 95);
        var inverse = PerspectiveTransform.QuadrilateralToQuadrilateral(
            10, 15, 105, 5, 95, 110, 5, 95,
            0, 0, 100, 0, 100, 100, 0, 100);

        for (var y = 10; y <= 90; y += 40)
        {
            for (var x = 10; x <= 90; x += 40)
            {
                forward.Transform(x, y, out var fx, out var fy);
                inverse.Transform(fx, fy, out var bx, out var by);
                Assert.Equal(x, bx, 2f);
                Assert.Equal(y, by, 2f);
            }
        }
    }

    private static void AssertMaps(in PerspectiveTransform transform, float x, float y, float expectedX, float expectedY)
    {
        transform.Transform(x, y, out var actualX, out var actualY);
        Assert.Equal(expectedX, actualX, 0.01f);
        Assert.Equal(expectedY, actualY, 0.01f);
    }
}
