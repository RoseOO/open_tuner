<#
.SYNOPSIS
    Builds (if needed) and launches the OpenTuner WPF app together with the
    PicoTuner (WH) demo emulator, so the app receives a test video.

.EXAMPLE
    .\run-demo.ps1
    .\run-demo.ps1 -Mp4 "C:\videos\clip.mp4" -Build
    .\run-demo.ps1 -DemoOnly
#>
[CmdletBinding()]
param(
    # Source MP4 the demo will remux to a transport stream.
    [string]$Mp4 = (Join-Path $env:USERPROFILE 'Downloads\720p.mp4'),

    # Base IP port the demo emulates (OpenTuner default is 9900).
    [int]$BasePort = 9900,

    # Force a build before launching.
    [switch]$Build,

    # Use the Release build instead of Debug.
    [switch]$Release,

    # Start only the demo (no OpenTuner).
    [switch]$DemoOnly,

    # Start only OpenTuner (no demo).
    [switch]$AppOnly
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$config = if ($Release) { 'Release' } else { 'Debug' }

$wpf  = Join-Path $root "opentunerWpf\bin\$config\net472\opentuner.wpf.exe"
$demo = Join-Path $root "picotunerdemo\bin\$config\net472\picotunerdemo.exe"
$sln  = Join-Path $root 'opentuner.sln'

function Write-Info($msg) { Write-Host $msg -ForegroundColor Cyan }
function Write-Warn($msg) { Write-Host $msg -ForegroundColor Yellow }

function Get-MsBuild {
    $candidates = @(
        'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe',
        'C:\Program Files\Microsoft Visual Studio\17\Community\MSBuild\Current\Bin\MSBuild.exe',
        'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe',
        'C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe'
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }

    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        $found = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' 2>$null | Select-Object -First 1
        if ($found -and (Test-Path $found)) { return $found }
    }
    return $null
}

function Build-Solution {
    $msbuild = Get-MsBuild
    if (-not $msbuild) {
        throw "MSBuild.exe not found. Install Visual Studio (with .NET desktop build tools) or build manually."
    }

    Write-Info "Building solution ($config|x64) with $msbuild ..."
    & $msbuild $sln /t:Restore /p:Configuration=$config /p:Platform=x64 /p:RestorePackagesConfig=true /v:minimal /nologo
    if ($LASTEXITCODE -ne 0) { throw "NuGet restore failed." }

    & $msbuild $sln /t:Build /p:Configuration=$config /p:Platform=x64 /p:RestorePackagesConfig=true /v:minimal /nologo
    if ($LASTEXITCODE -ne 0) { throw "Build failed." }
}

# ---------------------------------------------------------------------------
if ($Build -or (-not $AppOnly -and -not (Test-Path $demo)) -or (-not $DemoOnly -and -not (Test-Path $wpf))) {
    Build-Solution
}

if (-not (Test-Path $wpf))  { throw "OpenTuner WPF not found: $wpf" }
if (-not (Test-Path $demo)) { throw "Demo emulator not found: $demo" }

# ---------------------------------------------------------------------------
$demoProc = $null

if (-not $AppOnly) {
    if (-not (Test-Path $Mp4)) {
        Write-Warn "MP4 not found: $Mp4"
        Write-Warn "Pass one with:  .\run-demo.ps1 -Mp4 <path>"
        throw "Missing source MP4."
    }

    Write-Info "Starting PicoTuner demo emulator ..."
    $demoProc = Start-Process -FilePath $demo `
                              -ArgumentList @('--mp4', $Mp4, '--base', "$BasePort") `
                              -WorkingDirectory (Split-Path $demo) `
                              -PassThru

    # give the demo a moment to remux (first run) and bind its UDP ports
    Start-Sleep -Seconds 4

    if ($demoProc.HasExited) {
        Write-Warn "The demo emulator exited immediately (code $($demoProc.ExitCode))."
        Write-Warn "Common causes: another demo/OpenTuner is using ports $($BasePort+21)/$($BasePort+22),"
        Write-Warn "or the MP4 could not be remuxed."
        Write-Warn "Run it directly to see the error:"
        Write-Warn "    `"$demo`" --mp4 `"$Mp4`""
        throw "Demo emulator failed to start."
    }

    Write-Host "  demo PID $($demoProc.Id)  (command ports $($BasePort+21)/$($BasePort+22), TS ports $($BasePort+41)/$($BasePort+42))" -ForegroundColor DarkGray
}

if (-not $DemoOnly) {
    Write-Info "Starting OpenTuner (WPF) ..."
    $appProc = Start-Process -FilePath $wpf -WorkingDirectory (Split-Path $wpf) -PassThru

    Write-Host ""
    Write-Host "  In OpenTuner: pick the 'WinterHill Variant' source, set its" -ForegroundColor Green
    Write-Host "  interface to 'PicoTuner (G4EWJ Ethernet WH)', then Connect." -ForegroundColor Green
    Write-Host "  Tuning the source will start playback automatically." -ForegroundColor Green
    Write-Host ""

    Wait-Process -Id $appProc.Id
    Write-Info "OpenTuner closed."
}

if ($demoProc -and -not $demoProc.HasExited) {
    Write-Info "Stopping demo emulator ..."
    Stop-Process -Id $demoProc.Id -Force -ErrorAction SilentlyContinue
}

Write-Info "Done."
