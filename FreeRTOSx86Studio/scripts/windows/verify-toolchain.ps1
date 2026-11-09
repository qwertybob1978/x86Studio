[CmdletBinding()]
param(
    [string]$OutputPath = "feasibility/evidence/latest-toolchain-report.json"
)

$ErrorActionPreference = "Stop"
$workspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
Push-Location $workspaceRoot
try {
    & deno run --allow-run --allow-read=.local-tools --allow-write scripts/windows/verify-toolchain.ts --output $OutputPath
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
