[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$LaunchSpecPath
)

$ErrorActionPreference = "Stop"
$spec = Get-Content -LiteralPath $LaunchSpecPath -Raw | ConvertFrom-Json

function ConvertTo-WindowsArgument([string]$Value) {
    $builder = New-Object System.Text.StringBuilder
    [void]$builder.Append('"')
    $slashes = 0
    foreach ($character in $Value.ToCharArray()) {
        if ($character -eq '\') {
            $slashes += 1
            continue
        }
        if ($character -eq '"') {
            if ($slashes -gt 0) {
                [void]$builder.Append(('\' * (2 * $slashes)))
            }
            [void]$builder.Append('\')
            [void]$builder.Append('"')
            $slashes = 0
            continue
        }
        if ($slashes -gt 0) {
            [void]$builder.Append(('\' * $slashes))
            $slashes = 0
        }
        [void]$builder.Append($character)
    }
    if ($slashes -gt 0) {
        [void]$builder.Append(('\' * (2 * $slashes)))
    }
    [void]$builder.Append('"')
    return $builder.ToString()
}

$quotedArguments = @($spec.args | ForEach-Object {
    ConvertTo-WindowsArgument ([string]$_)
})
$argumentLine = [string]::Join(" ", $quotedArguments)
$startParameters = @{
    FilePath = [string]$spec.command
    PassThru = $true
}
if ($spec.cwd) {
    $startParameters.WorkingDirectory = [string]$spec.cwd
}
if ($spec.stdoutPath) {
    $startParameters.RedirectStandardOutput = [string]$spec.stdoutPath
}
if ($spec.stderrPath) {
    $startParameters.RedirectStandardError = [string]$spec.stderrPath
}
if ($argumentLine) {
    $startParameters.ArgumentList = $argumentLine
}
$process = Start-Process @startParameters
if ($spec.waitForExit) {
    $process.WaitForExit()
    Write-Output $process.Id
}
else {
    Write-Output $process.Id
}
