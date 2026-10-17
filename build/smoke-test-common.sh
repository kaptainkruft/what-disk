#!/usr/bin/env bash
# Runs WhatDisk with a folder to scan and --exit-when-done, failing if it does not finish in time, exits with an
# error, or prints no scan summary. Shared by the platforms' smoke-test.sh scripts.
#
#   build/smoke-test-common.sh <executable> <folder-to-scan> [timeout-seconds]
set -euo pipefail

executable="${1:?usage: smoke-test-common.sh <executable> <folder> [timeout-seconds]}"
folder="${2:?usage: smoke-test-common.sh <executable> <folder> [timeout-seconds]}"
limit="${3:-120}"
log="$(mktemp)"
trap 'rm -f "$log"' EXIT

"$executable" "$folder" --exit-when-done >"$log" 2>&1 &
pid=$!
for ((elapsed = 0; elapsed < limit; elapsed++)); do
  kill -0 "$pid" 2>/dev/null || break
  sleep 1
done
if kill -0 "$pid" 2>/dev/null; then
  kill "$pid" 2>/dev/null || true
  echo "error: WhatDisk did not finish within $limit seconds" >&2
  cat "$log" >&2
  exit 1
fi

status=0
wait "$pid" || status=$?
cat "$log"
if [[ $status -ne 0 ]]; then
  echo "error: WhatDisk exited with status $status" >&2
  exit 1
fi
grep -q "^Scanned " "$log" || { echo "error: no scan summary was printed" >&2; exit 1; }
echo "Smoke test passed."
