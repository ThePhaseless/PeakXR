# PeakXR

A VR mod for [PEAK](https://store.steampowered.com/app/3527290/PEAK/), built as a BepInEx 5 plugin. It
loads Unity's OpenXR plugin into the game at startup, and can use Valve's OpenVR plugin instead.
Work in progress.

PeakXR is MIT licensed and written from scratch. It does not contain code from other PEAK or Unity VR
mods. The XR runtime files it ships come from Unity's packages (Unity Companion License) and Valve's
[unity-xr-plugin](https://github.com/ValveSoftware/unity-xr-plugin) (BSD-3-Clause). The build fetches
them; they are not in this repository.

## Build

Needs [mise](https://mise.jdx.dev) and a PEAK install. `PEAK_DIR` in `mise.toml` points at the default
Steam library; override it in `mise.local.toml` if yours is elsewhere.

```sh
mise run build      # fetch XR deps into deps/, then dotnet build
mise run test
mise run coverage   # tests with coverlet, cobertura XML in TestResults/
mise run format     # CSharpier; `format-check` verifies without writing
mise run package    # dist/PeakXR-<version>.zip (Thunderstore layout) and dist/PeakXR/
```

Other tasks: `mise run deploy` copies `dist/PeakXR/BepInEx` into the game, `mise run decompile` refreshes
`decomp/` after a game update, and `mise run compat-check` prints the game and Unity versions.

## Install

1. Install [BepInExPack_PEAK](https://thunderstore.io/c/peak/p/BepInEx/BepInExPack_PEAK/).
2. Install the PeakXR zip with a mod manager, or copy its `BepInEx` folder into the game folder.
3. Start SteamVR (or another OpenXR runtime) before the game.

The runtime is set in `BepInEx/config/PeakXR.Preloader.cfg` (`[Runtime] Mode = OpenXR | OpenVR |
Disabled`). The launch options `-peakxr-openxr`, `-peakxr-openvr` and `-peakxr-novr` override it for one
run.

### Linux (Proton)

Steam launch options:

```
WINEDLLOVERRIDES="winhttp=n,b" %command% -force-d3d11
```

The DLL override lets BepInEx load under Proton. `-force-d3d11` makes the game use D3D11, which is the
best-tested path through Proton's OpenXR bridge.
