@echo off
REM =============================================================================
REM WheelWizard macOS Build Script - Windows Batch Wrapper
REM =============================================================================
REM Builds WheelWizard for macOS and creates a .app bundle.
REM Requires: dotnet SDK
REM
REM Environment variables:
REM   BUILD_ARCH  - "arm64" or "x64" (default: x64 on Windows)
REM   SKIP_BUILD  - Set to "true" to skip dotnet build
REM   OUTPUT_DIR  - Output directory (default: .\release)
REM =============================================================================

setlocal enabledelayedexpansion

set "SCRIPT_DIR=%~dp0"
set "WW_DIR=%SCRIPT_DIR%"
set "MAC_DIRS=%SCRIPT_DIR%MacAppTemplate"
set "DEFAULT_OUTPUT=%SCRIPT_DIR%release"

REM ---- Detect architecture ----
if "%PROCESSOR_ARCHITECTURE%"=="ARM64" (
    set "DEFAULT_BUILD_ARCH=arm64"
) else (
    set "DEFAULT_BUILD_ARCH=x64"
)

if "%BUILD_ARCH%"=="" set "BUILD_ARCH=%DEFAULT_BUILD_ARCH%"
set "RID=osx-%BUILD_ARCH%"
if "%OUTPUT_DIR%"=="" set "OUTPUT_DIR=%DEFAULT_OUTPUT%"

echo [INFO] Building for %RID%, output: %OUTPUT_DIR%

if not exist "%OUTPUT_DIR%" mkdir "%OUTPUT_DIR%"

REM ---- Extract version from csproj ----
set "APP_VERSION=dev"
if exist "%WW_DIR%\WheelWizard\WheelWizard.csproj" (
    for /f "tokens=2 delims=<>" %%a in ('findstr "<Version>" "%WW_DIR%\WheelWizard\WheelWizard.csproj"') do set "APP_VERSION=%%a"
)
echo [INFO] App version: %APP_VERSION%

REM ---- Build ----
if /I not "%SKIP_BUILD%"=="true" (
    echo [INFO] Building WheelWizard for %RID%...
    cd /d "%WW_DIR%"
    dotnet publish -r "%RID%" -c Release ^
        /p:PublishSingleFile=true ^
        /p:IncludeAllContentForSelfExtract=true ^
        /p:IncludeNativeLibrariesForSelfExtract=true ^
        /p:EnableCompressionInSingleFile=true ^
        /p:PublishReadyToRun=true ^
        -p:UseAppHost=true ^
        --self-contained true ^
        -o "%OUTPUT_DIR%\compiled\%RID%"
    cd /d "%SCRIPT_DIR%"
) else (
    echo [INFO] Skipping build (SKIP_BUILD=true)
)

set "EXE_DIR=%OUTPUT_DIR%\compiled\%RID%"
if not exist "%EXE_DIR%\WheelWizard" (
    echo [ERROR] Built binary not found at %EXE_DIR%\WheelWizard
    exit /b 1
)

REM ---- Create .app bundle ----
set "APP_BUNDLE=%OUTPUT_DIR%\WheelWizard.app"
if exist "%APP_BUNDLE%" rmdir /s /q "%APP_BUNDLE%"

if exist "%MAC_DIRS%" (
    xcopy /E /I /Q "%MAC_DIRS%" "%APP_BUNDLE%"
) else (
    echo [ERROR] Template directory not found: %MAC_DIRS%
    exit /b 1
)

if not exist "%APP_BUNDLE%\Contents\MacOS" mkdir "%APP_BUNDLE%\Contents\MacOS"
copy /Y "%EXE_DIR%\WheelWizard" "%APP_BUNDLE%\Contents\MacOS\WheelWizard" >nul

if exist "%MAC_DIRS%\Contents\Resources\WheelWizard.icns" (
    if not exist "%APP_BUNDLE%\Contents\Resources" mkdir "%APP_BUNDLE%\Contents\Resources"
    copy /Y "%MAC_DIRS%\Contents\Resources\WheelWizard.icns" "%APP_BUNDLE%\Contents\Resources\WheelWizard.icns" >nul
)

echo [INFO] .app bundle created at %APP_BUNDLE%

REM ---- Cleanup ----
echo [INFO] Cleaning up intermediate build artifacts...
if exist "%OUTPUT_DIR%\compiled" rmdir /s /q "%OUTPUT_DIR%\compiled"

echo.
echo ============================================
echo   ✅ Build complete!
echo   .app bundle: %APP_BUNDLE%
echo ============================================
