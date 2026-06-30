#!/usr/bin/env bash
# =============================================================================
# WheelWizard macOS Build Script
# =============================================================================
# Builds WheelWizard for macOS and creates a .app bundle.
# Designed to run on macOS CI runners (GitHub Actions).
#
# Environment variables:
#   BUILD_ARCH   - "arm64" or "x64" (default: auto-detect)
#   SKIP_BUILD   - Set to "true" to skip dotnet build
#   OUTPUT_DIR   - Output directory (default: ./release)
#
# No codesigning, no notarization, no DMG creation.
# DMG is created by the GitHub Actions workflow using create-dmg action.
# =============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MACOS_DIR="$SCRIPT_DIR"
WW_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
MAC_DIRS="$MACOS_DIR/MacAppTemplate"
DEFAULT_OUTPUT="$WW_DIR/release"

# ---- Detect architecture ----
ARCH="$(uname -m)"
case "$ARCH" in
    arm64|aarch64) DEFAULT_BUILD_ARCH="arm64" ;;
    x86_64|amd64)  DEFAULT_BUILD_ARCH="x64" ;;
    *)             DEFAULT_BUILD_ARCH="x64" ;;
esac

BUILD_ARCH="${BUILD_ARCH:-$DEFAULT_BUILD_ARCH}"
RID="osx-$BUILD_ARCH"
OUTPUT_DIR="${OUTPUT_DIR:-$DEFAULT_OUTPUT}"

echo "[INFO] Building for RID: $RID (arch: $BUILD_ARCH)"
echo "[INFO] Output: $OUTPUT_DIR"

mkdir -p "$OUTPUT_DIR"

# =============================================================================
# STEP 1: Build
# =============================================================================
if [ "${SKIP_BUILD:-}" != "true" ]; then
    echo "[INFO] Building WheelWizard for $RID..."
    cd "$WW_DIR"
    dotnet publish -r "$RID" -c Release \
        /p:PublishSingleFile=true \
        /p:IncludeAllContentForSelfExtract=true \
        /p:IncludeNativeLibrariesForSelfExtract=true \
        /p:EnableCompressionInSingleFile=true \
        /p:PublishReadyToRun=true \
        -p:UseAppHost=true \
        --self-contained true \
        -o "$OUTPUT_DIR/compiled/$RID"
    cd "$SCRIPT_DIR"
else
    echo "[INFO] Skipping build (SKIP_BUILD=true)"
fi

EXE_DIR="$OUTPUT_DIR/compiled/$RID"
if [ ! -f "$EXE_DIR/WheelWizard" ]; then
    echo "[ERROR] Built binary not found at $EXE_DIR/WheelWizard"
    echo "        Make sure the build step succeeded or set SKIP_BUILD=true if pre-built."
    exit 1
fi

# =============================================================================
# STEP 2: Create .app bundle
# =============================================================================
APP_BUNDLE="$OUTPUT_DIR/WheelWizard.app"
echo "[INFO] Creating .app bundle at $APP_BUNDLE"

# Clean any previous bundle
rm -rf "$APP_BUNDLE"

# Copy the .app template
if [ -d "$MAC_DIRS" ]; then
    cp -R "$MAC_DIRS" "$APP_BUNDLE"
else
    echo "[ERROR] Template directory not found: $MAC_DIRS"
    echo "        The MacAppTemplate directory is required for the .app bundle structure."
    exit 1
fi

# Place the binary
mkdir -p "$APP_BUNDLE/Contents/MacOS"
cp "$EXE_DIR/WheelWizard" "$APP_BUNDLE/Contents/MacOS/WheelWizard"

# Copy the icon if present
if [ -f "$MAC_DIRS/Contents/Resources/WheelWizard.icns" ]; then
    mkdir -p "$APP_BUNDLE/Contents/Resources"
    cp "$MAC_DIRS/Contents/Resources/WheelWizard.icns" "$APP_BUNDLE/Contents/Resources/WheelWizard.icns"
fi

echo "[INFO] .app bundle created successfully"

# =============================================================================
# STEP 3: Cleanup compiled artifacts
# =============================================================================
echo "[INFO] Cleaning up intermediate build artifacts..."
rm -rf "$OUTPUT_DIR/compiled"

echo ""
echo "============================================"
echo "  ✅ Build complete!"
echo "  .app bundle: $APP_BUNDLE"
echo "============================================"
