#!/bin/sh
# Puts Quick Match back into the game's mods folder, for example after a Steam update or "Verify
# integrity of game files" removed it. Builds it first with ../build.sh (STS2_STABLE_DATA must be set,
# as there), so the installed copy always matches the source. It never deletes anything.
#
#   ./install.sh                  the default macOS Steam location
#   ./install.sh /path/to/mods    into another mods folder
set -eu

cd "$(dirname "$0")"
id=QuickMatch
app="$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app"
mods="${1:-$app/Contents/MacOS/mods}"

echo "Building $id..."
out="$(../build.sh)"

if [ ! -d "$(dirname "$mods")" ]; then
    echo "Game folder not found: $(dirname "$mods")" >&2
    echo "Pass the mods folder as an argument." >&2
    exit 1
fi

mkdir -p "$mods/$id"
cp "$out"/*.dll "$out/$id.json" "$mods/$id/"
echo "Installed $id into $mods/$id"
