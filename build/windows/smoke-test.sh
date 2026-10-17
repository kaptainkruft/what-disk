#!/usr/bin/env bash
# Starts the published app, scans a folder and checks that it exits successfully with a scan summary.
#
#   build/windows/smoke-test.sh <directory-containing-WhatDisk>
set -euo pipefail

app="${1:?usage: smoke-test.sh <app-directory>}/WhatDisk/WhatDisk.exe"
[[ -f "$app" ]] || { echo "error: $app not found" >&2; exit 1; }
exec "$(dirname "$0")/../smoke-test-common.sh" "$app" "$(dirname "$app")"
