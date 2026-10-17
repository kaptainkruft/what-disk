#!/usr/bin/env bash
# Tests next-version.sh against throwaway repositories. Run: .github/scripts/test-next-version.sh
set -euo pipefail

script="$(cd "$(dirname "$0")" && pwd)/next-version.sh"
failures=0

# expect <description> <expected version or "error"> <tags (space separated)> -- <arguments...>
expect() {
  local description="$1" expected="$2" tags="$3"
  shift 4
  local repo actual
  repo="$(mktemp -d)"
  git -C "$repo" init --quiet
  git -C "$repo" -c user.name=test -c user.email=test@example.com commit --quiet --allow-empty -m init
  for tag in $tags; do git -C "$repo" tag "$tag"; done
  if actual="$(cd "$repo" && "$script" "$@" 2>/dev/null | sed -n 's/^version=//p')" && [[ -n "$actual" ]]; then :; else actual="error"; fi
  rm -rf "$repo"
  if [[ "$actual" == "$expected" ]]; then
    echo "ok   - $description"
  else
    echo "FAIL - $description: expected $expected, got $actual"
    failures=$((failures + 1))
  fi
}

expect "first version"                     "0.1.0"        ""                                   -- patch
expect "first prerelease"                  "0.1.0-alpha"  ""                                   -- patch alpha
expect "patch follows the highest tag"     "0.1.3-alpha"  "v0.1.0-alpha v0.1.2-alpha v0.1.1-alpha" -- patch alpha
expect "numeric, not lexical, ordering"    "0.10.1"       "v0.9.5 v0.10.0 v0.2.30"            -- patch
expect "minor resets patch"                "0.2.0-beta"   "v0.1.7-alpha"                       -- minor beta
expect "major resets minor and patch"      "1.0.0"        "v0.4.2-rc.1"                        -- major
expect "stable after prerelease"           "0.1.3"        "v0.1.2-alpha"                       -- patch
expect "ignores tags that are not versions" "0.1.1"       "v0.1.0 release-1 v1.2 vnext"        -- patch
expect "dotted prerelease labels"          "0.1.1-rc.1"   "v0.1.0"                             -- patch rc.1
expect "rejects an invalid bump"           "error"        ""                                   -- huge
expect "rejects an invalid label"          "error"        ""                                   -- patch "bad label!"

if ((failures > 0)); then
  echo "$failures test(s) failed"
  exit 1
fi
echo "all tests passed"
