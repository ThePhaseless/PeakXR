#!/usr/bin/env bash
# Re-decompile the game's own code into decomp/ (gitignored) after a game update.
# Covers Assembly-CSharp*, and every Zorro.*.dll the build ships.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$ROOT/decomp"

die() { printf '\033[1;31m[decompile] error:\033[0m %s\n' "$*" >&2; exit 1; }

[[ -n "${PEAK_DIR:-}" && -d "$PEAK_DIR" ]] || die "PEAK_DIR is not set or does not exist (${PEAK_DIR:-unset})"
command -v ilspycmd >/dev/null || die "ilspycmd not on PATH; run through 'mise run decompile'"
MANAGED="$(find "$PEAK_DIR" -mindepth 2 -maxdepth 2 -type d -path '*_Data/Managed' -print -quit)"
[[ -n "$MANAGED" ]] || die "no *_Data/Managed under $PEAK_DIR"

# Keep the previous game version's decompile as the compat-check baseline (body and signature diffs).
old_version="$(grep -oP '^game_version=\K.*' "$OUT/VERSION" 2>/dev/null || echo unknown)"
if [[ -d "$OUT/Assembly-CSharp" && "$old_version" != "$(head -n1 "$PEAK_DIR/version.txt" 2>/dev/null)" ]]; then
    rm -rf "${OUT:?}.prev"
    cp -a "$OUT" "$OUT.prev"
fi
mkdir -p "$OUT"
shopt -s nullglob
targets=("$MANAGED"/Assembly-CSharp.dll "$MANAGED"/Assembly-CSharp-firstpass.dll "$MANAGED"/Zorro.*.dll)
for dll in "${targets[@]}"; do
    name="$(basename "$dll" .dll)"
    echo "[decompile] $name"
    rm -rf "${OUT:?}/$name"
    ilspycmd -p -o "$OUT/$name" -r "$MANAGED" "$dll" >/dev/null
done

{
    echo "game_version=$(head -n1 "$PEAK_DIR/version.txt" 2>/dev/null || echo unknown)"
    echo "game_commit=$(sed -n 2p "$PEAK_DIR/version.txt" 2>/dev/null || echo unknown)"
    echo "decompiled=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
} >"$OUT/VERSION"
echo "[decompile] done: $(cat "$OUT/VERSION" | tr '\n' ' ')"
