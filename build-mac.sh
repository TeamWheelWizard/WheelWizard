#!/usr/bin/env bash
# https://avaloniaui.net/blog/the-definitive-guide-to-building-and-deploying-avalonia-applications-for-macos

PROJECT_NAME="$1"
OUTPUT_DIR="./WheelWizard/bin/Release/compiled"

build_for_arch() {
    local arch=$1
    echo "Building for $arch..."
    dotnet publish -r osx-$arch -c Release /p:PublishSingleFile=true \
        /p:IncludeAllContentForSelfExtract=true /p:IncludeNativeLibrariesForSelfExtract=true \
        /p:EnableCompressionInSingleFile=true /p:PublishReadyToRun=true \
        -p:UseAppHost=true --self-contained true -o "$OUTPUT_DIR/osx-$arch"
}

build_for_arch "x64"
build_for_arch "arm64"

echo "Creating universal binary..."
mkdir -p "$OUTPUT_DIR/Universal"
lipo -create \
    "$OUTPUT_DIR/osx-x64/WheelWizard" \
    "$OUTPUT_DIR/osx-arm64/WheelWizard" \
    -output "$OUTPUT_DIR/Universal/WheelWizard"

echo "Universal binary created"

