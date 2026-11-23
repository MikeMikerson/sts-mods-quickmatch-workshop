#!/usr/bin/env bash
# Usage: scripts/desktop-kit.sh <commit>
# STS2_STABLE_DATA and STS2_BETA_DATA can override the game data paths.
set -euo pipefail
commit=${1:?usage: scripts/desktop-kit.sh <commit>}
root="$(cd "$(dirname "$0")/.." && pwd)"
export STS2_STABLE_DATA="${STS2_STABLE_DATA:-$root/.work/depots/linux-public/data_sts2_linuxbsd_x86_64}"
work="$root/.work/kit-build"
mkdir -p "$work"
config="$root/.work/QuickMatch.cfg"
export TZ=UTC LC_ALL=C
for copy in a b; do
  rm -rf "${work:?}/$copy"
  mkdir "$work/$copy"
  git -C "$root" archive "$commit" global.json LICENSE README.md mod/build.sh mod/QuickMatch mod/QuickMatch.SteamKit mod/QuickMatchLoader | tar -x -C "$work/$copy" || exit 1
  # Keeps an .editorconfig above the work folder from changing the build, so both copies build the same.
  printf 'root = true\n' > "$work/$copy/.editorconfig"
  "$work/$copy/mod/build.sh" > "$work/build-$copy.log" 2>&1 \
    || { echo "build $copy failed: $work/build-$copy.log and $work/$copy/mod/out/logs"; exit 1; }
  (
    cd "$work/$copy/mod/out"
    if [[ -f "$config" ]]; then cp "$config" QuickMatch/; fi
    # ZIP records file modification times, including those of fresh compiler outputs.
    touch -t 200001010000 QuickMatch/*
    zip -qX "$work/$copy/kit.zip" QuickMatch/*
  )
done
cmp "$work/a/kit.zip" "$work/b/kit.zip"
built=mod/out/QuickMatch
version=$(python3 -c "import json, sys; print(json.load(open(sys.argv[1]))['version'].lstrip('v'))" "$work/a/$built/QuickMatch.json")
kit="$root/.work/desktop-kit-$version/QuickMatch"
rm -rf "$kit"
mkdir -p "$kit"
cp "$work/a/$built"/* "$kit/"
zip="$root/.work/desktop-kit-$version/QuickMatch-$version.zip"
cp "$work/a/kit.zip" "$zip"
( cd "$kit" && shasum -a 256 *.dll )
echo "manifest $(shasum -a 256 "$kit/QuickMatch.json" | cut -d' ' -f1)"
echo "kit $kit"
echo "release $zip"
