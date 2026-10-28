# VR prior art for PEAK (BepInEx, Unity 6000.3.15f1, Mono, URP, PUN2, Proton + native SteamVR)

> **Clean-room rule (PeakXR):** this file names other VR mods only through public product facts: store pages, READMEs, release notes, licenses and user reports. It says nothing about their source code (no file, class or method names, no snippets, no commits, no constants). Implementers must not read, copy or paraphrase source code from PeakVR (Andrey04o), PEAK_VR, LCVR, CWVR or RepoXR. The techniques below come from Unity, Khronos, Valve, Microsoft and Proton documentation and packages. Unity packages (Unity Companion License) and Valve unity-xr-plugin (BSD-3) may be used as dependencies.

Researched 2026-10-19, redacted 2026-10-22. Audience: agents building our PEAK VR mod. Facts marked (verified) were checked against downloaded files or primary sources. (inference) = my reading, not stated by a source.

Facts about the game (verified from the installed game folder, `$PEAK_DIR`): Unity `6000.3.15f1` (UnityPlayer.dll + data.unity3d header), game build 2.6.b / Steam buildid 25739797, Mono (`MonoBleedingEdge/`), `PEAK_Data/Managed/Unity.InputSystem.dll` = com.unity.inputsystem **1.19.0**, URP (`Unity.RenderPipelines.Universal.Runtime.dll`), `UnityEngine.XRModule.dll` + `UnityEngine.VRModule.dll` + `UnityEngine.SubsystemsModule.dll` present, **no** Unity.XR.* managed DLLs, **no** `PEAK_Data/UnitySubsystems/`, no UnityOpenXR.dll. `PEAK_Data/Plugins/x86_64/` has only steam_api64, EOSSDK, opus, webrtc, AudioIn, lib_burst_generated, MemoryWrapper-*. `D3D12/D3D12Core.dll` beside the exe.

---

## 0. Summary

1. Other PEAK VR mods exist (section 1). They are GPL-3.0 or unlicensed, so PeakXR (MIT) takes nothing from their code. Their store pages show that OpenXR injection into PEAK through BepInEx works.
2. Injection approach, from Unity's own packages and docs: a BepInEx **preloader patcher** puts the provider's native plugin and its `UnitySubsystemsManifest.json` (both ship in the Unity package tarball) into the game's data folder before Unity enumerates subsystems; the plugin loads the managed `Unity.XR.Management.dll` + `Unity.XR.OpenXR.dll` with `Assembly.LoadFile`; at runtime it creates the XR Management settings and the loader in code and starts them as in Unity's "manual XR loading" example (XR Plug-in Management manual).
3. Render mode **MultiPass**: the game build has no stereo shader variants (stack.md section 4), so single-pass instanced would draw one eye.
4. Native binaries come straight from the Unity registry tarball, no editor needed: `https://packages.unity.com/com.unity.xr.openxr/-/com.unity.xr.openxr-1.16.1.tgz` -> `package/Runtime/windows/x64/UnityOpenXR.dll` + `package/RuntimeLoaders/windows/x64/openxr_loader.dll` (+ `package/Runtime/UnitySubsystemsManifest.json`).
5. Managed `Unity.XR.*` DLLs are NOT in any tarball (C# source + asmdef only). Options: (a) compile from the tarball source ourselves (done: `Unity.XR.Management` 4.5.4 and `Unity.XR.OpenXR` 1.16.1 build clean against PEAK's `Managed/` with plain `dotnet build`, section 3.4); (b) build a dummy project in Unity 6000.3.15f1 with OpenXR enabled and take them from its `_Data/Managed`.
6. NuGet "Unity.XR.OpenXR/Management" packages on community feeds are **reference stubs** (`throw null`). Compile-time only. Do not ship them.
7. Versions for Unity 6000.3: OpenXR plugin **1.16.1** (Unity docs: "released for 6000.3") or 1.18.0 (6.3 docs "verified" table); XR Management 4.5.4 (4.7.0 listed in 6.3 docs); XR Hands 1.8.1 (docs list 1.9); Input System: use the game's own 1.19.0, ship nothing.
8. Proton: wineopenxr (in `ValveSoftware/Proton/wineopenxr`) bridges Win32 OpenXR to the host runtime and **substitutes `XR_KHR_D3D11_enable`/`D3D12_enable` with `XR_KHR_vulkan_enable`**. D3D11 (DXVK) and D3D12 (vkd3d-proton, since Proton 9.0-3) both supported. Latest Proton: 11.0-2e (11.0-3 RC in test).
9. PEAK defaults to **DX12** (Steam launch option 1 "PEAK using DX12", no args). Force DX11 with `-force-d3d11` (Steam launch option 2, verified in appinfo.vdf); Vulkan is `-force-vulkan`. A public PEAK VR mod README reports +10-27 FPS from DX11.
10. Known Proton/wineopenxr bug (2026-10): `xrEndFrame` composition-layer conversion sizes every layer from layer[0] (XR_ERROR_POSE_INVALID with quad layers after a projection layer, SteamVR 2.17.10 + Proton Experimental 11.0). Unity OpenXR submits one projection layer by default. Do NOT use XR composition/quad layers; use world-space canvases.
11. Do not set `XR_RUNTIME_JSON` under Proton: the only runtime in the prefix is `wineopenxr64.json`, which the registry already points at. Leave the default (no override).
12. Networking: stay vanilla-compatible. Photon `RaiseEvent` with a custom code in the user range (Photon docs: 0-199 are free for games) to `ReceiverGroup.Others`; vanilla clients ignore event codes they do not handle (verified in the decompile, see `NetPoseSync.cs` remarks).
13. Input: the OpenXR plugin's interaction profiles define the controls; they reach both the Input System and `UnityEngine.XR.InputDevices` (OpenXR plugin input docs). See stack.md section 5.
14. Comfort: snap turn default, smooth optional, movement vignette driven by character velocity, room-scale + height calibration, never move the camera for the player (fade/detach for ragdoll, cutscene, spectate). Meta's locomotion guidance, section 5.
15. Fingers: SteamVR OpenXR advertises `XR_EXT_hand_tracking`; reports conflict on whether Index controllers produce active joints. Safe path: analog `trigger`, `squeeze` (grip), `squeeze/force`, `thumbstick/touch`, `trackpad/touch`, `a/b touch` from the Valve Index profile. XR Hands 1.8.1 as optional experiment.
16. Highest-risk item for our setup: wineopenxr + native Linux SteamVR + Unity OpenXR is untested in public sources for Unity mods. Public user reports: a Unity 2022 Mono OpenXR BepInEx mod (RepoXR) works on Linux/Proton via WiVRn (3 reports), SteamVR only over Steam Link with a 30 FPS lock. Valve's **OpenVR** path (`vrclient_x64`, proven for years) is the fallback: `XRSDKOpenVR.dll` + `openvr_api.dll` from Valve's BSD-3 `unity-xr-plugin`.
17. Licenses: PeakVR, LCVR, RepoXR, CWVR are **GPL-3.0** (GitHub license metadata); PEAK_VR declares none. Valve plugin BSD-3. Unity packages: Unity Companion License (usable with Unity projects; the mod community ships the DLLs; do not commit them to our repo, download at build time).
18. Concrete binary list is in section 8.

---

## 1. Existing PEAK VR mods (public product facts only)

| Name | Where | Status | User-visible approach | License |
|---|---|---|---|---|
| **PeakVR** (Andrey04o) | Thunderstore `Andrey04o/PeakVR` | Active. v1.0.0 2026-07-19 ... v1.6.0 2026-10-17; ~6.5k downloads; tested 1.6.0 = PEAK 2.6.b. Public issue titles: mod not usable from Thunderstore, audio not working, support 2.4.b, yellow tint. | README: OpenXR VR mod, OpenVR (SteamVR) runtime option added in 1.6.0, BepInEx preloader + plugin, depends on PEAKLib.Core / PEAKLib.UI / BepInExPack_PEAK. Says it is a fork of LCVR. Limit stated in the README: "you still climb the way the base game does". | GPL-3.0 |
| PEAK_VR (AstienVR) | GitHub `AstienVR/PEAK_VR` | Dead (last push 2025-12-17, "not compatible past 1.44.a") | README: OpenVR default / OpenXR optional, needs the ViGEmBus virtual gamepad driver | none declared |
| UUVR (Raicuparta) via Rai Pal | Steam guide https://steamcommunity.com/sharedfiles/filedetails/?id=3551256479 | Community guides only | Generic Unity VR injector, no per-game logic | MIT (UUVR) |

Thunderstore API scan of all 2547 PEAK packages (`https://thunderstore.io/c/peak/api/v1/package/`) found only PeakVR as a VR mod (BoneWorksOST is an audio mod). Nexus not checked (blocked/no API); no Nexus-only mod surfaced in web search.

Related mods on other games (same author family, public facts only): LCVR (Lethal Company, HDRP, GPL-3.0), RepoXR (R.E.P.O., URP + Photon PUN2, GPL-3.0), CWVR (Content Warning, Unity 6 URP + Photon, GPL-3.0, archived). Their READMEs say runtime dependencies come from a release or can be extracted from a Unity project.

Source repositories of these mods are deliberately not linked here.

---

## 2. Injection approach from public documentation

- **Why a preloader patcher.** Unity registers XR providers from `<Game>_Data/UnitySubsystems/<Name>/UnitySubsystemsManifest.json` when the player starts, and loads the native plugin it names from the data folder's `Plugins` directories. A BepInEx 5 preloader patcher's `Initialize()` runs in the Doorstop entry point before the game assemblies load (BepInEx preloader docs), so it can write both in time; a plugin `Awake` is too late. The manifest itself ships in the OpenXR package tarball (`package/Runtime/UnitySubsystemsManifest.json`), as does Valve's for OpenVR.
- **Managed assemblies.** BepInEx-loaded assemblies are not in Mono's probing path, so the plugin loads them with `Assembly.LoadFile` and answers `AppDomain.AssemblyResolve` for them (Microsoft Learn), before any method touching their types is JIT-compiled.
- **XR Management in code.** The game ships no XR settings asset. XR Plug-in Management documents manual initialization (`XRManagerSettings.InitializeLoader`/`InitializeLoaderSync`, then `StartSubsystems`, with automatic loading off). `XRGeneralSettings`, `XRManagerSettings` and the loader are ScriptableObjects, created with `ScriptableObject.CreateInstance`.
- `XRGeneralSettings.AttemptStartXRSDKOnBeforeSplashScreen` is a `[RuntimeInitializeOnLoadMethod]` that Unity only invokes for assemblies in `ScriptingAssemblies.json`, so BepInEx-loaded assemblies are not auto-started (verified in the Management 4.5.4 source); manual init is required.
- **OpenXR settings.** `OpenXRSettings.renderMode`, `depthSubmissionMode`, `features` (interaction profiles enabled) are documented on `OpenXRSettings`/`OpenXRFeature`. In a player build `OpenXRSettings.Instance` creates a default ScriptableObject at runtime; the mod fills `features` itself (verified in the package source).
- **Stereo mode.** A game built without an XR loader has its `STEREO_INSTANCING_ON`/multiview variants stripped (stack.md section 4), so single-pass instanced renders broken materials; multipass uses the same variants as mono. Multipass costs ~2x CPU draw submission (inference).
- **UI.** Overlay canvases do not render in the headset; world-space canvases (`RenderMode.WorldSpace`) do (Unity UI docs). Our design: world-space panels.
- **Multiplayer with vanilla clients.** Photon `RaiseEvent` with an unused code, sent to `Others`; vanilla clients ignore unknown codes. Announce VR presence through a player custom property (Photon docs), refreshed on `OnPlayerEnteredRoom`.
- **Licensing.** GPL-3.0 on the existing mods means copied code would make our mod GPL-3.0. PeakXR is MIT and clean-room: implement from Unity package docs and sources, Valve's BSD-3 plugin, Khronos specs and the game decompile.

---

## 3. Unity packages, versions, how to get the binaries

### 3.1 Versions (queried `https://packages.unity.com/<package>` registry JSON and Unity docs)
Unity docs, 6.3 LTS page `https://docs.unity3d.com/6000.3/Documentation/Manual/pack-safe.html` ("released packages"): openxr **1.18**, management **4.7**, hands **1.9**, inputsystem **1.20**, XRI **3.6** (core-utils and URP not listed). The OpenXR package page `https://docs.unity3d.com/6000.3/Documentation/Manual/com.unity.xr.openxr.html` says "Package version **1.16.1** is released for Unity Editor version 6000.3" (cached/older). Both pages exist; the first is newer. Registry state today:

| Package | Versions in registry (stable tail) | Notes |
|---|---|---|
| com.unity.xr.openxr | 1.15.1 (2025-07-25, unity 2021.3), 1.16.0, **1.16.1** (2025-12-10, unity 2022.3), 1.17.0, 1.17.1, **1.18.0** (2026-08-11, unity 6000.0), 1.19.0-pre.2 | |
| com.unity.xr.management | 4.5.0, 4.5.3, 4.5.4, 4.6.0, 4.6.1, 4.7.0 | |
| com.unity.xr.hands | 1.7.3, 1.8.0, **1.8.1** (2026-07-23), 1.9.0 (unity 6000.0, needs com.unity.collections 2.2.0), 1.10.0-pre.2 | has native `Runtime/windows/x64/UnityOpenXRHands.dll`. |
| com.unity.inputsystem | 1.14.2 ... 1.19.0, 1.20.0, 1.20.1 | **Game ships 1.19.0 in `Managed/Unity.InputSystem.dll`. Do not ship another.** OpenXR package only needs >= 1.6.3 (defines `INPUT_SYSTEM_BINDING_VALIDATOR`). |
| com.unity.xr.core-utils | 2.5.3, 2.6.0 | Not required by Management/OpenXR runtime; only XRI/hands samples. |
| com.unity.xr.interaction.toolkit | 3.0.9 ... 3.6.1 | Not needed. |

Recommendation: **OpenXR 1.16.1 + Management 4.5.4** ("released for 6000.3" per Unity docs). 1.18.0 is a drop-in candidate to test later (natives: UnityOpenXR.dll 919040 B, openxr_loader.dll 2373632 B).

### 3.2 Registry download (no editor, no auth)
`https://packages.unity.com/<pkg>/-/<pkg>-<ver>.tgz` -> HTTP 302 to `https://cdn.packages.unity.com/tarballs/<pkg>/<ver>/<sha1>.tgz` (verified; shasum in registry JSON `versions[v].dist.shasum`). Sizes: openxr 1.15.1 13 MB, 1.16.1 40 MB, 1.18.0 22 MB; management 4.5.4 220 KB; hands 1.8.1 23 MB; core-utils 2.5.3 1.7 MB. Tarball root is `package/`.

```bash
curl -sL https://packages.unity.com/com.unity.xr.openxr/-/com.unity.xr.openxr-1.16.1.tgz | tar -xz -C oxr
curl -sL https://packages.unity.com/com.unity.xr.management/-/com.unity.xr.management-4.5.4.tgz | tar -xz -C mgmt
```

### 3.3 What `com.unity.xr.openxr` tarball contains (verified 1.15.1, 1.16.1, 1.18.0)
- Native, Windows x64: `package/Runtime/windows/x64/UnityOpenXR.dll`, `package/RuntimeLoaders/windows/x64/openxr_loader.dll`. (Also osx dylib, android .so, `APILayers~/WindowsLayers/x64/XrApiLayer_{core_validation,best_practices_validation,api_dump}.dll`, MockRuntime, RuntimeDebugger natives.)
- `package/Runtime/UnitySubsystemsManifest.json` (the provider manifest; `"version"` field equals package version, not validated by anyone).
- Managed: **C# source only** (`Runtime/**/*.cs` + `Unity.XR.OpenXR.asmdef`, `Editor/`, `Tests/`, `Samples~/`). No managed DLL anywhere.
- Hashes (SHA-256, first 20 hex): 1.16.1 `UnityOpenXR.dll` a1d3e2123e52410a2e96 (863744 B), `openxr_loader.dll` fed37528996fd91960ab (2373632 B). 1.15.1 `UnityOpenXR.dll` 7e9d1597ec0f821499c5 (834048 B). 1.18.0 `UnityOpenXR.dll` a344bf5428d70d41e91b (919040 B), `openxr_loader.dll` f7384429c16288d9c2c1 (2373632 B).
- Third-party in package: OpenXR-SDK-Source (Apache-2.0). Package license: Unity Companion License / Unity Package Distribution License (`LICENSE.md`). The mod ecosystem redistributes the DLLs; legal grey. Do not vendor into our git repo; fetch in build.
- `com.unity.xr.management` tarball: C# only, no natives. `com.unity.xr.hands`: C# + `Runtime/windows/x64/UnityOpenXRHands.dll`.
- Compile-time reference stubs exist on community NuGet feeds; verified with ilspycmd that `OpenXRLoader()` there is `throw null;` (reference assemblies, never ship).

### 3.4 Compile the managed side ourselves (tested, no Unity editor)
Tested 2026-10-19 with .NET 9 SDK, target `netstandard2.1`, references = PEAK's `Managed/` DLLs:
1. `Unity.XR.Management` 4.5.4: all `Runtime/**/*.cs`, refs `UnityEngine.CoreModule`, `XRModule`, `SubsystemsModule`, `UnityEngine.dll`. Build succeeded, 0 warnings.
2. `Unity.XR.OpenXR` 1.16.1: compile the 170 `.cs` files under `Runtime/` whose nearest asmdef is `Unity.XR.OpenXR.asmdef` (skip `MockRuntime`, `RuntimeDebugger`, `ConformanceAutomation`, `MetaQuest`, `OculusQuest`, `TestTooling`, `Features/MetaHandMeshData` which have their own asmdefs). `AllowUnsafeBlocks=true`. Refs: `UnityEngine.dll`, `CoreModule`, `XRModule`, `SubsystemsModule`, `VRModule`, `IMGUIModule`, `VideoModule`, `AudioModule`, `PhysicsModule`, `UIModule`, `InputLegacyModule`, `InputModule`, `Unity.InputSystem.dll`, `Unity.RenderPipelines.Core.Runtime.dll`, `Unity.RenderPipelines.Universal.Runtime.dll`, `Unity.Mathematics.dll`, `Unity.Collections.dll`, `Unity.Burst.dll`, and our built `Unity.XR.Management.dll`. Defines (from the asmdef versionDefines + Unity macros): `UNITY_ANALYTICS;INPUT_SYSTEM_POSE_VALID;INPUT_SYSTEM_BINDING_VALIDATOR;UNITY_RENDER_PIPELINES_UNIVERSAL;UNITY_VIDEO;SPACEWARP_RIGHT_HAND_NDC_SUPPORTED;UNITY_STANDALONE_WIN;UNITY_STANDALONE;UNITY_2019_3_OR_NEWER;UNITY_2020_1_OR_NEWER;UNITY_2021_2_OR_NEWER;UNITY_2022_1_OR_NEWER;UNITY_2022_3_OR_NEWER;UNITY_2023_1_OR_NEWER;UNITY_6000_0_OR_NEWER;UNITY_6000_1_OR_NEWER`. Missing `UNITY_2019_3_OR_NEWER` gave 3 errors in `input/OpenXRDevice.cs`; with it, 0 errors, 0 warnings, output 251 KB.
3. `InitXRSDK`/`Start` on `XRGeneralSettings` are private/internal in 4.5.4 source. Use the public manual-init path instead: `xrManagerSettings.InitializeLoaderSync(); xrManagerSettings.StartSubsystems();`.

### 3.5 Input System, hands, XRI
- Input System: use the game's 1.19.0. It already contains `UnityEngine.InputSystem.XR.*` (`XRHMD`, `XRController`, `TrackedPoseDriver`, `XRDeviceDescriptor`). Valve Index layout comes from `ValveIndexControllerProfile` in the OpenXR package (control names, see Unity source `Runtime/Features/Interactions/ValveIndexControllerProfile.cs`: `squeeze`, `squeezeForce`, `squeezeClicked`, `thumbstick`, `thumbstickClicked`, `thumbstickTouched`, `trackpad`, `trackpadTouched`, `trackpadForce`, `primaryButton/Touch` (A), `secondaryButton/Touch` (B), `devicePose`/`pointer`, haptic).
- XR Hands: 1.8.1 tarball `package/Runtime/windows/x64/UnityOpenXRHands.dll` + C# `Unity.XR.Hands.asmdef`, `Runtime/OpenXR/HandTracking.cs` (OpenXR feature), `OpenXRHandProvider.cs`. Needs the `HandTracking` OpenXR feature added to `OpenXRSettings.features` and the DLL in `PEAK_Data/Plugins/`. Deps: inputsystem, mathematics, core-utils 2.2.0, management 4.0.1 (1.9+ adds Unity.Collections 2.2.0).
- XRI / CoreUtils / SpatialTracking: not needed. The game's Input System has its own `TrackedPoseDriver`, and we register our own bindings.

---

## 4. OpenXR under Proton with native Linux SteamVR

### 4.1 Mechanism (verified in Proton source)
`https://github.com/ValveSoftware/Proton/tree/HEAD/wineopenxr`:
- `wineopenxr64.json` = `{"runtime":{"library_path":"C:\\windows\\system32\\wineopenxr.dll"}}`; `proton` script copies it to `<prefix>/drive_c/openxr/wineopenxr64.json` (`proton` lines ~1159-1160). Registry `HKLM\Software\Khronos\OpenXR\1\ActiveRuntime` points the Windows OpenXR loader (the game's `openxr_loader.dll` from the Unity package) at it. Proton's `steam_helper`/`vrclient` populate the VR registry keys (`HKCU\Software\Wine\VR`: `state`, `openxr_vulkan_instance_extensions`, `openxr_vulkan_device_extensions`, `PROTON_VR_RUNTIME`) on VR startup; when Steam does not do it, GE-Proton 10-33 patch (Proton issue #8256 comment by GloriousEggroll) rebuilds the keys from wineopenxr.
- `openxr.c` `substitute_extensions[]`: `XR_KHR_D3D11_enable` -> `XR_KHR_vulkan_enable`; `XR_KHR_D3D12_enable` -> `XR_KHR_vulkan_enable`; `XR_KHR_win32_convert_performance_counter_time` -> `XR_KHR_convert_timespec_time`. `xrGetD3D11GraphicsRequirementsKHR`/`xrGetD3D12GraphicsRequirementsKHR` always available. Interop headers `dxvk-interop.h`, `vkd3d-proton-interop.h`: D3D11 swapchain via DXVK, D3D12 via vkd3d-proton. Release notes: OpenXR since Proton 5.13-5; D3D12 in OpenXR added in **9.0-3**; OpenXR 1.1.36 in 9.0-2; early 32-bit OpenXR in Experimental/11.0-3 RC (`PROTON_USE_WOW64=1`, irrelevant, PEAK is 64-bit).
- All OpenXR functions incl. `xrCreateHandTrackerEXT`/`xrLocateHandJointsEXT` and `XR_EXT_hand_tracking`, `XR_EXT_hand_tracking_data_source` are thunked (generated `openxr_thunks.c`), so extension availability = the host runtime's.
- Current Proton releases (GitHub): 11.0-2e (2026-10-09), 11.0-2 (2026-08-21, "Latest"), 11.0-1, 10.0-4. 11.0-3 RC in testing (issue #10236). Recent VR notes: 11.0-1 "Improved VR support so that No Man's Sky VR mode is playable again", "Fixed VR controller tracking in Microsoft Flight Simulator".

### 4.2 Does it work with Unity OpenXR games?
- Evidence for: RepoXR (Unity 2022, Mono, BepInEx, Unity OpenXR) reported working on Fedora KDE + Quest 3 over WiVRn "No manual configuration required" (2025-07-13), Pico 4 NixOS WiVRn with launch option, Quest 3 CachyOS: https://db.vronlinux.org/games/3241660.html. Same page: **SteamVR via Steam Link only, locked 30 FPS + chromatic-aberration-like text** (2026-01-12). Proton version not stated in any report.
- No public report found for any PEAK VR mod on Linux + native SteamVR (web search, public issue titles). So our combination is unverified: native-Linux SteamVR + wineopenxr + Unity OpenXR D3D11.
- Alternative that Valve explicitly supports: **OpenVR** through `vrclient_x64` (Proton bridges `openvr_api.dll`). Valve repo has the DLLs checked in: https://github.com/ValveSoftware/unity-xr-plugin `com.valve.openvr/Runtime/x64/XRSDKOpenVR.dll` (599040 B) and `com.valve.openvr/Runtime/x64/openvr_api.dll` (836760 B), v1.2.4 (2025-10-29, OpenVR SDK 2.12.14, DX12 support added), BSD-3, with the `UnitySubsystemsManifest` (`OpenVR Display`/`OpenVR Input`) and the managed C# (`OpenVRLoader.cs`, `OpenVRSettings.cs`) in the same package.

### 4.3 Env vars / launch options (collected; none officially documented as a single recipe)
- Steam launch option (DX11): `-force-d3d11 %command%` (PEAK's own Steam launch entries, below).
- Mod loader under Proton: BepInEx via Doorstop needs `WINEDLLOVERRIDES="winhttp=n,b" %command%` when launched from Steam directly, or r2modman/Gale "Start modded" which adds `--doorstop-enable true --doorstop-target "Z:/.../BepInEx/core/BepInEx.Preloader.dll"` (seen in the vronlinux report above). BepInExPack_PEAK 5.4.75301 is the Thunderstore dependency.
- Runtime visibility in the Steam pressure-vessel container: for non-SteamVR runtimes (WiVRn/Monado) `PRESSURE_VESSEL_FILESYSTEMS_RW=$XDG_RUNTIME_DIR/wivrn/comp_ipc` (or `monado_comp_ipc`) and sometimes `PRESSURE_VESSEL_IMPORT_OPENXR_1_RUNTIMES=1`. Do not combine with `XR_RUNTIME_JSON` (Arch forum report). For native SteamVR set "SteamVR as OpenXR runtime" in SteamVR settings (Developer tab) so `~/.config/openxr/1/active_runtime.json` points at it. No source states the exact needs for SteamVR; test empirically.
- Library on another disk: pressure-vessel must see the game path or SteamVR cannot load the action manifest (Proton #8473: `PRESSURE_VESSEL_FILESYSTEMS_RW=/data steam ...`; controllers dead with app name `wine64-preloader`). Keep PEAK in the default Steam library, or add the path.
- Debug: `PROTON_LOG=+openxr,+vrclient %command%`. Mod side: `UnityOpenXR.dll` exports `DiagnosticReport_GenerateReport` (Unity OpenXR package source).
- Do not set `XR_RUNTIME_JSON` from the mod on Proton. Make any runtime override optional and empty by default; also don't assume `steamxr_win64.json` exists.

### 4.4 Known issues
- wineopenxr `xrEndFrame` layer-conversion bug: https://github.com/ValveSoftware/Proton/issues/291 (comment transferred from #10275, 2026-10-17): conversion in `wineopenxr/openxr_thunks.c`/`make_openxr` uses first layer's size for all layers; quad layers after a projection layer -> `XR_ERROR_POSE_INVALID`; reproduced with Proton Experimental 11.0, SteamVR 2.17.10, PSVR2. Workaround: put a small quad first. Unity OpenXR: only projection layer unless composition-layer package is used. **Avoid OpenXR quad layers for HUD/menus.**
- Steam Frame ARM64: x86-64 Windows OpenXR apps crash in `xrCreateInstance` under Proton ARM64 (#10211, closed). N/A for x86-64 desktop.
- SteamVR on Linux is considered poorly maintained (Arch Wiki Virtual_reality page, as of Jan 2025 "many unfixable issues"); Monado/WiVRn are common alternatives. Our user runs native SteamVR, so expect quirks.
- Non-Steam VR needs GE-Proton (10-33) patches; irrelevant for Steam-launched PEAK.

### 4.5 DX11 vs DX12 vs Vulkan for PEAK
- Steam launch options defined for app 3527290 (verified in the Steam client's `appcache/appinfo.vdf`): `option1` "PEAK using DX12" executable `PEAK.exe` no args (default for windows); `option2` "PEAK using DX11" arguments `-force-d3d11`; `option3` "PEAK using Vulkan" arguments `-force-vulkan`; also a `linux` "Steam Deck Default" entry with `-force-vulkan` (internal-playtesting config; PEAK has no public native Linux build).
- So default = **DX12**; force DX11 with `-force-d3d11` (standard Unity player flag). Also `-force-vulkan`, `-force-d3d12`. A public PEAK VR mod README says "-force-d3d11 can give an extra 10-27 FPS"; its 1.2.0 release notes mention a desktop mirror fix on Vulkan (so Vulkan VR works on native Windows).
- Recommendation for Proton: `-force-d3d11` (DXVK; wineopenxr maps `XR_KHR_D3D11_enable` -> `XR_KHR_vulkan_enable`). D3D12 via vkd3d-proton is supported since Proton 9.0-3 but is the heavier, less-exercised path; PEAK's Steam forum threads mention DX12 crashes (Tropics biome) with DX11/Vulkan as workarounds (https://steamcommunity.com/app/3527290/discussions/0/603033240727548733/ is only tangential). Our mod should detect the graphics API (`SystemInfo.graphicsDeviceType`) and warn if it is Direct3D12 or Vulkan.
- Superseded by testing (2026-10-21, `docs/test-report.md`, "Graphics API"): under Proton launched without Wine's VR registry keys, D3D11 and D3D12 both failed to create an OpenXR device and Unity fell back to Vulkan, which is the API every Proton VR run used. `GraphicsAdvice` (`src/PeakXR.Core/GraphicsAdvice.cs`) now advises `-force-vulkan` under Proton and `-force-d3d11` instead of Direct3D12 on Windows; Vulkan gets no warning.
- OpenXR graphics binding in Unity is chosen from the active GfxDevice (D3D11/D3D12/Vulkan) automatically; no setting needed.

---

## 5. Comfort and locomotion (concise; sources: Meta https://developers.meta.com/horizon/design/locomotion-best-practices/ and https://developers.meta.com/vr/design/locomotion-comfort-usability/; rest is general VR practice, not verified here)

Defaults for a first release:
- **Turning**: snap turn (30-45 deg) on by default; smooth turn opt-in with speed setting. Both on the right stick; deadzone 0.15.
- **Locomotion**: PEAK is first-person physical walking/climbing with stamina; keep stick-relative movement with a **head-relative or hand-relative** direction option. Teleport is awkward for a physics climber; offer smooth only plus strong comfort. Meta: acceleration brief/rare, quantized speeds, keep speeds near real walk speed.
- **Tunneling vignette**: drive by character velocity and turn rate, ease in fast and out slower. Place it in front of the eyes in head space (not screen-fixed UI) so it is correct per headset FOV (Meta notes screen-fixed vignette covers different FOV per HMD).
- **Height/scale calibration**: floor-level tracking origin; recenter on spawn and on a button hold; optional arm-length calibration; seated mode = raise origin offset instead of rescaling; crouch from physical head height.
- **No forced camera motion**: never drive head rotation/position from animation or physics (screen shake, head bob, recoil, fall camera dip). Head roll/tilt only shown on avatars, fog/stamina effects kept in a UI plane.
- **Third-person/ragdoll/cutscene/spectate**: PEAK switches to ragdoll, death, passed-out, ending cutscenes and spectate. Options: fade to black briefly then place the head at the ragdoll head / spectate pivot with yaw locked and no pitch/roll follow; during cutscenes keep the HMD pose-driven view in a fixed rig and show the cutscene camera on a world-locked screen. Always fade to black on hard cuts and teleports.
- **Climbing specifically**: The Climb (Crytek) moves the world by gripping holds; hands drive velocity, reviewer found grip-to-move itself comfortable but drops/launches uncomfortable (RoadtoVR hands-on https://roadtovr.com/hands-on-the-climb-with-oculus-touch-reaches-new-heights/). A 2020-2023 review reports arm-tracked grab locomotion went from ~3% of VR apps (2016) to >30% yearly 2020-2023 (From Teleportation to Climbing, BCU: https://www.open-access.bcu.ac.uk/15705/1/From%20Teleportation%20to%20Climbing%20%20A%20Review%20of%20Locomotion%20Techniques%20in%20the%20Most%20Used%20Commercial%20Virtual%20Reality%20Applications.pdf). Gorilla Tag-style arm swinging: hand collider pushes the body opposite to hand motion relative to the body (no source found in this research; general knowledge). Design implications for PEAK: tie hand motion to stamina/grip (hold = grip button), avoid any extra camera acceleration on grab/release, keep body velocity continuous, clamp max launch, vignette on falls/launch.
- **Settings to expose**: snap/smooth, angle, speed, vignette on/off/strength, deadzones, left/right dominant, seated/standing, height offset, arm scale, hide controllers, HUD position (wrist vs head-locked), desktop mirror mode.

---

## 6. Hand and finger tracking via OpenXR

- Unity side: `XR_EXT_hand_tracking` is exposed by **com.unity.xr.hands** (`Runtime/OpenXR/HandTracking.cs` feature id; `XRHandSubsystem` gives 26 joints per hand). XR Hands 1.8.1 supports Unity 2022.3; 1.9.0 requires 6000.0. Needs `UnityOpenXRHands.dll` in `PEAK_Data/Plugins/` and the managed DLLs; managed `Unity.XR.Hands` also has to be compiled from the tarball (C#). Hand Interaction Profile (`XR_EXT_hand_interaction`, `HandInteractionProfile.cs`) is in the OpenXR package itself.
- SteamVR OpenXR runtime and Index controllers: sources disagree.
  - Unity Discussions reply by VRDave_Unity (Unity 2021, OpenXR 1.10.0, XR Hands 1.5.0-pre.1): "The Index controllers should have support for the OpenXR XR_EXT_hand_tracking extension via SteamVR"; worked with the HandVisualizer sample (https://discussions.unity.com/t/seeking-assistance-on-integrating-valve-index-controllers-with-unity-openxr-for-finger-tracking/949483).
  - 2020 SteamVR OpenXR forum thread: runtime advertised the extension but joints inactive/zero; later a poster reported it working with Index (https://steamcommunity.com/app/250820/discussions/8/2798377275375245919/). Khronos forum: finger curl from Index through `XR_EXT_hand_tracking` worked in UE 4.27 for one user, `isActive=false` for another.
  - A SteamVR beta note says Quest hand tracking is forwarded via skeletal input and `XR_EXT_hand_tracking`, "does not introduce full hand tracking support" (store.steampowered.com news for app 250820).
  - Not verified against a current SteamVR build. Test at runtime: `OpenXRRuntime.IsExtensionEnabled("XR_EXT_hand_tracking")` plus `xrLocateHandJointsEXT.isActive`, and fall back.
- Under Proton: wineopenxr passes `XR_EXT_hand_tracking` calls to the host loader (thunks present), no filtering found in `make_openxr` beyond an `UNSUPPORTED_EXTENSIONS` list (debug_report etc.; not hand tracking).
- Controller-estimated finger curls: SteamVR's skeletal input documents "estimated" skeletal tracking for controllers, where the hand pose is inferred from the buttons, triggers and capacitive sensors it touches (Valve OpenVR wiki, SteamVR Skeletal Input). The Valve Index, Oculus Touch and Touch Pro profiles expose analog `trigger`, `squeeze`, and touch controls (`triggerTouched`, `thumbstickTouched`, `primaryTouched`), enough to drive index, the other fingers and the thumb. Smooth the result.
- Optional XR Hands + Index: use joints only when `isActive` for >1 s; else use analog curls.

---

## 7. Risks and open questions (for the next agent)

1. Runtime-test the compiled-from-source `Unity.XR.OpenXR.dll`/`Unity.XR.Management.dll` (section 3.4). If anything fails, build them from a dummy Unity 6000.3.15f1 project.
2. First Proton run: confirm `UnitySubsystems/UnityOpenXR` manifest is found inside the Proton prefix (it is just the game dir; fine) and `openxr_loader.dll` finds `wineopenxr64.json`; check `Player.log` + `PROTON_LOG=+openxr`. The prefix does not exist yet (`compatdata/3527290` absent): game never launched on this machine.
3. Decide on SteamVR OpenXR vs OpenVR backend for the Proton run. Keep both selectable.
4. DX11 forcing: add `-force-d3d11` to launch options; the mod could log `SystemInfo.graphicsDeviceType`.
5. BepInEx on Proton needs the winhttp override or a launcher; r2modman "Start modded" works per the vronlinux reports.
6. Photon event code: pick one in the user range that nothing in the game, Zorro or PUN raises (check the decompile: Zorro `CustomCommands`, `PhotonShim`, PUN `PunEvent`).
7. License: PeakXR stays MIT and clean-room; never copy from the GPL mods.

---

## 8. Concrete binary list and sources (recommended injection approach)

Approach: BepInEx preloader patcher (`BepInEx/patchers/<Mod>/`) + plugin, MultiPass, OpenXR 1.16.1, D3D11.

| # | File | Install location | Source |
|---|---|---|---|
| 1 | `UnityOpenXR.dll` (863744 B) | `PEAK_Data/Plugins/x86_64/UnityOpenXR.dll` | tarball `com.unity.xr.openxr-1.16.1.tgz` -> `package/Runtime/windows/x64/UnityOpenXR.dll` |
| 2 | `openxr_loader.dll` (2373632 B) | `PEAK_Data/Plugins/x86_64/openxr_loader.dll` | same tarball -> `package/RuntimeLoaders/windows/x64/openxr_loader.dll` |
| 3 | `UnitySubsystemsManifest.json` | `PEAK_Data/UnitySubsystems/UnityOpenXR/UnitySubsystemsManifest.json` | same tarball -> `package/Runtime/UnitySubsystemsManifest.json` |
| 4 | `Unity.XR.Management.dll` (~18 KB) | the plugin's own folder, then `Assembly.LoadFile` | compile `com.unity.xr.management-4.5.4.tgz` `Runtime/**/*.cs` (section 3.4) |
| 5 | `Unity.XR.OpenXR.dll` (~250 KB) | same | compile `com.unity.xr.openxr-1.16.1.tgz` `Runtime/` (section 3.4) |
| 6 | (fallback backend) `XRSDKOpenVR.dll` (599040 B), `openvr_api.dll` (836760 B) + OpenVR manifest | `PEAK_Data/Plugins/x86_64/`, `PEAK_Data/UnitySubsystems/XRSDKOpenVR/UnitySubsystemsManifest.json` | https://github.com/ValveSoftware/unity-xr-plugin `com.valve.openvr/Runtime/x64/` (BSD-3) |
| 7 | (optional) `UnityOpenXRHands.dll` + managed `Unity.XR.Hands.dll` | `PEAK_Data/Plugins/x86_64/` / plugin folder | `com.unity.xr.hands-1.8.1.tgz` -> `package/Runtime/windows/x64/UnityOpenXRHands.dll`, compile `Runtime/` |
| - | Input System | none | game's own `Unity.InputSystem.dll` 1.19.0 |
| - | Compile-time refs | csproj only | `PEAK_Data/Managed/*.dll`; BepInEx `5.4.x`; BepInExPack_PEAK 5.4.75301; Harmony |

Download commands (verified reachable, no auth):
```
https://packages.unity.com/com.unity.xr.openxr/-/com.unity.xr.openxr-1.16.1.tgz    (sha1 ef0033a586bf33cb236bfe77cb661fe1b2882748)
https://packages.unity.com/com.unity.xr.management/-/com.unity.xr.management-4.5.4.tgz
https://packages.unity.com/com.unity.xr.hands/-/com.unity.xr.hands-1.8.1.tgz
```

## 9. URL index
- PeakVR store page https://thunderstore.io/c/peak/p/Andrey04o/PeakVR/ (README, changelog, issue titles only)
- Valve OpenVR Unity plugin https://github.com/ValveSoftware/unity-xr-plugin
- Unity registry https://packages.unity.com/ ; docs https://docs.unity3d.com/6000.3/Documentation/Manual/pack-safe.html , https://docs.unity3d.com/6000.3/Documentation/Manual/com.unity.xr.openxr.html , XR Plug-in Management manual https://docs.unity3d.com/Packages/com.unity.xr.management@4.5/manual/EndUser.html
- Proton wineopenxr https://github.com/ValveSoftware/Proton/tree/HEAD/wineopenxr ; issues #291 (layer bug), #8256 (decouple OpenVR/OpenXR from Steam, GE-Proton patch), #8473 (VR controllers/appname/pressure-vessel), #10236 (11.0-3 RC) ; releases https://github.com/ValveSoftware/Proton/releases
- VR on Linux DB report https://db.vronlinux.org/games/3241660.html
- Meta locomotion guidance https://developers.meta.com/horizon/design/locomotion-best-practices/
