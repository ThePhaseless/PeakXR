using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Social;
using PeakXR.UI.Affordances;
using PeakXR.VR;
using Photon.Pun;
using Steamworks;
using UnityEngine;
using Zorro.Core;
using Vector3 = UnityEngine.Vector3;

namespace PeakXR.Sandbox;

/// <summary>
/// The <c>-peakxr-mptest</c> script (client A of the two-client test, docs/testing.md). It joins the lobby a vanilla
/// peer hosts, then plays a VR sequence with the scripted hands: walk, emote, ping, board, open a suitcase, grab,
/// drop, pick up, throw, eat, holster and draw, climb, two-hand hold, feed the host, then the social steps (a refused
/// feed and reach, help-up, kick, carry, the host's pack, pal jump, a far campfire, spectating). Every step ends with
/// a line in <c>a-checkpoints.jsonl</c>, which the observer answers with its own snapshot under the same name. Nothing
/// here spawns or uses the sandbox, and every action is one the controllers can make, with two exceptions that use
/// the game's own commands on a character the issuer owns: the host is asked (<see cref="MpRequest"/>) to pass out,
/// get webbed, wake or step onto A, and A passes itself out (Character.PassOut) to spectate and wakes after.
/// </summary>
internal sealed partial class SelfTestRunner
{
    const float MpMinutes = 30f;

    /// <summary>Hold still after each checkpoint so the observer's snapshot (polled every 0.25 s) sees the same state.</summary>
    const float CheckpointHold = 2f;

    const float NearItems = 40f;

    /// <summary>How far (m) from the head a scripted hand may reach toward a friend: about a real arm.</summary>
    const float MpArmReach = 0.85f;

    /// <summary>Set by <see cref="MpTestFeature"/>; non-null switches this runner to the mp script.</summary>
    internal static string MpDirectory;

    readonly Dictionary<string, string> cpExtra = new(StringComparer.Ordinal);
    string cpPath;
    int cpItemId = -1;
    Item mpItem;

    static double Unix => (DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds;

    static bool MpReady =>
        !LoadingScreenHandler.loading
        && Character.localCharacter != null
        && PhotonNetwork.InRoom
        && !PhotonNetwork.OfflineMode;

    void StartMp()
    {
        shotDir = MpDirectory;
        logPath = Path.Combine(MpDirectory, "a-mptest.log");
        cpPath = Path.Combine(MpDirectory, "a-checkpoints.jsonl");
        try
        {
            Directory.CreateDirectory(MpDirectory);
            foreach (var old in Directory.GetFiles(MpDirectory, "a-*"))
                File.Delete(old);
            File.WriteAllText(logPath, Header() + "\n");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Plugin.Log.LogError($"[mptest] cannot prepare {MpDirectory}: {e.Message}");
        }
        Plugin.Log.LogInfo($"[mptest] started, results in {MpDirectory}");
        StartCoroutine(
            Guard(
                MpScript(),
                Now + MpMinutes * 60f,
                e =>
                {
                    Plugin.Log.LogError("[mptest] script aborted: " + (e?.ToString() ?? "timeout"));
                    MpFinish(e == null ? "timeout" : "aborted: " + e.Message);
                }
            )
        );
    }

    IEnumerator MpScript()
    {
        yield return Run("mp.title", 180f, Title());
        if (VRRun)
            yield return Run("vr.00-session", 60f, Session());
        // Scripted hands for the whole run; nobody holds the controllers.
        if (VRSession.Active)
            BeginHands();

        yield return MpStep("join", 300f, false, MpJoin());
        if (!MpReady || Host() == null)
        {
            MpFinish("not in the host's room");
            yield break;
        }
        yield return MpStep("walk-to-host", 60f, true, WalkTo(HostCenter, 1.5f, true));
        yield return MpStep("emote", 20f, true, EmoteStep());
        yield return MpStep("ping", 15f, true, PingStep());
        // A host-owned scene button refused for this non-host (WP5 body, SelfTestRunner.World.cs); SKIP when none.
        yield return MpStep("scene-button", 60f, true, MpSceneButton());
        yield return MpStep("board", 400f, false, Board());
        if (!MpReady || Singleton<MapHandler>.Instance == null)
        {
            MpFinish("no level after boarding");
            yield break;
        }
        yield return MpStep("regroup", 60f, true, WalkTo(HostCenter, 1.5f, true));
        yield return MpStep("luggage", 300f, true, OpenLuggage());
        yield return MpStep("grab", 40f, true, GrabNearest(food: false));
        yield return MpStep("drop", 15f, true, DropByButton());
        yield return MpStep("pickup", 40f, true, PickUpAgain());
        yield return MpStep("throw", 60f, true, ThrowToHost());
        yield return MpStep("eat", 150f, true, Eat());
        yield return MpStep("holster", 60f, true, Holster());
        yield return MpStep("climb", 120f, true, MpClimb());
        yield return MpStep("two-hand", 60f, true, MpTwoHand());
        yield return MpStep("feed", 180f, true, Feed());
        yield return MpSocialSteps();
        MpFinish("script done");
    }

    /// <summary>One checkpoint: the step, then its line in a-checkpoints.jsonl, a screenshot, a short still hold and
    /// the hands back at rest. A passed step keeps its grips, so a held item stays held; a failed or skipped one opens
    /// both, so a grip left closed on a failure cannot hold the next step back.</summary>
    IEnumerator MpStep(string id, float timeout, bool vr, IEnumerator body)
    {
        cpExtra.Clear();
        cpItemId = -1;
        if (vr && !VRSession.Active)
        {
            Skip(id, "VR not active");
            WriteCheckpoint(id, "skip", "VR not active");
            yield break;
        }
        yield return Run(id, timeout, vr && MpReady ? MpSettled(body) : body);
        var r = report.Steps[report.Steps.Count - 1];
        WriteCheckpoint(
            id,
            r.Skipped ? "skip"
                : r.Passed ? "pass"
                : "fail",
            r.Note
        );
        yield return Shot("a-" + id);
        yield return Wait(CheckpointHold);
        if (VRInput.TestOverride == applyHands)
            yield return Guard(r.Passed ? MpRest() : HandsRelease(), Now + 3f, _ => { });
    }

    /// <summary>
    /// The online counterpart of the self-test's <see cref="Settle"/>, without warps (nothing here warps online): A
    /// awake (the game's own revive on its own character), pause closed, buttons and sticks at rest, any head dip
    /// undone, then the rig recentred and the body grounded, under its own control (not ragdolled or climbing) and
    /// still for <see cref="StillWindow.Required"/>. A body that never settles fails the step with the reason, so a step never
    /// starts on a body a previous step left falling, sliding or in the detached ragdoll view (MP run 7: holster and
    /// climb ran with the view 2.9 m behind the torso).
    /// </summary>
    IEnumerator MpSettled(IEnumerator body)
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        if (c == null || rig == null)
        {
            Fail("not settled: no character or rig");
            yield break;
        }
        if (c.data.dead || c.data.fullyPassedOut)
        {
            Wake(c);
            yield return WaitFor(() => !c.data.dead && !c.data.fullyPassedOut, 5f);
        }
        ClosePause();
        Calm(ref scriptLeft);
        Calm(ref scriptRight);
        if (VRInput.TestOverride != null && VRInput.TestOverride != applyHands)
            VRInput.TestOverride = applyHands;
        // A climb left on: let go, the body drops to its feet.
        if (c.data.isClimbingAnything)
        {
            scriptLeft.Grip = scriptRight.Grip = 0f;
            yield return WaitFor(() => !c.data.isClimbingAnything, 3f);
        }
        // Hands back to rest in front of the head as it is now (grips kept, so a held item stays held).
        yield return MpRest();
        // Sloped ground keeps a standing body sliding; offline the settle warps to a flat home spot, online A walks.
        yield return MpFlatGround(c);
        yield return SettleRecenter(rig);
        if (!stepOk)
            yield break;
        yield return MpStandStill(c);
        if (mpStill)
        {
            yield return body;
            yield break;
        }
        // Not still: a body that is down, ragdolled, airborne or climbing would make the step meaningless, so it
        // fails; a body that only drifts starts the step anyway with the reason in the note (MP run 7 passed 19
        // steps with no settle at all; the settle must not become the failure).
        var d = c.data;
        if (!d.isGrounded || d.currentRagdollControll < 0.9f || d.isClimbingAnything || d.passedOut)
        {
            Fail("not settled: " + mpStillNote);
            yield break;
        }
        Note("started unsettled: " + mpStillNote);
        yield return body;
    }

    bool mpStill;
    string mpStillNote;

    /// <summary>Online stillness: the offline settle's own rule (<see cref="StillTick"/>), up to 6 s.</summary>
    IEnumerator MpStandStill(Character c)
    {
        mpStill = false;
        stillWindow.Reset();
        System.Array.Clear(blocked, 0, blocked.Length);
        standFrames = 0;
        hipPeak = 0f;
        float end = Now + 6f;
        string why = "never checked";
        while (Now < end)
        {
            if (StillTick(c, out var now))
            {
                mpStill = true;
                yield break;
            }
            if (now != null)
                why = now;
            yield return null;
        }
        mpStillNote = F(
            $"{why}; hip net travel {stillWindow.Drift:0.000} m in the last window, still {stillWindow.StillFor:0.00} s, speed peak {hipPeak:0.00} m/s, at {c.Center}"
        );
    }

    /// <summary>Steepest ground (deg) the MP settle stands on; past it A walks to flatter ground nearby.</summary>
    const float MpFlatSlope = 15f;

    /// <summary>
    /// When the ground under A slopes past <see cref="MpFlatSlope"/>, walks to the nearest flatter, open spot found
    /// 1.5 or 3 m around A (a terrain ray down, the normal's tilt, nothing at chest height). Nothing found: stays.
    /// </summary>
    IEnumerator MpFlatGround(Character c)
    {
        if (Slope(c.Center) <= MpFlatSlope)
            yield break;
        Vector3? best = null;
        float bestSq = float.MaxValue;
        foreach (float r in new[] { 1.5f, 3f })
        {
            for (int i = 0; i < 8; i++)
            {
                var dir = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward;
                var probe = c.Center + dir * r;
                if (
                    Slope(probe) > MpFlatSlope
                    || Physics.CheckSphere(
                        probe,
                        0.3f,
                        HelperFunctions.terrainMapMask,
                        QueryTriggerInteraction.Ignore
                    )
                )
                    continue;
                float sq = (probe - c.Center).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = probe;
                }
            }
            if (best != null)
                break;
        }
        if (best is not { } spot)
        {
            Note($"ground slopes {Slope(c.Center):0} deg and no flat spot within 3 m");
            yield break;
        }
        yield return WalkTo(() => spot, 0.3f, false);
    }

    /// <summary>Tilt (deg) of the terrain under <paramref name="from"/>; 90 with no ground within 3 m.</summary>
    static float Slope(Vector3 from) =>
        Physics.Raycast(
            from,
            Vector3.down,
            out var hit,
            3f,
            HelperFunctions.terrainMapMask,
            QueryTriggerInteraction.Ignore
        )
            ? Vector3.Angle(hit.normal, Vector3.up)
            : 90f;

    void WriteCheckpoint(string id, string status, string note)
    {
        var c = Character.localCharacter;
        var held = HeldItem();
        var j = new JsonLine()
            .Str("name", id)
            .Num("t", Unix)
            .Str("status", status)
            .Str("note", note);
        if (c != null)
        {
            j.Vec("pos", c.Center.x, c.Center.y, c.Center.z)
                .Vec("head", c.Head.x, c.Head.y, c.Head.z)
                .Raw(
                    "look",
                    "["
                        + c.data.lookValues.x.ToString("0.#", CultureInfo.InvariantCulture)
                        + ","
                        + c.data.lookValues.y.ToString("0.#", CultureInfo.InvariantCulture)
                        + "]"
                );
            MpPoseFields(j, c);
        }
        j.Str("item", held != null ? held.name : null)
            .Num("heldId", held != null ? held.itemID : -1)
            .Num(
                "itemId",
                cpItemId >= 0 ? cpItemId
                    : held != null ? held.itemID
                    : -1
            )
            .Str("scene", UnityEngine.SceneManagement.SceneManager.GetActiveScene().name)
            .Num("sent151", MpSendProbe.Sent.TryGetValue(151, out int n) ? n : 0);
        foreach (var kv in cpExtra)
            j.Raw(kv.Key, kv.Value);
        try
        {
            File.AppendAllText(cpPath, j + "\n");
        }
        catch (IOException e)
        {
            Plugin.Log.LogWarning("[mptest] checkpoint write failed: " + e.Message);
        }
    }

    /// <summary>
    /// A's tracked head, its wrist targets (<see cref="AvatarPoseDriver.HandTarget"/>, the bone the tracked hand asks
    /// for) and its own avatar's head and hand bones, in its body frame (<see cref="MpPoseFrame"/>). A modded B writes
    /// A's avatar bones in the same frame, and mp-check --modded-b compares them. Only while the arms follow the pose.
    /// </summary>
    static void MpPoseFields(JsonLine j, Character c)
    {
        var driver = AvatarPoseDriver.Of(c);
        var rig = c.refs.hip != null ? c.refs.hip.Rig : null;
        var parts = c.refs.ragdoll != null ? c.refs.ragdoll.partDict : null;
        if (
            driver == null
            || rig == null
            || parts == null
            || !driver.HasFreshPose
            || driver.ArmsSuspended
            || !AvatarPoseDriver.ArmsTracked(c)
        )
            return;
        var root = new System.Numerics.Vector3(rig.position.x, rig.position.y, rig.position.z);
        float yaw = c.data.lookValues.x;
        void Put(string key, Vector3 w)
        {
            var v = MpPoseFrame.ToFrame(new System.Numerics.Vector3(w.x, w.y, w.z), root, yaw);
            j.Vec(key, v.X, v.Y, v.Z);
        }
        if (driver.Pose.HasHead)
            Put("poseHead", driver.Pose.HeadPosition);
        if (driver.HandTarget(true, out var left))
            Put("poseL", left);
        if (driver.HandTarget(false, out var right))
            Put("poseR", right);
        if (parts.TryGetValue(BodypartType.Head, out var head))
            Put("boneHead", head.Rig.position);
        if (parts.TryGetValue(BodypartType.Hand_L, out var handL))
            Put("boneL", handL.Rig.position);
        if (parts.TryGetValue(BodypartType.Hand_R, out var handR))
            Put("boneR", handR.Rig.position);
        // Which hands hold the item (AvatarPoseDriver.Apply's holding test): their bone, not the tracked pose, is what
        // A's own client shows, since a held item follows the controller free of a hand that cannot reach it.
        bool item = c.data.currentItem != null;
        j.Bool("holdL", item && (driver.Pose.TwoHandHold || driver.Pose.ItemInLeftHand))
            .Bool("holdR", item && (driver.Pose.TwoHandHold || !driver.Pose.ItemInLeftHand));
    }

    void Extra(string key, Vector3 v) =>
        cpExtra[key] = "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "]";

    void Extra(string key, string text) =>
        cpExtra[key] = new JsonLine().Str("s", text).ToString()[5..^1];

    void Extra(string key, bool value) => cpExtra[key] = value ? "true" : "false";

    static string F(float f) => f.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Write <c>a-done.json</c> (the observer's stop signal) with the send counts, then the usual log and quit.</summary>
    void MpFinish(string reason)
    {
        if (finished)
            return;
        var sent = new StringBuilder("{");
        foreach (var kv in MpSendProbe.Sent)
            sent.Append(sent.Length > 1 ? "," : "")
                .Append('"')
                .Append(kv.Key)
                .Append("\":")
                .Append(kv.Value);
        int skipped = report.Steps.Count - report.Passed - report.Failed;
        var done = new JsonLine()
            .Num("t", Unix)
            .Str("reason", reason)
            .Num("pass", report.Passed)
            .Num("fail", report.Failed)
            .Num("skip", skipped)
            .Bool("vr", VRSession.Active)
            .Num("sent151", MpSendProbe.Sent.TryGetValue(151, out int n) ? n : 0)
            .Raw("sent", sent.Append('}').ToString());
        try
        {
            File.WriteAllText(Path.Combine(MpDirectory, "a-done.json"), done + "\n");
        }
        catch (IOException e)
        {
            Plugin.Log.LogError("[mptest] cannot write a-done.json: " + e.Message);
        }
        Plugin.Log.LogInfo("[mptest] " + reason);
        Finish();
    }

    // ---------------------------------------------------------------- steps

    /// <summary>
    /// Join by id the way the game does: <c>SteamLobbyHandler.TryJoinLobby</c> (SteamLobbyHandler.cs:374) requests the
    /// lobby data, compares <c>PeakVersion</c> (:306, :332-349, mismatch modal otherwise), joins (:357, :107), asks the
    /// host for the Photon room over lobby chat (:169, :246, :279-281), and NetworkConnector joins it
    /// (NetworkConnector.cs:158-170). Then it checks the version, the lobby and that the master is the host.
    /// </summary>
    IEnumerator MpJoin()
    {
        var path = Path.Combine(MpDirectory, "lobby.json");
        string line = null;
        ulong lobbyId = 0;
        float end = Now + 240f;
        while (
            (line = ReadText(path)) == null
            || !JsonLine.TryReadNumber(line, "time", out var t)
            || Unix - t > 30 * 60
            || !ulong.TryParse(
                JsonLine.ReadString(line, "lobbyId"),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out lobbyId
            )
        )
        {
            if (Now > end)
            {
                Fail("no fresh lobby.json in " + MpDirectory);
                yield break;
            }
            yield return Wait(1f);
        }
        var host = JsonLine.ReadString(line, "hostSteamId");
        // The host drops lobby chat from its own account (SteamLobbyHandler.cs:222-225), so the room ID would never come.
        if (SteamUser.GetSteamID().m_SteamID.ToString(CultureInfo.InvariantCulture) == host)
        {
            Fail(
                $"this client and the host share Steam account {host}; restart this side's Steam client"
            );
            yield break;
        }
        var lobby = new CSteamID(lobbyId);
        var lobbies = GameHandler.GetService<SteamLobbyHandler>();
        lobbies.TryJoinLobby(lobby);
        yield return Until(
            () => InScene("Airport") && MpReady && Host() != null,
            240f,
            "not in the host's Airport room"
        );

        var theirs = SteamMatchmaking.GetLobbyData(lobby, "PeakVersion");
        var ours = new BuildVersion(Application.version).ToMatchmaking();
        bool inLobby = lobbies.InSteamLobby(out var current) && current == lobby;
        var master = PhotonNetwork.MasterClient?.UserId;
        Extra("lobbyId", lobbyId.ToString(CultureInfo.InvariantCulture));
        Extra("lobbyVersion", theirs);
        Extra("localVersion", ours);
        Extra("versionOk", !string.IsNullOrEmpty(theirs) && theirs == ours);
        Extra("inLobby", inLobby);
        Extra("masterIsHost", master != null && master == host);
        Extra("room", PhotonNetwork.CurrentRoom?.Name);
        cpExtra["players"] = (PhotonNetwork.CurrentRoom?.PlayerCount ?? 0).ToString(
            CultureInfo.InvariantCulture
        );
        if (!stepOk)
            yield break;
        var note =
            $"lobby {lobbyId}, version {theirs} vs ours {ours}, in lobby {inLobby}, master {master} host {host}";
        if (theirs != ours)
            Fail("version mismatch: " + note);
        else if (!inLobby)
            Fail("not in the Steam lobby: " + note);
        else if (master != host)
            Fail("the master is not the host: " + note);
        else
            stepNote = note;
        yield return Wait(3f);
    }

    /// <summary>
    /// Finger-gun ping (Interact/Ping/PingInput.cs, src/PeakXR.Core/FingerGun.cs): the right hand closes its grip
    /// with the index straight, aims its aim pose at the ground in front of the host, then squeezes the trigger.
    /// </summary>
    IEnumerator PingStep()
    {
        var c = Character.localCharacter;
        var host = Host();
        var rig = VRRig.Instance;
        // Only an empty hand fires (PingInput.cs:42-52) and the Airport puts a passport in the right hand.
        bool left = HeldItem() != null && !LocalHold.ItemInLeftHand;
        var aim = host.Center + Flat(c.Center - host.Center) * 1.5f;
        if (
            Physics.Raycast(
                aim + Vector3.up * 2f,
                Vector3.down,
                out var ground,
                6f,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            )
        )
            aim = ground.point;
        MpPingProbe.Last = null;
        HeadFrame(out var head, out var forward, out var right);
        Script(left).Position = head + right * 0.2f + Vector3.down * 0.2f + forward * 0.3f;
        Script(left).TriggerTouch = false;
        Script(left).Trigger = 0f;
        yield return GripTo(left, 1f, 0.1f);
        for (float until = Now + 0.4f; Now < until; )
        {
            AimHand(rig, left, aim);
            yield return null;
        }
        Script(left).TriggerTouch = true;
        Script(left).Trigger = 1f;
        yield return Wait(0.15f);
        Script(left).Trigger = 0f;
        yield return WaitFor(() => MpPingProbe.Last != null, 2f);
        yield return GripTo(left, 0f, 0.1f);
        if (MpPingProbe.Last is not { } point)
        {
            Fail("no ping received");
            yield break;
        }
        Extra("ping", point);
        stepNote = $"ping {Vector3.Distance(point, aim):0.00} m from the aim point";
    }

    /// <summary>Turns the hand so its aim pose (<c>VRRig.Aim</c>, the ping ray's origin) looks at a world point.</summary>
    void AimHand(VRRig rig, bool left, Vector3 world)
    {
        var hand = rig.Hand(left);
        var aim = rig.Aim(left);
        if (hand == null || aim == null)
            return;
        var offset = Quaternion.Inverse(hand.rotation) * aim.rotation;
        var want =
            Quaternion.LookRotation(world - aim.position, Vector3.up) * Quaternion.Inverse(offset);
        Script(left).Rotation = Quaternion.Inverse(rig.Origin.rotation) * want;
    }

    /// <summary>
    /// The kiosk any player may open, as a VR player boards: walk up, grip while looking at it (the grip's interact
    /// holds the kiosk's cast, AirportCheckInKiosk.cs:94-112, which opens the boarding pass), then the laser presses
    /// the pass's Start (BoardingPass.startGameButton, :111, :228), and AirportCheckInKiosk.StartGame (:117-122) asks
    /// the master, here the vanilla host, to load the island (:135-152). Without VR, or when the hand path does not
    /// start the load, StartGame is called directly and the note names that shortcut.
    /// </summary>
    IEnumerator Board()
    {
        var kiosk = FindAnyObjectByType<AirportCheckInKiosk>();
        if (kiosk == null)
        {
            Fail("no AirportCheckInKiosk");
            yield break;
        }
        string shortcut = "no VR";
        if (VRSession.Active)
        {
            // Inside the game's 2 m interact reach (Interaction.cs:7).
            yield return MpWalkAround(() => kiosk.transform.position, 1.6f, 15f);
            yield return BoardByHand(kiosk, s => shortcut = s);
        }
        if (!LoadingScreenHandler.loading)
        {
            // shortcut: the island load is asked for directly; the note says which part of the hand path failed.
            Note("shortcut: StartGame called directly, " + shortcut);
            kiosk.StartGame(0);
        }
        yield return Until(() => LoadingScreenHandler.loading, 30f, "level load did not start");
        if (!stepOk)
            yield break;
        yield return Until(
            () => !InScene("Airport") && Singleton<MapHandler>.Instance != null && MpReady,
            300f,
            "level not ready"
        );
        if (!stepOk)
            yield break;
        var c = Character.localCharacter;
        yield return Wait(5f);
        yield return Until(
            () => c.data.fallSeconds <= 0f && !c.data.passedOut && c.data.isGrounded,
            40f,
            "still down after the beach wake-up"
        );
        yield return Wait(2f);
        string scene = "on " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        stepNote = stepNote != null ? scene + "; " + stepNote : scene;
    }

    /// <summary>The player's path through the kiosk (see <see cref="Board"/>): the free hand's grip opens the pass, the
    /// laser presses Start. <paramref name="failed"/> gets why when the load did not start.</summary>
    IEnumerator BoardByHand(AirportCheckInKiosk kiosk, Action<string> failed)
    {
        var pass = GUIManager.instance != null ? GUIManager.instance.boardingPass : null;
        if (pass == null)
        {
            failed("no boarding pass");
            yield break;
        }
        // The airport hands A its passport: the other hand grips.
        bool left = HeldItem() != null && !LocalHold.ItemInLeftHand;
        // On foot to a clear spot at the kiosk (the solo ToKiosk's stand-off, walked instead of warped), then the hand
        // on the kiosk's near face at chest height (MP run 14: the grip closed at rest 5.1 m away and missed it).
        string why = null;
        yield return MpToKioskFace(kiosk, left, s => why = s);
        if (why != null)
        {
            failed(why);
            yield break;
        }
        float cast = GameTiming.InteractSeconds(kiosk, Character.localCharacter);
        yield return GripTo(left, 1f, 0.1f);
        yield return WaitFor(() => pass.isOpen, GameTiming.Budget(cast, 1f));
        yield return GripTo(left, 0f, 0.1f);
        if (!pass.isOpen)
        {
            failed(
                F(
                    $"the grip did not open the boarding pass ({Vector3.Distance(Character.localCharacter.Center, kiosk.Center()):0.0} m from the kiosk)"
                )
            );
            yield break;
        }
        // BoardingPass.OnOpen fades it in over 0.5 s (BoardingPass.cs:147).
        yield return Wait(0.8f);
        yield return LaserPress(pass.startGameButton);
        yield return WaitFor(() => LoadingScreenHandler.loading, 3f);
        if (!LoadingScreenHandler.loading)
            failed("the laser press on Start did not start the load");
        else
            Note("boarded by hand: grip opened the pass, laser pressed Start");
    }

    /// <summary>
    /// Walks to the nearest clear spot around the kiosk at the solo steps' stand-off (<see cref="KioskStandOff"/>
    /// past its bounds on eight headings, the floor found down from the kiosk's top so never the roof), steps in until
    /// the near face at chest height is 0.5 m ahead, and puts the <paramref name="left"/> hand on it. Each walk is
    /// bounded; <paramref name="failed"/> gets why when no spot or face is reached.
    /// </summary>
    IEnumerator MpToKioskFace(AirportCheckInKiosk kiosk, bool left, Action<string> failed)
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        PeakXR.UI.Lobby.KioskDock.Measure(kiosk.transform, out var centre, out var top);
        var bounds = KioskBounds(kiosk);
        var forward = Flat(kiosk.transform.forward);
        var spots = new List<Vector3>();
        for (int i = 0; i < 8; i++)
        {
            var dir = Quaternion.Euler(0f, i * 45f, 0f) * forward;
            float reach =
                Mathf.Abs(dir.x) * bounds.extents.x
                + Mathf.Abs(dir.z) * bounds.extents.z
                + KioskStandOff;
            var spot = new Vector3(bounds.center.x, top, bounds.center.z) + dir * reach;
            if (
                Physics.Raycast(
                    spot,
                    Vector3.down,
                    out var floor,
                    top - bounds.min.y + 1f,
                    HelperFunctions.terrainMapMask,
                    QueryTriggerInteraction.Ignore
                )
            )
                spots.Add(floor.point);
        }
        spots.Sort((a, b) => FlatDistance(a, c.Center).CompareTo(FlatDistance(b, c.Center)));
        bool there = false;
        for (int i = 0; i < spots.Count && i < 3 && !there; i++)
        {
            var spot = spots[i];
            yield return MpWalkAround(() => spot, 0.25f, 12f);
            there = FlatDistance(c.Center, spot) <= 0.45f;
        }
        if (!there)
        {
            failed(
                F(
                    $"no spot at the kiosk reached on foot ({FlatDistance(c.Center, bounds.center):0.0} m from it)"
                )
            );
            yield break;
        }
        Face(centre);
        yield return Wait(0.5f);
        var eye = rig.Head.position;
        float y = Mathf.Min(eye.y - 0.35f, top - 0.1f, bounds.max.y - 0.1f);
        var from = new Vector3(eye.x, y, eye.z);
        var toward = new Vector3(bounds.center.x, y, bounds.center.z) - from;
        if (
            toward.sqrMagnitude < 1e-4f
            || !Physics.Raycast(
                from,
                toward.normalized,
                out var face,
                toward.magnitude + bounds.extents.magnitude,
                HelperFunctions.AllPhysicalExceptCharacter,
                QueryTriggerInteraction.Ignore
            )
            || face.collider.GetComponentInParent<AirportCheckInKiosk>() != kiosk
        )
        {
            failed("no kiosk face at hand height from the stand spot");
            yield break;
        }
        var touch = face.point + face.normal * 0.02f;
        var gap = touch - eye;
        gap.y = 0f;
        if (gap.magnitude > 0.6f)
        {
            // Step in, as a player does when the top overhangs the face (the solo step's run 51 note).
            var to = c.Center + gap.normalized * (gap.magnitude - 0.5f);
            yield return MpWalkBounded(() => to, 0.15f, 3f);
            Face(centre);
            yield return Wait(0.3f);
        }
        yield return MoveTo(left, () => touch, 0.5f);
        if (OverReach(left) is { } far)
            failed(far);
    }

    /// <summary>Lifts suitcase lids (<see cref="LiftLid"/>) until enough food and other items lie around, trying at
    /// most six cases. A case the walk cannot reach (terrain, another case in the way) is passed over for the next.</summary>
    IEnumerator OpenLuggage()
    {
        var c = Character.localCharacter;
        var tried = new HashSet<Luggage>();
        int opened = 0;
        int unreached = 0;
        while (tried.Count < 6 && !EnoughItems())
        {
            var lug = Nearest<Luggage>(
                c.Center,
                l => !l.IsOpen && l is not RespawnChest && !tried.Contains(l)
            );
            if (lug == null || (lug.Center() - c.Center).sqrMagnitude > 80f * 80f)
                break;
            tried.Add(lug);
            // The SP step stands 0.8 m from the centre; online the case's own collider can stop the body farther out
            // than that, so the reach is measured from its solid surface.
            var solid = SolidColliders(lug);
            var centre = lug.Center();
            yield return WalkTo(
                () => SolidPoint(solid, centre, Character.localCharacter.Center),
                0.5f,
                false
            );
            var near = SolidPoint(solid, centre, c.Center) - c.Center;
            near.y = 0f;
            if (near.magnitude > 0.6f)
            {
                unreached++;
                continue;
            }
            Face(lug.Center());
            yield return Wait(0.4f);
            yield return LiftLid(lug);
            if (!stepOk || stepSkip != null)
                yield break;
            opened++;
            yield return Wait(2.5f);
            yield return MpRest();
        }
        int food = CountNear(true);
        int other = CountNear(false);
        var note =
            $"opened {opened} suitcase(s), {unreached} out of reach; {food} food and {other} other items within {NearItems:0} m";
        if (food + other == 0)
            Fail(note);
        else
            stepNote = note;
    }

    IEnumerator GrabNearest(bool food)
    {
        yield return FreeHands();
        if (!stepOk)
            yield break;
        var item = NearestGround(i => IsFood(i) == food) ?? NearestGround(_ => true);
        yield return GrabWorld(item);
        mpItem = HeldItem();
    }

    /// <summary>Drop through <c>Interaction.DropButton</c> (default DominantSecondary): a tap is the game's drop with no
    /// charge (CharacterItems.cs:252 DoDropping, :385 DropItemRpc).</summary>
    IEnumerator DropByButton()
    {
        var held = HeldItem();
        if (held == null)
        {
            Fail("nothing in hand");
            yield break;
        }
        ushort id = held.itemID;
        cpItemId = id;
        SnapshotItems();
        yield return TapDrop();
        yield return Until(() => HeldItem() == null, 2f, "the drop button did not drop it");
        if (!stepOk)
            yield break;
        Item dropped = null;
        yield return WaitFor(() => (dropped = NewItem(id)) != null, 2f);
        if (dropped == null)
        {
            Fail("no dropped item in the world");
            yield break;
        }
        yield return Settle(dropped);
        mpItem = dropped;
        if (dropped == null)
        {
            Fail("dropped item vanished");
            yield break;
        }
        Extra("landing", dropped.Center());
        stepNote = $"dropped {dropped.name}";
    }

    IEnumerator PickUpAgain()
    {
        var item = OnGround(mpItem) ?? NearestGround(_ => true);
        yield return GrabWorld(item);
        mpItem = HeldItem();
    }

    /// <summary>Faces the host and throws the held item at them (<see cref="ThrowHeld"/>).</summary>
    IEnumerator ThrowToHost()
    {
        var held = HeldItem();
        if (held == null)
        {
            Fail("nothing in hand");
            yield break;
        }
        cpItemId = held.itemID;
        Face(HostCenter());
        yield return Wait(0.5f);
        var from = Character.localCharacter.Center;
        var toHost = HelperFunctions.ZeroY(HostCenter() - from);
        yield return ThrowHeld();
        if (lastLanding is { } landed)
        {
            Extra("landing", landed);
            var host = Host();
            if (host != null)
                cpExtra["landingToHost"] = F(Vector3.Distance(landed, host.Center));
            mpItem = NearestGround(i => i.itemID == cpItemId, landed);
            // The throw goes where the player faced, toward the host: a release that dropped it at the feet or sent it
            // off to the side would land the same way on B (mp-check checks B's copy against A's landing).
            var flight = HelperFunctions.ZeroY(landed - from);
            float off = Vector3.Angle(flight, toHost);
            if (!stepOk)
                yield break;
            if (flight.magnitude < MpThrowMinFlight || off > MpThrowMaxOff)
                Fail(
                    F(
                        $"landed {flight.magnitude:0.0} m out, {off:0}° off the line to the host (want {MpThrowMinFlight:0} m or more within {MpThrowMaxOff:0}°)"
                    )
                );
            else
                Note(F($"{stepNote ?? "thrown"}; {off:0}° off the line to the host"));
        }
    }

    /// <summary>A throw toward the host lands at least this far (m, flat) from where A stood: a drop at the feet is not a
    /// throw. The host stands about 1.5 m away (walk-to-host), so the item lands at or past them; MP run 13: 5.5 m.</summary>
    const float MpThrowMinFlight = 1.5f;

    /// <summary>How far (degrees) the landing may sit off the line from A to the host (a hand arc and a bounce); not yet
    /// measured over runs.</summary>
    const float MpThrowMaxOff = 35f;

    IEnumerator Eat()
    {
        yield return FreeHands();
        if (!stepOk)
            yield break;
        yield return FetchFood(IsFood);
        if (!stepOk || stepSkip != null)
            yield break;
        cpItemId = HeldItem().itemID;
        yield return EatHeld();
    }

    IEnumerator Holster()
    {
        if (HeldItem() == null)
            yield return GrabReachable(_ => true, OnGround(mpItem));
        if (!stepOk || stepSkip != null)
            yield break;
        if (HeldItem() == null)
        {
            Fail("nothing to holster");
            yield break;
        }
        yield return HolsterAndDraw();
    }

    /// <summary>vr.15's climb at the nearest steep face, reached on foot instead of by warp.</summary>
    IEnumerator MpClimb()
    {
        yield return FreeHands();
        if (!stepOk)
            yield break;
        var c = Character.localCharacter;
        // On foot, so only walls near the player, nearest first; a wall the walk cannot reach (MP run 11: stuck 1.9 m
        // short) gives way to the next. Each try is bounded so all of them fit the step's budget.
        var walls = MpWalls(c);
        RaycastHit wall = default;
        bool reached = false;
        int tried = 0;
        foreach (var (face, stand) in walls)
        {
            if (tried++ >= MpWallTries)
                break;
            yield return MpWalkBounded(() => stand, 0.4f, MpWallWalkSeconds);
            var miss = stand - c.Center;
            miss.y = 0f;
            if (miss.magnitude <= 0.6f)
            {
                wall = face;
                reached = true;
                break;
            }
        }
        if (!reached)
        {
            stepSkip =
                walls.Count == 0
                    ? "no climbable wall with walkable ground in front within 40 m"
                    : $"none of the {tried} nearest climbable walls was reachable on foot";
            yield break;
        }
        var rig = VRRig.Instance;
        rig.RotateYaw(Vector3.SignedAngle(Flat(rig.Head.forward), -Flat(wall.normal), Vector3.up));
        yield return Wait(0.6f);
        yield return ClimbWall(wall);
        Script(VRInput.LeftHanded).Grip = 0f;
        yield return WaitFor(() => !c.data.isClimbing, 2f);
        yield return Wait(1f);
    }

    /// <summary>Walls the climb step tries, and how long each walk may take: 4 x 18 s, plus the settle (about 12 s)
    /// and the climb itself (about 20 s), fits the step's 120 s.</summary>
    const int MpWallTries = 4;

    const float MpWallWalkSeconds = 18f;

    /// <summary>
    /// Climbable faces with walkable ground in front within 40 m of A, nearest first: <see cref="WallsFrom"/>'s best
    /// face from A and from rings of seeds 6 and 15 m around A, each standing spot at least 2 m from the others.
    /// </summary>
    static List<(RaycastHit Face, Vector3 Stand)> MpWalls(Character c)
    {
        var found = new List<(RaycastHit Face, Vector3 Stand)>();
        var home = c.Center;
        void From(Vector3 seed)
        {
            float best = float.MaxValue;
            RaycastHit face = default;
            var stand = Vector3.zero;
            WallsFrom(seed, home, 1.2f, ref best, ref face, ref stand);
            if (best == float.MaxValue || Vector3.Distance(stand, home) > 40f)
                return;
            foreach (var other in found)
            {
                if (Vector3.Distance(other.Stand, stand) < 2f)
                    return;
            }
            found.Add((face, stand));
        }
        From(home);
        foreach (float radius in new[] { 6f, 15f })
        {
            for (int o = 0; o < 8; o++)
                From(home + Quaternion.Euler(0f, o * 45f, 0f) * Vector3.forward * radius);
        }
        found.Sort(
            (a, b) => Vector3.Distance(a.Stand, home).CompareTo(Vector3.Distance(b.Stand, home))
        );
        return found;
    }

    /// <summary><see cref="WalkTo"/> (not must-arrive) cut off after <paramref name="seconds"/>, the stick released
    /// either way.</summary>
    IEnumerator MpWalkBounded(Func<Vector3> target, float stop, float seconds)
    {
        yield return Guard(WalkTo(target, stop, false), Now + seconds, _ => { });
        Script(!VRInput.LeftHanded).Stick = Vector2.zero;
        yield return Wait(0.3f);
    }

    /// <summary>
    /// <see cref="MpWalkBounded"/> that walks around what blocks the straight line, as a player steps around a
    /// stanchion or a bench (MP run 16: the walk to the kiosk stalled 12.5 m short every time). When a try ends short,
    /// A sidesteps 2.5 m to a side whose chest-height line is clear, alternating sides, then tries again; at most
    /// <paramref name="detours"/> times, each walk bounded by <paramref name="seconds"/>.
    /// </summary>
    IEnumerator MpWalkAround(Func<Vector3> target, float stop, float seconds, int detours = 3)
    {
        var c = Character.localCharacter;
        for (int i = 0; ; i++)
        {
            yield return MpWalkBounded(target, stop, seconds);
            if (FlatDistance(c.Center, target()) <= stop + 0.2f || i >= detours)
                yield break;
            var to = target() - c.Center;
            to.y = 0f;
            if (to.sqrMagnitude < 1e-4f)
                yield break;
            var side = Vector3.Cross(Vector3.up, to.normalized) * (i % 2 == 0 ? 1f : -1f);
            var chest = c.Center + Vector3.up * 0.3f;
            if (
                Physics.Raycast(
                    chest,
                    side,
                    2.5f,
                    HelperFunctions.AllPhysicalExceptCharacter,
                    QueryTriggerInteraction.Ignore
                )
            )
                side = -side;
            var aside = c.Center + side * 2.5f;
            yield return MpWalkBounded(() => aside, 0.4f, 4f);
        }
    }

    IEnumerator MpTwoHand()
    {
        yield return FreeHands();
        if (!stepOk)
            yield break;
        var info = new ItemHoldInfo();
        var item = NearestGround(i =>
            info.Bind(i) && info.Profile.Handedness != Handedness.OneHanded
        );
        if (item == null)
        {
            stepSkip = $"no two-hand item within {NearItems:0} m";
            yield break;
        }
        yield return GrabWorld(item);
        if (!stepOk)
            yield break;
        yield return TwoHandHeld();
    }

    /// <summary>
    /// Food held at the host's head: interact.social's feed (Social/SocialHands.cs UpdateFeed) holds use-secondary, the
    /// game's feed path runs (Item.SendFeedDataRPC, Item.cs:996; Character.FeedItem, Character.cs:1637 ->
    /// GetFedItemRPC :1643 on the host, who needs to do nothing).
    /// </summary>
    IEnumerator Feed()
    {
        yield return FreeHands();
        if (!stepOk)
            yield break;
        yield return FetchFood(i => IsFood(i) && i.canUseOnFriend);
        if (!stepOk || stepSkip != null)
            yield break;
        var item = HeldItem();
        cpItemId = item.itemID;
        yield return WalkTo(HostCenter, 0.75f, true);
        if (!stepOk)
            yield break;
        var host = Host();
        Face(host.Head);
        yield return Wait(0.3f);
        var rig = VRRig.Instance;
        bool left = LocalHold.ItemInLeftHand;
        var c = Character.localCharacter;
        // The game's own mark of a finished feed on the feeder (Item.FinishCastSecondary, Item.cs:1043): a drop or a
        // lost grip also takes the item from the hand, and only this tells them apart.
        float consumedBefore = c.data.lastConsumedItem;
        yield return GripTo(left, 1f, 0.1f);
        // Feeding a friend is the secondary use: its own hold time, after up to 6 s reaching the friend's head and 2 s
        // held there first. A hand-over (SocialHands.UpdateFeed, HandOver): the food rests at the mouth for 2 s with
        // the trigger up and must not be given (external review row 9), then the trigger squeeze gives it.
        float end = Now + GameTiming.Budget(GameTiming.FeedSeconds(item), 8f);
        float best = float.MaxValue;
        float atMouth = 0f;
        bool proximityFed = false;
        float squeezeMark = -1f;
        float stalled = 0f;
        int resqueezes = 0;
        while (
            item != null
            && HeldItem() == item
            && Now < end
            && host != null
            && c.data.lastConsumedItem == consumedBefore
        )
        {
            var mouth = HostPart(host, BodypartType.Head);
            var miss = mouth - item.Center();
            best = Mathf.Min(best, miss.magnitude);
            atMouth = miss.magnitude <= 0.2f ? atMouth + Time.unscaledDeltaTime : atMouth;
            bool squeeze = atMouth >= 2f;
            if (!squeeze && item.isUsingSecondary)
                proximityFed = true;
            if (squeeze && squeezeMark < 0f)
                squeezeMark = CueMark();
            // A squeeze that is not feeding (the hand slipped off the mouth): let go and squeeze again, as a player
            // would.
            stalled = squeeze && !item.isUsingSecondary ? stalled + Time.unscaledDeltaTime : 0f;
            bool resqueeze = stalled > 0.6f;
            if (resqueeze)
            {
                stalled = 0f;
                resqueezes++;
            }
            Script(left).Trigger = squeeze && !resqueeze ? 1f : 0f;
            var goal = Script(left).Position + rig.Origin.InverseTransformVector(miss);
            // A real arm's length from the head, as feed-refuse (a host down a slope is not fed through the floor).
            goal =
                VRInput.HeadPosition
                + Vector3.ClampMagnitude(goal - VRInput.HeadPosition, MpArmReach);
            SetHand(
                left,
                HandScript
                    .Approach(Script(left).Position.N(), goal.N(), 1.2f, Time.unscaledDeltaTime)
                    .U()
            );
            yield return null;
        }
        Script(left).Trigger = 0f;
        var note =
            $"item closest {best:0.00} m to the host's head, {atMouth:0.0} s at the mouth, {resqueezes} re-squeeze(s)";
        // The squeeze is the hand-over, not a try at eating far from A's own mouth (HeldGestures.MouthHint's NotHere).
        bool notHere =
            squeezeMark >= 0f
            && CueLog.Capture.Fired(left, DenyReason.NotHere, squeezeMark, false, out _);
        if (proximityFed)
            Fail("the feed started with the trigger up, by proximity alone: " + note);
        else if (notHere)
            Fail("the squeeze that fed the host was also refused as NotHere: " + note);
        else if (c.data.lastConsumedItem == consumedBefore)
            Fail(
                (item != null && HeldItem() == item ? "not fed: " : "item left the hand unfed: ")
                    + note
            );
        else
            stepNote = "fed; " + note;
    }

    // ---------------------------------------------------------------- movement and hands

    /// <summary>Walks on the move stick, turning the rig toward the target (a snap or smooth turn would do the same),
    /// until within <paramref name="stop"/> m on the level. Stuck for 5 s fails when <paramref name="mustArrive"/>.</summary>
    IEnumerator WalkTo(Func<Vector3> target, float stop, bool mustArrive)
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        bool hand = !VRInput.LeftHanded;
        var last = c.Center;
        float progress = Now;
        while (true)
        {
            var to = target() - c.Center;
            to.y = 0f;
            float distance = to.magnitude;
            if (distance <= stop)
                break;
            float angle = Vector3.SignedAngle(Flat(rig.Head.forward), to, Vector3.up);
            float turn = 150f * Time.unscaledDeltaTime;
            if (Mathf.Abs(angle) > 3f)
                rig.RotateYaw(Mathf.Clamp(angle, -turn, turn));
            Script(hand).Stick = Mathf.Abs(angle) < 35f ? Vector2.up : Vector2.zero;
            if ((c.Center - last).sqrMagnitude > 0.09f)
            {
                last = c.Center;
                progress = Now;
            }
            else if (Now - progress > 5f)
            {
                Script(hand).Stick = Vector2.zero;
                if (mustArrive)
                    Fail($"stuck {distance:0.0} m short of the goal");
                yield break;
            }
            yield return null;
        }
        Script(hand).Stick = Vector2.zero;
        yield return Wait(0.5f);
    }

    static void Face(Vector3 world)
    {
        var rig = VRRig.Instance;
        rig.RotateYaw(
            Vector3.SignedAngle(Flat(rig.Head.forward), Flat(world - rig.Head.position), Vector3.up)
        );
    }

    /// <summary>
    /// Grabs the first of up to three ground items (<paramref name="first"/>, then the nearest matching
    /// <paramref name="ok"/>) that the walk actually reaches; one on a ledge or behind rock is passed over (MP run 7:
    /// holster walked into rock 4.2 m short of the thrown drink). SKIP when none can be reached.
    /// </summary>
    IEnumerator GrabReachable(
        Func<Item, bool> ok,
        Item first = null,
        float range = NearItems,
        int tries = 3
    )
    {
        var tried = new HashSet<Item>();
        for (int i = 0; i < tries; i++)
        {
            var item =
                i == 0 && first != null
                    ? first
                    : NearestGround(x => ok(x) && !tried.Contains(x), null, range);
            if (item == null)
                break;
            tried.Add(item);
            // Bounded, so far tries still fit the step (walking 4 m/s).
            yield return MpWalkBounded(
                () => item != null ? item.Center() : Character.localCharacter.Center,
                0.55f,
                5f + Vector3.Distance(item.Center(), Character.localCharacter.Center) / 3f
            );
            var to = item != null ? item.Center() - Character.localCharacter.Center : Vector3.zero;
            to.y = 0f;
            if (item == null || to.magnitude > 0.8f)
                continue;
            yield return GrabWorld(item);
            yield break;
        }
        stepSkip = $"no reachable item among {tried.Count} tried within {range:0} m";
    }

    /// <summary>How far (m) A goes for food: along the beach, as a player would.</summary>
    const float FoodRange = 120f;

    /// <summary>
    /// Food the way a player gets it (MP run 14: "no food within 40 m" skipped eat, feed and feed-refuse): the nearest
    /// reachable food lying within <see cref="FoodRange"/> (a berry on a bush, BerryBush.cs:13-49, is a ground item
    /// like any other); none, then the closed suitcases within that range opened by hand (<see cref="LiftLid"/>), one at
    /// a time until one spills food. SKIP with the reason when none turns up.
    /// </summary>
    IEnumerator FetchFood(Func<Item, bool> food)
    {
        var c = Character.localCharacter;
        var opened = new HashSet<Luggage>();
        for (int round = 0; round < 4; round++)
        {
            if (NearestGround(food, null, FoodRange) != null)
            {
                stepSkip = null;
                yield return GrabReachable(food, null, FoodRange, 4);
                if (stepSkip == null || !stepOk)
                    yield break;
            }
            var lug = Nearest<Luggage>(
                c.Center,
                l => !l.IsOpen && l is not RespawnChest && !opened.Contains(l)
            );
            if (lug == null || FlatDistance(lug.Center(), c.Center) > FoodRange)
                break;
            opened.Add(lug);
            var solid = SolidColliders(lug);
            var centre = lug.Center();
            yield return MpWalkBounded(
                () => SolidPoint(solid, centre, Character.localCharacter.Center),
                0.5f,
                5f + FlatDistance(centre, c.Center) / 3f
            );
            var near = SolidPoint(solid, centre, c.Center) - c.Center;
            near.y = 0f;
            if (near.magnitude > 0.6f)
                continue;
            Face(lug.Center());
            yield return Wait(0.4f);
            stepSkip = null;
            yield return LiftLid(lug);
            if (!stepOk)
                yield break;
            yield return Wait(2.5f);
            yield return MpRest();
        }
        stepSkip = $"no food within {FoodRange:0} m after opening {opened.Count} more suitcase(s)";
    }

    /// <summary>Walks to a ground item and closes the right hand on it (vr.10's grab, on an item the level spawned).</summary>
    IEnumerator GrabWorld(Item world)
    {
        if (world == null)
        {
            Fail($"no item on the ground within {NearItems:0} m");
            yield break;
        }
        ushort id = world.itemID;
        string label = world.name;
        cpItemId = id;
        yield return WalkTo(
            () => world != null ? world.Center() : Character.localCharacter.Center,
            0.55f,
            true
        );
        if (!stepOk)
            yield break;
        if (world == null || world.itemState != ItemState.Ground)
        {
            Fail(label + " is gone");
            yield break;
        }
        Face(world.Center());
        yield return Wait(0.3f);
        const bool left = false;
        yield return GripTo(left, 0f, 0.05f);
        var at = world.Center();
        yield return MoveTo(
            left,
            () =>
            {
                if (world != null)
                    at = world.Center();
                return at;
            },
            0.6f
        );
        yield return GripTo(left, 1f, 0.1f);
        yield return Until(
            () => HeldItem() != null && HeldItem().itemID == id,
            3f,
            label + " not picked up"
        );
        if (!stepOk)
            yield break;
        stepNote = "holding " + label;
        HeadFrame(out var head, out var forward, out var right);
        yield return MoveTrack(left, Rest(head, forward, right).Position, 0.4f);
    }

    /// <summary>Taps the drop button (<c>InteractConfig.DropButton</c>, DominantSecondary by default).</summary>
    IEnumerator TapDrop()
    {
        bool hand = VRInput.LeftHanded;
        Script(hand).Secondary = true;
        yield return Wait(0.12f);
        Script(hand).Secondary = false;
        yield return null;
    }

    /// <summary>Drops what is in hand and opens both grips, so a grip an earlier step left closed (a two-hand hold,
    /// a grab) does not start this one already closed.</summary>
    IEnumerator FreeHands()
    {
        if (HeldItem() != null)
        {
            yield return TapDrop();
            yield return WaitFor(() => HeldItem() == null, 2f);
        }
        if (HeldItem() != null)
        {
            Fail("could not drop " + HeldItem().name + " to free the hands");
            yield break;
        }
        scriptLeft.Grip = scriptRight.Grip = 0f;
        yield return Wait(0.5f);
    }

    /// <summary>Both hands back to the rest pose with nothing pressed; the grips stay as they are.</summary>
    IEnumerator MpRest()
    {
        Calm(ref scriptLeft);
        Calm(ref scriptRight);
        HeadFrame(out var head, out var forward, out var right);
        yield return MoveTrack(true, Rest(head, forward, -right).Position, 0.3f);
        yield return MoveTrack(false, Rest(head, forward, right).Position, 0.3f);
    }

    static IEnumerator Settle(Item item)
    {
        float still = 0f;
        float end = Now + 5f;
        while (item != null && Now < end && still < 0.3f)
        {
            float speed = item.rig != null ? item.rig.linearVelocity.magnitude : 0f;
            still = speed < 0.2f ? still + Time.unscaledDeltaTime : 0f;
            yield return null;
        }
    }

    // ---------------------------------------------------------------- world queries

    /// <summary>The other player: the first non-bot remote character (Character.cs:131 AllCharacters).</summary>
    static Character Host()
    {
        foreach (var c in Character.AllCharacters)
        {
            if (c != null && !c.IsLocal && !c.isBot)
                return c;
        }
        return null;
    }

    static Vector3 HostCenter()
    {
        var host = Host();
        return host != null ? host.Center : Character.localCharacter.Center;
    }

    /// <summary>Food lowers Hunger when eaten (<see cref="StatusEffect"/>).</summary>
    static bool IsFood(Item item) =>
        StatusEffect(item, out var status, out float change)
        && status == CharacterAfflictions.STATUSTYPE.Hunger
        && change < 0f;

    /// <summary>A ground item the hand can take. A ground backpack opens its wheel instead (Backpack.cs:32-35), so it is
    /// never a generic grab target; vr.29 covers it.</summary>
    static bool Grabbable(Item item) =>
        item != null && item.itemState == ItemState.Ground && item is not Backpack;

    static Item OnGround(Item item) => Grabbable(item) ? item : null;

    /// <summary>The nearest grabbable ground item (Item.cs:103 ALL_ITEMS, :286 itemState) within <see cref="NearItems"/> m.</summary>
    static Item NearestGround(Func<Item, bool> ok, Vector3? from = null, float range = NearItems)
    {
        var at = from ?? Character.localCharacter.Center;
        Item best = null;
        float bestSq = range * range;
        foreach (var item in Item.ALL_ITEMS)
        {
            if (!Grabbable(item) || !ok(item))
                continue;
            float sq = (item.Center() - at).sqrMagnitude;
            if (sq < bestSq)
            {
                bestSq = sq;
                best = item;
            }
        }
        return best;
    }

    static int CountNear(bool food)
    {
        var at = Character.localCharacter.Center;
        int n = 0;
        foreach (var item in Item.ALL_ITEMS)
        {
            if (
                Grabbable(item)
                && IsFood(item) == food
                && (item.Center() - at).sqrMagnitude < NearItems * NearItems
            )
                n++;
        }
        return n;
    }

    /// <summary>Two foods (eat, feed) and one other item (grab, throw, holster).</summary>
    static bool EnoughItems() => CountNear(true) >= 2 && CountNear(false) >= 1;

    /// <summary>A case's enabled solid colliders; triggers widen its bounds (see <see cref="LidPoint"/>).</summary>
    static List<Collider> SolidColliders(Luggage lug)
    {
        var solid = new List<Collider>();
        foreach (var col in lug.GetComponentsInChildren<Collider>())
        {
            if (!col.isTrigger && col.enabled)
                solid.Add(col);
        }
        return solid;
    }

    /// <summary>The point of the <paramref name="solid"/> colliders' bounds nearest <paramref name="from"/>;
    /// <paramref name="fallback"/> when there are none (or all are gone).</summary>
    static Vector3 SolidPoint(List<Collider> solid, Vector3 fallback, Vector3 from)
    {
        var best = fallback;
        float bestSq = float.MaxValue;
        foreach (var col in solid)
        {
            if (col == null)
                continue;
            var p = col.bounds.ClosestPoint(from);
            float sq = (p - from).sqrMagnitude;
            if (sq < bestSq)
            {
                bestSq = sq;
                best = p;
            }
        }
        return best;
    }

    static string ReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// The social checkpoints (WP4), after <c>feed</c>. The host is a vanilla player A cannot steer, so each step
    /// reads the host's state and is SKIP when it does not fit (not passed out, no pack, not in trouble, no kick mode
    /// in the host's run). Everything A does goes through interact.social's grips and the game's own RPCs.
    /// </summary>
    IEnumerator MpSocialSteps()
    {
        yield return MpStep("feed-refuse", 150f, true, FeedRefuse());
        yield return MpStep("reach-deny", 40f, true, ReachDeny());
        yield return MpStep("reach", 40f, true, MpReach());
        yield return HostWake();
        yield return MpStep("kick-mode", 40f, true, KickMode());
        yield return MpStep("carry", 60f, true, MpCarry());
        yield return HostWake();
        yield return MpStep("pack", MpPackBudget, true, MpPack());
        yield return MpStep("pal-jump", 40f, true, PalJump());
        // A timed-out step is dropped without its finally: put the head back from the crouch dip.
        if (VRSession.Active && VRInput.TestOverride != null)
            VRInput.TestOverride = applyHands;
        yield return HostWake();
        yield return MpStep("campfire-far", 60f, true, CampfireFar());
        // Last: A passes out to spectate, then wakes.
        yield return MpStep("spectate", 40f, true, MpSpectate());
        yield return MpStep("save-untouched", 10f, false, SaveUntouched());
        Safe("mp.self-wake", () => Wake(Character.localCharacter));
    }

    int mpRequestId;

    /// <summary>
    /// Asks the observer to do <paramref name="what"/> to its own character (<see cref="MpRequest"/>) and waits up to
    /// <paramref name="seconds"/> for its answer. No answer (an older observer) or a refusal skips the step.
    /// </summary>
    IEnumerator Ask(string what, float seconds = 5f)
    {
        int id = ++mpRequestId;
        try
        {
            File.AppendAllText(
                Path.Combine(MpDirectory, MpRequest.RequestFile),
                MpRequest.Line(id, what, Unix) + "\n"
            );
        }
        catch (IOException e)
        {
            stepSkip = $"request {what} not written: {e.Message}";
            yield break;
        }
        var acks = Path.Combine(MpDirectory, MpRequest.AckFile);
        float end = Now + seconds;
        while (Now < end)
        {
            var text = ReadText(acks);
            if (text != null)
            {
                foreach (var line in text.Split('\n'))
                {
                    if (!MpRequest.TryReadAck(line, out int got, out bool ok) || got != id)
                        continue;
                    if (!ok)
                        stepSkip = $"observer refused {what}: {JsonLine.ReadString(line, "note")}";
                    yield break;
                }
            }
            yield return Wait(0.25f);
        }
        stepSkip = $"observer did not answer {what} in {seconds:0} s";
    }

    /// <summary>Between social steps: the host wakes and clears statuses (<see cref="MpRequest.Wake"/>); a missing
    /// answer only logs.</summary>
    IEnumerator HostWake()
    {
        var host = Host();
        if (
            host == null
            || !(host.data.passedOut || host.data.dead || host.refs.afflictions.isStruggling)
        )
            yield break;
        stepSkip = null;
        yield return Ask(MpRequest.Wake);
        if (stepSkip != null)
            Plugin.Log.LogWarning("[mptest] " + stepSkip);
        stepSkip = null;
        var end = Now + 6f;
        while (Now < end && (host.data.passedOut || host.refs.afflictions.isStruggling))
            yield return null;
    }

    /// <summary>The game's own wake for a character we own: statuses cleared, then the revive RPC
    /// (RPCA_ReviveAtPosition, Character.cs:1799) where it lies.</summary>
    static void Wake(Character c)
    {
        if (c == null || !(c.data.passedOut || c.data.dead))
            return;
        c.refs.afflictions.ClearAllStatus(excludeCurse: false, excludePetrify: false);
        c.refs.view.RPC("RPCA_ReviveAtPosition", RpcTarget.All, c.Center, false, -1);
    }

    static Vector3 HostPart(Character host, BodypartType type) =>
        host.refs.ragdoll != null && host.refs.ragdoll.partDict.TryGetValue(type, out var part)
            ? part.transform.position
            : host.Center;

    /// <summary>Walks up to the host, empty-handed, facing them.</summary>
    IEnumerator ToHost(float stop)
    {
        yield return FreeHands();
        if (!stepOk)
            yield break;
        yield return WalkTo(HostCenter, stop, true);
        if (!stepOk)
            yield break;
        Face(Host().Head);
        yield return Wait(0.3f);
    }

    /// <summary>Shoulder to fingertip of an adult arm (m), the brief's 0.6-0.75 m.</summary>
    const float MpShoulderReach = 0.75f;

    /// <summary>How far (m) past a real arm the <paramref name="left"/> hand must go to touch <paramref name="world"/>:
    /// from the shoulder (0.25 m under the head, 0.18 m to that side) less the grip's own pick radius
    /// (<see cref="SocialConfig.Reach"/>, SocialHands.Find); 0 or less is in reach.</summary>
    static float PastArm(bool left, Vector3 world)
    {
        // HeadFrame is tracking space; the target is world space (MP run 14: the two were mixed and every friend's hand
        // read 370 m past reach, skipping reach and reach-deny).
        HeadFrame(out var head, out _, out var right);
        var shoulder = head + Vector3.down * 0.25f + (left ? -right : right) * 0.18f;
        var target = VRRig.Instance.Origin.InverseTransformPoint(world);
        return Vector3.Distance(shoulder, target) - SocialConfig.Instance.Reach - MpShoulderReach;
    }

    /// <summary>A grip closes on the host's right hand (where a helping hand goes). A hand out of a real arm's reach
    /// takes one more step in, then skips the step rather than stretch the scripted arm.</summary>
    IEnumerator GripHostHand(bool left)
    {
        var host = Host();
        // Whichever of the host's hands is nearer this hand's shoulder, as a player reaches for the closer one.
        Vector3 Part()
        {
            var l = HostPart(host, BodypartType.Hand_L);
            var r = HostPart(host, BodypartType.Hand_R);
            return PastArm(left, l) < PastArm(left, r) ? l : r;
        }
        // Up to two steps in toward that hand, as the solo steps do (MP run 15: the walk to the host stopped 0.27 m
        // and 0.30 m short of reach). Each step goes as far as the hand is past reach, bounded.
        for (int i = 0; i < 2 && PastArm(left, Part()) > 0f; i++)
        {
            var c = Character.localCharacter;
            var toward = Part() - c.Center;
            toward.y = 0f;
            float step = PastArm(left, Part()) + 0.1f;
            var to = c.Center + toward.normalized * Mathf.Min(step, toward.magnitude);
            yield return MpWalkBounded(() => to, 0.1f, 3f);
            Face(host.Head);
            yield return Wait(0.3f);
        }
        float past = PastArm(left, Part());
        if (past > 0f)
        {
            stepSkip = $"the host's hand is {past:0.00} m past a real arm's reach";
            yield break;
        }
        yield return MoveTo(left, Part, 0.5f);
        yield return GripTo(left, 1f, 0.1f);
    }

    /// <summary>
    /// feed-refuse: a non-food item (not <c>canUseOnFriend</c>) held at the host's mouth is refused
    /// (CharacterInteractible.HasItemCanUseOnFriend, :90): CantUse at that hand with the item turned red, and B gets
    /// no feed RPC.
    /// </summary>
    IEnumerator FeedRefuse()
    {
        yield return FreeHands();
        if (!stepOk)
            yield break;
        // A small one-handed thing (MP run 7 took the two-hand step's Guidebook, whose two-hand hold kept it from the
        // host's mouth).
        var info = new ItemHoldInfo();
        // As far as food is fetched, five tries (MP run 14: three within 40 m were all out of reach).
        yield return GrabReachable(
            i =>
                !i.canUseOnFriend
                && !IsFood(i)
                // No trigger use of its own: one that has one (MP run 15's Flare) is no hand-over at all, so its
                // trigger stays its own and nothing is refused (HandOver ownUse).
                && !i.CanUsePrimary()
                && info.Bind(i)
                && info.Profile.Handedness == Handedness.OneHanded,
            null,
            FoodRange,
            5
        );
        if (!stepOk || stepSkip != null)
            yield break;
        var item = HeldItem();
        // Close enough that the item reaches the mouth with the arm short of full stretch (MP run 10: from 0.6 m the
        // hand, capped at arm's reach, held the Frisbee 0.45 m from the head, outside the 0.22 m mouth zone).
        // Not a must-arrive: the host's own body can stop the walk a little short; the reach check below decides.
        yield return WalkTo(HostCenter, 0.45f, false);
        var host = Host();
        var rig = VRRig.Instance;
        // The zone SocialHands.UpdateFeed tests: the item's centre within FeedRadius of the Head bodypart.
        Vector3 Mouth() => HostPart(host, BodypartType.Head);
        // The host's mouth must be within arm's reach of the head, not on a ledge below (MP run 7: the hand was driven
        // 1.4 m under the floor toward a host down the slope).
        float gap = Vector3.Distance(rig.Head.position, Mouth());
        // Still more than a bent arm away (MP run 13: 0.87 m, the walk stopped against the host): one more, closer step.
        if (gap > MpArmReach - 0.15f)
        {
            yield return MpWalkBounded(HostCenter, 0.3f, 4f);
            Face(Mouth());
            yield return Wait(0.3f);
            gap = Vector3.Distance(rig.Head.position, Mouth());
        }
        // The item reaches past the hand, so a mouth up to a full arm plus the item away is in reach.
        if (gap > MpArmReach + 0.1f)
        {
            stepSkip = $"the host's head is {gap:0.00} m from A's, out of arm's reach";
            yield break;
        }
        Face(Mouth());
        bool left = LocalHold.ItemInLeftHand;
        LookAtHand(left);
        float mark = CueMark();
        // A hand-over (HandOver): 1 s resting at the mouth with the trigger up refuses nothing (no feed was tried),
        // then the trigger squeeze is refused.
        float end = Now + 6f;
        float best = float.MaxValue;
        float atMouth = 0f;
        bool proximityRefused = false;
        float squeezeMark = -1f;
        while (Now < end && HeldItem() == item)
        {
            best = Mathf.Min(best, Vector3.Distance(item.Center(), Mouth()));
            atMouth = best <= 0.2f ? atMouth + Time.unscaledDeltaTime : atMouth;
            bool squeeze = atMouth >= 1f;
            if (!squeeze && CueLog.Capture.Fired(left, DenyReason.CantUse, mark, false, out _))
                proximityRefused = true;
            if (squeeze && squeezeMark < 0f)
                squeezeMark = CueMark();
            Script(left).Trigger = squeeze ? 1f : 0f;
            var goal =
                Script(left).Position + rig.Origin.InverseTransformVector(Mouth() - item.Center());
            goal =
                VRInput.HeadPosition
                + Vector3.ClampMagnitude(goal - VRInput.HeadPosition, MpArmReach);
            SetHand(
                left,
                HandScript
                    .Approach(Script(left).Position.N(), goal.N(), 1.2f, Time.unscaledDeltaTime)
                    .U()
            );
            if (CueLog.Capture.Fired(left, DenyReason.CantUse, mark, true, out _))
                break;
            yield return null;
        }
        Script(left).Trigger = 0f;
        if (proximityRefused)
        {
            Fail($"{item.name} was refused with the trigger up, before any feed was tried");
            yield break;
        }
        bool fired = CueLog.Capture.Fired(left, DenyReason.CantUse, mark, true, out _);
        if (!fired)
            Note(
                $"{item.name} came within {best:0.00} m of the host's head, {atMouth:0.0} s at the mouth"
            );
        if (squeezeMark < 0f)
        {
            Fail(
                $"{item.name} never rested at the host's mouth for the squeeze (closest {best:0.00} m)"
            );
            yield break;
        }
        // The window runs from the squeeze, not from the approach (MP run 15: a window from the start of the walk was
        // over before the squeeze).
        yield return ExpectCue(left, DenyReason.CantUse, squeezeMark, true, 1.5f);
        if (stepOk)
            stepNote = $"{item.name} at the host's mouth refused; {cueNote}";
    }

    /// <summary>reach-deny: an empty hand grips a host who needs no help (CharacterGrabbing.TargetCanBeHelped,
    /// :293): NotInTrouble at that hand, no tint (D10), and no reach starts.</summary>
    IEnumerator ReachDeny()
    {
        yield return ToHost(0.6f);
        if (!stepOk)
            yield break;
        var c = Character.localCharacter;
        if (CharacterGrabbing.TargetCanBeHelped(Host(), c))
        {
            stepSkip = "the host needs help now (reach covers it)";
            yield break;
        }
        LookAtHand(false);
        float mark = CueMark();
        yield return GripHostHand(false);
        if (stepSkip != null)
            yield break;
        yield return ExpectCue(false, DenyReason.NotInTrouble, mark, false);
        if (!stepOk)
            yield break;
        for (float end = Now + 0.5f; Now < end; )
        {
            if (c.data.isReaching)
            {
                Fail("a reach started on a host who needs no help");
                yield break;
            }
            yield return null;
        }
        stepNote = "grip on a fine host refused; " + cueNote;
    }

    /// <summary>reach: the host is asked to get webbed (<see cref="MpRequest.Web"/>), so the game lets a friend help
    /// (CharacterGrabbing.TargetCanBeHelped, :293); a grip on their hand holds the game's reach and it catches them
    /// (GUIManager.Grasp after CharacterGrabbing.Reach, :247-276).</summary>
    IEnumerator MpReach()
    {
        var c = Character.localCharacter;
        var host = Host();
        if (host != null && !CharacterGrabbing.TargetCanBeHelped(host, c))
        {
            yield return Ask(MpRequest.Web);
            if (stepSkip != null)
                yield break;
            yield return WaitFor(() => CharacterGrabbing.TargetCanBeHelped(host, c), 3f);
        }
        if (host == null || !CharacterGrabbing.TargetCanBeHelped(host, c))
        {
            stepSkip = "the host needs no help (web request had no effect)";
            yield break;
        }
        yield return ToHost(0.6f);
        if (!stepOk)
            yield break;
        yield return GripHostHand(false);
        if (stepSkip != null)
            yield break;
        yield return Until(
            () => c.data.sinceGrabFriend < 0.3f,
            3f,
            "the reach never caught the host"
        );
        if (stepOk)
            stepNote = "reached and caught the host";
    }

    /// <summary>
    /// kick-mode (the host's custom run sets HelpingHand to kick, CharacterGrabbing.cs:33): a grip on the host's hand
    /// must not kick (no stamina spent, no RPCA_Fall on B); then both open palms pushed at the host kick through the
    /// game's own reach press (CharacterGrabbing.cs:104).
    /// </summary>
    IEnumerator KickMode()
    {
        if (RunSettings.GetValue(RunSettings.SETTINGTYPE.HelpingHand) != 1)
        {
            stepSkip = "the host's run has no kick mode";
            yield break;
        }
        yield return ToHost(0.7f);
        if (!stepOk)
            yield break;
        var c = Character.localCharacter;
        float stamina = c.data.currentStamina;
        yield return GripHostHand(false);
        if (stepSkip != null)
            yield break;
        for (float end = Now + 1f; Now < end; )
        {
            if (c.data.isKicking)
            {
                Fail("a grip on the host kicked them");
                yield break;
            }
            yield return null;
        }
        yield return HandsRelease();
        HeadFrame(out var head, out var forward, out var right);
        var l = Rest(head, forward, -right).Position;
        var r = Rest(head, forward, right).Position;
        float start = Now;
        // 0.45 m in 0.15 s: 3 m/s, both palms open (ShoveGesture).
        while (Now - start < 0.15f)
        {
            float t = (Now - start) / 0.15f;
            SetHand(true, l + forward * (0.45f * t));
            SetHand(false, r + forward * (0.45f * t));
            yield return null;
        }
        yield return Until(() => c.data.isKicking, 1f, "the shove did not kick");
        if (stepOk)
            stepNote = $"grip kept stamina {stamina:0.00}; shove kicked";
    }

    /// <summary>How long (s) A carries the host, grips open and standing, before putting them down: a player walks
    /// off with a friend, so a carry the game ends at once (MP run 13: 1 ms) fails.</summary>
    const float MpCarryHold = 2f;

    /// <summary>
    /// carry: the host is passed out and stays out (statuses at 1 or more; under 1 the game wakes them 3 s after passing
    /// out, Character.HandlePassedOut :574-579, and a carried friend who wakes is dropped, CharacterCarrying.Update
    /// :29-31). A stoops to the body on the ground (<see cref="ReachDown"/>, a player squats; standing it is 1.3 m of
    /// arm) and both hands on it lift them (CharacterInteractible.Interact :114-124, RPCA_StartCarry,
    /// CharacterCarrying.cs:56). A worn pack drops at their torso (:59-67), marked by a Dropped notice. A stands, grips
    /// open, and the host stays carried for <see cref="MpCarryHold"/>; then a grip on the carried host drops them
    /// (Interact :116-119).
    /// </summary>
    IEnumerator MpCarry()
    {
        var c = Character.localCharacter;
        var host = Host();
        if (host != null && !host.data.fullyPassedOut && !host.data.dead)
        {
            yield return Ask(MpRequest.PassOut);
            if (stepSkip != null)
                yield break;
            yield return WaitFor(() => host.data.fullyPassedOut, 8f);
        }
        if (
            host == null
            || !host.data.fullyPassedOut
            || host.data.dead
            || host.data.carrier != null
        )
        {
            stepSkip = "the host is not passed out (pass-out request had no effect)";
            yield break;
        }
        // The owner's statuses reach A by SyncStatusesRPC (CharacterAfflictions.cs:1107).
        var statuses = host.refs.afflictions;
        yield return WaitFor(() => statuses.statusSum >= 1f, 2f);
        if (statuses.statusSum < 1f)
        {
            stepSkip = F(
                $"the host's statuses sum {statuses.statusSum:0.00}, under 1: the game wakes them 3 s after passing out"
            );
            yield break;
        }
        yield return ToHost(0.6f);
        if (!stepOk)
            yield break;
        bool pack = c.player != null && !c.player.backpackSlot.IsEmpty();
        float mark = CueMark();
        yield return ReachDown(true, () => HostPart(host, BodypartType.Torso), 0.6f);
        yield return ReachDown(false, () => HostPart(host, BodypartType.Hip), 0.5f);
        var over = OverReach(true) ?? OverReach(false);
        if (over != null)
        {
            stepSkip = "stooped to the lying host, " + over;
            yield break;
        }
        Script(true).Grip = Script(false).Grip = 1f;
        float hold = GameTiming.InteractSeconds(host.refs.interactible, c);
        float liftEnd = Now + GameTiming.Budget(hold, 1f);
        int startsBefore = SocialHands.CarryStarts;
        while (Now < liftEnd && c.data.carriedPlayer != host)
        {
            if (c.data.carriedPlayer == null && SocialHands.CarryStarts > startsBefore)
            {
                Fail(CarryEnded("lifted then dropped at once", host, pack));
                yield break;
            }
            yield return null;
        }
        if (c.data.carriedPlayer != host)
        {
            Fail(
                F(
                    $"the host was not lifted (host fully passed out {host.data.fullyPassedOut}, lifts {SocialHands.CarryStarts - startsBefore})"
                )
            );
            yield break;
        }
        if (pack)
            yield return ExpectCue(
                SocialHands.Instance.CarryLeft,
                DenyReason.Dropped,
                mark,
                seen: false
            );
        if (!stepOk)
            yield break;
        // Lifted: the hands let go and A stands up with the host on its back, as a player walks off.
        Script(true).Grip = Script(false).Grip = 0f;
        float carriedFrom = Now;
        var rise = StandUp(true);
        while (c.data.carriedPlayer == host && rise.MoveNext())
            yield return rise.Current;
        while (Now - carriedFrom < MpCarryHold && c.data.carriedPlayer == host)
            yield return null;
        if (c.data.carriedPlayer != host)
        {
            Fail(
                CarryEnded(
                    F($"the host fell off {Now - carriedFrom:0.00} s after the lift"),
                    host,
                    pack
                )
            );
            yield break;
        }
        yield return MoveTo(false, () => HostPart(host, BodypartType.Torso), 0.4f);
        over = OverReach(false);
        if (over != null)
        {
            Fail("putting the carried host down: " + over);
            yield break;
        }
        Script(false).Grip = 1f;
        yield return Until(
            () => c.data.carriedPlayer == null,
            GameTiming.Budget(hold, 1f),
            "the host was not put down"
        );
        Script(false).Grip = 0f;
        if (stepOk)
            stepNote = F(
                $"stooped, lifted{(pack ? ", pack dropped and marked" : "")}, carried {MpCarryHold:0.#} s standing, put down"
            );
    }

    /// <summary>Why a carry ended early, from the game's own drop paths: the carried friend woke or died, or the carrier
    /// went down (CharacterCarrying.Update :28-32), or the backpack slot was picked (CharacterItems.cs:459-462, :821).</summary>
    static string CarryEnded(string what, Character host, bool pack)
    {
        var c = Character.localCharacter;
        var slot = c.refs.items.currentSelectedSlot;
        return F(
            $"{what}: host fully passed out {host.data.fullyPassedOut} (statuses {host.refs.afflictions.statusSum:0.00}), dead {host.data.dead}, A down {c.data.fullyPassedOut}, A slot {(slot.IsSome ? slot.Value.ToString(CultureInfo.InvariantCulture) : "none")}, A wore a pack {pack}, interact held {c.input.interactIsPressed}"
        );
    }

    /// <summary>Each of the three spots behind the host gets this long to walk to (MP run 11: three unbounded walks ran
    /// the step past 40 s).</summary>
    const float MpPackWalkSeconds = 8f;

    /// <summary>The pack step's budget from its parts: the settle (recentre about 4 s, stillness up to 6 s, a slope
    /// walk), freeing the hands (about 3 s), three bounded walks, turning and reaching (about 1 s) and the hold check
    /// (2 s), with a margin.</summary>
    const float MpPackBudget = 14f + 3f + 3 * (MpPackWalkSeconds + 0.3f) + 1f + 2f + 8f;

    /// <summary>pack: the host's worn pack, gripped from behind while A looks away; the hold starts
    /// (BackpackOnBackVisuals.IsConstantlyInteractable, :152) because the look check follows the hand.</summary>
    IEnumerator MpPack()
    {
        var host = Host();
        var visuals =
            host != null && host.refs.backpackHandler != null
                ? host.refs.backpackHandler.activeBackpackVisuals
                : null;
        // The visuals component answers even with an empty backpack slot (SocialHands.WearsPack; MP run 13 "held" an
        // invisible pack on a host who had none).
        if (visuals == null || !SocialHands.WearsPack(host))
        {
            stepSkip =
                "the host wears no pack (vanilla B can only put one on by picking one up, which takes more than local input)";
            yield break;
        }
        yield return FreeHands();
        if (!stepOk)
            yield break;
        // A spot behind the host: straight behind, else 45° to either side of behind (the pack still faces
        // the hand from there). MP run 10: straight behind was blocked 1.2 m short.
        var c = Character.localCharacter;
        bool behind = false;
        foreach (float yaw in new[] { 0f, 45f, -45f })
        {
            var back = Quaternion.Euler(0f, yaw, 0f) * -host.data.lookDirection_Flat.normalized;
            Vector3 Spot() => host.Center + back * 0.8f;
            yield return MpWalkBounded(Spot, 0.3f, MpPackWalkSeconds);
            var miss = Spot() - c.Center;
            miss.y = 0f;
            if (
                miss.magnitude <= 0.5f
                && Vector3.Dot(c.Center - host.Center, host.data.lookDirection_Flat) < 0f
            )
            {
                behind = true;
                break;
            }
        }
        if (!behind)
        {
            stepSkip = "no reachable spot behind the host";
            yield break;
        }
        Face(visuals.transform.position);
        // Look away as a player can: vanilla needs the look within 110° of the pack's facing
        // (BackpackOnBackVisuals.cs:99-104). The body turns 45° away from that facing, which leaves the pack ahead of the
        // hand on the near side, and the head alone 75° further, a neck's turn (MP run 13 turned the whole body 150° and
        // reached behind its own back).
        float side = Vector3.SignedAngle(
            HelperFunctions.ZeroY(visuals.transform.forward),
            HelperFunctions.ZeroY(c.data.lookDirection),
            Vector3.up
        );
        float turn = side >= 0f ? 1f : -1f;
        // Turning right puts the pack on the left.
        bool left = turn > 0f;
        VRRig.Instance.RotateYaw(turn * MpPackBodyTurn);
        var hands = applyHands;
        var neck = Quaternion.Euler(0f, turn * MpPackNeckTurn, 0f);
        VRInput.TestOverride = () =>
        {
            hands();
            VRInput.HeadRotation = neck * VRInput.HeadRotation;
        };
        try
        {
            yield return Wait(0.5f);
            float off = Vector3.Angle(
                HelperFunctions.ZeroY(c.data.lookDirection),
                HelperFunctions.ZeroY(visuals.transform.forward)
            );
            // Within 110° vanilla takes the hold anyway, so a pass would not show the hand pick (SocialPatches.PackPick).
            if (off < 110f)
            {
                stepSkip = F(
                    $"the look is only {off:0}° off the pack's facing, which vanilla accepts"
                );
                yield break;
            }
            float past = PastArm(left, visuals.transform.position);
            if (past > 0f)
            {
                stepSkip = F($"the host's pack is {past:0.00} m past a real arm's reach");
                yield break;
            }
            yield return MoveTo(left, () => visuals.transform.position, 0.6f);
            yield return GripTo(left, 1f, 0.1f);
            yield return Until(
                () =>
                    Interaction.instance != null
                    && Interaction.instance.currentHeldInteractible is BackpackOnBackVisuals,
                2f,
                "the pack hold did not start"
            );
            Script(left).Grip = 0f;
            if (stepOk)
                stepNote = F(
                    $"host's pack held from behind by the {(left ? "left" : "right")} hand, looking {off:0}° off its facing"
                );
        }
        finally
        {
            VRInput.TestOverride = hands;
        }
    }

    /// <summary>Body and neck turn (degrees) of the pack step: 120° in all, past vanilla's 110° (BackpackOnBackVisuals.cs:102).</summary>
    const float MpPackBodyTurn = 45f;

    const float MpPackNeckTurn = 75f;

    /// <summary>
    /// pal-jump (CharacterMovement.StandOnPlayer :992-1022, CheckForPalJump :1024-1034): A crouches by a scripted head
    /// dip (player.locomotion's physical crouch), the host is asked to stand on A's head (<see cref="MpRequest.StandOn"/>,
    /// the game's warp RPC), and A jumps with the jump button. B's own client boosts B (JumpRpc true, checked by
    /// tools/mp-check.sh). The note says whether A felt the pressure of a friend on its back.
    /// </summary>
    IEnumerator PalJump()
    {
        var c = Character.localCharacter;
        yield return ToHost(1.5f);
        if (!stepOk)
            yield break;
        var hands = applyHands;
        float low = VRInput.HeadPosition.y * 0.6f;
        VRInput.TestOverride = () =>
        {
            hands();
            var p = VRInput.HeadPosition;
            p.y = low;
            VRInput.HeadPosition = p;
        };
        try
        {
            yield return WaitFor(() => c.data.isCrouching, 2f);
            if (!c.data.isCrouching)
            {
                stepSkip = "A did not crouch (Locomotion.PhysicalCrouch off?)";
                yield break;
            }
            yield return Ask(MpRequest.StandOn);
            if (stepSkip != null)
                yield break;
            float mark = CueMark();
            yield return Wait(1.2f);
            bool felt = CueLog.Capture.Felt(true, CueFeel.Progress, mark, out _);
            // Jump is the dominant hand's primary (Bindings: VRAction.Jump); Script takes "left" (MP run 7 pressed the
            // off hand's, so A never jumped and B was never boosted).
            bool jumper = VRInput.LeftHanded;
            Script(jumper).Primary = true;
            yield return Wait(0.15f);
            Script(jumper).Primary = false;
            yield return Until(() => c.data.sinceJump < 1f, 1f, "A did not jump");
            if (!stepOk)
                yield break;
            yield return Wait(0.5f);
            stepNote = $"crouched under the host and jumped; pressure felt on A: {felt}";
        }
        finally
        {
            VRInput.TestOverride = hands;
        }
    }

    /// <summary>
    /// campfire-far (Campfire.cs:327-338, EveryoneInRange :387-398): A grips an unlit campfire within reach while the
    /// host stays outside its <c>moraleBoostRadius</c>: FriendsFar with the logs red (interact.items), the fire stays
    /// unlit and B sees no Light_Rpc. SKIP when no unlit fire is within walking range.
    /// </summary>
    IEnumerator CampfireFar()
    {
        yield return FreeHands();
        if (!stepOk)
            yield break;
        var c = Character.localCharacter;
        var host = Host();
        Campfire fire = null;
        float bestSq = NearItems * NearItems;
        foreach (var f in FindObjectsByType<Campfire>(FindObjectsSortMode.None))
        {
            float sq = (f.transform.position - c.Center).sqrMagnitude;
            if (
                f.state == Campfire.FireState.Off
                && sq < bestSq
                && host != null
                && Vector3.Distance(host.Center, f.transform.position) > f.moraleBoostRadius
            )
            {
                bestSq = sq;
                fire = f;
            }
        }
        if (fire == null)
        {
            stepSkip = $"no unlit campfire within {NearItems:0} m with the host away from it";
            yield break;
        }
        yield return WalkTo(() => fire.transform.position, 1.2f, true);
        if (!stepOk)
            yield break;
        Face(fire.Center());
        bool left = VRInput.LeftHanded; // the dominant hand
        var logs = fire.Center();
        yield return MoveTo(left, () => logs, 0.5f);
        LookAtHand(left);
        float mark = CueMark();
        yield return GripTo(left, 1f, 0.1f);
        yield return ExpectCue(left, DenyReason.FriendsFar, mark, true, 2f);
        var cue = cueNote;
        Script(left).Grip = 0f;
        if (!stepOk)
            yield break;
        if (fire.state != Campfire.FireState.Off)
            Fail($"{fire.name} lit with the host away");
        else
            stepNote = $"{fire.name} refused: {cue}";
    }

    /// <summary>
    /// spectate (MainCameraMovement.cs:77-84): A passes out with the game's own command on its own character
    /// (Character.PassOut, :986). A passed-out player still counts as spectatable (CharacterData.canBeSpectated,
    /// :323-331), so the game watches A's own body (GetSpecPlayer, :257-260; MP run 7 waited for the host in vain),
    /// and HandleSpecSelection returns before it reads the switch presses (:189): a flick of the stick
    /// (SpectateNext) keeps the view on A and must say so (CantSwitch, player.locomotion's DenyLoneSpectate, at the
    /// hand the action's label names). A stays down until after the checkpoint, so B sees it passed out.
    /// </summary>
    IEnumerator MpSpectate()
    {
        var c = Character.localCharacter;
        if (Host() == null)
        {
            stepSkip = "no host";
            yield break;
        }
        yield return FreeHands();
        if (!stepOk)
            yield break;
        Character.PassOut();
        yield return Until(() => c.data.fullyPassedOut, 8f, "A never fully passed out");
        if (!stepOk)
            yield break;
        yield return Until(
            () => MainCameraMovement.specCharacter == c,
            5f,
            "the view never settled on A's own body"
        );
        if (!stepOk)
            yield break;
        // SpectateNext is the dominant stick up (Bindings); Script takes "left".
        bool stick = VRInput.LeftHanded;
        bool cueLeft = VRInput.LeftHanded;
        if (
            ControllerLabels.ForAction(
                VRAction.SpectateNext,
                VRInput.LeftHanded,
                LocalHold.ItemInLeftHand,
                out var actionLeft,
                out _
            )
        )
            cueLeft = actionLeft;
        float mark = CueMark();
        Script(stick).Stick = Vector2.up;
        yield return Wait(0.2f);
        Script(stick).Stick = Vector2.zero;
        yield return Wait(0.5f);
        if (MainCameraMovement.specCharacter != c)
        {
            Fail("the flick moved the view off A's own body");
            yield break;
        }
        yield return ExpectCue(cueLeft, DenyReason.CantSwitch, mark, false, 0.5f, seen: false);
        if (stepOk)
            stepNote = "spectating A's own body; flick refused: " + cueNote;
    }
}
