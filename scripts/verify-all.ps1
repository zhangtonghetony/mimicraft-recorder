param([switch]$IncludeDevices,[switch]$IncludeStress,[switch]$SkipDesktop,[string]$Results='')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$env:DOTNET_ROOT=Join-Path $projectRoot '.tools/dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$sdk=Join-Path $env:DOTNET_ROOT 'dotnet.exe'
if(!$Results){$Results=Join-Path $projectRoot ('artifacts/verification-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))}
$env:MIMICRAFT_EV_ROOT=$projectRoot
$env:MIMICRAFT_NETWORK_TESTS='1'
$env:MIMICRAFT_DESKTOP_TESTS=if($SkipDesktop){'0'}else{'1'}
$env:MIMICRAFT_AUDIO_TESTS=if($IncludeDevices){'1'}else{'0'}
$env:MIMICRAFT_CAMERA_TESTS=if($IncludeDevices){'1'}else{'0'}
$env:MIMICRAFT_STRESS_TESTS=if($IncludeStress -and $IncludeDevices -and !$SkipDesktop){'1'}else{'0'}
& $sdk build (Join-Path $projectRoot 'Mimicraft.Recorder.slnx') --no-restore -v minimal
if($LASTEXITCODE){exit $LASTEXITCODE}
# Serialize projects so backend and desktop tests never fight over physical devices.
foreach($test in @('Unit','MediaIntegration','DesktopUI')){
    & $sdk test (Join-Path $projectRoot "tests/$test/$test.csproj") --no-build --no-restore --logger "trx;LogFileName=$test.trx" --results-directory $Results --blame-hang-timeout 120s --blame-hang-dump-type none
    if($LASTEXITCODE){exit $LASTEXITCODE}
}
Write-Output "Verification completed: $Results"
