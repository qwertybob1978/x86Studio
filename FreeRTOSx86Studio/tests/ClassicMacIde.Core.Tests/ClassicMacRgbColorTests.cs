using ClassicMacIde.Core;

namespace ClassicMacIde.Core.Tests;

public class ClassicMacRgbColorTests
{
    [Fact]
    public void ParsesHexRgbAndExpandsChannelsToQuickDrawPrecision()
    {
        var color = ClassicMacRgbColor.Parse("#336699");

        Assert.Equal((0x33, 0x66, 0x99), (color.Red, color.Green, color.Blue));
        Assert.Equal((0x3333, 0x6666, 0x9999),
            (color.Red16, color.Green16, color.Blue16));
    }

    [Theory]
    [InlineData("336699")]
    [InlineData("#369")]
    [InlineData("#GG6699")]
    [InlineData("#336699FF")]
    public void RejectsValuesOutsideSixDigitHexRgb(string value)
    {
        Assert.False(ClassicMacRgbColor.TryParse(value, out _));
        Assert.Throws<FormatException>(() => ClassicMacRgbColor.Parse(value));
    }
}
