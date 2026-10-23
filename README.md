# PeakXR

A VR mod for [PEAK](https://store.steampowered.com/app/3527290/PEAK/), built as a BepInEx 5 plugin. It
loads Unity's OpenXR plugin into the game at startup, and can use Valve's OpenVR plugin instead.
It supports PEAK 3.0 and 3.0.1 and still runs on 2.6.b. Work in progress.

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
`decomp/` after a game update (keeping the old one in `decomp.prev/`), and `mise run compat-check` prints the game and
Unity versions and checks every patch target and name lookup against the decompile.

## Install

1. Install [BepInExPack_PEAK](https://thunderstore.io/c/peak/p/BepInEx/BepInExPack_PEAK/).
2. Install the PeakXR zip with a mod manager, or copy its `BepInEx` folder into the game folder.
3. Start SteamVR (or another OpenXR runtime) before the game.

The runtime is set in `BepInEx/config/PeakXR.Preloader.cfg` (`[Runtime] Mode = OpenXR | OpenVR |
Disabled`). The launch options `-peakxr-openxr`, `-peakxr-openvr` and `-peakxr-novr` override it for one
run. With `Mode = OpenXR` and `OpenVRFallback = true` (the default), the OpenVR plugin is installed too:
if OpenXR does not start within `[XR] StartTimeoutSeconds`, the game tries OpenVR, and if that fails as
well it runs flat. `BepInEx/LogOutput.log` shows the outcome on a line starting with
`[xr.bootstrap] VR active` or `[xr.bootstrap] No XR runtime started`.

In the headset, hold the menu button for a second to recenter your view and recalibrate eye height.
Every other option is in `BepInEx/config/com.thephaseless.peakxr.cfg`, and in VR also in the VR tab of
the game's settings: camera under `[Camera]`, rendering under `[Render]`, runtime start-up under `[XR]`,
and comfort under `[Comfort]` and `[ComfortEvents]`. [docs/controls.md](docs/controls.md) lists the
controls and the settings behind them. To report a bug, see [docs/debugging.md](docs/debugging.md): hold Menu
on one controller and click the other stick for 1.5 s, then send the folder it saves.

### Linux (Proton)

Steam launch options:

```
WINEDLLOVERRIDES="winhttp=n,b" %command% -force-vulkan
```

The DLL override lets BepInEx load under Proton. `-force-vulkan` makes the game use Vulkan, the only
graphics API tested in VR so far. On a Proton launch without Wine's VR registry keys, D3D12 and D3D11
fail to create a device and the game falls back to Vulkan anyway.
