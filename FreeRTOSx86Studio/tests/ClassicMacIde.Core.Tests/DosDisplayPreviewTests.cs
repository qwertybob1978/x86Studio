using ClassicMacIde.Core;

namespace ClassicMacIde.Core.Tests;

public sealed class DosDisplayPreviewTests
{
    public static IEnumerable<object[]> DosProfiles =>
        FreeRTOSx86TargetProfiles.All.Select(profile => new object[] { profile.Id });

    [Theory]
    [MemberData(nameof(DosProfiles))]
    public void PreviewMetricsKeepEveryDosProfileAtFourByThree(
        string profileId)
    {
        var profile = FreeRTOSx86TargetProfiles.All.Single(item => item.Id == profileId);
        var metrics = DosDisplayPreviewMetrics.Create(profile);

        Assert.Equal(4d / 3d, metrics.Width / metrics.Height, precision: 10);
        Assert.Equal(profile.ScreenWidth, metrics.Width / metrics.ScaleX, precision: 10);
        Assert.Equal(profile.ScreenHeight, metrics.Height / metrics.ScaleY, precision: 10);
    }

    [Fact]
    public void OrderedMonochromeDitherRendersMidGrayAsHalfWhite()
    {
        var pattern = DosDisplayDither.CreateOrdered8x8Pattern(
            ClassicMacRgbColor.Parse("#808080"),
            [ClassicMacRgbColor.Parse("#000000"), ClassicMacRgbColor.Parse("#FFFFFF")]);

        Assert.Equal(64, pattern.Count);
        Assert.Equal(32, pattern.Count(color => color == ClassicMacRgbColor.Parse("#FFFFFF")));
        Assert.Equal(32, pattern.Count(color => color == ClassicMacRgbColor.Parse("#000000")));
    }

    [Fact]
    public void OrderedColorDitherUsesTheSelectedCgaPalette()
    {
        var black = ClassicMacRgbColor.Parse("#000000");
        var green = ClassicMacRgbColor.Parse("#00AA00");
        var palette =
            new[]
            {
                black,
                green,
                ClassicMacRgbColor.Parse("#AA0000"),
                ClassicMacRgbColor.Parse("#AA5500"),
            };

        var pattern = DosDisplayDither.CreateOrdered8x8Pattern(
            ClassicMacRgbColor.Parse("#005500"),
            palette);

        Assert.Equal(32, pattern.Count(color => color == green));
        Assert.Equal(32, pattern.Count(color => color == black));
        Assert.All(pattern, color => Assert.Contains(color, palette));
    }

    [Fact]
    public void MonochromeDitherChoicesExposeThreeUsefulGrayLevels()
    {
        var choices = DosDisplayDither.GetRepresentativeChoices(
            [ClassicMacRgbColor.Parse("#000000"), ClassicMacRgbColor.Parse("#FFFFFF")]);

        Assert.Collection(
            choices,
            choice =>
            {
                Assert.Equal("#404040", Format(choice.Color));
                Assert.Equal(25, choice.HighColorPercent);
            },
            choice =>
            {
                Assert.Equal("#808080", Format(choice.Color));
                Assert.Equal(50, choice.HighColorPercent);
            },
            choice =>
            {
                Assert.Equal("#BFBFBF", Format(choice.Color));
                Assert.Equal(75, choice.HighColorPercent);
            });
    }

    private static string Format(ClassicMacRgbColor color) =>
        $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
}
