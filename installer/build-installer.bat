@echo off
echo ============================================
echo  Click Run Installer Builder
echo ============================================
echo.

:: Step 1: Publish the app
echo [1/2] Publishing Click Run...
cd /d "%~dp0\.."
dotnet publish src/ClickRun/ClickRun.csproj -c Release
if %ERRORLEVEL% neq 0 (
    echo ERROR: dotnet publish failed.
    pause
    exit /b 1
)
echo       Published successfully.
echo.

:: Step 2: Build installer
echo [2/2] Building installer with Inno Setup...
set ISCC="C:\Users\kotam\AppData\Local\Programs\Inno Setup 6\ISCC.exe"
if not exist %ISCC% (
    echo ERROR: Inno Setup not found at %ISCC%
    echo        Download from: https://jrsoftware.org/isinfo.php
    pause
    exit /b 1
)

cd /d "%~dp0"
%ISCC% clickrun-setup.iss
if %ERRORLEVEL% neq 0 (
    echo ERROR: Inno Setup compilation failed.
    pause
    exit /b 1
)

echo.
echo ============================================
echo  Installer built successfully!
echo  Output: installer\Output\ClickRunSetup.exe
echo ============================================
pause
