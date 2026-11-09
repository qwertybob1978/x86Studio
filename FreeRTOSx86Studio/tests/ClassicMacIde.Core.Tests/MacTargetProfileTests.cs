using ClassicMacIde.Core;

namespace ClassicMacIde.Core.Tests;

public class MacTargetProfileTests
{
    [Fact]
    public void KnownProfilesHaveUniqueIdsAndValidDimensions()
    {
        Assert.Equal(
            MacTargetProfiles.All.Count,
            MacTargetProfiles.All.Select(profile => profile.Id).Distinct().Count());
        Assert.All(
            MacTargetProfiles.All,
            profile =>
            {
                Assert.True(profile.ScreenWidth > 0);
                Assert.True(profile.ScreenHeight > 0);
            });
    }

    [Fact]
    public void MacSeProfileMatchesObservedMonochromeGuest()
    {
        var profile = Assert.Single(
            MacTargetProfiles.All,
            profile => profile.Id == "mac-se-68k");

        Assert.Equal(512, profile.ScreenWidth);
        Assert.Equal(342, profile.ScreenHeight);
        Assert.Equal(DisplayColorMode.Monochrome, profile.ColorMode);
        Assert.Equal(1, profile.ColorDepthBits);
        Assert.Equal(
            TargetVerificationState.GuestBootVerified,
            profile.VerificationState);
        Assert.False(profile.CanClaimApplicationCompatibility);
    }

    [Fact]
    public void ColorClassicAndPowerBookProfilesUseTheirNativeColorDisplays()
    {
        var colorClassic = Assert.Single(
            MacTargetProfiles.All,
            profile => profile.Id == "mac-color-classic-68k");
        var powerBook = Assert.Single(
            MacTargetProfiles.All,
            profile => profile.Id == "mac-powerbook-165c-68k");

        Assert.Equal((512, 384, DisplayColorMode.Color, 8),
            (colorClassic.ScreenWidth, colorClassic.ScreenHeight,
                colorClassic.ColorMode, colorClassic.ColorDepthBits));
        Assert.Equal((640, 400, DisplayColorMode.Color, 8),
            (powerBook.ScreenWidth, powerBook.ScreenHeight,
                powerBook.ColorMode, powerBook.ColorDepthBits));
        Assert.Equal(TargetVerificationState.Candidate, colorClassic.VerificationState);
        Assert.Equal(TargetVerificationState.Candidate, powerBook.VerificationState);
        Assert.False(colorClassic.CanClaimApplicationCompatibility);
        Assert.False(powerBook.CanClaimApplicationCompatibility);
    }

    [Fact]
    public void CandidateProfilesCannotClaimApplicationCompatibility()
    {
        Assert.All(
            MacTargetProfiles.All.Where(profile =>
                profile.VerificationState !=
                    TargetVerificationState.ApplicationLaunchVerified),
            profile => Assert.False(profile.CanClaimApplicationCompatibility));
    }

    [Fact]
    public void MonochromeProfileRejectsNonOneBitDepth()
    {
        Assert.Throws<ArgumentException>(() => new MacTargetProfile(
            "invalid",
            "Invalid monochrome",
            MacArchitecture.M68k,
            512,
            342,
            DisplayColorMode.Monochrome,
            8,
            TargetVerificationState.Candidate));
    }

    [Fact]
    public void BuildAndRunCommandTargetsTheMacSeScriptWithoutStringQuoting()
    {
        var profile = Assert.Single(
            MacTargetProfiles.All,
            profile => profile.Id == "mac-se-68k");
        var arguments = MacBuildRunCommand.CreatePowerShellArguments(
            profile,
            @"C:\Program Files\ClassicMacIde\build-and-run-mac-se.ps1");

        Assert.Equal(
            [
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                @"C:\Program Files\ClassicMacIde\build-and-run-mac-se.ps1",
                "-TargetProfileId",
                "mac-se-68k",
                "-Emulator",
                "MiniVmac",
                "-GuestScreenWidth",
                "512",
                "-GuestScreenHeight",
                "342",
            ],
            arguments);
    }

    [Fact]
    public void BuildAndRunCommandRejectsUnconfiguredTargets()
    {
        var profile = Assert.Single(
            MacTargetProfiles.All,
            profile => profile.Id == "mac-lc-iii-68k");

        Assert.Throws<NotSupportedException>(() =>
            MacBuildRunCommand.CreatePowerShellArguments(
                profile,
                @"C:\workspace\build-and-run-mac-se.ps1"));
    }

    [Theory]
    [InlineData("mac-color-classic-68k")]
    [InlineData("mac-powerbook-165c-68k")]
    public void BuildAndRunCommandAllowsColorTargetsOnlyWithBasiliskII(
        string profileId)
    {
        var profile = Assert.Single(
            MacTargetProfiles.All,
            profile => profile.Id == profileId);

        Assert.Throws<NotSupportedException>(() =>
            MacBuildRunCommand.CreatePowerShellArguments(
                profile,
                @"C:\workspace\build-and-run-mac-color.ps1"));

        var arguments = MacBuildRunCommand.CreatePowerShellArguments(
            profile,
            @"C:\workspace\build-and-run-mac-color.ps1",
            emulator: MacEmulator.BasiliskII);
        Assert.Contains("BasiliskII", arguments);
        Assert.Contains(profileId, arguments);
    }

    [Fact]
    public void BuildAndRunCommandPassesBasiliskSerialEndpointsSeparately()
    {
        var profile = Assert.Single(
            MacTargetProfiles.All,
            profile => profile.Id == "mac-color-classic-68k");

        var arguments = MacBuildRunCommand.CreatePowerShellArguments(
            profile,
            @"C:\workspace\build-and-run-mac.ps1",
            emulator: MacEmulator.BasiliskII,
            basiliskSerialA: "COM7",
            basiliskSerialB: "COM8");

        Assert.Equal(
            ["-BasiliskSerialA", "COM7", "-BasiliskSerialB", "COM8"],
            arguments.Skip(13).Take(4));
    }

    [Theory]
    [InlineData("COM7", "COM7")]
    [InlineData("COM0", null)]
    [InlineData("7", null)]
    public void BuildAndRunCommandRejectsInvalidBasiliskSerialEndpoints(
        string? serialA,
        string? serialB)
    {
        var profile = Assert.Single(
            MacTargetProfiles.All,
            profile => profile.Id == "mac-color-classic-68k");

        Assert.Throws<ArgumentException>(() =>
            MacBuildRunCommand.CreatePowerShellArguments(
                profile,
                @"C:\workspace\build-and-run-mac.ps1",
                emulator: MacEmulator.BasiliskII,
                basiliskSerialA: serialA,
                basiliskSerialB: serialB));
    }

    [Fact]
    public void BuildAndRunCommandRejectsSerialEndpointsForMiniVmac()
    {
        var profile = Assert.Single(
            MacTargetProfiles.All,
            profile => profile.Id == "mac-se-68k");

        Assert.Throws<NotSupportedException>(() =>
            MacBuildRunCommand.CreatePowerShellArguments(
                profile,
                @"C:\workspace\build-and-run-mac.ps1",
                basiliskSerialA: "COM7"));
    }

    [Fact]
    public void BuildAndRunCommandRejectsBasiliskForUnconfiguredTargets()
    {
        var profile = Assert.Single(
            MacTargetProfiles.All,
            profile => profile.Id == "mac-lc-iii-68k");

        Assert.Throws<NotSupportedException>(() =>
            MacBuildRunCommand.CreatePowerShellArguments(
                profile,
                @"C:\workspace\build-and-run-mac-color.ps1",
                emulator: MacEmulator.BasiliskII));
    }

    [Fact]
    public void BuildAndRunCommandPassesGeneratedFormPathsAsSeparateArguments()
    {
        var profile = Assert.Single(
            MacTargetProfiles.All,
            profile => profile.Id == "mac-se-68k");
        var arguments = MacBuildRunCommand.CreatePowerShellArguments(
            profile,
            @"C:\workspace\build-and-run-mac-se.ps1",
            @"C:\workspace with spaces\.local-tools\designer-form\source",
            @"C:\workspace with spaces\.local-tools\designer-form\build",
            "DesignerForm");

        Assert.Equal(
            new[]
            {
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                @"C:\workspace\build-and-run-mac-se.ps1",
                "-TargetProfileId",
                "mac-se-68k",
                "-Emulator",
                "MiniVmac",
                "-GuestScreenWidth",
                "512",
                "-GuestScreenHeight",
                "342",
                "-SourceDirectory",
                @"C:\workspace with spaces\.local-tools\designer-form\source",
                "-BuildDirectory",
                @"C:\workspace with spaces\.local-tools\designer-form\build",
                "-AppName",
                "DesignerForm",
            },
            arguments);
    }

    [Fact]
    public void BuildAndTestCommandPassesMailboxPathsAsSeparateArguments()
    {
        var profile = Assert.Single(
            MacTargetProfiles.All,
            profile => profile.Id == "mac-se-68k");
        var arguments = MacBuildRunCommand.CreatePowerShellArguments(
            profile,
            @"C:\workspace\build-and-run-mac-se.ps1",
            @"C:\workspace\source",
            @"C:\workspace\build",
            "DesignerForm",
            @"C:\workspace\bridge\request.txt",
            @"C:\workspace\bridge\telemetry.txt");

        Assert.Equal(
            [
                "-TestRequestPath",
                @"C:\workspace\bridge\request.txt",
                "-TelemetryPath",
                @"C:\workspace\bridge\telemetry.txt",
            ],
            arguments.Skip(arguments.Count - 4));
        Assert.Contains("-GuestScreenWidth", arguments);
        Assert.Contains("512", arguments);
        Assert.Contains("-GuestScreenHeight", arguments);
        Assert.Contains("342", arguments);
    }

    [Fact]
    public void BuildAndTestCommandRequiresBothMailboxPaths()
    {
        var profile = Assert.Single(
            MacTargetProfiles.All,
            profile => profile.Id == "mac-se-68k");

        Assert.Throws<ArgumentNullException>(() =>
            MacBuildRunCommand.CreatePowerShellArguments(
                profile,
                @"C:\workspace\build-and-run-mac-se.ps1",
                @"C:\workspace\source",
                @"C:\workspace\build",
                "DesignerForm",
                @"C:\workspace\bridge\request.txt"));
    }

    [Fact]
    public void BuildAndTestCommandRejectsBasiliskIITargets()
    {
        var profile = Assert.Single(
            MacTargetProfiles.All,
            profile => profile.Id == "mac-powerbook-165c-68k");

        Assert.Throws<NotSupportedException>(() =>
            MacBuildRunCommand.CreatePowerShellArguments(
                profile,
                @"C:\workspace\build-and-run-mac-color.ps1",
                @"C:\workspace\source",
                @"C:\workspace\build",
                "DesignerForm",
                @"C:\workspace\bridge\request.txt",
                @"C:\workspace\bridge\telemetry.txt",
                MacEmulator.BasiliskII));
    }
}
