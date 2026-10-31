using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ExitGames.Client.Photon;
using PeakXR.Core;
using Photon.Pun;
using Steamworks;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zorro.Core;

namespace MpObserver;

/// <summary>
/// The observer's script: host from the title, publish the lobby id, then log every remote character twice a
/// second and answer each checkpoint the VR side writes with a snapshot and a screenshot. Files (all in the shared
/// directory): <c>lobby.json</c>, <c>b-observer.jsonl</c>, <c>b-summary.json</c>, <c>b-&lt;checkpoint&gt;.png</c>.
/// It reads <c>a-checkpoints.jsonl</c>, <c>a-requests.jsonl</c> and <c>a-done.json</c> from the VR side and answers
/// requests in <c>b-acks.jsonl</c>.
/// </summary>
internal sealed partial class Observer : MonoBehaviour
{
    const float SampleInterval = 0.5f;
    const float PollInterval = 0.25f;
    const float JoinWaitMinutes = 10f;
    const int MaxLogLines = 300;
    const int MaxRpcsPerCheckpoint = 60;

    internal string Dir;

    readonly Dictionary<byte, int> eventCounts = new();
    readonly Dictionary<string, int> rpcCounts = new(StringComparer.Ordinal);
    readonly List<string> rpcsSince = new();
    readonly List<string> firstExceptions = new();
    readonly HashSet<int> seenActors = new();

    string outPath;
    double lobbyTime = double.MaxValue;
    readonly LogCursor checkpointLines = new();
    readonly LogCursor requestLines = new();
    int checkpoints;

    /// <summary>When the last of A's checkpoints came in (our clock), -1 before any: B stays while they keep coming
    /// (<see cref="MpRunLimit"/>).</summary>
    float lastCheckpointAt = -1f;
    int samples;
    int exceptions;
    int errors;
    int unknownEventErrors;
    int logLines;
    string endReason = "running";
    bool finished;

    static double Unix => (DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds;

    static float Now => Time.realtimeSinceStartup;

    void Start()
    {
        var marker = Path.Combine(Dir, MpDir.Marker);
        bool clear = MpDir.MayClear(
            Dir,
            MpDir.UnixHome(
                Environment.GetEnvironmentVariable("HOME"),
                Environment.GetEnvironmentVariable("WINEHOMEDIR")
            ),
            Directory.Exists(Dir),
            File.Exists(marker)
        );
        Directory.CreateDirectory(Dir);
        if (clear)
        {
            File.WriteAllText(marker, "");
            foreach (var old in Directory.GetFiles(Dir, "b-*"))
                File.Delete(old);
            File.Delete(Path.Combine(Dir, "lobby.json"));
        }
        else
            ObserverPlugin.Log.LogWarning(
                $"not deleting old b-* files in {Dir}: the test did not make that folder; use an empty one"
            );
        outPath = Path.Combine(Dir, "b-observer.jsonl");
        Application.logMessageReceived += OnLog;
        PhotonNetwork.NetworkingClient.EventReceived += OnEvent;
        RpcProbe.Seen = OnRpc;
        Write(
            new JsonLine()
                .Str("type", "start")
                .Num("t", Unix)
                .Str("game", Application.version)
                .Str("dir", Dir)
        );
        Write(
            new JsonLine()
                .Str("type", "save")
                .Num("t", Unix)
                .Str("when", "start")
                .Str("path", ProgressGuard.SavePath)
                .Str("fingerprint", ProgressGuard.SaveAtStart.ToString())
        );
        StartPeakXR();
        StartCoroutine(Flow());
    }

    void OnDestroy()
    {
        Application.logMessageReceived -= OnLog;
        PhotonNetwork.NetworkingClient.EventReceived -= OnEvent;
        RpcProbe.Seen = null;
    }

    IEnumerator Flow()
    {
        // Title: the main page with its Host button (MainMenuMainPage.cs:43 PlayButton) and no load running.
        MainMenuMainPage page;
        float end = Now + 180f;
        while (
            (page = FindAnyObjectByType<MainMenuMainPage>()) == null
            || !page.isActiveAndEnabled
            || LoadingScreenHandler.loading
        )
        {
            if (Now > end)
            {
                Finish("title not reached in 180 s");
                yield break;
            }
            yield return null;
        }
        yield return new WaitForSecondsRealtime(5f);

        // The VR client joins by lobby id; the default InviteOnly maps to a Private lobby (LobbyTypeSetting.cs:34,
        // SteamLobbyAPI.cs:78-81) that Steam opens to invites only. Friends is the settings menu's other choice.
        var handler = GameHandler.Instance.SettingsHandler;
        var lobbyType = handler.GetSetting<LobbyTypeSetting>();
        if (lobbyType.Value != LobbyTypeSetting.LobbyType.Friends)
        {
            lobbyType.SetValue(LobbyTypeSetting.LobbyType.Friends, handler, false);
            Note("lobby type set to Friends (saved, as the settings menu does)");
        }

        // Host: PlayClicked (MainMenuMainPage.cs:47, :228) -> StartMultiplayerLobby (:250) ->
        // NetCode.Matchmaking.CreateLobby (:271) -> SteamLobbyHandler.OnLobbyCreated (SteamLobbyHandler.cs:396) loads the
        // Airport (:419); NetworkConnector creates the Photon room (NetworkConnector.cs:143-153).
        page.PlayButton.onClick.Invoke();
        Note("host clicked");
        var lobbies = GameHandler.GetService<SteamLobbyHandler>();
        CSteamID lobby;
        end = Now + 240f;
        while (
            !SceneManager.GetActiveScene().name.Contains("Airport", StringComparison.Ordinal)
            || LoadingScreenHandler.loading
            || !PhotonNetwork.InRoom
            || PhotonNetwork.OfflineMode
            || Character.localCharacter == null
            || !lobbies.InSteamLobby(out lobby)
        )
        {
            if (Now > end)
            {
                Finish("hosted room not ready in 240 s");
                yield break;
            }
            yield return null;
        }
        yield return new WaitForSecondsRealtime(2f);

        lobbyTime = Unix;
        var lobbyJson = new JsonLine()
            .Str("lobbyId", lobby.m_SteamID.ToString(CultureInfo.InvariantCulture))
            .Str(
                "hostSteamId",
                SteamUser.GetSteamID().m_SteamID.ToString(CultureInfo.InvariantCulture)
            )
            .Num("time", lobbyTime)
            .Str("room", PhotonNetwork.CurrentRoom.Name)
            .Str("peakVersion", SteamMatchmaking.GetLobbyData(lobby, "PeakVersion"))
            .Str("game", Application.version)
            .ToString();
        var tmp = Path.Combine(Dir, "lobby.json.tmp");
        File.WriteAllText(tmp, lobbyJson + "\n");
        File.Move(tmp, Path.Combine(Dir, "lobby.json"));
        Write(new JsonLine().Str("type", "lobby").Num("t", lobbyTime).Raw("lobby", lobbyJson));

        float joinedAt = -1f;
        float nextSample = 0f;
        float nextPoll = 0f;
        end = Now + JoinWaitMinutes * 60f;
        while (!finished)
        {
            if (Now >= nextSample)
            {
                nextSample = Now + SampleInterval;
                if (SampleAll() && joinedAt < 0f)
                {
                    joinedAt = Now;
                }
            }
            if (Now >= nextPoll)
            {
                nextPoll = Now + PollInterval;
                yield return PollCheckpoints();
                PollRequests();
                if (DoneWritten())
                {
                    Finish("vr side done");
                    yield break;
                }
            }
            if (joinedAt >= 0f)
                end = MpRunLimit.End(joinedAt, lastCheckpointAt);
            if (Now > end)
            {
                Finish(joinedAt < 0f ? "nobody joined" : "time limit");
                yield break;
            }
            yield return null;
        }
    }

    // ------------------------------------------------------------------ samples

    /// <summary>Logs every remote character; true when at least one is present.</summary>
    bool SampleAll()
    {
        bool any = false;
        foreach (var c in Character.AllCharacters)
        {
            if (c == null || c.IsLocal || c.isBot)
                continue;
            any = true;
            var owner = c.refs.view.Owner;
            int actor = owner != null ? owner.ActorNumber : -1;
            if (seenActors.Add(actor))
            {
                Write(
                    new JsonLine()
                        .Str("type", "join")
                        .Num("t", Unix)
                        .Num("actor", actor)
                        .Str("userId", owner?.UserId)
                        .Str("name", c.characterName)
                        .Str("scene", SceneManager.GetActiveScene().name)
                );
            }
            samples++;
            Write(Snapshot(new JsonLine().Str("type", "sample").Num("t", Unix), c));
        }
        SampleFlat(FirstRemote());
        return any;
    }

    /// <summary>What vanilla shows of a remote character: Character.cs:273/:275 Center/Head, CharacterData lookValues
    /// (:77), currentItem (:417), climbing (:118-124), carry (:239-265), passedOut/fallSeconds (:18-20, :230), the hand
    /// IK targets CharacterAnimations.ConfigureIK writes (Character.cs:87/:89, CharacterAnimations.cs:390-408).</summary>
    JsonLine Snapshot(JsonLine j, Character c)
    {
        var d = c.data;
        var item = d.currentItem;
        var refs = c.refs;
        j.Num("actor", refs.view.Owner != null ? refs.view.Owner.ActorNumber : -1)
            .Str("scene", SceneManager.GetActiveScene().name)
            .Raw("pos", V(c.Center))
            .Raw("head", V(c.Head))
            .Raw("look", "[" + F(d.lookValues.x) + "," + F(d.lookValues.y) + "]")
            .Str("item", item != null ? item.name : null)
            .Num("itemId", item != null ? item.itemID : -1)
            .Str("itemState", item != null ? item.itemState.ToString() : null)
            .Bool("usingPrimary", item != null && item.isUsingPrimary)
            .Bool("usingSecondary", item != null && item.isUsingSecondary)
            .Num("cast", item != null ? item.castProgress : 0f)
            .Bool("grounded", d.isGrounded)
            .Bool("sprinting", d.isSprinting)
            .Bool("crouching", d.isCrouching)
            .Bool("reaching", d.isReaching)
            .Bool("climbing", d.isClimbing)
            .Bool("ropeClimbing", d.isRopeClimbing)
            .Bool("vineClimbing", d.isVineClimbing)
            .Bool("carried", d.isCarried)
            .Bool("carrying", d.carriedPlayer != null)
            .Bool("passedOut", d.passedOut)
            .Bool("fullyPassedOut", d.fullyPassedOut)
            .Bool("dead", d.dead)
            .Num("fallSeconds", d.fallSeconds)
            .Bool("emoteWheel", d.usingEmoteWheel)
            .Raw(
                "ikLeft",
                refs.IKHandTargetLeft != null ? V(refs.IKHandTargetLeft.position) : "null"
            )
            .Raw(
                "ikRight",
                refs.IKHandTargetRight != null ? V(refs.IKHandTargetRight.position) : "null"
            )
            .Num("ev151", Count(151))
            .Num("events", TotalEvents());
        PoseFields(j, c);
        return j;
    }

    // ------------------------------------------------------------------ checkpoints

    IEnumerator PollCheckpoints()
    {
        var path = Path.Combine(Dir, "a-checkpoints.jsonl");
        if (!File.Exists(path))
            yield break;
        string[] lines;
        try
        {
            lines = ReadShared(path).Split('\n');
        }
        catch (IOException)
        {
            yield break;
        }
        // The last element is the unfinished line (or empty after the final newline).
        checkpointLines.Sync(lines);
        for (; checkpointLines.Next < lines.Length - 1; checkpointLines.Next++)
        {
            var line = lines[checkpointLines.Next];
            if (!JsonLine.TryReadNumber(line, "t", out var t) || t < lobbyTime)
                continue;
            var id = JsonLine.ReadString(line, "name") ?? "unnamed";
            yield return Checkpoint(id, line);
        }
    }

    IEnumerator Checkpoint(string name, string aLine)
    {
        checkpoints++;
        lastCheckpointAt = Now;
        var remote = FirstRemote();
        var local = Character.localCharacter;
        var j = new JsonLine().Str("type", "checkpoint").Str("name", name).Num("t", Unix);
        j.Bool("remotePresent", remote != null);
        if (remote != null)
            Snapshot(j, remote);
        if (JsonLine.TryReadNumber(aLine, "itemId", out var id) && id >= 0)
            j.Raw("groundItems", GroundItems((ushort)id, remote));
        if (local != null)
        {
            j.Raw("localPos", V(local.Center))
                .Num("localLastConsumed", local.data.lastConsumedItem)
                .Num(
                    "localHunger",
                    local.refs.afflictions.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Hunger)
                )
                // A friend's reach clears Web on the helped client (CharacterGrabbing.Reach, :247-276).
                .Num(
                    "localWeb",
                    local.refs.afflictions.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Web)
                )
                .Num("localSinceGrab", local.data.sinceGrabFriend)
                // Player.backpackSlot (Player.cs:21): still worn after A held it from behind.
                .Bool("localPack", local.player != null && !local.player.backpackSlot.IsEmpty());
        }
        j.Raw("rpcs", "[" + string.Join(",", rpcsSince) + "]");
        rpcsSince.Clear();
        Write(j);

        // Turn our own view to the VR player, as a mouse would (CharacterMovement.cs:286-288 adds input to
        // lookValues; HelperFunctions.cs:183 LookToEuler maps x to yaw, y to pitch up), then capture.
        if (remote != null && local != null)
        {
            var to = remote.Center - local.Head;
            if (to.sqrMagnitude > 0.01f)
            {
                to.Normalize();
                local.data.lookValues = new Vector2(
                    Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg,
                    Mathf.Clamp(Mathf.Asin(to.y) * Mathf.Rad2Deg, -85f, 85f)
                );
            }
        }
        yield return null;
        yield return null;
        ScreenCapture.CaptureScreenshot(Path.Combine(Dir, "b-" + SafeName(name) + ".png"), 1);
        yield return null;
    }

    /// <summary>World positions of ground items of one kind (Item.cs:103 ALL_ITEMS, :286 itemState), nearest the remote
    /// player first, at most 8.</summary>
    static string GroundItems(ushort itemId, Character near)
    {
        var found = new List<Vector3>();
        foreach (var item in Item.ALL_ITEMS)
        {
            if (item != null && item.itemID == itemId && item.itemState == ItemState.Ground)
                found.Add(item.Center());
        }
        if (near != null)
        {
            var at = near.Center;
            found.Sort((a, b) => (a - at).sqrMagnitude.CompareTo((b - at).sqrMagnitude));
        }
        var sb = new StringBuilder("[");
        for (int i = 0; i < found.Count && i < 8; i++)
            sb.Append(i > 0 ? "," : "").Append(V(found[i]));
        return sb.Append(']').ToString();
    }

    // ------------------------------------------------------------------ requests

    /// <summary>
    /// The VR side's requests (<see cref="MpRequest"/>), each done once to our own character with what the game
    /// itself offers: its console command, its own RPCs on our view, or our own input, as a player at the keyboard
    /// could. Answered in <c>b-acks.jsonl</c> and logged as a <c>request</c> line.
    /// </summary>
    void PollRequests()
    {
        var path = Path.Combine(Dir, MpRequest.RequestFile);
        if (!File.Exists(path))
            return;
        string[] lines;
        try
        {
            lines = ReadShared(path).Split('\n');
        }
        catch (IOException)
        {
            return;
        }
        requestLines.Sync(lines);
        for (; requestLines.Next < lines.Length - 1; requestLines.Next++)
        {
            if (
                !MpRequest.TryRead(lines[requestLines.Next], out int id, out var what, out double t)
                || t < lobbyTime
            )
                continue;
            bool ok;
            string note;
            try
            {
                ok = Execute(what, lines[requestLines.Next], out note);
            }
            catch (Exception e)
            {
                ok = false;
                note = e.GetType().Name + ": " + e.Message;
            }
            var ack = MpRequest.Ack(id, what, ok, note, Unix);
            Write(new JsonLine().Str("type", "request").Num("t", Unix).Raw("ack", ack));
            try
            {
                File.AppendAllText(Path.Combine(Dir, MpRequest.AckFile), ack + "\n");
            }
            catch (IOException e)
            {
                ObserverPlugin.Log.LogWarning("ack write failed: " + e.Message);
            }
        }
    }

    static bool Execute(string what, string line, out string note)
    {
        var c = Character.localCharacter;
        note = "";
        if (c == null)
        {
            note = "no local character";
            return false;
        }
        switch (what)
        {
            case MpRequest.PassOut:
                // The game's console command (Character.cs:986): Starve, then RPCA_PassOut.
                if (c.data.passedOut || c.data.dead)
                {
                    note = "already down";
                    return true;
                }
                Character.PassOut();
                // A body wakes 3 s after passing out once its statuses sum under 1 (Character.HandlePassedOut,
                // :574-579). Hunger is refused under a NoHunger buff (CharacterAfflictions.canGetHungry, :211; the
                // campfire's, Campfire.cs:246): MP run 13's host woke the moment A lifted it, and the carry dropped
                // it (CharacterCarrying.Update, :29-31). Injury fills the rest, so the host stays out.
                var afflictions = c.refs.afflictions;
                bool hunger = afflictions.statusSum >= 1f;
                if (!hunger)
                    afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Injury, 1f);
                float sum = afflictions.statusSum;
                note =
                    (hunger ? "passed out" : "passed out (Hunger refused, Injury added)")
                    + ", statuses "
                    + F(sum);
                return sum >= 1f;
            case MpRequest.Web:
                // CharacterAfflictions.AddStatus (:712), as a spider's web does; the game refuses it in the airport.
                bool webbed = c.refs.afflictions.AddStatus(
                    CharacterAfflictions.STATUSTYPE.Web,
                    0.6f
                );
                note = webbed ? "webbed" : "the game refused Web (airport?)";
                return webbed;
            case MpRequest.Wake:
                // ClearAllStatus (CharacterAfflictions.cs:1427), then the revive RPC the game's own revives send
                // (RPCA_ReviveAtPosition, Character.cs:1802) where the body lies.
                InputHold.Crouch = false;
                c.refs.afflictions.ClearAllStatus(excludeCurse: false, excludePetrify: false);
                if (c.data.dead || c.data.passedOut)
                    c.refs.view.RPC("RPCA_ReviveAtPosition", RpcTarget.All, c.Center, false, -1);
                note = "awake";
                return true;
            case MpRequest.Crouch:
                InputHold.Crouch = true;
                note = "crouching";
                return true;
            case MpRequest.Stand:
                InputHold.Crouch = false;
                note = "standing";
                return true;
            case MpRequest.StandOn:
                // The game's own warp RPC on our view (Character.cs:1822), onto the VR player's head.
                var vr = FirstRemote();
                if (vr == null)
                {
                    note = "no remote player";
                    return false;
                }
                c.refs.view.RPC("WarpPlayerRPC", RpcTarget.All, vr.Head + Vector3.up * 0.6f, false);
                note = "warped onto " + vr.characterName;
                return true;
            case MpRequest.HangBelow:
                return HangBelow(c, line, out note);
            case MpRequest.StartClimb:
                return StartClimb(c, out note);
            case MpRequest.WearPack:
                return WearPack(c, out note);
            case MpRequest.SpawnItem:
                return SpawnItem(line, out note);
            default:
                note = "unknown request";
                return false;
        }
    }

    bool DoneWritten()
    {
        var path = Path.Combine(Dir, "a-done.json");
        if (!File.Exists(path))
            return false;
        try
        {
            return JsonLine.TryReadNumber(ReadShared(path), "t", out var t) && t >= lobbyTime;
        }
        catch (IOException)
        {
            return false;
        }
    }

    // ------------------------------------------------------------------ listeners

    void OnEvent(EventData ev)
    {
        eventCounts.TryGetValue(ev.Code, out int n);
        eventCounts[ev.Code] = n + 1;
        OnPoseEvent(ev.Code);
    }

    void OnRpc(string method, Component at, object[] args)
    {
        rpcCounts.TryGetValue(method, out int n);
        rpcCounts[method] = n + 1;
        var c = at != null ? at.GetComponentInParent<Character>() : null;
        if (c == null && at is Item item)
            c = item.holderCharacter;
        var j = new JsonLine()
            .Str("name", method)
            .Num("t", Unix)
            .Num(
                "actor",
                c != null && c.refs.view.Owner != null ? c.refs.view.Owner.ActorNumber : -1
            )
            .Bool("local", c != null && c.IsLocal)
            .Str("on", at != null ? at.name : null);
        var a = new StringBuilder("[");
        for (int i = 0; args != null && i < args.Length; i++)
        {
            a.Append(i > 0 ? "," : "");
            a.Append(
                args[i] switch
                {
                    Vector3 v => V(v),
                    float f => F(f),
                    int k => k.ToString(CultureInfo.InvariantCulture),
                    byte b => b.ToString(CultureInfo.InvariantCulture),
                    bool b => b ? "true" : "false",
                    string s => new JsonLine().Str("s", s).ToString(),
                    _ => "null",
                }
            );
        }
        j.Raw("args", a.Append(']').ToString());
        var text = j.ToString();
        if (rpcsSince.Count < MaxRpcsPerCheckpoint)
            rpcsSince.Add(text);
        Write(new JsonLine().Str("type", "rpc").Raw("rpc", text));
    }

    void OnLog(string condition, string stackTrace, LogType type)
    {
        if (finished)
            return;
        // CustomCommandListener.cs:57-60 is the game's only complaint about an event code it does not know.
        bool unknown =
            condition != null
            && condition.Contains("No registered packages for code", StringComparison.Ordinal);
        if (unknown)
            unknownEventErrors++;
        if (type == LogType.Exception)
        {
            exceptions++;
            if (firstExceptions.Count < 20)
                firstExceptions.Add(FirstLine(condition) + " @ " + FirstLine(stackTrace));
        }
        else if (type is LogType.Error or LogType.Assert)
            errors++;
        else if (!unknown)
            return;
        if (logLines++ < MaxLogLines)
        {
            Write(
                new JsonLine()
                    .Str("type", "log")
                    .Num("t", Unix)
                    .Str("level", type.ToString())
                    .Str("text", FirstLine(condition))
                    .Str("at", FirstLine(stackTrace))
            );
        }
    }

    // ------------------------------------------------------------------ finish

    void Finish(string reason)
    {
        if (finished)
            return;
        finished = true;
        endReason = reason;
        var events = new StringBuilder("{");
        foreach (var kv in eventCounts)
            events
                .Append(events.Length > 1 ? "," : "")
                .Append('"')
                .Append(kv.Key)
                .Append("\":")
                .Append(kv.Value);
        var rpcs = new JsonLine();
        foreach (var kv in rpcCounts)
            rpcs.Num(kv.Key, kv.Value);
        // The quicksave must be exactly as B found it (ProgressGuard).
        var saveEnd = FileFingerprint.Of(ProgressGuard.SavePath);
        Write(
            new JsonLine()
                .Str("type", "save")
                .Num("t", Unix)
                .Str("when", "end")
                .Str("path", ProgressGuard.SavePath)
                .Str("fingerprint", saveEnd.ToString())
        );
        var exc = new StringBuilder("[");
        for (int i = 0; i < firstExceptions.Count; i++)
            exc.Append(i > 0 ? "," : "").Append(new JsonLine().Str("e", firstExceptions[i]));
        var summary = new JsonLine()
            .Str("endReason", endReason)
            .Num("t", Unix)
            .Num("lobbyTime", lobbyTime == double.MaxValue ? double.NaN : lobbyTime)
            .Num("remotePlayersSeen", seenActors.Count)
            .Num("samples", samples)
            .Num("checkpoints", checkpoints)
            .Num("event151", Count(151))
            .Num("eventsTotal", TotalEvents())
            .Raw("eventCounts", events.Append('}').ToString())
            .Raw("rpcCounts", rpcs.ToString())
            .Num("exceptions", exceptions)
            .Num("errors", errors)
            .Num("unknownEventErrors", unknownEventErrors)
            .Raw("firstExceptions", exc.Append(']').ToString())
            .Str("saveStart", ProgressGuard.SaveAtStart.ToString())
            .Str("saveEnd", saveEnd.ToString())
            .Bool("saveSame", saveEnd.Same(ProgressGuard.SaveAtStart));
        SummaryFlat(summary);
        try
        {
            File.WriteAllText(Path.Combine(Dir, "b-summary.json"), summary.ToString() + "\n");
        }
        catch (IOException e)
        {
            ObserverPlugin.Log.LogError("cannot write the summary: " + e.Message);
        }
        ObserverPlugin.Log.LogInfo($"done ({reason}); quitting");
        Application.Quit();
        // Quit can hang under Proton after the files are written.
        new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(5000);
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        })
        {
            IsBackground = true,
        }.Start();
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// The game's warp RPC on our view (Character.cs:1824) onto the face below the VR player's lip, found the way A's
    /// lip search finds it (SelfTestRunner.MpReachOut.cs <c>Lip</c>): stepping out along A's look from A's ground, the
    /// first spot whose down cast drops 1.5-5 m, then from 1.5 m under A's ground (at least 0.5 m above the lower
    /// ground) a cast back toward A onto the face. Every cast starts in open air past the lip. The old cast started a fixed 2.5 m under A and 1 m out, which
    /// is inside the rock whenever A stands back from the edge or the face leans out (MP run 13, #95). Along the
    /// lip's outward direction when the request carries it (A measured it), else A's look: in MP run 14 the look
    /// found no drop below a lip A had found. Out to 4 m: A may stop up to 0.6 m back from its lip spot, itself 0.7 m
    /// short of the drop. From the lip's ground point when the request carries it (A measured it there), else from the
    /// ground under A as seen here: in MP run 16 A stood 1.98 m from its lip and this search found no drop (#95).
    /// </summary>
    static bool HangBelow(Character c, string line, out string note)
    {
        var vr = FirstRemote();
        if (vr == null)
        {
            note = "no remote player";
            return false;
        }
        bool given = MpRequest.TryReadDirection(line, out double dx, out double dz);
        var look = given ? new Vector3((float)dx, 0f, (float)dz) : vr.data.lookDirection_Flat;
        look.y = 0f;
        if (look.sqrMagnitude < 1e-4f)
        {
            note = "the remote player has no flat look";
            return false;
        }
        look.Normalize();
        int mask = HelperFunctions.terrainMapMask;
        const QueryTriggerInteraction ignore = QueryTriggerInteraction.Ignore;
        bool fromLip = MpRequest.TryReadPoint(line, out double px, out double py, out double pz);
        Vector3 ground;
        if (fromLip)
            ground = new Vector3((float)px, (float)py, (float)pz);
        else if (Physics.Raycast(vr.Center, Vector3.down, out var feet, 3f, mask, ignore))
            ground = feet.point;
        else
        {
            note = "no ground under the remote player";
            return false;
        }
        float deepest = 0f,
            deepestAt = 0f;
        int faceMissed = 0;
        for (float outBy = 0.25f; outBy <= 4f; outBy += 0.25f)
        {
            var above = ground + look * outBy + Vector3.up * 0.5f;
            if (!Physics.Raycast(above, Vector3.down, out var low, 6f, mask, ignore))
                continue;
            float drop = ground.y - low.point.y;
            if (drop > deepest)
            {
                deepest = drop;
                deepestAt = outBy;
            }
            if (drop > 5f)
                break;
            // About 2.5 m under A's centre as before (inside the game's 4 m reach), never under the lower ground.
            var at = low.point;
            at.y = Mathf.Max(low.point.y + 0.5f, ground.y - 1.5f);
            if (drop < 1.5f)
                continue;
            if (!Physics.Raycast(at, -look, out var face, outBy + 0.5f, mask, ignore))
            {
                faceMissed++;
                continue;
            }
            c.refs.view.RPC("WarpPlayerRPC", RpcTarget.All, face.point + face.normal * 0.3f, false);
            note = FormattableString.Invariant(
                $"warped below {vr.characterName}: {drop:0.0} m drop, face {outBy:0.00} m out"
            );
            return true;
        }
        note = FormattableString.Invariant(
            $"no face below the remote player (no 1.5-5 m drop within 4 m out along {(given ? "the lip's outward" : "its look")} from {(fromLip ? "the lip point" : "its ground")} {V(ground)}; deepest drop {deepest:0.0} m at {deepestAt:0.00} m out, {faceMissed} face cast(s) missed, the remote player at {V(vr.Center)})"
        );
        return false;
    }

    /// <summary>The game's climb-start RPC on our view (CharacterClimbing.StartClimbRpc, :349), on the face between us
    /// and the VR player.</summary>
    static bool StartClimb(Character c, out string note)
    {
        var vr = FirstRemote();
        if (vr == null)
        {
            note = "no remote player";
            return false;
        }
        var to = vr.Center - c.Center;
        to.y = 0f;
        if (
            to.sqrMagnitude < 1e-4f
            || !Physics.Raycast(
                c.Center,
                to.normalized,
                out var hit,
                1.2f,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            )
        )
        {
            note = "no face toward the remote player";
            return false;
        }
        c.refs.view.RPC("StartClimbRpc", RpcTarget.All, hit.point, hit.normal);
        note = "climbing";
        return true;
    }

    /// <summary>
    /// A backpack on B's back the way a player gets one: the master spawns it on the ground with
    /// <c>PhotonNetwork.InstantiateItemRoom</c>, as the game's own spawners do (BerryBush.cs:31), and B wears it with the
    /// backpack wheel's own call (<c>Backpack.Wear</c>, BackpackWheel.cs:133): <c>Item.Interact</c> (Item.cs:578) sends
    /// <c>RequestPickup</c> to the master, whose <c>Player.AddItem</c> fills the backpack slot and syncs it
    /// (Player.cs:91-115). The wear waits out the pickup guard after an equip (Item.cs:580).
    /// </summary>
    static bool WearPack(Character c, out string note)
    {
        if (c.player == null)
        {
            note = "no player";
            return false;
        }
        if (!c.player.backpackSlot.IsEmpty())
        {
            note = "already wears a pack";
            return true;
        }
        // Player.AddItem refuses off the master (Player.cs:98); the observer hosts.
        if (!PhotonNetwork.IsMasterClient)
        {
            note = "not the master client";
            return false;
        }
        Backpack prefab = null;
        foreach (var item in SingletonAsset<ItemDatabase>.Instance.Objects)
        {
            if (item is Backpack b && b.backpackType == BackpackSlot.BackpackType.Backpack)
            {
                prefab = b;
                break;
            }
        }
        if (prefab == null)
        {
            note = "no backpack in the item database";
            return false;
        }
        var go = PhotonNetwork.InstantiateItemRoom(
            prefab.gameObject.name,
            c.Center + c.data.lookDirection_Flat * 0.6f,
            Quaternion.identity
        );
        var pack = go != null ? go.GetComponent<Backpack>() : null;
        if (pack == null)
        {
            note = "spawning " + prefab.name + " failed";
            return false;
        }
        c.StartCoroutine(WearSoon(pack, c));
        note = "spawned " + prefab.name + ", wearing it";
        return true;
    }

    /// <summary>
    /// The named item on the ground about 0.7 m in front of the VR player along the request's direction, spawned by the
    /// master with <c>PhotonNetwork.InstantiateItemRoom</c> as the game's spawners do (BerryBush.cs:31), dropped from
    /// 0.3 m over the ground there so it settles where it lands (#103: the steps that need food or a small item get one
    /// at hand instead of walking off to fetch one).
    /// </summary>
    static bool SpawnItem(string line, out string note)
    {
        var prefabName = MpRequest.ReadItem(line);
        var vr = FirstRemote();
        if (vr == null || string.IsNullOrEmpty(prefabName))
        {
            note = vr == null ? "no remote player" : "no item named";
            return false;
        }
        if (!PhotonNetwork.IsMasterClient)
        {
            note = "not the master client";
            return false;
        }
        Item prefab = null;
        foreach (var item in SingletonAsset<ItemDatabase>.Instance.Objects)
        {
            if (item != null && item.gameObject.name == prefabName)
            {
                prefab = item;
                break;
            }
        }
        if (prefab == null)
        {
            note = "no " + prefabName + " in the item database";
            return false;
        }
        var dir = MpRequest.TryReadDirection(line, out double dx, out double dz)
            ? new Vector3((float)dx, 0f, (float)dz)
            : vr.data.lookDirection_Flat;
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f)
            dir = Vector3.forward;
        var at = vr.Center + dir.normalized * 0.7f;
        if (
            Physics.Raycast(
                at + Vector3.up,
                Vector3.down,
                out var hit,
                4f,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            )
        )
            at = hit.point + Vector3.up * 0.3f;
        var go = PhotonNetwork.InstantiateItemRoom(prefab.gameObject.name, at, Quaternion.identity);
        if (go == null)
        {
            note = "spawning " + prefabName + " failed";
            return false;
        }
        note = "spawned " + prefabName + " in front of " + vr.characterName;
        return true;
    }

    static IEnumerator WearSoon(Backpack pack, Character c)
    {
        yield return new WaitForSeconds(0.3f);
        if (pack != null && c != null)
            pack.Wear(c);
    }

    static Character FirstRemote()
    {
        foreach (var c in Character.AllCharacters)
        {
            if (c != null && !c.IsLocal && !c.isBot)
                return c;
        }
        return null;
    }

    int Count(byte code) => eventCounts.TryGetValue(code, out int n) ? n : 0;

    int TotalEvents()
    {
        int total = 0;
        foreach (var n in eventCounts.Values)
            total += n;
        return total;
    }

    void Note(string text)
    {
        ObserverPlugin.Log.LogInfo(text);
        Write(new JsonLine().Str("type", "note").Num("t", Unix).Str("text", text));
    }

    void Write(JsonLine line)
    {
        try
        {
            File.AppendAllText(outPath, line + "\n");
        }
        catch (IOException e)
        {
            ObserverPlugin.Log.LogWarning("write failed: " + e.Message);
        }
    }

    static string ReadShared(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete
        );
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    static string V(Vector3 v) => "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "]";

    static string F(float f) =>
        float.IsNaN(f) || float.IsInfinity(f)
            ? "null"
            : f.ToString("0.###", CultureInfo.InvariantCulture);

    static string FirstLine(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        int nl = text.IndexOf('\n', StringComparison.Ordinal);
        return (nl >= 0 ? text.Substring(0, nl) : text).Trim();
    }

    static string SafeName(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (char ch in name)
            sb.Append(char.IsLetterOrDigit(ch) || ch == '-' || ch == '.' ? ch : '_');
        return sb.ToString();
    }
}
