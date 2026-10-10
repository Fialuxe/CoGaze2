<#
.SYNOPSIS
    Runs a Unity project's tests in batch mode and prints a pass/fail summary.

.EXAMPLE
    ./scripts/run-unity-tests.ps1 -Project expert-app
    ./scripts/run-unity-tests.ps1 -Project worker-app -Platform PlayMode
    ./scripts/run-unity-tests.ps1 -ProjectPath C:\tmp\copy\expert-app -Unity "C:\Program Files\Unity\Hub\Editor\6000.1.7f1\Editor\Unity.exe"

.NOTES
    Picks the editor matching ProjectSettings/ProjectVersion.txt from Unity Hub; if that version is
    not installed it falls back to the newest installed editor and says so. A newer editor upgrades
    the project on open — run against a copy (-ProjectPath) if you do not want that.
    Exit code: 0 all passed, 1 failures, 2 could not run.
#>
param(
    [ValidateSet("expert-app", "worker-app")] [string] $Project = "expert-app",
    [ValidateSet("EditMode", "PlayMode")] [string] $Platform = "EditMode",
    [string] $ProjectPath = "",
    [string] $Unity = ""
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
if (-not $ProjectPath) { $ProjectPath = Join-Path $repo $Project }
$ProjectPath = (Resolve-Path $ProjectPath).Path

if (-not $Unity) {
    $hub = "C:\Program Files\Unity\Hub\Editor"
    $wanted = (Get-Content (Join-Path $ProjectPath "ProjectSettings\ProjectVersion.txt") |
        Where-Object { $_ -like "m_EditorVersion:*" }) -replace "m_EditorVersion:\s*", ""
    $exact = Join-Path $hub "$wanted\Editor\Unity.exe"
    if (Test-Path $exact) { $Unity = $exact }
    else {
        $newest = Get-ChildItem $hub -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending | Select-Object -First 1
        if (-not $newest) { Write-Host "No Unity editor found under $hub. Pass -Unity." -ForegroundColor Red; exit 2 }
        $Unity = Join-Path $newest.FullName "Editor\Unity.exe"
        Write-Host "Unity $wanted is not installed; using $($newest.Name). The project will be upgraded on open." -ForegroundColor Yellow
    }
}

$out = Join-Path $repo "Logs"
New-Item -ItemType Directory -Force $out | Out-Null
$results = Join-Path $out "$Project-$Platform-results.xml"
$log = Join-Path $out "$Project-$Platform.log"
Remove-Item $results -ErrorAction SilentlyContinue

Write-Host "Running $Platform tests in $ProjectPath ..."
# Wait for Unity itself only: -Wait would also wait for the licensing client it leaves running.
$proc = Start-Process -FilePath $Unity -PassThru -NoNewWindow -ArgumentList @(
    "-batchmode", "-nographics", "-projectPath", "`"$ProjectPath`"",
    "-runTests", "-testPlatform", $Platform, "-testResults", "`"$results`"", "-logFile", "`"$log`"")
$proc.WaitForExit()

if (-not (Test-Path $results)) {
    if (Select-String -Path $log -Pattern "No valid Unity Editor license" -Quiet) {
        Write-Host "Unity has no active license. Sign in to Unity Hub and activate a licence, then rerun." -ForegroundColor Red
        exit 2
    }
    Write-Host "No test results (exit $($proc.ExitCode)). Compile errors, from $log :" -ForegroundColor Red
    Select-String -Path $log -Pattern "error CS\d+|Scripts have compiler errors" | Select-Object -First 30 |
        ForEach-Object { Write-Host "  $($_.Line.Trim())" }
    exit 2
}

[xml] $xml = Get-Content $results
$run = $xml.'test-run'
Write-Host ("{0} passed, {1} failed, {2} skipped ({3} total)" -f $run.passed, $run.failed, $run.skipped, $run.total)
$xml.SelectNodes("//test-case[@result='Failed']") | ForEach-Object {
    Write-Host "  FAIL $($_.fullname)" -ForegroundColor Red
    Write-Host "       $($_.failure.message.'#cdata-section'.Trim())"
}
if ([int]$run.failed -gt 0) { exit 1 } else { exit 0 }
