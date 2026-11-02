using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Diagnostics;
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
/// feed and reach, help-up, kick, carry, the host's pack, an item stashed into it, pal jump, a far campfire, spectating). Every step ends with
/// a line in <c>a-checkpoints.jsonl</c>, which the observer answers with its own snapshot under the same name. A
/// spawns nothing itself and uses no sandbox, and every action is one the controllers can make, with two exceptions that use
/// the game's own commands on a character the issuer owns: the host is asked (<see cref="MpRequest"/>) to pass out,
/// get webbed, wake, step onto A, wear a pack or spawn an item at A's feet, and A passes itself out (Character.PassOut)
/// to spectate and wakes after.
/// </summary>
internal sealed partial class SelfTestRunner
{
    const float MpMinutes = 30f;

    /// <summary>Hold still after each checkpoint so the observer's snapshot (polled every 0.25 s) sees the same state.</summary>
    const float CheckpointHold = 2f;

    const float NearItems = 40f;

    /// <summary>How far (m) from the head a scripted hand may reach toward a friend: about a real arm.</summary>
    const float MpArmReach = 0.85f;

    /// <summary>How far (m) from the eye the feed steps hold an item out: a bent arm the avatar's ragdoll arm keeps up
    /// with. At the full 0.85 m the arm fell 0.25-0.67 m behind the controller, the hand weld let go and rejoined over
    /// and over, and the item jumped about the mouth so it never rested (MP runs 31 and 32).</summary>
    const float MpFeedHold = 0.6f;

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
            var marker = Path.Combine(MpDirectory, MpDir.Marker);
            bool clear = MpDir.MayClear(
                MpDirectory,
                MpDir.UnixHome(
                    Environment.GetEnvironmentVariable("HOME"),
                    Environment.GetEnvironmentVariable("WINEHOMEDIR")
                ),
                Directory.Exists(MpDirectory),
                File.Exists(marker)
            );
            Directory.CreateDirectory(MpDirectory);
            if (clear)
            {
                File.WriteAllText(marker, "");
                foreach (var old in Directory.GetFiles(MpDirectory, "a-*"))
                    File.Delete(old);
            }
            else
                Plugin.Log.LogWarning(
                    $"[mptest] not deleting old a-* files in {MpDirectory}: the test did not make that folder; use an empty one"
                );
            File.WriteAllText(logPath, Header() + "\n");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Plugin.Log.LogError($"[mptest] cannot prepare {MpDirectory}: {e.Message}");
        }
        Plugin.Log.LogInfo($"[mptest] started, results in {MpDirectory}");
        if (SelfTestFeature.OnlySpec != null)
        {
            // Each MP step writes a checkpoint the vanilla peer answers, and mp-check pairs them in order; a filtered
            // run would leave both sides out of step, so it is refused rather than half-honoured.
            Plugin.Log.LogError(
                $"[mptest] {SelfTestFeature.OnlyFlag} is not supported with the two-player test"
            );
            MpFinish($"{SelfTestFeature.OnlyFlag} is not supported with the two-player test");
            return;
        }
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
        // A host-owned scene button refused for this non-host (WP5 body, SelfTestRunner.World.cs); SKIP when none.
        yield return MpStep("scene-button", 60f, true, MpSceneButton());
        yield return MpStep("board", 400f, false, Board());
        if (!MpReady || Singleton<MapHandler>.Instance == null)
        {
            MpFinish("no level after boarding");
            yield break;
        }
        yield return MpStep("regroup", 60f, true, WalkTo(HostCenter, 1.5f, true));
        // On the beach, not the airport: the corridor B spawns in has a wall a metre off, so there A's body filled B's
        // picture of the ping and no ground lay 3 m out for B's own (#130).
        yield return MpStep("ping", 15f, true, PingStep());
        yield return MpStep("ping-seen", 20f, true, PingSeen());
        yield return MpStep("luggage", 300f, true, OpenLuggage());
        yield return MpStep("grab", 40f, true, GrabNearest(food: false));
        // One item passes along this chain (mpItem): a link after a failed one would judge what that failure left.
        yield return MpStep("drop", 15f, true, DropByButton(), after: "grab");
        yield return MpStep("pickup", 40f, true, PickUpAgain(), after: "drop");
        yield return MpStep("throw", 60f, true, ThrowToHost(), after: "pickup");
        yield return MpStep("eat", 150f, true, Eat());
        // 120 s: the walk back from eat's food to the thrown item can be 100 m (MP run 35 timed out at 60 s on it).
        yield return MpStep("holster", 120f, true, Holster());
        // Five walls at 25 s of walk and 21 s of climb each, with room to finish.
        yield return MpStep("climb", 255f, true, MpClimb());
        yield return MpStep("two-hand", 90f, true, MpTwoHand());
        yield return MpStep("feed", 180f, true, Feed());
        yield return MpStep("feed-fetch-front", 90f, true, FeedFetchFront());
        yield return MpStep("feed-held-ahead", 120f, true, FeedHeldAhead());
        yield return MpSocialSteps();
        MpFinish("script done");
    }

    /// <summary>One checkpoint: the step, then its line in a-checkpoints.jsonl, a screenshot, a short still hold and
    /// the hands back at rest. A passed step keeps its grips, so a held item stays held; a failed or skipped one opens
    /// both, so a grip left closed on a failure cannot hold the next step back.</summary>
    IEnumerator MpStep(string id, float timeout, bool vr, IEnumerator body, string after = null)
    {
        cpExtra.Clear();
        cpItemId = -1;
        string skip =
            vr && !VRSession.Active ? "VR not active"
            : after != null && !StepPassed(after)
                ? after + " did not pass, and this step carries on its item"
            : null;
        if (skip != null)
        {
            Skip(id, skip);
            WriteCheckpoint(id, "skip", skip);
            yield break;
        }
        yield return Run(id, timeout, vr && MpReady ? MpSettled(body) : body);
        var r = report.Steps[report.Steps.Count - 1];
        // A step that did not pass lets go of what it holds before the hands move again: no later step carries it on
        // (StepPassed), and food left in hand fed the host as the next step walked in (MP run 16, #100).
        if ((!r.Passed || r.Skipped) && HeldItem() != null)
            yield return Guard(TapDrop(), Now + 1f, _ => { });
        if (vr)
            yield return HoldingHandSettled();
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
        if (vr && MpReady)
        {
            yield return Guard(BackToHost(id), Now + MpBackSeconds, _ => { });
            Script(!VRInput.LeftHanded).Stick = Vector2.zero;
        }
    }

    /// <summary>Farther than this (m, level) from the host after a step, A walks back before the next.</summary>
    const float MpBackFar = 5f;

    /// <summary>How long (s) the walk back after a step may take.</summary>
    const float MpBackSeconds = 45f;

    /// <summary>
    /// A step that left A far from the host (a fetch, a lip, a timeout mid-walk) walks back, as regroup does, so one
    /// long walk does not fail every step after it (MP run 15, #103: eat walked 175 m off, and reach-deny to carry all
    /// failed "stuck 177.7 m short").
    /// </summary>
    IEnumerator BackToHost(string after)
    {
        var host = Host();
        var c = Character.localCharacter;
        if (host == null || c == null)
            yield break;
        float from = FlatDistance(c.Center, host.Center);
        if (from <= MpBackFar)
            yield break;
        yield return MpWalkAround(HostCenter, 1.5f, 15f, 3);
        Plugin.Log.LogInfo(
            F(
                $"[mptest] back to the host after {after}: from {from:0.0} m to {FlatDistance(c.Center, HostCenter()):0.0} m"
            )
        );
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
        // No glance carried over from the step before (MP run 18: reach-deny's glance at the hand held into reach).
        EndGlance();
        FreeTurn(true);
        FreeTurn(false);
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
        Terrain(from, Vector3.down, 3f, out var hit) ? Vector3.Angle(hit.normal, Vector3.up) : 90f;

    /// <summary>Both empty hands on their tracked poses (up to 1.5 s), named in the checkpoint's <c>settle</c>.</summary>
    IEnumerator FreeHandsSettled()
    {
        var c = Character.localCharacter;
        var driver = c != null ? AvatarPoseDriver.Of(c) : null;
        if (driver == null)
            yield break;
        float Gap(bool left) =>
            driver.HandTarget(left, out var target)
                ? Vector3.Distance(
                    c.refs
                        .ragdoll
                        .partDict[left ? BodypartType.Hand_L : BodypartType.Hand_R]
                        .Rig
                        .position,
                    target
                )
                : float.PositiveInfinity;
        float start = Now;
        yield return WaitFor(
            () => Gap(true) <= HandSettledGap && Gap(false) <= HandSettledGap,
            1.5f
        );
        Extra(
            "settle",
            F(
                $"free hands after {Now - start:0.00} s: left {Gap(true):0.00} m, right {Gap(false):0.00} m off their tracked hands"
            )
        );
    }

    /// <summary>A holding hand's bone off its tracked hand by at most this (m) counts as settled.</summary>
    const float HandSettledGap = 0.1f;

    /// <summary>
    /// Before a checkpoint while A holds an item: wait (1.5 s at most) until A's own holding-hand bone is back on its
    /// tracked hand and the item is welded to it again, so B samples a still hand and not one caught between the hip
    /// slot and rest (MP run 24: the bandage off the weld at 0.40 m and 0.30 m, back at 0.03 m just after the step).
    /// The wait, or its timeout, is the checkpoint's <c>settle</c> note.
    /// </summary>
    IEnumerator HoldingHandSettled()
    {
        var c = Character.localCharacter;
        var driver = c != null ? AvatarPoseDriver.Of(c) : null;
        if (driver == null || c.data.currentItem == null)
            yield break;
        bool left = driver.Pose.ItemInLeftHand;
        var part = c.refs.ragdoll.partDict[left ? BodypartType.Hand_L : BodypartType.Hand_R];
        float Gap() =>
            driver.HandTarget(left, out var target)
                ? Vector3.Distance(part.Rig.position, target)
                : float.PositiveInfinity;
        float start = Now;
        yield return WaitFor(
            () => c.data.currentItem == null || (!driver.HeldReleased && Gap() <= HandSettledGap),
            1.5f
        );
        bool settled =
            c.data.currentItem == null || (!driver.HeldReleased && Gap() <= HandSettledGap);
        Extra(
            "settle",
            settled
                ? F($"hand settled after {Now - start:0.00} s")
                : F(
                    $"hand not settled after {Now - start:0.00} s (own bone {Gap():0.00} m off its tracked hand, off the weld {driver.HeldReleased})"
                )
        );
    }

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
        ReportDegraded();
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
    /// Button ping (Interact/Ping/PingInput.cs): the layout's ping hand (off-hand B/Y) aims its aim pose at the ground
    /// in front of the host and taps the button.
    /// </summary>
    IEnumerator PingStep()
    {
        var c = Character.localCharacter;
        var host = Host();
        if (!PingButtonHand(out bool left))
            yield break;
        var aim = PingSpotInHostsView(c, host);
        MpPingProbe.Last = null;
        PingProbe.Last = null;
        yield return ButtonAim(left, aim, 0.4f);
        yield return ButtonTap(left, aim);
        Script(left).SecondaryTouch = false;
        yield return WaitFor(() => MpPingProbe.Last != null, 2f);
        if (MpPingProbe.Last is not { } point)
        {
            Fail("no ping received");
            yield break;
        }
        Extra("ping", point);
        yield return WaitFor(() => PingProbe.Last != null, 1f);
        stepNote =
            $"ping {Vector3.Distance(point, aim):0.00} m from the aim point; own {PingProbe.Last?.Line ?? "probe wrote nothing"}";
    }

    /// <summary>How far (m) ahead of the host <see cref="PingSpotInHostsView"/> aims, and how far (degrees) off the
    /// host's view: well inside a flat camera's field, on ground it sees.</summary>
    const float PingSpotAhead = 3f;

    const float PingSpotOff = 25f;

    /// <summary>
    /// Ground in the host's view, on the side away from A. The old spot, 1.5 m from the host toward A, lay behind A's
    /// body from the host's camera, so the host's screenshot could say nothing about the marker (#130).
    /// </summary>
    static Vector3 PingSpotInHostsView(Character c, Character host)
    {
        var look = Flat(host.data.lookDirection_Flat);
        var toA = Flat(c.Center - host.Center);
        float side = Vector3.Dot(Vector3.Cross(Vector3.up, look), toA) > 0f ? -1f : 1f;
        if (
            OpenSpot(
                host.Center,
                look,
                new[] { side * PingSpotOff, side * 45f, side * 60f, -side * PingSpotOff },
                c,
                host,
                out var spot
            )
        )
            return spot;
        return host.Center + Quaternion.Euler(0f, side * PingSpotOff, 0f) * look * PingSpotAhead;
    }

    /// <summary>Ground <see cref="PingSpotAhead"/> m from <paramref name="from"/>, at the first of
    /// <paramref name="turns"/> (degrees off <paramref name="dir"/>) that is under open sky or a high roof and in plain
    /// view of both players' heads: a marker someone cannot see tells nothing.</summary>
    static bool OpenSpot(
        Vector3 from,
        Vector3 dir,
        float[] turns,
        Character c,
        Character host,
        out Vector3 spot
    )
    {
        foreach (float turn in turns)
        {
            var at = from + Quaternion.Euler(0f, turn, 0f) * dir * PingSpotAhead;
            if (!Terrain(at + Vector3.up * 2f, Vector3.down, 6f, out var ground))
                continue;
            var over = ground.point + Vector3.up * 0.15f;
            if (
                ground.normal.y < 0.7f
                || Physics.Linecast(
                    c.Head,
                    over,
                    HelperFunctions.terrainMapMask,
                    QueryTriggerInteraction.Ignore
                )
                || Physics.Linecast(
                    host.Head,
                    over,
                    HelperFunctions.terrainMapMask,
                    QueryTriggerInteraction.Ignore
                )
            )
                continue;
            spot = ground.point;
            return true;
        }
        spot = default;
        return false;
    }

    /// <summary>
    /// ping-seen (#130): the host pings ground ahead of A through its own PointPinger (<see cref="MpRequest.Ping"/>) while
    /// A looks at it. <see cref="PingProbe"/> must report a marker from the host on this client: the distance check
    /// passed, its renderer on, its layer in the camera's mask and <c>renderer.isVisible</c>. A reads none of the
    /// marker's looks; its checkpoint's screenshot falls inside the marker's 2 s for those.
    /// </summary>
    IEnumerator PingSeen()
    {
        var c = Character.localCharacter;
        var host = Host();
        // Turned off the line to the host, so the spot is clear of the host's body from both cameras.
        if (
            !OpenSpot(
                c.Center,
                Flat(c.Center - host.Center),
                new[] { 60f, -60f, 100f, -100f, 140f, -140f },
                c,
                host,
                out var spot
            )
        )
        {
            stepSkip = "no open ground 3 m out in view of both players for the host to ping";
            yield break;
        }
        Face(spot);
        LookAtPoint(spot);
        yield return Wait(0.5f);
        PingProbe.Last = null;
        yield return Ask(MpRequest.Ping, from: spot);
        if (stepSkip != null)
        {
            EndGlance();
            yield break;
        }
        yield return WaitFor(() => PingProbe.Last is { Local: false }, 2f);
        EndGlance();
        if (PingProbe.Last is not { Local: false } seen)
        {
            Fail("no ping from the host reached this client within 2 s");
            yield break;
        }
        Extra("pingSeen", seen.Marker && seen.Visible);
        if (!seen.Expected)
            Fail("the game's own distance check drops this ping: " + seen.Line);
        else if (!seen.Marker)
            Fail("no marker for the host's ping: " + seen.Line);
        else if (!seen.RendererOn || !seen.LayerSeen)
            Fail("the host's marker cannot draw: " + seen.Line);
        else if (!seen.Visible || !seen.InView)
            Fail("the host's marker is not visible: " + seen.Line);
        else
            stepNote = seen.Line;
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
            yield return MpWalkAround(() => kiosk.transform.position, 1.6f, 15f, 6);
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
        // A second grip after a miss, the hand put back on the face, as a player tries again; the note says what the
        // grip decided and what the game held (MP run 13: the grip at 1.0 m opened nothing and said nothing why).
        string gripNote = null;
        for (int attempt = 0; attempt < 2 && !pass.isOpen; attempt++)
        {
            if (attempt > 0)
            {
                yield return MpToKioskFace(kiosk, left, s => why = s);
                if (why != null)
                    break;
            }
            yield return GripTo(left, 1f, 0.1f);
            yield return Wait(0.2f);
            var interaction = Interaction.instance;
            var hovered = interaction != null ? interaction.currentHovered : null;
            bool held =
                interaction != null && interaction.currentHeldInteractible is AirportCheckInKiosk;
            gripNote = F(
                $"{PeakXR.Interact.PhysicalInteraction.LastGripNote}; hovered {(hovered != null ? hovered.GetName() : "nothing")}, kiosk held {held}"
            );
            yield return WaitFor(() => pass.isOpen, GameTiming.Budget(cast, 1f));
            yield return GripTo(left, 0f, 0.1f);
        }
        if (!pass.isOpen)
        {
            failed(
                F(
                    $"the grip did not open the boarding pass ({Vector3.Distance(Character.localCharacter.Center, kiosk.Center()):0.0} m from the kiosk; {why ?? gripNote})"
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
            if (Terrain(spot, Vector3.down, top - bounds.min.y + 1f, out var floor))
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
                    $"no spot at the kiosk reached on foot ({FlatDistance(c.Center, bounds.center):0.0} m from it, stopped at {c.Center:F1}, kiosk at {bounds.center:F1})"
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

    /// <summary>The last report line for <paramref name="id"/> passed.</summary>
    bool StepPassed(string id)
    {
        for (int i = report.Steps.Count - 1; i >= 0; i--)
        {
            if (report.Steps[i].Id == id)
                return report.Steps[i].Passed && !report.Steps[i].Skipped;
        }
        return false;
    }

    IEnumerator GrabNearest(bool food)
    {
        yield return FreeHands();
        if (!stepOk)
            yield break;
        var item = NearestGround(i => IsFood(i) == food);
        bool fallback = item == null;
        if (fallback)
            item = NearestGround(_ => true);
        // Reached for again when it slid off under the hand, then the next one: on a slope a player does the same (#100).
        yield return GrabReachable(i => fallback || IsFood(i) == food, item, nextOnMiss: true);
        mpItem = HeldItem();
        if (fallback && mpItem != null)
            Note(
                $"{stepNote ?? "grabbed"}; no {(food ? "food" : "non-food")} item near, took {mpItem.name} instead"
            );
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

    /// <summary>Picks up the item the drop step left; another item would not test the pickup of a dropped one.</summary>
    IEnumerator PickUpAgain()
    {
        var item = OnGround(mpItem);
        if (item == null)
        {
            Fail("the dropped item is no longer on the ground to pick up");
            yield break;
        }
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
            yield return GrabReachable(_ => true, OnGround(mpItem), nextOnMiss: true);
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
        var rig = VRRig.Instance;
        int tried = 0;
        // A wall where the game itself let go during the pull, from ClimbWall's fail note.
        string letGoNote = null;
        int passedOver = 0;
        foreach (var (face, stand, profile) in walls)
        {
            if (tried++ >= MpWallTries)
                break;
            yield return MpWalkBounded(() => stand, 0.4f, MpWallWalkSeconds);
            var miss = stand - c.Center;
            miss.y = 0f;
            if (miss.magnitude > 0.6f)
                continue;
            WallProfile = profile;
            rig.RotateYaw(
                Vector3.SignedAngle(Flat(rig.Head.forward), -Flat(face.normal), Vector3.up)
            );
            yield return Wait(0.6f);
            climbLetGo = false;
            yield return ClimbWall(face);
            Script(VRInput.LeftHanded).Grip = 0f;
            yield return WaitFor(() => !c.data.isClimbing, 2f);
            yield return Wait(1f);
            if (stepOk && passedOver > 0)
                stepNote += F(
                    $"; passed over {passedOver} wall(s) the game let go of: {letGoNote}"
                );
            if (stepOk || !climbLetGo)
                yield break;
            passedOver++;
            // The game let go of this face during the pull (#60: SampleWall refuses a facet flatter than 50 degrees
            // while the stick moves, on a coarse palm mesh): a bad wall, and vanilla's rule. On to the next.
            letGoNote = failNote;
            failNote = null;
            stepOk = true;
            Plugin.Log.LogInfo(
                "[mptest] climb passes over a wall the game let go of: " + letGoNote
            );
        }
        if (letGoNote != null)
        {
            Fail(letGoNote + F($" (the last of {tried} wall(s) tried)"));
            yield break;
        }
        stepSkip =
            walls.Count == 0
                ? $"no wall steep enough to climb ({MinWallAngle:0} deg from up or more from the hold to 1 m above) with walkable ground in front within {MpWallRange:0} m; online no test wall can be placed"
                : $"none of the {tried} nearest climbable walls ({walls.Count} within {MpWallRange:0} m) was reachable on foot";
    }

    /// <summary>The last <see cref="ClimbWall"/> failed because the game ended the climb during the pull with stamina
    /// left and the body under control: the face, not the arm, ended it.</summary>
    bool climbLetGo;

    /// <summary>Walls the climb step tries, the farthest it looks (m), and how long each walk may take: 5 walks of up
    /// to 25 s, plus the settle (about 14 s) and the climb itself (about 20 s), fit the step's 200 s. MP run 18 found
    /// one wall within 40 m and skipped, once the climb kept to faces under 68 degrees. On the same level a start 60 m
    /// from the nearest found none, and a walk covers 100 m in its 25 s (#60).</summary>
    const int MpWallTries = 5;

    const float MpWallRange = 100f;

    const float MpWallWalkSeconds = 25f;

    /// <summary>
    /// Climbable faces with walkable ground in front within <see cref="MpWallRange"/> of A, nearest first: <see cref="WallsFrom"/>'s best
    /// face from A and from rings of seeds 6, 15, 30, 45, 60 and 80 m around A, each standing spot at least 2 m from the others.
    /// </summary>
    /// <summary>Steepest the face may lean back over the climber along the pull (deg from up): past it is an overhang
    /// the arm pull cannot rise up (MP run 26: 77/72/72 then 113/107 degrees 0.5-1 m above the hold, the body sank).</summary>
    const float MaxPullAngle = 85f;

    /// <summary>
    /// The face stays climbable along the whole 1 m arm pull: at the hold and every 0.25 m above it, a level ray from
    /// in front meets terrain between <see cref="MinWallAngle"/> and <see cref="MaxPullAngle"/> from up, so no overhang
    /// waits within the pull.
    /// </summary>
    static bool PullClimbable(RaycastHit face)
    {
        var flat = Flat(face.normal);
        if (flat.sqrMagnitude < 1e-4f)
            return false;
        var from = face.point + flat * 0.6f;
        for (float up = 0f; up <= 1.001f; up += 0.25f)
        {
            if (!Terrain(from + Vector3.up * up, -flat, 1.6f, out var hit))
                return false;
            float a = Vector3.Angle(hit.normal, Vector3.up);
            if (a < MinWallAngle || a > MaxPullAngle)
                return false;
        }
        return true;
    }

    static List<(RaycastHit Face, Vector3 Stand, string Profile)> MpWalls(Character c)
    {
        var found = new List<(RaycastHit Face, Vector3 Stand, string Profile)>();
        var home = c.Center;
        void From(Vector3 seed)
        {
            float best = float.MaxValue;
            RaycastHit face = default;
            var stand = Vector3.zero;
            WallsFrom(seed, home, 1.2f, ref best, ref face, ref stand);
            if (best == float.MaxValue || Vector3.Distance(stand, home) > MpWallRange)
                return;
            if (!PullClimbable(face))
                return;
            foreach (var other in found)
            {
                if (Vector3.Distance(other.Stand, stand) < 2f)
                    return;
            }
            found.Add((face, stand, WallProfile));
        }
        From(home);
        foreach (float radius in new[] { 6f, 15f, 30f, 45f, 60f, 80f })
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
    /// stanchion, a bench or along a wall. When a try ends short, A walks 3 m along the clear heading nearest the
    /// goal's (<see cref="ClearHeading"/>), keeping to the side that last worked so it follows a wall instead of
    /// pacing in front of it, then tries again; at most <paramref name="detours"/> times, each walk bounded by
    /// <paramref name="seconds"/>. MP run 16 and 12: a 2.5 m sidestep that swapped sides every try, judged by one ray at
    /// chest height, left A boxed in at the airport spawn for 75 s and pacing a wall 12 m short of the kiosk.
    /// </summary>
    IEnumerator MpWalkAround(Func<Vector3> target, float stop, float seconds, int detours = 3)
    {
        var c = Character.localCharacter;
        float side = 1f;
        var shut = new List<Vector3>();
        for (int i = 0; ; i++)
        {
            yield return MpWalkBounded(target, stop, seconds);
            if (FlatDistance(c.Center, target()) <= stop + 0.2f || i >= detours)
                yield break;
            var to = target() - c.Center;
            to.y = 0f;
            if (to.sqrMagnitude < 1e-4f)
                yield break;
            if (ClearHeading(c, to.normalized, ref side, shut) is not { } dir)
                yield break;
            var from = c.Center;
            var aside = from + dir * MpDetourStep;
            yield return MpWalkBounded(() => aside, 0.4f, 4f);
            // The walk is the measure: a heading the cast called clear but the body could not walk is shut for the
            // rest of this walk (MP run 13: the same 30 degree heading, blocked 0.3 m out by something the cast did not
            // see, was tried six times and A stayed put for 220 s, 53 m from the kiosk).
            if (FlatDistance(c.Center, from) < 1f)
            {
                shut.Add(dir);
                side = -side;
            }
        }
    }

    /// <summary>How far one detour walks (m).</summary>
    const float MpDetourStep = 3f;

    /// <summary>
    /// The heading nearest <paramref name="goal"/> (flat, normalised) that a body can walk <see cref="MpDetourStep"/>
    /// along: a capsule from knee to chest height swept 30 to 150 degrees off the goal, <paramref name="side"/> first at
    /// each angle (+1 turns right), passing over a heading within 20 degrees of one in <paramref name="shut"/>. Sets
    /// <paramref name="side"/> to the side taken; null when every heading is shut.
    /// </summary>
    static Vector3? ClearHeading(Character c, Vector3 goal, ref float side, List<Vector3> shut)
    {
        var top = c.Center + Vector3.up * 0.3f;
        var bottom = c.Center + Vector3.down * 0.4f;
        for (int a = 30; a <= 150; a += 30)
        {
            for (int k = 0; k < 2; k++)
            {
                float s = k == 0 ? side : -side;
                var dir = Quaternion.Euler(0f, s * a, 0f) * goal;
                if (Shut(shut, dir))
                    continue;
                if (
                    !Physics.CapsuleCast(
                        top,
                        bottom,
                        0.25f,
                        dir,
                        MpDetourStep,
                        HelperFunctions.AllPhysicalExceptCharacter,
                        QueryTriggerInteraction.Ignore
                    )
                )
                {
                    side = s;
                    return dir;
                }
            }
        }
        return null;
    }

    static bool Shut(List<Vector3> shut, Vector3 dir)
    {
        foreach (var d in shut)
        {
            if (Vector3.Dot(d, dir) > 0.94f)
                return true;
        }
        return false;
    }

    IEnumerator MpTwoHand()
    {
        yield return FreeHands();
        if (!stepOk)
            yield break;
        var info = new ItemHoldInfo();
        // The nearest reachable one, walking around what blocks the way and passing over one it cannot reach (MP run
        // 23: the walk to the nearest stopped 0.7 m short against a blocker and the step failed).
        yield return GrabReachable(
            i => info.Bind(i) && info.Profile.Handedness != Handedness.OneHand,
            spawn: MpTwoHandItem
        );
        if (!stepOk || stepSkip != null)
            yield break;
        yield return TwoHandHeld();
        if (!stepOk)
            yield break;
        // The step's last grip opening drops the item: the checkpoint waits for the free hands, as it does for a
        // holding hand, or it reads arms still on the way (#145: own bones 0.38 m off the tracked hands).
        yield return WaitFor(() => HeldItem() == null, 1f);
        yield return MpRest();
        yield return FreeHandsSettled();
    }

    /// <summary>
    /// Food held at the host's mouth with the trigger up: after the mouth dwell (<see cref="HandOver.MouthDwell"/>, issue
    /// #14) interact.social's feed (Social/SocialHands.cs UpdateFeed) holds use-secondary, the game's feed path runs (Item.SendFeedDataRPC, Item.cs:996; Character.FeedItem, Character.cs:1637 ->
    /// GetFedItemRPC :1643 on the host, who needs to do nothing).
    /// </summary>
    IEnumerator Feed()
    {
        yield return FreeHands();
        if (!stepOk)
            yield break;
        yield return GrabReachable(
            i => IsFood(i) && i.canUseOnFriend,
            null,
            NearItems,
            3,
            spawn: MpFeedFood
        );
        if (!stepOk || stepSkip != null)
            yield break;
        var item = HeldItem();
        string foodName = item.name;
        cpItemId = item.itemID;
        bool left = LocalHold.ItemInLeftHand;
        var c = Character.localCharacter;
        // The game's own mark of a finished feed on the feeder (Item.FinishCastSecondary, Item.cs:1043): a drop or a
        // lost grip also takes the item from the hand, and only this tells them apart.
        float consumedBefore = c.data.lastConsumedItem;
        // Carried low at the side and a little back on the walk in, as a player carries food they are not offering
        // (MP run 13, #93: held at chest height it passed the host's mouth during a stop between stick pulses; MP run
        // 14: 0.15 m ahead it still met the mouth of a host standing lower on the slope and fed them in a pause).
        yield return CarryFoodBack(left);
        // The walk's stop is on A's mass centre; its eye stands up to 0.28 m ahead of that (MP runs 18 and 19).
        yield return WalkTo(HostCenter, MpSqueezeEye + 0.3f, true);
        if (!stepOk)
            yield break;
        var host = Host();
        var rig = VRRig.Instance;
        yield return MpStandOff(host);
        bool walkFed = c.data.lastConsumedItem != consumedBefore;
        if (item == null || HeldItem() != item)
        {
            Fail(
                walkFed
                    ? "the host was fed on the way in, before the squeeze beside the mouth"
                    : "the food left the hand on the walk in"
            );
            yield break;
        }
        // The squeeze starts with the game's use-on-friend released, so it tests the squeeze alone (MP run 14, #93: a
        // pause on the walk in fed the multi-use ScoutCookies, which stays in hand, and the band only read that).
        string walkNote = "";
        if (walkFed || item.isUsingSecondary || c.input.useSecondaryIsPressed)
        {
            walkNote = F(
                $"the walk in {(walkFed ? "fed the host" : "held use-on-friend")} (gap {Social.SocialHands.FeedGap(item, HostPart(host, BodypartType.Head)):0.00} m, hand-over feeding {Social.SocialHands.Instance.FeedReady}); "
            );
            yield return CarryFoodBack(left);
            yield return WaitFor(
                () => item == null || (!item.isUsingSecondary && !c.input.useSecondaryIsPressed),
                2f
            );
            if (item == null || HeldItem() != item || item.isUsingSecondary)
            {
                Fail(walkNote + "use-on-friend did not let go with the food held back");
                yield break;
            }
            consumedBefore = c.data.lastConsumedItem;
        }
        yield return GripTo(left, 1f, 0.1f);
        float best = float.MaxValue;
        float atMouth = 0f;
        // The item's place on the controller, taken once while it rests in the hand: reading it every frame from
        // the item fed the weld's lag back into the aim (MP run 25: the hand jumped 0.9 m as the weld let go and
        // rejoined, and the item left the mouth).
        var grip = HeldGrip.Of(rig, Script(left), item);
        // First a squeeze just short of the mouth, outside the feed zone: it must not feed and must say "closer"
        // (OutOfReach) with the ring on the host's mouth, never nothing (HandOver.Context). Only the hand moves, from
        // MpStandOff's stance: the bag's near end stays clear of A's own mouth, where a squeeze is an eat try and stays
        // quiet (HeldGestures.AwayFromMouth), and the hand clear of the host's face (MP run 12, #93).
        float bandMark = -1f;
        float placedFor = 0f;
        float bandEnd = Now + MpBandBudget;
        // The band is on the item's nearest point (the zone's measure, SocialHands.FeedGap), whatever its length. It
        // starts past the zone's exit radius, not its entry: an item that once came within FeedRadius stays in the zone
        // out to FeedExitRadius, and a squeeze there feeds (MP run 17, #111: the band's 0.26 m floor sat inside the 0.30 m
        // exit, and the host was fed). It ends inside NearFriendRadius, where the refusal says "closer".
        const float bandLow = MpBandLow;
        const float bandHigh = MpBandHigh;
        var holdHand = PeakXR.Interact.Physical.ItemGestures.HoldingHand(rig);
        float ownGap = 0f;
        string pressNote = null;
        // The first frame the game's use-on-friend runs in the band: which path pressed it, and the gaps then.
        string useNote = null;
        float bandMin = float.MaxValue,
            bandMax = 0f;
        // The nearest the item came to the mouth before the squeeze, A's eye from the mouth, the steps in and how long
        // the hand has been out at its full hold and still short of the band.
        float approachMin = float.MaxValue;
        float bandEye = 0f;
        int bandSteps = 0;
        float shortFor = 0f;
        bool ringInBand = false;
        // A destroyed item is == null, and so is HeldItem() once it is gone: test the item first (MP run 17, #111).
        while (Now < bandEnd && item != null && HeldItem() == item && host != null)
        {
            var mouthNow = HostPart(host, BodypartType.Head);
            bandEye = Vector3.Distance(rig.Head.position, mouthNow);
            float d = Social.SocialHands.FeedGap(item, mouthNow);
            if (bandMark < 0f)
                approachMin = Mathf.Min(approachMin, d);
            // The hand's speed comes from the real gap, so it never runs ahead of an arm still catching up (MP run 19,
            // #119: a standoff wound 1 cm a frame while the food rose from the carry put the target on the mouth).
            var fullHold =
                VRInput.HeadPosition
                + Vector3.ClampMagnitude(
                    rig.Origin.InverseTransformPoint(mouthNow) - VRInput.HeadPosition,
                    MpFeedHold
                );
            float speed = HandScript.BandSpeed(d, bandLow, bandHigh);
            SetHand(
                left,
                HandScript
                    .Approach(
                        Script(left).Position.N(),
                        (speed >= 0f ? fullHold : VRInput.HeadPosition).N(),
                        Mathf.Abs(speed),
                        Time.unscaledDeltaTime
                    )
                    .U()
            );
            shortFor =
                d > bandHigh && Vector3.Distance(Script(left).Position, fullHold) < 0.02f
                    ? shortFor + Time.unscaledDeltaTime
                    : 0f;
            // Too far off for the full hold: a short step with the food carried back, never walked in held out.
            if (bandMark < 0f && shortFor >= 1f && bandSteps < 3)
            {
                bandSteps++;
                shortFor = 0f;
                yield return CarryFoodBack(left);
                var toward = mouthNow - c.Center;
                toward.y = 0f;
                var to = c.Center + toward.normalized * (d - 0.5f * (bandLow + bandHigh) + 0.1f);
                yield return MpWalkBounded(() => to, 0.1f, 3f);
                Face(HostPart(host, BodypartType.Head));
                bandEnd = Now + MpBandBudget;
                continue;
            }
            if (holdHand != null)
                ownGap = PeakXR.Interact.Physical.HeldGestures.OwnMouthGap(
                    holdHand,
                    left,
                    PeakXR.Interact.Physical.HeldGestures.OwnMouth(rig.Head)
                );
            placedFor =
                d > bandLow
                && d < bandHigh
                && ownGap >= PeakXR.Interact.Physical.HeldGestures.AwayFromMouth + 0.05f
                    ? placedFor + Time.unscaledDeltaTime
                    : 0f;
            // Past the dwell with the trigger up, the ring already marks the mouth (#112).
            ringInBand |=
                placedFor >= 0.4f && Social.SocialHands.Instance.FeedState == HandOverState.Hint;
            if (bandMark < 0f && placedFor >= 0.5f)
            {
                bandMark = CueMark();
                Script(left).Trigger = 1f;
            }
            if (bandMark >= 0f)
            {
                bandMin = Mathf.Min(bandMin, d);
                bandMax = Mathf.Max(bandMax, d);
            }
            if (useNote == null && (item.isUsingSecondary || c.input.useSecondaryIsPressed))
                useNote = F(
                    $"use-on-friend on {(bandMark >= 0f ? "after" : "before")} the squeeze at gap {d:0.00} m, hand-over feeding {Social.SocialHands.Instance.FeedReady}, hand-over owns the trigger {Social.SocialHands.Instance.HandOverOwnsTrigger}, trigger {VRInput.Hand(left).Trigger:0.00}"
                );
            // What the refusal reads (HeldGestures.MouthHint), the frame after the press's use pass.
            if (
                bandMark >= 0f
                && pressNote == null
                && PeakXR.Interact.PhysicalInteraction.UsePressed(left)
            )
                pressNote = F(
                    $"at the press: gap {d:0.00} m to the host's mouth, {ownGap:0.00} m to A's own, hand-over owns the trigger {Social.SocialHands.Instance.HandOverOwnsTrigger}"
                );
            if (bandMark >= 0f && CueMark() - bandMark >= 0.4f)
                break;
            yield return null;
        }
        Script(left).Trigger = 0f;
        // What the zone saw before and during the squeeze, for every verdict below.
        string bandGaps = F(
            $"before the squeeze the food came within {approachMin:0.00} m of the host's mouth (zone in {Social.SocialHands.FeedRadius:0.00}, out {Social.SocialHands.FeedExitRadius:0.00}), {(bandMark >= 0f ? $"squeezed {bandMin:0.00}-{bandMax:0.00} m off" : "never squeezed")}, A's eye {bandEye:0.00} m from the mouth, {bandSteps} step(s) in"
        );
        Plugin.Log.LogInfo("[mptest] feed band: " + bandGaps);
        if (item == null || item.isUsingSecondary || c.data.lastConsumedItem != consumedBefore)
        {
            Fail(
                F(
                    $"{walkNote}the host was fed {(bandMark >= 0f ? "by the squeeze beside the mouth" : "before the squeeze, as the food came beside the mouth")}: {bandGaps}; {useNote ?? "no use-on-friend seen in the band"}; {pressNote ?? "the trigger made no use press"}"
                )
            );
            yield break;
        }
        if (bandMark < 0f)
        {
            Fail(
                F(
                    $"the food never rested {bandLow:0.00}-{bandHigh:0.00} m from the host's mouth, clear of A's own ({ownGap:0.00} m), for the beside-the-mouth squeeze: {bandGaps}"
                )
            );
            yield break;
        }
        if (!ringInBand)
        {
            Fail("no ring on the host's mouth with the food rested just short of it: " + bandGaps);
            yield break;
        }
        yield return ExpectCue(left, DenyReason.OutOfReach, bandMark, false, 1.5f);
        if (!stepOk)
        {
            failNote += "; " + (pressNote ?? "the trigger made no use press (muted)");
            yield break;
        }
        var bandCue = cueNote;
        // From here on: the mouth dwell can feed during the walk back in, before the hold below starts (#119).
        void SampleMouth()
        {
            if (item == null || HeldItem() != item || host == null)
                return;
            float gapNow = Social.SocialHands.FeedGap(item, HostPart(host, BodypartType.Head));
            best = Mathf.Min(best, gapNow);
            if (gapNow <= 0.18f)
                atMouth += Time.unscaledDeltaTime;
        }
        yield return Sampled(Wait(0.3f), SampleMouth);
        // Within arm's reach of the mouth again before the hold (MP run 15b, #106: A stood 1.09 m off and the 0.6 m hold
        // kept the item 0.30 m from the host's head), the food carried back so the step does not bring it there.
        yield return Sampled(CarryFoodBack(left), SampleMouth);
        yield return Sampled(MpStepToMouth(host), SampleMouth);
        string fedBy =
            c.data.lastConsumedItem != consumedBefore
                ? "fed by the mouth dwell on the way back in, before the hold; "
                : "fed by resting at the mouth; ";
        Face(HostPart(host, BodypartType.Head));
        float holdFrom = Vector3.Distance(rig.Head.position, HostPart(host, BodypartType.Head));
        string standNote = F(
            $"A's eye {holdFrom:0.00} m from the host's mouth ({FlatDistance(c.Center, host.Center):0.00} m level) at the hold"
        );
        if (holdFrom > MpFeedReach)
        {
            Fail(
                "not in reach for the mouth hold after stepping in: "
                    + standNote
                    + "; "
                    + mouthStepNote
            );
            yield break;
        }
        // Feeding a friend is the secondary use: its own hold time, after up to 6 s reaching the friend's head and the
        // mouth dwell. A hand-over (SocialHands.UpdateFeed, HandOver): food resting at the mouth feeds with the trigger
        // up, as at your own mouth.
        float end = Now + GameTiming.Budget(GameTiming.FeedSeconds(item), 8f);
        while (
            item != null
            && HeldItem() == item
            && Now < end
            && host != null
            && c.data.lastConsumedItem == consumedBefore
        )
        {
            var mouth = HostPart(host, BodypartType.Head);
            // The zone's own measure: the item's nearest point, not its centre (MP run 27: a lollipop's centre never
            // came within 0.2 m while its candy reached the mouth).
            SampleMouth();
            // Hold the item, not the hand, on the moving mouth every frame: the hand goes where the item's offset from
            // the controller puts the item on the mouth (MP run 19: chasing the lagging item kept it at the mouth for
            // only 0.4 s in all). Eased only while still far.
            var goal = rig.Origin.InverseTransformPoint(mouth - grip.World(rig, Script(left)));
            // A real arm's length from the head, as feed-refuse (a host down a slope is not fed through the floor).
            goal =
                VRInput.HeadPosition
                + Vector3.ClampMagnitude(goal - VRInput.HeadPosition, MpFeedHold);
            SetHand(
                left,
                Vector3.Distance(goal, Script(left).Position) > 0.25f
                    ? HandScript
                        .Approach(Script(left).Position.N(), goal.N(), 1.5f, Time.unscaledDeltaTime)
                        .U()
                    : goal
            );
            yield return null;
        }
        // A feed the hold started near the end of the budget finishes its use (Item.FinishCastSecondary, Item.cs:1043)
        // before the verdict, so the step sees the feed it caused.
        if (c.data.lastConsumedItem == consumedBefore && item != null && item.isUsingSecondary)
            yield return WaitFor(
                () =>
                    item == null
                    || !item.isUsingSecondary
                    || c.data.lastConsumedItem != consumedBefore,
                GameTiming.FeedSeconds(item) + 0.5f
            );
        // Fed: the game consumes the item and unequips it a few hundred ms later (Consume, then EquipSlotRpc on B 0.2 s
        // after GetFedItemRPC in MP run 17); the checkpoint waits for that, so it does not list an item B already saw
        // gone.
        if (c.data.lastConsumedItem != consumedBefore)
            yield return WaitFor(() => HeldItem() != item, 2f);
        var note = F(
            $"{foodName} closest {GapNote(best)} to the host's head after the squeeze, {atMouth:0.0} s at the mouth, trigger up; {standNote}"
        );
        if (c.data.lastConsumedItem == consumedBefore)
            Fail(
                (item != null && HeldItem() == item ? "not fed: " : "item left the hand unfed: ")
                    + note
            );
        else
            stepNote = walkNote + "beside the mouth: " + bandCue + "; " + fedBy + note;
    }

    /// <summary>
    /// feed-fetch-front: food in the feed zone with its controller well off the mouth, both players still, feeds nobody
    /// (<see cref="HandOver.PlaceRadius"/>). #119's run got there by chance: the host's body held a bag fetched at their
    /// feet in the zone with the controller away. Fetched the same way, then carried past the host's head, the head keeps
    /// the bag on A's side of it, and its place on the controller stops past the limit. <see cref="SpawnSide"/> took this
    /// case out of the other fetches. The carry to the side the host stood on put the controller at the mouth (0.00 and
    /// 0.10 m in both pairings' runs), where a feed is fair, so the step never reached its check.
    /// </summary>
    IEnumerator FeedFetchFront()
    {
        yield return FreeHands();
        if (!stepOk)
            yield break;
        // A 0.7 m spawn along the line then lands about 0.5 m from the host (#119's run: 0.54 m).
        yield return WalkTo(HostCenter, 1.2f, true);
        if (!stepOk)
            yield break;
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        var host = Host();
        yield return GrabReachable(
            i => IsFood(i) && i.canUseOnFriend,
            null,
            NearItems,
            3,
            spawn: MpFood,
            spawnSide: Flat(host.Center - c.Center)
        );
        if (!stepOk || stepSkip != null)
            yield break;
        var item = HeldItem();
        cpItemId = item.itemID;
        bool left = LocalHold.ItemInLeftHand;
        float consumedBefore = c.data.lastConsumedItem;
        float closest = float.MaxValue;
        float place = float.MaxValue;
        float inZone = 0f;
        bool used = false;
        void Sample()
        {
            if (item == null || HeldItem() != item || host == null)
                return;
            var mouth = HostPart(host, BodypartType.Head);
            float gap = Social.SocialHands.FeedGap(item, mouth);
            closest = Mathf.Min(closest, gap);
            if (gap <= Social.SocialHands.FeedRadius)
                inZone += Time.unscaledDeltaTime;
            place = Mathf.Min(place, Social.SocialHands.FeedPlaceGap(item, mouth));
            used |= item.isUsingSecondary || c.input.useSecondaryIsPressed;
        }
        bool Fed() => item == null || c.data.lastConsumedItem != consumedBefore;
        string Seen() =>
            F(
                $"the food came within {GapNote(closest)} of the host's mouth, {inZone:0.0} s in the zone at rest, its place on the controller no nearer than {GapNote(place)} (limit {HandOver.PlaceRadius:0.00}), {FlatDistance(c.Center, host.Center):0.00} m level"
            );
        Face(HostPart(host, BodypartType.Head));
        yield return Sampled(MoveTrack(left, FetchFrontLow(left), 0.3f), Sample);
        yield return Sampled(MpStepToMouth(host), Sample);
        Face(HostPart(host, BodypartType.Head));
        if (Fed() || used)
        {
            stepSkip = "the walk in fed the host, so the stance never came: " + Seen();
            yield break;
        }
        var grip = HeldGrip.Of(rig, Script(left), item);
        float past = FetchFrontPast;
        Vector3 PastHead()
        {
            var mouth = HostPart(host, BodypartType.Head);
            var goal = mouth + Flat(mouth - rig.Head.position) * past;
            return rig.Origin.InverseTransformPoint(goal - grip.World(rig, Script(left)));
        }
        // Past the mouth at a hand's speed, never resting on the way, on until its place is clear of the limit.
        for (float end = Now + 3f; Now < end && !Fed() && HeldItem() == item; )
        {
            var goal = PastHead();
            bool clear =
                Social.SocialHands.FeedPlaceGap(item, HostPart(host, BodypartType.Head))
                >= HandOver.PlaceRadius + 0.08f;
            if (clear && Vector3.Distance(Script(left).Position, goal) < 0.03f)
                break;
            if (!clear && Vector3.Distance(Script(left).Position, goal) < 0.03f)
                past = Mathf.Min(past + 0.5f * Time.unscaledDeltaTime, FetchFrontMostPast);
            SetHand(
                left,
                HandScript
                    .Approach(Script(left).Position.N(), goal.N(), 1.5f, Time.unscaledDeltaTime)
                    .U()
            );
            yield return null;
        }
        closest = place = float.MaxValue;
        inZone = 0f;
        used = false;
        // Past what the dwell and the cast took in #119's run (0.3 s, then 1.15 s in all).
        for (float end = Now + 3f; Now < end && !Fed() && HeldItem() == item; )
        {
            SetHand(left, PastHead());
            Sample();
            yield return null;
        }
        var note = Seen();
        bool fed = Fed();
        if (!fed && HeldItem() == item)
            yield return MoveTrack(left, FetchFrontLow(left), 0.3f);
        if ((fed || used) && place <= HandOver.PlaceRadius)
            stepSkip = "the hand stayed at the host's mouth, so the feed was fair: " + note;
        else if (fed || used)
            Fail($"the host was {(fed ? "fed" : "getting fed")} with the controller away: {note}");
        else if (inZone < HandOver.MouthDwell)
            stepSkip =
                "the head did not keep the food in the zone, so no feed proves nothing: " + note;
        else
            stepNote = "no feed: " + note;
    }

    /// <summary>feed-fetch-front's carry: low at the holding side, its place well under a host of the same height.</summary>
    static Vector3 FetchFrontLow(bool left)
    {
        var head = VRInput.HeadPosition;
        var right = VRInput.HeadRotation * Vector3.right;
        return head + (left ? -right : right) * 0.25f + Vector3.down * 0.85f;
    }

    /// <summary>How far (m) past the host's mouth feed-fetch-front first sends the food's place on the controller, and
    /// the most: the bag's box counts in the place's gap, so the first may read inside the limit.</summary>
    const float FetchFrontPast = 0.35f;

    const float FetchFrontMostPast = 0.9f;

    /// <summary>A measured gap, or says none was: <c>float.MaxValue</c> is the start of a minimum no frame lowered, and
    /// printed as a number it reads as a result (#119).</summary>
    static string GapNote(float gap) => gap == float.MaxValue ? "not measured" : F($"{gap:0.00} m");

    /// <summary>
    /// feed-held-ahead: food carried ahead at chest height, no carry-back, walked in until it meets the host, then raised
    /// to their mouth. #14's "feeding is hard": the body weld keeps it on them with the hand short of its controller, and
    /// it still has to feed (<see cref="HandOver.HeldOffAtSpot"/>). Nothing may start while A walks (#93).
    /// </summary>
    IEnumerator FeedHeldAhead()
    {
        yield return FreeHands();
        if (!stepOk)
            yield break;
        yield return FetchFood(i => IsFood(i) && i.canUseOnFriend);
        if (!stepOk || stepSkip != null)
            yield break;
        var item = HeldItem();
        cpItemId = item.itemID;
        bool left = LocalHold.ItemInLeftHand;
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        float consumedBefore = c.data.lastConsumedItem;
        HeadFrame(out var head, out var forward, out var right);
        yield return MoveTrack(
            left,
            head + (left ? -right : right) * 0.15f + Vector3.down * 0.35f + forward * 0.4f,
            0.3f
        );
        var grip = HeldGrip.Of(rig, Script(left), item);
        float startSpeed = -1f;
        var host = Host();
        var driver = AvatarPoseDriver.Of(c);
        float best = float.MaxValue;
        float placeBest = float.MaxValue;
        float keptFor = 0f;
        // From the walk on: the dwell often feeds during the step in, on the host's body keep, before the raise (#112).
        void Sample()
        {
            if (item == null || HeldItem() != item || host == null)
                return;
            var mouth = HostPart(host, BodypartType.Head);
            best = Mathf.Min(best, Social.SocialHands.FeedGap(item, mouth));
            placeBest = Mathf.Min(placeBest, Social.SocialHands.FeedPlaceGap(item, mouth));
            if (driver != null && driver.HeldBackBody == host)
                keptFor += Time.unscaledDeltaTime;
        }
        yield return Sampled(
            WalkTo(HostCenter, 0.45f, false),
            () =>
            {
                Sample();
                if (startSpeed < 0f && item != null && item.isUsingSecondary)
                    startSpeed = new Vector2(
                        c.data.avarageVelocity.x,
                        c.data.avarageVelocity.z
                    ).magnitude;
            }
        );
        // Twice the gate: the average decays over the stop, so only a walk's own speed is a fault.
        if (startSpeed > 2f * HandOver.StillSpeed)
        {
            Fail(F($"use-on-friend started on the walk in, A moving {startSpeed:0.00} m/s"));
            yield break;
        }
        yield return Sampled(MpStepToMouth(host), Sample);
        float end = Now + GameTiming.Budget(GameTiming.FeedSeconds(item), 8f);
        while (
            item != null
            && HeldItem() == item
            && Now < end
            && host != null
            && c.data.lastConsumedItem == consumedBefore
        )
        {
            var mouth = HostPart(host, BodypartType.Head);
            Sample();
            var goal = rig.Origin.InverseTransformPoint(mouth - grip.World(rig, Script(left)));
            goal =
                VRInput.HeadPosition
                + Vector3.ClampMagnitude(goal - VRInput.HeadPosition, MpFeedHold);
            SetHand(
                left,
                HandScript
                    .Approach(Script(left).Position.N(), goal.N(), 1.5f, Time.unscaledDeltaTime)
                    .U()
            );
            yield return null;
        }
        if (c.data.lastConsumedItem == consumedBefore && item != null && item.isUsingSecondary)
            yield return WaitFor(
                () =>
                    item == null
                    || !item.isUsingSecondary
                    || c.data.lastConsumedItem != consumedBefore,
                GameTiming.FeedSeconds(item) + 0.5f
            );
        if (c.data.lastConsumedItem != consumedBefore)
            yield return WaitFor(() => HeldItem() != item, 2f);
        var note = F(
            $"item closest {GapNote(best)} to the host's mouth, its place on the controller {GapNote(placeBest)}, kept by the host's body {keptFor:0.0} s, {mouthStepNote}"
        );
        if (c.data.lastConsumedItem == consumedBefore)
            Fail("raised to the host's mouth after the walk in, the food did not feed: " + note);
        else
            stepNote = "fed after a walk in with the food held ahead: " + note;
    }

    /// <summary>Brings the held item to rest just short of the host's mouth with the hand alone, speed from the measured
    /// gap, a short step in when the full hold falls short, until <paramref name="done"/>; the last gap in
    /// <see cref="bandGap"/>.</summary>
    IEnumerator MpRestInBand(bool left, Item item, Character host, Func<bool> done)
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        float shortFor = 0f;
        int steps = 0;
        for (
            float end = Now + MpBandBudget;
            Now < end && item != null && HeldItem() == item && !done();

        )
        {
            var mouth = HostPart(host, BodypartType.Head);
            bandGap = Social.SocialHands.FeedGap(item, mouth);
            var fullHold =
                VRInput.HeadPosition
                + Vector3.ClampMagnitude(
                    rig.Origin.InverseTransformPoint(mouth) - VRInput.HeadPosition,
                    MpFeedHold
                );
            float speed = HandScript.BandSpeed(bandGap, MpBandLow, MpBandHigh);
            SetHand(
                left,
                HandScript
                    .Approach(
                        Script(left).Position.N(),
                        (speed >= 0f ? fullHold : VRInput.HeadPosition).N(),
                        Mathf.Abs(speed),
                        Time.unscaledDeltaTime
                    )
                    .U()
            );
            shortFor =
                bandGap > MpBandHigh && Vector3.Distance(Script(left).Position, fullHold) < 0.02f
                    ? shortFor + Time.unscaledDeltaTime
                    : 0f;
            if (shortFor >= 1f && steps < 3)
            {
                steps++;
                shortFor = 0f;
                yield return CarryFoodBack(left);
                var toward = mouth - c.Center;
                toward.y = 0f;
                var to =
                    c.Center
                    + toward.normalized * (bandGap - 0.5f * (MpBandLow + MpBandHigh) + 0.1f);
                yield return MpWalkBounded(() => to, 0.1f, 3f);
                Face(HostPart(host, BodypartType.Head));
                end = Now + MpBandBudget;
                continue;
            }
            yield return null;
        }
    }

    /// <summary>The nearest point of the item from the mouth at the last frame of <see cref="MpRestInBand"/> (m).</summary>
    float bandGap;

    /// <summary>The band just short of the mouth (m, the zone's own measure, SocialHands.FeedGap): past the zone's exit
    /// radius, not its entry, and inside the radius where a press says "closer" (MP run 17, #111).</summary>
    const float MpBandLow = Social.SocialHands.FeedExitRadius + 0.02f;

    const float MpBandHigh = Social.SocialHands.NearFriendRadius - 0.03f;

    /// <summary>The food low at the holding side and a little back, out of a host's mouth zone on the walk in.</summary>
    IEnumerator CarryFoodBack(bool left)
    {
        HeadFrame(out var head, out var forward, out var right);
        // MoveTrack itself, not a wrapper around it: a caller's MoveNext gets every frame (FeedFetchFront).
        return MoveTrack(
            left,
            head + (left ? -right : right) * 0.3f + Vector3.down * 0.6f - forward * 0.1f,
            0.3f
        );
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
    /// <remarks>With <paramref name="nextOnMiss"/> (a step whose subject is not the pickup), an item the grip closed on
    /// and did not take is passed over for the next, and only the last miss fails (MP run 13, #100: a Bugle rolled
    /// onto a 45 degree slope under the reaching hand and vr.434 never reached its pointing).
    /// With <paramref name="spawn"/> (a prefab name) the host first spawns one at A's feet (<see cref="MpRequest.SpawnItem"/>)
    /// (on <paramref name="spawnSide"/>, by default <see cref="SpawnSide"/>)
    /// and that is the first try. Candidates lie within <see cref="MpFetchRange"/> of the host and the tries stop after
    /// <see cref="MpFetchSeconds"/> (MP run 15, #103: a fetch for food 175 m off ran out its step and stranded A).</remarks>
    IEnumerator GrabReachable(
        Func<Item, bool> ok,
        Item first = null,
        float range = NearItems,
        int tries = 3,
        bool nextOnMiss = false,
        string spawn = null,
        Vector3? spawnSide = null
    )
    {
        var c = Character.localCharacter;
        string spawnNote = "";
        if (spawn != null && first == null)
        {
            stepSkip = null;
            yield return Ask(MpRequest.SpawnItem, 5f, spawnSide ?? SpawnSide(c), spawn);
            if (stepSkip == null)
            {
                bool Spawned(Item x) => ok(x) && x.name.StartsWith(spawn, StringComparison.Ordinal);
                yield return WaitFor(() => NearestGround(Spawned, null, 3f) != null, 3f);
                first = NearestGround(Spawned, null, 3f);
                if (first == null)
                    spawnNote = $"; the host's {spawn} did not show within 3 m";
            }
            else
                spawnNote = "; " + stepSkip;
            stepSkip = null;
        }
        var tried = new HashSet<Item>();
        string missed = null;
        float giveUp = Now + MpFetchSeconds;
        float lastFar = 0f;
        float lastShort = 0f;
        for (int i = 0; i < tries && Now < giveUp; i++)
        {
            // Nearest first, and only one a player can pick up off the ground (OnReachableGround).
            var item =
                i == 0 && first != null && OnReachableGround(first)
                    ? first
                    : NearestGround(
                        x => ok(x) && !tried.Contains(x) && NearHost(x) && OnReachableGround(x),
                        null,
                        range
                    );
            if (item == null)
                break;
            tried.Add(item);
            lastFar = FlatDistance(item.Center(), c.Center);
            // Bounded, so far tries still fit the step (walking 4 m/s), with one sidestep round a blocker.
            yield return MpWalkAround(
                () => item != null ? item.Center() : Character.localCharacter.Center,
                0.55f,
                5f + Vector3.Distance(item.Center(), Character.localCharacter.Center) / 3f,
                1
            );
            var to = item != null ? item.Center() - Character.localCharacter.Center : Vector3.zero;
            to.y = 0f;
            lastShort = to.magnitude;
            if (item == null || to.magnitude > 0.8f)
                continue;
            yield return GrabWorld(item);
            if (stepOk || !nextOnMiss)
                yield break;
            missed = failNote;
            failNote = null;
            stepOk = true;
            // A grip on air takes what the view aims at (the vanilla run of #100: a Guidebook lying near), which this
            // step did not fetch.
            yield return MpDropHeld();
            if (HeldItem() != null)
            {
                Fail(missed + "; and the grip took " + HeldItem().name + ", which would not drop");
                yield break;
            }
            // One that slid off under the hand is reached for again, as a player does.
            if (grabMoved)
                tried.Remove(item);
        }
        if (missed != null)
            Fail(missed + $" (the last of {tried.Count} tried){spawnNote}");
        else
            stepSkip = F(
                $"no reachable item among {tried.Count} tried within {MpFetchRange:0} m of the host{(tried.Count > 0 ? $"; the last lay {lastFar:0.0} m off and the walk stopped {lastShort:0.0} m short" : "")}{(Now >= giveUp ? $"; gave up after {MpFetchSeconds:0} s" : "")}{spawnNote}"
            );
    }

    /// <summary>The side of A the host spawns an item on: away from the host, so A turns away to fetch it. Along A's
    /// look it lands at the feet of a host A is facing, and the lift and the carry both end up on the host (#119: the
    /// mouth dwell fed them 2 s after the pickup, A standing still; #112: a Compass out of sight behind them).</summary>
    static Vector3 SpawnSide(Character c)
    {
        var host = Host();
        return host != null ? Flat(c.Center - host.Center) : Flat(VRRig.Instance.Head.forward);
    }

    /// <summary>How far (m) from the host a step goes for an item, so it never strands A away from the host (#103).</summary>
    const float MpFetchRange = 15f;

    /// <summary>How long (s) <see cref="GrabReachable"/> tries before it skips, well inside any step's budget.</summary>
    const float MpFetchSeconds = 60f;

    /// <summary>The food the steps ask the host for: one-hand, feeds a friend, and packs take it.</summary>
    const string MpFood = "TrailMix";

    /// <summary>The food feed asks for: its pivot sits 0.38 m from the far end of its box, where <see cref="MpFood"/>'s
    /// is nearly in the box, so it proves the zone's own measure at the held place (#119).</summary>
    const string MpFeedFood = "Apple Berry Red";

    /// <summary>A plain one-hand item with no use of its own (feed-refuse, a full hand).</summary>
    const string MpPlainItem = "Compass";

    /// <summary>An item a second hand can join (OptionalTwoHand, MP run 14's two-hand).</summary>
    const string MpTwoHandItem = "Guidebook";

    static bool NearHost(Item item)
    {
        var host = Host();
        return host == null || FlatDistance(item.Center(), host.Center) <= MpFetchRange;
    }

    /// <summary>Food the way the other steps get an item: the host spawns some at A's feet, else the nearest reachable
    /// food lying near the host (<see cref="GrabReachable"/>). SKIP with the reason when none turns up.</summary>
    IEnumerator FetchFood(Func<Item, bool> food)
    {
        yield return GrabReachable(food, null, NearItems, 3, spawn: MpFood);
    }

    /// <summary>How far (m) a hand follows an item from where it lay; past it the item slid off and is not chased.</summary>
    const float GripFollow = 0.3f;

    /// <summary>The last <see cref="GrabWorld"/> missed an item that had moved past <see cref="GripNudged"/>.</summary>
    bool grabMoved;

    /// <summary>How far (m) an item moved under a missed reach to get another try: the hand follows it to
    /// <see cref="GripFollow"/>, but a Guidebook on a 14 degree slope slid 0.20 m and the grip still missed by 0.16 m (#100).</summary>
    const float GripNudged = 0.1f;

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
        var c = Character.localCharacter;
        // A round item the walk bumps rolls on (MP run 20: a coconut came to rest 8-9 m off and the hand was sent
        // after it, a pose B rejected as implausible): let it settle and walk again while it lies out of reach.
        for (int walk = 0; walk < 3; walk++)
        {
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
            // An item the walk ended on top of is underfoot, out of a standing hand's reach and under the body: step
            // back from it first (MP run 17: a Compass at A's feet, the hand sent 1.5 m down and behind, the grip found
            // nothing).
            var under = world.Center() - c.Center;
            under.y = 0f;
            if (under.magnitude < 0.35f)
            {
                var back =
                    c.Center
                    - (
                        under.sqrMagnitude > 1e-4f
                            ? under.normalized
                            : Flat(VRRig.Instance.Head.forward)
                    ) * 0.5f;
                yield return MpWalkBounded(() => back, 0.15f, 3f);
            }
            yield return WaitFor(
                () =>
                    world == null || world.rig == null || world.rig.linearVelocity.magnitude < 0.1f,
                2f
            );
            if (world == null || FlatDistance(world.Center(), c.Center) <= 1.2f)
                break;
        }
        if (world == null || world.itemState != ItemState.Ground)
        {
            Fail(label + " is gone");
            yield break;
        }
        // Within a squat's reach of the eye, not only of the body's centre (MP run 21: a granola bar 0.79 m out from
        // the eye, past reach even stooped 0.89 m): step in toward it.
        var eyeNow = VRRig.Instance.Head.position;
        if (FlatDistance(eyeNow, world.Center()) > 0.5f)
        {
            var near = world.Center();
            yield return MpWalkBounded(() => near, 0.45f, 3f);
        }
        Face(world.Center());
        yield return Wait(0.3f);
        const bool left = false;
        yield return GripTo(left, 0f, 0.05f);
        var at = GripPoint(world);
        var lay = world.Center();
        // Stooping as a player squats for something low (ReachDown); standing again after the grab. The hand follows
        // the item only while it stays near where it lay: one the hand nudges off down a slope is not chased.
        yield return ReachDown(
            left,
            () =>
            {
                if (world != null && (world.Center() - lay).sqrMagnitude < GripFollow * GripFollow)
                    at = GripPoint(world);
                return CapToReach(at);
            },
            0.6f
        );
        // The capped hand stopped short of it (MP run 33: 0.28 m, stooped 0.49 m): step in toward it and reach again.
        if (world != null && Vector3.Distance(VRRig.Instance.Hand(left).position, at) > 0.15f)
        {
            headDrop = 0f;
            var closer = world.Center();
            yield return MpWalkBounded(() => closer, 0.35f, 3f);
            Face(world.Center());
            yield return Wait(0.3f);
            yield return ReachDown(
                left,
                () =>
                {
                    if (
                        world != null
                        && (world.Center() - lay).sqrMagnitude < GripFollow * GripFollow
                    )
                        at = GripPoint(world);
                    return CapToReach(at);
                },
                0.6f
            );
        }
        if (OverReach(left) is { } far)
        {
            headDrop = 0f;
            Fail(
                F(
                    $"{label} out of reach: {far}; it lies {(world != null ? Vector3.Distance(world.Center(), lay) : -1f):0.00} m from where the reach began"
                )
            );
            yield break;
        }
        yield return GripTo(left, 1f, 0.1f);
        yield return Until(
            () => HeldItem() != null && HeldItem().itemID == id,
            3f,
            F(
                $"{label} not picked up ({Vector3.Distance(VRRig.Instance.Hand(left).position, at):0.00} m from the hand to where it was aimed, the item now {(world != null ? PeakXR.Interact.ItemGrabShape.Distance(world, VRRig.Instance.Hand(left).position, out _) : -1f):0.00} m from it and {(world != null ? Vector3.Distance(world.Center(), lay) : -1f):0.00} m from where it lay, stooped {headDrop:0.00} m; {GroundNote(world)}; {PeakXR.Interact.PhysicalInteraction.LastGripNote})"
            )
        );
        headDrop = 0f;
        grabMoved = !stepOk && world != null && Vector3.Distance(world.Center(), lay) > GripNudged;
        if (!stepOk)
            yield break;
        // What was fetched, not what is held: a later failure appends this note (MP run 17: "holding TrailMix" after
        // the host had eaten it).
        stepNote = "fetched " + label;
        HeadFrame(out var head, out var forward, out var right);
        yield return MoveTrack(left, Rest(head, forward, right).Position, 0.4f);
    }

    /// <summary>Where a hand takes a ground item: its centre, or for a big item (MP run 21: a Shell Big, centre 0.41 m
    /// up and inside it, "out of sight" from the eye) the point of its solid surface nearest the eye, as a hand closes
    /// on the side a player sees.</summary>
    static Vector3 GripPoint(Item item)
    {
        var centre = item.Center();
        var eye = VRRig.Instance.Head.position;
        Collider best = null;
        foreach (var col in item.GetComponentsInChildren<Collider>())
        {
            if (
                col.enabled
                && !col.isTrigger
                && (best == null || col.bounds.size.sqrMagnitude > best.bounds.size.sqrMagnitude)
            )
                best = col;
        }
        if (best == null || best.bounds.extents.magnitude < 0.15f)
            return centre;
        var surface = best.ClosestPoint(eye);
        var outward = eye - surface;
        return surface + (outward.sqrMagnitude > 1e-6f ? outward.normalized * 0.02f : Vector3.zero);
    }

    /// <summary>The world position of <paramref name="ch"/>'s body part nearest <paramref name="world"/>.</summary>
    static Vector3 NearestPartTo(Character ch, Vector3 world)
    {
        var best = ch.Center;
        float bestSq = float.MaxValue;
        if (ch.refs.ragdoll == null)
            return best;
        foreach (var part in ch.refs.ragdoll.partList)
        {
            if (part == null)
                continue;
            float sq = (part.transform.position - world).sqrMagnitude;
            if (sq < bestSq)
            {
                bestSq = sq;
                best = part.transform.position;
            }
        }
        return best;
    }

    /// <summary>A world point brought within a player's reach of the eye (<see cref="MaxHandFromEye"/>): the hand is
    /// never sent past an arm, which B would reject as an implausible pose (MP run 21).</summary>
    static Vector3 CapToReach(Vector3 world)
    {
        var eye = VRRig.Instance.Head.position;
        return eye + Vector3.ClampMagnitude(world - eye, MaxHandFromEye * 0.97f);
    }

    /// <summary>How close (m) the feed steps step in to the host's mouth from A's eye: from <see cref="MpFeedHold"/>
    /// the capped hand left the held item about 0.3 m short of the mouth (MP run 35).</summary>
    const float MpFeedStepIn = 0.4f;

    /// <summary>How far (m) from the eye the host's mouth may be for the hold: the hand held out <see cref="MpFeedHold"/>
    /// and the feed zone around the item (<see cref="Social.SocialHands.FeedRadius"/>). The item reaches past the hand
    /// too, so this is the near edge of the real reach (MP run 16, #106: the 0.6 m gate gave up at 0.61 m and the host
    /// was fed by the same hold 3.7 s later).</summary>
    const float MpFeedReach = MpFeedHold + Social.SocialHands.FeedRadius;

    /// <summary>Steps in (up to three bounded steps) until the host's mouth is within <see cref="MpFeedStepIn"/> of A's
    /// eye and the hand that puts the held item on it (the item's offset from the controller taken off the mouth) is
    /// within <see cref="MpFeedHold"/>, so the item is held out on a bent arm rather than at full stretch.</summary>
    IEnumerator MpStepToMouth(Character host)
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        float startEye = Vector3.Distance(rig.Head.position, HostPart(host, BodypartType.Head));
        var startAt = c.Center;
        mouthStepNote = F($"no step-in needed (eye {startEye:0.00} m from the mouth)");
        for (int i = 0; i < 3; i++)
        {
            var mouth = HostPart(host, BodypartType.Head);
            var held = HeldItem();
            var hand = Script(LocalHold.ItemInLeftHand);
            var handAt =
                held != null ? mouth - HeldGrip.Of(rig, hand, held).World(rig, hand) : mouth;
            float over = Mathf.Max(
                Vector3.Distance(rig.Head.position, mouth) - MpFeedStepIn,
                Vector3.Distance(rig.Head.position, handAt) - MpFeedHold
            );
            if (over <= 0f)
                yield break;
            var toward = mouth - c.Center;
            toward.y = 0f;
            float step = over + 0.1f;
            var to =
                c.Center
                + toward.normalized * Mathf.Min(step, Mathf.Max(0f, toward.magnitude - 0.35f));
            yield return MpWalkBounded(() => to, 0.1f, 3f);
            Face(mouth);
            yield return Wait(0.3f);
            mouthStepNote = F(
                $"{i + 1} step-in(s) moved A {FlatDistance(c.Center, startAt):0.00} m, the eye from {startEye:0.00} to {Vector3.Distance(rig.Head.position, HostPart(host, BodypartType.Head)):0.00} m off the mouth (last aimed {FlatDistance(c.Center, to):0.00} m short)"
            );
        }
    }

    /// <summary>How far (m) A's eye stands from the host's mouth for the squeeze beside it: the hand at its full
    /// <see cref="MpFeedHold"/> stops 0.4 m short, so only the hand brings the food to the band (#119).</summary>
    const float MpSqueezeEye = 1f;

    /// <summary>How long (s) the hand gets to bring the food to the band from the carry, from each stance.</summary>
    const float MpBandBudget = 8f;

    /// <summary>Backs A off (up to three bounded walks) until its eye is <see cref="MpSqueezeEye"/> from the host's mouth
    /// on the level, then faces the host: the fetch often ends at the host's feet. A wall behind A leaves it nearer; the
    /// hand still stops at the band.</summary>
    IEnumerator MpStandOff(Character host)
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        for (int i = 0; i < 3; i++)
        {
            Face(HostPart(host, BodypartType.Head));
            yield return Wait(0.3f);
            var away = rig.Head.position - HostPart(host, BodypartType.Head);
            away.y = 0f;
            float near = MpSqueezeEye - away.magnitude;
            if (near <= 0f)
                yield break;
            var to = c.Center + away.normalized * (near + 0.1f);
            yield return MpWalkBounded(() => to, 0.1f, 3f);
        }
        Face(HostPart(host, BodypartType.Head));
        yield return Wait(0.3f);
    }

    /// <summary>What the last <see cref="MpStepToMouth"/> did, for a reach failure's note.</summary>
    string mouthStepNote = "";

    /// <summary>A held item's centre in the frame of the scripted controller holding it (clamped to 0.6 m).</summary>
    readonly struct HeldGrip
    {
        readonly Vector3 local;

        HeldGrip(Vector3 local) => this.local = local;

        public static HeldGrip Of(VRRig rig, HandState hand, Item item)
        {
            var rot = rig.Origin.rotation * hand.Rotation;
            var ctrl = rig.Origin.TransformPoint(hand.Position);
            var off = Quaternion.Inverse(rot) * (item.Center() - ctrl);
            // Up to 0.6 m: a lollipop held by its stick has its centre well past 0.3 m (MP run 27).
            return new HeldGrip(Vector3.ClampMagnitude(off, 0.6f));
        }

        /// <summary>World offset from the controller to the item for the controller's pose now.</summary>
        public Vector3 World(VRRig rig, HandState hand) =>
            rig.Origin.rotation * hand.Rotation * local;

        /// <summary>How far the item's centre sits from the controller (m).</summary>
        public float Length => local.magnitude;
    }

    /// <summary>Steepest ground (deg from level) a fetched item may lie on, and how far above that ground its centre may
    /// sit (an item caught in a bush or on a ledge).</summary>
    const float FetchSlope = 35f;

    const float FetchAboveGround = 0.5f;

    /// <summary>
    /// A ground item a player can walk up to and pick up: terrain under it no steeper than <see cref="FetchSlope"/>,
    /// not a bush (MP run 33: an apple in a berry bush on a 48 degree slope, 1.82 m from the reaching hand), and its
    /// centre at most <see cref="FetchAboveGround"/> above that ground.
    /// </summary>
    static bool OnReachableGround(Item item)
    {
        if (item == null)
            return false;
        var at = item.Center();
        if (!Terrain(at + Vector3.up * 0.5f, Vector3.down, 3f, out var ground))
            return false;
        if (Vector3.Angle(ground.normal, Vector3.up) > FetchSlope)
            return false;
        if (
            ground.collider.GetComponentInParent<BerryBush>() != null
            || ground.collider.name.Contains("bush", StringComparison.OrdinalIgnoreCase)
        )
            return false;
        return at.y - ground.point.y <= FetchAboveGround;
    }

    /// <summary>Where a ground item lies, for a failed pickup's note: the terrain under it (its collider, tilt from
    /// level, how far the item's centre sits above it, negative when sunk in), and the item's own state.</summary>
    static string GroundNote(Item item)
    {
        if (item == null)
            return "item gone";
        var at = item.Center();
        var state = $"{item.itemState}, kinematic {(item.rig != null && item.rig.isKinematic)}";
        if (!Terrain(at + Vector3.up * 0.5f, Vector3.down, 3f, out var ground))
            return $"no terrain within 2.5 m under it ({state})";
        return F(
            $"lies on {ground.collider.name}, {Vector3.Angle(ground.normal, Vector3.up):0} deg slope, centre {at.y - ground.point.y:0.00} m above it ({state})"
        );
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

    /// <summary>Presses drop (the VR button, as a player does) until the hands are empty, three tries of 2 s at most: one
    /// press can land while the item is busy (a wheel closing, a weld rejoining) and do nothing.</summary>
    IEnumerator MpDropHeld()
    {
        for (int i = 0; i < 3 && HeldItem() != null; i++)
        {
            yield return TapDrop();
            yield return WaitFor(() => HeldItem() == null, 2f);
        }
    }

    /// <summary>Drops what is in hand and opens both grips, so a grip an earlier step left closed (a two-hand hold,
    /// a grab) does not start this one already closed.</summary>
    IEnumerator FreeHands()
    {
        yield return MpDropHeld();
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
        item != null
        && item.itemState == ItemState.Ground
        && item is not Backpack
        // Nothing that floats away: a dropped balloon rose 230 m and the hand chased it, a pose B rejected as
        // implausible (MP run 21, pickup).
        && !item.TryGetComponent<Balloon>(out _)
        && (item.rig == null || item.rig.useGravity);

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
        yield return MpStep("reach-look", 60f, true, MpReachLook());
        yield return MpStep("reach", 50f, true, MpReach());
        // The reach-out (#70) from 3 m: the no-catch steps first, so one web lasts.
        yield return MpStep("vr.421-reach-out-cone", 50f, true, MpReachOutCone());
        yield return MpStep("vr.422-reach-out-idle", 50f, true, MpReachOutIdle());
        yield return MpStep("vr.423-reach-out-hands-full", 120f, true, MpReachOutHandsFull());
        yield return MpStep("vr.424-reach-out-far", 50f, true, MpReachOutFar());
        yield return HostWake();
        yield return MpStep("kick-mode", 40f, true, KickMode());
        yield return MpStep("carry", 90f, true, MpCarry());
        yield return HostWake();
        yield return MpStep("pack", MpPackBudget, true, MpPack());
        yield return MpStep("vr.330-pack-stash", MpPackBudget + 60f, true, MpPackStash());
        yield return MpStep(
            "vr.434-point-friend-pack",
            MpPackBudget + 2 * MpPackWalkSeconds + 30f,
            true,
            MpPointFriendPack()
        );
        yield return MpStep("pal-jump", 40f, true, PalJump());
        // A timed-out step is dropped without its finally: put the head back from the crouch dip.
        if (VRSession.Active && VRInput.TestOverride != null)
            VRInput.TestOverride = applyHands;
        // Its warp strands B on a face: after kick-mode, carry and pack, and after the crouch dip is put back.
        yield return MpStep("vr.420-reach-out-ledge", 120f, true, MpReachOutLedge());
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
    /// <paramref name="seconds"/> for its answer. No answer (an older observer) or a refusal skips the step. A
    /// non-zero <paramref name="dir"/> goes with the request as a level direction (hang-below's lip outward), and
    /// <paramref name="item"/> as the prefab to spawn (spawn-item).
    /// </summary>
    IEnumerator Ask(
        string what,
        float seconds = 5f,
        Vector3 dir = default,
        string item = null,
        Vector3? from = null
    )
    {
        int id = ++mpRequestId;
        double asked = Unix;
        try
        {
            File.AppendAllText(
                Path.Combine(MpDirectory, MpRequest.RequestFile),
                (
                    item != null ? MpRequest.Line(id, what, asked, dir.x, dir.z, item)
                    : from is { } p ? MpRequest.Line(id, what, asked, dir.x, dir.z, p.x, p.y, p.z)
                    : dir == Vector3.zero ? MpRequest.Line(id, what, asked)
                    : MpRequest.Line(id, what, asked, dir.x, dir.z)
                ) + "\n"
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
                    // An older ack with this id is the previous run's, left in a reused folder (#99).
                    if (
                        !MpRequest.TryReadAck(line, out int got, out bool ok)
                        || got != id
                        || !JsonLine.TryReadNumber(line, "t", out var at)
                        || at < asked
                    )
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
        // The host can leave mid-step (MP run 15, #104: B quit at its time limit and Face threw on a null host).
        if (Host() == null)
        {
            stepSkip = "no host";
            yield break;
        }
        yield return WalkTo(HostCenter, stop, true);
        if (!stepOk)
            yield break;
        var host = Host();
        if (host == null)
        {
            stepSkip = "no host";
            yield break;
        }
        Face(host.Head);
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

    /// <summary>Up to three steps in toward <paramref name="target"/> while it lies past a real arm's reach
    /// (<see cref="PastArm"/>), as the solo steps do; each goes the level distance that brings it within reach
    /// (<see cref="LevelStepIn"/>), bounded, then faces <paramref name="face"/>.</summary>
    IEnumerator StepInToReach(bool left, Func<Vector3> target, Func<Vector3> face)
    {
        var c = Character.localCharacter;
        for (int i = 0; i < 3 && PastArm(left, target()) > 0f; i++)
        {
            var toward = target() - c.Center;
            toward.y = 0f;
            float step = LevelStepIn(left, target()) + 0.1f;
            var to = c.Center + toward.normalized * Mathf.Min(step, toward.magnitude);
            yield return MpWalkBounded(() => to, 0.1f, 3f);
            Face(face());
            yield return Wait(0.3f);
        }
    }

    /// <summary>The level distance (m) to walk in so the shoulder comes within a real arm's reach of
    /// <paramref name="world"/>. Part of the gap can be height, which a level step closes only by its cosine (MP run
    /// 14, #100: two steps of the 3D excess left the host's pack 0.14 m past reach).</summary>
    static float LevelStepIn(bool left, Vector3 world)
    {
        ShoulderGap(left, world, out float level, out float up);
        float reach = SocialConfig.Instance.Reach + MpShoulderReach;
        return Mathf.Max(0f, level - Mathf.Sqrt(Mathf.Max(0f, reach * reach - up * up)));
    }

    /// <summary>The shoulder's gap to <paramref name="world"/> on the level and in height (tracking space, as
    /// <see cref="PastArm"/>).</summary>
    static void ShoulderGap(bool left, Vector3 world, out float level, out float up)
    {
        HeadFrame(out var head, out _, out var right);
        var shoulder = head + Vector3.down * 0.25f + (left ? -right : right) * 0.18f;
        var to = VRRig.Instance.Origin.InverseTransformPoint(world) - shoulder;
        level = new Vector2(to.x, to.z).magnitude;
        up = to.y;
    }

    /// <summary>A grip closes on the host's right hand (where a helping hand goes). A hand out of a real arm's reach
    /// takes one more step in, then skips the step rather than stretch the scripted arm.</summary>
    IEnumerator GripHostHand(bool left, float turnAway = 0f)
    {
        var host = Host();
        // Whichever of the host's hands is nearer this hand's shoulder, as a player reaches for the closer one; a hand
        // held out of reach is taken at the forearm instead, where a player grabs a friend's wrist (MP run 22: the
        // host's outstretched hand stayed 0.04-0.12 m past reach). SocialHands.Find takes hand, elbow and arm alike.
        Vector3 Part()
        {
            Vector3 Nearer(BodypartType a, BodypartType b)
            {
                var pa = HostPart(host, a);
                var pb = HostPart(host, b);
                return PastArm(left, pa) <= PastArm(left, pb) ? pa : pb;
            }
            var hand = Nearer(BodypartType.Hand_L, BodypartType.Hand_R);
            if (PastArm(left, hand) <= 0f)
                return hand;
            var forearm = Nearer(BodypartType.Elbow_L, BodypartType.Elbow_R);
            return PastArm(left, forearm) < PastArm(left, hand) ? forearm : hand;
        }
        // MP run 15: the walk to the host stopped 0.27 m and 0.30 m short of reach.
        yield return StepInToReach(left, Part, () => host.Head);
        // Turned away first when asked: the body and the look turn, the hand still reaches to the side.
        if (turnAway != 0f)
        {
            VRRig.Instance.RotateYaw(turnAway);
            yield return Wait(0.5f);
        }
        float past = PastArm(left, Part());
        if (past > 0f)
        {
            stepSkip = $"the host's hand is {past:0.00} m past a real arm's reach";
            yield break;
        }
        // On its feet and not sliding: a body sliding down ground too steep to stand carries the hand off the friend's
        // before the grip closes, and the grip then catches the slope (MP run 16, #110: A on a 51 deg slope 0.28 m above
        // the host slid with TooSteep, and the grip started a climb).
        var c = Character.localCharacter;
        bool Footed() =>
            GripIntent.OnFeet(c.data.isGrounded, c.data.sinceGrounded)
            && c.data.sinceFallSlide > 0.5f;
        yield return WaitFor(Footed, 1.5f);
        if (!Footed())
        {
            stepSkip = F(
                $"A slides on ground too steep to stand, {c.Center.y - host.Center.y:0.00} m above the host"
            );
            yield break;
        }
        yield return MoveTo(left, Part, 0.5f);
        gripMark = CueMark();
        yield return GripTo(left, 1f, 0.1f);
    }

    /// <summary>When <see cref="GripHostHand"/>'s grip started to close: a cue's window starts here, not before the
    /// step-in (MP run 16, #110: a step-in on a slope spent the 1 s window before the grip closed).</summary>
    float gripMark;

    /// <summary>
    /// feed-refuse: a non-food item (not <c>canUseOnFriend</c>) resting at the host's mouth, trigger up, is refused after
    /// the mouth dwell (CharacterInteractible.HasItemCanUseOnFriend, :90): CantUse at that hand with the item turned
    /// red, and B gets no feed RPC.
    /// </summary>
    IEnumerator FeedRefuse()
    {
        yield return FreeHands();
        if (!stepOk)
            yield break;
        // A small one-hand thing (MP run 7 took the two-hand step's Guidebook, whose two-hand hold kept it from the
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
                // Nor an aimed use the trigger reaches another way (MP run 22's piton: SpikeCue's NoWall). The mod
                // says CantUse for any refusal at a friend's mouth now; a plain item keeps the step about that alone.
                && !i.TryGetComponent<ClimbingSpikeComponent>(out _)
                && !i.TryGetComponent<RopeTier>(out _)
                && !i.TryGetComponent<RopeShooter>(out _)
                && !i.TryGetComponent<VineShooter>(out _)
                && !i.TryGetComponent<Constructable>(out _)
                && info.Bind(i)
                && info.Profile.Handedness == Handedness.OneHand,
            null,
            NearItems,
            5,
            nextOnMiss: true,
            spawn: MpPlainItem
        );
        if (!stepOk || stepSkip != null)
            yield break;
        var item = HeldItem();
        // Just short of the mouth first, trigger up: refused after the dwell, never nothing (#112).
        bool nearLeft = LocalHold.ItemInLeftHand;
        yield return CarryFoodBack(nearLeft);
        yield return WalkTo(HostCenter, MpSqueezeEye + 0.3f, true);
        if (!stepOk)
            yield break;
        yield return MpStandOff(Host());
        // The near band's refusal comes only with the view on the spot (HandOver.NearLookDegrees).
        LookAtPoint(HostPart(Host(), BodypartType.Head));
        Affordance.ResetGate();
        float nearMark = CueMark();
        yield return MpRestInBand(
            nearLeft,
            item,
            Host(),
            () => CueLog.Capture.Fired(nearLeft, DenyReason.CantUse, nearMark, true, out _)
        );
        yield return ExpectCue(nearLeft, DenyReason.CantUse, nearMark, true, 0.5f);
        if (!stepOk)
        {
            failNote += F(
                $"; the item rested {bandGap:0.00} m from the host's mouth with the trigger up"
            );
            yield break;
        }
        var nearCue = cueNote;
        EndGlance();
        Affordance.ResetGate();
        // Carried back on the way, as feed does: held ahead, the item met the host's body first and the weld kept it
        // there, short of the mouth, for the whole hold (#112: a Frisbee 0.24 m off with the hand 1.00 m short).
        yield return CarryFoodBack(LocalHold.ItemInLeftHand);
        var host = Host();
        var rig = VRRig.Instance;
        // The zone SocialHands.UpdateFeed tests: the item's nearest point within FeedRadius of the Head bodypart.
        Vector3 Mouth() => HostPart(host, BodypartType.Head);
        bool left = LocalHold.ItemInLeftHand;
        // The item's place on the controller, read here where the air is clear. Read at the host, it took the body's keep
        // for the item's place (#112, modded B: 0.46 m on a Compass, with A's eye 0.28 m from the mouth after a walk to
        // 0.45 m, so the hand never brought it to the mouth). It stays read once: read every frame it fed the weld's lag
        // back into the aim (MP run 25: the hand jumped 0.9 m as the weld let go and rejoined, and the item left the mouth).
        yield return HoldingHandSettled();
        var grip = HeldGrip.Of(rig, Script(left), item);
        // The host's mouth must be within arm's reach of the head, not on a ledge below (MP run 7: the hand was driven
        // 1.4 m under the floor toward a host down the slope).
        yield return MpStepToMouth(host);
        var c = Character.localCharacter;
        float gap = Vector3.Distance(rig.Head.position, Mouth());
        // The feed reach, as feed: the hand held out MpFeedHold and the zone around the item. Farther, the clamped
        // hand cannot bring the item to the mouth (MP run 17, #112: a host 0.2 m down the slope, A's eye 0.97 m from
        // their mouth after the step-in, the item never counted a frame at the mouth).
        if (gap > MpFeedReach)
        {
            Fail(
                F(
                    $"not in reach of the host's mouth after stepping in: A's eye {gap:0.00} m from it ({FlatDistance(c.Center, host.Center):0.00} m level, the mouth {Mouth().y - rig.Head.position.y:+0.00;-0.00} m from the eye's height), reach {MpFeedReach:0.00} m; {mouthStepNote}"
                )
            );
            yield break;
        }
        Face(Mouth());
        // The look on the host's face, as a player feeding a friend looks at them: following a hand that hung low took
        // the look 60 deg down (MP run 17).
        LookAtPoint(Mouth());
        // A hand-over (HandOver): resting at the mouth with the trigger up is the try, as with food (issue #14), so
        // after the mouth dwell it is refused with no squeeze. Until then a cue may still be the near band's, on a new
        // visit (the walk back can end the last), so only one from inside the zone counts as the mouth's.
        float mark = CueMark();
        bool inZoneYet = false;
        bool mouthCue = false;
        float end = Now + 8f;
        float best = float.MaxValue;
        float atMouth = 0f;
        float standsFor = 0f;
        float mouthPlace = float.MaxValue;
        // The hand keeps its turn through the hold: re-aimed from the head every frame, the turn moved the item's
        // offset, which moved the aim, which turned the hand again (MP run 17: the hand swung 0.15 m every other frame).
        HoldTurn(left, Script(left).Rotation);
        var standAt = c.Center;
        float shortMax = 0f;
        var driver = AvatarPoseDriver.Of(c);
        var social = Social.SocialHands.Instance;
        while (Now < end && item != null && HeldItem() == item)
        {
            // The zone's own measure (SocialHands.UpdateFeed): the item's nearest point, as feed counts it.
            float gapNow = Social.SocialHands.FeedGap(item, Mouth());
            best = Mathf.Min(best, gapNow);
            bool inZone = gapNow <= Social.SocialHands.FeedRadius;
            if (inZone && !inZoneYet)
            {
                inZoneYet = true;
                // The near band's cue a moment ago must not swallow the mouth's in the rate limit.
                Affordance.ResetGate();
                mark = CueMark();
            }
            atMouth = inZone ? atMouth + Time.unscaledDeltaTime : atMouth;
            if (inZone)
                mouthPlace = Mathf.Min(mouthPlace, Social.SocialHands.FeedPlaceGap(item, Mouth()));
            standsFor =
                social.FeedState == HandOverState.Refused
                    ? standsFor + Time.unscaledDeltaTime
                    : standsFor;
            if (driver != null && driver.ReachGap(left, out float shortNow))
                shortMax = Mathf.Max(shortMax, shortNow);
            // The item, not the hand, on the moving mouth every frame, as feed does (MP run 24: chasing the gap kept a
            // Frisbee at the mouth 0.4 s in all).
            var goal = rig.Origin.InverseTransformPoint(Mouth() - grip.World(rig, Script(left)));
            goal =
                VRInput.HeadPosition
                + Vector3.ClampMagnitude(goal - VRInput.HeadPosition, MpFeedHold);
            SetHand(
                left,
                Vector3.Distance(goal, Script(left).Position) > 0.25f
                    ? HandScript
                        .Approach(Script(left).Position.N(), goal.N(), 1.5f, Time.unscaledDeltaTime)
                        .U()
                    : goal
            );
            if (inZoneYet && CueLog.Capture.Fired(left, DenyReason.CantUse, mark, true, out _))
            {
                mouthCue = true;
                break;
            }
            // A refusal that stands is the answer too, with the item kept short of the zone: once the hand has had time
            // to bring it in, there is nothing more to wait for.
            if (!inZoneYet && standsFor >= 3f)
                break;
            yield return null;
        }
        string itemName = item != null ? item.name : "the item";
        string held = F(
            $"{itemName} came within {GapNote(best)} of the host's mouth (nearest point, zone {Social.SocialHands.FeedRadius:0.00}), {atMouth:0.0} s at the mouth, its place {GapNote(mouthPlace)} off there, the refusal standing {standsFor:0.0} s; A's eye {gap:0.00} m from it at the start, the body moved {FlatDistance(c.Center, standAt):0.00} m during the hold, the drawn hand up to {shortMax:0.00} m short of its controller, the item {grip.Length:0.00} m from the controller"
        );
        // Silence is the failure (#112): the mouth's own refusal, or the near band's still standing.
        if (!mouthCue && standsFor <= 0f)
        {
            Fail("nothing said while the item rested against the host: " + held);
            yield break;
        }
        var restCue = mouthCue ? cueNote : "the near band's refusal still standing as a ring";
        if (mouthCue)
        {
            yield return ExpectCue(left, DenyReason.CantUse, mark, true, 0.5f);
            if (!stepOk)
                yield break;
            restCue = cueNote;
        }
        // The squeeze is the other try: refused in full on each press, wherever the body kept the item (HandOver.Step at
        // the mouth, the item's own refusal beside it).
        Affordance.ResetGate();
        float squeeze = CueMark();
        Script(left).Trigger = 1f;
        yield return ExpectCue(left, DenyReason.CantUse, squeeze, true, 0.5f);
        Script(left).Trigger = 0f;
        if (stepOk)
            stepNote =
                $"just short of the mouth: {nearCue}; {held}; at rest: {restCue}; squeezed: {cueNote}";
        else
            Note(held);
    }

    /// <summary>reach-deny: an empty hand grips a host who needs no help (CharacterGrabbing.TargetCanBeHelped,
    /// :293): NotInTrouble at that hand, no tint (D10), and no reach starts.</summary>
    IEnumerator ReachDeny()
    {
        yield return ToHost(0.6f);
        if (!stepOk || stepSkip != null)
            yield break;
        var c = Character.localCharacter;
        if (CharacterGrabbing.TargetCanBeHelped(Host(), c))
        {
            stepSkip = "the host needs help now (reach covers it)";
            yield break;
        }
        LookAtHand(false);
        yield return GripHostHand(false);
        if (stepSkip != null)
            yield break;
        yield return ExpectCue(false, DenyReason.NotInTrouble, gripMark, false);
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
    /// <summary>
    /// reach-look: the host is webbed (<see cref="MpRequest.Web"/>), A grips their hand while looking well to the side,
    /// past the game's 60 degree reach cone (CharacterGrabbing.cs:252-253): nobody is pulled up, so A must get
    /// WrongAngle at the hand once (SocialHands.UpdateReachLook), not a silent grip. Then A looks at them and the game's
    /// reach catches them (GUIManager.Grasp after CharacterGrabbing.Reach).
    /// </summary>
    IEnumerator MpReachLook()
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
        if (!stepOk || stepSkip != null)
            yield break;
        // Turned 100 degrees left with the host on the right, the right hand reaching out to the side: the body and
        // the look turn together (MP run 24: a head glance alone left the look 21 degrees off, inside the cone; run 25:
        // an 80 degree turn left it 69 degrees off).
        yield return GripHostHand(false, -100f);
        if (stepSkip != null)
            yield break;
        float off = Vector3.Angle(c.data.lookDirection, host.Center - c.Center);
        // A clear margin past the game's cone, so the reach plainly cannot catch.
        if (off <= SocialIntent.ReachConeDegrees + 5f)
        {
            stepSkip = F(
                $"the turn left the look {off:0} deg off the host, too close to the game's {SocialIntent.ReachConeDegrees:0} deg cone for a clear test (wanted over {SocialIntent.ReachConeDegrees + 5f:0})"
            );
            yield break;
        }
        // Held outside the cone through the cue window, as a player keeps looking away: whenever the look drifts back
        // toward the host, the body turns away again, the hand kept where it holds them (MP run 17, #113: past 65 deg at
        // the grip, 57 deg by the end of the window, and no cue).
        float atGrip = off;
        float minOff = off;
        int turnsBack = 0;
        float nextTurn = 0f;
        var rig = VRRig.Instance;
        for (
            float end = gripMark + 2f;
            Now < end && !CueLog.Capture.Fired(false, DenyReason.WrongAngle, gripMark, out _);

        )
        {
            float now = Vector3.Angle(c.data.lookDirection, host.Center - c.Center);
            minOff = Mathf.Min(minOff, now);
            // The character's look follows the rig a frame or more late: one turn per 0.15 s, not one per stale frame.
            if (now < atGrip - 2f && Now >= nextTurn)
            {
                var hand = rig.Origin.TransformPoint(Script(false).Position);
                float side = Vector3.SignedAngle(
                    Flat(host.Center - c.Center),
                    Flat(c.data.lookDirection),
                    Vector3.up
                );
                rig.RotateYaw((side >= 0f ? 1f : -1f) * (atGrip - now));
                SetHand(false, rig.Origin.InverseTransformPoint(hand));
                nextTurn = Now + 0.15f;
                turnsBack++;
            }
            yield return null;
        }
        string lookNote = F(
            $"look {atGrip:0} deg off the host at the grip, at least {minOff:0} deg through the window, {Vector3.Angle(c.data.lookDirection, host.Center - c.Center):0} deg at its end, turned away again {turnsBack} time(s) (cone {SocialIntent.ReachConeDegrees:0})"
        );
        if (minOff <= SocialIntent.ReachConeDegrees)
        {
            Fail("the look drifted into the reach cone before the cue: " + lookNote);
            yield break;
        }
        yield return ExpectCue(false, DenyReason.WrongAngle, gripMark, false, 2f);
        if (!stepOk)
        {
            // MP run 20 missed the cue with nothing to tell a look inside the cone from a reach the game caught.
            stepNote = F(
                $"{lookNote}, caught {c.data.sinceGrabFriend < 0.3f}, grip {PeakXR.Interact.PhysicalInteraction.HandAction(false)}"
            );
            yield break;
        }
        if (c.data.sinceGrabFriend < 0.3f)
        {
            Fail("the reach caught the host while A looked away: the cue is wrong");
            yield break;
        }
        var cue = cueNote;
        Face(host.Center);
        LookAtPoint(host.Center);
        yield return Until(
            () => c.data.sinceGrabFriend < 0.3f,
            3f,
            F(
                $"looking at the host, the reach still never caught them (look {Vector3.Angle(c.data.lookDirection, host.Center - c.Center):0} deg off)"
            )
        );
        EndGlance();
        if (stepOk)
            stepNote =
                "looking away ("
                + lookNote
                + "): "
                + cue
                + "; looking at them: caught"
                + $"; reach-out {SocialHands.Instance.ReachOutFriend(false) != null}";
    }

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
        if (!stepOk || stepSkip != null)
            yield break;
        yield return GripHostHand(false);
        if (stepSkip != null)
            yield break;
        // The game's reach only catches a friend within 60 degrees of the look (CharacterGrabbing.Reach, :253),
        // every client from the synced look: a player pulling a friend up looks at them (MP run 18: the head still
        // glanced 57 degrees down at the hand from reach-deny, and the reach never caught).
        LookAtPoint(host.Center);
        yield return Until(
            () => c.data.sinceGrabFriend < 0.3f,
            3f,
            F(
                $"the reach never caught the host (look {Vector3.Angle(c.data.lookDirection, host.Center - c.Center):0} deg off them, host webbed {host.refs.afflictions.isStruggling})"
            )
        );
        if (!stepOk)
        {
            EndGlance();
            yield break;
        }
        // A's own catch is local: the web comes off on the host's own client, when its copy of A's reach passes the
        // same test from the synced look and reaching flag (CharacterGrabbing.Reach, :250-260, SubtractStatus Web on
        // every client, the owner's counting), which B saw 0.24 s after A's checkpoint in MP run 24. Keep the reach
        // held until A sees the host free (statuses synced back, SyncStatusesRPC), so the checkpoint is not early.
        float caughtAt = Now;
        yield return WaitFor(() => !host.refs.afflictions.isStruggling, 3f);
        EndGlance();
        if (host.refs.afflictions.isStruggling)
            Fail(
                F(
                    $"A's reach caught the host, but the host was still webbed 3 s later (look {Vector3.Angle(c.data.lookDirection, host.Center - c.Center):0} deg off them)"
                )
            );
        else
            stepNote = F(
                $"reached and caught the host; free of the web {Now - caughtAt:0.0} s later; reach-out {SocialHands.Instance.ReachOutFriend(false) != null}"
            );
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
        if (!stepOk || stepSkip != null)
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
        if (!stepOk || stepSkip != null)
            yield break;
        // A lying body stretches out from its centre, and walking at its middle runs into the body itself (MP run 23:
        // the steps in stalled and the hip lay 0.77 m out). Stand beside it instead: 0.4 m off the torso-hip line on
        // A's side, facing the body, so a squat reaches both. Up to three tries, each bounded.
        int steps = 0;
        for (int i = 0; i < 3; i++)
        {
            var eye = VRRig.Instance.Head.position;
            var torso = HostPart(host, BodypartType.Torso);
            var hip = HostPart(host, BodypartType.Hip);
            var mid = (torso + hip) * 0.5f;
            if (FlatDistance(eye, torso) <= 0.55f && FlatDistance(eye, hip) <= 0.55f)
                break;
            var axis = hip - torso;
            axis.y = 0f;
            var side =
                axis.sqrMagnitude > 1e-4f
                    ? Vector3.Cross(Vector3.up, axis.normalized)
                    : Flat(c.Center - mid);
            if (Vector3.Dot(side, c.Center - mid) < 0f)
                side = -side;
            var spot = mid + side * 0.4f;
            steps++;
            yield return MpWalkAround(() => spot, 0.15f, 4f, 1);
            Face(mid);
            yield return Wait(0.3f);
        }
        var eyeAt = VRRig.Instance.Head.position;
        var stand = F(
            $"{steps} step(s) beside the body; torso {FlatDistance(eyeAt, HostPart(host, BodypartType.Torso)):0.00} m and hip {FlatDistance(eyeAt, HostPart(host, BodypartType.Hip)):0.00} m from the eye on the level"
        );
        bool pack = c.player != null && !c.player.backpackSlot.IsEmpty();
        float mark = CueMark();
        yield return ReachDown(true, () => HostPart(host, BodypartType.Torso), 0.6f);
        yield return ReachDown(false, () => HostPart(host, BodypartType.Hip), 0.5f);
        var over = OverReach(true) ?? OverReach(false);
        if (over != null)
        {
            stepSkip = "stooped to the lying host, " + over + "; " + stand;
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
        // The carried body rides the shoulder: the hand goes to whichever of its parts is nearest the eye, capped at
        // a player's reach (MP run 24: aimed at the torso 0.78 m out, past reach). A grip on any part of the carried
        // friend puts them down (SocialHands: carried by me is an interact on the body).
        yield return MoveTo(
            false,
            () => CapToReach(NearestPartTo(host, VRRig.Instance.Head.position)),
            0.4f
        );
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

    /// <summary>The pack step's budget from its parts: the host asked to wear a pack (an ack within 5 s, the slot within
    /// 3 s), the settle (recentre about 4 s, stillness up to 6 s, a slope
    /// walk), freeing the hands (about 3 s), three bounded walks, turning and reaching (about 1 s) and the hold check
    /// (2 s), with a margin.</summary>
    const float MpPackBudget = 8f + 14f + 3f + 3 * (MpPackWalkSeconds + 0.3f) + 1f + 2f + 8f;

    /// <summary>A vanilla host never wears a pack (MP run 12, #94): ask B to put one on (<see cref="MpRequest.WearPack"/>)
    /// and wait until A's copy of B's backpack slot shows it (Player.AddItem syncs it, Player.cs:113-114).</summary>
    IEnumerator HostWearsPack()
    {
        var host = Host();
        if (host == null || SocialHands.WearsPack(host))
            yield break;
        yield return Ask(MpRequest.WearPack);
        if (stepSkip != null)
            yield break;
        yield return WaitFor(() => host == null || SocialHands.WearsPack(host), 3f);
    }

    /// <summary>pack: the host's worn pack, gripped from behind while A looks away; the hold starts
    /// (BackpackOnBackVisuals.IsConstantlyInteractable, :152) because the look check follows the hand.</summary>
    IEnumerator MpPack()
    {
        yield return HostWearsPack();
        if (stepSkip != null)
            yield break;
        var host = Host();
        var visuals =
            host != null && host.refs.backpackHandler != null
                ? host.refs.backpackHandler.activeBackpackVisuals
                : null;
        // The visuals component answers even with an empty backpack slot (SocialHands.WearsPack; MP run 13 "held" an
        // invisible pack on a host who had none).
        if (visuals == null || !SocialHands.WearsPack(host))
        {
            stepSkip = "the host wears no pack";
            yield break;
        }
        yield return FreeHands();
        if (!stepOk)
            yield break;
        var c = Character.localCharacter;
        yield return BehindHost(host);
        if (!mpBehind)
        {
            stepSkip = "no reachable spot behind the host";
            yield break;
        }
        Face(visuals.transform.position);
        // Look away as a player can: vanilla needs the look within 110° of the pack's facing
        // (BackpackOnBackVisuals.cs:99-104). The body turns 45° away from that facing, which leaves the pack ahead of the
        // hand on the near side, and the head alone 75° further, a neck's turn (MP run 13 turned the whole body 150° and
        // reached behind its own back).
        // From the view Face just turned, not c.data.lookDirection: the character's look takes it up a frame or more
        // later, so the side came from the walk's heading and the turn went back toward the pack's facing (MP run 17:
        // about 123 deg off after facing the pack, 3 deg after turning 120 deg the wrong way).
        float side = Vector3.SignedAngle(
            HelperFunctions.ZeroY(visuals.transform.forward),
            HelperFunctions.ZeroY(VRRig.Instance.Head.forward),
            Vector3.up
        );
        float turn = side >= 0f ? 1f : -1f;
        // Turning right puts the pack on the left.
        bool left = turn > 0f;
        // MP run 13: the spot behind left the pack 0.06 m past reach (vr.330); step in before turning away.
        yield return StepInToReach(
            left,
            () => visuals.transform.position,
            () => visuals.transform.position
        );
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
                    $"the look is only {off:0}° off the pack's facing, which vanilla accepts ({Mathf.Abs(side):0}° off it facing the pack, then turned {turn * (MpPackBodyTurn + MpPackNeckTurn):+0;-0}°)"
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

    /// <summary>Set by <see cref="BehindHost"/>: A stands behind the host.</summary>
    bool mpBehind;

    /// <summary>Walks to a spot behind the host: straight behind, else 45° to either side of behind (the pack still
    /// faces the hand from there). MP run 10: straight behind was blocked 1.2 m short. Sets <see cref="mpBehind"/>.</summary>
    IEnumerator BehindHost(Character host)
    {
        var c = Character.localCharacter;
        mpBehind = false;
        foreach (float yaw in new[] { 0f, 45f, -45f })
        {
            var facing = host.data.lookDirection_Flat.normalized;
            var back = Quaternion.Euler(0f, yaw, 0f) * -facing;
            Vector3 Spot() => host.Center + back * 0.8f;
            // From the host's front the straight line to their back runs into them (MP run 13: no spot behind was
            // reached): go round their nearer side first, walking round what blocks that too.
            var from = c.Center - host.Center;
            from.y = 0f;
            if (Vector3.Dot(from, facing) > -0.3f)
            {
                var side = Vector3.Cross(Vector3.up, facing);
                if (Vector3.Dot(side, from) < 0f)
                    side = -side;
                var round = host.Center + side * 1.2f;
                yield return MpWalkAround(() => round, 0.4f, MpPackWalkSeconds, 2);
            }
            yield return MpWalkAround(Spot, 0.3f, MpPackWalkSeconds, 2);
            var miss = Spot() - c.Center;
            miss.y = 0f;
            if (
                miss.magnitude <= 0.5f
                && Vector3.Dot(c.Center - host.Center, host.data.lookDirection_Flat) < 0f
            )
            {
                mpBehind = true;
                yield break;
            }
        }
    }

    /// <summary>
    /// vr.330-pack-stash (issues #46, #47): a small item A holds, brought into the host's worn pack from behind, shows
    /// the would-go-in pin (<see cref="Inventory.PackStash.Verdict"/> None) and goes in when the grip opens, through the
    /// game's own stash (CharacterBackpackHandler.StashInBackpack, CharacterBackpackHandler.cs:127-136), into the free
    /// pocket nearest the hand. A sees the item in that pocket; mp-check expects RPCAddItemToCharacterBackpack on B.
    /// </summary>
    IEnumerator MpPackStash()
    {
        yield return HostWearsPack();
        if (stepSkip != null)
            yield break;
        var host = Host();
        var visuals =
            host != null && host.refs.backpackHandler != null
                ? host.refs.backpackHandler.activeBackpackVisuals
                : null;
        if (visuals == null || !SocialHands.WearsPack(host))
        {
            stepSkip = "the host wears no pack";
            yield break;
        }
        if (!Inventory.InventoryConfig.Instance.StashIntoPacks)
        {
            stepSkip = "Inventory.StashIntoPacks is off";
            yield break;
        }
        yield return FreeHands();
        if (!stepOk)
            yield break;
        var info = new ItemHoldInfo();
        yield return GrabReachable(
            i =>
                i.UIData.canBackpack
                && i is not Backpack
                && info.Bind(i)
                && info.Profile.Handedness == Handedness.OneHand,
            null,
            NearItems,
            5,
            nextOnMiss: true,
            spawn: MpFood
        );
        if (!stepOk || stepSkip != null)
            yield break;
        var item = HeldItem();
        cpItemId = item.itemID;
        yield return BehindHost(host);
        if (!mpBehind)
        {
            stepSkip = "no reachable spot behind the host";
            yield break;
        }
        Face(visuals.transform.position);
        bool left = LocalHold.ItemInLeftHand;
        // MP run 13: the spot behind left the pack 0.06 m past reach.
        yield return StepInToReach(
            left,
            () => visuals.transform.position,
            () => visuals.transform.position
        );
        LookAtHand(left);
        yield return Wait(0.3f);
        float past = PastArm(left, visuals.transform.position);
        ShoulderGap(left, visuals.transform.position, out float level, out float up);
        // A pack below the shoulder is reached by bending, as a player does (ReachDown stoops): only one past reach on
        // the level is out of reach (MP run 17: 0.65 m out and 0.69 m under the shoulder skipped as 0.00 m past).
        float levelReach = SocialConfig.Instance.Reach + MpShoulderReach;
        if (past > 0f && (up >= 0f || level > levelReach))
        {
            stepSkip = F(
                $"the host's pack is {past:0.00} m past a real arm's reach ({level:0.00} m out, {up:+0.00;-0.00} m up from the shoulder)"
            );
            yield break;
        }
        // The stash destroys the held instance, and a destroyed object's name throws (MP run 14, #100).
        string itemName = item.name;
        ushort itemId = item.itemID;
        var stash = Inventory.PackStash.Instance;
        if (past > 0f)
            yield return ReachDown(left, () => visuals.transform.position, 0.6f);
        else
            yield return MoveTo(left, () => visuals.transform.position, 0.6f);
        yield return Until(
            () => stash.Over && stash.Verdict == DenyReason.None,
            1.5f,
            "no would-go-in pin with the item in the host's pack"
        );
        if (!stepOk)
        {
            stepNote = F($"over {stash.Over}, verdict {stash.Verdict}");
            yield break;
        }
        int pocket = stash.Pocket;
        yield return GripTo(left, 0f, 0.3f);
        yield return Until(() => HeldItem() == null, 2f, "the item stayed in hand on release");
        if (!stepOk)
            yield break;
        yield return Wait(0.5f);
        var slots = BackpackReference.GetFromEquippedBackpack(host).GetData().itemSlots;
        // The release must stash where the pin showed (#105: the pin said pocket 2, the item went into pocket 3).
        int sent = stash.StashedPocket;
        bool inPocket =
            pocket >= 0
            && pocket < slots.Length
            && slots[pocket] != null
            && !slots[pocket].IsEmpty()
            && slots[pocket].prefab.itemID == itemId;
        if (sent != pocket)
            Fail(
                F(
                    $"the pin showed pocket {pocket} and the release stashed {itemName} into pocket {sent}"
                )
            );
        else if (!inPocket)
            Fail(F($"{itemName} is not in pocket {pocket} of the host's pack"));
        else
            stepNote = F(
                $"{itemName} let go in the host's pack from behind went into pocket {pocket}, the one nearest the hand{(headDrop > 0f ? F($", stooped {headDrop:0.00} m") : "")}"
            );
        // Up again from a stoop to a low pack: a passed step's rest (MpRest) keeps the head where it is.
        if (headDrop > 0f)
            yield return StandUp(left);
    }

    /// <summary>
    /// vr.434-point-friend-pack (#71): A holds a small item packs take and stands behind the host. The free hand points
    /// at the host's worn pack: the pick arms with nothing refusing. The
    /// trigger opens the pack's wheel (BackpackOnBackVisuals.Interact_CastFinished, :162), the free hand picks a
    /// pocket and the release stashes into it (CharacterBackpackHandler.StashInBackpack). mp-check expects
    /// RPCAddItemToCharacterBackpack on B. Then, from the host's front, the pick shows WrongAngle and the pull opens
    /// no wheel.
    /// </summary>
    IEnumerator MpPointFriendPack()
    {
        yield return HostWearsPack();
        if (stepSkip != null)
            yield break;
        var host = Host();
        var visuals =
            host != null && host.refs.backpackHandler != null
                ? host.refs.backpackHandler.activeBackpackVisuals
                : null;
        if (visuals == null || !SocialHands.WearsPack(host))
        {
            stepSkip = "the host wears no pack";
            yield break;
        }
        if (
            !Inventory.InventoryConfig.Instance.PointToOpenPack
            || !Inventory.BodyInventory.Instance.Valid
        )
        {
            stepSkip = "Inventory.PointToOpenPack or Inventory.BodySlots is off";
            yield break;
        }
        yield return FreeHands();
        if (!stepOk)
            yield break;
        var info = new ItemHoldInfo();
        yield return GrabReachable(
            i =>
                i.UIData.canBackpack
                && i is not Backpack
                && info.Bind(i)
                && info.Profile.Handedness == Handedness.OneHand,
            null,
            NearItems,
            5,
            nextOnMiss: true,
            spawn: MpFood
        );
        if (!stepOk || stepSkip != null)
            yield break;
        var item = HeldItem();
        cpItemId = item.itemID;
        yield return BehindHost(host);
        if (!mpBehind)
        {
            stepSkip = "no reachable spot behind the host";
            yield break;
        }
        var c = Character.localCharacter;
        Face(visuals.transform.position);
        bool free = !LocalHold.ItemInLeftHand;
        yield return GripTo(!free, 1f, 0.1f);
        // The fetch leaves the holding hand low and ahead, close enough to the pack's low transform to count as in it
        // (PackStash.Reach), and a held item in a pack turns the free hand's point off (TickPoint; #144).
        yield return CarryFoodBack(!free);
        PointAt(free, visuals.transform.position);
        yield return Until(
            () => Points(free, visuals, DenyReason.None),
            1.5f,
            "the host's pack pointed at from behind did not arm"
        );
        if (!stepOk)
        {
            stepNote = PointState();
            yield break;
        }
        // shortcut: no reach-out yield check here, the host stands so the reach-out has no one to pick; it needs a
        // webbed host (vr.421) whose pose may change the pack's angle, add it once a headset run shows that works.
        Script(free).Trigger = 1f;
        yield return Until(
            () => c.data.usingBackpackWheel,
            0.5f,
            "the trigger did not open the host's pack"
        );
        if (!stepOk)
            yield break;
        yield return Wait(0.1f);
        var wheel = GUIManager.instance.backpackWheel;
        var start = Script(free).Position;
        for (int deg = 0; deg < 360 && !PocketChosen(wheel); deg += 30)
        {
            float a = deg * Mathf.Deg2Rad;
            SetHand(
                free,
                start + VRInput.HeadRotation * new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.15f
            );
            yield return Wait(0.15f);
        }
        if (!PocketChosen(wheel))
        {
            Script(free).Trigger = 0f;
            Fail("no pocket of the host's pack offered the stash");
            yield break;
        }
        byte pocket = wheel.chosenSlice.Value.slotID;
        // The stash destroys the held instance, and a destroyed object's name throws (MP run 14, #100).
        string itemName = item.name;
        ushort itemId = item.itemID;
        Script(free).Trigger = 0f;
        yield return Until(
            () => !c.data.usingBackpackWheel && HeldItem() == null,
            2f,
            "the trigger's release did not stash into the host's pack"
        );
        if (!stepOk)
            yield break;
        yield return Wait(0.5f);
        var slots = BackpackReference.GetFromEquippedBackpack(host).GetData().itemSlots;
        if (
            pocket >= slots.Length
            || slots[pocket] == null
            || slots[pocket].IsEmpty()
            || slots[pocket].prefab.itemID != itemId
        )
        {
            Fail(F($"{itemName} is not in pocket {pocket} of the host's pack"));
            yield break;
        }
        string stashed = F($"{itemName} went into pocket {pocket} of the host's pack");
        // From the front: WrongAngle, and the pull opens nothing.
        yield return GrabReachable(
            i => i.UIData.canBackpack && i is not Backpack,
            null,
            NearItems,
            5,
            spawn: MpFood
        );
        if (!stepOk || stepSkip != null)
        {
            stepSkip = null;
            stepNote = stashed + "; no second item for the front check";
            yield break;
        }
        var front = host.Center + host.data.lookDirection_Flat.normalized * 1f;
        yield return MpWalkBounded(() => front, 0.3f, MpPackWalkSeconds);
        Face(visuals.transform.position);
        free = !LocalHold.ItemInLeftHand;
        yield return CarryFoodBack(!free);
        PointAt(free, visuals.transform.position);
        yield return Until(
            () => Points(free, visuals, DenyReason.WrongAngle),
            1.5f,
            "from the host's front the pack did not show WrongAngle"
        );
        if (!stepOk)
        {
            stepNote = stashed + "; " + PointState();
            yield break;
        }
        LookAtHand(free);
        yield return Wait(0.3f);
        float mark = CueMark();
        Script(free).Trigger = 1f;
        yield return ExpectCue(free, DenyReason.WrongAngle, mark);
        Script(free).Trigger = 0f;
        EndGlance();
        if (stepOk && c.data.usingBackpackWheel)
            Fail("from the front the pull opened the wheel");
        else if (stepOk)
            stepNote = stashed + "; from the front WrongAngle refused the pull";
        // The next steps start empty-handed, as vr.330 leaves A. Clean-up, not part of the verdict (MP run 17, #100:
        // DropHeld's 3 s ran out on the front check's TrailMix, which left the hand later, and failed a step whose
        // stash and refusal had both worked).
        yield return MpDropHeld();
        if (HeldItem() != null)
            stepNote += "; still holding " + HeldItem().name + " after the clean-up drop";
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
        if (!stepOk || stepSkip != null)
            yield break;
        var hands = applyHands;
        float low = VRInput.HeadPosition.y * 0.6f;
        float drop = VRInput.HeadPosition.y - low;
        VRInput.TestOverride = () =>
        {
            hands();
            var p = VRInput.HeadPosition;
            p.y = low;
            VRInput.HeadPosition = p;
            // The hands crouch with the head, as a player's do. Left at standing height they sat level with the
            // crouched head and 0.4 m ahead, so a modded peer drew A's arms up there (net.pose), B landed on them and
            // slid off before A jumped: no boost on modded B in MP runs 20, 21 and 23, while vanilla B (A's arms on
            // the game's crouch animation) was boosted.
            VRInput.Left.Position.y -= drop;
            VRInput.Right.Position.y -= drop;
        };
        try
        {
            yield return WaitFor(() => c.data.isCrouching, 2f);
            if (!c.data.isCrouching)
            {
                stepSkip = "A did not crouch (Locomotion.PhysicalCrouch off?)";
                yield break;
            }
            // B lands on A's head and slides off within about a second (MP runs 19 and 21: B 0.65 m off A at the
            // checkpoint, so the jump 1.4 s after the warp boosted nobody). The jump goes the moment B is seen standing
            // on A, and the warp is asked again if B slid off first.
            var host = Host();
            bool onTop = false;
            float mark = CueMark();
            for (int ask = 0; ask < 2 && !onTop; ask++)
            {
                yield return Ask(MpRequest.StandOn);
                if (stepSkip != null)
                    yield break;
                float until = Now + 3f;
                // The host's height a moment ago: its remote position steps at the network rate, so a per-frame
                // speed reads 0 between packets while it still falls (MP run 32: the jump went 0.9 s after the warp,
                // before B had settled on A, and boosted nobody).
                float sampledY = host.Center.y;
                float sampledAt = Now;
                float onHeadFor = 0f;
                while (Now < until && !onTop)
                {
                    yield return null;
                    var flat = host.Center - c.Center;
                    flat.y = 0f;
                    bool still = true;
                    if (Now - sampledAt >= 0.2f)
                    {
                        still = Mathf.Abs(host.Center.y - sampledY) < 0.05f;
                        sampledY = host.Center.y;
                        sampledAt = Now;
                    }
                    // On A's head and settled there (the warp drops B from 0.6 m above): B's own check needs B already
                    // standing on the crouched A when A's jump arrives, as JumpRpc ends A's crouch
                    // (CharacterMovement.cs:646, :1006-1013, :1021). B's centre at least 0.45 m over A's crouched head:
                    // a B standing on the ground beside A has its centre about 0.1 m over it (MP run 16, #108: B slid
                    // off, stood 0.56 m from A, counted as on top, and the jump 2.36 s after the warp boosted nobody).
                    // Standing on the head puts it about 0.85 m over; the stillness check keeps out a B still falling.
                    bool there =
                        flat.magnitude < 0.45f
                        && host.Center.y > c.Head.y + 0.45f
                        && host.Center.y < c.Head.y + 1.2f;
                    onHeadFor = there && still ? onHeadFor + Time.unscaledDeltaTime : 0f;
                    onTop = onHeadFor >= 0.3f;
                }
            }
            if (!onTop)
            {
                stepSkip = "the host never came to stand on A after two warps";
                yield break;
            }
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
            // B's boost is the step: B's own JumpRpc(true) reaches A too and stamps sincePalJump on A's copy of B
            // (CharacterMovement.cs:625-629, Character.cs:1238).
            yield return Until(
                () => host.data.sincePalJump < 1f,
                1.5f,
                F(
                    $"the host was not boosted: B {host.Center.y - c.Head.y:0.00} m over A's head and {FlatDistance(host.Center, c.Center):0.00} m off it at the jump; pressure felt on A: {felt}"
                )
            );
            if (!stepOk)
                yield break;
            stepNote =
                $"crouched under the host and jumped, the host was boosted; pressure felt on A: {felt}";
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
