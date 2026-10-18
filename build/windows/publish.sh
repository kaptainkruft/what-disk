#!/usr/bin/env bash
# Publishes WhatDisk for Windows as a Native AOT executable for x64 (the only Windows architecture released). Runs in
# Git Bash on Windows: Native AOT cannot compile for another operating system.
#
#   build/windows/publish.sh x64 <output-directory>
#
# Environment (all optional; set by the release pipeline):
#   VERSION_PREFIX   e.g. 0.1.3         (default 0.0.0)
#   VERSION_SUFFIX   e.g. alpha; empty for a final release (default: unset, giving "dev" locally)
#   BUILD_TIMESTAMP  e.g. 2026-10-17T04:12:00Z (default: the time of the build)
#
# Produces <output-directory>/WhatDisk/: WhatDisk.exe and the native libraries it loads (Skia, HarfBuzz, ANGLE).
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
arch="${1:?usage: publish.sh x64 <output-directory>}"
output="${2:?usage: publish.sh x64 <output-directory>}"
case "$arch" in
  x64) ;;
  *) echo "error: the only Windows architecture is x64 (got '$arch')" >&2; exit 2 ;;
esac

mkdir -p "$output"
output="$(cd "$output" && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

properties=(-c Release -r "win-$arch" "-p:VersionPrefix=${VERSION_PREFIX:-0.0.0}")
if [[ -n "${VERSION_SUFFIX+set}" ]]; then properties+=("-p:VersionSuffix=${VERSION_SUFFIX}"); fi
if [[ -n "${BUILD_TIMESTAMP:-}" ]]; then properties+=("-p:BuildTimestamp=${BUILD_TIMESTAMP}"); fi

dotnet publish "$root/src/WhatDisk.Windows/WhatDisk.Windows.csproj" -o "$work" "${properties[@]}"

app="$output/WhatDisk"
rm -rf "$app"
mkdir -p "$app"
# Everything the app needs, without debug symbols and documentation files.
find "$work" -maxdepth 1 -type f ! -name '*.pdb' ! -name '*.xml' -exec cp {} "$app/" \;
[[ -f "$app/WhatDisk.exe" ]] || { echo "error: $app/WhatDisk.exe was not produced" >&2; exit 1; }

"$root/build/windows/pe-machine.sh" "$app/WhatDisk.exe" "$arch"
echo "Published $app ($arch):"
ls -l "$app"
