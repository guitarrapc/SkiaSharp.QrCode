using System.Linq;
using Xunit;
using static SkiaSharp.QrCode.QRCodeGenerator;

namespace SkiaSharp.QrCode.Tests;

public class QrCodeConstantsUnitTest
{
    [Theory]
    [InlineData('0', true)]
    [InlineData('1', true)]
    [InlineData('2', true)]
    [InlineData('3', true)]
    [InlineData('4', true)]
    [InlineData('5', true)]
    [InlineData('6', true)]
    [InlineData('7', true)]
    [InlineData('8', true)]
    [InlineData('9', true)]
    [InlineData('A', false)]
    [InlineData('#', false)]
    public void IsNumericTest(char c, bool expected)
    {
        var result = QrCodeConstants.IsNumeric(c);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData('0', true)]
    [InlineData('9', true)]
    [InlineData('A', true)]
    [InlineData('Z', true)]
    [InlineData(' ', true)]
    [InlineData('$', true)]
    [InlineData('%', true)]
    [InlineData('*', true)]
    [InlineData('+', true)]
    [InlineData('-', true)]
    [InlineData('.', true)]
    [InlineData('/', true)]
    [InlineData(':', true)]
    [InlineData('a', false)] // small letters are invalid
    [InlineData('z', false)]
    [InlineData('@', false)]
    [InlineData('#', false)]
    public void IsAlphanumericTest(char c, bool expected)
    {
        var result = QrCodeConstants.IsAlphanumeric(c);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData('0', 0)]
    [InlineData('9', 9)]
    [InlineData('A', 10)]
    [InlineData('Z', 35)]
    [InlineData(' ', 36)]
    [InlineData('$', 37)]
    [InlineData('%', 38)]
    [InlineData('*', 39)]
    [InlineData('+', 40)]
    [InlineData('-', 41)]
    [InlineData('.', 42)]
    [InlineData('/', 43)]
    [InlineData(':', 44)]
    [InlineData('a', -1)] // small letters are invalid
    [InlineData('z', -1)]
    [InlineData('@', -1)]
    [InlineData('#', -1)]
    public void GetAlphanumericValueTest(char c, int expected)
    {
        var success = QrCodeConstants.TryGetAlphanumericValue(c, out var result);
        if (expected == -1)
        {
            Assert.False(success);
        }
        else
        {
            Assert.Equal(expected, result);
        }
    }
}
