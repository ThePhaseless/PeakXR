using System.Collections;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using Peak.Network;
using PeakXR.Core;
using PeakXR.UI.Affordances;
using PeakXR.UI.Common;
using PeakXR.UI.Guide;
using PeakXR.VR;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PeakXR.Sandbox;

/// <summary>
/// The item room (feature <c>sandbox.room</c>): every item of the game's <c>ItemDatabase</c> on waist-high tables in the
/// middle of the most open floor around the player in the solo airport (<see cref="ShelfLayout"/>), helpful items
/// nearest, dangerous ones behind them, and what goes off by itself (dynamite) farthest out, each by kind
/// (<see cref="ItemShelf"/>). Beside them stands what the items need: a lit cooking fire heading the helpful tables, the
/// sandbox test wall with a target past the last tables, and an eye chart on the far wall for the binoculars; the
/// airport's hard floor breaks what breaks. A touch orb at each table restocks it, two at the aisle's start restock
/// everything (chevron) or leave to the title (cross), so the headset alone runs it. Offline only; the progress and
/// save guard (<see cref="SelfTestProgressGuard.Room"/>) holds while it is open. Statuses work in it (the airport
/// otherwise drops them, CharacterAfflictions.cs:741) and a keep-alive clears them before they knock you out.
/// </summary>
internal sealed class ItemRoom : MonoBehaviour
{
    private const float LoadTimeout = 120f;
    private const float ArrivalSettle = 2f;
    private const float LeaveTimeout = 60f;
    private const float TableHeight = 0.95f;
    private const float MaxReach = 40f;
    private const int SpawnPerFrame = 4;
    private const float KeepAliveEvery = 0.5f;
    private const float KeepAliveSum = 0.85f;
    private const int RestockAll = -1;
    private const int LeaveRoom = -2;

    /// <summary>Table colours by <see cref="ShelfGroup"/>: muted, and never the cue system's teal or red. Dangerous
    /// tables wear them darkened.</summary>
    private static readonly Color[] Palette =
    {
        new(0.85f, 0.55f, 0.25f),
        new(0.80f, 0.72f, 0.45f),
        new(0.90f, 0.90f, 0.85f),
        new(0.55f, 0.45f, 0.35f),
        new(0.45f, 0.55f, 0.80f),
        new(0.65f, 0.55f, 0.85f),
        new(0.50f, 0.55f, 0.55f),
        new(0.80f, 0.55f, 0.75f),
        new(0.45f, 0.60f, 0.35f),
        new(0.65f, 0.65f, 0.65f),
    };

    /// <summary><c>CharacterAfflictions.m_inAirport</c> (CharacterAfflictions.cs:160, set in Awake :272, read by
    /// <c>AddStatus</c> :741); set by <see cref="ItemRoomFeature"/>, null keeps the airport's statuses off.</summary>
    internal static AccessTools.FieldRef<CharacterAfflictions, bool> InAirportRef;

    /// <summary><c>Dynamite.dynamitePosition</c> (Dynamite.cs:34): set to the spawn spot in Awake (:44), and the fuse
    /// lights once the local character comes within <c>lightFuseRadius</c> of it (TestLightWick, :111-117), held or
    /// not. Set by <see cref="ItemRoomFeature"/>; null leaves the game's proximity wick as it is.</summary>
    internal static AccessTools.FieldRef<Dynamite, Vector3> WickRef;

    /// <summary>A wick spot no one reaches: the room's dynamite stays unlit until picked up.</summary>
    private static readonly Vector3 NoWick = new(0f, -100000f, 0f);

    /// <summary>Zone sign colours: the helpful area plain, the dangerous ones a warning amber (not the cue red).</summary>
    private static readonly Color[] ZoneColor =
    {
        Color.white,
        new(1f, 0.78f, 0.3f),
        new(1f, 0.6f, 0.2f),
    };

    private static readonly string[] ZoneSign =
    {
        "HELPFUL\n<size=50%>food, healing, tools, climbing gear</size>",
        "DANGEROUS\n<size=50%>hurts, kills, warps, poisons or explodes</size>",
        "GOES OFF BY ITSELF\n<size=50%>dynamite lights when you pick it up</size>",
    };

    public static ItemRoom Current { get; private set; }

    private enum Phase : byte
    {
        Loading,
        Open,
        Leaving,
    }

    private readonly List<Object> owned = new();
    private readonly Material[] groupMaterials = new Material[ShelfLayout.Buckets];
    private readonly List<Dynamite> wicks = new();
    private readonly List<StatusChange> statuses = new();
    private readonly HashSet<string> afflictions = new(System.StringComparer.Ordinal);
    private readonly List<KeyValuePair<TouchOrb, int>> orbs = new();
    private Phase phase;
    private bool inAirport;
    private float deadline;
    private float settled;
    private float keepAlive;
    private bool spawning;
    private Transform root;
    private CharacterAfflictions statusesOn;
    private GameObject firePrefab;
    private GameObject fireRoot;
    private Campfire fire;

    /// <summary>A lit campfire sets this shader global (Campfire.cs:478); put back when the room closes.</summary>
    private float fakeMountain = -1f;

    private ShelfPlan plan;
    private RoomRect room;
    private float igniteRadius;
    private Item[] slotItems;
    private Vector3[] slotAt;
    private GameObject[] slotSpawned;
    private Quaternion facing;

    // ---- entering and leaving ----

    /// <summary>The VR tab's "Item room" button and <c>Sandbox.Room</c>: opens the room from the title or the solo
    /// airport, or leaves an open one. A refusal always shows, sounds and buzzes at <paramref name="at"/>.</summary>
    public static void Toggle(Vector3 at, bool left)
    {
        if (Current != null)
        {
            if (Current.phase == Phase.Open)
                Current.Leave();
            else
                Refuse(at, left, DenyReason.NotNow, "The item room is still loading or leaving.");
            return;
        }
        if (!SandboxActions.Enabled)
        {
            Refuse(
                at,
                left,
                DenyReason.Locked,
                "Turn on Sandbox Enabled first: the item room is part of the sandbox."
            );
            return;
        }
        var scene = SceneManager.GetActiveScene().name ?? "";
        bool airport = scene.Contains("Airport", System.StringComparison.Ordinal);
        var entry = PracticeStart.Decide(
            PhotonNetwork.InRoom,
            PhotonNetwork.OfflineMode,
            scene == MainMenu.SceneName,
            airport && Character.localCharacter != null,
            LoadingScreenHandler.loading,
            NetworkingUtilities.EnteringRoom
        );
        if (entry == PracticeEntry.Refused)
        {
            Refuse(
                at,
                left,
                DenyReason.NotHere,
                "The item room opens from the title or the solo airport, offline only."
            );
            return;
        }
        if (entry == PracticeEntry.FromTitle && !PlaySolo())
        {
            Refuse(at, left, DenyReason.NotHere, "Play Solo did not start; item room not opened.");
            return;
        }
        var go = new GameObject("PeakXR_ItemRoom");
        DontDestroyOnLoad(go);
        Current = go.AddComponent<ItemRoom>();
        Current.inAirport = entry == PracticeEntry.InPlace;
        Current.deadline = Time.unscaledTime + LoadTimeout;
        SelfTestProgressGuard.Room = true;
        if (entry == PracticeEntry.InPlace)
            ClosePause();
        SandboxActions.Say("Opening the item room.");
    }

    /// <summary>The title's Play Solo button (MainMenu.cs:45 -> StartSoloSession, :133): offline mode, then the
    /// airport. It sets EnteringRoom and starts the load at once; without that, nothing is coming.</summary>
    private static bool PlaySolo()
    {
        var menu = FindAnyObjectByType<MainMenu>();
        if (menu == null || menu.playSoloButton == null)
            return false;
        menu.playSoloButton.onClick.Invoke();
        return NetworkingUtilities.EnteringRoom || LoadingScreenHandler.loading;
    }

    private static void Refuse(Vector3 at, bool left, DenyReason reason, string why)
    {
        SandboxActions.Say(why);
        if (!Affordance.Deny(left, at, reason))
        {
            CueAudio.Play(CueSound.Refused, at, 1f);
            UIHaptics.Tick(left, 0.4f, 0.06f);
        }
    }

    /// <summary>The pause menu's own quit (PauseMenuMainPage.cs:206): disconnecting state, then leave the room, which
    /// loads the title (NetworkConnector.cs:220). Outside a room the title loads directly.</summary>
    private void Leave()
    {
        if (phase == Phase.Leaving)
            return;
        phase = Phase.Leaving;
        deadline = Time.unscaledTime + LeaveTimeout;
        SandboxActions.Say("Leaving the item room for the title.");
        ClosePause();
        if (PhotonNetwork.InRoom)
        {
            GameHandler
                .GetService<ConnectionService>()
                .StateMachine.SwitchState<DisconnectingState>();
            NetworkConnector.LeaveRoom();
        }
        else
        {
            NetworkingUtilities.EnteringRoom = false;
            SceneManager.LoadScene(MainMenu.SceneName);
        }
    }

    /// <summary>The game's own close of the pause menu (PauseMenuHandler.cs:59).</summary>
    private static void ClosePause()
    {
        if (!GUIManager.InPauseMenu)
            return;
        var handler = FindAnyObjectByType<PauseMenuHandler>();
        if (handler != null)
            handler.gameObject.SetActive(false);
    }

    private void Awake() => SceneManager.activeSceneChanged += OnScene;

    /// <summary>Loading: note whether the airport arrived. Open or leaving: any scene change (the pause menu's quit,
    /// boarding the plane, our own leave) ends the room, never the run that follows.</summary>
    private void OnScene(Scene previous, Scene next)
    {
        if (phase == Phase.Loading)
            inAirport = (next.name ?? "").Contains("Airport", System.StringComparison.Ordinal);
        else
            Destroy(gameObject);
    }

    private void OnDestroy()
    {
        SceneManager.activeSceneChanged -= OnScene;
        if (Current == this)
            Current = null;
        SelfTestProgressGuard.Room = false;
        if (statusesOn != null)
            InAirportRef(statusesOn) = true;
        foreach (var pair in orbs)
            pair.Key.Destroy();
        foreach (var o in owned)
            if (o != null)
                Destroy(o);
        if (root != null)
            Destroy(root.gameObject);
        SandboxActions.RemoveWall();
        // Last, and guarded: the scene may be half torn down, and everything above must be gone regardless.
        try
        {
            if (slotSpawned != null)
                foreach (var go in slotSpawned)
                    Despawn(go);
            if (fakeMountain >= 0f)
                Shader.SetGlobalFloat("FakeMountainEnabled", fakeMountain);
            if (
                fireRoot != null
                && fireRoot.TryGetComponent<PhotonView>(out var view)
                && view.IsMine
            )
                PhotonNetwork.Destroy(fireRoot);
            else if (fireRoot != null)
                Destroy(fireRoot);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"[sandbox.room] cleanup: {e.Message}");
        }
        Plugin.Log.LogInfo("[sandbox.room] closed");
    }

    private void Update()
    {
        // Never online: a room that is not offline ends it on the spot, without touching the session.
        if (PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode)
        {
            Plugin.Log.LogWarning("[sandbox.room] online room: item room closed");
            Destroy(gameObject);
            return;
        }
        float dt = Time.unscaledDeltaTime;
        switch (phase)
        {
            case Phase.Loading:
                WaitForAirport(dt);
                break;
            case Phase.Open:
                TickOrbs(dt);
                KeepAlive(dt);
                Rearm();
                break;
            default:
                if (Time.unscaledTime > deadline)
                    Destroy(gameObject);
                break;
        }
    }

    private void WaitForAirport(float dt)
    {
        bool ready =
            inAirport
            && !LoadingScreenHandler.loading
            && Character.localCharacter != null
            && PhotonNetwork.InRoom
            && PhotonNetwork.OfflineMode;
        settled = ready ? settled + dt : 0f;
        if (settled >= ArrivalSettle)
        {
            phase = Phase.Open;
            Build(Character.localCharacter);
        }
        else if (Time.unscaledTime > deadline)
        {
            SandboxActions.Say("The solo airport did not load in time; item room not opened.");
            Destroy(gameObject);
        }
    }

    /// <summary>Statuses are on in the room, so a poison or a fall could knock you out: cleared well before that, and
    /// woken or revived if it happened anyway.</summary>
    private void KeepAlive(float dt)
    {
        keepAlive -= dt;
        if (keepAlive > 0f)
            return;
        keepAlive = KeepAliveEvery;
        var c = Character.localCharacter;
        if (c == null || c.data == null || c.refs == null || c.refs.afflictions == null)
            return;
        if (c.data.dead || c.data.fullyPassedOut)
            SandboxActions.Revive();
        else if (c.refs.afflictions.statusSum > KeepAliveSum)
        {
            SandboxActions.ClearAll(c);
            SandboxActions.Say("Item room: statuses cleared so you stay up.");
        }
    }

    // ---- building ----

    private void Build(Character c)
    {
        if (InAirportRef != null && c.refs != null && c.refs.afflictions != null)
        {
            statusesOn = c.refs.afflictions;
            InAirportRef(statusesOn) = false;
        }
        var buckets = Collect();
        var counts = new int[buckets.Length];
        int total = 0;
        for (int b = 0; b < buckets.Length; b++)
            total += counts[b] = buckets[b].Count;
        slotItems = new Item[total];
        for (int b = 0, i = 0; b < buckets.Length; b++)
            foreach (var item in buckets[b])
                slotItems[i++] = item;

        // Feet on the floor under the player; the room in the heading whose open floor holds the plan best, the
        // view first on a tie.
        var center = c.Center;
        var feet = Floor(center, center.y - 1f);
        var look = Vector3.ProjectOnPlane(
            MainCamera.instance != null
                ? MainCamera.instance.transform.forward
                : c.transform.forward,
            Vector3.up
        );
        if (look.sqrMagnitude < 1e-4f)
            look = Vector3.forward;
        look.Normalize();
        // Dynamite lights within its radius of where it lies: kept that far from you and everything else as well.
        var spec = new ShelfSpec { VolatileClearance = igniteRadius + 1f };
        var forward = look;
        for (int i = 0; i < 16; i++)
        {
            var dir = Quaternion.Euler(0f, i * 22.5f, 0f) * look;
            var rect = Measure(feet, dir);
            var candidate = ShelfLayout.Plan(counts, rect, spec);
            if (
                plan == null
                || (candidate.Fits && !plan.Fits)
                || (candidate.Fits == plan.Fits && candidate.Over < plan.Over - 0.01f)
            )
            {
                plan = candidate;
                room = rect;
                forward = dir;
            }
        }
        var right = Vector3.Cross(Vector3.up, forward);
        facing = Quaternion.LookRotation(forward);
        Vector3 At(float x, float z) => feet + right * x + forward * z;

        slotAt = new Vector3[total];
        slotSpawned = new GameObject[total];
        root = new GameObject("PeakXR_ItemRoomProps").transform;
        Tables(At, counts);
        StartOrbs(At(plan.StartX, 1f));
        Props(feet, At, forward);
        if (plan.Fits)
            Plugin.Log.LogInfo(
                $"[sandbox.room] {total} items on {plan.Tables.Length} tables, {plan.PerRow} a row, block "
                    + $"{plan.Block} in room {room} along {forward}, wall {(plan.HasWall ? "placed" : "left out")}"
            );
        else
        {
            Plugin.Log.LogWarning(
                $"[sandbox.room] the open floor here ({room}) is too small for the tables ({plan.Block}): "
                    + "some may stand in the walls"
            );
            SandboxActions.Say(
                "Item room: not enough open floor here, some tables may touch walls. Leave, play solo, walk into the open hall and press Enter there."
            );
        }
        StartCoroutine(Spawn(AllSlots()));

        var head =
            VRRig.Instance != null && VRRig.Instance.Head != null
                ? VRRig.Instance.Head.position
                : center;
        CueAudio.Play(CueSound.Notice, head + forward * 0.6f, 1f);
    }

    /// <summary>Every database item by bucket (zone and table, <see cref="ShelfLayout.Bucket"/>); those that cannot
    /// stand alone are logged and left out. The zone comes from what using the item does (<see cref="Effects"/>) and
    /// from a self-lighting wick. Also finds the fire: a constructable whose built prefab is a <see cref="Campfire"/>
    /// (the portable stove).</summary>
    private List<Item>[] Collect()
    {
        var buckets = new List<Item>[ShelfLayout.Buckets];
        for (int b = 0; b < buckets.Length; b++)
            buckets[b] = new List<Item>();
        var names = new HashSet<string>(System.StringComparer.Ordinal);
        var skipped = new StringBuilder();
        var dangerous = new StringBuilder();
        int skips = 0;
        foreach (var item in SandboxActions.Items)
        {
            names.Clear();
            foreach (var component in item.GetComponentsInChildren<Component>(true))
                if (component != null)
                    names.Add(component.GetType().Name);
            if (
                firePrefab == null
                && item.TryGetComponent<Constructable>(out var build)
                && build.constructedPrefab != null
                && build.constructedPrefab.GetComponentInChildren<Campfire>(true) != null
            )
                firePrefab = build.constructedPrefab;
            var reason = !item.TryGetComponent<PhotonView>(out _)
                ? "no PhotonView, cannot be spawned"
                : ItemShelf.SkipReason(item.name, names);
            if (reason != null)
            {
                skips++;
                skipped.Append(item.name).Append(" (").Append(reason).Append("); ");
                continue;
            }
            statuses.Clear();
            afflictions.Clear();
            foreach (var action in item.GetComponentsInChildren<ItemAction>(true))
                Effects(action);
            // The prefab never ran Awake, so its radius is not squared yet (Dynamite.cs:42).
            var wick = item.GetComponentInChildren<Dynamite>(true);
            float ignite = wick != null ? Mathf.Max(0f, wick.lightFuseRadius) : 0f;
            var harm = ItemShelf.HarmOf(names, statuses, afflictions);
            var zone = ItemShelf.ZoneOf(harm, ignite);
            if (zone == ItemZone.Volatile)
                igniteRadius = Mathf.Max(igniteRadius, ignite);
            if (zone != ItemZone.Helpful)
                dangerous
                    .Append(item.name)
                    .Append(" (")
                    .Append(zone == ItemZone.Volatile ? $"lights within {ignite:0.#} m" : harm)
                    .Append("); ");
            buckets[ShelfLayout.Bucket(zone, ItemShelf.GroupOf(item.name, names))].Add(item);
        }
        if (skips > 0)
            Plugin.Log.LogInfo($"[sandbox.room] {skips} items left out: {skipped}");
        Plugin.Log.LogInfo($"[sandbox.room] dangerous: {dangerous}");
        return buckets;
    }

    /// <summary>What using the item does to its user, from the action's own data: status changes with their sign
    /// (Action_ModifyStatus.cs:5-9, Affliction_AdjustStatus.cs:9-11, the over-time afflictions' statusPerSecond) and
    /// every other affliction by type (Affliction.cs:11-39).</summary>
    private void Effects(ItemAction action)
    {
        switch (action)
        {
            case Action_ModifyStatus modify:
                statuses.Add(new StatusChange(modify.statusType.ToString(), modify.changeAmount));
                break;
            case Action_ApplyAffliction apply:
                Effect(apply.affliction);
                if (apply.extraAfflictions != null)
                    foreach (var extra in apply.extraAfflictions)
                        Effect(extra);
                break;
        }
    }

    private void Effect(Peak.Afflictions.Affliction affliction)
    {
        switch (affliction)
        {
            case null:
                return;
            case Peak.Afflictions.Affliction_AdjustStatus adjust:
                statuses.Add(new StatusChange(adjust.statusType.ToString(), adjust.statusAmount));
                break;
            case Peak.Afflictions.Affliction_AdjustColdOverTime cold:
                statuses.Add(new StatusChange("Cold", cold.statusPerSecond));
                break;
            case Peak.Afflictions.Affliction_AdjustDrowsyOverTime drowsy:
                statuses.Add(new StatusChange("Drowsy", drowsy.statusPerSecond));
                break;
            case Peak.Afflictions.Affliction_AdjustStatusOverTime overTime:
                statuses.Add(new StatusChange("status over time", overTime.statusPerSecond));
                break;
            default:
                afflictions.Add(affliction.GetAfflictionType().ToString());
                break;
        }
    }

    /// <summary>The open floor in the frame of <paramref name="dir"/> from <paramref name="feet"/> (X right, Z
    /// ahead, the player at the origin): the walls of the room around the middle of the open span ahead, each the
    /// nearest of three parallel casts at knee and chest height so benches and planters count.</summary>
    private static RoomRect Measure(Vector3 feet, Vector3 dir)
    {
        var right = Vector3.Cross(Vector3.up, dir);
        float mid = Open(feet, dir, right, 1f, 1f) * 0.5f;
        var middle = feet + dir * mid;
        float left = Open(middle, -right, dir, 0.8f * mid, 0.8f * mid);
        float rightSide = Open(middle, right, dir, 0.8f * mid, 0.8f * mid);
        float ahead = Open(middle, dir, right, 0.8f * left, 0.8f * rightSide);
        return new RoomRect(-left, rightSide, 0f, mid + ahead);
    }

    /// <summary>Free distance along <paramref name="dir"/> from <paramref name="from"/> and from points
    /// <paramref name="back"/> and <paramref name="on"/> along <paramref name="across"/> beside it.</summary>
    private static float Open(Vector3 from, Vector3 dir, Vector3 across, float back, float on)
    {
        int mask = HelperFunctions.terrainMapMask | LayerMask.GetMask("Default");
        float reach = MaxReach;
        for (int k = -1; k <= 1; k++)
        for (int h = 0; h < 2; h++)
        {
            var start =
                from + Vector3.up * (h == 0 ? 0.5f : 1.3f) + across * (k < 0 ? -back : k * on);
            if (
                Physics.Raycast(
                    start,
                    dir,
                    out var hit,
                    MaxReach,
                    mask,
                    QueryTriggerInteraction.Ignore
                )
                && hit.distance < reach
            )
                reach = hit.distance;
        }
        return reach;
    }

    /// <summary>The floor under <paramref name="p"/>, or <paramref name="fallbackY"/> when there is none.</summary>
    private static Vector3 Floor(Vector3 p, float fallbackY)
    {
        var hit = HelperFunctions.LineCheck(
            p + Vector3.up * 1.5f,
            p + Vector3.down * 5f,
            HelperFunctions.LayerType.TerrainMap
        );
        return hit.transform != null ? hit.point : new Vector3(p.x, fallbackY, p.z);
    }

    /// <summary>A coloured plank under each table with its items' spots on it, at each bucket's first table a restock
    /// orb and its label, and a sign over each zone. A table that still meets level geometry is logged.</summary>
    private void Tables(System.Func<float, float, Vector3> at, int[] counts)
    {
        int mask = HelperFunctions.terrainMapMask | LayerMask.GetMask("Default");
        int lastGroup = -1;
        foreach (var t in plan.Tables)
        {
            var f = t.Footprint;
            var floor = Floor(at(f.CenterX, f.CenterZ), at(0f, 0f).y);
            for (int k = t.First; k < t.First + t.Count; k++)
            {
                var s = plan.Slots[k];
                var spot = at(s.X, s.Z);
                slotAt[k] = new Vector3(spot.x, floor.y + TableHeight, spot.z);
            }
            var hits = Physics.OverlapBox(
                floor + Vector3.up * (TableHeight * 0.5f + 0.1f),
                new Vector3(f.Width * 0.5f, TableHeight * 0.5f, f.Depth * 0.5f),
                facing,
                mask,
                QueryTriggerInteraction.Ignore
            );
            if (hits.Length > 0)
                Plugin.Log.LogWarning(
                    $"[sandbox.room] {ItemShelf.Title(ShelfLayout.GroupOf(t.Group))} table ({f}) meets {hits[0].name}"
                );
            var plank = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plank.name = "PeakXR_ItemRoomTable";
            Destroy(plank.GetComponent<Collider>());
            plank.transform.SetParent(root, false);
            plank.transform.SetPositionAndRotation(
                floor + Vector3.up * (TableHeight - 0.08f),
                facing
            );
            plank.transform.localScale = new Vector3(f.Width, 0.03f, f.Depth);
            plank.GetComponent<Renderer>().sharedMaterial = GroupMaterial(t.Group);
            if (t.Group == lastGroup)
                continue;
            lastGroup = t.Group;
            var beside = at(f.MinX - 0.25f, f.MinZ + 0.2f);
            var orbAt = new Vector3(beside.x, floor.y + TableHeight + 0.15f, beside.z);
            AddOrb(t.Group, cross: false, orbAt);
            Label(
                $"{ItemShelf.Title(ShelfLayout.GroupOf(t.Group))} ({counts[t.Group]})",
                orbAt + Vector3.up * 0.22f,
                facing,
                64f,
                ZoneColor[(int)t.Zone]
            );
        }
        for (int z = 0; z < (int)ItemZone.Count; z++)
        {
            var zone = plan.Zones[z];
            if (zone.Width <= 0f)
                continue;
            Label(
                ZoneSign[z],
                Floor(at(zone.CenterX, zone.MinZ - 0.4f), at(0f, 0f).y) + Vector3.up * 2.3f,
                facing,
                90f,
                ZoneColor[z]
            );
        }
    }

    /// <summary>In front of the player, chest high: restock everything (chevron) and leave (cross).</summary>
    private void StartOrbs(Vector3 floorAt)
    {
        var right = facing * Vector3.right;
        var at = floorAt + Vector3.up * 1.25f;
        AddOrb(RestockAll, cross: false, at - right * 0.22f);
        AddOrb(LeaveRoom, cross: true, at + right * 0.22f);
        Label("Restock all", at - right * 0.22f + Vector3.up * 0.12f, facing, 40f, Color.white);
        Label("Leave", at + right * 0.22f + Vector3.up * 0.12f, facing, 40f, Color.white);
    }

    /// <summary>The fire, the test wall with its target, and the eye chart, where the plan put them.</summary>
    private void Props(Vector3 feet, System.Func<float, float, Vector3> at, Vector3 forward)
    {
        if (firePrefab == null)
            Plugin.Log.LogInfo(
                "[sandbox.room] no portable fire in the item database: cook and warm hands in a run"
            );
        else
            PlaceFire(Floor(at(plan.Fire.CenterX, plan.Fire.CenterZ), feet.y));

        // The sandbox test wall past the last tables, its face toward them, a target painted on it.
        if (plan.HasWall)
        {
            var wall = SandboxActions.PlaceWall(at(plan.Wall.CenterX, plan.Wall.CenterZ), forward);
            if (wall != null)
            {
                var face = wall.transform.position - forward * 0.26f;
                var targetMat = Own(
                    UIMaterials.Create(new Color(0.95f, 0.85f, 0.3f, 1f), false, 3000)
                );
                var targetY = Floor(face, feet.y).y + 2f;
                for (int i = 1; i <= 3; i++)
                    Ring(new Vector3(face.x, targetY, face.z), -forward, 0.22f * i, targetMat);
            }
        }
        else
            Plugin.Log.LogInfo("[sandbox.room] no room for the test wall here");

        // An eye chart on the far wall, across from the test wall: only the top lines read without the binoculars.
        Label(
            "<size=60>E</size>\n<size=30>F P</size>\n<size=15>T O Z</size>\n<size=8>L P E D</size>\n"
                + "<size=4>P E C F D</size>\n<size=2>E D F C Z P</size>",
            at(Mathf.Max(room.CenterX, room.MaxX - 1.5f), room.MaxZ - 0.3f) + Vector3.up * 1.9f,
            facing,
            60f,
            Color.white,
            0.01f
        );
    }

    /// <summary>The stove's built fire, created as its <c>Constructable.FinishConstruction</c> does
    /// (Constructable.cs:168-175), then lit without a segment change or save (<c>LightWithoutReveal</c>,
    /// Campfire.cs:468 -> <c>Light_Rpc(false)</c>, :474).</summary>
    private void PlaceFire(Vector3 at)
    {
        var go =
            firePrefab.GetComponent<PhotonView>() != null
                ? PhotonNetwork.Instantiate(firePrefab.name, at, facing, 0)
                : Instantiate(firePrefab, at, facing);
        fireRoot = go;
        fire = go != null ? go.GetComponentInChildren<Campfire>() : null;
        Relight();
    }

    private void Relight()
    {
        if (fire == null || fire.Lit)
            return;
        if (fakeMountain < 0f)
            fakeMountain = Shader.GetGlobalFloat("FakeMountainEnabled");
        fire.LightWithoutReveal();
        Plugin.Log.LogInfo($"[sandbox.room] fire {(fire.Lit ? "lit" : "would not light")}");
    }

    // ---- items ----

    private List<int> AllSlots()
    {
        var all = new List<int>(slotItems.Length);
        for (int i = 0; i < slotItems.Length; i++)
            all.Add(i);
        return all;
    }

    /// <summary>Puts a table's items (or all, <see cref="RestockAll"/>) back: the ones lying anywhere in the world are
    /// removed and fresh ones placed. Held or packed ones stay yours.</summary>
    public bool Restock(int group)
    {
        if (phase != Phase.Open || plan == null)
            return false;
        if (spawning)
        {
            SandboxActions.Say("Still placing items.");
            return false;
        }
        var redo = new List<int>();
        for (int i = 0; i < slotItems.Length; i++)
        {
            if (group != RestockAll && plan.Slots[i].Group != group)
                continue;
            Despawn(slotSpawned[i]);
            slotSpawned[i] = null;
            redo.Add(i);
        }
        if (group == RestockAll)
            Relight();
        StartCoroutine(Spawn(redo));
        return true;
    }

    /// <summary>A few a frame, held still where they belong (kinematic until picked up: <c>Item.SetState</c> clears it,
    /// Item.cs:734-760). <c>PhotonNetwork.InstantiateItemRoom</c> is the game's own spawner call (GameUtils.cs:270).</summary>
    private IEnumerator Spawn(List<int> slots)
    {
        spawning = true;
        int placed = 0,
            failed = 0;
        for (int k = 0; k < slots.Count && phase == Phase.Open; k++)
        {
            int i = slots[k];
            try
            {
                var go = PhotonNetwork.InstantiateItemRoom(
                    slotItems[i].gameObject.name,
                    slotAt[i],
                    facing
                );
                if (go != null && go.TryGetComponent<Item>(out var item) && item.rig != null)
                    item.rig.isKinematic = true;
                // Before its first Update: Awake took this spot as the wick (Dynamite.cs:44).
                var dynamite =
                    go != null && WickRef != null
                        ? go.GetComponentInChildren<Dynamite>(true)
                        : null;
                if (dynamite != null)
                {
                    WickRef(dynamite) = NoWick;
                    wicks.Add(dynamite);
                }
                slotSpawned[i] = go;
                if (go != null)
                    placed++;
                else
                    failed++;
            }
            catch (System.Exception e)
            {
                failed++;
                Plugin.Log.LogWarning(
                    $"[sandbox.room] {slotItems[i].name} did not spawn: {e.Message}"
                );
            }
            if ((k + 1) % SpawnPerFrame == 0)
                yield return null;
        }
        spawning = false;
        SandboxActions.Say(
            $"Item room: {placed} items placed"
                + (failed > 0 ? $", {failed} failed (see log)." : ".")
        );
    }

    /// <summary>Removes a placed item only while it lies in the world (<c>Item.itemState</c> Ground, Item.cs:286).</summary>
    private static void Despawn(GameObject go)
    {
        if (
            go == null
            || !go.TryGetComponent<Item>(out var item)
            || item.itemState != ItemState.Ground
        )
            return;
        var view = item.GetComponent<PhotonView>();
        if (view != null && (view.IsMine || PhotonNetwork.IsMasterClient))
            PhotonNetwork.Destroy(go);
    }

    /// <summary>The room's dynamite lights when picked up: its wick moves to where it is now, which the holder is
    /// next to, so the game's own fuse runs from there (Dynamite.cs:56-60, :111-117).</summary>
    private void Rearm()
    {
        for (int i = wicks.Count - 1; i >= 0; i--)
        {
            var dynamite = wicks[i];
            if (dynamite == null)
                wicks.RemoveAt(i);
            else if (dynamite.item != null && dynamite.item.itemState != ItemState.Ground)
            {
                WickRef(dynamite) = dynamite.transform.position;
                wicks.RemoveAt(i);
            }
        }
    }

    // ---- orbs, labels, rings ----

    private void AddOrb(int action, bool cross, Vector3 at)
    {
        var orb = new TouchOrb("PeakXR_ItemRoomOrb", cross, seconds: cross ? 1f : 0.6f);
        orb.Place(at, facing);
        orbs.Add(new KeyValuePair<TouchOrb, int>(orb, action));
    }

    private void TickOrbs(float dt)
    {
        var rig = VRRig.Instance;
        if (rig == null || !VRSession.Active)
            return;
        for (int i = 0; i < orbs.Count; i++)
        {
            var orb = orbs[i].Key;
            if (!orb.Tick(rig, dt))
                continue;
            int action = orbs[i].Value;
            if (action == LeaveRoom)
            {
                CueAudio.Play(CueSound.Notice, orb.Position, 1f);
                Leave();
                return;
            }
            if (Restock(action))
                CueAudio.Play(CueSound.Notice, orb.Position, 1f);
            else
                Refuse(orb.Position, VRInput.LeftHanded, DenyReason.NotNow, "Still placing items.");
        }
    }

    private void Label(
        string text,
        Vector3 at,
        Quaternion rotation,
        float size,
        Color color,
        float scale = 0.002f
    )
    {
        var go = new GameObject("PeakXR_ItemRoomLabel", typeof(RectTransform));
        go.transform.SetParent(root, false);
        go.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        ((RectTransform)go.transform).sizeDelta = new Vector2(900f, 400f);
        go.transform.SetPositionAndRotation(at, rotation);
        go.transform.localScale = Vector3.one * scale;
        var label = go.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = size;
        label.color = color;
        label.alignment = TextAlignmentOptions.Bottom;
        label.raycastTarget = false;
        // The game's prompt font: TMP's default font may not ship with the game.
        var font =
            GUIManager.instance != null && GUIManager.instance.interactName != null
                ? GUIManager.instance.interactName.GetComponentInChildren<TextMeshProUGUI>(true)
                : null;
        if (font != null)
            label.font = font.font;
    }

    private void Ring(Vector3 center, Vector3 normal, float radius, Material material)
    {
        var go = new GameObject("PeakXR_ItemRoomTarget");
        go.transform.SetParent(root, false);
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 32;
        line.loop = true;
        line.startWidth = line.endWidth = 0.03f;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.sharedMaterial = material;
        var a = Vector3.Cross(normal, Vector3.up).normalized;
        var b = Vector3.Cross(normal, a);
        for (int i = 0; i < 32; i++)
        {
            float angle = i * Mathf.PI * 2f / 32f;
            line.SetPosition(i, center + (a * Mathf.Cos(angle) + b * Mathf.Sin(angle)) * radius);
        }
    }

    private Material GroupMaterial(int group)
    {
        var m = groupMaterials[group];
        if (m == null)
        {
            var color = Palette[(int)ShelfLayout.GroupOf(group) % Palette.Length];
            if (ShelfLayout.ZoneOf(group) != ItemZone.Helpful)
                color = Color.Lerp(color, Color.black, 0.5f);
            groupMaterials[group] = m = Own(UIMaterials.Create(color, false, 2450));
        }
        return m;
    }

    private Material Own(Material m)
    {
        owned.Add(m);
        return m;
    }
}
