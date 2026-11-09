param(
    [Parameter(Mandatory = $true)]
    [string] $ProjectDirectory,
    [string] $AgentPort,
    [string] $LinkPort,
    [switch] $SmokeTest
)

$ErrorActionPreference = 'Stop'
$projectPath = (Resolve-Path -LiteralPath $ProjectDirectory).Path
$studioRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$repositoryRoot = (Resolve-Path (Join-Path $studioRoot '..')).Path
$buildScript = Join-Path $PSScriptRoot 'build.bat'
$dosbox = Join-Path $repositoryRoot 'dosbox-x\dosbox-x.exe'
$outputDirectory = Join-Path $projectPath 'build'
$executable = Join-Path $outputDirectory 'FRTOSGUI.EXE'

if ($AgentPort -and $AgentPort -notmatch '^COM[1-9][0-9]*$') {
    throw "AgentPort must be a Windows COM port name such as COM2, not '$AgentPort'."
}

$linkPortFile = Join-Path $projectPath 'link-port.txt'
if (-not $LinkPort -and (Test-Path -LiteralPath $linkPortFile -PathType Leaf)) {
    $LinkPort = (Get-Content -LiteralPath $linkPortFile -Raw).Trim()
}
if ($LinkPort -and $LinkPort -notmatch '^COM[1-9][0-9]*$') {
    throw "LinkPort must be a Windows COM port name such as COM5, not '$LinkPort'."
}
if ($AgentPort -and $LinkPort -and $AgentPort -eq $LinkPort) {
    throw 'AgentPort and LinkPort must use separate Windows COM ports.'
}

if (-not (Test-Path -LiteralPath $dosbox -PathType Leaf)) {
    throw "DOSBox-X was not found at '$dosbox'."
}

if ($SmokeTest) {
    & $env:ComSpec /d /c "`"$buildScript`" `"$projectPath`" SMOKE"
}
else {
    & $env:ComSpec /d /c "`"$buildScript`" `"$projectPath`""
}
if ($LASTEXITCODE -ne 0) {
    throw "Open Watcom build failed with exit code $LASTEXITCODE."
}
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "The build did not produce '$executable'."
}

$agentConfigPath = Join-Path $outputDirectory 'DOSAGENT.CFG'
if (-not (Test-Path -LiteralPath $agentConfigPath -PathType Leaf)) {
    @(
        'BASE=0x3F8',
        'IRQ=4'
    ) | Set-Content -LiteralPath $agentConfigPath -Encoding ASCII
}

$configPath = Join-Path $outputDirectory 'dosbox-hercules.conf'
$serialDevice = if ($AgentPort) {
    "directserial realport:$AgentPort rxdelay:1000"
}
else {
    'dummy'
}
$linkSerialDevice = if ($LinkPort) {
    "directserial realport:$LinkPort rxdelay:1000"
}
else {
    'dummy'
}
$configuration = @(
    '[sdl]',
    'fullscreen = false',
    '',
    '[dosbox]',
    'machine = hercules',
    'memsize = 1',
    'memsizekb = 640',
    'working directory option = noprompt',
    'startbanner = false',
    '',
    '[cpu]',
    'core = normal',
    'cputype = 8086',
    'cycles = 3000',
    '',
    '[dos]',
    'xms = false',
    'ems = false',
    'umb = false',
    '',
    '[serial]',
    "serial1 = $serialDevice",
    "serial2 = $linkSerialDevice",
    '',
    '[autoexec]',
    'mount c .',
    'c:',
    'cd BUILD',
    'FRTOSGUI.EXE',
    'exit'
)
Set-Content -LiteralPath $configPath -Value $configuration -Encoding ASCII

Push-Location -LiteralPath $projectPath
try {
    if ($SmokeTest) {
        $resultPath = Join-Path $outputDirectory 'RESULT.TXT'
        if (Test-Path -LiteralPath $resultPath) {
            Remove-Item -LiteralPath $resultPath
        }
        Write-Output 'Running the 8088 / Hercules DOSBox-X smoke test.'
        & $dosbox -nopromptfolder -conf $configPath -fastlaunch -silent -exit -time-limit 60
    }
    else {
        Write-Output 'Starting DOSBox-X in 8088 / Hercules mode. Press ESC in the guest to exit.'
        & $dosbox -nopromptfolder -conf $configPath -fastlaunch -exit
    }
}
finally {
    Pop-Location
}
if ($LASTEXITCODE -ne 0) {
    throw "DOSBox-X exited with code $LASTEXITCODE."
}
if ($SmokeTest) {
    $resultPath = Join-Path $outputDirectory 'RESULT.TXT'
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    $passed = $false
    do {
        if (Test-Path -LiteralPath $resultPath) {
            $statusPassed = Select-String -LiteralPath $resultPath -Pattern '^status=PASS$' -Quiet
            $pagesMatch = Select-String -LiteralPath $resultPath -Pattern '^hgc_pages_match=1$' -Quiet
            $passed = $statusPassed -and $pagesMatch
        }
        if ($passed) {
            break
        }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)

    if (-not $passed) {
        $details = if (Test-Path -LiteralPath $resultPath) {
            (Get-Content -LiteralPath $resultPath) -join ', '
        }
        else {
            'DOSBox-X did not create RESULT.TXT.'
        }
        throw "The FreeRTOS GUI smoke test did not report status=PASS ($details)."
    }
    Write-Output 'FreeRTOS task startup, generated form drawing, and Hercules page synchronization passed.'
}
