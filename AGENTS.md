# PeakXR agent guide

PeakXR is a clean-room, MIT-licensed BepInEx 5 VR mod for PEAK (Unity 6000.3.15f1, Mono, URP 17.3, Photon PUN 2). Read `docs/design.md` first: it names every feature, the shared contracts and the forward-compat rules.

## Sources

- Game decompile (read-only, gitignored): `decomp/` in the main checkout, written by `mise run decompile` from `$PEAK_DIR`. A worktree has no copy: read the main checkout's `decomp/`.
- Game install (read-only): `$PEAK_DIR`, the PEAK folder under Steam's `steamapps/common/`.
- `docs/research/game-player-items-net.md`: character, camera, input, items, IK, netcode patch points.
- `docs/research/game-ui-settings-render.md`: scenes, canvases, settings menu, URP, volumes, comfort hazards.
- `docs/research/vr-prior-art.md`: XR injection approach, Proton notes, comfort practice.
- `docs/research/creative-test-mode.md`: console, spawn APIs.
- `docs/research/stack.md`: chosen versions and tools (when present).

Clean-room boundary: code comes from your own reasoning, the game decompile, Unity/Valve/Microsoft docs and packages. PeakVR (Andrey04o), PEAK_VR, LCVR, CWVR and RepoXR source stays unread.

## Writing code

- Each area is a `Feature` subclass in its own folder under `src/PeakXR/`, with a stable `Id` from the design table. Patches are `[HarmonyPatch]` prefix/postfix classes applied with `Patch(typeof(...))` inside `Install()`. Resolve private members with `GameMember` inside `Install()`.
- Settings: bind your own `Plugin.Settings.Bind(section, key, default, new ConfigDescription(text, AcceptableValueRange/List))` entries in `Install()`. Section names start with the area, e.g. `Comfort`, `Locomotion`, `Interaction`. Defaults favour comfort.
- Pure math and state machines go in `src/PeakXR.Core` with xunit tests in `tests/PeakXR.Core.Tests` (test first). Unity-facing code stays thin.
- VR hot paths run at 90-144 Hz: cache components and lookups, keep `Update`/`LateUpdate` allocation-free, no LINQ or `Find*` per frame.
- Every behaviour that only makes sense in VR checks `VRSession.Active` at runtime.
- Match the existing style: file-scoped namespaces, short XML doc comments on public types explaining *why*.
- Shared contracts (`src/PeakXR/VR/*`, `src/PeakXR/Avatar/AvatarPose*.cs`, `src/PeakXR/Framework/*`, `Plugin.cs`, csproj files) belong to the orchestrator. When you need a contract change, make the smallest additive change and list it in your report.

## Tooling

mise provides everything, rootless: run `mise trust` once in a new worktree, then `mise run build` and `mise run test` (or `mise exec -- dotnet ...`). Game folder writes (`mise run deploy`) belong to the user.

Run `mise run format` (CSharpier) before every commit; `mise run format-check` must be clean. Analyzers run in every build: CA rules at `latest-all`, enforced IDE style rules, Roslynator, SonarAnalyzer, Meziantou, and for the plugin Microsoft.Unity and BUTR.Harmony. With `CI=true` all warnings are errors: fix them, or turn a rule off in `.editorconfig` (grouped by analyzer) or with a `#pragma` around a false positive, each with a one-line reason. Test a `UnityEngine.Object` with `== null` / `!= null`, which see destroyed objects (UNT0007/0008/0023/0029 are errors). Never use default interface methods or `[ModuleInitializer]` (Unity Mono). `mise run coverage` writes cobertura XML to `TestResults/`.

## Done means

1. `mise run build` reports 0 errors and 0 warnings in files you touched, and `mise run test` is green.
2. Every patch target you use exists in the current decompile (cite file:line in the patch class comment).
3. Work is committed on your branch with a Conventional Commits message.
4. Your report lists: features and settings added, patch targets, contract changes, what needs in-game verification, and known gaps.
