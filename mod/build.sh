#!/bin/sh
# Builds Quick Match for every game version it supports and lays it out as the mod folder players install,
# out/QuickMatch: the loader QuickMatch.dll, a build per game version (QuickMatch-<version>.dll), the manifest, the
# LICENSE and the README. Each build compiles against its own game's files: the beta's (0.113), by default the
# installed game's, and the stable branch's (0.111), whose data folder STS2_STABLE_DATA names, e.g.
# <stable install>/data_sts2_linuxbsd_x86_64 (Steam's console: download_depot 2868840 2868843). While the installed
# game is on stable, STS2_BETA_DATA names a copy of the beta's data folder for the loader and the 0.113 build.
# Logs go to out/logs.
set -eu

cd "$(dirname "$0")"
: "${STS2_STABLE_DATA:?set STS2_STABLE_DATA to the data folder of the stable game}"
out="$PWD/out/QuickMatch"
logs="$PWD/out/logs"
rm -rf "$out"
mkdir -p "$out" "$logs"

build() {
    log=$1
    shift
    dotnet build -c Release "$@" > "$logs/$log.log" 2>&1 || { echo "build failed: $logs/$log.log" >&2; exit 1; }
}
beta() {
    if [ -n "${STS2_BETA_DATA:-}" ]; then build "$@" -p:Sts2DataPath="$STS2_BETA_DATA"; else build "$@"; fi
}
(cd QuickMatchLoader && beta loader)
(cd QuickMatch && beta 0.113)
(cd QuickMatch && build 0.111 -p:GameVersion=0.111 -p:Sts2DataPath="$STS2_STABLE_DATA")

cp QuickMatchLoader/bin/Release/net9.0/QuickMatch.dll \
   QuickMatch/bin/0.113/Release/net9.0/QuickMatch-0.113.dll \
   QuickMatch/bin/0.111/Release/net9.0/QuickMatch-0.111.dll \
   QuickMatch/QuickMatch.json ../LICENSE ../README.md "$out/"
echo "$out"
