#!/usr/bin/env bash
# Build the Thunderstore package: dist/PeakXR-<version>.zip plus an unpacked copy in dist/PeakXR/.
# Expects `mise run build` (Release) and `mise run deps` to have run; `mise run package` does both.
#
# Layout:
#   manifest.json README.md LICENSE icon.png
#   BepInEx/plugins/PeakXR/PeakXR*.dll
#   BepInEx/plugins/PeakXR/RuntimeDeps/*.dll                 managed XR assemblies
#   BepInEx/plugins/PeakXR/licenses/*                        upstream licenses of the XR deps
#   BepInEx/patchers/PeakXR/PeakXR.Preloader.dll
#   BepInEx/patchers/PeakXR/RuntimeDeps/{OpenXR,OpenVR}/*    native plugins + subsystem manifests
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DIST="$ROOT/dist"
STAGE="$DIST/PeakXR"
CONFIG="${CONFIG:-Release}"
WEBSITE="https://github.com/ThePhaseless/PeakXR"
DESCRIPTION="VR for PEAK: play in a headset through OpenXR (SteamVR, Monado, WMR, ...) with OpenVR as a fallback."

log() { printf '\033[1;34m[package]\033[0m %s\n' "$*" >&2; }
die() { printf '\033[1;31m[package] error:\033[0m %s\n' "$*" >&2; exit 1; }

command -v zip >/dev/null || die "missing tool: zip"

VERSION="$(grep -m1 -oP '(?<=<Version>)[^<]+' "$ROOT/Directory.Build.props" || true)"
[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || die "Directory.Build.props <Version> must be Major.Minor.Patch for Thunderstore (got '${VERSION}')"
# thunderstore.toml follows the same version, so a later tcli publish cannot drift from the zip.
sed -i "s/^versionNumber = .*/versionNumber = \"$VERSION\"/" "$ROOT/thunderstore.toml"

PLUGIN_BIN="$(dirname "$(find "$ROOT/src/PeakXR/bin/$CONFIG" -name PeakXR.dll -print -quit 2>/dev/null || true)")"
PRELOADER_BIN="$(dirname "$(find "$ROOT/src/PeakXR.Preloader/bin/$CONFIG" -name PeakXR.Preloader.dll -print -quit 2>/dev/null || true)")"
[[ -f "$PLUGIN_BIN/PeakXR.dll" ]] || die "PeakXR.dll not built; run 'mise run build'"
[[ -f "$PRELOADER_BIN/PeakXR.Preloader.dll" ]] || die "PeakXR.Preloader.dll not built; run 'mise run build'"
[[ -f "$ROOT/deps/xr/managed/Unity.XR.OpenXR.dll" ]] || die "XR deps missing; run 'mise run deps'"

rm -rf "$STAGE"
PLUGIN_DIR="$STAGE/BepInEx/plugins/PeakXR"
PATCHER_DIR="$STAGE/BepInEx/patchers/PeakXR"
mkdir -p "$PLUGIN_DIR/RuntimeDeps" "$PLUGIN_DIR/licenses" "$PATCHER_DIR/RuntimeDeps/OpenXR"

# Plugin and our own libraries (PeakXR.Core etc.); never the game's or BepInEx's DLLs.
for f in "$PLUGIN_BIN"/PeakXR*.dll; do
    [[ "$(basename "$f")" == "PeakXR.Preloader.dll" ]] && continue
    cp "$f" "$PLUGIN_DIR/"
done
cp "$PRELOADER_BIN/PeakXR.Preloader.dll" "$PATCHER_DIR/"

# Managed XR assemblies, loaded by the plugin.
cp "$ROOT"/deps/xr/managed/*.dll "$PLUGIN_DIR/RuntimeDeps/"
# Native runtimes, installed into <Game>_Data by the preloader.
cp "$ROOT"/deps/xr/native/* "$PATCHER_DIR/RuntimeDeps/OpenXR/"
if [[ -f "$ROOT/deps/openvr/managed/Unity.XR.OpenVR.dll" ]]; then
    mkdir -p "$PATCHER_DIR/RuntimeDeps/OpenVR"
    cp "$ROOT"/deps/openvr/managed/*.dll "$PLUGIN_DIR/RuntimeDeps/"
    cp "$ROOT"/deps/openvr/native/* "$PATCHER_DIR/RuntimeDeps/OpenVR/"
else
    log "warning: OpenVR deps missing; packaging without the OpenVR fallback"
fi
cp "$ROOT"/deps/licenses/* "$PLUGIN_DIR/licenses/"

# Thunderstore metadata.
cp "$ROOT/LICENSE" "$STAGE/LICENSE"
cp "$ROOT/LICENSE" "$PLUGIN_DIR/licenses/PeakXR-LICENSE"
if [[ -f "$ROOT/assets/thunderstore-README.md" ]]; then
    cp "$ROOT/assets/thunderstore-README.md" "$STAGE/README.md"
else
    # TODO: write assets/thunderstore-README.md (shown on the Thunderstore page).
    cat >"$STAGE/README.md" <<EOF
# PeakXR

$DESCRIPTION

Work in progress. See $WEBSITE for install notes and launch options.

PeakXR is MIT licensed. The bundled Unity XR assemblies and native plugins (OpenXR, XR Management) are under
the Unity Companion License and Valve's OpenVR plugin under BSD-3-Clause; see BepInEx/plugins/PeakXR/licenses/.
EOF
fi
if [[ ! -f "$ROOT/assets/icon.png" ]]; then
    log "assets/icon.png missing; generating a placeholder"
    if command -v uv >/dev/null; then
        uv run --no-project --quiet python "$ROOT/tools/make-icon.py" "$ROOT/assets/icon.png"
    elif command -v python3 >/dev/null; then
        python3 "$ROOT/tools/make-icon.py" "$ROOT/assets/icon.png"
    else
        die "no python to generate assets/icon.png; add a 256x256 PNG by hand"
    fi
fi
cp "$ROOT/assets/icon.png" "$STAGE/icon.png"

cat >"$STAGE/manifest.json" <<EOF
{
  "name": "PeakXR",
  "version_number": "$VERSION",
  "website_url": "$WEBSITE",
  "description": "$DESCRIPTION",
  "dependencies": [
    "BepInEx-BepInExPack_PEAK-5.4.75301"
  ]
}
EOF
(( ${#DESCRIPTION} <= 250 )) || die "Thunderstore description is longer than 250 characters"

ZIP="$DIST/PeakXR-$VERSION.zip"
rm -f "$ZIP"
(cd "$STAGE" && find . -exec touch -h -d '2026-01-01 00:00:00' {} + && zip -qrX "$ZIP" .)

log "packaged $ZIP"
(cd "$STAGE" && find . -type f | sort | while read -r f; do
    printf '  %s  %9s  %s\n' "$(sha256sum "$f" | cut -c1-16)" "$(stat -c %s "$f")" "${f#./}"
done)
