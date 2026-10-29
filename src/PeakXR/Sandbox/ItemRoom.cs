using System.Collections;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using Peak.Network;
using PeakXR.Core;
using PeakXR.Framework;
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
/// nearest, dangerous ones behind them, and what goes off by itself (dynamite) a gap past them, each by kind
/// (<see cref="ItemShelf"/>). Beside them stands what the items need: a lit cooking fire heading the helpful tables, the
/// sandbox test wall with a target past the last tables (when the floor has room for it), and an eye chart on a board at the far end for the binoculars; the
/// airport's hard floor breaks what breaks. A touch orb on a post at each kind's first table restocks it, three at the aisle's start light the fire, restock
/// everything (chevron) or leave to the title (cross), so the headset alone runs it. Offline only; the progress and
/// save guard (<see cref="SelfTestProgressGuard.Room"/>) holds while it is open. Statuses work in it (the airport
/// otherwise drops them, CharacterAfflictions.cs:741) and a keep-alive clears them before they knock you out.
/// </summary>
internal sealed class ItemRoom : MonoBehaviour
{
    private const float LoadTimeout = 120f;
    private const float ArrivalSettle = 2f;
    private const float LeaveTimeout = 60f;

    /// <summary>Table top surface above the floor: waist high.</summary>
    private const float TableHeight = 0.88f;

    /// <summary>An item's lowest drawn point this far over the top: resting on it, not sunk in or hovering.</summary>
    private const float Rest = 0.004f;
    private const float MaxReach = 40f;
    private const int Headings = 16;

    /// <summary>A plan this much bigger than the best open floor is built anyway (a few tables touch walls); a bigger
    /// one would stand in them, so the room does not open.</summary>
    private const float MaxOverflow = 1.25f;

    /// <summary>Every orb's centre above the floor: hand high, on its own post.</summary>
    private const float OrbHeight = 1.1f;

    /// <summary>The fixed airport place was found blocked this session (logged once).</summary>
    private static bool fixedWarned;

    /// <summary>Best open floor under this many square metres: walled in (closed doors), not a small room.</summary>
    private const float BoxedArea = 4f;

    /// <summary>Seconds a walled-in start keeps measuring again before it refuses.</summary>
    private const float BoxedWait = 30f;

    private const string BoxedIn = "walled in";
    private float boxedUntil;
    private bool boxedSaid;
    private int[] counts;
    private System.Numerics.Vector2[] sizes;

    /// <summary>About 15 x 15 m: open floor this big holds every item of the game at its real size, a hand apart
    /// (ShelfLayoutTests).</summary>
    private const float HallArea = 220f;

    // An item's first spawn this session (asset load, collider cooking, first draw) cost up to ~30 ms on the iGPU
    // (VR run of 3a1b2f9: a 126.8 ms frame on a 15-item table, four a frame); later spawns are cheap. So at most two a
    // frame, and none after the frame's spawning has used its budget.
    private const int SpawnPerFrame = 2;
    private static readonly long SpawnBudget = System.Diagnostics.Stopwatch.Frequency * 2 / 1000;
    private const float KeepAliveEvery = 0.5f;
    private const float KeepAliveSum = 0.85f;
    private const int RestockAll = -1;
    private const int LeaveRoom = -2;
    private const int LightFire = -3;

    // A table's items are out only while you are near it (ShelfLoad); the fire only while you stay by it.
    // 4 m: at 6 m the first tables' lit lanterns, torches and particles all drew at once (VR run of 3a1b2f9: GPU p95
    // 38-47 ms against 13.9 ms while facing them).
    private const float LoadRadius = 4f;
    private const float UnloadRadius = 7f;
    private const float LoadEvery = 0.25f;
    private const float FireKeep = 8f;

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
        "GOES OFF BY ITSELF\n<size=50%>quiet on the table: dynamite lights and peels slip once picked up; the hive has no bees</size>",
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
    private Material legMaterial;
    private Material chartMaterial;
    private readonly List<Dynamite> wicks = new();

    /// <summary>Per item lying on its table, what was switched off until it is picked up (<see cref="Quiet"/>).</summary>
    private readonly List<KeyValuePair<Item, Component[]>> quiet = new();
    private readonly List<Component> quieting = new();
    private int quietLights;
    private int quietParticles;
    private Vector3 roomOrigin;
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

    /// <summary>Each item's drawn box in its own frame (pivot at the origin, unrotated): its pivot is not its
    /// bottom, so its spot and footprint come from this.</summary>
    private Bounds[] slotBounds;

    /// <summary>Items the drawn-box data does not have: their box is their parts' boxes, measured again once spawned
    /// (<see cref="Settle"/>).</summary>
    private bool[] measured;

    /// <summary>Where each item's drawn box rests: the middle of its footprint, <see cref="Rest"/> over its table.</summary>
    private Vector3[] slotAt;
    private GameObject[] slotSpawned;
    private bool[] tableLoaded;
    private Vector3[] tableCenter;
    private float[] tableRadius;
    private float loadTick;
    private Vector3 fireAt;
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
        if (!Affordance.DenyWithText(left, at, reason, why))
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

    /// <summary>The room did not open: everything it placed goes (<see cref="OnDestroy"/>), Enter opens a fresh one at
    /// once, and the player gets the refusal with <paramref name="why"/> in front of them.</summary>
    private void Close(string why)
    {
        Current = null;
        Destroy(gameObject);
        var c = Character.localCharacter;
        var head =
            VRRig.Instance != null && VRRig.Instance.Head != null ? VRRig.Instance.Head
            : c != null ? c.transform
            : null;
        var at = head != null ? head.position + head.forward * 0.6f : Vector3.zero;
        Refuse(at, VRInput.LeftHanded, DenyReason.NotHere, why);
        RoomNotice.Show(why);
    }

    private void OnDestroy()
    {
        SceneManager.activeSceneChanged -= OnScene;
        if (Current == this)
            Current = null;
        // A room opened after this one failed (Close) keeps its guard.
        if (Current == null)
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
                TickLoad(dt);
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
            if (boxedUntil <= 0f)
                boxedUntil = Time.unscaledTime + BoxedWait;
            string refusal;
            try
            {
                refusal = Build(Character.localCharacter);
            }
            catch (System.Exception e)
            {
                PatchGuard.Fail<ItemRoomFeature>(e);
                refusal =
                    "The item room hit an error and closed (details in LogOutput.log). Press Enter to try again.";
            }
            if (ReferenceEquals(refusal, BoxedIn))
            {
                if (!boxedSaid)
                {
                    boxedSaid = true;
                    Plugin.Log.LogInfo(
                        $"[sandbox.room] walled in where you stand (doors closed?): measuring again every second for {BoxedWait:0} s"
                    );
                }
                phase = Phase.Loading;
                settled = ArrivalSettle - 1f;
            }
            else if (refusal != null)
                Close(refusal);
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

    /// <summary>Lays the room out and places its tables; null when it stands, else why it did not open (nothing is
    /// placed then).</summary>
    private string Build(Character c)
    {
        if (InAirportRef != null && c.refs != null && c.refs.afflictions != null)
        {
            statusesOn = c.refs.afflictions;
            InAirportRef(statusesOn) = false;
        }
        // Once: a boxed-in try (BoxedIn) comes back here a second later.
        if (slotItems == null)
        {
            var buckets = Collect();
            counts = new int[buckets.Length];
            int n = 0;
            for (int b = 0; b < buckets.Length; b++)
                n += counts[b] = buckets[b].Count;
            slotItems = new Item[n];
            for (int b = 0, i = 0; b < buckets.Length; b++)
                foreach (var item in buckets[b])
                    slotItems[i++] = item;
            slotBounds = new Bounds[n];
            measured = new bool[n];
            sizes = new System.Numerics.Vector2[n];
            for (int i = 0; i < n; i++)
            {
                var t = slotItems[i].transform;
                Bounds b;
                if (ItemDrawnBox.TryGet(slotItems[i].name, out var lo, out var hi))
                    b = new Bounds(
                        new Vector3(lo.X + hi.X, lo.Y + hi.Y, lo.Z + hi.Z) * 0.5f,
                        new Vector3(hi.X - lo.X, hi.Y - lo.Y, hi.Z - lo.Z)
                    );
                else
                {
                    // An item the data does not have (a game update): its parts' boxes, measured again once spawned.
                    measured[i] = true;
                    if (
                        !Drawn(t, Matrix4x4.TRS(t.position, t.rotation, Vector3.one).inverse, out b)
                    )
                        b = new Bounds(Vector3.zero, Vector3.zero);
                }
                slotBounds[i] = b;
                sizes[i] = new System.Numerics.Vector2(b.size.x, b.size.z);
            }
        }
        int total = slotItems.Length;
        plan = null;

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
        // Dynamite lights within its radius of where it lies, unless its wick is moved away (WickRef): only then is it
        // kept that far from you and everything else as well.
        var spec = new ShelfSpec { VolatileClearance = WickRef == null ? igniteRadius + 1f : 0f };
        var origin = feet;
        var forward = look;
        // The solo airport: always its fixed place (AirportRoom), wherever you pressed Enter, unless level geometry
        // now stands in it. Elsewhere, or then, the open floor measured around you.
        bool fixedPlace = FixedPlace(spec, out var fixedOrigin);
        if (fixedPlace)
        {
            room = AirportRoom.Bounds;
            origin = fixedOrigin;
            forward = Vector3.forward;
        }
        for (int i = 0; i < Headings && !fixedPlace; i++)
            Consider(feet, Quaternion.Euler(0f, i * 360f / Headings, 0f) * look);
        // Too tight where you stand (the spawn elevator opens onto the airport hall): spots out along each open
        // heading, nearest first, the room continuing that way.
        for (float d = 2f; d <= MaxReach && !plan.Fits; d *= 2f)
        for (int i = 0; i < Headings && !plan.Fits; i++)
        {
            var dir = Quaternion.Euler(0f, i * 360f / Headings, 0f) * look;
            if (Open(feet, dir, Vector3.Cross(Vector3.up, dir), 0.3f, 0.3f) < d + 1f)
                continue;
            var spot = Floor(feet + dir * d, float.NaN);
            if (Mathf.Abs(spot.y - feet.y) < 1f)
                Consider(spot, dir);
        }

        void Consider(Vector3 from, Vector3 dir)
        {
            var rect = Measure(from, dir);
            var candidate = ShelfLayout.Plan(counts, sizes, rect, spec);
            if (
                plan == null
                || (candidate.Fits && !plan.Fits)
                || (candidate.Fits == plan.Fits && candidate.Over < plan.Over - 0.01f)
            )
            {
                plan = candidate;
                room = rect;
                origin = from;
                forward = dir;
            }
        }

        if (!plan.Fits && !(plan.Over <= MaxOverflow))
        {
            // Walled in on every side and no way out (the spawn elevator before its doors open, VR run of dc78fa3:
            // 2.7 x 0.6 m, while 1.5 m out at its door the hall was found): try again in a moment.
            if (room.Width * room.Depth < BoxedArea && Time.unscaledTime < boxedUntil)
                return BoxedIn;
            Plugin.Log.LogWarning(
                $"[sandbox.room] no open floor within {MaxReach:0} m holds the tables: best {room} "
                    + (
                        plan.Over < float.MaxValue
                            ? $"({plan.Over:0.0}x too small)"
                            : "(no open floor found)"
                    )
                    + "; not opened"
            );
            if (room.Width * room.Depth < BoxedArea)
                return "Item room: walled in here, with no open floor in sight. Step out and press Enter again.";
            // Only send the player on where it can work: a hall-sized floor holds every item (ShelfLayout tests).
            bool hall =
                room.Width >= 13f && room.Depth >= 13f && room.Width * room.Depth >= HallArea;
            return !hall
                ? $"Item room: the most open floor within {MaxReach:0} m is only {room.Width:0} x {room.Depth:0} m. "
                    + "It needs about 15 x 15 m: go to a big open hall and press Enter again."
                : $"Item room: {total} items do not fit even {room.Width:0} x {room.Depth:0} m of open floor. "
                    + "Nothing to do here; the log has the details.";
        }

        var right = Vector3.Cross(Vector3.up, forward);
        facing = Quaternion.LookRotation(forward);
        Vector3 At(float x, float z) => origin + right * x + forward * z;
        roomOrigin = origin;

        slotAt = new Vector3[total];
        slotSpawned = new GameObject[total];
        root = new GameObject("PeakXR_ItemRoomProps").transform;
        Tables(At, counts);
        var start = At(plan.StartX, 1f);
        start = Floor(start, start.y);
        Entrance = start;
        StartOrbs(start);
        Props(origin, At, forward);
        var unlit = RoomProps.Unlit(root);
        if (unlit != null)
            Plugin.Log.LogWarning($"[sandbox.room] a table is not drawn lit: {unlit}");
        // Warm-up while you stand at the start: every table once, nearest first, a spawn or two a frame. Each item's
        // costly first spawn happens here, spread out; far tables go again on the next load tick (ShelfLoad), and
        // coming back to them later is cheap (VR run of 3a1b2f9: the second pass held 72 fps).
        var all = new List<int>(total);
        for (int t = 0; t < plan.Tables.Length; t++)
        {
            tableLoaded[t] = true;
            for (int k = plan.Tables[t].First; k < plan.Tables[t].First + plan.Tables[t].Count; k++)
                all.Add(k);
        }
        StartCoroutine(Spawn(all));
        float away = Flat(origin - feet).magnitude;
        if (plan.Fits)
            Plugin.Log.LogInfo(
                $"[sandbox.room] {total} items on {plan.Tables.Length} tables, block "
                    + $"{plan.Block} in {(fixedPlace ? "the fixed airport place" : "measured room")} {room} "
                    + $"along {forward}, {away:0.0} m from you, wall "
                    + $"{(plan.HasWall ? "placed" : "left out")}"
            );
        else
        {
            Plugin.Log.LogWarning(
                $"[sandbox.room] the open floor here ({room}) is too small for the tables ({plan.Block}): "
                    + "some may stand in the walls"
            );
            SandboxActions.Say(
                "Item room: the open floor here is a little small, so some tables may touch walls."
            );
        }

        var head =
            VRRig.Instance != null && VRRig.Instance.Head != null
                ? VRRig.Instance.Head.position
                : center;
        // Built out in the hall: the bell rings from there and its spot is marked.
        CueAudio.Play(CueSound.Notice, away > 1f ? start : head + forward * 0.6f, 1f);
        if (away > 1f || !plan.Fits)
        {
            var note = plan.Fits
                ? $"Item room set up {away:0} m away: follow the bell to the marked spot"
                : "Item room: not quite enough open floor here, some tables touch walls";
            Affordance.Inform(
                VRInput.LeftHanded,
                head + forward * 0.6f,
                DenyReason.HappenedHere,
                DenyHint.At(start),
                note,
                forceText: true
            );
            RoomNotice.Show(note);
        }
        else
            RoomNotice.Show(
                $"Item room open: {total} items on {plan.Tables.Length} tables ahead. "
                    + "The orbs in front of you light the fire, restock and leave."
            );
        return null;
    }

    /// <summary><c>Sandbox.RoomTable</c>: puts you in the walkway in front of the first table whose area and kind
    /// ("Dangerous Food", "Goes off", "Tools") contain <paramref name="kind"/>. Null when done, else why not.</summary>
    public string WarpTo(string kind)
    {
        var c = Character.localCharacter;
        if (phase != Phase.Open || plan == null || c == null)
            return "The item room is not open.";
        kind ??= "";
        for (int t = 0; t < plan.Tables.Length; t++)
        {
            var table = plan.Tables[t];
            var label =
                (table.Zone == ItemZone.Volatile ? "Goes off by itself" : table.Zone.ToString())
                + " "
                + ItemShelf.Title(ShelfLayout.GroupOf(table.Group));
            if (label.IndexOf(kind, System.StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            var f = table.Footprint;
            var spot = roomOrigin + facing * new Vector3(f.CenterX, 0f, f.MinZ - 0.8f);
            spot.y = tableCenter[t].y + 0.1f;
            c.photonView.RPC("WarpPlayerRPC", RpcTarget.All, spot, false);
            return null;
        }
        return $"No item room table matches \"{kind}\".";
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
        var placed = new List<Item>();
        var harms = new List<string>();
        var prefabNames = new List<string>();
        var keys = new List<string>();
        var bucketOf = new List<int>();
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
            var goesOff = ItemShelf.GoesOff(names);
            var zone = ItemShelf.ZoneOf(harm, ignite, goesOff);
            if (zone == ItemZone.Volatile)
                igniteRadius = Mathf.Max(igniteRadius, ignite);
            harms.Add(
                zone == ItemZone.Helpful ? null
                : ignite > 0f ? $"lights within {ignite:0.#} m"
                : goesOff ?? harm
            );
            placed.Add(item);
            prefabNames.Add(item.name);
            // One per display name (Item.UIData.itemName, Item.cs:41) and zone: chess colours and guidebook pages
            // are separate prefabs of one item; a poison look-alike keeps its own spot.
            var shown = item.UIData != null ? item.UIData.itemName : null;
            keys.Add($"{(string.IsNullOrEmpty(shown) ? item.name : shown)}|{zone}");
            bucketOf.Add(ShelfLayout.Bucket(zone, ItemShelf.GroupOf(item.name, names)));
        }
        var keep = ItemShelf.Distinct(prefabNames, keys);
        var duplicates = new StringBuilder();
        int kinds = 0;
        for (int i = 0; i < placed.Count; i++)
        {
            if (keep[i])
            {
                kinds++;
                buckets[bucketOf[i]].Add(placed[i]);
                // Only what is on the tables: a left-out duplicate is never listed as dangerous as well.
                if (harms[i] != null)
                    dangerous.Append(prefabNames[i]).Append(" (").Append(harms[i]).Append("); ");
            }
            else
                duplicates.Append(prefabNames[i]).Append("; ");
        }
        if (skips > 0)
            Plugin.Log.LogInfo($"[sandbox.room] {skips} items left out: {skipped}");
        Plugin.Log.LogInfo(
            $"[sandbox.room] one of each: {kinds} kinds from {placed.Count} items; "
                + $"duplicates left out: {duplicates}"
        );
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

    /// <summary>Level geometry in the waist-high box over footprint <paramref name="f"/> standing on
    /// <paramref name="floor"/>, or null.</summary>
    private static Collider Blocker(RoomRect f, Vector3 floor, Quaternion rotation)
    {
        var hits = Physics.OverlapBox(
            floor + Vector3.up * (TableHeight * 0.5f + 0.1f),
            new Vector3(f.Width * 0.5f, TableHeight * 0.5f, f.Depth * 0.5f),
            rotation,
            HelperFunctions.terrainMapMask | LayerMask.GetMask("Default"),
            QueryTriggerInteraction.Ignore
        );
        return hits.Length > 0 ? hits[0] : null;
    }

    /// <summary>Lays the room out in its fixed airport place (<see cref="AirportRoom"/>) and checks every table, the
    /// fire and the test wall against level geometry first. True with <see cref="plan"/> set when it is used; a
    /// blocked place (a game update moved the lobby) is logged once a session and the measured layout takes over.</summary>
    private bool FixedPlace(ShelfSpec spec, out Vector3 at)
    {
        at = Floor(
            new Vector3(AirportRoom.OriginX, AirportRoom.FloorY, AirportRoom.OriginZ),
            AirportRoom.FloorY
        );
        if (!inAirport)
            return false;
        var candidate = ShelfLayout.Plan(counts, sizes, AirportRoom.Bounds, spec);
        int blocked = 0;
        string first = null;
        var origin = at;
        void Check(RoomRect f)
        {
            var c = Blocker(
                f,
                Floor(origin + new Vector3(f.CenterX, 0f, f.CenterZ), origin.y),
                Quaternion.identity
            );
            if (c == null)
                return;
            blocked++;
            first ??= $"{c.name} at {f}";
        }
        if (candidate.Fits)
        {
            foreach (var t in candidate.Tables)
                Check(t.Footprint);
            Check(candidate.Fire);
            if (candidate.HasWall)
                Check(candidate.Wall);
        }
        if (AirportRoom.Choose(inAirport, candidate.Fits, blocked) == RoomPlace.Fixed)
        {
            plan = candidate;
            return true;
        }
        if (!fixedWarned)
        {
            fixedWarned = true;
            Plugin.Log.LogWarning(
                $"[sandbox.room] the fixed airport place {(candidate.Fits ? $"is blocked ({blocked}, first {first})" : "does not hold the tables")}: a game update may have moved the lobby; measuring the open floor instead"
            );
        }
        return false;
    }

    /// <summary>A lit table in its group colour (<see cref="RoomProps.Table"/>) under each table's items, sized by
    /// the plan to its items' footprints; at each bucket's first table a restock orb on a post at its front left
    /// corner, labelled; a sign over each zone. A table that still meets level geometry is logged. Each item's spot is
    /// where its drawn box rests: the middle of its footprint, <see cref="Rest"/> over the top. The items' pivots
    /// used to sit 6.5 cm over the old top: anything reaching further below its pivot sank into the table.</summary>
    private void Tables(System.Func<float, float, Vector3> at, int[] counts)
    {
        int lastGroup = -1;
        int table = 0;
        tableLoaded = new bool[plan.Tables.Length];
        tableCenter = new Vector3[plan.Tables.Length];
        tableRadius = new float[plan.Tables.Length];
        TableRoots = new Transform[plan.Tables.Length];
        foreach (var t in plan.Tables)
        {
            var f = t.Footprint;
            var floor = Floor(at(f.CenterX, f.CenterZ), at(0f, 0f).y);
            tableCenter[table] = floor;
            tableRadius[table] = 0.5f * Mathf.Sqrt(f.Width * f.Width + f.Depth * f.Depth);
            for (int k = t.First; k < t.First + t.Count; k++)
            {
                var s = plan.Slots[k];
                var spot = at(s.X, s.Z);
                slotAt[k] = new Vector3(spot.x, floor.y + TableHeight + Rest, spot.z);
            }
            var blocker = Blocker(f, floor, facing);
            if (blocker != null)
                Plugin.Log.LogWarning(
                    $"[sandbox.room] {ItemShelf.Title(ShelfLayout.GroupOf(t.Group))} table ({f}) meets {blocker.name}"
                );
            TableRoots[table++] = RoomProps.Table(
                root,
                floor,
                facing,
                f.Width,
                f.Depth,
                TableHeight,
                GroupMaterial(t.Group),
                LegMaterial
            );
            if (t.Group == lastGroup)
                continue;
            lastGroup = t.Group;
            // In the walkway at the table's front left corner, on a post: a button standing by its table.
            AddOrb(
                t.Group,
                cross: false,
                Floor(at(f.MinX + 0.15f, f.MinZ - 0.2f), floor.y),
                $"{ItemShelf.Title(ShelfLayout.GroupOf(t.Group))} ({counts[t.Group]})\n<size=55%>rest a hand in the orb: restock</size>",
                48f,
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

    /// <summary>On posts in a row 1 m ahead of where you start, 0.44 m apart: Light fire, Restock all, Leave. A hand
    /// within 9 cm of an orb's centre fills it (<see cref="TouchOrb"/>): 0.6 s fires a chevron, 1 s the cross. Every
    /// orb's spot is logged for testers.</summary>
    private void StartOrbs(Vector3 floorAt)
    {
        var right = facing * Vector3.right;
        AddOrb(RestockAll, cross: false, floorAt - right * 0.22f, "Restock all", 40f, Color.white);
        AddOrb(LeaveRoom, cross: true, floorAt + right * 0.22f, "Leave", 40f, Color.white);
        // Beside the other two, where you start: at the fire's own spot it was out of sight (VR run of 3a1b2f9).
        if (firePrefab != null)
            AddOrb(
                LightFire,
                cross: false,
                floorAt - right * 0.66f,
                "Light fire",
                40f,
                Color.white
            );
        var spots = new StringBuilder();
        foreach (var mark in Marks)
            spots.Append(mark.Label.text).Append(' ').Append(mark.Orb.Position).Append("; ");
        Plugin.Log.LogInfo(
            System.FormattableString.Invariant(
                $"[sandbox.room] orbs {OrbHeight} m above the floor: {spots}rest a hand in one (within 9 cm) for 0.6 s, Leave 1 s"
            )
        );
    }

    /// <summary>The fire, the test wall with its target, and the eye chart, where the plan put them.</summary>
    private void Props(Vector3 feet, System.Func<float, float, Vector3> at, Vector3 forward)
    {
        if (firePrefab == null)
            Plugin.Log.LogInfo(
                "[sandbox.room] no portable fire in the item database: cook and warm hands in a run"
            );
        else
        {
            // Unlit until asked for: a lit campfire's particles, light and fog switch cost frames all the time.
            fireAt = Floor(at(plan.Fire.CenterX, plan.Fire.CenterZ), feet.y);
            Label("Fire", fireAt + Vector3.up * 1.3f, facing, 40f, Color.white);
        }

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
                // On the wall itself: they go with it if anything moves or removes the wall.
                for (int i = 1; i <= 3; i++)
                    Ring(
                        wall.transform,
                        new Vector3(face.x, targetY, face.z),
                        -forward,
                        0.22f * i,
                        targetMat
                    );
            }
        }
        else
            Plugin.Log.LogInfo("[sandbox.room] no room for the test wall here");

        // An eye chart on a white board on two posts at the far end, across from the test wall, named over it: only
        // the top lines read without the binoculars. Drawn as bare text in the open hall, its big E read as a stray
        // floating letter (tester), and its bottom-aligned box put the small lines under the floor.
        var chartAt = Floor(
            at(Mathf.Max(room.CenterX, room.MaxX - 1.5f), room.MaxZ - 0.3f),
            feet.y
        );
        var chart = new GameObject("PeakXR_ItemRoomEyeChart").transform;
        chart.SetParent(root, false);
        chart.SetPositionAndRotation(chartAt, facing);
        if (chartMaterial == null)
            chartMaterial = Own(RoomProps.Lit(new Color(0.92f, 0.92f, 0.9f)));
        RoomProps.Box(
            chart,
            "PeakXR_ItemRoomEyeChartBoard",
            new Vector3(0f, 1.6f, 0f),
            new Vector3(0.75f, 1f, 0.03f),
            chartMaterial
        );
        for (int side = -1; side <= 1; side += 2)
            RoomProps.Box(
                chart,
                "PeakXR_ItemRoomEyeChartPost",
                new Vector3(side * 0.3f, 0.55f, 0.04f),
                new Vector3(0.05f, 1.1f, 0.05f),
                LegMaterial
            );
        // 1 mm a unit: the E is 20 cm tall, the last line 1.6 cm.
        var lines = Label(
            "<size=200>E</size>\n<size=110>F P</size>\n<size=70>T O Z</size>\n<size=45>L P E D</size>\n"
                + "<size=28>P E C F D</size>\n<size=16>E D F C Z P</size>",
            chartAt + Vector3.up * 1.15f - forward * 0.02f,
            facing,
            100f,
            new Color(0.08f, 0.08f, 0.08f),
            0.001f
        );
        ((RectTransform)lines.transform).sizeDelta = new Vector2(700f, 900f);
        lines.alignment = TextAlignmentOptions.Top;
        Label(
            "EYE CHART\n<size=55%>read the small lines through the binoculars</size>",
            chartAt + Vector3.up * 2.15f - forward * 0.02f,
            facing,
            48f,
            Color.white
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

    /// <summary>Puts the fire out by removing it, once you have walked away from it.</summary>
    private void PutOut()
    {
        if (fakeMountain >= 0f)
            Shader.SetGlobalFloat("FakeMountainEnabled", fakeMountain);
        fakeMountain = -1f;
        if (fireRoot.TryGetComponent<PhotonView>(out var view) && view.IsMine)
            PhotonNetwork.Destroy(fireRoot);
        else
            Destroy(fireRoot);
        fireRoot = null;
        fire = null;
        Plugin.Log.LogInfo("[sandbox.room] fire put out: you walked away");
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

    /// <summary>Spawns the tables you come near and removes the untouched items of those you leave
    /// (<see cref="ShelfLoad"/>), a table per tick; puts the fire out once you are <see cref="FireKeep"/> m away.</summary>
    private void TickLoad(float dt)
    {
        loadTick -= dt;
        if (loadTick > 0f || tableLoaded == null)
            return;
        loadTick = LoadEvery;
        var c = Character.localCharacter;
        if (c == null)
            return;
        var p = c.Center;
        if (fireRoot != null && Flat(p - fireAt).magnitude > FireKeep)
            PutOut();
        if (spawning)
            return;
        for (int t = 0; t < tableLoaded.Length; t++)
        {
            float d = Flat(p - tableCenter[t]).magnitude - tableRadius[t];
            bool want =
                KeepAllLoaded || ShelfLoad.Wanted(tableLoaded[t], d, LoadRadius, UnloadRadius);
            if (want == tableLoaded[t])
                continue;
            tableLoaded[t] = want;
            var table = plan.Tables[t];
            if (want)
            {
                StartCoroutine(Spawn(Missing(table.First, table.First + table.Count)));
                return;
            }
            for (int i = table.First; i < table.First + table.Count; i++)
                if (!Untouched(slotSpawned[i]) || Despawn(slotSpawned[i]))
                    slotSpawned[i] = null;
        }
    }

    private static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);

    /// <summary>Slots from <paramref name="from"/> to <paramref name="to"/> - 1 with nothing lying untouched on them;
    /// an item that left its slot is the player's now and is let go.</summary>
    private List<int> Missing(int from, int to)
    {
        var list = new List<int>(to - from);
        for (int i = from; i < to; i++)
        {
            if (Untouched(slotSpawned[i]))
                continue;
            slotSpawned[i] = null;
            list.Add(i);
        }
        return list;
    }

    /// <summary>Still lying where the room put it: on the ground and kinematic, which picking it up clears
    /// (<c>Item.SetState</c>, Item.cs:734-760).</summary>
    private static bool Untouched(GameObject go) =>
        go != null
        && go.TryGetComponent<Item>(out var item)
        && item.itemState == ItemState.Ground
        && item.rig != null
        && item.rig.isKinematic;

    /// <summary>Puts a table's items (or all, <see cref="RestockAll"/>) back: items still on their spot stay, ones
    /// lying anywhere else in the world are removed and fresh ones placed on the tables you are near. Held or packed
    /// ones stay yours. Null when it started, else why not; an error is caught here, at the room's boundary.</summary>
    public string Restock(int group)
    {
        if (phase != Phase.Open || plan == null)
            return "The item room is not open.";
        if (spawning)
            return "Still placing items.";
        try
        {
            RestockTables(group);
            return null;
        }
        catch (System.Exception e)
        {
            PatchGuard.Fail<ItemRoomFeature>(e);
            return "Restock hit an error (details in LogOutput.log).";
        }
    }

    private void RestockTables(int group)
    {
        var redo = new List<int>();
        for (int t = 0; t < plan.Tables.Length; t++)
        {
            var table = plan.Tables[t];
            if (group != RestockAll && table.Group != group)
                continue;
            for (int i = table.First; i < table.First + table.Count; i++)
            {
                if (Untouched(slotSpawned[i]))
                    continue;
                Despawn(slotSpawned[i]);
                slotSpawned[i] = null;
                if (tableLoaded[t])
                    redo.Add(i);
            }
        }
        StartCoroutine(Spawn(redo));
    }

    /// <summary>One or two a frame (<see cref="SpawnBudget"/>), held still where they belong (kinematic until picked up:
    /// <c>Item.SetState</c> clears it, Item.cs:734-760). <c>PhotonNetwork.InstantiateItemRoom</c> is the game's own
    /// spawner call (GameUtils.cs:270). The slowest spawn is logged, to find what a hitch came from.</summary>
    private IEnumerator Spawn(List<int> slots)
    {
        spawning = true;
        int placed = 0,
            failed = 0,
            thisFrame = 0,
            slowest = -1;
        long frameStart = System.Diagnostics.Stopwatch.GetTimestamp(),
            slowestTicks = 0;
        // finally: a spawn that dies mid-way must not leave every later restock "still placing".
        try
        {
            for (int k = 0; k < slots.Count && phase == Phase.Open; k++)
            {
                int i = slots[k];
                long before = System.Diagnostics.Stopwatch.GetTimestamp();
                try
                {
                    int what = plan.Slots[i].Item;
                    var drawn = slotBounds[what];
                    var turn = Turn(i);
                    var go = PhotonNetwork.InstantiateItemRoom(
                        slotItems[what].gameObject.name,
                        slotAt[i] - turn * new Vector3(drawn.center.x, drawn.min.y, drawn.center.z),
                        turn
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
                    // Before its Start (Beehive.cs:26-55), which would release a swarm that chases and poisons you.
                    var hive = go != null ? go.GetComponentInChildren<Beehive>(true) : null;
                    if (hive != null)
                        hive.spawnBees = false;
                    if (go != null)
                    {
                        Quiet(go);
                        if (measured[plan.Slots[i].Item])
                            Settle(go, slotAt[i]);
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
                        $"[sandbox.room] {slotItems[plan.Slots[i].Item].name} did not spawn: {e.Message}"
                    );
                }
                long now = System.Diagnostics.Stopwatch.GetTimestamp();
                if (now - before > slowestTicks)
                {
                    slowestTicks = now - before;
                    slowest = i;
                }
                if (++thisFrame >= SpawnPerFrame || now - frameStart > SpawnBudget)
                {
                    yield return null;
                    thisFrame = 0;
                    frameStart = System.Diagnostics.Stopwatch.GetTimestamp();
                }
            }
        }
        finally
        {
            spawning = false;
        }
        Plugin.Log.LogDebug(
            $"[sandbox.room] {placed} items placed, {failed} failed; slowest "
                + $"{slowestTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency:0.0} ms"
                + (slowest >= 0 ? $" ({slotItems[plan.Slots[slowest].Item].name})" : "")
                + $"; held off on the tables so far: {quietLights} lights, {quietParticles} particle systems"
        );
        if (failed > 0)
            SandboxActions.Say($"Item room: {failed} items did not spawn (see log).");
    }

    /// <summary>Removes a placed item only while it lies in the world (<c>Item.itemState</c> Ground, Item.cs:286).
    /// Never a warp item: its <c>WarpOnThrow.OnDisable</c> stops a trail that may be gone and throws (WarpOnThrow.cs:33-38),
    /// so it stays where it lies. False when the item stays.</summary>
    private static bool Despawn(GameObject go)
    {
        if (
            go == null
            || !go.TryGetComponent<Item>(out var item)
            || item.itemState != ItemState.Ground
            || go.GetComponentInChildren<Peak.WarpOnThrow>(true) != null
        )
            return false;
        var view = item.GetComponent<PhotonView>();
        if (view == null || !(view.IsMine || PhotonNetwork.IsMasterClient))
            return false;
        PhotonNetwork.Destroy(go);
        return true;
    }

    /// <summary>The room's dynamite lights when picked up: its wick moves to where it is now, which the holder is
    /// next to, so the game's own fuse runs from there (Dynamite.cs:56-60, :111-117); a banana peel slips again.</summary>
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
        for (int i = quiet.Count - 1; i >= 0; i--)
        {
            var item = quiet[i].Key;
            if (item == null)
                quiet.RemoveAt(i);
            else if (item.itemState != ItemState.Ground)
            {
                foreach (var c in quiet[i].Value)
                    if (c is Behaviour b && b != null)
                        b.enabled = true;
                    else if (c is Renderer r && r != null)
                        r.enabled = true;
                quiet.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Switches off, until the item is picked up (<see cref="Rearm"/>), what costs or hurts while it only lies on its
    /// table: its real-time lights and particle renderers (each light draws the scene again for both eyes), and a
    /// banana peel, whose Update slips anyone moving within 1 m (BananaPeel.cs:17-27). Item scripts switch their
    /// light's object and play their particles (Lantern.cs:120-145, Candle.cs:89-110), never these components, and
    /// a lantern lying kinematic skips even that (:122), so nothing turns them back on early.
    /// </summary>
    private void Quiet(GameObject go)
    {
        if (!go.TryGetComponent<Item>(out var item))
            return;
        quieting.Clear();
        foreach (var light in go.GetComponentsInChildren<Light>(false))
            if (light.enabled)
            {
                light.enabled = false;
                quieting.Add(light);
                quietLights++;
            }
        foreach (var particles in go.GetComponentsInChildren<ParticleSystemRenderer>(false))
            if (particles.enabled)
            {
                particles.enabled = false;
                quieting.Add(particles);
                quietParticles++;
            }
        var peel = go.GetComponentInChildren<BananaPeel>(true);
        if (peel != null && peel.enabled)
        {
            peel.enabled = false;
            quieting.Add(peel);
        }
        if (quieting.Count > 0)
            quiet.Add(new KeyValuePair<Item, Component[]>(item, quieting.ToArray()));
    }

    // ---- orbs, labels, rings ----

    /// <summary>An orb <see cref="OrbHeight"/> over <paramref name="floor"/> on a post, named just over it, so it
    /// reads as a button where it stands. Floating at assorted heights with each label drawn under it, testers took
    /// the orbs for stray circles.</summary>
    private void AddOrb(
        int action,
        bool cross,
        Vector3 floor,
        string label,
        float size,
        Color color
    )
    {
        var orb = new TouchOrb("PeakXR_ItemRoomOrb", cross, seconds: cross ? 1f : 0.6f);
        var at = floor + Vector3.up * OrbHeight;
        orb.Place(at, facing);
        orbs.Add(new KeyValuePair<TouchOrb, int>(orb, action));
        float post = OrbHeight - TouchOrb.Radius;
        var stand = RoomProps.Box(
            root,
            "PeakXR_ItemRoomOrbPost",
            floor + Vector3.up * (post * 0.5f),
            new Vector3(0.04f, post, 0.04f),
            LegMaterial
        );
        Marks.Add(
            (
                orb,
                stand,
                Label(label, at + Vector3.up * (TouchOrb.Radius + 0.03f), facing, size, color)
            )
        );
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
            if (action == LightFire)
            {
                if (fireRoot == null)
                    PlaceFire(fireAt);
                else
                    Relight();
                CueAudio.Play(CueSound.Notice, orb.Position, 1f);
                continue;
            }
            if (action == LeaveRoom)
            {
                CueAudio.Play(CueSound.Notice, orb.Position, 1f);
                Leave();
                return;
            }
            var why = Restock(action);
            if (why == null)
                CueAudio.Play(CueSound.Notice, orb.Position, 1f);
            else
                Refuse(orb.Position, VRInput.LeftHanded, DenyReason.NotNow, why);
        }
    }

    /// <summary>World text whose bottom line sits on <paramref name="at"/>.</summary>
    private TextMeshProUGUI Label(
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
        var rect = (RectTransform)go.transform;
        rect.sizeDelta = new Vector2(900f, 400f);
        // Bottom-aligned text in a box centred on its spot hung 0.4 m under it: every orb's label sat below its orb.
        rect.pivot = new Vector2(0.5f, 0f);
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
        return label;
    }

    private static void Ring(
        Transform parent,
        Vector3 center,
        Vector3 normal,
        float radius,
        Material material
    )
    {
        var go = new GameObject("PeakXR_ItemRoomTarget");
        go.transform.SetParent(parent, false);
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
            groupMaterials[group] = m = Own(RoomProps.Lit(color));
        }
        return m;
    }

    /// <summary>Legs and posts: a mid warm grey, readable on the hall's floor in the tops' shadow. Near-black (0.18)
    /// legs did not read at all.</summary>
    private Material LegMaterial =>
        legMaterial != null
            ? legMaterial
            : legMaterial = Own(RoomProps.Lit(new Color(0.42f, 0.36f, 0.3f)));

    private Material Own(Material m)
    {
        owned.Add(m);
        return m;
    }

    private static readonly List<Renderer> drawnScratch = new();

    /// <summary>The box around every mesh drawn under <paramref name="root"/> (enabled mesh and skinned renderers
    /// on active objects; particles, lines and trails left out), in the frame <paramref name="toFrame"/> maps the
    /// world into. False when nothing under it draws a mesh.</summary>
    internal static bool Drawn(Transform root, Matrix4x4 toFrame, out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        root.GetComponentsInChildren(true, drawnScratch);
        foreach (var r in drawnScratch)
        {
            if (!r.enabled || !ActiveUnder(r.transform, root))
                continue;
            var mesh =
                r is SkinnedMeshRenderer skinned ? skinned.sharedMesh
                : r is MeshRenderer && r.TryGetComponent<MeshFilter>(out var filter)
                    ? filter.sharedMesh
                : null;
            if (mesh == null)
                continue;
            var m = toFrame * r.localToWorldMatrix;
            var b = mesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var corner = m.MultiplyPoint3x4(
                    b.center
                        + Vector3.Scale(
                            b.extents,
                            new Vector3(
                                (i & 1) == 0 ? -1f : 1f,
                                (i & 2) == 0 ? -1f : 1f,
                                (i & 4) == 0 ? -1f : 1f
                            )
                        )
                );
                if (any)
                    bounds.Encapsulate(corner);
                else
                    bounds = new Bounds(corner, Vector3.zero);
                any = true;
            }
        }
        drawnScratch.Clear();
        return any;
    }

    /// <summary>Active up to <paramref name="root"/>: a prefab asset's own objects are never active in a scene.</summary>
    private static bool ActiveUnder(Transform t, Transform root)
    {
        for (; t != null; t = t.parent)
        {
            if (!t.gameObject.activeSelf)
                return false;
            if (t == root)
                break;
        }
        return true;
    }

    /// <summary>How slot <paramref name="slot"/>'s item lies: facing the room, or a quarter turn round when the plan
    /// laid its long side across the row (<see cref="ShelfSlot.Turned"/>).</summary>
    private Quaternion Turn(int slot) =>
        plan.Slots[slot].Turned ? facing * Quaternion.Euler(0f, 90f, 0f) : facing;

    /// <summary>Slot <paramref name="slot"/>'s item as it lies now, in the room's frame (rotated to it, world origin):
    /// its drawn-box data carried by its pose, or for an item the data lacks its parts' boxes. False when nothing is
    /// there.</summary>
    internal bool BoxOf(int slot, out Bounds box)
    {
        box = default;
        var go = slotSpawned[slot];
        if (go == null)
            return false;
        var frame = Matrix4x4.Rotate(Quaternion.Inverse(facing));
        int what = plan.Slots[slot].Item;
        if (measured[what])
            return Drawn(go.transform, frame, out box);
        var t = go.transform;
        var m = frame * Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
        var b = slotBounds[what];
        for (int i = 0; i < 8; i++)
        {
            var corner = m.MultiplyPoint3x4(
                b.center
                    + Vector3.Scale(
                        b.extents,
                        new Vector3(
                            (i & 1) == 0 ? -1f : 1f,
                            (i & 2) == 0 ? -1f : 1f,
                            (i & 4) == 0 ? -1f : 1f
                        )
                    )
            );
            if (i == 0)
                box = new Bounds(corner, Vector3.zero);
            else
                box.Encapsulate(corner);
        }
        return true;
    }

    /// <summary>Moves a just spawned item so its drawn footprint is centred on <paramref name="spot"/> and its
    /// lowest drawn point lies on it, measured on the item as spawned (its own Awake may change what it draws).</summary>
    private void Settle(GameObject go, Vector3 spot)
    {
        if (!Drawn(go.transform, Matrix4x4.TRS(spot, facing, Vector3.one).inverse, out var b))
            return;
        go.transform.position += facing * new Vector3(-b.center.x, -b.min.y, -b.center.z);
        if (go.TryGetComponent<Rigidbody>(out var body))
            body.position = go.transform.position;
    }

    // ---- read by the self-test (01c-item-room-layout) ----

    internal ShelfPlan Layout => plan;

    /// <summary>Each plan table's object: its top and legs are its children.</summary>
    internal Transform[] TableRoots { get; private set; }

    internal IReadOnlyList<GameObject> Placed => slotSpawned;

    /// <summary>Still building or placing items.</summary>
    internal bool Placing => phase != Phase.Open || plan == null || spawning;

    internal Quaternion Facing => facing;

    /// <summary>Where the start orbs stand: the room's entrance.</summary>
    internal Vector3 Entrance { get; private set; }

    /// <summary>Table <paramref name="table"/>'s top surface height (world y).</summary>
    internal float TopOf(int table) => tableCenter[table].y + TableHeight;

    /// <summary>Every orb with its post and its label.</summary>
    internal readonly List<(TouchOrb Orb, MeshRenderer Post, TextMeshProUGUI Label)> Marks = new();

    /// <summary>Every table's items stay out wherever you stand, so all can be checked at once.</summary>
    internal bool KeepAllLoaded;
}
