using System.Globalization;
using System.Text.RegularExpressions;

namespace ClassicMacIde.Core;

public static class MacBuildRunCommand
{
    public static IReadOnlyList<string> CreatePowerShellArguments(
        MacTargetProfile profile,
        string scriptPath,
        string? sourceDirectory = null,
        string? buildDirectory = null,
        string? appName = null,
        string? testRequestPath = null,
        string? telemetryPath = null,
        MacEmulator emulator = MacEmulator.MiniVmac,
        string? basiliskSerialA = null,
        string? basiliskSerialB = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptPath);
        if (!IsSupportedCombination(profile, emulator))
        {
            throw new NotSupportedException(
                $"Build and run with {emulator} is not configured for target '{profile.Id}'.");
        }
        if (basiliskSerialA is not null || basiliskSerialB is not null)
        {
            if (emulator != MacEmulator.BasiliskII)
            {
                throw new NotSupportedException(
                    "Host serial ports can be configured only for Basilisk II.");
            }

            ValidateBasiliskSerialPort(basiliskSerialA, nameof(basiliskSerialA));
            ValidateBasiliskSerialPort(basiliskSerialB, nameof(basiliskSerialB));
            if (basiliskSerialA is not null &&
                string.Equals(
                    basiliskSerialA,
                    basiliskSerialB,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "Basilisk II serial A and serial B must use different COM ports.");
            }
        }

        var arguments = new List<string>
        {
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            scriptPath,
            "-TargetProfileId",
            profile.Id,
            "-Emulator",
            emulator.ToString(),
            "-GuestScreenWidth",
            profile.ScreenWidth.ToString(CultureInfo.InvariantCulture),
            "-GuestScreenHeight",
            profile.ScreenHeight.ToString(CultureInfo.InvariantCulture),
        };
        if (basiliskSerialA is not null)
        {
            arguments.AddRange(["-BasiliskSerialA", basiliskSerialA]);
        }
        if (basiliskSerialB is not null)
        {
            arguments.AddRange(["-BasiliskSerialB", basiliskSerialB]);
        }
        if (sourceDirectory is not null || buildDirectory is not null ||
            appName is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
            ArgumentException.ThrowIfNullOrWhiteSpace(buildDirectory);
            ArgumentException.ThrowIfNullOrWhiteSpace(appName);
            arguments.AddRange(
            [
                "-SourceDirectory",
                sourceDirectory,
                "-BuildDirectory",
                buildDirectory,
                "-AppName",
                appName,
            ]);
        }
        if (testRequestPath is not null || telemetryPath is not null)
        {
            if (emulator != MacEmulator.MiniVmac)
            {
                throw new NotSupportedException(
                    "Automated guest tests are supported only with Mini vMac.");
            }
            ArgumentException.ThrowIfNullOrWhiteSpace(testRequestPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(telemetryPath);
            if (sourceDirectory is null || buildDirectory is null || appName is null)
            {
                throw new ArgumentException(
                    "Test mailbox paths require an explicit generated application source, build directory, and application name.");
            }
            arguments.AddRange(
            [
                "-TestRequestPath",
                testRequestPath,
                "-TelemetryPath",
                telemetryPath,
            ]);
        }

        return arguments.AsReadOnly();
    }

    private static void ValidateBasiliskSerialPort(
        string? portName,
        string parameterName)
    {
        if (portName is not null &&
            !Regex.IsMatch(
                portName,
                @"\ACOM[1-9][0-9]*\z",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            throw new ArgumentException(
                "Basilisk II serial ports must be Windows COM names such as COM3.",
                parameterName);
        }
    }

    public static bool IsSupportedCombination(
        MacTargetProfile profile,
        MacEmulator emulator)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return emulator switch
        {
            MacEmulator.MiniVmac => profile.Id == "mac-se-68k",
            MacEmulator.BasiliskII => profile.Id is
                "mac-color-classic-68k" or "mac-powerbook-165c-68k",
            _ => false,
        };
    }
}
