[CmdletBinding()]
param(
    [string]$DiskImagePath,
    [string]$RomImagePath,
    [string]$EmulatorPath,
    [ValidateSet("MiniVmac", "BasiliskII")]
    [string]$Emulator = "MiniVmac",
    [ValidateSet(
        "mac-se-68k",
        "mac-color-classic-68k",
        "mac-powerbook-165c-68k"
    )]
    [string]$TargetProfileId = "mac-se-68k",
    [string]$WslDistribution = "Ubuntu",
    [string]$SourceDirectory,
    [string]$BuildDirectory,
    [string]$AppName = "HelloMac",
    [string]$TestRequestPath,
    [string]$TelemetryPath,
    [string]$GuestScreenshotPath,
    [string]$BasiliskSerialA,
    [string]$BasiliskSerialB,
    [int]$GuestScreenWidth = 512,
    [int]$GuestScreenHeight = 342,
    [ValidateRange(1, 600)]
    [int]$CompletionTimeoutSeconds = 120
)

$ErrorActionPreference = "Stop"
$workspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
switch ($TargetProfileId) {
    "mac-se-68k" {
        $profileWidth = 512
        $profileHeight = 342
        $profileDepth = 1
        $defaultRomPath = "images/256KB ROMs/1987-03 - B2E362A8 - Mac SE.ROM"
    }
    "mac-color-classic-68k" {
        $profileWidth = 512
        $profileHeight = 384
        $profileDepth = 8
        $defaultRomPath = "images/1MB ROMs/1993-02 - ECD99DC0 - Color Classic.ROM"
    }
    "mac-powerbook-165c-68k" {
        $profileWidth = 640
        $profileHeight = 400
        $profileDepth = 8
        $defaultRomPath = "images/1MB ROMs/1992-10 - E33B2724 - Powerbook 160 & 165 & 165c & 180 & 180c.ROM"
    }
    default {
        throw "Build & Run is not configured for target '$TargetProfileId'."
    }
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
if ($GuestScreenWidth -ne $profileWidth -or $GuestScreenHeight -ne $profileHeight) {
    throw "Guest screen dimensions must match target '$TargetProfileId' ($profileWidth x $profileHeight)."
}
if ($Emulator -eq "BasiliskII" -and $TestRequestPath) {
    throw "Automated guest tests are supported only with Mini vMac and the Macintosh SE."
}
if ([string]::IsNullOrWhiteSpace($DiskImagePath)) {
    $DiskImagePath = Join-Path $workspaceRoot "images/OS_755_2GB.dsk"
}
if ([string]::IsNullOrWhiteSpace($EmulatorPath)) {
    $EmulatorPath = if ($Emulator -eq "MiniVmac") {
        Join-Path $workspaceRoot ".local-tools/emulators/MiniVmac-SE/Mini vMac.exe"
    }
    else {
        Join-Path $workspaceRoot ".local-tools/emulators/BasiliskII/BasiliskII.exe"
    }
}
if ([string]::IsNullOrWhiteSpace($SourceDirectory)) {
    $SourceDirectory = Join-Path `
        $workspaceRoot `
        "tutorial/samples/hello-mac"
}
if ([string]::IsNullOrWhiteSpace($BuildDirectory)) {
    $BuildDirectory = Join-Path `
        $workspaceRoot `
        ".local-tools/retro68-hello-mac-build"
}
Push-Location $workspaceRoot
try {
    $buildScript = Join-Path $workspaceRoot "scripts/wsl/build-hello-mac.sh"
    $bootScript = Join-Path $workspaceRoot "scripts/windows/boot-mac-se.ps1"
    $bridgeScript = Join-Path $workspaceRoot "scripts/wsl/mac-test-bridge.sh"

    foreach ($requiredPath in @(
        $DiskImagePath,
        $EmulatorPath,
        $buildScript,
        $bootScript,
        $bridgeScript
    )) {
        if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
            throw "Required file was not found: $requiredPath"
        }
    }
    if ([string]::IsNullOrWhiteSpace($TestRequestPath) -xor
        [string]::IsNullOrWhiteSpace($TelemetryPath)) {
        throw "TestRequestPath and TelemetryPath must be provided together."
    }
    if ($TestRequestPath) {
        if ($GuestScreenWidth -lt 1 -or $GuestScreenHeight -lt 1) {
            throw "Guest screen dimensions must be positive."
        }
        if (-not (Test-Path -LiteralPath $bridgeScript -PathType Leaf)) {
            throw "Required test bridge script was not found: $bridgeScript"
        }
        if (-not (Test-Path -LiteralPath $TestRequestPath -PathType Leaf)) {
            throw "Test request file was not found: $TestRequestPath"
        }
        if (Test-Path -LiteralPath $TelemetryPath) {
            throw "Telemetry output already exists; use a fresh per-run path: $TelemetryPath"
        }
    }
    if (-not (Test-Path -LiteralPath $SourceDirectory -PathType Container)) {
        throw "Application source directory was not found: $SourceDirectory"
    }
    if ($AppName -notmatch '^[A-Za-z][A-Za-z0-9_]{0,30}$') {
        throw "Application name must start with a letter and contain only letters, digits, or underscores."
    }

    if (-not $RomImagePath) {
        $RomImagePath = Join-Path $workspaceRoot $defaultRomPath
    }
    if (-not (Test-Path -LiteralPath $RomImagePath -PathType Leaf)) {
        throw "ROM image was not found: $RomImagePath"
    }

    $wslBuildScript = & wsl.exe --distribution $WslDistribution --exec wslpath -u $buildScript
    if ($LASTEXITCODE -ne 0 -or -not $wslBuildScript) {
        throw "Could not resolve the build script path in WSL distribution '$WslDistribution'."
    }
    $wslBuildScript = ([string]$wslBuildScript).Trim()

    $wslSourceDirectory = & wsl.exe --distribution $WslDistribution --exec wslpath -u $SourceDirectory
    if ($LASTEXITCODE -ne 0 -or -not $wslSourceDirectory) {
        throw "Could not resolve the application source path in WSL distribution '$WslDistribution'."
    }
    $wslSourceDirectory = ([string]$wslSourceDirectory).Trim()

    $wslProjectDirectory = "/tmp/classic-mac-ide/$AppName"
    $wslSourceCopyDirectory = "$wslProjectDirectory/source"
    $wslBuildDirectory = "$wslProjectDirectory/build"
    $wslWindowsBuildDirectory = & wsl.exe --distribution $WslDistribution --exec wslpath -u $BuildDirectory
    if ($LASTEXITCODE -ne 0 -or -not $wslWindowsBuildDirectory) {
        throw "Could not resolve the Windows build output path in WSL distribution '$WslDistribution'."
    }
    $wslWindowsBuildDirectory = ([string]$wslWindowsBuildDirectory).Trim()

    Write-Output "Building the 68k $AppName application with Retro68 in WSL ($WslDistribution)."
    & wsl.exe --distribution $WslDistribution --exec mkdir -p $wslSourceCopyDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "Could not create the WSL source directory '$wslSourceCopyDirectory'."
    }
    & wsl.exe `
        --distribution $WslDistribution `
        --exec cp -R -- "$wslSourceDirectory/." $wslSourceCopyDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "Could not copy the generated application sources into WSL."
    }

    & wsl.exe `
        --distribution $WslDistribution `
        --exec bash $wslBuildScript `
        $wslSourceCopyDirectory `
        $wslBuildDirectory `
        $AppName
    if ($LASTEXITCODE -ne 0) {
        throw "Retro68 build failed with exit code $LASTEXITCODE."
    }
    New-Item -ItemType Directory -Path $BuildDirectory -Force | Out-Null
    $wslArtifactPath = "$wslBuildDirectory/$AppName.dsk"
    $wslWindowsArtifactPath = "$wslWindowsBuildDirectory/$AppName.dsk"
    & wsl.exe --distribution $WslDistribution --exec cp -- $wslArtifactPath $wslWindowsArtifactPath
    if ($LASTEXITCODE -ne 0) {
        throw "Could not copy the application disk from WSL to $BuildDirectory."
    }

    $artifactPath = Join-Path $BuildDirectory "$AppName.dsk"
    if (-not (Test-Path -LiteralPath $artifactPath -PathType Leaf)) {
        throw "Retro68 reported success but the application disk is missing: $artifactPath"
    }

    $runDiskPath = $artifactPath
    $wslBridgeScript = & wsl.exe `
        --distribution $WslDistribution `
        --exec wslpath -u $bridgeScript
    if ($LASTEXITCODE -ne 0 -or -not $wslBridgeScript) {
        throw "Could not resolve the HFS bridge script in WSL distribution '$WslDistribution'."
    }
    $wslBridgeScript = ([string]$wslBridgeScript).Trim()
    if ($TestRequestPath) {
        $telemetryDirectory = Split-Path -Parent $TelemetryPath
        if ($telemetryDirectory) {
            New-Item -ItemType Directory -Path $telemetryDirectory -Force | Out-Null
        }
    }

    Write-Output "Build succeeded: $artifactPath"
    $bootParameters = @{
        DiskImagePath = $DiskImagePath
        RomImagePath = $RomImagePath
        EmulatorPath = $EmulatorPath
        Emulator = $Emulator
        TargetProfileId = $TargetProfileId
        GuestScreenWidth = $profileWidth
        GuestScreenHeight = $profileHeight
        GuestScreenDepthBits = $profileDepth
        AppName = $AppName
        AppDiskImagePath = $runDiskPath
        WslDistribution = $WslDistribution
        HfsBridgeScriptPath = $bridgeScript
        AutoStartApplication = $true
    }
    if ($TestRequestPath) {
        $bootParameters.StartupRequestPath = $TestRequestPath
        $bootParameters.StartupTelemetryPath = $TelemetryPath
        $bootParameters.GuestScreenWidth = $GuestScreenWidth
        $bootParameters.GuestScreenHeight = $GuestScreenHeight
        $bootParameters.CompletionTimeoutSeconds = $CompletionTimeoutSeconds
    }
    if ($GuestScreenshotPath) {
        $bootParameters.GuestScreenshotPath = $GuestScreenshotPath
    }
    if ($BasiliskSerialA) {
        $bootParameters.BasiliskSerialA = $BasiliskSerialA
    }
    if ($BasiliskSerialB) {
        $bootParameters.BasiliskSerialB = $BasiliskSerialB
    }
    & $bootScript @bootParameters

    if ($TestRequestPath) {
        Get-Content -LiteralPath $TelemetryPath | ForEach-Object {
            Write-Output "GUEST: $_"
        }
        Write-Output "Emulator test session ended."
    }
    else {
        Write-Output "Emulator session ended after launching $AppName from System 7 Startup Items."
    }
}
finally {
    Pop-Location
}
