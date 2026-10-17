#!/usr/bin/env bash
# Packages the published WhatDisk folder for Windows: a portable zip and a per-user installer (Inno Setup), each with
# a .sha256 checksum. Runs in Git Bash on Windows.
#
#   build/windows/package.sh <directory-containing-WhatDisk> <version> <file-name-suffix> <output-directory>
#
# Produces, for example:
#   WhatDisk-0.1.3-alpha-windows-x64.zip         the app folder; unzip anywhere and run WhatDisk.exe
#   WhatDisk-0.1.3-alpha-windows-x64-setup.exe   installs for the current user (no administrator rights), with a
#                                                Start menu entry and an uninstaller
set -euo pipefail

usage="usage: package.sh <app-directory> <version> <file-name-suffix> <output-directory>"
source_directory="${1:?$usage}"
version="${2:?$usage}"
suffix="${3:?$usage}"
output="${4:?$usage}"
root="$(cd "$(dirname "$0")/../.." && pwd)"
app="$source_directory/WhatDisk"
[[ -f "$app/WhatDisk.exe" ]] || { echo "error: $app/WhatDisk.exe not found" >&2; exit 1; }

mkdir -p "$output"
output="$(cd "$output" && pwd)"
app="$(cd "$app" && pwd)"
arch="$("$root/build/windows/pe-machine.sh" "$app/WhatDisk.exe")"
base="WhatDisk-$version-$suffix"

# Portable zip, with the WhatDisk folder at its top.
zip="$output/$base.zip"
rm -f "$zip"
pwsh -NoProfile -NonInteractive -Command \
  "Compress-Archive -Path '$(cygpath -w "$app")' -DestinationPath '$(cygpath -w "$zip")' -CompressionLevel Optimal"

# Installer. Inno Setup is preinstalled on GitHub's Windows images; install it with Chocolatey where it is not.
iscc="$(command -v iscc || command -v ISCC || true)"
for candidate in "/c/Program Files (x86)/Inno Setup 6/ISCC.exe" "/c/Program Files/Inno Setup 6/ISCC.exe"; do
  [[ -z "$iscc" && -f "$candidate" ]] && iscc="$candidate"
done
if [[ -z "$iscc" ]]; then
  choco install innosetup --no-progress -y >/dev/null
  iscc="/c/Program Files (x86)/Inno Setup 6/ISCC.exe"
fi

"$iscc" /Qp \
  "/DAppVersion=$version" \
  "/DNumericVersion=${version%%-*}" \
  "/DArch=$arch" \
  "/DSourceDir=$(cygpath -w "$app")" \
  "/DOutputDir=$(cygpath -w "$output")" \
  "/DOutputBaseName=$base-setup" \
  "$(cygpath -w "$root/build/windows/WhatDisk.iss")"

(cd "$output" && for file in "$base.zip" "$base-setup.exe"; do sha256sum "$file" >"$file.sha256"; done)
echo "Packaged:"
ls -l "$output"
