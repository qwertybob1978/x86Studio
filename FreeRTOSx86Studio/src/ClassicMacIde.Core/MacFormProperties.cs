namespace ClassicMacIde.Core;

/// <summary>Classic Macintosh window attributes configured in the form designer.</summary>
public sealed record MacFormProperties
{
    /// <summary>Gets whether the form has a title bar.</summary>
    public bool ShowTitleBar { get; init; } = true;

    /// <summary>Gets whether the title bar has a grow box.</summary>
    public bool Resizable { get; init; }

    /// <summary>Gets whether the title bar has a close box.</summary>
    public bool HasCloseBox { get; init; }

    /// <summary>Gets whether the title bar has a zoom box.</summary>
    public bool HasZoomBox { get; init; }

    /// <summary>Gets whether the form is visible when the application starts.</summary>
    public bool Visible { get; init; } = true;

    /// <summary>Gets whether the form is centered on the main screen.</summary>
    public bool CenterOnScreen { get; init; } = true;

    /// <summary>Gets whether the form can be moved by dragging its title bar.</summary>
    public bool Movable { get; init; }

    /// <summary>Gets the initial left edge when the form is not centered.</summary>
    public int Left { get; init; } = 24;

    /// <summary>Gets the initial top edge when the form is not centered.</summary>
    public int Top { get; init; } = 32;

    /// <summary>Gets the optional client width; null uses the target's default.</summary>
    public int? Width { get; init; }

    /// <summary>Gets the optional client height; null uses the target's default.</summary>
    public int? Height { get; init; }

    /// <summary>Gets the client width for a target profile.</summary>
    public int GetClientWidth(MacTargetProfile profile) =>
        Width ?? (profile.IsFullScreen ? profile.ScreenWidth : profile.ScreenWidth - 50);

    /// <summary>Gets the client height for a target profile.</summary>
    public int GetClientHeight(MacTargetProfile profile) =>
        Height ?? (profile.IsFullScreen ? profile.ScreenHeight : profile.ScreenHeight - 80);

    /// <summary>Validates the form's bounds and supported window-attribute combinations.</summary>
    public void ValidateFor(MacTargetProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var clientWidth = GetClientWidth(profile);
        var clientHeight = GetClientHeight(profile);
        var maximumWidth = profile.ScreenWidth - (profile.IsFullScreen ? 0 : 50);
        var maximumHeight = profile.ScreenHeight - (profile.IsFullScreen ? 0 : 80);
        if (clientWidth is < 100 || clientWidth > maximumWidth)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Width),
                $"Form client width must be from 100 to {maximumWidth} pixels.");
        }

        if (clientHeight is < 60 || clientHeight > maximumHeight)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Height),
                $"Form client height must be from 60 to {maximumHeight} pixels.");
        }

        if (profile.IsFullScreen)
        {
            if (ShowTitleBar || Resizable || HasCloseBox || HasZoomBox || Movable)
            {
                throw new ArgumentException(
                    "Full-screen targets do not support window decorations.");
            }
            return;
        }

        if (!ShowTitleBar && (Resizable || HasCloseBox || HasZoomBox || Movable))
        {
            throw new ArgumentException(
                "Resizable, movable, close-box, and zoom-box options require a title bar.");
        }

        if (!CenterOnScreen &&
            (Left < 0 ||
             Top < 0 ||
             (long)Left + clientWidth + 2 > profile.ScreenWidth ||
             (long)Top + clientHeight + (ShowTitleBar ? 22 : 2) > profile.ScreenHeight))
        {
            throw new ArgumentOutOfRangeException(
                nameof(Left),
                "The form's initial position must keep the entire window on the target screen.");
        }
    }
}
