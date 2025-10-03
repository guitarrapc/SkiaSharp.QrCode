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
    [InlineData('a', false)] // 小文字は無効
    [InlineData('z', false)]
    [InlineData('@', false)]
    [InlineData('#', false)]
    public void AlphanumEncTable(char c, bool expected)
    {
        var result = QrCodeConstants.AlphanumEncTable.Contains(c);
        Assert.Equal(expected, result);
    }
}
