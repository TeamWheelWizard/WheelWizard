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
#   RECOMP_SETUP - Optional game-code-free WiiCompiled-Setup-macos-arm64.run to bundle
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

# ---- Detect host architecture ----
HOST_ARCH="$(uname -m)"
case "$HOST_ARCH" in
    arm64|aarch64) HOST_BUILD_ARCH="arm64" ;;
    x86_64|amd64)  HOST_BUILD_ARCH="x64" ;;
    *)             HOST_BUILD_ARCH="x64" ;;
esac

# Use BUILD_ARCH if set, otherwise default to host architecture
BUILD_ARCH="${BUILD_ARCH:-$HOST_BUILD_ARCH}"
RID="osx-$BUILD_ARCH"
OUTPUT_DIR="${OUTPUT_DIR:-$DEFAULT_OUTPUT}"

echo "[INFO] Host architecture: $HOST_BUILD_ARCH"
echo "[INFO] Building for RID: $RID (arch: $BUILD_ARCH)"
echo "[INFO] Output: $OUTPUT_DIR"

# If cross-compiling (e.g., building x64 on arm64 host), set the appropriate architecture flag
if [ "$BUILD_ARCH" != "$HOST_BUILD_ARCH" ]; then
    echo "[INFO] Cross-compiling: $HOST_BUILD_ARCH -> $BUILD_ARCH"
    case "$BUILD_ARCH" in
        x64)  ARCH_FLAG="-arch x86_64" ;;
        arm64) ARCH_FLAG="-arch arm64" ;;
    esac
else
    ARCH_FLAG=""
fi

mkdir -p "$OUTPUT_DIR"

# =============================================================================
# STEP 1: Build
# =============================================================================
if [ "${SKIP_BUILD:-}" != "true" ]; then
    echo "[INFO] Building WheelWizard for $RID..."
    # Publish the project, not the solution: WheelWizard.sln only has
    # Debug|Any CPU and Release|Any CPU, so -c Release-macOS fails against the .sln.
    dotnet publish "$WW_DIR/WheelWizard/WheelWizard.csproj" -r "$RID" -c Release-macOS \
        /p:PublishSingleFile=true \
        /p:IncludeAllContentForSelfExtract=true \
        /p:IncludeNativeLibrariesForSelfExtract=true \
        /p:EnableCompressionInSingleFile=true \
        /p:PublishReadyToRun=true \
        -p:UseAppHost=true \
        --self-contained true \
        -o "$OUTPUT_DIR/compiled/$RID"
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

# Bundle the native installer so local builds are usable before a GitHub release exists.
if [ -n "${RECOMP_SETUP:-}" ]; then
    [ "$BUILD_ARCH" = arm64 ] || { echo "RECOMP_SETUP requires BUILD_ARCH=arm64" >&2; exit 1; }
    [ -s "$RECOMP_SETUP" ] || { echo "RECOMP_SETUP does not exist: $RECOMP_SETUP" >&2; exit 1; }
    mkdir -p "$APP_BUNDLE/Contents/Resources"
    cp "$RECOMP_SETUP" "$APP_BUNDLE/Contents/Resources/WiiCompiled-Setup-macos-arm64.run"
fi

# Set the bundle version from the release tag (e.g. "v2.5.1" -> "2.5.1"),
# falling back to the version declared in the project file.
if [ -n "${GITHUB_REF_NAME:-}" ]; then
    BUNDLE_VERSION="${GITHUB_REF_NAME#v}"
else
    BUNDLE_VERSION="$(sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' "$WW_DIR/WheelWizard/WheelWizard.csproj" | head -n1)"
fi
if [ -z "$BUNDLE_VERSION" ]; then
    echo "[WARN] Could not determine version; leaving Info.plist version unchanged."
else
    echo "[INFO] Setting bundle version to $BUNDLE_VERSION"
    /usr/libexec/PlistBuddy -c "Set :CFBundleVersion $BUNDLE_VERSION" "$APP_BUNDLE/Contents/Info.plist"
    /usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $BUNDLE_VERSION" "$APP_BUNDLE/Contents/Info.plist"
fi

chmod +x "$APP_BUNDLE/Contents/MacOS/WheelWizard"
codesign --force --deep --sign - "$APP_BUNDLE"
codesign --verify --deep --strict "$APP_BUNDLE"
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
