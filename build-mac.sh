#!/usr/bin/env bash
# Local macOS build. Produces a .app bundle via macos/release-macos.sh.
# https://avaloniaui.net/blog/the-definitive-guide-to-building-and-deploying-avalonia-applications-for-macos
set -euo pipefail
exec "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/macos/release-macos.sh" "$@"
