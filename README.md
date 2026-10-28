# PeakXR

A VR mod for [PEAK](https://store.steampowered.com/app/3527290/PEAK/), built as a BepInEx 5 plugin. It
loads Unity's OpenXR plugin into the game at startup, and can use Valve's OpenVR plugin instead.
It supports PEAK 3.0 and 3.0.1 and still runs on 2.6.b. Work in progress.

PeakXR is MIT licensed and written from scratch. It does not contain code from other PEAK or Unity VR
mods. The XR runtime files it ships come from Unity's packages (Unity Companion License) and Valve's
[unity-xr-plugin](https://github.com/ValveSoftware/unity-xr-plugin) (BSD-3-Clause), and stay under those
licenses; the package carries them in `BepInEx/plugins/PeakXR/licenses/`. The build fetches these files;
they are not in this repository.

## Build

Needs [mise](https://mise.jdx.dev). `PEAK_DIR` in `mise.toml` points at the default Steam library;
override it in `mise.local.toml` or the environment if yours is elsewhere. The plugin compiles against
the game's own assemblies, so building it needs PEAK installed. GitHub CI only checks formatting and runs
the Core tests (`mise run test-core`), which need no game.

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

## Releasing

Releases are built on a machine with PEAK installed. Bump `<Version>` in `Directory.Build.props`, commit
and push to `master`, then run:

```sh
mise run release
```

It checks the tree is clean and pushed and that `v<version>` is a new tag, runs the format check, a
warnings-as-errors build, the tests and the package, then pushes the tag and creates a GitHub release
with `PeakXR-<version>.zip` attached.

## Install

You need:

- PEAK from Steam. PeakXR is tested on 3.0.1, and also loads on 3.0 and 2.6.b.
- A PC VR headset with SteamVR, or another OpenXR runtime such as Meta Quest Link or Virtual Desktop. Only
  SteamVR has been tested.
- [BepInExPack_PEAK](https://thunderstore.io/c/peak/p/BepInEx/BepInExPack_PEAK/), the mod loader.
- The PeakXR zip. There is no public release yet: build it with `mise run package` (see [Build](#build)),
  which writes `dist/PeakXR-<version>.zip`.

To find the game folder, right-click PEAK in your Steam library and pick Manage, then Browse local files.
On Linux it is usually `~/.local/share/Steam/steamapps/common/PEAK`.

### Windows

1. Install BepInEx. With a mod manager (r2modman or the Thunderstore Mod Manager), add BepInExPack_PEAK
   to a PEAK profile. By hand, download BepInExPack_PEAK, open its `BepInExPack_PEAK` folder, and copy
   what is inside (the `BepInEx` folder, `winhttp.dll` and `doorstop_config.ini`) into the game folder,
   next to `PEAK.exe`.
2. Start PEAK once without VR, wait for the title screen, and quit. BepInEx creates its folders and
   `BepInEx/LogOutput.log`.
3. Install PeakXR. In a mod manager, use "Import local mod" and pick the zip. By hand, open the zip and
   copy its `BepInEx` folder into the game folder, merging it with the one already there.
4. Set your OpenXR runtime. For SteamVR, open SteamVR's settings, go to OpenXR and choose
   "Set SteamVR as OpenXR runtime". Other runtimes have their own switch.
5. Start SteamVR (or your runtime), put the headset on, then start PEAK: from Steam if you installed by
   hand, or with "Start modded" in the mod manager.

Most testing so far ran on Linux. On Windows, PEAK starts on Direct3D12, which usually runs VR slower.
For a higher frame rate add `-force-d3d11` to PEAK's launch options in Steam (Properties, General,
Launch options), or pick "PEAK using DX11" when Steam asks. If you launch from a mod manager, put it in
the manager's launch arguments setting instead. The log line `[xr.bootstrap] Graphics API` says which
one is running; see [Check that it works](#check-that-it-works).

### Linux (Steam with Proton)

1. Make sure PEAK runs through Proton, which Steam picks by default for this game.
2. Install BepInEx by hand as in Windows step 1, copying into the game folder. r2modman also runs on
   Linux if you prefer a mod manager.
3. Set PEAK's launch options in Steam (Properties, General, Launch options):

   ```
   WINEDLLOVERRIDES="winhttp=n,b" %command% -force-vulkan
   ```

   The DLL override lets BepInEx load under Proton. `-force-vulkan` makes the game use Vulkan, the only
   graphics API tested in VR so far. On a Proton launch without Wine's VR registry keys, D3D12 and D3D11
   fail to create a device and the game falls back to Vulkan anyway.
4. Start PEAK once without VR, wait for the title screen, and quit.
5. Copy the `BepInEx` folder from the PeakXR zip into the game folder, merging it with the one there.
6. Start SteamVR with your headset, then start PEAK from Steam. On a laptop
   with two GPUs, SteamVR and PEAK have to run on the same GPU, the one that drives the headset.

### Check that it works

- In the headset you stand on the beach with the title menu in front of you.
- `BepInEx/LogOutput.log` has a line starting with `[xr.bootstrap] VR active`. If it says
  `[xr.bootstrap] No XR runtime started`, the runtime was not running when the game started, or it is not
  set as the OpenXR runtime.
- If the view is dimmed and the controllers do nothing, SteamVR's dashboard is open on top of the game.
  Close it with the controller's system button.
- Click both sticks in and hold them for a second to recenter your view and recalibrate eye height. This
  works on every controller. Where the menu button reaches the game (not Quest or Rift under SteamVR),
  holding it does the same, and a tap pauses.

### Playing with friends

Friends don't need PeakXR. Toward players without it, the game's network traffic stays the same as the
unmodded game, and they see you as a normal player. Friends who also run PeakXR, in VR or on a flat
screen, see your head and hands where you hold them.

To play on a flat screen with PeakXR installed, set `Mode = Disabled` in
`BepInEx/config/PeakXR.Preloader.cfg`, or add `-peakxr-novr` to the launch options for one run.

### Uninstall

The preloader copies the XR runtime into the game's data folder, where Unity looks for it, so it is shared by
every mod-manager profile:

- `PEAK_Data/Plugins/x86_64/`: the native runtime DLLs (`UnityOpenXR.dll`, `openxr_loader.dll`,
  `XRSDKOpenVR.dll`, `openvr_api.dll`)
- `PEAK_Data/UnitySubsystems/UnityOpenXR/` and `PEAK_Data/UnitySubsystems/XRSDKOpenVR/`: one
  `UnitySubsystemsManifest.json` each
- `PEAK_Data/PeakXR.Preloader.state`: the list of those files with their hashes

To remove them, start the game once with `-peakxr-novr` (or `Mode = Disabled`) before you uninstall: the
preloader then deletes every file in its list whose bytes are still its own, and the list itself. Or delete
them by hand.

Then delete `BepInEx/plugins/PeakXR`, `BepInEx/patchers/PeakXR`, `BepInEx/config/PeakXR.Preloader.cfg`,
`BepInEx/config/com.thephaseless.peakxr.cfg` and, if present, `BepInEx/config/PeakXR-xr-start.txt`,
`BepInEx/PeakXR-reports` (saved bug reports) and `BepInEx/PeakXR-trace.log`. To remove BepInEx as well,
delete the `BepInEx` folder, `winhttp.dll` and `doorstop_config.ini`, and on Linux clear the launch options.

## Settings

The runtime is set in `BepInEx/config/PeakXR.Preloader.cfg` (`[Runtime] Mode = OpenXR | OpenVR |
Disabled`). The launch options `-peakxr-openxr`, `-peakxr-openvr` and `-peakxr-novr` override it for one
run. With `Mode = OpenXR` and `OpenVRFallback = true` (the default), the OpenVR plugin is installed too:
if OpenXR does not start within `[XR] StartTimeoutSeconds`, the game tries OpenVR, and if that fails as
well it runs flat.

If the VR runtime crashes the game while it starts up, twice in a row, later launches start flat and say so
on the title screen. PeakXR writes `BepInEx/config/PeakXR-xr-start.txt` just before the runtime starts and
deletes it as soon as that call returns, so closing the game or SteamVR while it waits for the headset does
not count. Once SteamVR or the headset is fixed, press Try VR again on the VR settings tab (or delete the
file) and restart. Test launches (`-peakxr-selftest`, `-peakxr-mptest`, `-peakxr-compat`) skip this guard.

When SteamVR quits during play, OpenXR leaves the game running flat. Valve's OpenVR plugin quits the game
instead, as SteamVR expects of its apps.

Every other option is in `BepInEx/config/com.thephaseless.peakxr.cfg`, and in VR also in the VR tab of
the game's settings: camera under `[Camera]`, rendering under `[Render]`, runtime start-up under `[XR]`,
and comfort under `[Comfort]` and `[ComfortEvents]`. [docs/controls.md](docs/controls.md) lists the
controls and the settings behind them. To report a bug, see [docs/debugging.md](docs/debugging.md): click
both sticks in and keep holding them past the recenter, 2.5 s in all, then send the folder it saves.
