@echo off
setlocal EnableExtensions
set "ROOT=%~dp0"

if /I "%~1"=="fx" goto RUN_FX
set "EXE=%ROOT%dist\ClipboardWB.exe"
goto LAUNCH

:RUN_FX
set "EXE=%ROOT%dist-fx\ClipboardWB.exe"
set "DOTNET_ROOT=%ROOT%..\.tools\dotnet"
goto LAUNCH

:LAUNCH
if not exist "%EXE%" (
  echo Cannot find: %EXE%
  echo Run build.bat first ^(or "build.bat fx" for the lightweight build^).
  exit /b 1
)
start "" "%EXE%"
endlocal
