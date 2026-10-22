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
        yield return MpStep("eat", 60f, true, Eat());
        yield return MpStep("holster", 60f, true, Holster());
        yield return MpStep("climb", 120f, true, MpClimb());
        yield return MpStep("two-hand", 60f, true, MpTwoHand());
        yield return MpStep("feed", 60f, true, Feed());
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
    /// still for <see cref="SettleHold"/>. A body that never settles fails the step with the reason, so a step never
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
        yield return SettleRecenter(rig);
        if (!stepOk)
            yield break;
        yield return StandStill(c);
        if (settledFor < SettleHold)
        {
            Fail("not settled: " + SettleProblem(c, needSpot: false));
            yield break;
        }
        yield return body;
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
        // On foot, so only a wall near the player (FindWall also looks across the whole segment).
        if (!FindWall(c, out var wall, out var stand) || Vector3.Distance(stand, c.Center) > 40f)
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
        // Feeding a friend is the secondary use: its own hold time, after up to 6 s reaching the friend's head.
        float end = Now + GameTiming.Budget(GameTiming.FeedSeconds(item), 6f);
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

    /// <summary>
    /// Grabs the first of up to three ground items (<paramref name="first"/>, then the nearest matching
    /// <paramref name="ok"/>) that the walk actually reaches; one on a ledge or behind rock is passed over (MP run 7:
    /// holster walked into rock 4.2 m short of the thrown drink). SKIP when none can be reached.
    /// </summary>
    IEnumerator GrabReachable(Func<Item, bool> ok, Item first = null)
    {
        var tried = new HashSet<Item>();
        for (int i = 0; i < 3; i++)
        {
            var item =
                i == 0 && first != null ? first : NearestGround(x => ok(x) && !tried.Contains(x));
            if (item == null)
                break;
            tried.Add(item);
            yield return WalkTo(
                () => item != null ? item.Center() : Character.localCharacter.Center,
                0.55f,
                false
            );
            var to = item != null ? item.Center() - Character.localCharacter.Center : Vector3.zero;
            to.y = 0f;
            if (item == null || to.magnitude > 0.8f)
                continue;
            yield return GrabWorld(item);
            yield break;
        }
        stepSkip = $"no reachable item among {tried.Count} tried within {NearItems:0} m";
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

    /// <summary>
    /// The social checkpoints (WP4), after <c>feed</c>. The host is a vanilla player A cannot steer, so each step
    /// reads the host's state and is SKIP when it does not fit (not passed out, no pack, not in trouble, no kick mode
    /// in the host's run). Everything A does goes through interact.social's grips and the game's own RPCs.
    /// </summary>
    IEnumerator MpSocialSteps()
    {
        yield return MpStep("feed-refuse", 60f, true, FeedRefuse());
        yield return MpStep("reach-deny", 40f, true, ReachDeny());
        yield return MpStep("reach", 40f, true, MpReach());
        yield return HostWake();
        yield return MpStep("kick-mode", 40f, true, KickMode());
        yield return MpStep("carry", 60f, true, MpCarry());
        yield return HostWake();
        yield return MpStep("pack", 40f, true, MpPack());
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
    /// (RPCA_ReviveAtPosition, Character.cs:1802) where it lies.</summary>
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

    /// <summary>A grip closes on the host's right hand (where a helping hand goes).</summary>
    IEnumerator GripHostHand(bool left)
    {
        var host = Host();
        yield return MoveTo(left, () => HostPart(host, BodypartType.Hand_R), 0.5f);
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
        yield return GrabReachable(i =>
            !i.canUseOnFriend
            && !IsFood(i)
            && info.Bind(i)
            && info.Profile.Handedness == Handedness.OneHanded
        );
        if (!stepOk || stepSkip != null)
            yield break;
        var item = HeldItem();
        yield return WalkTo(HostCenter, 0.6f, true);
        if (!stepOk)
            yield break;
        var host = Host();
        var rig = VRRig.Instance;
        // The host's mouth must be within arm's reach of the head, not on a ledge below (MP run 7: the hand was driven
        // 1.4 m under the floor toward a host down the slope).
        float gap = Vector3.Distance(rig.Head.position, host.Head);
        if (gap > MpArmReach)
        {
            stepSkip = $"the host's head is {gap:0.00} m from A's, out of arm's reach";
            yield break;
        }
        Face(host.Head);
        bool left = LocalHold.ItemInLeftHand;
        float mark = CueMark();
        float end = Now + 3f;
        float best = float.MaxValue;
        while (Now < end && HeldItem() == item)
        {
            best = Mathf.Min(best, Vector3.Distance(item.Center(), host.Head));
            var goal =
                Script(left).Position
                + rig.Origin.InverseTransformVector(host.Head - item.Center());
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
        bool fired = CueLog.Capture.Fired(left, DenyReason.CantUse, mark, true, out _);
        if (!fired)
            Note($"{item.name} came within {best:0.00} m of the host's head");
        yield return ExpectCue(left, DenyReason.CantUse, mark, true, 0f);
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
        float mark = CueMark();
        yield return GripHostHand(false);
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

    /// <summary>
    /// carry: the host is passed out; both hands on their body lift them (RPCA_StartCarry, CharacterCarrying.cs:56).
    /// A worn pack drops at their torso (:59-67), marked by a Dropped notice. A grip on the carried host drops them.
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
        yield return ToHost(0.6f);
        if (!stepOk)
            yield break;
        bool pack = c.player != null && !c.player.backpackSlot.IsEmpty();
        float mark = CueMark();
        yield return MoveTo(true, () => HostPart(host, BodypartType.Torso), 0.5f);
        yield return MoveTo(false, () => HostPart(host, BodypartType.Hip), 0.5f);
        Script(true).Grip = Script(false).Grip = 1f;
        float hold = GameTiming.InteractSeconds(host.refs.interactible, c);
        yield return Until(
            () => c.data.carriedPlayer == host,
            GameTiming.Budget(hold, 1f),
            "the host was not lifted"
        );
        if (!stepOk)
            yield break;
        if (pack)
            yield return ExpectCue(SocialHands.Instance.CarryLeft, DenyReason.Dropped, mark);
        if (!stepOk)
            yield break;
        Script(true).Grip = Script(false).Grip = 0f;
        yield return Wait(0.5f);
        yield return MoveTo(false, () => HostPart(host, BodypartType.Torso), 0.4f);
        Script(false).Grip = 1f;
        yield return Until(
            () => c.data.carriedPlayer == null,
            GameTiming.Budget(hold, 1f),
            "the host was not put down"
        );
        Script(false).Grip = 0f;
        if (stepOk)
            stepNote = pack ? "lifted, pack dropped and marked, put down" : "lifted and put down";
    }

    /// <summary>pack: the host's worn pack, gripped from behind while A looks away; the hold starts
    /// (BackpackOnBackVisuals.IsConstantlyInteractable, :152) because the look check follows the hand.</summary>
    IEnumerator MpPack()
    {
        var host = Host();
        var visuals =
            host != null && host.refs.backpackHandler != null
                ? host.refs.backpackHandler.activeBackpackVisuals
                : null;
        if (visuals == null || !visuals.isActiveAndEnabled)
        {
            stepSkip = "the host wears no pack";
            yield break;
        }
        yield return FreeHands();
        if (!stepOk)
            yield break;
        yield return WalkTo(() => host.Center - host.data.lookDirection_Flat * 0.8f, 0.3f, true);
        if (!stepOk)
            yield break;
        Face(visuals.transform.position);
        // Turn away: vanilla needs the look within 110° of the pack's facing (BackpackOnBackVisuals.cs:99-104).
        VRRig.Instance.RotateYaw(150f);
        yield return Wait(0.3f);
        yield return MoveTo(false, () => visuals.transform.position, 0.6f);
        yield return GripTo(false, 1f, 0.1f);
        yield return Until(
            () =>
                Interaction.instance != null
                && Interaction.instance.currentHeldInteractible is BackpackOnBackVisuals,
            2f,
            "the pack hold did not start"
        );
        Script(false).Grip = 0f;
        if (stepOk)
            stepNote = "host's pack held from behind while looking away";
    }

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
    /// campfire-far (Campfire.cs:315-326, EveryoneInRange :375-386): A grips an unlit campfire within reach while the
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
        yield return ExpectCue(cueLeft, DenyReason.CantSwitch, mark, false, 0.5f);
        if (stepOk)
            stepNote = "spectating A's own body; flick refused: " + cueNote;
    }
}
