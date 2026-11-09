namespace ClassicMacIde.Core;

/// <summary>Provides snapping and alignment operations for designer controls.</summary>
public static class DesignerGeometry
{
    /// <summary>Snaps a coordinate to the nearest positive grid interval.</summary>
    public static double Snap(double coordinate, double gridSpacing)
    {
        if (!double.IsFinite(coordinate))
        {
            throw new ArgumentOutOfRangeException(nameof(coordinate));
        }

        if (!double.IsFinite(gridSpacing) || gridSpacing <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gridSpacing));
        }

        return Math.Round(
            coordinate / gridSpacing,
            MidpointRounding.AwayFromZero) * gridSpacing;
    }

    /// <summary>
    /// Aligns controls to the corresponding edge of the first supplied control.
    /// </summary>
    public static IReadOnlyList<DesignerControl> Align(
        IReadOnlyList<DesignerControl> controls,
        DesignerControlAlignment alignment)
    {
        ArgumentNullException.ThrowIfNull(controls);
        if (controls.Count == 0)
        {
            return [];
        }

        var anchor = controls[0];
        var target = alignment switch
        {
            DesignerControlAlignment.Left => anchor.X,
            DesignerControlAlignment.Right => anchor.X + anchor.Width,
            DesignerControlAlignment.Top => anchor.Y,
            DesignerControlAlignment.Bottom => anchor.Y + anchor.Height,
            _ => throw new ArgumentOutOfRangeException(nameof(alignment)),
        };

        return controls.Select(control =>
        {
            var x = alignment switch
            {
                DesignerControlAlignment.Left => target,
                DesignerControlAlignment.Right => target - control.Width,
                _ => control.X,
            };
            var y = alignment switch
            {
                DesignerControlAlignment.Top => target,
                DesignerControlAlignment.Bottom => target - control.Height,
                _ => control.Y,
            };
            return control.WithGeometry(x, y, control.Width, control.Height);
        }).ToArray();
    }
}

/// <summary>Identifies the edge used to align a group of controls.</summary>
public enum DesignerControlAlignment
{
    /// <summary>Aligns the left edges.</summary>
    Left,

    /// <summary>Aligns the right edges.</summary>
    Right,

    /// <summary>Aligns the top edges.</summary>
    Top,

    /// <summary>Aligns the bottom edges.</summary>
    Bottom,
}
