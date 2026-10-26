using System.Collections.Generic;
using Peak.Network;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Interact;
using PeakXR.Inventory;
using PeakXR.Sandbox;
using PeakXR.UI.Affordances;
using PeakXR.UI.Common;
using PeakXR.VR;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PeakXR.UI.Guide;

/// <summary>
/// The VR practice: a few stations in the offline solo airport, each teaching one VR mechanic, grouped in zones
/// (<see cref="PracticeZones"/>): the basics where you start, then one short walk to each further zone, the climb
/// zone at the airport's climbing wall and its rope (<see cref="PracticeLayout"/>). Between zones a light beam and
/// the breathing double ring on the floor mark where to walk, with a cue sound from there. A station shows its
/// ghost demo at its zone, marks where to act with the "works here" double ring, waits for the same game state the
/// self-test reads, then confirms with the rising note, a pulse on both controllers and a widening ring. Two touch
/// orbs on the off-hand side skip the station or the walk (chevron) or leave (cross); the VR tab's Practice row
/// stops it too. It never runs in an online room and always ends back at the title through the game's own leave path.
/// </summary>
internal sealed class PracticeRun : MonoBehaviour
{
    private const float LoadTimeout = 120f;
    private const float ArrivalSettle = 2f;
    private const float TurnGoal = 25f;
    private const float ClimbHold = 0.3f;

    /// <summary>The Refused drill's apple: past vanilla's 2 m interact reach (Interaction.distance, Interaction.cs:7).</summary>
    private const float FarApple = 2.6f;
    private const float ReplaceDistance = 1.5f;
    private const float ReplaceAngle = 70f;
    private const float BeaconHeight = 3f;
    private const float TravelPing = 4f;

    private static readonly float[] AppleYaws = { 0f, 35f, -35f, 70f, -70f };

    public static PracticeRun Current { get; private set; }

    public static bool Running => Current != null;

    public readonly PracticeFlow Flow = new();

    private readonly TurnTracker turn = new();
    private readonly List<Object> owned = new();
    private readonly string[] appleTerms = NameFilter.Parse("Apple");

    private GhostDemo demo;
    private Transform station;
    private TouchOrb skipOrb,
        exitOrb;
    private LineRenderer goalInner,
        goalOuter,
        beacon;
    private PracticeSpot[] zones;
    private float eyeHeight;
    private Vector3 orbsAt;
    private float pingIn;
    private TextMeshProUGUI caption;
    private PracticePhase lastPhase;
    private int lastIndex = -1;
    private float phaseTime;
    private float arrivedFor;
    private float deadline;
    private bool leaving;
    private bool quitSent;

    // Station state, set on entry.
    private Item apple;
    private GameObject spawned;
    private Transform goalFollow;
    private Vector3 goal;
    private bool hasGoal;
    private bool seenHeld;
    private bool wheelSeen;
    private float consumedBefore;
    private int throwsBefore;
    private float climbFor;
    private int deniesBefore;
    private int recenters;
    private VRRig recenterRig;
    private Vector3 confirmAt;

    /// <summary>Where the skip and exit orbs are, for the self-test's scripted hands.</summary>
    public Vector3 SkipOrbPosition => skipOrb != null ? skipOrb.Position : Vector3.zero;

    public Vector3 ExitOrbPosition => exitOrb != null ? exitOrb.Position : Vector3.zero;

    /// <summary>A zone as laid out on arrival, for the self-test; false before the layout.</summary>
    public bool TryZone(PracticeZone zone, out PracticeSpot spot)
    {
        spot = zones != null ? zones[(int)zone] : default;
        return zones != null;
    }

    /// <summary>Where the practice could start now (<see cref="PracticeStart.Decide"/>).</summary>
    public static PracticeEntry Entry() =>
        PracticeStart.Decide(
            PhotonNetwork.InRoom,
            PhotonNetwork.OfflineMode,
            GuideScene.Title,
            GuideScene.Airport && Character.localCharacter != null,
            LoadingScreenHandler.loading || NetworkingUtilities.EnteringRoom
        );

    /// <summary>Starts the practice; false when it cannot (the caller plays the refusal cue).</summary>
    public static bool Begin(PracticeEntry entry)
    {
        if (Current != null || entry == PracticeEntry.Refused || !VRSession.Active)
            return false;
        if (entry == PracticeEntry.FromTitle)
        {
            var menu = FindAnyObjectByType<MainMenu>();
            if (menu == null || menu.playSoloButton == null)
            {
                Plugin.Log.LogWarning(
                    "[ui.guide] no Play Solo button on the title; practice not started"
                );
                return false;
            }
            // The Play Solo button (MainMenu.cs:45 -> StartSoloSession, :133): offline mode, then the Airport.
            // StartSoloSession sets EnteringRoom and starts the load at once; without that, nothing is coming.
            menu.playSoloButton.onClick.Invoke();
            if (!NetworkingUtilities.EnteringRoom && !LoadingScreenHandler.loading)
            {
                Plugin.Log.LogWarning(
                    "[ui.guide] Play Solo did not start a load; practice not started"
                );
                return false;
            }
        }
        var go = new GameObject("PeakXR_Practice");
        DontDestroyOnLoad(go);
        Current = go.AddComponent<PracticeRun>();
        Current.Flow.Begin(entry == PracticeEntry.FromTitle);
        Current.deadline = Time.unscaledTime + LoadTimeout;
        if (entry == PracticeEntry.InPlace)
            ClosePause();
        Plugin.Log.LogInfo($"[ui.guide] practice started ({entry})");
        return true;
    }

    public void Stop()
    {
        Plugin.Log.LogInfo("[ui.guide] practice stopped");
        Flow.Exit();
    }

    private void OnDestroy()
    {
        if (recenterRig != null)
            recenterRig.Recentered -= OnRecentered;
        GuideRows.ComfortFirst = false;
        if (Current == this)
            Current = null;
        demo?.Destroy();
        skipOrb?.Destroy();
        exitOrb?.Destroy();
        foreach (var o in owned)
            if (o != null)
                Destroy(o);
        if (station != null)
            Destroy(station.gameObject);
        // Last, and guarded: the scene may be half torn down, and everything above must be gone regardless.
        try
        {
            Despawn();
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"[ui.guide] practice cleanup: {e.Message}");
        }
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        // Never online: a room that is not offline ends it on the spot, without touching the session.
        if (PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode)
        {
            Plugin.Log.LogWarning("[ui.guide] online room: practice ended");
            Destroy(gameObject);
            return;
        }
        if (Flow.Phase == PracticePhase.Exiting)
        {
            Leave();
            return;
        }
        if (Flow.Phase == PracticePhase.Loading)
        {
            WaitForAirport(dt);
            return;
        }
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        if (
            c == null
            || rig == null
            || rig.Head == null
            || !VRSession.Active
            || !GuideScene.Airport
        )
        {
            // Left by other means (the pause menu's quit, or boarding the plane into a real run): end quietly,
            // never quitting a run the player started.
            Plugin.Log.LogInfo("[ui.guide] practice ended: left the airport");
            Destroy(gameObject);
            return;
        }

        Flow.Tick(dt);
        if (Flow.Phase != lastPhase || Flow.Index != lastIndex)
            OnPhase(c, rig);
        if (Flow.Phase == PracticePhase.Exiting)
            return;
        phaseTime += dt;

        PlaceOrbs(rig.Head, false);
        demo?.Tick(dt, 1f, rig.Head);
        TickOrbs(rig, dt);
        if (Flow.Phase == PracticePhase.Travel)
            Travel(rig.Head, dt);
        else if (Flow.Phase == PracticePhase.Station && Done(c, rig, dt))
            Flow.Succeed();
        PoseGoal(rig.Head);
    }

    // ---- loading and leaving ----

    private void WaitForAirport(float dt)
    {
        bool ready =
            !LoadingScreenHandler.loading
            && Character.localCharacter != null
            && PhotonNetwork.InRoom
            && PhotonNetwork.OfflineMode
            && GuideScene.Airport
            && VRRig.Instance != null;
        arrivedFor = ready ? arrivedFor + dt : 0f;
        if (arrivedFor >= ArrivalSettle)
        {
            Flow.Arrived();
            return;
        }
        if (Time.unscaledTime > deadline)
        {
            Plugin.Log.LogWarning(
                "[ui.guide] the solo airport did not load in time; practice ended"
            );
            CueAudio.Play(
                CueSound.Refused,
                VRRig.Instance != null && VRRig.Instance.Head != null
                    ? VRRig.Instance.Head.position
                    : Vector3.zero,
                1f
            );
            Flow.Exit();
        }
    }

    /// <summary>The pause menu's own quit (PauseMenuMainPage.cs:206): disconnecting state, then leave the room, which
    /// loads the title (NetworkConnector.cs:220). comfort.events fades that cut. Outside a room (a load that broke half
    /// way) the title loads directly, as the game's own error paths do (NetworkConnector.cs:263).</summary>
    private void Leave()
    {
        if (!leaving)
        {
            leaving = true;
            // A stop during the Play Solo load waits for the airport, then quits from there.
            deadline = Time.unscaledTime + LoadTimeout;
            HideStation();
            Despawn();
            Plugin.Log.LogInfo(
                $"[ui.guide] practice over: {Flow.Completed} done, {Flow.Skipped} skipped; back to the title"
            );
        }
        if (!quitSent && !GuideScene.Title && !LoadingScreenHandler.loading)
        {
            quitSent = true;
            if (PhotonNetwork.InRoom)
            {
                ClosePause();
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
        bool home =
            GuideScene.Title && !LoadingScreenHandler.loading && !NetworkingUtilities.EnteringRoom;
        if (home || Time.unscaledTime > deadline)
        {
            if (!home)
                Plugin.Log.LogWarning("[ui.guide] the title did not come back in time");
            Destroy(gameObject);
        }
    }

    private static void ClosePause()
    {
        if (!GUIManager.InPauseMenu)
            return;
        // The game's own close (PauseMenuHandler.cs:59).
        var handler = FindAnyObjectByType<PauseMenuHandler>();
        if (handler != null)
            handler.gameObject.SetActive(false);
    }

    // ---- stations ----

    private void OnPhase(Character c, VRRig rig)
    {
        var phase = Flow.Phase;
        bool newStation =
            phase == PracticePhase.Station
            && (lastPhase != PracticePhase.Station || Flow.Index != lastIndex);
        lastPhase = phase;
        lastIndex = Flow.Index;
        phaseTime = 0f;
        if (station == null)
            BuildStation();
        if (zones == null)
            PlanZones(rig.Head);
        beacon.gameObject.SetActive(phase == PracticePhase.Travel);
        switch (phase)
        {
            case PracticePhase.Station when newStation:
                Despawn();
                skipOrb.Reset();
                exitOrb.Reset();
                PlaceStation();
                PlaceOrbs(rig.Head, true);
                EnterStation(c, rig);
                break;
            case PracticePhase.Travel:
                // The next station's demo already plays at its zone, under a beam, with a ring on the floor there.
                Despawn();
                skipOrb.Reset();
                exitOrb.Reset();
                hasGoal = false;
                PlaceStation();
                if (demo == null || !demo.Alive)
                    demo = new GhostDemo("PeakXR_PracticeDemo", station);
                demo.Play(PracticeFlow.TopicOf(Flow.Current));
                SetCaption("Walk to the light");
                var spot = zones[(int)Flow.Zone];
                beacon.SetPosition(0, spot.Feet);
                beacon.SetPosition(1, spot.Feet + Vector3.up * BeaconHeight);
                pingIn = 0f;
                Plugin.Log.LogInfo(
                    $"[ui.guide] practice: walk to the {Flow.Zone} zone ({spot.Anchor})"
                );
                break;
            case PracticePhase.Confirm:
                hasGoal = false;
                CueAudio.Play(CueSound.WouldWork, confirmAt, 1f);
                UIHaptics.Tick(true, 0.35f, 0.1f);
                UIHaptics.Tick(false, 0.35f, 0.1f);
                break;
            case PracticePhase.Done:
                hasGoal = false;
                demo?.Destroy();
                demo = null;
                SetCaption("Practice done");
                CueAudio.Play(CueSound.Notice, rig.Head.position + rig.Head.forward * 0.6f, 1f);
                UIHaptics.Tick(true, 0.3f, 0.15f);
                UIHaptics.Tick(false, 0.3f, 0.15f);
                break;
        }
    }

    private void EnterStation(Character c, VRRig rig)
    {
        var s = Flow.Current;
        if (demo == null || !demo.Alive)
            demo = new GhostDemo("PeakXR_PracticeDemo", station);
        demo.Play(PracticeFlow.TopicOf(s));
        SetCaption(GuideTable.Get(PracticeFlow.TopicOf(s)).Caption);
        hasGoal = false;
        goalFollow = null;
        seenHeld = false;
        wheelSeen = false;
        climbFor = 0f;
        confirmAt = rig.Head.position + rig.Head.forward * 0.5f;
        apple ??= FindApple();
        Plugin.Log.LogInfo(
            $"[ui.guide] practice station {Flow.Index + 1}/{PracticeFlow.Stations.Length}: {s}"
        );
        switch (s)
        {
            case PracticeStation.Turn:
                turn.Reset(rig.Origin.eulerAngles.y);
                break;
            case PracticeStation.Grab:
                SpawnApple(rig);
                break;
            case PracticeStation.Holster:
            case PracticeStation.Eat:
            case PracticeStation.Throw:
                if (HeldItem(c) == null)
                    GiveApple(c);
                consumedBefore = c.data.lastConsumedItem;
                throwsBefore = PhysicalInteraction.ThrowCount;
                break;
            case PracticeStation.Climb:
                hasGoal = FindWall(c, out goal);
                if (!hasGoal)
                    Plugin.Log.LogInfo("[ui.guide] no wall near the practice spot; walk to one");
                break;
            case PracticeStation.Rope:
                goalFollow = RopeHold(rig.Head.position);
                hasGoal = goalFollow != null;
                if (!hasGoal)
                {
                    // Nothing to climb: say so in the log and move on rather than wait on a rope that is not there.
                    Plugin.Log.LogWarning(
                        "[ui.guide] no hanging rope in the scene; rope station skipped"
                    );
                    Flow.Skip();
                }
                break;
            case PracticeStation.Refused:
                // Past the game's 2 m interact reach: a pointed grip at it gets the out-of-reach refusal.
                SpawnApple(rig, FarApple);
                deniesBefore = Affordance.Count;
                break;
            case PracticeStation.Recenter:
                if (recenterRig != rig)
                {
                    if (recenterRig != null)
                        recenterRig.Recentered -= OnRecentered;
                    recenterRig = rig;
                    rig.Recentered += OnRecentered;
                }
                recenters = 0;
                break;
        }
    }

    private void OnRecentered() => recenters++;

    /// <summary>The station's success signal, the same game state the self-test steps check.</summary>
    private bool Done(Character c, VRRig rig, float dt)
    {
        var held = HeldItem(c);
        bool left = LocalHold.ItemInLeftHand;
        switch (Flow.Current)
        {
            case PracticeStation.Turn:
                turn.Update(rig.Origin.eulerAngles.y);
                confirmAt = rig.Head.position + rig.Head.forward * 0.6f;
                return turn.Total >= TurnGoal;
            case PracticeStation.Grab:
                if (held == null || apple == null || held.itemID != apple.itemID)
                    return false;
                confirmAt = rig.Hand(left).position;
                return true;
            case PracticeStation.Holster:
                if (held != null)
                {
                    seenHeld = true;
                    var selected = c.refs.items.currentSelectedSlot;
                    hasGoal =
                        selected.IsSome
                        && BodyInventory.Instance.Valid
                        && BodyInventory.Instance.SlotWorld(selected.Value, out goal);
                    return false;
                }
                hasGoal = false;
                confirmAt = rig.Hand(left).position;
                return seenHeld && HotbarHolds(c);
            case PracticeStation.Eat:
                hasGoal = false;
                confirmAt = rig.Head.position + rig.Head.forward * 0.3f;
                return !Mathf.Approximately(c.data.lastConsumedItem, consumedBefore);
            case PracticeStation.Throw:
                confirmAt = rig.Hand(left).position;
                return PhysicalInteraction.ThrowCount != throwsBefore;
            case PracticeStation.Climb:
            case PracticeStation.Rope:
                // The wall and the rope count apart (CharacterData.isClimbing :119, isRopeClimbing :121).
                bool on =
                    Flow.Current == PracticeStation.Rope
                        ? c.data.isRopeClimbing
                        : c.data.isClimbing;
                climbFor = on ? climbFor + dt : 0f;
                if (climbFor > 0f)
                    confirmAt = rig.Head.position + rig.Head.forward * 0.3f;
                return climbFor >= ClimbHold;
            case PracticeStation.Refused:
                // Any refusal counts: the drill is to see, hear and feel one.
                confirmAt = Affordance.LastPosition;
                return Affordance.Count > deniesBefore;
            case PracticeStation.Recenter:
                confirmAt = rig.Head.position + rig.Head.forward * 0.6f;
                return recenters > 0;
            default:
                var wheel = GUIManager.instance != null ? GUIManager.instance.emoteWheel : null;
                if (wheel == null)
                    return false;
                if (wheel.activeInHierarchy)
                {
                    wheelSeen = true;
                    return false;
                }
                confirmAt = rig.Head.position + rig.Head.forward * 0.5f;
                return wheelSeen;
        }
    }

    private static Item HeldItem(Character c) => c.data != null ? c.data.currentItem : null;

    private bool HotbarHolds(Character c)
    {
        if (c.player == null || apple == null)
            return false;
        for (byte i = 0; i < 3; i++)
        {
            var slot = c.player.GetItemSlot(i);
            if (
                slot != null
                && !slot.IsEmpty()
                && slot.prefab != null
                && slot.prefab.itemID == apple.itemID
            )
                return true;
        }
        return false;
    }

    private Item FindApple()
    {
        foreach (var item in SandboxActions.Items)
            if (NameFilter.Matches(item.name, appleTerms))
                return item;
        Plugin.Log.LogWarning(
            "[ui.guide] no Apple in the item database; item stations can only be skipped"
        );
        return null;
    }

    /// <summary>An Apple held still at waist height in front of the dominant hand, the self-test's spawn call
    /// (<c>PhotonNetwork.InstantiateItemRoom</c>; offline, so the room is ours).</summary>
    private void SpawnApple(VRRig rig, float distance = 0.45f)
    {
        if (apple == null)
            return;
        var head = rig.Head;
        var ahead = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
        var at = Vector3.zero;
        // Never inside a wall or behind one: the first heading with a clear line from the eyes.
        foreach (float yaw in AppleYaws)
        {
            var forward = Quaternion.Euler(0f, yaw, 0f) * ahead;
            var side = Vector3.Cross(Vector3.up, forward) * (VRInput.LeftHanded ? -1f : 1f);
            at = head.position + forward * distance + side * 0.15f + Vector3.down * 0.5f;
            if (PracticeLayout.Clear(head.position, at + (at - head.position).normalized * 0.1f))
                break;
        }
        spawned = PhotonNetwork.InstantiateItemRoom(apple.gameObject.name, at, Quaternion.identity);
        var world = spawned != null ? spawned.GetComponent<Item>() : null;
        if (world == null)
        {
            Plugin.Log.LogWarning("[ui.guide] could not place the practice Apple");
            return;
        }
        if (world.rig != null)
            world.rig.isKinematic = true;
        goalFollow = world.transform;
        hasGoal = true;
    }

    /// <summary>Into the hand through the game's own give (<c>GameUtils.InstantiateAndGrab</c>, GameUtils.cs:274).</summary>
    private void GiveApple(Character c)
    {
        if (apple != null && GameUtils.instance != null)
            GameUtils.instance.InstantiateAndGrab(apple, c);
    }

    /// <summary>Removes the station's Apple only while it lies in the world (<c>Item.itemState</c> Ground,
    /// Item.cs:286). One in a hand or a slot stays; the offline room is discarded on the way out anyway.</summary>
    private void Despawn()
    {
        var go = spawned;
        spawned = null;
        if (go == null)
            return;
        var item = go.GetComponent<Item>();
        if (item == null || item.itemState != ItemState.Ground)
            return;
        var view = go.GetComponent<PhotonView>();
        if (view != null && (view.IsMine || PhotonNetwork.IsMasterClient))
            PhotonNetwork.Destroy(go);
    }

    /// <summary>The nearest steep face within 12 m at chest height, so the climb station can mark it.</summary>
    private static bool FindWall(Character c, out Vector3 point)
    {
        point = default;
        float best = float.MaxValue;
        var origin = c.Center + Vector3.up * 0.3f;
        for (int i = 0; i < 24; i++)
        {
            var dir = Quaternion.Euler(0f, i * 15f, 0f) * Vector3.forward;
            if (
                !Physics.Raycast(
                    origin,
                    dir,
                    out var hit,
                    12f,
                    HelperFunctions.terrainMapMask,
                    QueryTriggerInteraction.Ignore
                )
                || hit.distance >= best
            )
                continue;
            float angle = Vector3.Angle(hit.normal, Vector3.up);
            if (angle < 65f || angle > 115f)
                continue;
            best = hit.distance;
            point = hit.point + hit.normal * 0.03f;
        }
        return best < float.MaxValue;
    }

    /// <summary>The hanging rope's segment nearest the eyes, the one to reach for: the airport wall's rope, spawned by
    /// <c>RopeAnchorWithRope.SpawnRope</c> on joining the room (RopeAnchorWithRope.cs:21, its <c>rope</c> :18), its
    /// segments from <c>Rope.GetRopeSegments</c> (Rope.cs:186). Null when no rope hangs anywhere.</summary>
    private static Transform RopeHold(Vector3 eye)
    {
        var anchor = FindAnyObjectByType<RopeAnchorWithRope>();
        if (anchor == null || anchor.rope == null)
            return null;
        Transform best = null;
        float nearest = float.MaxValue;
        foreach (var segment in anchor.rope.GetRopeSegments())
        {
            if (segment == null)
                continue;
            float d = (segment.position - eye).sqrMagnitude;
            if (d < nearest)
            {
                nearest = d;
                best = segment;
            }
        }
        return best;
    }

    // ---- zones ----

    private void PlanZones(Transform head)
    {
        var feet = PracticeLayout.FloorUnder(head.position);
        eyeHeight = Mathf.Clamp(head.position.y - feet.y, 0.9f, 2f);
        var forward = PracticeLayout.Flat(head.forward);
        if (forward.sqrMagnitude < 1e-4f)
            forward = PracticeLayout.Flat(head.up);
        zones = PracticeLayout.Plan(feet, forward.normalized);
    }

    /// <summary>Walking to the next zone: arrival starts its station; the cue sounds from the spot now and then, so
    /// it can be found by ear when it is behind you.</summary>
    private void Travel(Transform head, float dt)
    {
        var spot = zones[(int)Flow.Zone];
        pingIn -= dt;
        if (pingIn <= 0f)
        {
            pingIn = TravelPing;
            CueAudio.Play(CueSound.Notice, spot.Feet + Vector3.up * eyeHeight, 1f);
        }
        if (PracticeZones.Arrived(head.position.x - spot.Feet.x, head.position.z - spot.Feet.z))
        {
            Plugin.Log.LogInfo($"[ui.guide] practice: reached the {Flow.Zone} zone");
            Flow.Reached();
        }
    }

    /// <summary>Skipping a walk brings a zone that is tied to nothing to where you stand; a tied zone (the climbing
    /// wall) stays, as its station needs the wall.</summary>
    private void BringZoneHere(Transform head)
    {
        int z = (int)Flow.Zone;
        if (zones[z].Fixed)
            return;
        var forward = PracticeLayout.Flat(head.forward);
        zones[z] = new PracticeSpot
        {
            Feet = PracticeLayout.FloorUnder(head.position),
            Facing = forward.sqrMagnitude > 1e-4f ? forward.normalized : zones[z].Facing,
            Anchor = "where you skipped the walk",
        };
    }

    // ---- the station's things in the world ----

    private void BuildStation()
    {
        station = new GameObject("PeakXR_PracticeStation").transform;
        DontDestroyOnLoad(station.gameObject);
        var hintMat = Own(
            UIMaterials.Create(Affordance.HintColor, drawOnTop: false, renderQueue: 3120)
        );
        // Chevron on skip, cross on exit: the shapes say which is which without text.
        skipOrb = new TouchOrb("PeakXR_PracticeSkip", cross: false, seconds: 0.6f);
        exitOrb = new TouchOrb("PeakXR_PracticeExit", cross: true, seconds: 0.8f);
        goalInner = Ring("GoalInner", hintMat);
        goalOuter = Ring("GoalOuter", hintMat);
        // The walk cue: a tall beam over the next zone, seen over fences and desks from across the hall.
        beacon = Ring("Beacon", hintMat);
        beacon.loop = false;
        beacon.positionCount = 2;
        beacon.startWidth = 0.03f;
        beacon.endWidth = 0.005f;

        if (AffordanceFeature.ActionHintText != null && AffordanceFeature.ActionHintText.Value)
        {
            var go = new GameObject("PeakXR_PracticeCaption", typeof(RectTransform));
            go.transform.SetParent(station, false);
            GuideStage.WorldSign(go);
            ((RectTransform)go.transform).sizeDelta = new Vector2(600f, 90f);
            go.transform.localScale = Vector3.one * 0.001f;
            go.transform.localPosition = new Vector3(0f, -0.62f, 0f);
            caption = go.AddComponent<TextMeshProUGUI>();
            caption.fontSize = 32f;
            caption.alignment = TextAlignmentOptions.Center;
            caption.raycastTarget = false;
            var font =
                GUIManager.instance != null && GUIManager.instance.interactName != null
                    ? GUIManager.instance.interactName.GetComponentInChildren<TextMeshProUGUI>(true)
                    : null;
            if (font != null)
                caption.font = font.font;
        }
    }

    private LineRenderer Ring(string name, Material material)
    {
        var go = new GameObject("PeakXR_Practice" + name);
        go.transform.SetParent(station, false);
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 24;
        line.loop = true;
        line.startWidth = line.endWidth = 0.004f;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.sharedMaterial = material;
        go.SetActive(false);
        return line;
    }

    private Material Own(Material m)
    {
        owned.Add(m);
        return m;
    }

    private void SetCaption(string text)
    {
        if (caption != null)
            caption.text = text;
    }

    /// <summary>
    /// The demo at the current zone, ahead of its spot and to the off-hand side at eye height less a little, kept
    /// off the wall the climb zone faces (<see cref="PracticeZones.DemoOffset"/>). It stays in the world: walking
    /// to a zone means walking to it.
    /// </summary>
    private void PlaceStation()
    {
        var spot = zones[(int)Flow.Zone];
        float chest = eyeHeight - 0.15f;
        var (ahead, side) = PracticeZones.DemoOffset(PracticeLayout.ClearAhead(spot, chest));
        var offSide = Vector3.Cross(Vector3.up, spot.Facing) * (VRInput.LeftHanded ? 1f : -1f);
        station.SetPositionAndRotation(
            spot.Feet + Vector3.up * chest + spot.Facing * ahead + offSide * side,
            Quaternion.LookRotation(spot.Facing, Vector3.up)
        );
    }

    /// <summary>
    /// The orbs within reach on the off-hand side, away from where the dominant hand works. Placed when a station
    /// starts and again only if you walk or turn well away from them (comfort: no UI follows the head).
    /// </summary>
    private void PlaceOrbs(Transform head, bool force)
    {
        var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (forward.sqrMagnitude < 1e-4f)
            forward = Vector3.ProjectOnPlane(head.up, Vector3.up);
        forward.Normalize();
        if (!force)
        {
            var to = orbsAt - head.position;
            to.y = 0f;
            if (to.magnitude < ReplaceDistance && Vector3.Angle(to, forward) < ReplaceAngle)
                return;
        }
        var offSide = Vector3.Cross(Vector3.up, forward) * (VRInput.LeftHanded ? 1f : -1f);
        var rotation = Quaternion.LookRotation(forward, Vector3.up);
        // Where the demo stood before zones: the anchor the walk-away test measures from.
        orbsAt = head.position + forward * 1.0f + offSide * 0.3f;
        // Chest high, above where an idle hand rests, so a resting hand never skips by accident.
        var orbs = head.position + forward * 0.4f + Vector3.down * 0.12f;
        skipOrb.Place(orbs + offSide * 0.28f, rotation);
        exitOrb.Place(orbs + offSide * 0.42f, rotation);
    }

    private void TickOrbs(VRRig rig, float dt)
    {
        if (
            Flow.Phase
            is not (PracticePhase.Station or PracticePhase.Confirm or PracticePhase.Travel)
        )
            return;
        if (skipOrb.Tick(rig, dt))
        {
            CueAudio.Play(CueSound.Notice, skipOrb.Position, 1f);
            if (Flow.Phase == PracticePhase.Travel)
            {
                Plugin.Log.LogInfo($"[ui.guide] practice walk to the {Flow.Zone} zone skipped");
                BringZoneHere(rig.Head);
            }
            else
                Plugin.Log.LogInfo($"[ui.guide] practice station {Flow.Current} skipped");
            Flow.Skip();
        }
        if (exitOrb.Tick(rig, dt))
        {
            CueAudio.Play(CueSound.Notice, exitOrb.Position, 1f);
            Stop();
        }
    }

    private void PoseGoal(Transform eye)
    {
        bool confirm = Flow.Phase == PracticePhase.Confirm;
        bool travel = Flow.Phase == PracticePhase.Travel;
        bool show = (hasGoal && Flow.Phase == PracticePhase.Station) || confirm || travel;
        if (goalInner.gameObject.activeSelf != show)
        {
            goalInner.gameObject.SetActive(show);
            goalOuter.gameObject.SetActive(show);
        }
        if (!show)
            return;
        if (travel)
        {
            // Lying on the floor where to stand, about a stride across.
            var feet = zones[(int)Flow.Zone].Feet + Vector3.up * 0.03f;
            float r = 0.35f * CueMotion.Breathe(phaseTime);
            RingAt(goalInner, feet, Vector3.up, r);
            RingAt(goalOuter, feet, Vector3.up, r * 1.4f);
            return;
        }
        if (goalFollow != null && Flow.Phase == PracticePhase.Station)
            goal = goalFollow.position;
        var center = confirm ? confirmAt : goal;
        // The confirm ring widens from the spot over the confirm time; the goal ring breathes slowly.
        float scale = confirm
            ? 1f + 2f * Mathf.Clamp01(phaseTime / PracticeFlow.ConfirmSeconds)
            : CueMotion.Breathe(phaseTime);
        float radius = 0.05f * scale;
        var normal = (eye.position - center).normalized;
        if (normal.sqrMagnitude < 0.5f)
            normal = -eye.forward;
        RingAt(goalInner, center, normal, radius);
        RingAt(goalOuter, center, normal, radius * 1.4f);
    }

    private static void RingAt(LineRenderer line, Vector3 center, Vector3 normal, float radius)
    {
        var a = Vector3
            .Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.forward)
            .normalized;
        var b = Vector3.Cross(normal, a);
        for (int i = 0; i < 24; i++)
        {
            float angle = i * Mathf.PI * 2f / 24f;
            line.SetPosition(i, center + (a * Mathf.Cos(angle) + b * Mathf.Sin(angle)) * radius);
        }
    }

    private void HideStation()
    {
        demo?.Destroy();
        demo = null;
        hasGoal = false;
        if (station != null)
            station.gameObject.SetActive(false);
        skipOrb?.SetActive(false);
        exitOrb?.SetActive(false);
    }
}
