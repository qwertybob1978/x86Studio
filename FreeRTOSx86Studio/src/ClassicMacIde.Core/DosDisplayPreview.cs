namespace ClassicMacIde.Core;

public readonly record struct DosDisplayPreviewMetrics(
    double Width,
    double Height,
    double ScaleX,
    double ScaleY)
{
    public const double PhysicalAspectRatio = 4d / 3d;
    public const double DefaultWidth = 960d;

    public static DosDisplayPreviewMetrics Create(
        MacTargetProfile profile,
        double previewWidth = DefaultWidth)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!double.IsFinite(previewWidth) || previewWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(previewWidth));
        }

        var previewHeight = previewWidth / PhysicalAspectRatio;
        return new DosDisplayPreviewMetrics(
            previewWidth,
            previewHeight,
            previewWidth / profile.ScreenWidth,
            previewHeight / profile.ScreenHeight);
    }
}

public sealed record DosDitheredColorChoice(
    ClassicMacRgbColor Color,
    ClassicMacRgbColor LowColor,
    ClassicMacRgbColor HighColor,
    int HighColorPercent);

public static class DosDisplayDither
{
    private static readonly int[] Bayer8Ranks = CreateBayer8Ranks();
    private static readonly int[] RepresentativeLevels = [16, 32, 48];

    public static IReadOnlyList<ClassicMacRgbColor> CreateOrdered8x8Pattern(
        ClassicMacRgbColor target,
        IReadOnlyList<ClassicMacRgbColor> palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        if (palette.Count == 0)
        {
            throw new ArgumentException(
                "The display palette must contain at least one color.",
                nameof(palette));
        }

        var bestError = long.MaxValue;
        var bestLow = palette[0];
        var bestHigh = palette[0];
        var bestCoverage = 0;
        for (var lowIndex = 0; lowIndex < palette.Count; lowIndex++)
        {
            for (var highIndex = lowIndex; highIndex < palette.Count; highIndex++)
            {
                for (var coverage = 0; coverage <= Bayer8Ranks.Length; coverage++)
                {
                    var average = Blend(
                        palette[lowIndex],
                        palette[highIndex],
                        coverage,
                        Bayer8Ranks.Length);
                    var error = ColorError(target, average);
                    if (error >= bestError)
                    {
                        continue;
                    }

                    bestError = error;
                    bestLow = palette[lowIndex];
                    bestHigh = palette[highIndex];
                    bestCoverage = coverage;
                }
            }
        }

        var pattern = new ClassicMacRgbColor[Bayer8Ranks.Length];
        for (var index = 0; index < Bayer8Ranks.Length; index++)
        {
            pattern[index] = Bayer8Ranks[index] < bestCoverage
                ? bestHigh
                : bestLow;
        }
        return Array.AsReadOnly(pattern);
    }

    public static IReadOnlyList<DosDitheredColorChoice> GetRepresentativeChoices(
        IReadOnlyList<ClassicMacRgbColor> palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        if (palette.Count < 2)
        {
            throw new ArgumentException(
                "At least two palette colors are required for dithering.",
                nameof(palette));
        }

        var hardwareColors = palette.ToHashSet();
        var result = new List<DosDitheredColorChoice>();
        var seenColors = new HashSet<ClassicMacRgbColor>(hardwareColors);
        for (var lowIndex = 0; lowIndex < palette.Count; lowIndex++)
        {
            for (var highIndex = lowIndex + 1; highIndex < palette.Count; highIndex++)
            {
                foreach (var coverage in RepresentativeLevels)
                {
                    var color = Blend(
                        palette[lowIndex],
                        palette[highIndex],
                        coverage,
                        Bayer8Ranks.Length);
                    if (!seenColors.Add(color))
                    {
                        continue;
                    }

                    result.Add(new DosDitheredColorChoice(
                        color,
                        palette[lowIndex],
                        palette[highIndex],
                        coverage * 100 / Bayer8Ranks.Length));
                }
            }
        }
        return Array.AsReadOnly(result.ToArray());
    }

    private static ClassicMacRgbColor Blend(
        ClassicMacRgbColor low,
        ClassicMacRgbColor high,
        int highCount,
        int totalCount) =>
        new(
            BlendChannel(low.Red, high.Red, highCount, totalCount),
            BlendChannel(low.Green, high.Green, highCount, totalCount),
            BlendChannel(low.Blue, high.Blue, highCount, totalCount));

    private static byte BlendChannel(
        byte low,
        byte high,
        int highCount,
        int totalCount) =>
        (byte)((low * (totalCount - highCount) + high * highCount + totalCount / 2) /
               totalCount);

    private static long ColorError(
        ClassicMacRgbColor first,
        ClassicMacRgbColor second)
    {
        var red = first.Red - second.Red;
        var green = first.Green - second.Green;
        var blue = first.Blue - second.Blue;
        return (long)red * red + (long)green * green + (long)blue * blue;
    }

    private static int[] CreateBayer8Ranks()
    {
        var ranks = new[] { 0 };
        for (var size = 1; size < 8; size *= 2)
        {
            var nextSize = size * 2;
            var nextRanks = new int[nextSize * nextSize];
            for (var y = 0; y < nextSize; y++)
            {
                for (var x = 0; x < nextSize; x++)
                {
                    var quadrant = (y < size, x < size) switch
                    {
                        (true, true) => 0,
                        (true, false) => 2,
                        (false, true) => 3,
                        _ => 1,
                    };
                    nextRanks[(y * nextSize) + x] =
                        (ranks[((y % size) * size) + (x % size)] * 4) + quadrant;
                }
            }
            ranks = nextRanks;
        }
        return ranks;
    }
}
