@echo off
setlocal
cd /d "%~dp0.."

echo ============================================================
echo  Cloudict installer build
echo ============================================================
echo.
echo [1/2] Publishing the app (Release, self-contained, win-x64)...
dotnet publish "src\Cloudict\Cloudict.csproj" -c Release -r win-x64 --self-contained true
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Publish failed with error code %ERRORLEVEL%.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo [2/2] Compiling the installer with Inno Setup...
set "ISCC="
if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"

if "%ISCC%"=="" (
    echo.
    echo Inno Setup 6 was not found. Install it from https://jrsoftware.org/isdl.php
    echo then re-run this script ^(or run: iscc installer\Cloudict.iss^).
    pause
    exit /b 1
)

"%ISCC%" "installer\Cloudict.iss"
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Installer compilation failed with error code %ERRORLEVEL%.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo Done. Installer is in:  installer\Output\
echo.
pause
