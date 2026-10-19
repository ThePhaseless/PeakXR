#!/usr/bin/env bash
# Copy the packaged mod (dist/PeakXR/BepInEx/*) into $PEAK_DIR/BepInEx/.
# Needs BepInExPack_PEAK already installed in the game folder. Run `mise run package` first.
# This writes into the game folder; PeakXR's preloader then manages <Game>_Data itself at launch.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SRC="$ROOT/dist/PeakXR/BepInEx"

die() { printf '\033[1;31m[deploy] error:\033[0m %s\n' "$*" >&2; exit 1; }

[[ -n "${PEAK_DIR:-}" && -d "$PEAK_DIR" ]] || die "PEAK_DIR is not set or does not exist (${PEAK_DIR:-unset})"
[[ -d "$SRC" ]] || die "$SRC missing; run 'mise run package' first"
[[ -d "$PEAK_DIR/BepInEx/core" ]] || die "BepInEx is not installed in $PEAK_DIR (install BepInExPack_PEAK first)"

# Replace our own folders wholesale so files removed from the package do not linger.
for d in plugins/PeakXR patchers/PeakXR; do
    rm -rf "${PEAK_DIR:?}/BepInEx/$d"
done
cp -r "$SRC"/. "$PEAK_DIR/BepInEx/"
echo "[deploy] copied $(find "$SRC" -type f | wc -l) files into $PEAK_DIR/BepInEx"
echo "[deploy] Steam launch options (Linux): WINEDLLOVERRIDES=\"winhttp=n,b\" %command% -force-d3d11"
