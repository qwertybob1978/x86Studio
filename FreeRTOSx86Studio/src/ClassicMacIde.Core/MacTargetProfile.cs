namespace ClassicMacIde.Core;

public enum MacArchitecture
{
    M68k,
    PowerPC,
    Intel8088,
    Intel80386,
}

public enum DisplayColorMode
{
    Monochrome,
    Grayscale,
    Color,
}

public enum TargetVerificationState
{
    Candidate,
    GuestBootVerified,
    ApplicationLaunchVerified,
}

public sealed record MacTargetProfile
{
    public MacTargetProfile(
        string id,
        string displayName,
        MacArchitecture architecture,
        int screenWidth,
        int screenHeight,
        DisplayColorMode colorMode,
        int colorDepthBits,
        TargetVerificationState verificationState,
        string graphicsBackendId = "quickdraw",
        bool isFullScreen = false,
        bool buildSupported = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(graphicsBackendId);
        if (screenWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(screenWidth));
        }

        if (screenHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(screenHeight));
        }

        if (colorDepthBits <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(colorDepthBits));
        }

        if (colorMode == DisplayColorMode.Monochrome && colorDepthBits != 1)
        {
            throw new ArgumentException(
                "Monochrome profiles must use a one-bit display depth.",
                nameof(colorDepthBits));
        }

        if (colorMode != DisplayColorMode.Monochrome && colorDepthBits < 2)
        {
            throw new ArgumentException(
                "Grayscale and color profiles must use at least two bits per pixel.",
                nameof(colorDepthBits));
        }

        Id = id;
        DisplayName = displayName;
        Architecture = architecture;
        ScreenWidth = screenWidth;
        ScreenHeight = screenHeight;
        ColorMode = colorMode;
        ColorDepthBits = colorDepthBits;
        VerificationState = verificationState;
        GraphicsBackendId = graphicsBackendId;
        IsFullScreen = isFullScreen;
        BuildSupported = buildSupported;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public MacArchitecture Architecture { get; }
    public int ScreenWidth { get; }
    public int ScreenHeight { get; }
    public DisplayColorMode ColorMode { get; }
    public int ColorDepthBits { get; }
    public TargetVerificationState VerificationState { get; }
    public string GraphicsBackendId { get; }
    public bool IsFullScreen { get; }
    public bool BuildSupported { get; }

    public bool CanClaimApplicationCompatibility =>
        VerificationState == TargetVerificationState.ApplicationLaunchVerified;
}

public static class FreeRTOSx86TargetProfiles
{
    public static IReadOnlyList<MacTargetProfile> All { get; } =
        Array.AsReadOnly(
        new MacTargetProfile[]
        {
            new(
                "dos-8088-hercules",
                "DOS FreeRTOS — 8088 / Hercules LVGL 1-bpp (720 × 348)",
                MacArchitecture.Intel8088,
                720,
                348,
                DisplayColorMode.Monochrome,
                1,
                TargetVerificationState.Candidate,
                graphicsBackendId: "hercules",
                isFullScreen: true),
            new(
                "dos-80386-vga",
                "DOS FreeRTOS — 80386 / VGA + VESA (planned, 16 colors)",
                MacArchitecture.Intel80386,
                640,
                480,
                DisplayColorMode.Color,
                4,
                TargetVerificationState.Candidate,
                graphicsBackendId: "vga",
                isFullScreen: true,
                buildSupported: false),
            new(
                "dos-8088-cga-320-palette0",
                "DOS FreeRTOS — 8088 / CGA 320 × 200 (Palette 0)",
                MacArchitecture.Intel8088,
                320,
                200,
                DisplayColorMode.Color,
                2,
                TargetVerificationState.Candidate,
                graphicsBackendId: "cga",
                isFullScreen: true,
                buildSupported: false),
            new(
                "dos-8088-cga-320-palette1",
                "DOS FreeRTOS — 8088 / CGA 320 × 200 (Palette 1)",
                MacArchitecture.Intel8088,
                320,
                200,
                DisplayColorMode.Color,
                2,
                TargetVerificationState.Candidate,
                graphicsBackendId: "cga",
                isFullScreen: true,
                buildSupported: false),
            new(
                "dos-8088-cga-640",
                "DOS FreeRTOS — 8088 / CGA 640 × 200 (1-bpp)",
                MacArchitecture.Intel8088,
                640,
                200,
                DisplayColorMode.Monochrome,
                1,
                TargetVerificationState.Candidate,
                graphicsBackendId: "cga",
                isFullScreen: true,
                buildSupported: false),
        });
}

public static class StudioTargetProfiles
{
    public static IReadOnlyList<MacTargetProfile> All { get; } =
        Array.AsReadOnly(
            MacTargetProfiles.All
                .Concat(FreeRTOSx86TargetProfiles.All)
                .ToArray());
}

public static class MacTargetProfiles
{
    public static IReadOnlyList<MacTargetProfile> All { get; } =
        Array.AsReadOnly(
        new MacTargetProfile[]
        {
            new(
                "mac-se-68k",
                "Macintosh SE",
                MacArchitecture.M68k,
                512,
                342,
                DisplayColorMode.Monochrome,
                1,
                TargetVerificationState.GuestBootVerified),
            new(
                "mac-lc-iii-68k",
                "Macintosh LC III (candidate)",
                MacArchitecture.M68k,
                640,
                480,
                DisplayColorMode.Color,
                8,
                TargetVerificationState.Candidate),
            new(
                "power-mac-6100-ppc",
                "Power Macintosh 6100 (candidate)",
                MacArchitecture.PowerPC,
                640,
                480,
                DisplayColorMode.Color,
                8,
                TargetVerificationState.Candidate),
            new(
                "mac-color-classic-68k",
                "Macintosh Color Classic (candidate)",
                MacArchitecture.M68k,
                512,
                384,
                DisplayColorMode.Color,
                8,
                TargetVerificationState.Candidate),
            new(
                "mac-powerbook-165c-68k",
                "PowerBook 165c (candidate)",
                MacArchitecture.M68k,
                640,
                400,
                DisplayColorMode.Color,
                8,
                TargetVerificationState.Candidate),
        });
}
