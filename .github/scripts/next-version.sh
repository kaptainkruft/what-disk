#!/usr/bin/env bash
# Computes the next semantic version from the repository's tags, which are the source of truth.
#
#   next-version.sh [patch|minor|major] [prerelease-label]
#
# Tags look like vMAJOR.MINOR.PATCH or vMAJOR.MINOR.PATCH-LABEL. The highest MAJOR.MINOR.PATCH among them is bumped
# (patch by default; minor and major reset the lower parts), so every build gets a new number. The optional label
# (for example "alpha") marks a prerelease. With no tags at all the first version is 0.1.0.
#
# Prints key=value lines (and appends them to $GITHUB_OUTPUT when set):
#   version=0.1.3-alpha  prefix=0.1.3  suffix=alpha  tag=v0.1.3-alpha  prerelease=true
set -euo pipefail

bump="${1:-patch}"
label="${2:-}"

case "$bump" in
  patch | minor | major) ;;
  *) echo "error: bump must be patch, minor or major (got '$bump')" >&2; exit 2 ;;
esac

if [[ -n "$label" && ! "$label" =~ ^[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*$ ]]; then
  echo "error: '$label' is not a valid semantic-version prerelease label" >&2
  exit 2
fi

latest="$(git tag --list 'v*' \
  | sed -nE 's/^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z.-]+)?$/\1 \2 \3/p' \
  | sort -k1,1n -k2,2n -k3,3n \
  | tail -n 1)"

if [[ -z "$latest" ]]; then
  major=0 minor=1 patch=0
else
  read -r major minor patch <<<"$latest"
  case "$bump" in
    patch) patch=$((patch + 1)) ;;
    minor) minor=$((minor + 1)); patch=0 ;;
    major) major=$((major + 1)); minor=0; patch=0 ;;
  esac
fi

prefix="$major.$minor.$patch"
version="$prefix${label:+-$label}"
tag="v$version"

if git rev-parse --quiet --verify "refs/tags/$tag" >/dev/null; then
  echo "error: tag $tag already exists" >&2
  exit 1
fi

output="version=$version
prefix=$prefix
suffix=$label
tag=$tag
prerelease=$([[ -n "$label" ]] && echo true || echo false)"

echo "$output"
if [[ -n "${GITHUB_OUTPUT:-}" ]]; then
  echo "$output" >>"$GITHUB_OUTPUT"
fi
