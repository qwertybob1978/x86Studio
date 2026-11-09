$ErrorActionPreference = "Stop"
$repositoryRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Push-Location $repositoryRoot

try {
    foreach ($path in @(
        "scripts/windows/boot-mac-se.ps1",
        "scripts/windows/build-and-run-mac-se.ps1"
    )) {
        $tokens = $null
        $parseErrors = $null
        [System.Management.Automation.Language.Parser]::ParseFile(
            (Join-Path $repositoryRoot $path),
            [ref]$tokens,
            [ref]$parseErrors
        ) | Out-Null
        if ($parseErrors.Count -gt 0) {
            $details = ($parseErrors | ForEach-Object { $_.Message }) -join "; "
            throw "PowerShell syntax validation failed for ${path}: $details"
        }
    }

    $bridgeScript = Join-Path $repositoryRoot "scripts/wsl/mac-test-bridge.sh"
    $wslBridgeScript = & wsl.exe --distribution Ubuntu --exec wslpath -u $bridgeScript
    if ($LASTEXITCODE -ne 0 -or -not $wslBridgeScript) {
        throw "Could not resolve the HFS bridge script in WSL."
    }
    & wsl.exe --distribution Ubuntu --exec bash -n ([string]$wslBridgeScript).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "HFS bridge Bash syntax validation failed."
    }

    & deno task check
    if ($LASTEXITCODE -ne 0) {
        throw "Deno type checking failed with exit code $LASTEXITCODE."
    }

    & deno task test
    if ($LASTEXITCODE -ne 0) {
        throw "Deno unit tests failed with exit code $LASTEXITCODE."
    }

    & dotnet build src\ClassicMacIde.App\ClassicMacIde.App.csproj --configuration Release
    if ($LASTEXITCODE -ne 0) {
        throw "WPF application build failed with exit code $LASTEXITCODE."
    }

    & dotnet test ClassicMacIde.slnx --configuration Release
    if ($LASTEXITCODE -ne 0) {
        throw ".NET unit tests failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}
