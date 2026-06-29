@echo off
setlocal
cd /d "%~dp0.."
echo Building Cloudict (Release, self-contained folder)...
echo.

dotnet publish "src\Cloudict\Cloudict.csproj" -c Release -r win-x64 --self-contained true

if %ERRORLEVEL% EQU 0 (
    echo.
    echo Build completed successfully!
    echo Output folder: src\Cloudict\bin\Release\net7.0-windows10.0.22621.0\win-x64\publish\
    echo Run "Cloudict.exe" from that folder.
    echo.
    pause
) else (
    echo.
    echo Build failed with error code %ERRORLEVEL%
    echo.
    pause
)
