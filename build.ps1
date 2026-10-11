param([switch]$Test, [string]$OutputDirectory = 'bin-centered-v4')
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x x64 compiler is required.' }
$output = if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $PSScriptRoot $OutputDirectory }
New-Item -ItemType Directory -Force -Path $output | Out-Null
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /warn:4 /codepage:65001 "/out:$output\WindowTabManager.exe" "/win32manifest:$PSScriptRoot\app.manifest" "/win32icon:$PSScriptRoot\assets\WindowTabManager.ico" "/resource:$PSScriptRoot\assets\WindowTabManager.ico,WindowTabManager.AppIcon.ico" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Runtime.Serialization.dll /reference:System.Xml.dll $sources
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Write-Output "Built $output\WindowTabManager.exe"
if ($Test) {
    $report = Join-Path $output 'self-test.txt'
    $run = Start-Process -FilePath "$output\WindowTabManager.exe" -ArgumentList @('--self-test', ('"' + $report + '"')) -WindowStyle Hidden -Wait -PassThru
    Get-Content -LiteralPath $report
    if ($run.ExitCode -ne 0) { throw 'Self-tests failed.' }
}
