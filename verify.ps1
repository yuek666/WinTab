param([switch]$Native, [switch]$UI, [switch]$Performance, [switch]$Preview, [switch]$Monitors, [string]$OutputDirectory = 'bin-centered-v4')
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build.ps1') -Test -OutputDirectory $OutputDirectory
$verifyOutput = Join-Path $PSScriptRoot 'artifacts\centered-v4'
$verifyBinaryDirectory = if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $PSScriptRoot $OutputDirectory }
New-Item -ItemType Directory -Force -Path $verifyOutput | Out-Null
function Invoke-Verification([string]$Mode, [string]$Target, [string]$Report) {
    $run = Start-Process -FilePath (Join-Path $verifyBinaryDirectory 'WindowTabManager.exe') -ArgumentList @($Mode, ('"' + $Target + '"')) -WindowStyle Hidden -Wait -PassThru
    Get-Content -LiteralPath $Report
    if ($run.ExitCode -ne 0) { throw "$Mode failed. See $Report" }
}
if ($Native) {
    $nativeReport = Join-Path $verifyOutput 'native-test.txt'
    Invoke-Verification '--native-test' $nativeReport $nativeReport
}
if ($UI) { Invoke-Verification '--ui-test' $verifyOutput (Join-Path $verifyOutput 'ui-test.txt') }
if ($Preview) { Invoke-Verification '--preview-test' $verifyOutput (Join-Path $verifyOutput 'preview-test.txt') }
if ($Monitors) { Invoke-Verification '--monitor-test' $verifyOutput (Join-Path $verifyOutput 'monitor-test.txt') }
if ($Performance) { Invoke-Verification '--perf-test' $verifyOutput (Join-Path $verifyOutput 'performance.txt') }
Write-Output 'Verification finished. BLOCKED lines remain unverified; zero failures does not imply every Windows interaction passed.'
