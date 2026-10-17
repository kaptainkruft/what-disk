#!/usr/bin/env bash
# Publishes WhatDisk for macOS as a Native AOT application bundle for one architecture.
#
#   build/macos/publish.sh <arm64|x64> <output-directory>
#
# Environment (all optional; set by the release pipeline):
#   VERSION_PREFIX   e.g. 0.1.3         (default 0.0.0)
#   VERSION_SUFFIX   e.g. alpha; empty for a final release (default: unset, giving "dev" locally)
#   BUILD_TIMESTAMP  e.g. 2026-10-17T04:12:00Z (default: the time of the build)
#
# Produces <output-directory>/WhatDisk.app.
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
arch="${1:?usage: publish.sh <arm64|x64> <output-directory>}"
output="${2:?usage: publish.sh <arm64|x64> <output-directory>}"
case "$arch" in
  arm64 | x64) ;;
  *) echo "error: architecture must be arm64 or x64 (got '$arch')" >&2; exit 2 ;;
esac

mkdir -p "$output"
output="$(cd "$output" && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

properties=(-c Release -r "osx-$arch" "-p:VersionPrefix=${VERSION_PREFIX:-0.0.0}")
if [[ -n "${VERSION_SUFFIX+set}" ]]; then properties+=("-p:VersionSuffix=${VERSION_SUFFIX}"); fi
if [[ -n "${BUILD_TIMESTAMP:-}" ]]; then properties+=("-p:BuildTimestamp=${BUILD_TIMESTAMP}"); fi

dotnet publish "$root/src/WhatDisk.MacOS/WhatDisk.MacOS.csproj" -o "$work" "${properties[@]}"

app="$output/WhatDisk.app"
rm -rf "$app"
cp -R "$work/WhatDisk.app" "$app"
codesign --verify --deep --strict "$app"

expected="$([[ "$arch" == x64 ]] && echo x86_64 || echo arm64)"
actual="$(lipo -archs "$app/Contents/MacOS/WhatDisk")"
if [[ "$actual" != "$expected" ]]; then
  echo "error: expected a $expected executable, got $actual" >&2
  exit 1
fi

echo "Published $app ($actual)"
