#!/usr/bin/env bash
# Fetch and build the XR runtime dependencies PeakXR ships next to the plugin.
#
#   OpenXR (default): com.unity.xr.openxr + com.unity.xr.management from packages.unity.com
#                     (Unity Companion License). Natives are copied, managed code is compiled
#                     from the package's C# source against the game's own Managed/ DLLs.
#   OpenVR (fallback): Valve unity-xr-plugin release tarball (BSD-3-Clause).
#
# Outputs (all under deps/, which is gitignored; never commit them):
#   deps/xr/native/      UnityOpenXR.dll openxr_loader.dll UnitySubsystemsManifest.json
#   deps/xr/managed/     Unity.XR.Management.dll Unity.XR.OpenXR.dll
#   deps/openvr/native/  XRSDKOpenVR.dll openvr_api.dll UnitySubsystemsManifest.json
#   deps/openvr/managed/ Unity.XR.OpenVR.dll
#   deps/licenses/       upstream license files (shipped in the package)
#
# Idempotent: tarballs are cached in deps/cache and re-verified on every run; the
# generated projects are rebuilt incrementally by MSBuild.
#
# Env: PEAK_DIR (game install, read only; refs/peak/ stands in when it is absent), XR_OPENXR_VERSION, XR_MANAGEMENT_VERSION,
#      OPENVR_TAG, SKIP_OPENVR=1.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEPS="$ROOT/deps"
CACHE="$DEPS/cache"
SRC="$DEPS/src"
BUILD="$DEPS/build"

OPENXR_VERSION="${XR_OPENXR_VERSION:-1.16.1}"
MANAGEMENT_VERSION="${XR_MANAGEMENT_VERSION:-4.5.4}"
# Valve unity-xr-plugin release. The sha256 is pinned because GitHub release assets carry no
# registry-style checksum we can fetch independently; update both together.
OPENVR_TAG="${OPENVR_TAG:-v1.2.4}"
OPENVR_SHA256_v1_2_4="495b3c17a8e080b58eeca97ec455b6af38671a1d8348c6d927c0139aaa34604f"

log() { printf '\033[1;34m[deps]\033[0m %s\n' "$*" >&2; }
die() { printf '\033[1;31m[deps] error:\033[0m %s\n' "$*" >&2; exit 1; }

for tool in curl tar sha1sum sha256sum jq strings sort; do
    command -v "$tool" >/dev/null || die "missing tool: $tool"
done
command -v dotnet >/dev/null || die "dotnet not on PATH; run through 'mise run deps' or 'mise exec --'"

# ---------------------------------------------------------------------------------------------
# Game layout (read only). Find <Game>_Data dynamically instead of assuming PEAK_Data.
# ---------------------------------------------------------------------------------------------
DATA_DIR=""
for d in "${PEAK_DIR:-/nonexistent}"/*_Data; do
    [[ -d "$d/Managed" ]] && { DATA_DIR="$d"; break; }
done
UNITY_VERSION=""
if [[ -n "$DATA_DIR" ]]; then
    MANAGED="$DATA_DIR/Managed"
    log "game data: $DATA_DIR"
else
    # No game install (CI): the committed reference assemblies carry the same metadata (tools/make-refs.sh).
    MANAGED="$ROOT/refs/peak"
    [[ -f "$MANAGED/versions.txt" ]] || die "PEAK_DIR has no *_Data/Managed (${PEAK_DIR:-unset}) and refs/peak/ is missing"
    UNITY_VERSION="$(grep -oP '^unity=\K.*' "$MANAGED/versions.txt")"
    log "no game install at ${PEAK_DIR:-unset}; using refs/peak (Unity $UNITY_VERSION)"
fi

# Unity version: the data.unity3d / globalgamemanagers header holds it; UnityPlayer.dll is the fallback.
for f in "$DATA_DIR/data.unity3d" "$DATA_DIR/globalgamemanagers"; do
    [[ -z "$UNITY_VERSION" ]] || break
    [[ -f "$f" ]] || continue
    UNITY_VERSION="$(head -c 256 "$f" | strings -n 6 | grep -m1 -oE '^[0-9]{4,}\.[0-9]+\.[0-9]+[abfpx][0-9]+$' || true)"
    [[ -n "$UNITY_VERSION" ]] && break
done
if [[ -z "$UNITY_VERSION" && -f "$PEAK_DIR/UnityPlayer.dll" ]]; then
    UNITY_VERSION="$(strings -n 8 "$PEAK_DIR/UnityPlayer.dll" | grep -m1 -oE '^[0-9]{4,}\.[0-9]+\.[0-9]+[abfpx][0-9]+ \(' | cut -d' ' -f1 || true)"
fi
[[ -n "$UNITY_VERSION" ]] || die "could not detect the game's Unity version"
log "game Unity version: $UNITY_VERSION"

for asm in Unity.XR.Management Unity.XR.OpenXR Unity.XR.OpenVR; do
    [[ -f "$MANAGED/$asm.dll" ]] && log "warning: the game now ships $asm.dll itself; prefer it over ours (check compat)"
done

# ---------------------------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------------------------
# version_ge A B: true when A >= B (Unity versions like 6000.3.15f1 compare numerically per part).
version_ge() {
    local a="${1//[abfpx]/.}" b="${2//[abfpx]/.}"
    [[ "$(printf '%s\n%s\n' "$a" "$b" | sort -V | head -n1)" == "$b" ]]
}

# dll_version DLL: FileVersion from the PE version resource, empty when 0.0.0.0 or absent.
# Reference assemblies lose that resource; tools/make-refs.sh records it in refs/peak/versions.txt.
dll_version() {
    local v
    if [[ "$1" == "$ROOT/refs/peak/"* ]]; then
        v="$(grep -m1 -oP "^file:\Q$(basename "$1")\E=\K.*" "$ROOT/refs/peak/versions.txt" || true)"
    else
        v="$(strings -el "$1" 2>/dev/null | grep -A1 -xm1 'FileVersion' | tail -n1 || true)"
    fi
    [[ "$v" =~ ^[0-9]+(\.[0-9]+)+$ && "$v" != "0.0.0.0" ]] && printf '%s' "$v"
    return 0
}

# Fetch URL to FILE (cached). Verification is done by the caller.
fetch() {
    local url="$1" out="$2"
    if [[ -f "$out" ]]; then return 0; fi
    log "download $url"
    curl -fL --retry 3 --proto '=https' -o "$out.part" "$url"
    mv "$out.part" "$out"
}

check_hash() {  # check_hash algo expected file
    local algo="$1" want="$2" file="$3" got
    got="$("${algo}sum" "$file" | cut -d' ' -f1)"
    if [[ "$got" != "$want" ]]; then
        rm -f "$file"
        die "$algo mismatch for $(basename "$file"): got $got, expected $want (cached copy deleted; re-run)"
    fi
}

# Extract TARBALL into DIR once (stamp keyed on the tarball hash).
extract() {
    local tgz="$1" dir="$2" stamp
    stamp="$(sha256sum "$tgz" | cut -d' ' -f1)"
    if [[ -f "$dir/.stamp" && "$(cat "$dir/.stamp")" == "$stamp" ]]; then return 0; fi
    rm -rf "$dir"; mkdir -p "$dir"
    tar -xzf "$tgz" -C "$dir" --no-same-owner --no-same-permissions
    printf '%s' "$stamp" >"$dir/.stamp"
}

# Unity registry package: verify the tarball against versions[v].dist (integrity if present, else shasum).
fetch_unity_pkg() {
    local pkg="$1" ver="$2" meta="$CACHE/$1.json" tgz="$CACHE/$1-$2.tgz" url shasum integrity
    if [[ ! -f "$meta" ]] || ! jq -e --arg v "$ver" '.versions[$v]' "$meta" >/dev/null 2>&1; then
        log "registry metadata $pkg"
        curl -fsSL --retry 3 --proto '=https' -o "$meta.part" "https://packages.unity.com/$pkg"
        mv "$meta.part" "$meta"
    fi
    jq -e --arg v "$ver" '.versions[$v]' "$meta" >/dev/null || die "$pkg $ver is not in the registry"
    url="$(jq -r --arg v "$ver" '.versions[$v].dist.tarball' "$meta")"
    shasum="$(jq -r --arg v "$ver" '.versions[$v].dist.shasum // empty' "$meta")"
    integrity="$(jq -r --arg v "$ver" '.versions[$v].dist.integrity // empty' "$meta")"
    fetch "$url" "$tgz"
    # packages.unity.com currently publishes only dist.shasum (sha1); check dist.integrity
    # (npm SRI, e.g. sha512-<base64>) too if the registry starts sending it.
    if [[ -n "$integrity" ]]; then
        local algo="${integrity%%-*}" b64="${integrity#*-}" got
        case "$algo" in sha512|sha384|sha256|sha1) ;; *) die "unsupported integrity algo $algo for $pkg";; esac
        command -v openssl >/dev/null || die "openssl is needed to check the integrity of $pkg"
        got="$(openssl dgst -"$algo" -binary "$tgz" | base64 -w0)"
        [[ "$got" == "$b64" ]] || { rm -f "$tgz"; die "integrity mismatch for $pkg $ver"; }
    fi
    [[ -n "$shasum" ]] || die "registry gives no checksum for $pkg $ver"
    check_hash sha1 "$shasum" "$tgz"
    log "verified $pkg $ver (sha1 $shasum${integrity:+, $integrity})"
    extract "$tgz" "$SRC/$pkg-$ver"
}

# ---------------------------------------------------------------------------------------------
# Scripting defines Unity would set for a Windows x64 Mono player build of this game.
# ---------------------------------------------------------------------------------------------
UNITY_MAJOR="${UNITY_VERSION%%.*}"
_rest="${UNITY_VERSION#*.}"
UNITY_MINOR="${_rest%%.*}"
_patch="${_rest#*.}"
UNITY_PATCH="${_patch%%[abfpx]*}"

unity_defines() {
    local d=(UNITY_5_3_OR_NEWER UNITY_5_4_OR_NEWER UNITY_5_5_OR_NEWER UNITY_5_6_OR_NEWER)
    local y m last
    for y in 2017 2018 2019 2020 2021 2022 2023; do
        # Last minor of each yearly series (2023.3 became Unity 6, which still defines it).
        case "$y" in 2017|2018|2019) last=4 ;; *) last=3 ;; esac
        for (( m = 1; m <= last; m++ )); do
            if (( UNITY_MAJOR > y )) || { (( UNITY_MAJOR == y )) && (( UNITY_MINOR >= m )); }; then
                d+=("UNITY_${y}_${m}_OR_NEWER")
            fi
        done
    done
    if (( UNITY_MAJOR >= 6000 )); then
        for (( m = 0; m <= UNITY_MINOR; m++ )); do d+=("UNITY_6000_${m}_OR_NEWER"); done
    fi
    d+=("UNITY_${UNITY_MAJOR}" "UNITY_${UNITY_MAJOR}_${UNITY_MINOR}" "UNITY_${UNITY_MAJOR}_${UNITY_MINOR}_${UNITY_PATCH}")
    # Standalone Windows x64 player, Mono backend, no editor, no development build.
    d+=(UNITY_STANDALONE_WIN UNITY_STANDALONE PLATFORM_STANDALONE_WIN PLATFORM_STANDALONE
        UNITY_64 PLATFORM_ARCH_64 ENABLE_MONO ENABLE_VR ENABLE_XR ENABLE_AR ENABLE_AUDIO
        ENABLE_PHYSICS ENABLE_VIDEO ENABLE_UNITYEVENTS ENABLE_MANAGED_JOBS
        NET_STANDARD_2_0 NET_STANDARD_2_1 NET_STANDARD CSHARP_7_3_OR_NEWER)
    # Active input handling: the new Input System is in use when the game ships it.
    [[ -f "$MANAGED/Unity.InputSystem.dll" ]] && d+=(ENABLE_INPUT_SYSTEM)
    # Deliberately NOT defined: UNITY_EDITOR, DEVELOPMENT_BUILD, UNITY_ASSERTIONS, ENABLE_PROFILER,
    # ENABLE_CLOUD_SERVICES_ANALYTICS (keeps the packages' analytics code compiled out: no telemetry
    # from a mod), USE_INPUT_SYSTEM_POSE_CONTROL / USE_STICK_CONTROL_THUMBSTICKS (opt-in project
    # settings, off in a fresh Unity project; changing them changes the public control types).
    printf '%s\n' "${d[@]}"
}

# Map a Unity package name to the assembly that proves it is in the game build.
package_assembly() {
    case "$1" in
        com.unity.inputsystem) echo "$MANAGED/Unity.InputSystem.dll" ;;
        com.unity.render-pipelines.universal) echo "$MANAGED/Unity.RenderPipelines.Universal.Runtime.dll" ;;
        com.unity.render-pipelines.core) echo "$MANAGED/Unity.RenderPipelines.Core.Runtime.dll" ;;
        com.unity.xr.management) echo "$DEPS/xr/managed/Unity.XR.Management.dll" ;;
        com.unity.xr.compositionlayers) echo "$MANAGED/Unity.XR.CompositionLayers.dll" ;;
        com.unity.xr.core-utils) echo "$MANAGED/Unity.XR.CoreUtils.dll" ;;
        com.microsoft.mixedreality.openxr) echo "$MANAGED/Microsoft.MixedReality.OpenXR.dll" ;;
        com.unity.modules.*)
            local mod="${1#com.unity.modules.}" f base
            for f in "$MANAGED"/UnityEngine.*Module.dll; do
                base="$(basename "$f" .dll)"; base="${base#UnityEngine.}"; base="${base%Module}"
                [[ "${base,,}" == "${mod,,}" ]] && { echo "$f"; return; }
            done ;;
    esac
}

# Evaluate an asmdef's versionDefines against the game build.
# Expressions: "" (any), "x.y" (>= x.y), "[a,b]" / "[a,b)" / "(a,b)" ranges.
version_defines() {
    local asmdef="$1" name expr define have dll lo hi lc hc ok
    # \x1f separator: a tab would collapse empty expressions because IFS treats it as whitespace.
    while IFS=$'\x1f' read -r name expr define; do
        have=""
        if [[ "$name" == "Unity" ]]; then
            have="$UNITY_VERSION"
        else
            dll="$(package_assembly "$name")"
            if [[ -n "$dll" && -f "$dll" ]]; then
                have="$(dll_version "$dll")"
                # Package assemblies built by Unity carry 0.0.0.0; presence is all we can prove.
                [[ -n "$have" ]] || have="present"
            fi
        fi
        [[ -n "$have" ]] || continue
        ok=1
        if [[ -n "$expr" && "$have" != "present" ]]; then
            if [[ "$expr" =~ ^([\[\(])([^,]*),([^\]\)]*)([\]\)])$ ]]; then
                lc="${BASH_REMATCH[1]}"; lo="${BASH_REMATCH[2]// /}"; hi="${BASH_REMATCH[3]// /}"; hc="${BASH_REMATCH[4]}"
                if [[ -n "$lo" ]]; then
                    version_ge "$have" "$lo" || ok=0
                    [[ "$lc" == "(" && "$have" == "$lo" ]] && ok=0
                fi
                if [[ -n "$hi" ]]; then
                    version_ge "$hi" "$have" || ok=0
                    [[ "$hc" == ")" && "$have" == "$hi" ]] && ok=0
                fi
            else
                version_ge "$have" "$expr" || ok=0
            fi
        fi
        if (( ok )); then echo "$define"; fi
    done < <(jq -r '.versionDefines[]? | select((.excludePlatforms // []) | index("WindowsStandalone64") | not) | [.name, (.expression // ""), .define] | join("\u001f")' "$asmdef")
}

# Write deps/build/<Asm>/<Asm>.csproj and build it into OUT_DIR.
#   build_asm NAME VERSION SRC_DIR OUT_DIR UNSAFE DEFINES_FILE EXTRA_REF...
build_asm() {
    local name="$1" pkgver="$2" srcdir="$3" outdir="$4" unsafe="$5" defines_file="$6"; shift 6
    local proj_dir="$BUILD/$name" excludes="" sub refs="" r defines
    mkdir -p "$proj_dir" "$outdir"
    # Folders below SRC_DIR with their own asmdef belong to other assemblies (tests, samples, mock runtime).
    while IFS= read -r sub; do
        sub="${sub#"$srcdir"/}"
        excludes+="    <Compile Remove=\"\$(PkgSrc)/${sub}/**/*.cs\" />"$'\n'
    done < <(find "$srcdir" -mindepth 2 -name '*.asmdef' -printf '%h\n' | sort -u)
    for r in "$@"; do
        refs+="    <Reference Include=\"$r\" Private=\"false\" />"$'\n'
    done
    defines="$(paste -sd';' "$defines_file")"
    cat >"$proj_dir/$name.csproj" <<EOF
<!-- Generated by tools/fetch-xr-deps.sh. Do not edit; re-run the script. -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <AssemblyName>$name</AssemblyName>
    <RootNamespace>$name</RootNamespace>
    <PkgSrc>$srcdir</PkgSrc>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <LangVersion>9.0</LangVersion>
    <Nullable>disable</Nullable>
    <AllowUnsafeBlocks>$unsafe</AllowUnsafeBlocks>
    <Optimize>true</Optimize>
    <DebugType>none</DebugType>
    <Deterministic>true</Deterministic>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
    <NoWarn>\$(NoWarn);CS0067;CS0168;CS0169;CS0219;CS0414;CS0618;CS0649;CS1591;CS8632</NoWarn>
    <!-- Unity builds package assemblies as 0.0.0.0; keep the same identity. -->
    <AssemblyVersion>0.0.0.0</AssemblyVersion>
    <FileVersion>$(echo "$pkgver" | grep -oE '^[0-9]+\.[0-9]+\.[0-9]+').0</FileVersion>
    <InformationalVersion>$pkgver (built from package source by PeakXR tools/fetch-xr-deps.sh)</InformationalVersion>
    <GenerateAssemblyInfo>true</GenerateAssemblyInfo>
    <GenerateAssemblyTitleAttribute>false</GenerateAssemblyTitleAttribute>
    <GenerateAssemblyProductAttribute>false</GenerateAssemblyProductAttribute>
    <GenerateAssemblyCompanyAttribute>false</GenerateAssemblyCompanyAttribute>
    <GenerateAssemblyConfigurationAttribute>false</GenerateAssemblyConfigurationAttribute>
    <DisableImplicitFrameworkReferences>false</DisableImplicitFrameworkReferences>
    <CopyLocalLockFileAssemblies>false</CopyLocalLockFileAssemblies>
    <DefineConstants>$defines</DefineConstants>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="\$(PkgSrc)/**/*.cs" />
$excludes  </ItemGroup>
  <ItemGroup>
    <!-- Every engine module: robust against types moving between modules in future Unity versions. -->
    <Reference Include="$MANAGED/UnityEngine.dll" Private="false" />
    <Reference Include="$MANAGED/UnityEngine.*Module.dll" Private="false" />
$refs  </ItemGroup>
</Project>
EOF
    log "build $name ($(grep -c . "$defines_file") defines)"
    dotnet build "$proj_dir/$name.csproj" -c Release --nologo -v quiet -clp:ErrorsOnly \
        -p:OutDir="$proj_dir/bin/" >"$proj_dir/build.log" 2>&1 \
        || { cat "$proj_dir/build.log" >&2; die "build of $name failed (log: $proj_dir/build.log)"; }
    cp -f "$proj_dir/bin/$name.dll" "$outdir/$name.dll"
}

# Managed refs that exist in this game build (optional ones are skipped when absent).
game_refs() {
    local n
    for n in "$@"; do [[ -f "$MANAGED/$n" ]] && echo "$MANAGED/$n"; done
    return 0
}

mkdir -p "$CACHE" "$SRC" "$BUILD" "$DEPS/xr/native" "$DEPS/xr/managed" "$DEPS/licenses"
# Stop MSBuild from walking up into the repo's Directory.Build.* files.
printf '<Project />\n' >"$BUILD/Directory.Build.props"
printf '<Project />\n' >"$BUILD/Directory.Build.targets"

# ---------------------------------------------------------------------------------------------
# OpenXR
# ---------------------------------------------------------------------------------------------
fetch_unity_pkg com.unity.xr.management "$MANAGEMENT_VERSION"
fetch_unity_pkg com.unity.xr.openxr "$OPENXR_VERSION"
MGMT_PKG="$SRC/com.unity.xr.management-$MANAGEMENT_VERSION/package"
OXR_PKG="$SRC/com.unity.xr.openxr-$OPENXR_VERSION/package"

for f in "$OXR_PKG/Runtime/windows/x64/UnityOpenXR.dll" "$OXR_PKG/RuntimeLoaders/windows/x64/openxr_loader.dll" "$OXR_PKG/Runtime/UnitySubsystemsManifest.json"; do
    [[ -f "$f" ]] || die "expected file missing from OpenXR package: ${f#"$OXR_PKG"/}"
    cp -f "$f" "$DEPS/xr/native/"
done
cp -f "$OXR_PKG/LICENSE.md" "$DEPS/licenses/com.unity.xr.openxr-LICENSE.md"
[[ -f "$OXR_PKG/Third Party Notices.md" ]] && cp -f "$OXR_PKG/Third Party Notices.md" "$DEPS/licenses/com.unity.xr.openxr-Third-Party-Notices.md"
cp -f "$MGMT_PKG/LICENSE.md" "$DEPS/licenses/com.unity.xr.management-LICENSE.md"

BASE_DEFINES="$BUILD/defines.base.txt"
unity_defines >"$BASE_DEFINES"

# Unity.XR.Management
{ cat "$BASE_DEFINES"; version_defines "$MGMT_PKG/Runtime/Unity.XR.Management.asmdef"; } | sort -u >"$BUILD/defines.Unity.XR.Management.txt"
build_asm Unity.XR.Management "$MANAGEMENT_VERSION" "$MGMT_PKG/Runtime" "$DEPS/xr/managed" false \
    "$BUILD/defines.Unity.XR.Management.txt"

# Unity.XR.OpenXR (runtime assembly only: nested asmdef folders and Editor/ are excluded)
{ cat "$BASE_DEFINES"; version_defines "$OXR_PKG/Runtime/Unity.XR.OpenXR.asmdef"; } | sort -u >"$BUILD/defines.Unity.XR.OpenXR.txt"
mapfile -t OXR_REFS < <(game_refs Unity.InputSystem.dll Unity.Mathematics.dll Unity.Collections.dll Unity.Burst.dll \
    Unity.RenderPipelines.Core.Runtime.dll Unity.RenderPipelines.Universal.Runtime.dll)
build_asm Unity.XR.OpenXR "$OPENXR_VERSION" "$OXR_PKG/Runtime" "$DEPS/xr/managed" true \
    "$BUILD/defines.Unity.XR.OpenXR.txt" "${OXR_REFS[@]}" "$DEPS/xr/managed/Unity.XR.Management.dll"

# ---------------------------------------------------------------------------------------------
# OpenVR fallback (Valve unity-xr-plugin)
# ---------------------------------------------------------------------------------------------
if [[ "${SKIP_OPENVR:-0}" != "1" ]]; then
    mkdir -p "$DEPS/openvr/native" "$DEPS/openvr/managed"
    ver="${OPENVR_TAG#v}"
    pin_var="OPENVR_SHA256_${OPENVR_TAG//./_}"
    want="${!pin_var:-}"
    [[ -n "$want" ]] || die "no pinned sha256 for OpenVR $OPENVR_TAG; add $pin_var to this script"
    tgz="$CACHE/com.valvesoftware.unity.openvr-$ver.tgz"
    fetch "https://github.com/ValveSoftware/unity-xr-plugin/releases/download/$OPENVR_TAG/com.valvesoftware.unity.openvr-$ver.tgz" "$tgz"
    check_hash sha256 "$want" "$tgz"
    log "verified unity-xr-plugin $OPENVR_TAG (sha256 $want)"
    extract "$tgz" "$SRC/com.valvesoftware.unity.openvr-$ver"
    OVR_PKG="$SRC/com.valvesoftware.unity.openvr-$ver/package"
    # XRSDKOpenVR.dll imports only KERNEL32 and openvr_api; the debug CRT (ucrtbased.dll) in the
    # package is not needed and is not shipped.
    for f in "$OVR_PKG/Runtime/x64/XRSDKOpenVR.dll" "$OVR_PKG/Runtime/x64/openvr_api.dll" "$OVR_PKG/Runtime/UnitySubsystemsManifest.json"; do
        [[ -f "$f" ]] || die "expected file missing from OpenVR package: ${f#"$OVR_PKG"/}"
        cp -f "$f" "$DEPS/openvr/native/"
    done
    cp -f "$OVR_PKG/LICENSE.md" "$DEPS/licenses/com.valvesoftware.unity.openvr-LICENSE.md"
    { cat "$BASE_DEFINES"; version_defines "$OVR_PKG/Runtime/Unity.XR.OpenVR.asmdef"; } | sort -u >"$BUILD/defines.Unity.XR.OpenVR.txt"
    mapfile -t OVR_REFS < <(game_refs Unity.InputSystem.dll)
    build_asm Unity.XR.OpenVR "$ver" "$OVR_PKG/Runtime" "$DEPS/openvr/managed" false \
        "$BUILD/defines.Unity.XR.OpenVR.txt" "${OVR_REFS[@]}" "$DEPS/xr/managed/Unity.XR.Management.dll"
else
    log "SKIP_OPENVR=1: OpenVR fallback skipped"
fi

# ---------------------------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------------------------
printf 'unity=%s\nopenxr=%s\nmanagement=%s\nopenvr=%s\n' "$UNITY_VERSION" "$OPENXR_VERSION" "$MANAGEMENT_VERSION" \
    "$([[ "${SKIP_OPENVR:-0}" == 1 ]] && echo skipped || echo "$OPENVR_TAG")" >"$DEPS/versions.txt"
echo
echo "XR dependencies (Unity $UNITY_VERSION, OpenXR $OPENXR_VERSION, Management $MANAGEMENT_VERSION, OpenVR ${OPENVR_TAG})"
(cd "$DEPS" && find xr openvr -type f 2>/dev/null | sort | while read -r f; do
    printf '  %s  %9s  %s\n' "$(sha256sum "$f" | cut -d' ' -f1)" "$(stat -c %s "$f")" "deps/$f"
done)
