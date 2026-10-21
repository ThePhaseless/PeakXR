using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.UI.Common;
using PeakXR.UI.Menu;
using PeakXR.UI.Settings;
using PeakXR.VR;
using PeakXR.XR;
using Photon.Pun;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zorro.Core;
using Vector3 = UnityEngine.Vector3;

namespace PeakXR.Sandbox;

/// <summary>
/// The <c>-peakxr-selftest</c> script. Each step runs under <see cref="Guard"/> with its own timeout, so a step
/// that throws or hangs is logged FAIL and the script moves on; errors and exceptions the game logs meanwhile
/// are attributed to the running step. Results go to <c>BepInEx/PeakXR-selftest.log</c> and screenshots to
/// <c>BepInEx/PeakXR-selftest/</c>. Correctness matters more than frame cost here: it runs once, unattended.
/// </summary>
internal sealed partial class SelfTestRunner : MonoBehaviour
{
    const int RowSize = 8;
    const float ScriptMinutes = 90f;
    const float HoldMinutes = 20f;

    // One name fragment per category to give to the hand and use: food, tools, rope, lights, launchers.
    static readonly string[] UseItems =
    {
        "Granola",
        "Apple",
        "Compass",
        "Binoculars",
        "RopeSpool",
        "Lantern",
        "Flare",
        "Torch",
        "RopeShooter",
        "ChainShooter",
    };

    static readonly float[] Times = { 6f, 12f, 21f };

    readonly SelfTestReport report = new();
    readonly List<GameObject> spawned = new();
    readonly List<MobEntry> mobs = new();
    string shotDir;
    string logPath;
    bool stepOk;
    string stepNote;

    /// <summary>The step's first failure; it leads the recorded note, so a success note written earlier cannot hide it.</summary>
    string failNote;

    /// <summary>Set by a step that cannot run because the level lacks what it needs; recorded as SKIP, not FAIL.</summary>
    string stepSkip;
    bool finished;

    static float Now => Time.realtimeSinceStartup;

    // Offline only: the script never drives a character in an online room.
    static bool Ready =>
        !LoadingScreenHandler.loading
        && Character.localCharacter != null
        && PhotonNetwork.InRoom
        && PhotonNetwork.OfflineMode;

    void Awake()
    {
        Application.logMessageReceived += OnLog;
        BepInEx.Logging.Logger.Listeners.Add(bootstrapTail);
    }

    void OnDestroy()
    {
        VR.VRInput.TestOverride = null;
        SandboxActions.Forced = false;
        SelfTestEndGamePatch.Hold = false;
        Application.logMessageReceived -= OnLog;
        BepInEx.Logging.Logger.Listeners.Remove(bootstrapTail);
    }

    void Start()
    {
        if (MpDirectory != null)
        {
            StartMp();
            return;
        }
        shotDir = Path.Combine(Paths.BepInExRootPath, "PeakXR-selftest");
        logPath = Path.Combine(Paths.BepInExRootPath, "PeakXR-selftest.log");
        try
        {
            Directory.CreateDirectory(shotDir);
            foreach (var old in Directory.GetFiles(shotDir, "*.png"))
                File.Delete(old);
            File.WriteAllText(logPath, Header() + "\n");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[selftest] cannot prepare output: {e.Message}");
        }
        Plugin.Log.LogInfo($"[selftest] started, results in {logPath}");
        StartCoroutine(
            Guard(
                Script(),
                Now + ScriptMinutes * 60f,
                e => Abort(e == null ? "timeout" : e.ToString())
            )
        );
    }

    // ---------------------------------------------------------------- script

    IEnumerator Script()
    {
        bool hold = SelfTestFeature.Hold;
        // Hold mode keeps the real controllers; otherwise idle hands stay in view until a hand step drives them.
        if (VRRun && !hold)
            VR.VRInput.TestOverride = IdleHands;
        yield return Run("00-title", 180f, Title());
        if (VRRun)
            yield return Run("vr.00-session", 60f, Session());
        yield return VRStep("vr.01-rig", 20f, RigCheck());
        yield return VRStep("vr.02-title-stage", 20f, TitleStageCheck());
        if (!hold)
            yield return TutorialSteps();
        yield return Run("01-airport", 180f, Airport());
        if (!Ready)
        {
            Finish();
            yield break;
        }
        SandboxActions.SetGodMode(true);
        yield return VRStep("vr.03-canvases", 20f, CanvasCheck());

        if (!hold)
        {
            yield return Run("02-settings-vr", 30f, SettingsVR());
            ClosePause();
            yield return VRStep("vr.04-settings-panel", 30f, SettingsPanel());
            ClosePause();
            yield return AirportScreenSteps();
        }

        if (!hold)
            yield return Items();

        StartLoadWatch();
        yield return Run("04-beach", 300f, Level());
        yield return VRStep("vr.05-loading-space", 5f, LoadingCheck());
        if (Ready && Singleton<MapHandler>.Instance != null)
        {
            SandboxActions.SetGodMode(true);
            yield return VRStep("vr.06-features", 15f, FeaturesCheck());
            yield return VRStep("vr.08-arm-reach", 15f, ArmReachCheck());
            yield return VRStep("vr.09-belt-view", 10f, BeltViewCheck());
            yield return VRStep("vr.33-ragdoll-arms", 20f, RagdollArmsCheck());
            if (hold)
            {
                Finish();
                yield break;
            }
            // Use steps need run-only systems (achievements, item render features), so they run in the level.
            yield return Uses();
            yield return Mobs();
            yield return VRStep("vr.07-comfort", 100f, ComfortCheck());
            yield return VRStep("vr.34-blind", 15f, BlindCheck());
            yield return HandSteps();
            yield return SpatialSteps();
            foreach (var segment in SandboxActions.Segments)
            {
                // The summit spawn looks level over the cloud sea with no ground in frame, so the time-of-day and
                // death (spectate) frames are taken a segment lower, where ground and props show the view.
                if (segment == Segment.Peak)
                    continue;
                if (Singleton<MapHandler>.Instance.GetCurrentSegment() == segment)
                    yield return Run("06-segment-" + segment, 5f, AlreadyIn(segment));
                else
                    yield return Run("06-segment-" + segment, 120f, Jump(segment));
                yield return TornadoIfHere();
                if (segment == Segment.Tropics)
                    yield return VineSteps();
                // Open desert with sky (TheKiln's spawn is indoors, so its frames all looked the same).
                if (segment == Segment.Caldera)
                    yield return TimeSteps();
            }
            // Each kills the solo player and revives them, so nothing after can trip over a death.
            yield return DeathSteps();
            if (System.Array.IndexOf(SandboxActions.Segments, Segment.Peak) >= 0)
                yield return Run("06-segment-Peak", 120f, Jump(Segment.Peak));
            // Loads another level when this one had no tornado spawner, so it goes last.
            yield return TornadoTrip();
        }
        Finish();
    }

    /// <summary>Title ready: the menu scene is up and no load is running (MainMenu.cs:18 <c>SceneName</c>).</summary>
    IEnumerator Title()
    {
        yield return Until(
            () =>
                InScene(MainMenu.SceneName)
                && !LoadingScreenHandler.loading
                && FindAnyObjectByType<MainMenu>() != null,
            170f,
            "title not ready"
        );
        if (!stepOk)
            yield break;
        yield return Wait(3f);
        yield return Shot("00-title");
    }

    /// <summary>
    /// The Play Solo button: MainMenu.cs:43 wires it to <c>PlaySoloClicked</c> -> <c>StartSoloSession</c> (:131),
    /// which goes offline (<c>DisconnectForOfflineMode</c>, :137) and loads "Airport" (:154).
    /// </summary>
    IEnumerator Airport()
    {
        var menu = FindAnyObjectByType<MainMenu>();
        if (menu == null || menu.playSoloButton == null)
        {
            Fail("no MainMenu.playSoloButton");
            yield break;
        }
        menu.playSoloButton.onClick.Invoke();
        yield return Until(() => InScene("Airport") && Ready, 170f, "airport not ready");
        if (!stepOk)
            yield break;
        yield return Wait(4f);
        yield return Shot("01-airport");
    }

    /// <summary>
    /// Pause menu through <c>GUIManager.UpdatePaused</c> (GUIManager.cs:420, the Esc path), then its settings page
    /// (PauseMenuSettingsMenuPage.cs:10 <c>sharedSettingsMenu</c>) and the ui.settings VR tab via <c>TABS.Select</c>.
    /// </summary>
    IEnumerator SettingsVR()
    {
        yield return OpenVRTab();
        if (!stepOk)
            yield break;
        var menu = RootCanvasOf(FindAnyObjectByType<PauseMenuHandler>());
        yield return menu != null
            ? ShotAt("02-settings-vr", CentreOf(menu.transform))
            : Shot("02-settings-vr");

        ClosePause();
        yield return Until(() => !GUIManager.InPauseMenu, 3f, "pause menu did not close");
    }

    /// <summary>Opens the pause menu, its settings page and the VR tab; leaves it open.</summary>
    IEnumerator OpenVRTab()
    {
        var gui = GUIManager.instance;
        var c = Character.localCharacter;
        if (gui == null || c == null)
        {
            Fail("no GUIManager or character");
            yield break;
        }
        c.input.pauseWasPressed = true;
        gui.UpdatePaused();
        yield return Until(
            () => GUIManager.InPauseMenu && FindAnyObjectByType<PauseMenuHandler>() != null,
            5f,
            "pause menu did not open"
        );
        if (!stepOk)
            yield break;
        yield return Wait(0.5f);

        var page = FindAnyObjectByType<PauseMenuHandler>()
            .TransistionToPage<PauseMenuSettingsMenuPage>();
        var settings = page != null ? page.sharedSettingsMenu : null;
        if (settings == null || UISettingsFeature.TabsRef == null)
        {
            Fail("no settings menu, or ui.settings inactive");
            yield break;
        }
        yield return Wait(0.5f);

        var tabs = UISettingsFeature.TabsRef(settings);
        SettingsTABSButton vr = null;
        if (tabs != null)
        {
            foreach (var b in tabs.buttons)
            {
                if (b != null && b.category == VRTab.Category)
                    vr = b;
            }
        }
        if (vr == null)
        {
            Fail("no VR tab");
            yield break;
        }
        tabs.Select(vr);
        yield return Wait(1f);
    }

    /// <summary>Spawn every item in rows, one step each.</summary>
    IEnumerator Items()
    {
        var all = new List<Item>(SandboxActions.Items);
        var positions = new System.Numerics.Vector3[RowSize];
        for (int start = 0, row = 0; start < all.Count; start += RowSize, row++)
        {
            Safe("items.place", () => PlaceRow(positions));
            for (int i = 0; i < RowSize && start + i < all.Count; i++)
            {
                var item = all[start + i];
                var p = new Vector3(positions[i].X, positions[i].Y, positions[i].Z);
                yield return Run("item." + item.name, 10f, SpawnItem(item, p));
            }
            yield return Wait(1.5f);
            yield return Shot("03-items-" + row.ToString("000", CultureInfo.InvariantCulture));
            Safe("items.despawn", Despawn);
        }
    }

    /// <summary>Hand-use one item per category, then despawn it so a dropped item does not keep logging.</summary>
    IEnumerator Uses()
    {
        var all = new List<Item>(SandboxActions.Items);
        foreach (var fragment in UseItems)
        {
            var item = FindItem(all, fragment);
            if (item == null)
            {
                Skip("use." + fragment, "no item name contains it");
                continue;
            }
            // Setup (drop, give, settle) plus the item's own use time.
            yield return Run(
                "use." + item.name,
                10f + GameTiming.Budget(GameTiming.UseSeconds(item)),
                UseItem(item)
            );
            SelfTestInputPatch.HoldPrimary = false;
            Safe("use.despawn", Despawn);
        }
    }

    IEnumerator SpawnItem(Item item, Vector3 position)
    {
        if (!SandboxActions.SpawnItemAt(item, position))
        {
            Fail(SandboxActions.LastMessage);
            yield break;
        }
        var go = SandboxActions.LastSpawned;
        spawned.Add(go);
        yield return null;
        yield return null;
        if (go == null)
            Fail("destroyed right after spawning");
    }

    /// <summary>Give to the hand (<c>GameUtils.InstantiateAndGrab</c>), hold primary use, release, drop.</summary>
    IEnumerator UseItem(Item item)
    {
        yield return DropHeld();
        if (!SandboxActions.GiveItem(item))
        {
            Fail(SandboxActions.LastMessage);
            yield break;
        }
        yield return Until(() => Holding(item), 5f, "not in hand");
        if (!stepOk)
            yield break;
        var held = HeldItem();
        spawned.Add(held.gameObject);
        yield return Wait(0.5f);
        // Item.StartUsePrimary sets isUsingPrimary (Item.cs:849-855); an eaten or spent item leaves the hand.
        // Held through the item's own use time, so a slow cast gets to finish (finishedCast, Item.cs:883).
        bool started = false;
        float progress = 0f;
        float useTime = GameTiming.UseSeconds(held);
        SelfTestInputPatch.HoldPrimary = true;
        float end = Now + GameTiming.Budget(useTime);
        while (Now < end && !held.finishedCast)
        {
            if (held == null || HeldItem() != held)
            {
                started = true;
                break;
            }
            started |= held.isUsingPrimary || held.finishedCast;
            progress = Mathf.Max(progress, held.castProgress);
            yield return null;
        }
        SelfTestInputPatch.HoldPrimary = false;
        yield return Wait(1f);
        bool gone = held == null || HeldItem() != held;
        var note =
            $"primary use started {started}, cast progress {progress:0.00} of {useTime:0.0} s, left the hand {gone}";
        if (!started)
            Fail("primary use never started: " + note);
        else
            stepNote = note;
        // Eaten or spent items leave the hand on their own.
        yield return DropHeld();
    }

    IEnumerator DropHeld()
    {
        float end = Now + 3f;
        while (HeldItem() != null)
        {
            if (Now > end)
            {
                Fail("could not drop " + HeldItem().name);
                yield break;
            }
            SelfTestInputPatch.DropOnce = true;
            yield return Wait(0.3f);
        }
    }

    /// <summary>
    /// Board the plane: <c>AirportCheckInKiosk.StartGame(int)</c> (AirportCheckInKiosk.cs:117), which the boarding
    /// pass calls (BoardingPass.cs:228); it RPCs <c>LoadIslandMaster</c> (:135) to the master (us, offline), which
    /// picks the level from <c>MapBaker</c> and loads it through <c>BeginIslandLoadRPC</c> (:152).
    /// </summary>
    IEnumerator Level(string shot = "04-beach")
    {
        var kiosk = FindAnyObjectByType<AirportCheckInKiosk>();
        if (kiosk == null)
        {
            Fail("no AirportCheckInKiosk");
            yield break;
        }
        kiosk.StartGame(0);
        yield return Until(() => LoadingScreenHandler.loading, 20f, "level load did not start");
        if (!stepOk)
            yield break;
        yield return Until(
            () => !InScene("Airport") && Singleton<MapHandler>.Instance != null && Ready,
            280f,
            "level not ready"
        );
        if (!stepOk)
            yield break;
        yield return Wait(8f);
        yield return ShotWhenClear(shot, 15f);
    }

    IEnumerator Mobs()
    {
        yield return Run("05-mob-scan", 120f, ScanMobs());
        foreach (var entry in mobs)
        {
            if (entry.Style == SpawnStyle.Unsupported)
            {
                if (Deferred(entry))
                    continue;
                var body = ContextStep(entry);
                if (body == null)
                {
                    Skip("mob." + entry.Name, entry.Note);
                    continue;
                }
                yield return Run("mob." + entry.Name, 30f, Isolated(body));
                SelfTestInputPatch.HoldPrimary = false;
            }
            else
                yield return Run("mob." + entry.Name, 20f, Isolated(Mob(entry)));
            Safe("mob.despawn", Despawn);
            Safe("mob.leftovers", ClearLeftovers);
            Safe("mob.recover", Recover);
            yield return Wait(1f);
        }
    }

    IEnumerator ScanMobs()
    {
        MobCatalog.Refresh(deep: true);
        mobs.Clear();
        mobs.AddRange(MobCatalog.Entries);
        stepNote = mobs.Count.ToString(CultureInfo.InvariantCulture) + " entities";
        yield break;
    }

    IEnumerator Mob(MobEntry entry)
    {
        if (!SandboxActions.SpawnMob(entry))
        {
            Fail(SandboxActions.LastMessage);
            yield break;
        }
        var go = SandboxActions.LastSpawned;
        spawned.Add(go);
        yield return MobBehaviour(entry, go);
    }

    /// <summary>The level's BLIND overlay quad (Misc/BlindPost, IllegalScreenEffect.cs:20), found without the patch
    /// under test; null when the scene has none.</summary>
    static MeshRenderer BlindWashRenderer()
    {
        var effect = UnityEngine.Object.FindAnyObjectByType<IllegalScreenEffect>();
        return effect != null ? effect.GetComponent<MeshRenderer>() : null;
    }

    /// <summary>Read after this frame's Updates, so it is what renders: the game's white-out quad is on and not held
    /// off, or the haze replacing it went over <see cref="ComfortPolicy.MaxWashAlpha"/>.</summary>
    static bool BlindWashedOut(MeshRenderer wash) =>
        (wash != null && wash.enabled && !wash.forceRenderingOff)
        || Comfort.ComfortEventsRuntime.HazeAlpha > ComfortPolicy.MaxWashAlpha + 0.001f;

    /// <summary>
    /// vr.34: BLIND (Character.illegalStatusAction, Character.cs:186, handled by IllegalScreenEffect.AddStatus
    /// IllegalScreenEffect.cs:65) reads as blindness without washing the view out: the game's white quad is not
    /// drawn and the haze that replaces it peaks above zero but at most at the cap.
    /// </summary>
    IEnumerator BlindCheck()
    {
        var c = Character.localCharacter;
        if (c == null || c.illegalStatusAction == null)
        {
            Fail("no local character with an IllegalScreenEffect bound");
            yield break;
        }
        var wash = BlindWashRenderer();
        if (wash == null)
        {
            Fail("no BLIND overlay (IllegalScreenEffect) in the scene");
            yield break;
        }
        const float blindFor = 5f;
        float blindAt = Now;
        c.illegalStatusAction("BLIND", blindFor);
        float peak = 0f;
        float strength = 0f;
        bool washed = false;
        for (float until = Now + 3f; Now < until; )
        {
            peak = Mathf.Max(peak, Comfort.ComfortEventsRuntime.HazeAlpha);
            strength = Mathf.Max(strength, Comfort.ComfortEventsPatches.BlindWash.Strength);
            washed |= BlindWashedOut(wash);
            yield return null;
        }
        bool blind = c.data.isBlind;
        yield return Shot("vr-34-blind");
        stepNote = F(
            $"blind {blind}, game wash {strength:0.00}, haze peak {peak:0.00} (cap {ComfortPolicy.MaxWashAlpha:0.00})"
        );
        if (!blind)
            Fail("BLIND did not apply");
        else if (washed)
            Fail("the view was washed out");
        else if (peak < 0.1f)
            Fail("no haze: blindness does not show");
        // The status runs the duration given above; wait out what is left of it.
        yield return WaitFor(() => !c.data.isBlind, GameTiming.Budget(blindFor - (Now - blindAt)));
    }

    IEnumerator Jump(Segment segment, string shot = null)
    {
        if (!SandboxActions.JumpToSegment(segment))
        {
            Fail(SandboxActions.LastMessage);
            yield break;
        }
        yield return Wait(3f);
        yield return Until(() => Ready, 100f, "not ready after the jump");
        if (!stepOk)
            yield break;
        yield return Wait(3f);
        Recover();
        var now = Singleton<MapHandler>.Instance.GetCurrentSegment();
        // The game keeps the Peak in TheKiln's segment slot: JumpToSegmentLogic drops the index by one for Peak
        // (MapHandler.cs:889-892) and warps to respawnThePeak (MapHandler.cs:958), so GetCurrentSegment says TheKiln.
        var expected = segment == Segment.Peak ? Segment.TheKiln : segment;
        if (now != expected)
            Fail("in " + now + " after the jump");
        if (segment == Segment.Peak)
            yield return PeakGround();
        yield return Shot(shot ?? "06-segment-" + segment);
        yield return Landing();
    }

    /// <summary>The segment the loop would jump to is the current one: assert it instead of jumping.</summary>
    IEnumerator AlreadyIn(Segment segment)
    {
        var current = Singleton<MapHandler>.Instance.GetCurrentSegment();
        if (current != segment)
        {
            Fail("in " + current);
            yield break;
        }
        yield return Shot("06-segment-" + segment);
        yield return Landing();
        if (stepOk)
            stepNote = "already here; " + stepNote;
    }

    IEnumerator TimeSteps()
    {
        lastEarth = null;
        foreach (var hours in Times)
        {
            var id = "07-time-" + ((int)hours).ToString("00", CultureInfo.InvariantCulture);
            yield return Run(id, 15f, TimeOfDay(hours, id));
        }
    }

    Quaternion? lastEarth;

    /// <summary>07-time-*: <c>setTimeOfDay</c> (DayNightManager.cs:277) moves the clock, and <c>UpdateCycle</c>
    /// (:382) turns the sun's <c>earth</c> pivot (:400) and sets sun and moon light. Each time must land near its
    /// hour and turn the pivot away from the previous step's.</summary>
    IEnumerator TimeOfDay(float hours, string shot)
    {
        if (!SandboxActions.SetTimeOfDay(hours))
        {
            Fail(SandboxActions.LastMessage);
            yield break;
        }
        yield return Wait(2f);
        // Level ahead and 20 degrees up: horizon and sky in frame.
        var cam = ViewCamera();
        if (cam != null)
        {
            var ahead = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            yield return ShotAt(shot, cam.transform.position + ahead * 10f + Vector3.up * 3.6f);
        }
        else
            yield return Shot(shot);
        var dn = DayNightManager.instance;
        var earth = dn.earth.rotation;
        float turned = lastEarth.HasValue ? Quaternion.Angle(lastEarth.Value, earth) : -1f;
        lastEarth = earth;
        var note =
            $"time {dn.timeOfDay:0.00} h, sun turned {turned:0} deg from the last step, "
            + $"sun {(dn.sun.enabled ? "on" : "off")} {dn.sun.intensity:0.00}, moon {(dn.moon.enabled ? "on" : "off")} {dn.moon.intensity:0.00}";
        // The clock runs a day per dayLengthInMinutes (10 min): 2 s is about 0.1 h.
        if (Mathf.Abs(Mathf.DeltaAngle(dn.timeOfDay * 15f, hours * 15f)) > 0.5f * 15f)
            Fail("clock not at the set hour: " + note);
        else if (turned >= 0f && turned < 10f)
            Fail("sun did not move from the last time step: " + note);
        else
            stepNote = note;
    }

    // ---------------------------------------------------------------- step machinery

    IEnumerator Run(string id, float timeout, IEnumerator body)
    {
        stepOk = true;
        stepNote = null;
        failNote = null;
        stepSkip = null;
        report.Begin(id, Now);
        bool god = SandboxActions.GodMode;
        yield return Guard(body, Now + timeout, OnStepFailure);
        // A step that lifts god mode or holds a campfire light (vr.12, vr.26) and ends early leaves both as they were.
        if (god && !SandboxActions.GodMode)
            SandboxActions.SetGodMode(true);
        SelfTestCampfirePatch.Hold = false;
        // Name the one cause a step cannot see: no runtime focus pauses input and holds the fade (vr.00).
        if (VRSession.Active && !VRSession.HasFocus && (!stepOk || stepSkip != null))
        {
            var cause = $"runtime focus lost ({XRBootstrapDriver.FocusSource})";
            if (stepOk)
                stepSkip += "; " + cause;
            else
                failNote += "; " + cause;
        }
        if (stepOk && stepSkip != null)
            report.Skip(id, stepSkip);
        else if (stepOk)
            report.End(true, Now, stepNote);
        else
            report.End(
                false,
                Now,
                stepNote == null || stepNote == failNote ? failNote : failNote + "; " + stepNote
            );
        Record(report.Steps.Count - 1);
    }

    void OnStepFailure(Exception e)
    {
        if (e == null)
        {
            Fail("timeout");
            return;
        }
        Fail("threw");
        report.Exception($"{e.GetType().Name}: {e.Message} @ {FirstLine(e.StackTrace)}");
    }

    /// <summary>
    /// Steps a coroutine tree by hand, so an exception or a deadline ends only this tree; Unity would stop
    /// the whole script. <paramref name="fail"/> gets the exception, or null for the timeout.
    /// </summary>
    static IEnumerator Guard(IEnumerator root, float deadline, Action<Exception> fail)
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            if (Now > deadline)
            {
                fail(null);
                yield break;
            }
            var top = stack.Peek();
            bool more;
            object current = null;
            try
            {
                more = top.MoveNext();
                if (more)
                    current = top.Current;
            }
            catch (Exception e)
            {
                fail(e);
                yield break;
            }
            if (!more)
                stack.Pop();
            else if (current is IEnumerator nested)
                stack.Push(nested);
            else
                yield return current;
        }
    }

    IEnumerator Until(Func<bool> condition, float seconds, string what)
    {
        float end = Now + seconds;
        while (!condition())
        {
            if (Now > end)
            {
                Fail(what);
                yield break;
            }
            yield return null;
        }
    }

    // Realtime: the offline pause menu sets timeScale to 0 (PauseMenuHandler.cs:21).
    static WaitForSecondsRealtime Wait(float seconds) => new(seconds);

    /// <summary>Shoots once no fade is drawn and no loading room is up, so a frame never shows a transition's
    /// black (a warp, a takeover blink, the room's hand-off) instead of the scene. Notes it when the view never
    /// cleared.</summary>
    IEnumerator ShotWhenClear(string name, float seconds)
    {
        yield return WaitFor(() => !VRSession.Active || ViewClear, seconds);
        if (VRSession.Active && !ViewClear)
            Note(
                $"view still faded ({FadeView.Alpha:0.00}, loading room {LoadingSpace.RoomUp}) at the shot"
            );
        yield return Shot(name);
    }

    static bool ViewClear => FadeView.Alpha < 0.01f && !LoadingSpace.RoomUp;

    IEnumerator Shot(string name)
    {
        ScreenCapture.CaptureScreenshot(
            Path.Combine(shotDir, SelfTestReport.SafeName(name) + ".png"),
            1
        );
        // The capture happens at the end of the frame; give it one more to reach the disk.
        yield return null;
        yield return null;
    }

    void Fail(string note)
    {
        if (stepOk)
            failNote = note;
        stepOk = false;
    }

    /// <summary>Housekeeping between steps: an exception here becomes a failed step instead of aborting the run.</summary>
    void Safe(string id, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            report.Begin(id, Now);
            report.Exception($"{e.GetType().Name}: {e.Message} @ {FirstLine(e.StackTrace)}");
            report.End(false, Now, "threw");
            Record(report.Steps.Count - 1);
        }
    }

    void Skip(string id, string note)
    {
        report.Skip(id, note);
        Record(report.Steps.Count - 1);
    }

    void OnLog(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Exception)
            report.Exception(FirstLine(condition) + " @ " + FirstLine(stackTrace), stackTrace);
        else if (type is LogType.Error or LogType.Assert)
            report.Error(condition);
    }

    void Record(int index)
    {
        var line = report.Line(index);
        Plugin.Log.LogInfo("[selftest] " + line);
        try
        {
            File.AppendAllText(logPath, line + "\n");
        }
        catch (IOException) { }
    }

    void Abort(string reason)
    {
        Plugin.Log.LogError("[selftest] script aborted: " + reason);
        report.Begin("script", Now);
        report.Exception(reason);
        report.End(false, Now, "aborted");
        Finish();
    }

    /// <summary>Rewrite the log with every step, the totals and the <c>[compat]</c> states, then quit.</summary>
    void Finish()
    {
        if (finished)
            return;
        finished = true;
        SelfTestInputPatch.HoldPrimary = false;
        // Hold mode keeps the offline sandbox for live-input tests; SandboxActions drops it once online.
        if (!SelfTestFeature.Hold)
            SandboxActions.Forced = false;
        VR.VRInput.TestOverride = null;

        int skipped = report.Steps.Count - report.Passed - report.Failed;
        var sb = new StringBuilder(Header()).Append('\n');
        for (int i = 0; i < report.Steps.Count; i++)
            sb.Append(report.Line(i)).Append('\n');
        sb.Append(
            $"summary: {report.Passed} pass, {report.Failed} fail, {skipped} skip, {Now:0}s\n"
        );
        sb.Append($"vanilla-exceptions: {report.VanillaExceptions}\n");
        foreach (var f in FeatureManager.Features)
        {
            var reason = f.FailureReason == null ? "" : $" ({f.FailureReason})";
            sb.Append($"[compat] {f.Id, -24} {f.State}{reason}\n");
        }
        try
        {
            File.WriteAllText(logPath, sb.ToString());
        }
        catch (IOException e)
        {
            Plugin.Log.LogError("[selftest] cannot write the log: " + e.Message);
        }
        Plugin.Log.LogInfo(
            $"[selftest] done: {report.Passed} pass, {report.Failed} fail, {skipped} skip; quitting"
        );
        if (SelfTestFeature.Hold)
        {
            Plugin.Log.LogInfo($"[selftest] hold: game stays up, quits in {HoldMinutes:0} min");
            StartCoroutine(HoldQuit());
            return;
        }
        Quit();
    }

    static IEnumerator HoldQuit()
    {
        yield return new WaitForSecondsRealtime(HoldMinutes * 60f);
        Quit();
    }

    static void Quit()
    {
        Application.Quit();
        // Quit can hang under Proton after the summary is written; a plain thread still runs then.
        new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(5000);
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        })
        {
            IsBackground = true,
        }.Start();
    }

    // ---------------------------------------------------------------- helpers

    static string Header() =>
        $"PeakXR self-test {MyPluginInfo.PLUGIN_VERSION}, game {Application.version}, "
        + $"VR {VR.VRSession.Active}, {DateTime.UtcNow.ToString("u", CultureInfo.InvariantCulture)}";

    static bool InScene(string name) =>
        SceneManager.GetActiveScene().name.Contains(name, StringComparison.Ordinal);

    static string FirstLine(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        int nl = text.IndexOf('\n', StringComparison.Ordinal);
        return (nl >= 0 ? text.Substring(0, nl) : text).Trim();
    }

    static Item HeldItem()
    {
        var c = Character.localCharacter;
        return c != null && c.data != null ? c.data.currentItem : null;
    }

    static bool Holding(Item item)
    {
        var held = HeldItem();
        return held != null && held.itemID == item.itemID;
    }

    static Item FindItem(List<Item> items, string fragment)
    {
        var terms = NameFilter.Parse(fragment);
        foreach (var item in items)
        {
            if (NameFilter.Matches(item.name, terms))
                return item;
        }
        return null;
    }

    /// <summary>A row of slots 2.5 m ahead of the player, across the view.</summary>
    static void PlaceRow(System.Numerics.Vector3[] positions)
    {
        if (Character.localCharacter == null)
            return;
        var o = SandboxActions.FrontPoint(2.5f) + Vector3.up * 0.5f;
        var f =
            MainCamera.instance != null ? MainCamera.instance.transform.forward : Vector3.forward;
        SpawnLayout.Grid(
            positions,
            new System.Numerics.Vector3(o.x, o.y, o.z),
            new System.Numerics.Vector3(f.x, 0f, f.z),
            0.8f,
            RowSize
        );
    }

    void Despawn()
    {
        foreach (var go in spawned)
        {
            if (go == null)
                continue;
            try
            {
                var view = go.GetComponent<PhotonView>();
                if (view != null && (view.IsMine || PhotonNetwork.IsMasterClient))
                    PhotonNetwork.Destroy(go);
                else
                    Destroy(go);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[selftest] despawn {go.name}: {e.Message}");
            }
        }
        spawned.Clear();
    }

    /// <summary>Mobs can still kill or knock out through god mode (falls into the void, scripted grabs).</summary>
    static void Recover()
    {
        var c = Character.localCharacter;
        if (c != null && c.data != null && (c.data.dead || c.data.fullyPassedOut))
            SandboxActions.Revive();
    }

    static void ClosePause()
    {
        if (!GUIManager.InPauseMenu)
            return;
        // The game's own close (PauseMenuHandler.cs:58).
        var handler = FindAnyObjectByType<PauseMenuHandler>();
        if (handler != null)
            handler.gameObject.SetActive(false);
    }
}
