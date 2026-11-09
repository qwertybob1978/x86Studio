[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$DiskImagePath,

    [Parameter(Mandatory = $true)]
    [string]$RomImagePath,

    [string]$AppDiskImagePath,

    [string]$AppName = "Application",

    [string]$WslDistribution = "Ubuntu",

    [string]$HfsBridgeScriptPath,

    [string]$StartupItemsPath = ":OS 7.5.5 (minimal):Startup Items",

    [switch]$AutoStartApplication,

    [string]$StartupRequestPath,

    [ValidateSet(
        "300", "600", "1200", "1800", "2400", "3600", "4800",
        "7200", "9600", "14400", "19200", "28800", "38400", "57600"
    )]
    [string]$AgentBaudRate = "9600",

    [string]$StartupTelemetryPath,

    [string]$GuestScreenshotPath,

    [int]$GuestScreenWidth = 512,

    [int]$GuestScreenHeight = 342,

    [ValidateRange(1, 600)]
    [int]$CompletionTimeoutSeconds = 120,

    [string]$ResultDiskImagePath,

    [string]$ResultSystemDiskImagePath,

    [string]$EmulatorPath,

    [ValidateSet("MiniVmac", "BasiliskII")]
    [string]$Emulator = "MiniVmac",

    [ValidateSet(
        "mac-se-68k",
        "mac-color-classic-68k",
        "mac-powerbook-165c-68k"
    )]
    [string]$TargetProfileId = "mac-se-68k",

    [string]$BasiliskSerialA,

    [string]$BasiliskSerialB,

    [ValidateSet(1, 8)]
    [int]$GuestScreenDepthBits = 1
)

$ErrorActionPreference = "Stop"
$profileGeometry = switch ($TargetProfileId) {
    "mac-se-68k" { @(512, 342, 1) }
    "mac-color-classic-68k" { @(512, 384, 8) }
    "mac-powerbook-165c-68k" { @(640, 400, 8) }
}
if ($GuestScreenWidth -ne $profileGeometry[0] -or
    $GuestScreenHeight -ne $profileGeometry[1] -or
    $GuestScreenDepthBits -ne $profileGeometry[2]) {
    throw "Guest display settings do not match target '$TargetProfileId'."
}
if ($Emulator -eq "MiniVmac" -and $TargetProfileId -ne "mac-se-68k") {
    throw "Mini vMac is configured only for the Macintosh SE target."
}
if ($Emulator -eq "BasiliskII" -and
    $TargetProfileId -notin @("mac-color-classic-68k", "mac-powerbook-165c-68k")) {
    throw "Basilisk II is configured only for the Color Classic and PowerBook 165c targets."
}
if ($Emulator -ne "BasiliskII" -and ($BasiliskSerialA -or $BasiliskSerialB)) {
    throw "Basilisk serial COM ports can be configured only when using Basilisk II."
}
foreach ($serialPort in @($BasiliskSerialA, $BasiliskSerialB)) {
    if ($serialPort -and $serialPort -notmatch '^COM[1-9][0-9]*$') {
        throw "Basilisk serial endpoints must be Windows COM names such as COM3."
    }
}
if ($BasiliskSerialA -and $BasiliskSerialB -and
    $BasiliskSerialA -ieq $BasiliskSerialB) {
    throw "Basilisk serial A and serial B must use different COM ports."
}
if ($StartupTelemetryPath -and $Emulator -ne "MiniVmac") {
    throw "Automated guest tests are supported only with Mini vMac."
}
if ([string]::IsNullOrWhiteSpace($EmulatorPath)) {
    $workspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
    $EmulatorPath = if ($Emulator -eq "MiniVmac") {
        Join-Path $workspaceRoot ".local-tools/emulators/MiniVmac-SE/Mini vMac.exe"
    }
    else {
        Join-Path $workspaceRoot ".local-tools/emulators/BasiliskII/BasiliskII.exe"
    }
}
if ($ResultDiskImagePath -and -not $AppDiskImagePath) {
    throw "A result disk path requires an application disk image."
}
if ($AutoStartApplication -and -not $AppDiskImagePath) {
    throw "AutoStartApplication requires an application disk image."
}
if ($AutoStartApplication -and [string]::IsNullOrWhiteSpace($HfsBridgeScriptPath)) {
    throw "AutoStartApplication requires the HFS bridge script."
}
if ($StartupRequestPath -and -not $AutoStartApplication) {
    throw "A startup request requires AutoStartApplication."
}
if ($PSBoundParameters.ContainsKey("AgentBaudRate") -and -not $AutoStartApplication) {
    throw "An Agent baud rate requires AutoStartApplication."
}
if ($PSBoundParameters.ContainsKey("AgentBaudRate") -and $AppName -ne "MacStudioAgent") {
    throw "An Agent baud rate can be configured only for the MacStudioAgent application."
}
if ($StartupTelemetryPath -and -not $AutoStartApplication) {
    throw "Startup telemetry requires AutoStartApplication."
}
if ($StartupTelemetryPath -and
    ($GuestScreenWidth -lt 1 -or $GuestScreenHeight -lt 1)) {
    throw "Guest screen dimensions must be positive for test completion detection."
}
if ($GuestScreenshotPath -and -not $StartupTelemetryPath) {
    throw "Guest screenshots require a startup telemetry path."
}
if ($GuestScreenshotPath -and (Test-Path -LiteralPath $GuestScreenshotPath)) {
    throw "Guest screenshot output already exists: $GuestScreenshotPath"
}

Add-Type -TypeDefinition @'
using System;
using System.IO;

public static class ClassicMacSparseDiskCopier
{
    public static void Copy(Stream source, FileStream destination, long length)
    {
        var buffer = new byte[16 * 1024 * 1024];
        long offset = 0;
        while (offset < length)
        {
            int requested = (int)Math.Min(buffer.Length, length - offset);
            int read = source.Read(buffer, 0, requested);
            if (read == 0)
                throw new EndOfStreamException("Unexpected end of HFS partition.");

            bool containsData = false;
            for (int index = 0; index < read; index++)
            {
                if (buffer[index] != 0)
                {
                    containsData = true;
                    break;
                }
            }

            if (containsData)
            {
                destination.Position = offset;
                destination.Write(buffer, 0, read);
            }
            offset += read;
        }

        destination.SetLength(length);
        destination.Flush(true);
    }
}
'@

if (-not ("ClassicMacCompletionMarkerProbe" -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

public static class ClassicMacCompletionMarkerProbe
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(IntPtr deviceContext, int x, int y);

    public static bool IsVisible(
        int processId,
        int screenWidth,
        int screenHeight,
        int markerLeft,
        int markerTop,
        int moduleSize,
        string pattern)
    {
        if (screenWidth < 1 || screenHeight < 1 || markerLeft < 0 ||
            markerTop < 0 || moduleSize < 1 || pattern == null ||
            pattern.Length != 49)
            throw new ArgumentException("Invalid guest completion marker geometry.");

        using (var process = Process.GetProcessById(processId))
        {
            process.Refresh();
            if (process.HasExited || process.MainWindowHandle == IntPtr.Zero)
                return false;

            Rect client;
            if (!GetClientRect(process.MainWindowHandle, out client))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            int width = client.Right - client.Left;
            int height = client.Bottom - client.Top;
            if (width < screenWidth || height < screenHeight)
                return false;

            IntPtr deviceContext = GetDC(process.MainWindowHandle);
            if (deviceContext == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            try
            {
                for (int index = 0; index < pattern.Length; index++)
                {
                    int column = index % 7;
                    int row = index / 7;
                    int x = (int)Math.Round(
                        (markerLeft + column * moduleSize + (moduleSize - 1) / 2.0) *
                        width / screenWidth,
                        MidpointRounding.AwayFromZero);
                    int y = (int)Math.Round(
                        (markerTop + row * moduleSize + (moduleSize - 1) / 2.0) *
                        height / screenHeight,
                        MidpointRounding.AwayFromZero);
                    uint color = GetPixel(deviceContext, x, y);
                    if (color == 0xffffffff)
                        return false;

                    int red = (int)(color & 0xff);
                    int green = (int)((color >> 8) & 0xff);
                    int blue = (int)((color >> 16) & 0xff);
                    bool isBlack = (red + green + blue) / 3 < 128;
                    if (isBlack != (pattern[index] == '1'))
                        return false;
                }
                return true;
            }
            finally
            {
                ReleaseDC(process.MainWindowHandle, deviceContext);
            }
        }
    }

    public static void SaveScreen(
        int processId,
        int screenWidth,
        int screenHeight,
        string path)
    {
        if (screenWidth < 1 || screenHeight < 1 || String.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Invalid guest screenshot parameters.");

        using (var process = Process.GetProcessById(processId))
        {
            process.Refresh();
            if (process.HasExited || process.MainWindowHandle == IntPtr.Zero)
                throw new InvalidOperationException("Mini vMac exited before its screen could be captured.");

            Rect client;
            if (!GetClientRect(process.MainWindowHandle, out client))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            int width = client.Right - client.Left;
            int height = client.Bottom - client.Top;
            if (width < screenWidth || height < screenHeight)
                throw new InvalidOperationException("Mini vMac's client area is smaller than the guest screen.");

            IntPtr deviceContext = GetDC(process.MainWindowHandle);
            if (deviceContext == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            try
            {
                int rowSize = (screenWidth * 3 + 3) & ~3;
                int imageSize = rowSize * screenHeight;
                var image = new byte[imageSize];
                for (int y = screenHeight - 1; y >= 0; y--)
                {
                    int rowOffset = (screenHeight - 1 - y) * rowSize;
                    int screenY = Math.Min(
                        height - 1,
                        (int)Math.Floor((y + 0.5) * height / screenHeight));
                    for (int x = 0; x < screenWidth; x++)
                    {
                        int screenX = Math.Min(
                            width - 1,
                            (int)Math.Floor((x + 0.5) * width / screenWidth));
                        uint color = GetPixel(deviceContext, screenX, screenY);
                        if (color == 0xffffffff)
                            throw new InvalidOperationException("Could not read a guest screen pixel.");
                        int offset = rowOffset + x * 3;
                        image[offset] = (byte)((color >> 16) & 0xff);
                        image[offset + 1] = (byte)((color >> 8) & 0xff);
                        image[offset + 2] = (byte)(color & 0xff);
                    }
                }

                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write((ushort)0x4d42);
                    writer.Write(54 + imageSize);
                    writer.Write((short)0);
                    writer.Write((short)0);
                    writer.Write(54);
                    writer.Write(40);
                    writer.Write(screenWidth);
                    writer.Write(screenHeight);
                    writer.Write((short)1);
                    writer.Write((short)24);
                    writer.Write(0);
                    writer.Write(imageSize);
                    writer.Write(2835);
                    writer.Write(2835);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(image);
                }
            }
            finally
            {
                ReleaseDC(process.MainWindowHandle, deviceContext);
            }
        }
    }
}
'@
}

function Read-BigEndianInt32([byte[]]$Bytes, [int]$Offset) {
    return [System.Net.IPAddress]::NetworkToHostOrder(
        [BitConverter]::ToInt32($Bytes, $Offset)
    )
}

function Read-BytesAt(
    [System.IO.FileStream]$Stream,
    [long]$Offset,
    [int]$Count
) {
    [void]$Stream.Seek($Offset, [System.IO.SeekOrigin]::Begin)
    $buffer = New-Object byte[] $Count
    $read = $Stream.Read($buffer, 0, $Count)
    if ($read -ne $Count) {
        throw "Unexpected end of disk image at byte offset $Offset."
    }
    return ,$buffer
}

function Resolve-FilePath([string]$Path) {
    $item = Get-Item -LiteralPath $Path -ErrorAction Stop
    if ($item.PSIsContainer) {
        throw "Expected a file path, found a directory: $Path."
    }
    return $item.FullName
}

$disk = Resolve-FilePath $DiskImagePath
$rom = Resolve-FilePath $RomImagePath
$emulatorExecutable = Resolve-FilePath $EmulatorPath
$temporaryDirectory = Join-Path $env:TEMP (
    "macstudio-se-boot-" + [Guid]::NewGuid().ToString("N")
)
$emulatorProcess = $null

try {
    New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null
    $diskStream = [System.IO.File]::OpenRead($disk)
    try {
        $descriptor = Read-BytesAt $diskStream 0 512
        if ([Text.Encoding]::ASCII.GetString($descriptor, 0, 2) -ne "ER") {
            throw "Disk image does not begin with an Apple Driver Descriptor Map."
        }
        $blockSize = [System.Net.IPAddress]::NetworkToHostOrder(
            [BitConverter]::ToInt16($descriptor, 2)
        )
        if ($blockSize -lt 512 -or $blockSize -gt 4096) {
            throw "Unsupported Apple partition-map block size: $blockSize."
        }

        $firstEntry = Read-BytesAt $diskStream $blockSize 512
        if ([Text.Encoding]::ASCII.GetString($firstEntry, 0, 2) -ne "PM") {
            throw "Disk image does not contain an Apple Partition Map."
        }
        $partitionCount = Read-BigEndianInt32 $firstEntry 4
        if ($partitionCount -lt 1 -or $partitionCount -gt 4096) {
            throw "Invalid Apple Partition Map entry count: $partitionCount."
        }

        $hfsPartitions = @()
        for ($index = 0; $index -lt $partitionCount; $index += 1) {
            $entry = if ($index -eq 0) {
                $firstEntry
            }
            else {
                Read-BytesAt $diskStream (($index + 1) * $blockSize) 512
            }
            if ([Text.Encoding]::ASCII.GetString($entry, 0, 2) -ne "PM") {
                throw "Invalid Apple Partition Map entry at index $index."
            }
            $type = [Text.Encoding]::ASCII.GetString($entry, 48, 32).Trim(
                [char[]]@([char]0, [char]32)
            )
            if ($type -eq "Apple_HFS") {
                $startBlock = Read-BigEndianInt32 $entry 8
                $blockCount = Read-BigEndianInt32 $entry 12
                if ($startBlock -lt 0 -or $blockCount -lt 1) {
                    throw "Invalid HFS partition geometry in map entry $index."
                }
                $hfsPartitions += [pscustomobject]@{
                    StartBlock = $startBlock
                    BlockCount = $blockCount
                }
            }
        }

        if ($hfsPartitions.Count -ne 1) {
            throw "Expected exactly one Apple_HFS partition; found $($hfsPartitions.Count)."
        }
        $partition = $hfsPartitions[0]
        $partitionOffset = [long]$partition.StartBlock * $blockSize
        $partitionLength = [long]$partition.BlockCount * $blockSize
        if (($partitionOffset + $partitionLength) -gt $diskStream.Length) {
            throw "HFS partition extends beyond the source disk image."
        }
        if ($partitionLength -ge 2147483648) {
            throw "Mini vMac requires an HFS volume smaller than 2 GiB."
        }
        $masterDirectoryBlock = Read-BytesAt $diskStream ($partitionOffset + 1024) 2
        if ([Text.Encoding]::ASCII.GetString($masterDirectoryBlock) -ne "BD") {
            throw "Apple_HFS partition does not contain a valid HFS volume signature."
        }

        $volumePath = Join-Path $temporaryDirectory "disk1.dsk"
        New-Item -ItemType File -Path $volumePath | Out-Null
        & fsutil.exe sparse setflag $volumePath | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Could not mark temporary boot volume as sparse: $volumePath"
        }
        $outputStream = [System.IO.File]::Open(
            $volumePath,
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::Write,
            [System.IO.FileShare]::None
        )
        try {
            [void]$diskStream.Seek($partitionOffset, [System.IO.SeekOrigin]::Begin)
            [ClassicMacSparseDiskCopier]::Copy(
                $diskStream,
                $outputStream,
                $partitionLength
            )
        }
        finally {
            $outputStream.Dispose()
        }
    }
    finally {
        $diskStream.Dispose()
    }

    if ((Get-Item -LiteralPath $volumePath).Length -ne $partitionLength) {
        throw "Temporary HFS volume length did not match the partition map."
    }

    if ($AppDiskImagePath) {
        $appDisk = Resolve-FilePath $AppDiskImagePath
        $appDiskStream = [System.IO.File]::OpenRead($appDisk)
        try {
            if ($appDiskStream.Length -ge 2147483648) {
                throw "The application disk must be smaller than 2 GiB."
            }
            $appVolumeSignature = Read-BytesAt $appDiskStream 1024 2
            if ([Text.Encoding]::ASCII.GetString($appVolumeSignature) -ne "BD") {
                throw "The application disk is not an HFS volume: $appDisk."
            }
        }
        finally {
            $appDiskStream.Dispose()
        }
        if ($AutoStartApplication) {
            $bridgeScript = Resolve-FilePath $HfsBridgeScriptPath
            if ($StartupRequestPath -and -not (Test-Path -LiteralPath $StartupRequestPath -PathType Leaf)) {
                throw "Startup test request file was not found: $StartupRequestPath"
            }
            $wslVolumePath = & wsl.exe `
                --distribution $WslDistribution `
                --exec wslpath -u $volumePath
            if ($LASTEXITCODE -ne 0 -or -not $wslVolumePath) {
                throw "Could not resolve the disposable System 7 volume in WSL."
            }
            $wslAppDiskPath = & wsl.exe `
                --distribution $WslDistribution `
                --exec wslpath -u $appDisk
            if ($LASTEXITCODE -ne 0 -or -not $wslAppDiskPath) {
                throw "Could not resolve the application disk in WSL."
            }
            $wslBridgeScript = & wsl.exe `
                --distribution $WslDistribution `
                --exec wslpath -u $bridgeScript
            if ($LASTEXITCODE -ne 0 -or -not $wslBridgeScript) {
                throw "Could not resolve the HFS bridge script in WSL."
            }
            $startupArguments = @(
                "install-startup-app",
                ([string]$wslVolumePath).Trim(),
                ([string]$wslAppDiskPath).Trim(),
                $AppName,
                $StartupItemsPath
            )
            if ($StartupRequestPath) {
                $wslRequestPath = & wsl.exe `
                    --distribution $WslDistribution `
                    --exec wslpath -u $StartupRequestPath
                if ($LASTEXITCODE -ne 0 -or -not $wslRequestPath) {
                    throw "Could not resolve the startup test request in WSL."
                }
                $startupArguments += ([string]$wslRequestPath).Trim()
            }
            elseif ($PSBoundParameters.ContainsKey("AgentBaudRate")) {
                $startupArguments += "__MACSTUDIO_NO_REQUEST__"
            }
            if ($PSBoundParameters.ContainsKey("AgentBaudRate")) {
                $startupArguments += $AgentBaudRate
            }
            if ($StartupTelemetryPath) {
                if (Test-Path -LiteralPath $StartupTelemetryPath) {
                    throw "Startup telemetry output already exists: $StartupTelemetryPath"
                }
                $telemetryDirectory = Split-Path -Parent $StartupTelemetryPath
                if ($telemetryDirectory) {
                    New-Item -ItemType Directory -Path $telemetryDirectory -Force | Out-Null
                }
                $wslTelemetryPath = & wsl.exe `
                    --distribution $WslDistribution `
                    --exec wslpath -u $StartupTelemetryPath
                if ($LASTEXITCODE -ne 0 -or -not $wslTelemetryPath) {
                    throw "Could not resolve startup telemetry output in WSL."
                }
                $wslTelemetryPath = ([string]$wslTelemetryPath).Trim()
            }
            & wsl.exe `
                --distribution $WslDistribution `
                --exec bash ([string]$wslBridgeScript).Trim() `
                @startupArguments
            if ($LASTEXITCODE -ne 0) {
                throw "Could not install $AppName in the System 7 Startup Items folder."
            }
        }
        Copy-Item -LiteralPath $appDisk -Destination (
            Join-Path $temporaryDirectory "disk2.dsk"
        )
    }

    if ($Emulator -eq "MiniVmac") {
        $romHash = (Get-FileHash -LiteralPath $rom -Algorithm SHA1).Hash.ToLowerInvariant()
        if ($romHash -ne "58532b7d0d49659fd5228ac334a1b094f0241968") {
            throw "ROM does not match the tested Macintosh SE ROM (SHA-1 $romHash)."
        }
        Copy-Item -LiteralPath $rom -Destination (Join-Path $temporaryDirectory "MacSE.ROM")
        Copy-Item -LiteralPath $emulatorExecutable -Destination (
            Join-Path $temporaryDirectory "Mini vMac.exe"
        )
        Write-Output "Starting the Mac SE emulator with a disposable System 7 volume."
    }
    else {
        Write-Output "Starting Basilisk II for $TargetProfileId with a disposable System 7 volume."
    }
    if ($AutoStartApplication) {
        Write-Output "System 7 will start $AppName from its Startup Items folder."
    }
    elseif ($AppDiskImagePath) {
        Write-Output "In Finder, open the second disk and double-click $AppName."
        Write-Output "The app should show its generated form; click a button to close it."
    }
    if ($StartupTelemetryPath) {
        Write-Output "Mini vMac will stop automatically when the guest test completes."
    }
    else {
        Write-Output "Use Stop Emulator in the IDE or close the emulator when finished."
    }
    if ($Emulator -eq "MiniVmac") {
        $emulatorProcess = Start-Process `
            -FilePath (Join-Path $temporaryDirectory "Mini vMac.exe") `
            -WorkingDirectory $temporaryDirectory `
            -PassThru
        Write-Output "Mini vMac process ID: $($emulatorProcess.Id)"
    }
    else {
        $basiliskArguments = @(
            "--config",
            (Join-Path $temporaryDirectory "BasiliskII.prefs"),
            "--rom",
            $rom,
            "--disk",
            $volumePath,
            "--screen",
            "win/$GuestScreenWidth/$GuestScreenHeight/$GuestScreenDepthBits",
            "--displaycolordepth",
            "$GuestScreenDepthBits",
            "--modelid",
            "5",
            "--ramsize",
            "33554432",
            "--cpu",
            "3",
            "--nosound",
            "true",
            "--nogui",
            "true"
        )
        if ($BasiliskSerialA) {
            $serialADevice = $BasiliskSerialA
            if ($serialADevice -match '^COM[1-9][0-9]+$') {
                $serialADevice = "\\.\$serialADevice"
            }
            $basiliskArguments += @("--seriala", $serialADevice)
        }
        if ($BasiliskSerialB) {
            $serialBDevice = $BasiliskSerialB
            if ($serialBDevice -match '^COM[1-9][0-9]+$') {
                $serialBDevice = "\\.\$serialBDevice"
            }
            $basiliskArguments += @("--serialb", $serialBDevice)
        }
        $applicationVolume = Join-Path $temporaryDirectory "disk2.dsk"
        if (Test-Path -LiteralPath $applicationVolume -PathType Leaf) {
            $basiliskArguments += @("--disk", $applicationVolume)
        }
        $argumentString = ($basiliskArguments | ForEach-Object {
            '"' + ([string]$_).Replace('"', '\"') + '"'
        }) -join " "
        $emulatorProcess = Start-Process `
            -FilePath $emulatorExecutable `
            -ArgumentList $argumentString `
            -WorkingDirectory (Split-Path -Parent $emulatorExecutable) `
            -PassThru
        Write-Output "Basilisk II process ID: $($emulatorProcess.Id)"
    }
    $completionTimedOut = $false
    if ($StartupTelemetryPath) {
        $completionPattern = "1111111100000110111011010101101110110000011111111"
        $dialogLeft = 24
        $dialogTop = 32
        # QuickDraw's dialog port starts one pixel below the DLOG top.
        $dialogPortTopInset = 1
        $dialogClientWidth = $GuestScreenWidth - 48
        $dialogClientHeight = $GuestScreenHeight - 58 - 20
        $markerLeft = $dialogLeft + $dialogClientWidth - 21 - 8
        $markerTop = $dialogTop + $dialogPortTopInset + $dialogClientHeight - 21 - 8
        $completionDeadline = [DateTime]::UtcNow.AddSeconds(
            $CompletionTimeoutSeconds
        )
        Write-Output "Waiting for the guest test completion marker."
        $completionDetected = $false
        while (-not $emulatorProcess.HasExited) {
            $emulatorProcess.Refresh()
            if ([ClassicMacCompletionMarkerProbe]::IsVisible(
                    $emulatorProcess.Id,
                    $GuestScreenWidth,
                    $GuestScreenHeight,
                    $markerLeft,
                    $markerTop,
                    3,
                    $completionPattern)) {
                $completionDetected = $true
                break
            }
            if ([DateTime]::UtcNow -ge $completionDeadline) {
                $completionTimedOut = $true
                break
            }
            Start-Sleep -Milliseconds 200
        }
        if ($completionDetected -or $completionTimedOut) {
            if ($completionDetected) {
                Write-Output "Guest test completion marker detected; stopping Mini vMac."
            }
            else {
                Write-Output "Guest test marker timed out; stopping Mini vMac to extract partial telemetry."
            }
            if ($GuestScreenshotPath) {
                $screenshotDirectory = Split-Path -Parent $GuestScreenshotPath
                if ($screenshotDirectory) {
                    New-Item -ItemType Directory -Path $screenshotDirectory -Force | Out-Null
                }
                [ClassicMacCompletionMarkerProbe]::SaveScreen(
                    $emulatorProcess.Id,
                    $GuestScreenWidth,
                    $GuestScreenHeight,
                    [System.IO.Path]::GetFullPath($GuestScreenshotPath)
                )
                Write-Output "Guest screenshot saved: $GuestScreenshotPath"
            }
            if (-not $emulatorProcess.CloseMainWindow()) {
                Stop-Process -Id $emulatorProcess.Id -Force
            }
            elseif (-not $emulatorProcess.WaitForExit(5000)) {
                Stop-Process -Id $emulatorProcess.Id -Force
            }
        }
    }
    $emulatorProcess.WaitForExit()
    if ($ResultDiskImagePath) {
        $temporaryResultDisk = Join-Path $temporaryDirectory "disk2.dsk"
        if (-not (Test-Path -LiteralPath $temporaryResultDisk -PathType Leaf)) {
            throw "Mini vMac exited without its application disk: $temporaryResultDisk"
        }
        Copy-Item `
            -LiteralPath $temporaryResultDisk `
            -Destination $ResultDiskImagePath `
            -Force
        Write-Output "Guest result disk preserved: $ResultDiskImagePath"
    }
    if ($ResultSystemDiskImagePath) {
        $temporarySystemDisk = Join-Path $temporaryDirectory "disk1.dsk"
        if (-not (Test-Path -LiteralPath $temporarySystemDisk -PathType Leaf)) {
            throw "Emulator exited without its System 7 disk: $temporarySystemDisk"
        }
        Copy-Item `
            -LiteralPath $temporarySystemDisk `
            -Destination $ResultSystemDiskImagePath `
            -Force
        Write-Output "Guest System 7 disk preserved: $ResultSystemDiskImagePath"
    }
    if ($StartupTelemetryPath) {
        & wsl.exe `
            --distribution $WslDistribution `
            --exec bash ([string]$wslBridgeScript).Trim() `
            extract-response $wslVolumePath $wslTelemetryPath
        if ($LASTEXITCODE -ne 0) {
            if ($completionTimedOut) {
                throw "Guest test completion marker was not detected within $CompletionTimeoutSeconds seconds, and partial telemetry was unavailable."
            }
            throw "Guest telemetry was not returned on the disposable System 7 volume."
        }
        Write-Output "Guest telemetry saved: $StartupTelemetryPath"
    }
    if ($completionTimedOut) {
        throw "Guest test completion marker was not detected within $CompletionTimeoutSeconds seconds; partial telemetry was saved to $StartupTelemetryPath."
    }
}
finally {
    if ($emulatorProcess) {
        $emulatorProcess.Refresh()
        if (-not $emulatorProcess.HasExited) {
            $closed = $emulatorProcess.CloseMainWindow()
            if (-not $closed -or -not $emulatorProcess.WaitForExit(5000)) {
                Stop-Process -Id $emulatorProcess.Id -Force
                $emulatorProcess.WaitForExit()
            }
        }
    }
    if (Test-Path -LiteralPath $temporaryDirectory) {
        Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
    }
}
