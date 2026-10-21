using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using PeakXR.Avatar;
using PeakXR.Core;
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
/// drop, pick up, throw, eat, holster and draw, climb, two-hand hold, feed the host. Every step ends with a line in
/// <c>a-checkpoints.jsonl</c>, which the observer answers with its own snapshot under the same name. Nothing here
/// spawns, warps or uses the sandbox: online, every action is one the controllers can make.
/// </summary>
internal sealed partial class SelfTestRunner
{
    const float MpMinutes = 30f;

    /// <summary>Hold still after each checkpoint so the observer's snapshot (polled every 0.25 s) sees the same state.</summary>
    const float CheckpointHold = 2f;

    const float NearItems = 40f;

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
        yield return MpStep("eat", 60f, true, Eat());
        yield return MpStep("holster", 60f, true, Holster());
        yield return MpStep("climb", 120f, true, MpClimb());
        yield return MpStep("two-hand", 60f, true, MpTwoHand());
        yield return MpStep("feed", 60f, true, Feed());
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
        yield return Run(id, timeout, body);
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
    /// (NetworkConnector.cs:155-167). Then it checks the version, the lobby and that the master is the host.
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
    /// The boarding pass's Start (BoardingPass.cs:228) at the kiosk any player may open: AirportCheckInKiosk.StartGame
    /// (AirportCheckInKiosk.cs:117-122) asks the master, here the vanilla host, to load the island (:135-152).
    /// </summary>
    IEnumerator Board()
    {
        var kiosk = FindAnyObjectByType<AirportCheckInKiosk>();
        if (kiosk == null)
        {
            Fail("no AirportCheckInKiosk");
            yield break;
        }
        if (VRSession.Active)
            yield return WalkTo(() => kiosk.transform.position, 2.5f, false);
        kiosk.StartGame(0);
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
        stepNote = "on " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
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
        yield return ThrowHeld();
        if (lastLanding is { } landed)
        {
            Extra("landing", landed);
            var host = Host();
            if (host != null)
                cpExtra["landingToHost"] = F(Vector3.Distance(landed, host.Center));
            mpItem = NearestGround(i => i.itemID == cpItemId, landed);
        }
    }

    IEnumerator Eat()
    {
        yield return FreeHands();
        if (!stepOk)
            yield break;
        var food = NearestGround(IsFood);
        if (food == null)
        {
            stepSkip = $"no food within {NearItems:0} m";
            yield break;
        }
        cpItemId = food.itemID;
        yield return GrabWorld(food);
        if (!stepOk)
            yield break;
        yield return EatHeld();
    }

    IEnumerator Holster()
    {
        var item = HeldItem() == null ? OnGround(mpItem) ?? NearestGround(_ => true) : null;
        if (item != null)
            yield return GrabWorld(item);
        if (!stepOk)
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
        if (!FindWall(c, out var wall, out var stand))
        {
            stepSkip = "no climbable wall with walkable ground in front within 40 m";
            yield break;
        }
        yield return WalkTo(() => stand, 0.4f, true);
        if (!stepOk)
            yield break;
        var rig = VRRig.Instance;
        rig.RotateYaw(Vector3.SignedAngle(Flat(rig.Head.forward), -Flat(wall.normal), Vector3.up));
        yield return Wait(0.6f);
        yield return ClimbWall(wall);
        Script(VRInput.LeftHanded).Grip = 0f;
        yield return WaitFor(() => !c.data.isClimbing, 2f);
        yield return Wait(1f);
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
    /// game's feed path runs (Item.SendFeedDataRPC, Item.cs:996; Character.FeedItem, Character.cs:1640 ->
    /// GetFedItemRPC :1646 on the host, who needs to do nothing).
    /// </summary>
    IEnumerator Feed()
    {
        yield return FreeHands();
        if (!stepOk)
            yield break;
        var food = NearestGround(i => IsFood(i) && i.canUseOnFriend);
        if (food == null)
        {
            stepSkip = $"no food to feed within {NearItems:0} m";
            yield break;
        }
        cpItemId = food.itemID;
        yield return GrabWorld(food);
        if (!stepOk)
            yield break;
        var item = HeldItem();
        yield return WalkTo(HostCenter, 0.75f, true);
        if (!stepOk)
            yield break;
        var host = Host();
        Face(host.Head);
        yield return Wait(0.3f);
        var rig = VRRig.Instance;
        bool left = LocalHold.ItemInLeftHand;
        yield return GripTo(left, 1f, 0.1f);
        float end = Now + item.usingTimePrimary + 8f;
        float best = float.MaxValue;
        while (item != null && HeldItem() == item && Now < end && host != null)
        {
            var miss = host.Head - item.Center();
            best = Mathf.Min(best, miss.magnitude);
            var goal = Script(left).Position + rig.Origin.InverseTransformVector(miss);
            SetHand(
                left,
                HandScript
                    .Approach(Script(left).Position.N(), goal.N(), 1.2f, Time.unscaledDeltaTime)
                    .U()
            );
            yield return null;
        }
        var note = $"item closest {best:0.00} m to the host's head";
        if (item != null && HeldItem() == item)
            Fail("not fed: " + note);
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

    /// <summary>The other player: the first non-bot remote character (Character.cs:128 AllCharacters).</summary>
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

    /// <summary>Food restores hunger when eaten (<c>Action_RestoreHunger</c> on the item).</summary>
    static bool IsFood(Item item) => item.GetComponent<Action_RestoreHunger>() != null;

    /// <summary>A ground item the hand can take. A ground backpack opens its wheel instead (Backpack.cs:32-35), so it is
    /// never a generic grab target; vr.29 covers it.</summary>
    static bool Grabbable(Item item) =>
        item != null && item.itemState == ItemState.Ground && item is not Backpack;

    static Item OnGround(Item item) => Grabbable(item) ? item : null;

    /// <summary>The nearest grabbable ground item (Item.cs:103 ALL_ITEMS, :286 itemState) within <see cref="NearItems"/> m.</summary>
    static Item NearestGround(Func<Item, bool> ok, Vector3? from = null)
    {
        var at = from ?? Character.localCharacter.Center;
        Item best = null;
        float bestSq = NearItems * NearItems;
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
}
