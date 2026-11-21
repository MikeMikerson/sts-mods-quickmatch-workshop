#!/usr/bin/env bash
# Builds QuickMatch twice from clean copies of one commit (mod/build.sh: the loader and a build per game version),
# checks that the two mod folders are the same, and puts one in .work/desktop-kit-<version>/QuickMatch, ready to copy
# into a game's mods folder, and the same folder zipped as the release file QuickMatch-<version>.zip next to it. The
# stable branch's build compiles against .work/depots/linux-public (or STS2_STABLE_DATA). It installs nothing.
# Build logs go to .work/kit-build/.
# Usage: scripts/desktop-kit.sh <commit>
set -uo pipefail
commit=${1:?usage: scripts/desktop-kit.sh <commit>}
root="$(cd "$(dirname "$0")/.." && pwd)"
export STS2_STABLE_DATA="${STS2_STABLE_DATA:-$root/.work/depots/linux-public/data_sts2_linuxbsd_x86_64}"
work="$root/.work/kit-build"
mkdir -p "$work"
for copy in a b; do
  rm -rf "${work:?}/$copy"
  mkdir "$work/$copy"
  git -C "$root" archive "$commit" global.json LICENSE README.md mod/build.sh mod/QuickMatch mod/QuickMatchLoader | tar -x -C "$work/$copy"
  # Keeps an .editorconfig above the work folder from changing the build, so both copies build the same.
  printf 'root = true\n' > "$work/$copy/.editorconfig"
  "$work/$copy/mod/build.sh" > "$work/build-$copy.log" 2>&1 \
    || { echo "build $copy failed: $work/build-$copy.log and $work/$copy/mod/out/logs"; exit 1; }
done
built=mod/out/QuickMatch
sums() { ( cd "$1" && shasum -a 256 *.dll ); }
first=$(sums "$work/a/$built")
[[ $first == "$(sums "$work/b/$built")" ]] || { echo "the two builds differ"; exit 1; }
version=$(python3 -c "import json, sys; print(json.load(open(sys.argv[1]))['version'].lstrip('v'))" "$work/a/$built/QuickMatch.json")
kit="$root/.work/desktop-kit-$version/QuickMatch"
rm -rf "$kit"
mkdir -p "$kit"
cp "$work/a/$built"/* "$kit/"
zip="$root/.work/desktop-kit-$version/QuickMatch-$version.zip"
rm -f "$zip"
( cd "$kit/.." && zip -qX -r "$zip" QuickMatch )
echo "$first"
echo "manifest $(shasum -a 256 "$kit/QuickMatch.json" | cut -d' ' -f1)"
echo "kit $kit"
echo "release $zip"
