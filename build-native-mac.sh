#!/usr/bin/env bash
# Build WheelWizard with the sibling Apple Silicon WiiCompiled backend bundled.
set -euo pipefail
root=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
recomp_repo="${WIICOMPILED_REPO:-$root/../Wiicompiled}"
tools_root="${WIICOMPILED_TOOLS_ROOT:-/Applications/WiiCompiled Setup.app/Contents/Resources/tools}"
[[ $(uname -m) == arm64 ]] || { echo 'Use a native Apple Silicon terminal.' >&2; exit 1; }
[[ -d "$tools_root" ]] || { echo 'Install WiiCompiled Setup.pkg first, or set WIICOMPILED_TOOLS_ROOT to its native tools directory.' >&2; exit 1; }
setup="$root/release/WiiCompiled-Setup-macos-arm64.run"
"$recomp_repo/Launcher/macos/build-wheelwizard-setup.command" --tools-root "$tools_root" --output "$setup"
BUILD_ARCH=arm64 RECOMP_SETUP="$setup" "$root/build-mac.sh"
