#!/usr/bin/env bash
# Regenerate refs/peak/: reference-only copies (metadata, no method bodies, no resources) of the game
# assemblies the build compiles against, so CI and machines without PEAK can build.
# The projects use $PEAK_DIR/PEAK_Data/Managed when it exists and fall back to refs/peak/ otherwise.
# Run after a game update (`mise run refs`) and commit the result.
#
# --all keeps private and internal members with their real access modifiers (no publicizing):
# BUTR.Harmony.Analyzer checks patch targets by name, and many of them are private.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$ROOT/refs/peak"

die() { printf '\033[1;31m[refs] error:\033[0m %s\n' "$*" >&2; exit 1; }

[[ -n "${PEAK_DIR:-}" && -d "$PEAK_DIR" ]] || die "PEAK_DIR is not set or does not exist (${PEAK_DIR:-unset})"
MANAGED="$PEAK_DIR/PEAK_Data/Managed"
[[ -d "$MANAGED" ]] || die "no PEAK_Data/Managed under $PEAK_DIR"
command -v refasmer >/dev/null || die "refasmer not on PATH; run through 'mise run refs'"

# Every game DLL a project references (src/PeakXR, tools/MpObserver), plus the extra ones
# tools/fetch-xr-deps.sh compiles the XR packages against (Unity.Collections, Unity.Burst).
shopt -s nullglob
dlls=()
for pattern in Assembly-CSharp.dll 'Zorro.*.dll' 'Photon*.dll' com.rlabrecque.steamworks.net.dll \
    Sirenix.Serialization.dll 'UnityEngine*.dll' Unity.InputSystem.dll Unity.Animation.Rigging.dll \
    Unity.RenderPipelines.Core.Runtime.dll Unity.RenderPipelines.Universal.Runtime.dll \
    Unity.TextMeshPro.dll Unity.Mathematics.dll Unity.Collections.dll Unity.Burst.dll; do
    # shellcheck disable=SC2206 # the pattern is meant to glob
    matches=("$MANAGED"/$pattern)
    (( ${#matches[@]} )) || die "no $pattern in $MANAGED"
    dlls+=("${matches[@]}")
done

rm -f "$OUT"/*.dll
mkdir -p "$OUT"
refasmer -q --all -O "$OUT" "${dlls[@]}"

# Read by tools/fetch-xr-deps.sh when building without the game, and quoted in refs/peak/README.md.
game_version="$(head -n1 "$PEAK_DIR/version.txt" | tr -d '\r')"
game_version="${game_version%%--*}"
unity_version="$(strings -n 8 "$PEAK_DIR/UnityPlayer.dll" | grep -m1 -oE '^[0-9]{4,}\.[0-9]+\.[0-9]+[abfpx][0-9]+ \(' | cut -d' ' -f1 || true)"
[[ -n "$unity_version" ]] || die "could not read the Unity version from UnityPlayer.dll"
{
    printf 'game=%s\nunity=%s\n' "$game_version" "$unity_version"
    # Refasmer drops the PE version resource; fetch-xr-deps.sh reads package versions from here instead.
    for f in "${dlls[@]}"; do
        v="$(strings -el "$f" | grep -A1 -xm1 FileVersion | tail -n1 || true)"
        if [[ "$v" =~ ^[0-9]+(\.[0-9]+)+$ && "$v" != "0.0.0.0" ]]; then printf 'file:%s=%s\n' "$(basename "$f")" "$v"; fi
    done
} >"$OUT/versions.txt"

echo "refs/peak: ${#dlls[@]} assemblies from PEAK $game_version (Unity $unity_version), $(du -sh "$OUT" | cut -f1)"
