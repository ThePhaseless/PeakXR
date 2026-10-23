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
/// The VR practice: a few stations in the offline solo airport, each teaching one VR mechanic. A station shows its
/// ghost demo ahead of you, marks where to act with the "works here" double ring, waits for the same game state the
/// self-test reads, then confirms with the rising note, a pulse on both controllers and a widening ring. Two touch
/// orbs on the off-hand side skip the station (chevron) or leave (cross); the VR tab's Practice row stops it too.
/// It never runs in an online room and always ends back at the title through the game's own leave path.
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
    private static readonly Color SkipColor = new(0.45f, 0.9f, 0.82f, 0.8f);
    private static readonly Color ExitColor = new(1f, 0.42f, 0.36f, 0.8f);

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
        goalOuter;
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

        PlaceStation(rig.Head, false);
        demo?.Tick(dt, 1f, rig.Head);
        TickOrbs(rig, dt);
        if (Flow.Phase == PracticePhase.Station && Done(c, rig, dt))
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
        switch (phase)
        {
            case PracticePhase.Station when newStation:
                Despawn();
                skipOrb.Reset();
                exitOrb.Reset();
                PlaceStation(rig.Head, true);
                EnterStation(c, rig);
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
                climbFor = c.data.isClimbingAnything ? climbFor + dt : 0f;
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
        var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
        var side = Vector3.Cross(Vector3.up, forward) * (VRInput.LeftHanded ? -1f : 1f);
        var at = head.position + forward * distance + side * 0.15f + Vector3.down * 0.5f;
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

    // ---- the station's things in the world ----

    private void BuildStation()
    {
        station = new GameObject("PeakXR_PracticeStation").transform;
        DontDestroyOnLoad(station.gameObject);
        var hintMat = Own(
            UIMaterials.Create(Affordance.HintColor, drawOnTop: false, renderQueue: 3120)
        );
        // Chevron on skip, cross on exit: the shapes say which is which without text.
        skipOrb = new TouchOrb("PeakXR_PracticeSkip", SkipColor, cross: false, seconds: 0.6f);
        exitOrb = new TouchOrb("PeakXR_PracticeExit", ExitColor, cross: true, seconds: 0.8f);
        goalInner = Ring("GoalInner", hintMat);
        goalOuter = Ring("GoalOuter", hintMat);

        if (AffordanceFeature.ActionHintText != null && AffordanceFeature.ActionHintText.Value)
        {
            var go = new GameObject("PeakXR_PracticeCaption", typeof(RectTransform));
            go.transform.SetParent(station, false);
            go.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
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
    /// Demo ahead and to the off-hand side, the orbs within reach below it on the same side, away from where the
    /// dominant hand works. Placed when a station starts and again only if you walk or turn well away (comfort: no
    /// UI follows the head).
    /// </summary>
    private void PlaceStation(Transform head, bool force)
    {
        var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (forward.sqrMagnitude < 1e-4f)
            forward = Vector3.ProjectOnPlane(head.up, Vector3.up);
        forward.Normalize();
        if (!force && station != null)
        {
            var to = station.position - head.position;
            to.y = 0f;
            if (to.magnitude < ReplaceDistance && Vector3.Angle(to, forward) < ReplaceAngle)
                return;
        }
        var offSide = Vector3.Cross(Vector3.up, forward) * (VRInput.LeftHanded ? 1f : -1f);
        var rotation = Quaternion.LookRotation(forward, Vector3.up);
        station.SetPositionAndRotation(
            head.position + forward * 1.0f + offSide * 0.3f + Vector3.down * 0.15f,
            rotation
        );
        // Chest high, above where an idle hand rests, so a resting hand never skips by accident.
        var orbs = head.position + forward * 0.4f + Vector3.down * 0.12f;
        skipOrb.Place(orbs + offSide * 0.28f, rotation);
        exitOrb.Place(orbs + offSide * 0.42f, rotation);
    }

    private void TickOrbs(VRRig rig, float dt)
    {
        if (Flow.Phase is not (PracticePhase.Station or PracticePhase.Confirm))
            return;
        if (skipOrb.Tick(rig, dt))
        {
            CueAudio.Play(CueSound.Notice, skipOrb.Position, 1f);
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
        bool show = (hasGoal && Flow.Phase == PracticePhase.Station) || confirm;
        if (goalInner.gameObject.activeSelf != show)
        {
            goalInner.gameObject.SetActive(show);
            goalOuter.gameObject.SetActive(show);
        }
        if (!show)
            return;
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
