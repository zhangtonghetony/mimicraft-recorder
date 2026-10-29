@echo off
setlocal
set "EV_ROOT=%~dp0.."
set "DOTNET_ROOT=%EV_ROOT%\.tools\dotnet"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "DOTNET_NOLOGO=1"
if not exist "%DOTNET_ROOT%\dotnet.exe" (
  echo SDK missing. Run python scripts\install-tools.py first.
  pause
  exit /b 1
)
pushd "%EV_ROOT%"
"%DOTNET_ROOT%\dotnet.exe" run --project "src\Mimicraft.Recorder\Mimicraft.Recorder.csproj" -c Debug
set "EV_EXIT=%ERRORLEVEL%"
popd
if not "%EV_EXIT%"=="0" pause
