# PeakXR stack decisions

Researched 2026-10-19. Audience: agents building PeakXR. Builds on `docs/research/vr-prior-art.md` (not repeated). Tags: (verified) = checked against a downloaded file, registry, decompile or a test I ran; (doc) = vendor documentation; (community) = forum or wiki, unconfirmed; (inference) = my reading. Versions come from live registry queries on this date.

Clean-room: nothing here comes from PeakVR, PEAK_VR, LCVR, CWVR or RepoXR source. About those mods: public READMEs, Thunderstore pages and issue titles only.

---

## 0. Decisions at a glance

1. **Stay on BepInEx 5.4.23.x** (ships as 5.4.23.3 inside BepInExPack_PEAK 5.4.75301). BepInEx 6 plugins are a different ABI; PEAK's whole mod ecosystem is v5.
2. **Move to .NET 10 SDK (10.0.402, LTS) now.** .NET 9 reaches end of support 2026-11-10. C# 14 with `netstandard2.1` plugin works (tested). Add PolySharp 1.16.0.
3. **XR packages: keep OpenXR 1.16.1 + Management 4.5.4 until the first Proton run passes, then A/B 1.18.0 + 4.6.1** (both compile clean with `tools/fetch-xr-deps.sh`, verified). Skip 1.17.x.
4. **MultiPass is the only option, and the game's own data proves it**: 0 of 20,326 compiled shader variants contain a stereo keyword. SRP foveation is also impossible (its keyword is stripped too). URP stripped the XR mirror and occlusion-mesh shaders: custom mirror shader needed.
5. **Input: keep `UnityEngine.XR.InputDevices` as the primary source.** The OpenXR plugin builds its actions from interaction-profile configs, not from our `InputAction`s, so both paths see the same controls. InputDevices does not depend on the game's Input System configuration.
6. **Proton: Proton Experimental first, Proton-CachyOS `-slr` second, Proton 11.0-2e as the stable reference.** OpenXR via wineopenxr is Valve's stated direction; keep OpenVR as the fallback.
7. **Tooling:** xunit.v3.mtp-v2 4.0.2, coverlet.MTP 10.1.0, CSharpier 1.3.0, Meziantou + BepInEx + BUTR Harmony + Microsoft.Unity analyzers, tcli 0.2.4 via `dotnet:tcli` (works with roll-forward).
8. **Networking:** `RaiseEvent` code in 100-199, `byte[]`, unreliable, `TargetActors` = modded peers only, <=30 Hz (matches the game's `SendRate=30`), payload well under 1000 B.
9. **Fingers:** analog curls first (ship). XR Hands 1.9.0 data-source path is a later optional upgrade for Index per-finger data.

---

## 1. Mod loader: BepInEx 5 vs 6, HarmonyX

### What exists (verified 2026-10-19)

| Item | Value |
|---|---|
| BepInEx 5 latest release | **5.4.23.5**, 2026-02-08 (Doorstop 4.5.0; "fix log writer errors for Unity 6" PR #1264; fix for Unity 2022.3.62f3 `get_graphicsDeviceID`). Prior: .4 2025-09-25, .3 2025-04-22. `v5-lts` branch still gets fixes (last commit 2026-09-20). https://github.com/BepInEx/BepInEx/releases |
| BepInExPack_PEAK | **5.4.75301**, 3.6M downloads, 1921 dependants. Bundles BepInEx.dll **5.4.23.3**, Doorstop **4.4.1** (`.doorstop_version` = `4.4.1PEAK`), `0Harmony.dll` **2.9.0** (HarmonyX), `MonoMod.RuntimeDetour/Utils` **22.1.29.1**, Mono.Cecil 0.10.4, HarmonyXInterop, a custom `winhttp.dll` with `[Extra] bypass_vulkan` in `doorstop_config.ini` (strips the game's `-force-vulkan`). https://thunderstore.io/c/peak/p/BepInEx/BepInExPack_PEAK/ |
| BepInEx 6 | Last tagged pre-release `6.0.0-pre.2` (2024-08-27). Bleeding Edge build #789 (2026-10-16) is `master` (https://builds.bepinex.dev/projects/bepinex_be). README: "Currently only Unity Mono has stable releases". BE targets HarmonyX 2.10.2. |
| NuGet | `BepInEx.Core` / `BaseLib` **5.4.21** (newest on https://nuget.bepinex.dev/v3/index.json), `BepInEx.Analyzers` 1.0.8, `BepInEx.PluginInfoProps` 2.1.0, `BepInEx.AssemblyPublicizer.MSBuild` 0.4.3 (nuget.org). HarmonyX on nuget.org: 2.16.1 (2026-03-30). `Lib.Harmony` 2.4.3, 3.0.0-rc.3. |

### Decision: BepInEx 5.4.23.x

- Compat break with v6 is structural (verified in repo trees): v5 `BaseUnityPlugin` lives in `BepInEx` (`BepInEx.dll`, `v5-lts/BepInEx/Contract/BaseUnityPlugin.cs`); v6 moves it to `BepInEx.Unity.Mono` (`master/Runtimes/Unity/BepInEx.Unity.Mono/BaseUnityPlugin.cs`) with a new `BepInEx.Core` assembly. A plugin compiled against v5 fails to load on v6. Every PEAK dependency (PEAKLib, mod managers r2modman/Gale, BepInExPack_PEAK) assumes v5. Players install the pack; we do not choose their loader.
- Compile against `BepInEx.Core 5.4.21` (what `src/PeakXR/PeakXR.csproj` already does). Runtime is the pack's 5.4.23.3. Add 5.4.23.5 to the smoke-test matrix once (Unity 6 log fix) but never require it.
- **Harmony:** the pack's `0Harmony.dll` is HarmonyX **2.9.0** on MonoMod **22.1.29.1**. HarmonyX 2.16.1 and Lib.Harmony 3.x use MonoMod 25.x and cannot be loaded beside BepInEx 5's MonoMod 22. Do not ship a Harmony copy. Code only against the 2.9 API: prefix/postfix, `AccessTools`, `HarmonyReversePatch`, no 2.10+ additions. This matches the design rule "prefix/postfix only".
- Launch on Proton: `WINEDLLOVERRIDES="winhttp=n,b" %command%`, or r2modman/Gale "Start modded". Design doc already says so.

Changes vs plan: none. Add 5.4.23.5 note only.

---

## 2. .NET SDK, C# language, polyfills

### SDK (verified via https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json and `mise ls-remote dotnet`)

| Channel | Status |
|---|---|
| 10.0 | **LTS, active**, SDK **10.0.402** (runtime 10.0.13, 2026-10-13), EOL 2028-11-14, C# 14 |
| 9.0 | STS, maintenance, SDK 9.0.319, **EOL 2026-11-10** (about 3 weeks) |
| 11.0 | STS, "go-live" RC2 (`11.0.100-rc.2.26504.105`). GA expected November 2026. Do not adopt before GA. |

`mise.toml`: `dotnet = "10"` (resolves to 10.0.402). Test projects `net10.0`. `ilspycmd` shim: current `9.1.0.7988` still works with `DOTNET_ROLL_FORWARD=Major`; latest is `11.1.0.9782` (optional bump).

### Trial I ran (verified, scratch copy of `src/PeakXR.Core` + `tests/PeakXR.Core.Tests`)

SDK 10.0.402, `LangVersion 14.0`, `PolySharp 1.16.0`, tests on `net10.0` with `xunit.v3.mtp-v2 4.0.2` and `OutputType Exe`, `global.json` with `{"test":{"runner":"Microsoft.Testing.Platform"}}`: **build clean (TreatWarningsAsErrors), 162/162 tests pass.** With `coverlet.MTP 10.1.0` and `dotnet test --coverlet --coverlet-output-format cobertura --coverlet-include "[PeakXR.Core]*"`: line rate 94.3%.

### What Unity 6000.3's Mono really supports (verified from the game's own `mscorlib.dll` via ilspycmd)

- `System.Runtime.CompilerServices.RuntimeFeature` advertises only `PortablePdb` and `DefaultImplementationsOfInterfaces`. Nothing else (`ByRefFields`, `VirtualStaticsInInterfaces`, `NumericIntPtr`, `UnmanagedSignatureCallingConvention`, `ByRefLikeGenerics` are absent).
- Present: `Span<T>`, `ReadOnlySpan<T>`, `Index`, `Range`, `MemoryMarshal`, `IAsyncEnumerable`, `ValueTask`, `HashCode`, `ArrayPool`, `ValueTuple`, `System.Runtime.CompilerServices.Unsafe.dll`, `System.Memory.dll`, `System.Numerics.dll`, `IsUnmanagedAttribute`, `IsReadOnlyAttribute`, `IsByRefLikeAttribute`.
- Absent types: `IsExternalInit`, `RequiredMemberAttribute`, `SetsRequiredMembers`, `CompilerFeatureRequiredAttribute`, `InlineArrayAttribute`, `ModuleInitializerAttribute`, `SkipLocalsInitAttribute`, `CallerArgumentExpressionAttribute`, `UnscopedRefAttribute`, `System.Half`, `PriorityQueue`.
- Runtime is Unity's Mono fork (`mono-2.0-bdwgc.dll`, Boehm GC). Unity 6.3 docs: C# 9 for in-editor compilation only; irrelevant to an externally compiled DLL, but it lists what Unity treats as unsupported. (doc) https://docs.unity3d.com/6000.3/Documentation/Manual/csharp-compiler.html
- Future risk (doc/community): Unity says 6.7 LTS is the last Mono release and 6.8 moves the editor to CoreCLR and C# 14; Unity 7 is said to be CoreCLR-based. PEAK stays on 6000.3 Mono until its developers upgrade. Keep code free of Mono-only hacks.

### Language features, tested with SDK 10.0.402 / `netstandard2.1` / PolySharp (verified by compile probe)

| Feature | Compiles | Safe on Unity Mono | Rule |
|---|---|---|---|
| file-scoped namespaces, records, `record struct`, pattern matching incl. list patterns, `is not`, raw strings | yes | yes (lowering only) | use |
| `init` accessors | yes with polyfill | yes (modreq metadata only) | use |
| `required` members | yes with polyfill | yes (runtime ignores attrs) | use sparingly; no reflection-based constructors on them |
| collection expressions `[..a, 4]` | yes | arrays and `List<T>` OK; `Span<T>` targets unverified | prefer arrays/lists; run an in-game smoke test before using spans |
| C# 14 `field` keyword, extension members (`extension(string s) {}`) | yes | lowering only (inference) | fine |
| Default interface methods | yes (runtime advertises it) | **risky**: Unity issue tracker has wrong-method and generic-interface crash bugs on Mono (fixed 2022-2023 in 2022.2/2023.2) | avoid; use abstract base classes |
| `static abstract` interface members | **no, CS8919** | no | impossible |
| `ref` fields in ref structs | **no, CS9064** | no | impossible |
| `allows ref struct` | **no, CS9240** | no | impossible |
| `[InlineArray]` | **no, type missing** (PolySharp does not generate it unless `PolySharpIncludeRuntimeSupportedAttributes`) | Mono has no layout support | never enable that PolySharp switch |
| `[ModuleInitializer]` | compiles with polyfill | unverified that Mono runs `<Module>.cctor` | avoid; init in `Feature.Install` |
| `[UnmanagedCallersOnly]`, function pointers with new calling conventions | polyfill-gated | Unity: unsupported | avoid |
| `Span<T>`/`stackalloc` in hot paths | yes | yes | use for codec buffers |

**LangVersion:** set `14.0` explicitly in a root `Directory.Build.props` (not `latest`, so a future SDK cannot silently move it). The compiler itself blocks the features Mono cannot run (static abstract, ref fields, allows ref struct, InlineArray). The remaining runtime-unsafe ones (DIM, module initializers) need review rules, not tooling.

### Polyfill packages (verified versions)

| Package | Version | Date | Use |
|---|---|---|---|
| **PolySharp** | **1.16.0** | 2026-05-25 | language-feature attributes as internal source (IsExternalInit, RequiredMember, CallerArgumentExpression, SkipLocalsInit, interpolated string handler, Index/Range only if missing). Use in `PeakXR.Core` and `PeakXR`. Keep types `internal` (default) so other mods' copies never collide. Do not set `PolySharpIncludeRuntimeSupportedAttributes`. https://github.com/Sergio0694/PolySharp |
| Polyfill (SimonCropp) | 11.4.5 | 2026-10-12 | 908 API polyfills for `netstandard2.1` (BCL methods added after ns2.1). Skip until a missing BCL API is actually needed; adds surface and compile time. https://github.com/SimonCropp/Polyfill |

Source-linked `PeakXR.Core` inside the plugin plus PolySharp in both projects is fine: generated types are internal and PolySharp skips types the compilation already has (inference; the trial built Core alone, link a build to confirm).

Changes vs plan: .NET 9 -> 10, `LangVersion latest` -> `14.0`, add PolySharp.

---

## 3. Unity XR packages for 6000.3

### Registry state (verified, `https://packages.unity.com/<pkg>`)

| Package | Stable tail | Notes |
|---|---|---|
| com.unity.xr.openxr | 1.16.1 (2025-12-10), 1.17.0 (2026-04-17), 1.17.1 (2026-06-03), **1.18.0 (2026-08-11)**; pre: 1.19.0-pre.2 (2026-10-03) | 1.18.0 min editor 6000.0. Unity 6.3 docs list 1.18 as verified. |
| com.unity.xr.management | 4.5.4 (2025-12-19), 4.6.0 (2026-04-14), 4.7.0 (2026-07-07), **4.6.1 (2026-08-11)** | 4.7.0 deprecates `XRGeneralSettings.AssignedSettings`/`k_SettingsKey`. 4.6.0 made `XRLoaderHelper.StartSubsystem/StopSubsystem/DestroySubsystem/CreateSubsystem` public. |
| com.unity.xr.hands | 1.8.1 (2026-07-23), **1.9.0 (2026-08-12)**; pre: 1.10.0-pre.2 | 1.9.0 adds Hand Tracking Data Source and Hand Joints Motion Range features; min editor 6000.0. Needs OpenXR >= 1.18.0-pre.1 for the data-source feature (`UNITY_OPENXR_HAS_EXTENSIBLE_HAND_TRACKING` versionDefine). |
| com.unity.xr.core-utils | 2.5.3, **2.6.0 (2026-04-17)** | Not referenced by OpenXR/Management runtime (both compiled without it). **Required only by XR Hands** (`Unity.XR.Hands.asmdef` references `Unity.XR.CoreUtils`). |
| com.unity.inputsystem | 1.19.0, 1.20.0, 1.20.1 | Game ships 1.19.0. Ship nothing. |

### Compile check (verified)

`XR_OPENXR_VERSION=1.18.0 XR_MANAGEMENT_VERSION=4.6.1 SKIP_OPENVR=1 mise x -- bash tools/fetch-xr-deps.sh` (run from a scratch copy so `deps/` stays untouched): both build with 0 errors. Same with Management 4.7.0. Outputs: `Unity.XR.OpenXR.dll` 307 KB, `UnityOpenXR.dll` 919,040 B (sha256 `a344bf54...`), `openxr_loader.dll` 2,373,632 B (sha256 `f7384429...`, loader 1.1.54).

### What changed from 1.16.1 to 1.18.0 (full changelog read, `CHANGELOG.md` in the 1.18.0 tarball)

No entry mentions SteamVR, Proton, Wine, D3D11/D3D12 changes or a URP 17.3 fix. Relevant to us:

- 1.16.0: crash fixes for `OpenXRProjectionLayer` and for display-subsystem calls before the library is initialized (both 1.16.1 includes), MSAA for Windows+Vulkan, one-frame black flash on recenter fixed, "Automatic viewport dynamic resolution" added (needs Meta/AndroidXR provider + Vulkan: unusable here).
- 1.16.1 (patch of 1.16.0): occlusion mesh skipped on 2022.3, loader 1.1.53, eye-tracked foveation sleep/wake crash.
- 1.17.0: `useOpenXRPredictedTime` default **false -> true**, `foveatedRenderingApi` default **Legacy -> SRPFoveation**, empty visibility mesh fix, `XR_META_tile_properties_hint` auto-enabled. Regressions introduced here: `OpenXRDevice` stopped setting `{LeftHand}`/`{RightHand}` usages and was not registered as a base layout (fixed only in 1.18.0). **Skip 1.17.x.**
- 1.18.0: input-device fixes above, `XR_EXT_hand_tracking` / `hand_joints_motion_range` / `hand_tracking_data_source` native types, API layers feature, Valve Index `thumbstick`/`trackpad` controls now `ThumbstickControl`.
- Defaults in both versions: `renderMode = SinglePassInstanced` (**must set MultiPass explicitly**, see section 4), `depthSubmissionMode = None`, `optimizeBufferDiscards = false`, `latencyOptimization = PrioritizeRendering`.

### Decision

- **Baseline now: OpenXR 1.16.1 + Management 4.5.4** (proven on this exact game by others, hash-identical natives).
- **Target after the first Proton run: OpenXR 1.18.0 + Management 4.6.1**, flipping only `OPENXR_VERSION`/`MANAGEMENT_VERSION` in `tools/fetch-xr-deps.sh` and `deps/versions.txt`. Set `useOpenXRPredictedTime` and `foveatedRenderingApi` explicitly in our settings bootstrap so the version flip cannot change behaviour. If we want XR Hands, 1.18.0 is a prerequisite.
- XR Hands 1.9.0 + core-utils 2.6.0: only for the optional finger upgrade (section 9). Needs `UnityOpenXRHands.dll` in `PEAK_Data/Plugins/` and compiled managed `Unity.XR.Hands.dll` (+ Unity.Collections/Burst/Mathematics from the game's `Managed/`, all present).
- Management: `InitXRSDK`/`Start` are non-public. Public path: `XRManagerSettings.InitializeLoaderSync()` then `StartSubsystems()`.

### Hand tracking on SteamVR Linux 2.x (see also section 9)

- SteamVR implements `XR_EXT_hand_tracking` (since 2020, community report) and **`XR_EXT_hand_tracking_data_source` since SteamVR 2.7 (2024-07-30)** (verified in Steam news API, app 250820). 2.8 (2024-10) forwards Quest hand tracking over Steam Link into `XR_EXT_hand_tracking`. 2.12 (2025-07) fixed a `XR_RUNTIME_FAILURE` when hand tracking should return invalid poses.
- Index controllers: community list of SteamVR OpenXR extensions says `XR_EXT_hand_tracking` "seems to emulate finger position based on button input", confirmed for Index only (community, https://steamcommunity.com/app/250820/discussions/8/3121550424355682585/). Joint output is synthesized from the per-finger curl the Index reports.
- Under wineopenxr: extensions pass through. Only `XR_EXT_debug_utils`, `XR_KHR_loader_init`, `XR_MSFT_perception_anchor_interop`, `XR_HTC_foveation` are blocked (verified in `wineopenxr/make_openxr`, UNSUPPORTED_EXTENSIONS). D3D11/D3D12 are substituted with `XR_KHR_vulkan_enable`.
- Not verified: that a Linux SteamVR session with Index returns `isActive = true` joints through wineopenxr. Test in game.

---

## 4. Stereo rendering, foveation, render scale

### Single Pass Instanced is not feasible (verified on the game's data)

I parsed every shader in `PEAK_Data/data.unity3d` (the only data container; `StreamingAssets` has no bundles) with UnityPy (script in the appendix). Results:

- 275 shaders, **20,326 player sub-programs** (D3D platform blobs).
- Sub-programs with `STEREO_INSTANCING_ON`, `STEREO_MULTIVIEW_ON`, `UNITY_SINGLE_PASS_STEREO` or `STEREO_CUBEMAP_RENDER_ON`: **0**. (The keywords sit in each shader's keyword name table, but no variant uses them.) Sanity check: URP/Lit keyword indices resolve correctly (`_MAIN_LIGHT_SHADOWS`, `_LIGHT_LAYERS`, ... appear thousands of times).
- Sub-programs with `_FOVEATED_RENDERING_NON_UNIFORM_RASTER`: **0**.
- `Hidden/Universal Render Pipeline/XR/XRMirrorView`, `XR/XROcclusionMesh`, `XR/XRMotionVector`: present as shader objects with **0 sub-programs**.
- `UberPost` and `FinalPost` have only the non-XR passes (`UberPost` 1,729 sub-programs, `FinalPost` 2x193): `UberPostXR`/`FinalPostXR` passes are gone.

Why (verified in URP 6000.3 source, `Packages/com.unity.render-pipelines.universal/Editor/ShaderBuildPreprocessor.cs:308-311`, `ShaderScriptableStripper.cs:784-803,1119-1140`): `s_StripXRVariants = generalSettings == null || Manager == null || activeLoaders.Count <= 0` strips the three XR shaders and the XR passes when the build has no XR loader. The built-in stereo keywords are stripped by the native shader compiler under the same condition (a Unity staff reply on a forum thread says they are stripped unless XR Management is configured; the variant counts above confirm it for this build). https://discussions.unity.com/t/stop-ios-build-from-stripping-stereo-instancing-on-shader-variants/1562156

Consequences:

1. `OpenXRSettings.renderMode = MultiPass` set explicitly. The default is `SinglePassInstanced` and it would draw one eye (Unity doc: unmodified shaders render only the first array slice). No known trick: the variants do not exist in the player and cannot be recompiled at runtime. Even an asset-bundle shader replacement would have to cover all ~275 shaders.
2. Desktop mirror: `XRMirrorView` has no code. Disable URP's mirror (`XRSRPSettings.mirrorViewMode`) and copy one eye to the game window ourselves (`XRDisplaySubsystem.GetMirrorViewBlitDesc`/`AddGraphicsThreadMirrorViewBlit`, or `Graphics.CopyTexture` of the eye texture).
3. Occlusion/visibility mesh: `XROcclusionMesh` has no code. Set `XRSRPSettings.useVisibilityMesh = false` and `occlusionMeshScale = 0`.
4. XR motion vectors (SpaceWarp) are impossible. Irrelevant on desktop.
5. CPU cost doubles on draw submission. Mitigations to measure: depth prepass off/on, LOD bias, shadow distance cap.

### Foveated rendering: not usable

- Unity's OpenXR foveation feature (doc, 1.18.0 `foveatedrendering.md`) needs the runtime's FDM/VRS support and, for the SRP path, URP shader variants with `_FOVEATED_RENDERING_NON_UNIFORM_RASTER`. Those variants are 0 in this build.
- SteamVR advertises `XR_FB_foveation`, `XR_FB_foveation_configuration`, `XR_FB_foveation_vulkan`, `XR_META_foveation_eye_tracked` for **Steam Frame** only (Valve Steamworks docs, https://partner.steamgames.com/doc/steamhardware/steamframe/engines/custom). Eye gaze there uses `XR_EXT_eye_gaze_interaction`. Not offered to desktop Index / Quest Link / Virtual Desktop sessions (community list). `XR_HTC_foveation` is blocked by wineopenxr.
- Decision: no foveation in v1. Revisit only for DX12 + VRS (URP has `Hidden/Core/VrsVisualization` in the build), and only if performance forces it.

### Render scale and upscaling (URP 17.3, doc https://docs.unity.com/en-us/engine/6000.3/manual/xr/graphics/resolution-scaling)

- Unity recommends `XRSettings.renderViewportScale` for URP: per-frame, no eye-texture reallocation, hint the runtime may override (`appliedRenderViewportScale` shows the result). With post-processing on, the camera needs **URP Dynamic Resolution** enabled, and that path **cannot be combined with TAA**.
- URP asset `renderScale` reallocates the eye texture: set once at start or on explicit user change, never per frame. `XRSettings.eyeTextureResolutionScale` is not supported in URP.
- Automatic viewport dynamic resolution (OpenXR 1.16+): needs Meta/Android XR provider and Vulkan. Unusable.
- STP requires TAA (implicitly enabled) and compute SM5; TAA and URP dynamic resolution conflict in XR, and a Unity issue tracker entry says upscaling filters do not work with URP XR (old, status unknown). **Do not use STP in XR.** FSR1 (EASU) has no documented XR support. A game-side STP setting exists (`upscalingFilter`), so the design rule "STP to linear under XR" stands.
- Recommended: `msaaSampleCount` 2 or 4 (MSAA on the eye texture is cheap on desktop), TAA off, upscaling `Linear`, URP `renderScale` from config (default 1.0, SteamVR's own render-resolution slider multiplies on top), `renderViewportScale` as the only dynamic knob (adaptive 0.7-1.0 with hysteresis if frame time exceeds budget). Keep screen-space effects that cost per eye (HBAO) optional in XR.

---

## 5. Input: Input System XR vs legacy InputDevices

Facts (verified):

- The game's Input System 1.19.0 already runs `XRSupport.Initialize()` inside `PerformDefaultPluginInitialization` (decompiled `UnityEngine.InputSystem.InputSystem`): `XRHMD`, `XRController`, `Pose`, `Bone`, `Eyes` layouts and `XRLayoutBuilder` are registered. No reflection call is needed. The OpenXR plugin registers its own device layouts (`ValveIndexControllerProfile.ValveIndexController`, etc.) when the loader initializes.
- The OpenXR plugin creates its OpenXR actions from the **enabled interaction features' `ActionMapConfig`s** in `OpenXRInput.AttachActionSets` (`Runtime/input/OpenXRInput.cs:202-247`), not from application `InputAction`s. Docs (doc): "OpenXR requires that all bindings be attached only once at application startup". So a runtime-created `InputAction` neither adds nor blocks controls; the profile defines them. Enable every profile we want before `StartSubsystems()`.
- Each `ActionConfig` carries legacy `usages`, so the same controls surface through `UnityEngine.XR.InputDevices` and `InputFeatureUsage` (doc: OpenXR input page says both the Input System and the Feature API are supported).
- Valve Index profile (verified, `ValveIndexControllerProfile.cs`): `trigger`, `triggerPressed`, `triggerTouched`, `grip`(squeeze), `gripPressed`, `gripForce`, `primaryButton`(A), `primaryTouched`, `secondaryButton`(B), `secondaryTouched`, `thumbstick`, `thumbstickClicked`, `thumbstickTouched`, `trackpad`, `trackpadTouched`, `trackpadForce`, `devicePose`, `pointer`, `haptic`. Legacy usage strings: `Trigger`, `TriggerButton`, `TriggerTouch`, `Grip`, `GripButton`, `GripForce`, `PrimaryTouch`, `SecondaryTouch`, `Primary2DAxisTouch`, `Secondary2DAxisTouch`, `Secondary2DAxisForce`, `Device`, `Pointer`.
- There is **no per-finger data** in any controller profile. Index `grip` is a single squeeze float.

Decision: **keep `UnityEngine.XR.InputDevices` as the primary `VRInput` source** (design `xr.input`).

| Concern | InputDevices | Input System |
|---|---|---|
| Depends on game's Input System settings (update mode, background behavior, device filters) | no | yes, and we cannot see the game's runtime config |
| Works under the Valve OpenVR plugin fallback | yes (its own device usages) | needs a separate custom device |
| Layout/binding lifecycle | none | layouts must exist before the device appears; `<XRController>` generic bindings are discouraged by Unity |
| Access to `*Touch`, `GripForce` | by usage string (custom `InputFeatureUsage<bool>("TriggerTouch")`), allocation-free `TryGetFeatureValue` | typed controls, strongly named |
| HMD pose timing | `Application.onBeforeRender` + `TryGetFeatureValue` | needs `BeforeRender` update enabled on XR devices |

Implementation rules:
1. Cache `InputDevice` per `XRNode` (`InputDevices.deviceConnected/Disconnected` events), reuse a `List<InputDevice>`, never call `GetDevices` per frame.
2. Enable these profiles together: Valve Index, Oculus Touch, Meta Quest Touch Plus, Touch Pro, HTC Vive, HP Reverb G2, Microsoft Motion, KHR Simple (fallback). WiVRn/Quest runtimes report Oculus/Touch Plus.
3. A camera with `stereoTargetEye = Both` already receives the head pose from the XR display subsystem each frame; a `TrackedPoseDriver` is only needed if we parent it oddly (verify in game).
4. Add a debug dump (`device.TryGetFeatureUsages`) in the compat report to confirm the touch/force usages exist on the user's runtime.
5. Optional second source: `InputSystem.GetDevice<XRController>`/OpenXR layouts for `triggerTouched` etc. if the dump shows missing usages. Keep it behind the same `VRInput` interface.

Changes vs plan: none, but note the profile list and the "OpenXR actions are profile-defined" fact.

---

## 6. OpenXR runtime path under Proton

### Versions (verified via GitHub API)

| Tool | Latest | Date | Notes |
|---|---|---|---|
| Proton (Valve) | **11.0-2e** | 2026-10-09 | stable line. 11.0-2 (2026-08-21), 11.0-1b, 10.0-4b (2026-07-28). 11.0-3 RC under test (issue #10236): adds early 32-bit OpenXR (`PROTON_USE_WOW64=1`, irrelevant for 64-bit PEAK). |
| Proton Experimental | tracks `experimental-11.0` | rolling | newest wineopenxr. |
| Proton-CachyOS | `cachyos-11.0-20261014-slr` | 2026-10-17 | rebased on Experimental 11.0-20261014 + wine bleeding edge. Use the `-slr` (Steam Linux Runtime) build; the native build "doesn't work with Steam presently" under SteamRT3 (release notes). |
| GE-Proton | 11-11 | 2026-10-19 | "Updated ... wineopenxr" in 11-8 and 11-9. Mainly for non-Steam VR (10-33, 2026-03). PEAK is a Steam launch, so no need. |

Release-note mentions of VR in Valve's Proton 11.x: 11.0-1 "Fixed VR controller tracking in Microsoft Flight Simulator", "No Man's Sky VR playable again". No Unity-OpenXR-specific note.

### Recommendation

1. **Proton Experimental** while we iterate (fastest wineopenxr fixes). Pin the version in bug reports.
2. **Proton-CachyOS `-slr`** as an A/B partner (same wineopenxr base).
3. **Proton 11.0-2e** as the reproducible reference for release testing; document it in the README as "tested with".
4. Launch options: `-force-d3d11 %command%` (DXVK; wineopenxr maps `XR_KHR_D3D11_enable` to `XR_KHR_vulkan_enable`). Debug: `PROTON_LOG=+openxr,+vrclient`. Do not set `XR_RUNTIME_JSON` in the mod.
5. SteamVR: stable **2.17.10** (hotfix 2026-09-18). Beta 2.18.3 (2026-10-10) fixes "OpenXR games crashing on launch if SteamVR wasn't already started" on Linux (verified in Steam news). Until you use that beta: start SteamVR before the game. Set SteamVR as the OpenXR runtime in SteamVR settings.
6. Known bug to design around: wineopenxr `xrEndFrame` uses `layers[0]` size for all layers (issue #291 / #10275; quad layers after a projection layer give `XR_ERROR_POSE_INVALID`). Unity OpenXR sends only the projection layer unless composition layers are used. **No OpenXR quad/cylinder layers.** All UI stays in world space.

### OpenXR vs OpenVR

- Valve says "We continue to focus on OpenXR as our preferred API for new games and applications" (SteamVR 2.15 and 2.16 release notes, verified in Steam news). SteamVR 2.15-2.18 has steady OpenXR fixes.
- OpenVR via `vrclient_x64` is the older, battle-tested Proton path. Valve's `unity-xr-plugin` v1.2.4 (2025-10-29, BSD-3) is the only OpenVR Unity XR provider; no release since.
- Evidence for OpenXR on Linux via Unity BepInEx mods is thin: RepoXR works with WiVRn (3 reports), SteamVR only through Steam Link at 30 FPS (https://db.vronlinux.org/games/3241660.html). Nothing public for native Linux SteamVR + wineopenxr + Unity OpenXR on PEAK.
- Decision: **OpenXR primary, OpenVR fallback** selectable in config, as in `fetch-xr-deps.sh`. First Proton run decides if the default should flip on Linux. Run both once.

### Other Linux runtimes (WiVRn, Monado, ALVR, Envision)

- WiVRn latest v26.9 (2026-09-12); standalone headsets. Monado: PC headsets. Envision: GUI for Monado/WiVRn. ALVR: streams to SteamVR. xrizer / OpenComposite: OpenVR-to-OpenXR shims (https://vronlinux.org/docs/fossvr/).
- Mod implications: (a) never enumerate Windows-registry runtimes or set `XR_RUNTIME_JSON` under Proton; honour a user-set value only; (b) enable all controller profiles (WiVRn reports Oculus/Touch Plus); (c) no quad layers; (d) hand tracking comes from the runtime (WiVRn forwards `XR_EXT_hand_tracking`; its 26.2.3/26.9 notes mention hand-tracking and skeletal fixes); (e) user env: `PRESSURE_VESSEL_FILESYSTEMS_RW=$XDG_RUNTIME_DIR/wivrn/comp_ipc` for WiVRn (vr-prior-art.md section 4.3). Nothing else changes in code.

---

## 7. Tooling

### Versions (verified)

| Tool | Version | Date | Notes |
|---|---|---|---|
| **tcli** (Thunderstore CLI) | **0.2.4** (NuGet `tcli`, DotnetTool package) | 2024-06-11 | GitHub tag 0.2.5 exists with no release or NuGet package. Tool targets net6.0/net7.0 only; installs and runs on SDK 10 with `DOTNET_ROLL_FORWARD=Major` (tested: `dotnet tool install tcli --version 0.2.4 --tool-path ...`, `tcli --version` prints `0.2.4+1116dc77...`). `mise ls-remote dotnet:tcli` lists 0.2.x. Add `"dotnet:tcli" = "0.2.4"` to `mise.toml` (roll-forward env already set). |
| **xunit.v3.mtp-v2** | **4.0.2** | 2026-10-09 | v3 is the active line (xunit v2 stopped at 2.9.3). Requires `OutputType Exe`, net8+. `xunit.v3` with 4.x equals mtp-v2. |
| Microsoft.NET.Test.Sdk / xunit.runner.visualstudio | 18.11.0 / 4.0.1 | | only needed if you keep VSTest instead of MTP; not needed with the MTP setup below |
| coverlet.MTP | **10.1.0** | 2026-09-27 | works with xunit.v3 MTP (tested: 94.3% on Core). `coverlet.collector` 10.1.0 is for VSTest, `Microsoft.Testing.Extensions.CodeCoverage` 18.12.0 is the Microsoft alternative. |
| TUnit | 1.75.0 | 2026-10-13 | fine, source-generated, parallel by default; rewrite of 162 tests buys nothing. |
| MSTest | 4.5.1 | 2026-10-07 | fine; no reason to switch. |
| CSharpier | **1.3.0** | 2026-06-07 | opinionated formatter, `dotnet:csharpier` in mise. |
| Meziantou.Analyzer | **3.0.313** | 2026-10-19 | broad correctness/perf rules, low noise. |
| Roslynator.Analyzers | 5.0.1 | 2026-10-04 | overlaps Meziantou and the SDK; skip, or enable only in a `.editorconfig` subset. |
| Microsoft.CodeAnalysis.NetAnalyzers | in-box with SDK 10.0.402 | | set `<AnalysisLevel>latest-recommended</AnalysisLevel>` instead of the package. |
| BepInEx.Analyzers | 1.0.8 (2022-01) | | stale but harmless, already referenced (`1.*`). |
| BUTR.Harmony.Analyzer | 1.0.1.50 (MIT, 2023-06) | | checks Harmony patch signatures (`__instance`, `__result`, parameter names); should work on HarmonyX because the namespaces match (inference, untested). Alternatives: MicroUtils.HarmonyAnalyzers 1.6.1. |
| Microsoft.Unity.Analyzers | 1.28.0 (MIT, Microsoft) | | works outside the editor; catches `?.`/`??` on `UnityEngine.Object` (UNT0007/UNT0008), `Update` allocations. Add to the plugin project. |
| BepInEx.AssemblyPublicizer.MSBuild | 0.4.3 | | alternative to `GameMember` reflection; design chose `GameMember`, keep it. |
| Krafs.Publicizer | 2.3.2 | | same. |

### Recommended setup

```xml
<!-- tests/PeakXR.Core.Tests/PeakXR.Core.Tests.csproj -->
<TargetFramework>net10.0</TargetFramework>
<OutputType>Exe</OutputType>
<PackageReference Include="xunit.v3.mtp-v2" Version="4.0.2" />
<PackageReference Include="coverlet.MTP" Version="10.1.0" />
```
```json
// global.json (repo root)
{ "test": { "runner": "Microsoft.Testing.Platform" } }
```
Run: `dotnet test --coverlet --coverlet-output-format cobertura --coverlet-include "[PeakXR.Core]*"`. Remove `Microsoft.NET.Test.Sdk` and `xunit.runner.visualstudio`. `mise run test` then prints MTP summary lines.

Formatter: CSharpier for code, `.editorconfig` for analyzer severity, `dotnet format analyzers --verify-no-changes` is optional. Keep `TreatWarningsAsErrors` for Core; for the plugin enable it after the analyzers' first clean run.

### Testing Unity-dependent code

Confirmed patterns (existing repo already follows them):

1. Unity engine types cannot run outside the engine: `UnityEngine` methods marked as internal calls (`[MethodImpl(InternalCall)]`, e.g. `Quaternion.Euler`, `Transform`, `GameObject`) throw outside a player. Struct fields and managed-only math may work, but do not rely on it.
2. Therefore: all logic lives in `PeakXR.Core` on `System.Numerics` plus our own `UnityQuat`, with xunit tests (already 162). The Unity layer stays thin: adapters, Harmony patches, `Feature.Install`.
3. Harmony patch targets are validated by `mise run compat-check` against the decompile (no runtime). Keep that.
4. In-game smoke harness (recommended addition, inference): a hidden launch arg (e.g. `--peakxr-selftest`) that runs the `[compat]` report plus a codec round-trip and probes (`device.TryGetFeatureUsages`, `OpenXRRuntime.IsExtensionEnabled("XR_EXT_hand_tracking")`, `SystemInfo.graphicsDeviceType`, collection-expression/Span lowering) then calls `Application.Quit()`. Cheap, and the only way to verify the Mono-only questions in section 2.
5. No Unity Test Framework (needs the editor; the game is a player build). No mocking of `UnityEngine.Object`.

---

## 8. Networking: Photon PUN 2 pose stream

Game facts (verified from the decomp, see `game-player-items-net.md` section 7): `PhotonNetwork.SendRate = 30`, `SerializationRate = 30` (`Peak.Network/NetworkingUtilities.cs:121-122`). Event codes in use: 1-4 (`CustomCommandType`) and 18 (kick). `CustomCommandListener.OnEvent` ignores unregistered codes silently when `code >= 6` and only logs an error for `code < 6` (`Zorro.PhotonUtility/CustomCommandListener.cs`, re-read today). `PhotonShim.RaiseGenericEvent` forwards all events to `PlayerHandler.OnNetworkEvent`, which does nothing. **Vanilla clients ignore unknown event codes in 100-199.** Photon DLLs are present, including Photon Voice (`PhotonVoice.PUN.dll`).

Practice (doc: https://doc.photonengine.com/pun/current/gameplay/rpcsandraiseevent and `.../reference/serialization-in-photon`, plus Photon staff forum answers on MTU):

| Topic | Rule |
|---|---|
| Event code | Custom codes are `0..199` (PUN doc); 200-255 are Photon internals. Use a fixed code in **100-199**, avoid 0 (cache wildcard), 1-6, 18. Log once at startup if another `EventReceived` consumer exists. |
| API | `PhotonNetwork.RaiseEvent(code, content, options, SendOptions.SendUnreliable)`. Receive via `PhotonNetwork.NetworkingClient.EventReceived += OnEvent` (same hook the game uses). Unsubscribe on shutdown. |
| Payload | `byte[]` (5 B overhead + length) built with our own codec. `object[]` pays 1-2 B type info per element; `Vector3` 16 B, `Quaternion` 20 B. Target about 60 B per frame (design: head quat, two hand poses, 10 curl bytes). Keep each event under 1000 B: an unreliable operation above the 1200 B MTU is fragmented and delivered **reliably**, which can trigger "send buffer full" disconnects (Photon staff, forum). |
| Allocation | Reuse one `RaiseEventOptions`, one `byte[]` scratch, one `int[] TargetActors` (the library serializes the operation at call time, so reusing the buffer is expected to be safe; verify once with a stress test). |
| Receivers | `RaiseEventOptions.TargetActors` = actor numbers of peers that announced the `PeakXR` player property. Vanilla peers get nothing (no bandwidth, no log noise). Fallback `Receivers = ReceiverGroup.Others` when the list is empty is wrong; send nothing instead. |
| Interest groups | Not needed (rooms are small). Group 0 = everyone. |
| Caching | `EventCaching.DoNotCache` (default). Cached events would replay stale poses to late joiners. |
| Rate | Send 20-30 Hz. `NetworkingClient` flushes outgoing commands at `SendRate` (30), so above 30 Hz events only pile up. Default 20-25 Hz, 30 Hz when frame time allows. Drive from `LateUpdate` with an accumulator; skip when no modded peer. Receiver interpolates with a 2-3 frame jitter buffer (`PoseBuffer`) and treats >1 s without data as stale. |
| Reliability | Pose stream unreliable (sequenced `SendUnreliable`). VR-mode on/off transitions and the handshake: reliable event or player custom property (`SetCustomProperties`, existing design). |
| Late join | The handshake property is read on `OnPlayerEnteredRoom` and `OnPlayerPropertiesUpdate`; start the stream to that actor only. |

No change to design; two refinements: use `TargetActors`, and cap at the game's `SendRate`.

---

## 9. Hands and fingers

Options:

| Approach | Data | Works where | Cost |
|---|---|---|---|
| A. Analog curls from controller profile | Index: `trigger` (index), `grip` (squeeze, shared by middle/ring/pinky), `thumbstickTouched`/`trackpadTouched`/`primaryTouched`/`secondaryTouched` (thumb), `gripForce` | every runtime and the OpenVR fallback; no extra DLLs | trivial; existing `FingerCurls` in Core |
| B. `XR_EXT_hand_tracking` via XR Hands 1.9.0 with `XR_EXT_hand_tracking_data_source` = Controller and `XR_EXT_hand_joints_motion_range` = conforming to controller | 26 joints per hand, per-finger curls on Index (SteamVR synthesizes from the Index's per-finger capacitive sensing, community report) | SteamVR >= 2.7 (data source) with an Index; Quest hand tracking via Link/Steam Link/Virtual Desktop; WiVRn | needs OpenXR >= 1.18.0, XR Hands 1.9.0, core-utils 2.6.0, `UnityOpenXRHands.dll`; unverified under wineopenxr; 3 extra DLLs |
| C. SteamVR Skeleton input (OpenVR `GetSkeletalSummaryData`) | per-finger curls | OpenVR backend only | only if the OpenVR fallback needs it |

Best practice in 2026 (doc: XR Hands 1.9.0 `handtrackingdatasource.md`): request the data source per hand (`Tracked Hand`, `Controller`, `Both`), query the active one each frame with `XRHandSubsystem.TryGetExtendedData<HandTrackingDataSource>(Handedness, out ...)`, and treat the preference as a hint. For controller-driven hands pair it with Hand Joints Motion Range so the joints conform to the held controller.

Decision:
1. **Ship A now.** Wire format is already 5 bytes (or 10 curl bytes both hands) independent of the source.
2. **Add B as an opt-in `Hands.UseXRHands` experiment after OpenXR 1.18.0 + Proton smoke tests pass.** Gate: `OpenXRRuntime.IsExtensionEnabled("XR_EXT_hand_tracking")` and `XRHandSubsystem` running and joints `isTracked` for >1 s; otherwise fall back to A silently. Compute curls from joint angles (proximal/intermediate flexion), map to the same 0-255 bytes.
3. Controllers with real optical hand tracking (Quest) switch the source automatically; the mod must not assume controllers: show a hands-active indicator and mute button input while `HandTrackingDataSource == TrackedHand`.
4. Real per-finger Index data is the only reason to take B on Linux; weigh it after measuring on the user's Index.

---

## 10. Recommended stack

| Layer | Choice | Version | Source | Reason | Changes vs current plan |
|---|---|---|---|---|---|
| Mod loader | BepInEx 5 (via BepInExPack_PEAK) | 5.4.23.3 runtime (pack 5.4.75301); latest 5.4.23.5 | https://thunderstore.io/c/peak/p/BepInEx/BepInExPack_PEAK/ | every PEAK mod and manager is v5; v6 uses a different plugin ABI | none |
| Compile refs | BepInEx.Core / PluginInfoProps / Analyzers | 5.4.21 / 2.1.0 / 1.0.8 | https://nuget.bepinex.dev/v3/index.json | newest on the BepInEx feed | tighten `2.*`/`1.*` to exact |
| Harmony | HarmonyX bundled by pack (0Harmony.dll) | 2.9.0 on MonoMod 22.1.29.1 | pack files | MonoMod 25/HarmonyX 2.16 cannot coexist with BepInEx 5; use only 2.9 API | do not ship a Harmony copy |
| .NET SDK | .NET 10 LTS via mise | 10.0.402 | https://dotnet.microsoft.com/download/dotnet/10.0 | 9 reaches EOL 2026-11-10; trial passed 162/162 | `dotnet = "9"` -> `"10"`; tests `net9.0` -> `net10.0` |
| Plugin TFM | netstandard2.1 | n/a | Unity docs | Unity 6.3 default profile; verified surface in game `mscorlib` | none |
| C# | LangVersion 14.0 | C# 14 | SDK 10.0.402 | compiler blocks static abstract/ref fields/allows ref struct; avoid DIM, module initializers, InlineArray | `latest` -> `14.0`; add review rules |
| Polyfills | PolySharp | 1.16.0 | https://github.com/Sergio0694/PolySharp | init/required/CallerArgumentExpression for ns2.1, internal source only | new |
| XR native + managed | com.unity.xr.openxr / com.unity.xr.management | 1.16.1 / 4.5.4 now; 1.18.0 / 4.6.1 after A/B | https://packages.unity.com/ | proven natives now; 1.18.0 fixes 1.17 input regressions and unlocks XR Hands data source; both compile clean | no urgent change; plan an A/B |
| Stereo | OpenXR render mode MultiPass (set explicitly) | n/a | URP stripper source + game data scan | 0/20,326 variants carry stereo keywords; default would be SinglePassInstanced | document it; add custom mirror shader, visibility mesh off |
| Foveation | none | n/a | Steam Frame docs; game scan | SteamVR offers FB/META foveation on Steam Frame only; URP variant stripped | none |
| Render scale | URP `renderScale` at start, `XRSettings.renderViewportScale` dynamic, MSAA 2-4x, Linear upscale, no STP/TAA | URP 17.3 | https://docs.unity.com/en-us/engine/6000.3/manual/xr/graphics/resolution-scaling | URP dynamic-res path conflicts with TAA; STP needs TAA | confirms "STP to linear" |
| Hands pkg (optional) | com.unity.xr.hands + core-utils | 1.9.0 + 2.6.0 | https://packages.unity.com/ | only for the Index per-finger upgrade; core-utils needed only here | new, optional |
| Input | `UnityEngine.XR.InputDevices`; Input System layouts as secondary | game Input System 1.19.0 | OpenXR docs + decompile | independent of the game's Input System config; same controls via profiles | none; enable all profiles |
| Proton | Experimental (dev), Proton-CachyOS `-slr` (A/B), 11.0-2e (reference) | 11.0-2e / cachyos-11.0-20261014-slr | https://github.com/ValveSoftware/Proton/releases | newest wineopenxr, known layer bug avoided | none |
| SteamVR | stable 2.17.10; beta 2.18.3+ fixes OpenXR launch crash on Linux | 2.17.10 | Steam news app 250820 | Valve prefers OpenXR | note start-order |
| Runtime API | OpenXR primary, Valve OpenVR plugin fallback | OpenVR plugin v1.2.4 | https://github.com/ValveSoftware/unity-xr-plugin | OpenXR is Valve's direction; OpenVR is battle-tested on Proton | none |
| Test framework | xunit.v3.mtp-v2 | 4.0.2 | https://xunit.net/docs/getting-started/v3/getting-started | active line; v2 is frozen; passes 162/162 on net10 | `xunit 2.9.2` -> `xunit.v3.mtp-v2 4.0.2`, `OutputType Exe`, `global.json` MTP runner, drop Test.Sdk + runner.visualstudio |
| Coverage | coverlet.MTP | 10.1.0 | https://www.nuget.org/packages/coverlet.MTP | tested: 94.3% on Core | new |
| Formatter | CSharpier | 1.3.0 | https://csharpier.com | deterministic, mise `dotnet:csharpier` | new |
| Analyzers | SDK NetAnalyzers (`latest-recommended`), Meziantou 3.0.313, Microsoft.Unity.Analyzers 1.28.0, BUTR.Harmony.Analyzer 1.0.1.50, BepInEx.Analyzers 1.0.8 | as listed | NuGet | broad coverage, Unity-null pitfalls, Harmony signature checks | new (all `PrivateAssets=all`) |
| Packaging | tcli via `dotnet:tcli` | 0.2.4 | https://www.nuget.org/packages/tcli | installs and runs on SDK 10 with roll-forward | add to `mise.toml` |
| Net | `RaiseEvent` code 100-199, `byte[]`, unreliable, `TargetActors`, <=30 Hz, <1000 B | PUN 2 (game DLLs) | Photon docs + decompile | vanilla ignores unknown codes (verified) | add `TargetActors`, cap at SendRate |
| Fingers | analog curls; optional XR Hands data-source upgrade | Hands 1.9.0 | XR Hands 1.9.0 tarball, `Documentation~/openxr-features/handtrackingdatasource.md` | A is universal; B gives per-finger Index data if SteamVR joints are active | B optional, later |

---

## 11. Urgent changes (work is in progress on the current plan)

| Item in progress | Verdict |
|---|---|
| .NET 9 SDK | **Move to 10.0.402 soon** (EOL 2026-11-10). Low risk: trial passed. Touch `mise.toml`, the test csproj TFM, `PeakXR.sln` unaffected. |
| xunit 2.9.2 | **Migrate to xunit.v3.mtp-v2 4.0.2** while the suite is small (162 tests, no source change needed). v2 is end of line. |
| netstandard2.1 | **Keep.** Matches Unity's profile and the game's `mscorlib`. Add PolySharp and pin `LangVersion 14.0`. Ban static abstract / ref fields / InlineArray (compiler already errors) and DIM / module initializers (review rule). |
| OpenXR 1.16.1, Management 4.5.4 | **Keep for now.** Plan an A/B to 1.18.0 + 4.6.1; skip 1.17.x. **Set `renderMode = MultiPass` explicitly** (the package default is SinglePassInstanced); set `useOpenXRPredictedTime` and `foveatedRenderingApi` explicitly. |
| `UnityEngine.XR.InputDevices` | **Keep as primary.** Enable all interaction profiles before starting subsystems. Add a usage dump to the compat report. |
| Not in the plan yet | Custom mirror blit and visibility mesh off (the URP XR shaders are stripped in this build); no quad layers (Proton bug); `TargetActors` for the pose stream. |

## 12. Open items to verify in game

1. Proton first run: `Unity.XR.OpenXR` init, `XRDisplaySubsystem` count, `PROTON_LOG=+openxr` extension list (hand tracking, data source present?).
2. `device.TryGetFeatureUsages` on Index through OpenXR: do `TriggerTouch`, `Primary2DAxisTouch`, `GripForce` exist as legacy usages?
3. 1.16.1 vs 1.18.0 A/B: frame pacing with predicted time on, input regressions, shutdown/restart.
4. Mono runtime check of collection expressions with `Span`, extension members, DIM through the self-test harness.
5. SteamVR joints `isActive` for Index through wineopenxr (only if section 9 option B is pursued).
6. Event-code collision with Photon Voice (grep the code range at runtime).
7. `RaiseEvent` buffer reuse safety.

## Appendix: shader variant scan (reproduce)

```bash
cd <scratch> && uv run --with UnityPy python -I scan.py "$PEAK_DIR/PEAK_Data/data.unity3d"
```
```python
import sys, collections, UnityPy
env = UnityPy.load(sys.argv[1])              # ~30 s, ~5 GB RAM
stereo = {'STEREO_INSTANCING_ON','STEREO_MULTIVIEW_ON','UNITY_SINGLE_PASS_STEREO','STEREO_CUBEMAP_RENDER_ON'}
total = hits = 0
for o in env.objects:
    if o.type.name != 'Shader': continue
    pf = o.read_typetree()['m_ParsedForm']
    kn = pf.get('m_KeywordNames', [])
    idx = {i for i, k in enumerate(kn) if k in stereo}
    for ss in pf['m_SubShaders']:
        for p in ss['m_Passes']:
            for st in ('progVertex', 'progFragment'):
                for plat in p[st]['m_PlayerSubPrograms']:
                    for sp in plat:
                        total += 1
                        hits += any(i in idx for i in sp['m_KeywordIndices'])
print(total, hits)   # PEAK 2.6.b / Unity 6000.3.15f1: 20326 0
```
Rerun after every game update; if `hits` becomes non-zero, Single Pass Instanced becomes worth a test.

## Sources

- BepInEx releases https://github.com/BepInEx/BepInEx/releases ; BE builds https://builds.bepinex.dev/projects/bepinex_be ; PEAK pack https://thunderstore.io/c/peak/p/BepInEx/BepInExPack_PEAK/
- .NET release index https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json
- Unity C# support https://docs.unity3d.com/6000.3/Documentation/Manual/csharp-compiler.html ; profiles https://docs.unity3d.com/6000.3/Documentation/Manual/dotnet-profile-support.html
- Unity registry https://packages.unity.com/ (openxr, management, hands, core-utils, inputsystem); URP stripper source https://github.com/Unity-Technologies/Graphics/tree/6000.3/staging/Packages/com.unity.render-pipelines.universal/Editor
- XR resolution scaling https://docs.unity.com/en-us/engine/6000.3/manual/xr/graphics/resolution-scaling
- SteamVR news (Steam API app 250820); SteamVR OpenXR extension list https://steamcommunity.com/app/250820/discussions/8/3121550424355682585/ ; Steam Frame engine notes https://partner.steamgames.com/doc/steamhardware/steamframe/engines/custom
- Proton https://github.com/ValveSoftware/Proton/releases , wineopenxr https://github.com/ValveSoftware/Proton/tree/HEAD/wineopenxr , issues #291/#10275/#10236 ; GE-Proton https://github.com/GloriousEggroll/proton-ge-custom/releases ; Proton-CachyOS https://github.com/CachyOS/proton-cachyos/releases
- LVRA wiki https://vronlinux.org/docs/fossvr/ ; VR on Linux DB https://db.vronlinux.org/games/3241660.html ; WiVRn https://github.com/WiVRn/WiVRn/releases
- Photon https://doc.photonengine.com/pun/current/gameplay/rpcsandraiseevent , https://doc.photonengine.com/pun/current/reference/serialization-in-photon
- xunit v3 https://xunit.net/docs/getting-started/v3/getting-started , https://xunit.net/docs/getting-started/v3/microsoft-testing-platform
- PolySharp https://github.com/Sergio0694/PolySharp ; Polyfill https://github.com/SimonCropp/Polyfill ; tcli https://www.nuget.org/packages/tcli
