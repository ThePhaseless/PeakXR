# PEAK creative/test mode research (2026-10-19)
Game: PEAK 2.6.b (version.txt), Mono build (PEAK_Data/Managed + MonoBleedingEdge present) -> BepInEx 5.x. Nothing installed in game dir (no BepInEx folder yet). Host OS Linux (Proton): see "Linux".

## A. Web

### BepInEx pack
- BepInEx-BepInExPack_PEAK 5.4.75301 (upd 2025-07-31; ~3.6M dl). Page https://thunderstore.io/c/peak/p/BepInEx/BepInExPack_PEAK/
- DL https://thunderstore.io/package/download/BepInEx/BepInExPack_PEAK/5.4.75301/
- Contents: BepInEx 5.4.23.3 win_x64 (BepInEx 5, Mono; not BepInEx 6). Preconfigured BepInEx.cfg (HideManagerGameObject=true). winhttp.dll = fork https://github.com/CyR1en/UnityDoorstop-PEAK (bypasses -force-vulkan). No separate Unity-6 doorstop config mentioned in readme; fork is the config.
- Crash on startup: add launch option `-dx12`. Manual install: extract elsewhere, copy contents of the `BepInExPack` folder into game folder (next to PEAK.exe). Plugins go in BepInEx/plugins.
- Many mods still depend on older name versions (BepInExPack_PEAK 5.4.2403 or BepInExPack 5.4.2100); 5.4.75301 is the current one and the 2026 mods target it. Old-version deps are only a manager constraint.
- Not verified: that the pack's doorstop works under Unity 6000.3.15f1 specifically; 2026 mods (updated Oct 2026, e.g. PEAK_MX 1.1.2) run on it, so assume yes. Test first launch.
- Linux/Proton: winhttp.dll proxy needs `WINEDLLOVERRIDES="winhttp=n,b" %command%` in Steam launch options (standard; not in pack readme). Add `-dx12` only if crash.
- PEAKLib (PEAKModding-PEAKLib_Core 1.7.2, _UI 1.6.1, _Items 1.6.2 upd 2026-09) = community API for custom items/UI; some mods depend on it.

### Recommended candidates (all BepInEx 5; no data on "game 2.6" field in API; "works" judged by last-update date 2026-08..10 and 2.x-era descriptions)
| Mod | Ver / upd | Does | Deps | Licence | All clients? | URL |
|---|---|---|---|---|---|---|
| Spoopylocal-Console_Unlocker | 1.1.2 / 2025-06-22 (OLD, unverified on 2.6) | Unlocks game's built-in dev console (F1): every [ConsoleCommand] in section B. Adds WeatherCommands.ToggleSnowStorm, PlayerCommands.WarpTo/WarpRandom/TickPlayer | BepInExPack 5.4.2100 | not stated (closed?) | client-local; host-gated actions via RPC | https://thunderstore.io/c/peak/p/Spoopylocal/Console_Unlocker/ |
| keklick1337-AdvancedConsole | 1.1.9 / 2025-07-22 (OLD) | Extends console UI (F1): spawn cmds, tp, map select (host) tab, hotkeys tab, player mgmt. Has kick tools. | BepInExPack_PEAK 5.4.2403 | repo not found | RPC-synced; "works for players without the mod" for map change | https://thunderstore.io/c/peak/p/keklick1337/AdvancedConsole/ |
| maxkir041-PEAK_MX | 1.1.2 / 2026-10-07 | 150+ funcs: god/stamina/status/afflictions editor, inventory editing + item spawn, Entities spawner (beetle, scorpion, frog[host], Scoutmaster, mushroom zombie, bee swarm, tornado[needs tornado map]) at crosshair or at player, biome teleport, world object finder, ESP, larger lobby. Menu key Insert. Also has anti-cheat/prank junk. | BepInExPack_PEAK 5.4.75301 | MIT https://github.com/maxkir041/PEAK-MX | menu client-only; host-only for some (frogs, host admin) | https://thunderstore.io/c/peak/p/maxkir041/PEAK_MX/ |
| tony4twenty-So_Fly | 2.1.0 / 2026-08-22 | Dev fly+noclip, invuln, warp to campfire (T), revive, afflictions add/clear, detect ESP (items/mobs/hazards), item IDs, delete dropped items, teleport. F2 menu. Host-gated for other players; solo/host = everything. | 5.4.75301 | not stated | host gating handshake; fine solo | https://thunderstore.io/c/peak/p/tony4twenty/So_Fly/ |
| lllei-ItemSpawnerEnhanced | 1.5.0 / 2026-09-30 | Client-side item browser, F5, search/tags/favourites, controller support, spawn for any scout | 5.4.75301 | MIT https://github.com/llleixx/ItemSpawnerEnhanced | client-side only | https://thunderstore.io/c/peak/p/lllei/ItemSpawnerEnhanced/ |
| cherrycove-ItemBrowser | 0.2.4 / 2026-04-03 | F5 item browser; spawns via GameUtils.InstantiateAndGrab (host needs no mod); optional console cmd `ItemBrowser.Spawn <Item>` with Console Unlocker | 5.4.75301-era; PEAKLib_Core 1.7.2, PEAKLib_UI 1.6.1 | MIT https://github.com/cherrycove/ItemBrowser | host needn't have it | https://thunderstore.io/c/peak/p/cherrycove/ItemBrowser/ |
| khalil-ItemSpawnerPremium | 2.4.0 / 2026-09-10 | Standalone F5 item spawner | 5.4.75301 | none found | client | https://thunderstore.io/c/peak/p/khalil/ItemSpawnerPremium/ |
| quackandcheese-ItemSpawner | 0.1.4 / 2025-07-14 (OLD, 350k dl) | Simple item spawner UI | BepInExPack_PEAK 5.4.2403, Hamunii-AutoHookGenPatcher 1.0.7 | ? | | https://thunderstore.io/c/peak/p/quackandcheese/ItemSpawner/ |
| lllei-FreeFly | 1.3.0 / 2026-09-01 | Local noclip flight + stage-aware teleport menu | 5.4.75301 | ? | local | https://thunderstore.io/c/peak/p/lllei/FreeFly/ |
| SiniK-ZombieSpawner | 1.0.0 / 2025-11-13 | J = spawn MushroomZombie at raycast | 5.4.2403 | ? | host to spawn | https://thunderstore.io/c/peak/p/SiniK/ZombieSpawner/ |
| snowybunny-PeakAMap | 1.2.12 / 2026-10-02 | Pick map (+biome variants shown) in boarding UI | 5.4.75301 | MIT https://github.com/snowy-bunny/peak-a-map | host only needs it | https://thunderstore.io/c/peak/p/snowybunny/PeakAMap/ |
| chuxiaaaa-PeakLevelSelect | 1.0.6 / 2026-08-12 | Level select at gate kiosk | 5.4.2100 | MIT | host | https://thunderstore.io/c/peak/p/chuxiaaaa/PeakLevelSelect/ |
| Snosz-TerrainRandomiser | 1.2.0 / 2026-09-08 | Seeded terrain / pick biome variants | 5.4.75301, Snosz-PhotonCustomPropsUtils 1.1.0 | ? | host/all? | https://thunderstore.io/c/peak/p/Snosz/TerrainRandomiser/ |
| Others seen | | KystoStudio-Admin_menukz 2.6.1 (F5 admin menu, bot spawner, fly, inf stamina; deps 5.4.2100); LIGHTNING_X-CubeX 3.4.1 (menu, host controls); RuangfafaPersonalTeam-PEAK_MENU_CN 0.4.0 (item spawn, tp; deps AutoHookGenPatcher, DetourContext_Dispose_Fix); k1r_gamer-PEAK_AIO_APEX 1.1.2 (ImGui; inventory editor, enemy spawn host-only, needs penswer-DearImGuiInjection); penswer-Everything; CptPICHU-TrueFinalAscent 2.9.4 | | | | |

Recommendation: BepInExPack_PEAK 5.4.75301 + PEAK_MX (single menu: god, stamina, items, entities, biome tp; MIT, updated 5 days ago) + Console_Unlocker (cheap access to all first-party commands: Item.Add, SetTimeOfDay, JumpToSegment, SetMapNumber, InfiniteStamina). Optional: So_Fly (noclip), ItemSpawnerEnhanced (MIT item browser), PeakAMap (map pick). Caveat: Console_Unlocker last updated 2025-06 (pre-2.x); if it fails on 2.6, our own plugin can do its job in one line (see B.2).
Anti-cheat risk: PEAK_MX/KinTheInfinite-PeakAntiCheatReborn exist; only use in private lobbies. Mods mark modded lobbies; use solo/offline for VR tests.
Mod-menu mods are IMGUI/UGUI screen-space: they likely will NOT render in VR; rely on console hotkeys or our own VR UI calling APIs in B.

## B. Local decomp (/home/thephaseless/Projects/PeakVR/decomp/)
Paths relative to Assembly-CSharp/ unless noted.

### B.1 Built-in dev console (Zorro.Core.CLI)
- Zorro.Core.Runtime/Zorro.Core.CLI/DebugUIHandler.cs: `public static bool AllowOpen;` line 44 (never set true in shipped code -> console is locked; this is all Console_Unlocker needs to flip). Open toggled by `InputForZorroCore.Open` (Update, ~line 223-238, `if (AllowOpen) Show()`); F1 per Console Unlocker. Hotkeys: field `Hotkeys` (ConsoleHotkey(KeyCode, command string), Zorro.Core.Runtime/ConsoleHotkey.cs), run via `ConsoleHandler.ProcessCommand` at line 219.
- Zorro.Core.Runtime/Zorro.Core.CLI/ConsoleHandler.cs: `public static async Task<bool> ProcessCommand(string command)` line 78. Callable directly from a plugin (no UI needed): `ConsoleHandler.ProcessCommand("Item.Add Rope")`. `ConsoleCommands` list line 38, `FindSuggestions` line 181. Initialised in GameHandler.cs:154-157 (`ScanForConsoleCommands` finds [ConsoleCommand] statics).
- Zorro.Core.CLI/ConsoleCommands.cs lines 14-95: static explicit command list (names = [ConsoleClassCustomizer("X")] class prefix + method). Type parsers lines 98-140 (Item via ItemCLIParser -> ObjectDatabaseAsset.GetObjectFromString, bool, int, float, Segment? via enum, ScriptPath).
- Gated debug UI: MapDebugUI.cs:86 `Start()`: disabled unless Application.isEditor || Debug.isDebugBuild; PlayerPrefs "DebugUIEnabled". CinemaCamera.cs:39 (C+M, editor/debug build only). PlayerConnectionLog.cs:152. DebugSpawner.cs (editor only, key 0, Object.Instantiate local only - useless). GameHandler.cs:65 IsOnDeveloperIsland editor-only.

### B.2 Commands (all `[ConsoleCommand] public static`)
- Item.Add(Item): ItemDatabase.cs:57 -> Add(Item,Vector3) line 70 -> `PhotonNetwork.Instantiate("0_Items/"+item.name, point, identity, 0).GetComponent<Item>().RequestPickup(Character.localCharacter...)`. Raycasts from MainCamera.
- Character.InfiniteStamina Character.cs:2015 (toggles localCharacter.infiniteStam); GainFullStamina :474; LockStatuses :2025 (toggle _debugStatusLock = freeze afflictions); Revive :1766; Die :696; PassOut :986; Zombify :789; TestWarp :795 (+50m up); TestWin :859; WarpToSpawn :2290 (RPC "WarpPlayerRPC" to SpawnPoint.allSpawnPoints[0]); RagdollExorcism.
- No true god mode command: closest = InfiniteStamina + LockStatuses + CharacterAfflictions.ClearAll (CharacterAfflictions.cs, class prefix "Afflictions"; Die :1537; Add*/Clear* for Cold Curse Drowsy Hot Hunger Injury Petrify Poison Spores...). Real god mode needs Harmony patch (e.g. on CharacterAfflictions status add / Character.Die) or set statuses each frame.
- Time of day: DayNightManager.SetTimeOfDay(float) DayNightManager.cs:272 (instance.timeOfDay; range 0-48; clamp bug: values >48 set to 48 then overwritten).
- Level/biome: MapHandler.JumpToSegment(Segment) MapHandler.cs:805 (master client only; non-master sends RPC "RPC_WarpToSegment" to master); JumpToNextSegment :798; JumpToSegmentLogic :862 (private). enum Segment Segment.cs: Beach, Tropics, Alpine, Caldera, TheKiln, Peak, Void. MountainProgressHandler.JumpToSegment(int,float) MountainProgressHandler.cs:78. Map (scene/seed) select: MapDebugUI.SetMapNumber(int) :131 / IncrementLevel :124 set NextLevelService.debugLevelIndexOffset (next level, not instant); MapBaker.ScenePaths (SingletonAsset<MapBaker>). Third-party PeakAMap/PeakLevelSelect/AdvancedConsole Map tab do instant/kiosk selection.
- Other: MapHandler.ForceSpawnGhostBall :791; NetworkingUtilities.SetMaxPlayers(int) Peak.Network/NetworkingUtilities.cs:42; Ascents.UnlockAll :260; AchievementManager.UnlockAll :538; RunSettings.Increment/PrintValue/ResetAll/Test; StuffFinder.Find(StuffType {Amulet,Item,Luggage,Mob,Bug}) Peak.Development/StuffFinder.cs:13 (ESP-like finder, ConsoleCommand); Backpack.PrintBackpacks; Player.PrintInventory; CharacterCustomization.Randomize; PassportManager.TestAllCosmetics; LevelGeneration.Run; ApplicationCLI.SetTargetFramerate.
- Free cam: Zorro.Core.Runtime/Zorro.Core/FreeCamera.cs (MonoBehaviour: WASD/QE/LeftShift, MovementForce) not wired to a command; can AddComponent. Also CinemaCamera.cs:39 (debug build only).

### B.3 Item database / listing all items
- ItemDatabase.cs:10 `class ItemDatabase : ObjectDatabaseAsset<ItemDatabase, Item>`; singleton `SingletonAsset<ItemDatabase>.Instance`.
  - `.Objects` (List<Item>, all item prefabs) via Zorro.Core.Runtime/Zorro.Core/ObjectDatabaseAsset.cs / DatabaseAsset.cs.
  - `ObjectDatabaseAsset<ItemDatabase,Item>.GetAllObjectNames()` (string[]) and `.GetObjectFromString(name)` (whitespace-insensitive, ObjectDatabaseAsset.cs:9-30).
  - `itemLookup` Dictionary<ushort,Item> (id->Item), `ItemDatabase.TryGetItem(ushort|string, out Item)` ItemDatabase.cs:81-98 (string = prefab GameObject.name).
- Item prefab path convention: PhotonNetwork prefab name = "0_Items/" + prefab.name (Resources-style folder in PhotonNetwork prefab pool). Item class: Item.cs (Interact(Character), RequestPickup(PhotonView), UIData.itemName, itemID).
- Debug dump: GameUtils.DebugAllItems (ContextMenu, GameUtils.cs:279) logs names.

### B.4 Spawn APIs (what our plugin can call)
Items:
1. `ItemDatabase.Add(Item item)` (camera raycast, picks up) / `ItemDatabase.Add(Item item, Vector3 point)` -- simplest; any client. Network-instantiates with Photon; owner = caller.
2. `PhotonNetwork.Instantiate("0_Items/"+name, pos, rot, 0)` (used also Item.cs:1380, Breakable.cs:99, CharacterItems.cs:530,1111).
3. `PhotonNetwork.InstantiateItemRoom(prefabName, pos, rot)` (Photon extension, assembly not in decomp; room-owned item, used by spawners e.g. Spawner.cs:238, CharacterItems.cs:360/415, GameUtils.cs:270). Master-client oriented; `warnIfNotHost:` param exists.
4. `GameUtils.instance.InstantiateAndGrab(Item item, Character character, int cookedAmount=0)` GameUtils.cs:274 -> RPC "InstantiateAndGrabRPC" to MasterClient, which does InstantiateItemRoom at the character's hip + Interact (pickup). Works for non-host clients; host needs no mod. Best for multiplayer; ItemBrowser uses it.
Mobs/entities (network-instantiated by prefab NAME in Photon pool; no shared database; names found in code):
- "MushroomZombie_Player" Character.cs:811 (MushroomZombie.cs; RPC_Arise); mushroomZombiePrefab spawned by MushroomZombieSpawner.cs:65 (prefab name = mushroomZombiePrefab.gameObject.name; name exact string unknown here - read from asset). ZombieManager.cs (zombies/spawners lists, RegisterZombie).
- "Character_Scoutmaster" ScoutmasterSpawner.cs:24 via InstantiateRoomObject (master only); Scoutmaster.cs. Scoutmaster ghost orbiter: ScoutmasterSoulPillar.cs:178.
- "Tornado" TornadoSpawner.cs:45 (+RPC RPCA_InitTornado needs spawner view id); Tornado.cs.
- Beehive.cs:51 beeSwarmPrefab (BeeSwarm.cs); "BugfixOnYou" (Bugfix tick, Bugfixer.cs:19, TickTrigger.cs:32, AttachBug(viewID)); "PetrifiedScout" Character.cs:785; "PlayerGhost" MainCameraMovement.cs:146; "TiedBalloon" CharacterBalloons.cs:101; ghost ball: GhostBallSpawner.cs:142 (ForceSpawnGhost via MapHandler.ForceSpawnGhostBall); RavenPerch.cs:144 voiceThiefPrefab.
- Mob base class: Mob.cs:7 (`Mob : MonoBehaviourPunCallbacks`; subclasses Beetle.cs:6, Scorpion.cs:4, FrogTongue.cs:6; MobItem.cs, MobManager.cs `instance` -- needs map's entity manager per PEAK_MX note). Other hazard classes: Spider.cs:8 (+SpiderTrigger), Antlion.cs:7, VenusFlyTrap.cs:6, SlipperyJellyfish.cs, SpikeTrap (Peak/), Lava/LavaRising.cs:9/LavaTides, Tornado, Geysers (not searched), CactusBall/StickyCactus, BotBoar.cs:3.
- To enumerate all spawnable prefab names at runtime: Photon's prefab pool `PhotonNetwork.PrefabPool` is DefaultPool (`.ResourceCache` Dictionary<string,GameObject>) -- not in decomp, inspect with dnSpy/runtime; plugin can iterate `((DefaultPool)PhotonNetwork.PrefabPool).ResourceCache.Keys` to list ALL network-spawnable prefabs (items under "0_Items/", mobs/hazards, "Character", "Player"). Alternative: Resources.LoadAll<GameObject>("") and filter by PhotonView component. Not verified in this pass: ResourceCache is filled lazily on first Instantiate, so use Resources.LoadAll.
- Local-only spawn (no network): GameUtils.RPC_SpawnResourceAtPosition(string resourcePath, Vector3) GameUtils.cs:302 does Resources.Load(path)+Instantiate; trigger via `GameUtils.instance.SpawnResourceAtPositionNetworked(path,pos,RpcTarget)` :296. BingBongSpawnTool.cs: dev tool (components with folder="0_Items/", objectToSpawn) -> PhotonNetwork.Instantiate(folder+name) on mouse hold; only exists if its component is in a scene (BingBong item).
- Player: Character.localCharacter, Player.localPlayer, `PlayerHandler.GetAllPlayers()`, `Character.localCharacter.photonView.RPC("WarpPlayerRPC", RpcTarget.All, Vector3, bool)` for teleport (Character.cs:2290 usage). `SpawnPoint.allSpawnPoints`.

### B.5 Minimal own test-harness plugin plan (BepInEx 5)
- On Awake/scene load: `DebugUIHandler.AllowOpen = true;` (replaces Console_Unlocker; console opens with Zorro open input, F1). Or bypass UI: `_ = ConsoleHandler.ProcessCommand("Item.Add Rope")`; map keys/VR controller buttons to ProcessCommand strings.
- Spawn every item: foreach item in `SingletonAsset<ItemDatabase>.Instance.Objects`: `ItemDatabase.Add(item, pos + offset)` (or PhotonNetwork.Instantiate("0_Items/"+item.name,...)).
- Mobs: PhotonNetwork.Instantiate(prefabName,...) (master for room objects); find names by enumerating prefabs as above.
- God mode: Harmony prefix on damage/status path (CharacterAfflictions add status methods) or per-frame ClearAll; stamina: Character.InfiniteStamina toggle (`localCharacter.infiniteStam`).
- Biome/level: MapHandler.JumpToSegment((Segment)n) (host/offline), time: DayNightManager.SetTimeOfDay(h).
- Offline/solo: use NetCode.Session.IsOffline (seen in MapHandler.cs:~810) mode so master-client checks pass.

## Gaps / unverified
- No 2.6-compat metadata on Thunderstore; judged by update date. Console_Unlocker/AdvancedConsole/ItemSpawner (quackandcheese) are pre-2.x -- test.
- PhotonNetwork.InstantiateItemRoom/ PrefabPool contents not in decomp; mob prefab asset names (except those in strings above) need asset inspection of PEAK_Data/Resources / data.unity3d.
- Licences: MIT confirmed for PEAK_MX, ItemSpawnerEnhanced, ItemBrowser, PeakLevelSelect, PeakAMap; Ruangfafa MENU NOASSERTION; AdvancedConsole/ItemSpawnerPremium/Console_Unlocker/So_Fly no repo/licence found.
