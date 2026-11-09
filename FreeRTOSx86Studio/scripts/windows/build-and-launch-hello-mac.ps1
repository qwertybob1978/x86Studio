[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Profile,

    [Parameter(Mandatory = $true)]
    [string]$ConfigPath,

    [string]$EvidencePath = "feasibility/evidence/latest-build-launch.json"
)

$ErrorActionPreference = "Stop"
$workspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
Push-Location $workspaceRoot
try {
    & deno run --allow-run --allow-read --allow-write scripts/windows/build-and-launch-hello-mac.ts `
        --profile $Profile `
        --config $ConfigPath `
        --evidence $EvidencePath
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
