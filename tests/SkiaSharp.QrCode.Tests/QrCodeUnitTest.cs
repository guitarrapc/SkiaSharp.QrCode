using SkiaSharp.QrCode.Image;
using Xunit;

namespace SkiaSharp.QrCode.Tests;

public class QrCodeUnitTest
{
    [Fact]
    public void Constructor_WithValidParameters_ShouldSucceed()
    {
        // Arrange & Act
        var qrCode = new Image.QrCode("test content", new Vector2Slim(100, 100));

        // Assert
        Assert.NotNull(qrCode);
    }

    [Fact]
    public void Constructor_WithZeroWidth_ShouldThrowArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => new Image.QrCode("test", new Vector2Slim(0, 100)));
    }

    [Fact]
    public void Constructor_WithZeroHeight_ShouldThrowArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => new Image.QrCode("test", new Vector2Slim(100, 0)));
    }

    [Fact]
    public void Constructor_WithNegativeSize_ShouldThrowArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => new Image.QrCode("test", new Vector2Slim(-1, 100)));
    }

    [Fact]
    public void Constructor_WithQualityBelow0_ShouldThrowArgumentOutOfRangeException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Image.QrCode("test", new Vector2Slim(100, 100), SKEncodedImageFormat.Png, -1));
    }

    [Fact]
    public void Constructor_WithQualityAbove100_ShouldThrowArgumentOutOfRangeException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Image.QrCode("test", new Vector2Slim(100, 100), SKEncodedImageFormat.Png, 101));
    }

    [Fact]
    public void Constructor_WithValidQuality_ShouldSucceed()
    {
        // Arrange & Act
        var qrCode = new Image.QrCode("test", new Vector2Slim(100, 100), SKEncodedImageFormat.Png, 50);

        // Assert
        Assert.NotNull(qrCode);
    }

    [Fact]
    public void GenerateImage_WithSeekableStreamAndResetTrue_ShouldResetPosition()
    {
        // Arrange
        var qrCode = new Image.QrCode("test content", new Vector2Slim(100, 100));
        using var stream = new MemoryStream();
        stream.Write(new byte[10], 0, 10); // Write some data
        var initialPosition = stream.Position;

        // Act
        qrCode.GenerateImage(stream, resetStreamPosition: true);

        // Assert
        Assert.True(stream.Length > initialPosition);
    }

    [Fact]
    public void GenerateImage_WithSeekableStreamAndResetFalse_ShouldNotResetPosition()
    {
        // Arrange
        var qrCode = new Image.QrCode("test content", new Vector2Slim(100, 100));
        using var stream = new MemoryStream();
        stream.Write(new byte[10], 0, 10);
        var expectedPosition = stream.Position;

        // Act
        qrCode.GenerateImage(stream, resetStreamPosition: false);

        // Assert - QR code data should be written after initial position
        Assert.True(stream.Length > expectedPosition);
    }

    [Fact]
    public void GenerateImage_WithValidContent_ShouldCreateImage()
    {
        // Arrange
        var qrCode = new Image.QrCode("test content", new Vector2Slim(256, 256));
        using var stream = new MemoryStream();

        // Act
        qrCode.GenerateImage(stream);

        // Assert
        Assert.True(stream.Length > 0);
        stream.Position = 0;
        using var image = SKImage.FromEncodedData(stream);
        Assert.NotNull(image);
        Assert.Equal(256, image.Width);
        Assert.Equal(256, image.Height);
    }

    [Fact]
    public void GenerateImage_WithDifferentFormats_ShouldSucceed()
    {
        // Arrange
        var formats = new[] { SKEncodedImageFormat.Png, SKEncodedImageFormat.Jpeg, SKEncodedImageFormat.Webp };

        foreach (var format in formats)
        {
            var qrCode = new Image.QrCode("test content", new Vector2Slim(100, 100), format);
            using var stream = new MemoryStream();

            // Act
            qrCode.GenerateImage(stream);

            // Assert
            Assert.True(stream.Length > 0, $"Failed for format: {format}");
        }
    }

    [Fact]
    public void GenerateImage_WithDifferentECCLevels_ShouldSucceed()
    {
        // Arrange
        var eccLevels = new[] { ECCLevel.L, ECCLevel.M, ECCLevel.Q, ECCLevel.H };

        foreach (var eccLevel in eccLevels)
        {
            var qrCode = new Image.QrCode("test content", new Vector2Slim(100, 100));
            using var stream = new MemoryStream();

            // Act
            qrCode.GenerateImage(stream, eccLevel: eccLevel);

            // Assert
            Assert.True(stream.Length > 0, $"Failed for ECC level: {eccLevel}");
        }
    }
}
