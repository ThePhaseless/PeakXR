using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using PeakXR.Core;
using PeakXR.VR;
using Photon.Pun;
using UnityEngine;
using Zorro.Core;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

namespace PeakXR.Sandbox;

/// <summary>
/// Everything the sandbox can do, as plain static methods with no UI types. The console commands, the
/// flat IMGUI window and the future VR panel all call these. Every action checks <see cref="Active"/>
/// first and reports through <see cref="Say"/>, so a caller only has to show <see cref="LastMessage"/>.
/// </summary>
public static class SandboxActions
{
    const float TestLineSpacing = 0.8f;
    const int TestLinePerRow = 12;
    const int TestLinePerFrame = 4;

    /// <summary>Segments the jump list offers (the game's enum also has <c>Void</c>).</summary>
    public static readonly Segment[] Segments =
    {
        Segment.Beach,
        Segment.Tropics,
        Segment.Alpine,
        Segment.Caldera,
        Segment.TheKiln,
        Segment.Peak,
    };

    /// <summary>Game-time presets, in hours (the day runs 0 to 24; sunrise 5, sunset 21).</summary>
    public static readonly float[] TimePresets = { 0f, 6f, 9f, 12f, 18f, 21f };

    static readonly CharacterAfflictions.STATUSTYPE[] clearable =
    {
        CharacterAfflictions.STATUSTYPE.Injury,
        CharacterAfflictions.STATUSTYPE.Hunger,
        CharacterAfflictions.STATUSTYPE.Cold,
        CharacterAfflictions.STATUSTYPE.Poison,
        CharacterAfflictions.STATUSTYPE.Curse,
        CharacterAfflictions.STATUSTYPE.Drowsy,
        CharacterAfflictions.STATUSTYPE.Hot,
        CharacterAfflictions.STATUSTYPE.Spores,
        CharacterAfflictions.STATUSTYPE.Web,
        CharacterAfflictions.STATUSTYPE.FlyTrap,
        CharacterAfflictions.STATUSTYPE.Petrify,
    };

    static readonly List<Item> items = new();
    static int itemsSourceCount = -1;
    static bool testLineRunning;

    // Resolved by SandboxFeature.Install; null makes the free camera report "unavailable".
    internal static AccessTools.FieldRef<MainCameraMovement, bool> GodCamRef;

    // ---------------------------------------------------------------- state

    /// <summary>Config switch <c>Sandbox.Enabled</c>, or forced on for this run by the self-test.</summary>
    public static bool Enabled => ForcedNow || SandboxFeature.EnabledEntry?.Value == true;

    /// <summary>Set by <c>sandbox.selftest</c>; never written to the config file.</summary>
    internal static bool Forced;

    /// <summary><see cref="Forced"/> counts only offline (Play Solo). It is dropped for good once this client is in
    /// an online room, so a session hosted after <c>-peakxr-selftest-hold</c> gets no sandbox.</summary>
    static bool ForcedNow
    {
        get
        {
            if (Forced && PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode)
                Forced = false;
            return Forced && PhotonNetwork.OfflineMode;
        }
    }

    /// <summary>The object the last successful item or entity spawn created, so the self-test can check and remove it.</summary>
    internal static GameObject LastSpawned { get; private set; }

    /// <summary>True when the sandbox may act: enabled, and either offline or we are the master client.
    /// This keeps it out of other people's lobbies.</summary>
    public static bool Active =>
        Enabled && (PhotonNetwork.OfflineMode || PhotonNetwork.IsMasterClient);

    public static bool GodMode { get; private set; }

    public static string LastMessage { get; private set; } = "";

    public static event Action<string> Message;

    /// <summary>Report to the game console, the BepInEx log and any UI that listens.</summary>
    public static void Say(string text)
    {
        LastMessage = text;
        Debug.Log("[Sandbox] " + text);
        Message?.Invoke(text);
    }

    static bool Guard(bool needCharacter = true)
    {
        if (!Enabled)
        {
            Say("Sandbox is disabled (config Sandbox.Enabled).");
            return false;
        }
        if (!Active)
        {
            Say("Sandbox only works offline or as the host.");
            return false;
        }
        if (needCharacter && Character.localCharacter == null)
        {
            Say("No local character yet (load into a run first).");
            return false;
        }
        return true;
    }

    static bool InRoom => PhotonNetwork.CurrentRoom != null;

    // ---------------------------------------------------------------- items

    /// <summary>Every item prefab in the game's <c>ItemDatabase</c>, sorted by name.</summary>
    public static IReadOnlyList<Item> Items
    {
        get
        {
            var db = SingletonAsset<ItemDatabase>.Instance;
            int count = db != null ? db.Objects.Count : 0;
            if (count != itemsSourceCount)
            {
                itemsSourceCount = count;
                items.Clear();
                if (db != null)
                {
                    foreach (var item in db.Objects)
                    {
                        if (item != null)
                            items.Add(item);
                    }
                }
                items.Sort(
                    (a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase)
                );
                ItemsVersion++;
            }
            return items;
        }
    }

    /// <summary>Bumped when <see cref="Items"/> is rebuilt.</summary>
    public static int ItemsVersion { get; private set; }

    /// <summary>
    /// Spawn an item on the ground in front of the player. Uses <c>PhotonNetwork.InstantiateItemRoom</c>, the
    /// call the game's own spawners use (room-owned, master client only, which is what Active guarantees).
    /// </summary>
    public static bool SpawnItem(Item item)
    {
        if (!Guard())
            return false;
        return SpawnItemAt(item, FrontPoint(SandboxFeature.SpawnDistance) + Vector3.up * 0.7f);
    }

    public static bool SpawnItemAt(Item item, Vector3 position)
    {
        if (!Guard(needCharacter: false))
            return false;
        if (item == null)
        {
            Say("Unknown item.");
            return false;
        }
        if (!InRoom)
        {
            Say("Not in a room yet.");
            return false;
        }
        var go = PhotonNetwork.InstantiateItemRoom(
            item.gameObject.name,
            position,
            Quaternion.identity
        );
        if (go == null)
        {
            Say($"Spawning {item.name} failed (see log).");
            return false;
        }
        LastSpawned = go;
        Say($"Spawned {item.name}.");
        return true;
    }

    /// <summary>
    /// Put an item into the player's hand or inventory. Uses <c>GameUtils.InstantiateAndGrab</c>, which asks
    /// the master client to create the item at the character's hip and pick it up, the same path the game's
    /// item browser mods use and the one that also works for non-host clients.
    /// </summary>
    public static bool GiveItem(Item item)
    {
        if (!Guard())
            return false;
        if (item == null)
        {
            Say("Unknown item.");
            return false;
        }
        if (GameUtils.instance == null || !InRoom)
        {
            Say("Not ready to give items (no GameUtils / room).");
            return false;
        }
        GameUtils.instance.InstantiateAndGrab(item, Character.localCharacter);
        Say($"Gave {item.name}.");
        return true;
    }

    /// <summary>
    /// Spawn every item in the database on a grid ahead of the player, a few per frame, for systematic
    /// testing (see docs/testing.md). Returns the number of items queued, or 0 when it could not start.
    /// </summary>
    public static int SpawnTestLine()
    {
        if (!Guard())
            return 0;
        if (testLineRunning)
        {
            Say("Test line already running.");
            return 0;
        }
        var driver = SandboxFeature.Driver;
        if (driver == null || !InRoom)
        {
            Say("Not ready to spawn the test line.");
            return 0;
        }

        var list = Items;
        var cam = AimTransform();
        var flat = new Vector3(cam.forward.x, 0f, cam.forward.z);
        var origin = Character.localCharacter.Center + flat.normalized * 3f;

        var positions = new System.Numerics.Vector3[list.Count];
        SpawnLayout.Grid(
            positions,
            new System.Numerics.Vector3(origin.x, origin.y, origin.z),
            new System.Numerics.Vector3(flat.x, 0f, flat.z),
            TestLineSpacing,
            TestLinePerRow
        );

        testLineRunning = true;
        driver.StartCoroutine(TestLineRoutine(list, positions));
        Say($"Spawning {list.Count} items ahead of you.");
        return list.Count;
    }

    static IEnumerator TestLineRoutine(
        IReadOnlyList<Item> list,
        System.Numerics.Vector3[] positions
    )
    {
        int spawned = 0;
        for (int i = 0; i < list.Count; i++)
        {
            if (!Active)
                break;

            var p = new Vector3(positions[i].X, positions[i].Y, positions[i].Z);
            // Drop each item onto the terrain below its grid slot so slopes do not bury or float it.
            var hit = HelperFunctions.LineCheck(
                p + Vector3.up * 8f,
                p + Vector3.down * 40f,
                HelperFunctions.LayerType.TerrainMap
            );
            if (hit.transform != null)
                p = hit.point + Vector3.up * 0.3f;

            if (
                list[i] != null
                && PhotonNetwork.InstantiateItemRoom(
                    list[i].gameObject.name,
                    p,
                    Quaternion.identity
                ) != null
            )
                spawned++;

            if ((i + 1) % TestLinePerFrame == 0)
                yield return null;
        }
        testLineRunning = false;
        Say($"Test line done: {spawned} of {list.Count} items spawned.");
    }

    // ---------------------------------------------------------------- mobs and entities

    public static IReadOnlyList<MobEntry> Mobs => MobCatalog.Entries;

    /// <summary>Re-scan prefabs. <paramref name="deep"/> loads all Resources roots once and may hitch.</summary>
    public static void RefreshMobs(bool deep)
    {
        MobCatalog.Refresh(deep);
        Say($"{MobCatalog.Entries.Count} spawnable entities found{(deep ? " (deep scan)" : "")}.");
    }

    public static bool SpawnMob(string name)
    {
        var entry = MobCatalog.Find(name);
        if (entry == null)
        {
            Say($"Unknown entity '{name}' (try the deep scan).");
            return false;
        }
        return SpawnMob(entry);
    }

    /// <summary>Spawn an entity on the ground a few meters in front of the player, facing them.</summary>
    public static bool SpawnMob(MobEntry entry)
    {
        if (!Guard())
            return false;
        var ground = GroundPoint(6f);
        var toPlayer = Character.localCharacter.Center - ground;
        toPlayer.y = 0f;
        var rotation =
            toPlayer.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toPlayer) : Quaternion.identity;
        return SpawnMobAt(entry, ground + Vector3.up * 0.5f, rotation);
    }

    /// <summary>
    /// Create an entity by prefab name. Mirrors how the game creates them: <c>InstantiateRoomObject</c> for
    /// characters (ScoutmasterSpawner.cs:24), <c>Instantiate</c> for the rest, plus the init each type needs.
    /// </summary>
    public static bool SpawnMobAt(MobEntry entry, Vector3 position, Quaternion rotation)
    {
        if (!Guard(needCharacter: false) || entry == null)
            return false;
        if (!InRoom)
        {
            Say("Not in a room yet.");
            return false;
        }
        if (entry.Style == SpawnStyle.Unsupported)
        {
            Say($"{entry.Name} cannot be spawned standalone: {entry.Note}");
            return false;
        }

        GameObject go;
        switch (entry.Style)
        {
            case SpawnStyle.Room:
                go = PhotonNetwork.InstantiateRoomObject(entry.Name, position, rotation, 0);
                break;

            case SpawnStyle.Tornado:
            {
                var spawner = UnityEngine.Object.FindAnyObjectByType<TornadoSpawner>();
                if (spawner == null || spawner.GetComponent<PhotonView>() == null)
                {
                    Say("Tornado needs a TornadoSpawner in the scene (a map with tornados).");
                    return false;
                }
                go = PhotonNetwork.Instantiate(entry.Name, position, rotation, 0);
                go?.GetComponent<PhotonView>()
                    .RPC(
                        "RPCA_InitTornado",
                        RpcTarget.All,
                        spawner.GetComponent<PhotonView>().ViewID
                    );
                break;
            }

            case SpawnStyle.AttachToPlayer:
            {
                if (Character.localCharacter == null)
                {
                    Say("No local character to attach to.");
                    return false;
                }
                go = PhotonNetwork.Instantiate(entry.Name, Vector3.zero, Quaternion.identity, 0);
                go?.GetComponent<Bugfix>()?.AttachBug(Character.localCharacter.photonView.ViewID);
                break;
            }

            case SpawnStyle.ItemMob:
                if (!EnsureMobManager())
                    return false;
                go = PhotonNetwork.InstantiateItemRoom(entry.Name, position, rotation);
                break;

            default:
                // Mob.Start registers with MobManager.instance; maps without mobs have none.
                if (entry.Kind == EntityKind.Mob && !EnsureMobManager())
                    return false;
                go = PhotonNetwork.Instantiate(entry.Name, position, rotation, 0);
                break;
        }

        if (go == null)
        {
            Say($"Spawning {entry.Name} failed (see log).");
            return false;
        }
        LastSpawned = go;
        Say($"Spawned {entry.Name}.");
        return true;
    }

    /// <summary>Mob.Start registers with MobManager.instance without a null check (Mob.cs:256). A local
    /// stand-in works for us, but peers have none and would throw for every mob, so refuse with company.</summary>
    static bool EnsureMobManager()
    {
        if (MobManager.instance != null)
            return true;
        // shortcut: a player who joins after the spawn still throws once; refuse online entirely if that matters.
        if (PhotonNetwork.CurrentRoom != null && PhotonNetwork.CurrentRoom.PlayerCount > 1)
        {
            Say("This map has no mobs; spawn mobs here only while you are alone in the lobby.");
            return false;
        }
        new GameObject("PeakXR.MobManager").AddComponent<MobManager>();
        return true;
    }

    // ---------------------------------------------------------------- player

    /// <summary>
    /// God mode: infinite stamina, statuses locked so none can be added, afflictions cleared on a short
    /// timer, and fall damage skipped by <see cref="FallDamagePatch"/>.
    /// </summary>
    public static bool SetGodMode(bool on)
    {
        if (on && !Guard())
            return false;
        GodMode = on;
        if (!on)
            ApplyGodFlags(false);
        Say($"God mode {(on ? "on" : "off")}.");
        return true;
    }

    public static bool ToggleGodMode() => SetGodMode(!GodMode);

    static void ApplyGodFlags(bool on)
    {
        var c = Character.localCharacter;
        if (c == null)
            return;
        c.infiniteStam = on;
        c.statusesLocked = on;
    }

    /// <summary>Called by the driver a few times a second.</summary>
    internal static void GodTick()
    {
        var c = Character.localCharacter;
        if (!GodMode || c == null || c.data == null || c.refs == null)
            return;

        ApplyGodFlags(true);
        if (c.data.dead)
            return;

        var afflictions = c.refs.afflictions;
        if (afflictions == null)
            return;

        bool dirty = false;
        for (int i = 0; i < clearable.Length; i++)
        {
            if (afflictions.GetCurrentStatus(clearable[i]) > 0.001f)
            {
                dirty = true;
                break;
            }
        }
        if (dirty)
        {
            // Same sequence as the game's Afflictions.ClearAll console command.
            c.data.SetPetrify(0);
            afflictions.RemoveAllThorns();
            afflictions.ClearAllStatus(excludeCurse: false, excludePetrify: false);
        }
        if (afflictions.afflictionList.Count > 0)
            afflictions.ClearAllAfflictions();
    }

    /// <summary>Revive the local character at the spot they died (game command <c>Character.Revive</c>).</summary>
    public static bool Revive()
    {
        if (!Guard())
            return false;
        var data = Character.localCharacter.data;
        if (!data.dead && !data.fullyPassedOut)
        {
            Say("You are not dead or passed out.");
            return false;
        }
        Character.Revive();
        Say("Revived.");
        return true;
    }

    public static bool WarpToSpawn()
    {
        if (!Guard())
            return false;
        if (SpawnPoint.allSpawnPoints.Count == 0)
        {
            Say("No spawn point in this scene.");
            return false;
        }
        Character.WarpToSpawn();
        Say("Warped to spawn.");
        return true;
    }

    public static bool JumpToSegment(Segment segment)
    {
        if (!Guard())
            return false;
        if (Singleton<MapHandler>.Instance == null)
        {
            Say("No map loaded (segments exist only in a run).");
            return false;
        }
        MapHandler.JumpToSegment(segment);
        Say($"Jumped to {segment}.");
        return true;
    }

    public static bool SetTimeOfDay(float hours)
    {
        if (!Guard(needCharacter: false))
            return false;
        if (DayNightManager.instance == null)
        {
            Say("No day/night cycle in this scene.");
            return false;
        }
        hours %= 24f;
        if (hours < 0f)
            hours += 24f;
        DayNightManager.instance.setTimeOfDay(hours);
        Say($"Time of day set to {hours:0.#}h.");
        return true;
    }

    // ---------------------------------------------------------------- camera

    public static bool FreeCamActive
    {
        get
        {
            var cam = Singleton<MainCameraMovement>.Instance;
            return GodCamRef != null && cam != null && GodCamRef(cam);
        }
    }

    /// <summary>Toggle the game's own fly camera (<c>MainCameraMovement.isGodCam</c>). Flat play only.</summary>
    public static bool ToggleFreeCam()
    {
        if (!Guard(needCharacter: false))
            return false;
        if (VRSession.Active)
        {
            Say("Free camera is flat-only; it would fight the headset.");
            return false;
        }
        var cam = Singleton<MainCameraMovement>.Instance;
        if (GodCamRef == null || cam == null)
        {
            Say("Free camera unavailable here.");
            return false;
        }
        bool on = !GodCamRef(cam);
        GodCamRef(cam) = on;
        Say(
            on ? "Free camera on (WASD, Space/E up, Q/Ctrl down, Shift fast)." : "Free camera off."
        );
        return true;
    }

    /// <summary>Called by the driver when the sandbox stops being active.</summary>
    internal static void Deactivate()
    {
        if (GodMode)
        {
            GodMode = false;
            ApplyGodFlags(false);
        }
        if (FreeCamActive)
            GodCamRef(Singleton<MainCameraMovement>.Instance) = false;
    }

    // ---------------------------------------------------------------- geometry helpers

    /// <summary>The transform whose forward means "where the player looks" in both flat and VR.</summary>
    static Transform AimTransform() =>
        MainCamera.instance != null
            ? MainCamera.instance.transform
            : Character.localCharacter.transform;

    /// <summary>A point <paramref name="distance"/> meters ahead of the player's eyes, flattened to chest height.</summary>
    public static Vector3 FrontPoint(float distance)
    {
        var forward = AimTransform().forward;
        forward.y = 0f;
        forward = forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;
        return Character.localCharacter.Center + forward * distance;
    }

    /// <summary>Terrain height at a point ahead of the player, or the player's height if nothing is below.</summary>
    static Vector3 GroundPoint(float distance)
    {
        var p = FrontPoint(distance);
        var hit = HelperFunctions.LineCheck(
            p + Vector3.up * 10f,
            p + Vector3.down * 60f,
            HelperFunctions.LayerType.TerrainMap
        );
        return hit.transform != null ? hit.point : p;
    }
}
