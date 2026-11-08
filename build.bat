@echo off
setlocal EnableExtensions
chcp 65001 >nul

set "ROOT=%~dp0"
set "SDK=%ROOT%..\.tools\dotnet\dotnet.exe"
if not exist "%SDK%" set "SDK=dotnet"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "DOTNET_NOLOGO=1"

if /I "%~1"=="fx" goto FX

echo [ClipboardWB] Publishing self-contained build to dist\ ...
"%SDK%" publish "%ROOT%src\ClipboardWB\ClipboardWB.csproj" ^
  -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:DebugType=None ^
  -p:EnableCompressionInSingleFile=true -o "%ROOT%dist"
if errorlevel 1 goto FAIL
echo.
echo Done: %ROOT%dist\ClipboardWB.exe
echo Keep the whole dist\ folder together (WPF native DLLs + app.ico live there).
goto END

:FX
echo [ClipboardWB] Publishing lightweight build to dist-fx\ ...
"%SDK%" publish "%ROOT%src\ClipboardWB\ClipboardWB.csproj" ^
  -c Release -r win-x64 --self-contained false ^
  -p:PublishSingleFile=true -p:DebugType=None -o "%ROOT%dist-fx"
if errorlevel 1 goto FAIL
echo.
echo Done: %ROOT%dist-fx\ClipboardWB.exe
echo This build needs the .NET 10 runtime; "run.bat fx" points DOTNET_ROOT at ..\..\.tools\dotnet.
goto END

:FAIL
echo.
echo Build failed.
exit /b 1

:END
endlocal
