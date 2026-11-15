@echo off
setlocal EnableExtensions
chcp 65001 >nul
set "ROOT=%~dp0"
set "SDK=%ROOT%..\.tools\dotnet\dotnet.exe"
if not exist "%SDK%" set "SDK=dotnet"
set "ISCC=%ROOT%..\.tools\InnoSetup7\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%ProgramFiles%\Inno Setup 7\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" (
  echo Inno Setup compiler not found. Install Inno Setup or place it in ..\.tools\InnoSetup7.
  exit /b 1
)
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "DOTNET_NOLOGO=1"
"%SDK%" clean "%ROOT%src\ClipboardWB\ClipboardWB.csproj" -c Release -r win-x64 -v:q
if errorlevel 1 exit /b 1
"%SDK%" publish "%ROOT%src\ClipboardWB\ClipboardWB.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:EnableCompressionInSingleFile=true -o "%ROOT%dist-installer"
if errorlevel 1 exit /b 1
"%ISCC%" "%ROOT%installer.iss"
if errorlevel 1 exit /b 1
echo Installer built in %ROOT%release\
