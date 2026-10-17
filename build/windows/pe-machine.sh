#!/usr/bin/env bash
# Checks the architecture of a Windows executable by reading its PE header.
#
#   build/windows/pe-machine.sh <file.exe> [x64|arm64]
#
# Prints the architecture (x64, arm64, x86 or the raw machine number). With an expected architecture, fails unless the
# file matches it.
set -euo pipefail

file="${1:?usage: pe-machine.sh <file.exe> [x64|arm64]}"
expected="${2:-}"

# The PE header's offset is at 0x3C; the machine field follows the 4-byte "PE\0\0" signature.
offset="$(od -An -tu4 -j 60 -N 4 "$file" | tr -d ' ')"
machine="$(od -An -tx2 -j $((offset + 4)) -N 2 "$file" | tr -d ' ')"
case "$machine" in
  8664) actual=x64 ;;
  aa64) actual=arm64 ;;
  014c) actual=x86 ;;
  *) actual="machine-0x$machine" ;;
esac

echo "$actual"
if [[ -n "$expected" && "$actual" != "$expected" ]]; then
  echo "error: expected an executable for $expected, got $actual ($file)" >&2
  exit 1
fi
