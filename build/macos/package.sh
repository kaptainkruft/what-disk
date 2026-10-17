#!/usr/bin/env bash
# Packages WhatDisk.app into a disk image with the usual drag-to-Applications layout.
#
#   build/macos/package.sh <directory-containing-WhatDisk.app> <version> <file-name-suffix> <output-directory>
#
# Produces <output-directory>/WhatDisk-<version>-<file-name-suffix>.dmg (for example
# WhatDisk-0.1.3-alpha-macos-apple-silicon.dmg) and a .sha256 checksum beside it.
set -euo pipefail

usage="usage: package.sh <app-directory> <version> <file-name-suffix> <output-directory>"
source_directory="${1:?$usage}"
version="${2:?$usage}"
suffix="${3:?$usage}"
output="${4:?$usage}"
app="$source_directory/WhatDisk.app"
[[ -d "$app" ]] || { echo "error: $app not found" >&2; exit 1; }

mkdir -p "$output"
staging="$(mktemp -d)"
trap 'rm -rf "$staging"' EXIT
cp -R "$app" "$staging/"
ln -s /Applications "$staging/Applications"

dmg="$output/WhatDisk-$version-$suffix.dmg"
rm -f "$dmg"
hdiutil create -volname "WhatDisk $version" -srcfolder "$staging" -fs HFS+ -format UDZO -imagekey zlib-level=9 -ov "$dmg"
hdiutil verify "$dmg"
(cd "$output" && shasum -a 256 "$(basename "$dmg")" >"$(basename "$dmg").sha256")
echo "Packaged $dmg"
