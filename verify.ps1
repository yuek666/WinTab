param([switch]$Native, [switch]$UI, [switch]$Performance, [switch]$Preview)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build.ps1') -Test
$verifyOutput = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Force -Path $verifyOutput | Out-Null
function Invoke-Verification([string]$Mode, [string]$Target, [string]$Report) {
    $run = Start-Process -FilePath (Join-Path $PSScriptRoot 'bin\WindowTabManager.exe') -ArgumentList @($Mode, ('"' + $Target + '"')) -WindowStyle Hidden -Wait -PassThru
    Get-Content -LiteralPath $Report
    if ($run.ExitCode -ne 0) { throw "$Mode failed. See $Report" }
}
if ($Native) {
    $nativeReport = Join-Path $verifyOutput 'native-test.txt'
    Invoke-Verification '--native-test' $nativeReport $nativeReport
}
if ($UI) { Invoke-Verification '--ui-test' $verifyOutput (Join-Path $verifyOutput 'ui-test.txt') }
if ($Preview) { Invoke-Verification '--preview-test' $verifyOutput (Join-Path $verifyOutput 'preview-test.txt') }
if ($Performance) { Invoke-Verification '--perf-test' $verifyOutput (Join-Path $verifyOutput 'performance.txt') }
Write-Output 'Verification finished. BLOCKED lines remain unverified; zero failures does not imply every Windows interaction passed.'
