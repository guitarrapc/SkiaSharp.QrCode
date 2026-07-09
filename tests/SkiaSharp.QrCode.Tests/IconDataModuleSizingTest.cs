using SkiaSharp.QrCode.Image;
using Xunit;

namespace SkiaSharp.QrCode.Tests;

public class IconDataModuleSizingTest
{
    private const string TestContent = "HELLO-ICON";

    [Fact]
    public void FromImageByModules_ValidArgs_SetsModuleProperties()
    {
        using var logo = CreateLogo(32);
        var icon = IconData.FromImageByModules(logo, iconSizeModules: 5, iconBorderModules: 1, maxCoreOccupancyPercent: 40);

        Assert.Equal(5, icon.IconSizeModules);
        Assert.Equal(1, icon.IconBorderModules);
        Assert.Equal(40, icon.MaxCoreOccupancyPercent);
    }

    [Theory]
    [InlineData(0, 1, 30)]
    [InlineData(-1, 1, 30)]
    [InlineData(5, -1, 30)]
    [InlineData(5, 1, 0)]
    [InlineData(5, 1, 101)]
    public void FromImageByModules_InvalidArgs_ThrowsArgumentOutOfRangeException(
        int iconSizeModules,
        int iconBorderModules,
        int maxCoreOccupancyPercent)
    {
        using var logo = CreateLogo(16);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            IconData.FromImageByModules(logo, iconSizeModules, iconBorderModules, maxCoreOccupancyPercent));
    }

    [Fact]
    public void GetIconRects_ModuleSizing_WithIntegerModulePixels_AlignsToModules()
    {
        const int modulePixelSize = 10;
        const int iconSizeModules = 5;
        const int iconBorderModules = 1;

        var qrData = QRCodeGenerator.CreateQrCode(TestContent, ECCLevel.H, requestedVersion: 1, quietZoneSize: 4);
        var imageSide = qrData.Size * modulePixelSize;
        var area = SKRect.Create(0, 0, imageSide, imageSide);

        using var logo = CreateLogo(32);
        var icon = IconData.FromImageByModules(logo, iconSizeModules, iconBorderModules, maxCoreOccupancyPercent: 40);

        var (iconRect, borderRect) = QRCodeRenderer.GetIconRects(qrData, area, icon);

        Assert.Equal(iconSizeModules * modulePixelSize, iconRect.Width);
        Assert.Equal(iconSizeModules * modulePixelSize, iconRect.Height);
        Assert.Equal((iconSizeModules + iconBorderModules * 2) * modulePixelSize, borderRect.Width);
        Assert.Equal((iconSizeModules + iconBorderModules * 2) * modulePixelSize, borderRect.Height);
        Assert.Equal(area.MidX, iconRect.MidX, 0.001f);
        Assert.Equal(area.MidY, iconRect.MidY, 0.001f);
    }

    [Fact]
    public void GetIconRects_ModuleSizing_IgnoresPercentAndPixelBorder()
    {
        const int modulePixelSize = 8;
        var qrData = QRCodeGenerator.CreateQrCode(TestContent, ECCLevel.H, requestedVersion: 5, quietZoneSize: 4);
        var imageSide = qrData.Size * modulePixelSize;
        var area = SKRect.Create(0, 0, imageSide, imageSide);

        using var logo = CreateLogo(32);
        var icon = IconData.FromImageByModules(logo, iconSizeModules: 7, iconBorderModules: 1);
        icon.IconSizePercent = 50;
        icon.IconBorderWidth = 99;

        var (iconRect, borderRect) = QRCodeRenderer.GetIconRects(qrData, area, icon);

        Assert.Equal(7 * modulePixelSize, iconRect.Width);
        Assert.Equal(9 * modulePixelSize, borderRect.Width);
    }

    [Fact]
    public void GetIconRects_ModuleSizing_NullBorderModules_DefaultsToOne()
    {
        const int modulePixelSize = 10;
        var qrData = QRCodeGenerator.CreateQrCode(TestContent, ECCLevel.H, requestedVersion: 5, quietZoneSize: 4);
        var imageSide = qrData.Size * modulePixelSize;
        var area = SKRect.Create(0, 0, imageSide, imageSide);

        using var logo = CreateLogo(16);
        var icon = new IconData
        {
            Icon = new ImageIconShape(logo),
            IconSizeModules = 5,
            IconBorderModules = null,
        };

        var (iconRect, borderRect) = QRCodeRenderer.GetIconRects(qrData, area, icon);

        Assert.Equal(50, iconRect.Width);
        Assert.Equal(70, borderRect.Width);
    }

    [Fact]
    public void GetIconRects_ModuleSizing_ExceedsMatrixSize_ThrowsInvalidOperationException()
    {
        var qrData = QRCodeGenerator.CreateQrCode(TestContent, ECCLevel.H, requestedVersion: 1, quietZoneSize: 0);
        // Version 1 core/matrix size = 21
        var area = SKRect.Create(0, 0, 210, 210);

        using var logo = CreateLogo(16);
        var icon = IconData.FromImageByModules(logo, iconSizeModules: 21, iconBorderModules: 1, maxCoreOccupancyPercent: 100);

        var ex = Assert.Throws<InvalidOperationException>(() => QRCodeRenderer.GetIconRects(qrData, area, icon));
        Assert.Contains("exceeds QR matrix size", ex.Message);
    }

    [Fact]
    public void GetIconRects_ModuleSizing_ExceedsCoreOccupancy_ThrowsInvalidOperationException()
    {
        var qrData = QRCodeGenerator.CreateQrCode(TestContent, ECCLevel.H, requestedVersion: 1, quietZoneSize: 4);
        // coreSize=21, 30% => max 6 modules
        var area = SKRect.Create(0, 0, qrData.Size * 10, qrData.Size * 10);

        using var logo = CreateLogo(16);
        var icon = IconData.FromImageByModules(logo, iconSizeModules: 5, iconBorderModules: 1); // total 7 > 6

        var ex = Assert.Throws<InvalidOperationException>(() => QRCodeRenderer.GetIconRects(qrData, area, icon));
        Assert.Contains("max allowed is", ex.Message);
        Assert.Contains("maxCoreOccupancyPercent=30", ex.Message);
    }

    [Fact]
    public void Builder_WithModulePixelSize_AndModuleIcon_RendersSuccessfully()
    {
        using var logo = CreateLogo(64);
        var icon = IconData.FromImageByModules(logo, iconSizeModules: 5, iconBorderModules: 0);

        using var bitmap = new QRCodeImageBuilder(TestContent)
            .WithErrorCorrection(ECCLevel.H)
            .WithVersion(1)
            .WithQuietZone(4)
            .WithModulePixelSize(10)
            .WithIcon(icon)
            .ToBitmap();

        Assert.Equal(290, bitmap.Width); // 29 modules * 10px
        Assert.Equal(290, bitmap.Height);
    }

    [Fact]
    public void GetIconRects_PercentSizing_StillWorks()
    {
        var qrData = QRCodeGenerator.CreateQrCode(TestContent, ECCLevel.M, requestedVersion: 1, quietZoneSize: 4);
        var area = SKRect.Create(0, 0, 500, 500);

        using var logo = CreateLogo(16);
        var icon = IconData.FromImage(logo, iconSizePercent: 20, iconBorderWidth: 10);

        var (iconRect, borderRect) = QRCodeRenderer.GetIconRects(qrData, area, icon);

        Assert.Equal(100, iconRect.Width);
        Assert.Equal(100, iconRect.Height);
        Assert.Equal(120, borderRect.Width);
        Assert.Equal(120, borderRect.Height);
    }

    private static SKBitmap CreateLogo(int size)
    {
        var bitmap = new SKBitmap(size, size);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Red);
        return bitmap;
    }
}
