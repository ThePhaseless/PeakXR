#!/usr/bin/env bash
# Compatibility check after a game update.
# Now: prints the game and Unity versions and warns when deps/ was built for another Unity version.
# TODO: diff the Harmony patch targets (types/method signatures PeakXR patches) against decomp/.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

die() { printf '\033[1;31m[compat] error:\033[0m %s\n' "$*" >&2; exit 1; }

[[ -n "${PEAK_DIR:-}" && -d "$PEAK_DIR" ]] || die "PEAK_DIR is not set or does not exist (${PEAK_DIR:-unset})"

game_version="$(head -n1 "$PEAK_DIR/version.txt" 2>/dev/null || echo unknown)"
game_commit="$(sed -n 2p "$PEAK_DIR/version.txt" 2>/dev/null || echo unknown)"
unity_version="unknown"
if [[ -f "$PEAK_DIR/UnityPlayer.dll" ]]; then
    # grep -m1 closes the pipe early; ignore the resulting SIGPIPE status from strings.
    unity_version="$(strings -n 8 "$PEAK_DIR/UnityPlayer.dll" | grep -m1 -oE '^[0-9]{4,}\.[0-9]+\.[0-9]+[abfpx][0-9]+ \([0-9a-f]+\)' || true)"
    unity_version="${unity_version:-unknown}"
fi
build_id="$(grep -m1 -oP '"buildid"\s+"\K[0-9]+' "$PEAK_DIR/../../appmanifest_3527290.acf" 2>/dev/null || echo unknown)"

echo "PEAK version : $game_version (commit $game_commit, Steam build $build_id)"
echo "Unity        : $unity_version"

if [[ -f "$ROOT/deps/versions.txt" ]]; then
    deps_unity="$(grep -oP '^unity=\K.*' "$ROOT/deps/versions.txt")"
    echo "XR deps      : $(tr '\n' ' ' <"$ROOT/deps/versions.txt")"
    if [[ "$unity_version" != "$deps_unity "* ]]; then
        echo "WARNING: deps/ was built against Unity $deps_unity; re-run 'mise run deps'" >&2
    fi
else
    echo "XR deps      : not built (run 'mise run deps')"
fi
if [[ -f "$ROOT/decomp/VERSION" ]]; then
    decomp_version="$(grep -oP '^game_version=\K.*' "$ROOT/decomp/VERSION")"
    echo "decomp/      : game $decomp_version"
    [[ "$decomp_version" == "$game_version" ]] || echo "WARNING: decomp/ is from game $decomp_version; run 'mise run decompile'" >&2
fi
