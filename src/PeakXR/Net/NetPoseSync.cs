using System;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.VR;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;
using NQuaternion = System.Numerics.Quaternion;
using NVector3 = System.Numerics.Vector3;
using PhotonPlayer = Photon.Realtime.Player;

namespace PeakXR.Net;

/// <summary>
/// Runs the <c>net.pose</c> handshake, send and receive loops. Lives on its own DontDestroyOnLoad
/// object so it survives scene changes; all per-room state is dropped when the room is left.
/// </summary>
/// <remarks>
/// Event code: <see cref="PoseEventCode"/> = 151, chosen from the user range 100-199 after checking
/// everything that raises or listens for events in this build:
/// the game's <c>CustomCommandType</c> (1-4, 18; CustomCommandType.cs:1-9) sent through
/// Zorro's <c>CustomCommands</c> (CustomCommands.cs:26) and <c>PhotonShim.Kick</c> (18, PhotonShim.cs:157);
/// Zorro's <c>CustomCommandListener.OnEvent</c> (CustomCommandListener.cs:52) ignores unregistered codes
/// at or above 6 silently; <c>PlayerHandler.OnNetworkEvent</c> (PlayerHandler.cs:161) only reads the code;
/// PUN internals use 200-212 (<c>PunEvent</c>), Photon Realtime 210-255 (<c>EventCode</c>), Photon Voice
/// 202/203 on its own client; PUN's TurnManager (1-2) is unused. 198/199 are avoided because the
/// unrelated PeakVR mod uses them, so the two mods never feed each other's decoders.
///
/// Packet size and bandwidth: the payload is a <see cref="PoseCodec"/> blob, 44 bytes with head, both
/// hands and fingers (4 header + 3 x 10 pose + 10 fingers). Photon adds about 30 bytes of event and
/// command framing (byte[] type tag and length, event code, target list, reliable/unreliable command
/// header); UDP/IP headers are shared with the vanilla 30 Hz CharacterSyncer traffic batched into the
/// same datagrams. At the 25 Hz default that is about 25 x 75 B = 1.9 KB/s (15 kbit/s) upload per VR
/// player, sent once to the server, and the same per VR sender on each modded receiver's download.
/// Unchanged poses are skipped (keepalive every 0.25 s), so a still player costs about 4 packets/s.
/// Photon message count: each event costs 1 + receivers messages; four VR players at 25 Hz add at most
/// 4 x 25 x 4 = 400 msg/s to the room, on top of vanilla's ~480 msg/s, which is why the rate tops out
/// at 30 Hz and unchanged poses are not sent.
/// </remarks>
internal sealed class NetPoseSync
    : MonoBehaviour,
        IOnEventCallback,
        IInRoomCallbacks,
        IMatchmakingCallbacks
{
    public const byte PoseEventCode = 151;

    /// <summary>Upper bound on tracked remote streams; actor ids come from the server, this only guards memory.</summary>
    const int MaxRemotes = 32;

    sealed class Remote
    {
        public readonly RemotePoseStream Stream = new();
        public Character Character;
        public AvatarPoseDriver Driver;
    }

    readonly PeerTable peers = new();
    readonly Dictionary<int, Remote> remotes = new();
    readonly PoseSendGate gate = new();

    // One reused buffer per possible encoded size (4 + 10k bytes, k = 0..4): Photon sends a byte[]
    // whole, so a single MaxSize buffer would pad short packets.
    readonly byte[][] sendBuffers = new byte[5][];
    readonly byte[] encodeScratch = new byte[PoseCodec.MaxSize];
    readonly byte[] receiveScratch = new byte[PoseCodec.MaxSize];
    readonly RaiseEventOptions sendOptions = new() { CachingOption = EventCaching.DoNotCache };

    bool registered;
    int publishedValue = -1;
    ushort sequence;
    Character localCharacter;
    AvatarPoseDriver localDriver;
    int loggedErrors;

    void Start()
    {
        PhotonNetwork.AddCallbackTarget(this);
        registered = true;
        VRSession.Started += PublishHandshake;
        VRSession.Stopped += PublishHandshake;
        if (PhotonNetwork.InRoom)
            OnJoinedRoom();
    }

    void OnDestroy()
    {
        if (registered)
            PhotonNetwork.RemoveCallbackTarget(this);
        VRSession.Started -= PublishHandshake;
        VRSession.Stopped -= PublishHandshake;
    }

    void Update()
    {
        if (!PhotonNetwork.InRoom)
            return;
        double now = Time.realtimeSinceStartupAsDouble;
        try
        {
            SendLocalPose(now);
            ApplyRemotePoses(now);
        }
        catch (Exception e)
        {
            // Never let a pose glitch break the frame; log a few and stay quiet after that.
            if (loggedErrors++ < 5)
                Plugin.Log.LogError($"[net.pose] {e}");
        }
    }

    // ---------------------------------------------------------------- handshake

    void PublishHandshake()
    {
        var local = PhotonNetwork.LocalPlayer;
        if (local == null)
            return;
        int value = Handshake.Encode(VRSession.Active);
        if (value == publishedValue)
            return;
        publishedValue = value;
        local.SetCustomProperties(new Hashtable { [Handshake.Key] = value });
    }

    void RebuildPeers()
    {
        peers.Clear();
        remotes.Clear();
        foreach (var player in PhotonNetwork.PlayerListOthers)
            peers.Set(player.ActorNumber, PropertyOf(player.CustomProperties));
    }

    static object PropertyOf(Hashtable props) =>
        props != null && props.TryGetValue(Handshake.Key, out var value) ? value : null;

    public void OnJoinedRoom()
    {
        publishedValue = -1; // the new room has not seen it yet
        PublishHandshake();
        RebuildPeers();
        gate.Reset();
    }

    public void OnLeftRoom()
    {
        peers.Clear();
        remotes.Clear();
        localCharacter = null;
        localDriver = null;
    }

    public void OnPlayerEnteredRoom(PhotonPlayer newPlayer)
    {
        if (peers.Set(newPlayer.ActorNumber, PropertyOf(newPlayer.CustomProperties)))
            gate.Reset();
    }

    public void OnPlayerLeftRoom(PhotonPlayer otherPlayer)
    {
        peers.Remove(otherPlayer.ActorNumber);
        remotes.Remove(otherPlayer.ActorNumber);
    }

    public void OnPlayerPropertiesUpdate(PhotonPlayer targetPlayer, Hashtable changedProps)
    {
        if (
            targetPlayer == null
            || targetPlayer.IsLocal
            || changedProps == null
            || !changedProps.ContainsKey(Handshake.Key)
        )
            return;
        int actor = targetPlayer.ActorNumber;
        bool wasPeer = peers.IsPeer(actor);
        if (!peers.Set(actor, changedProps[Handshake.Key]))
            return;
        if (!peers.IsVr(actor))
            remotes.Remove(actor); // left VR: stop applying, the driver falls back to vanilla animation
        if (!wasPeer && peers.IsPeer(actor))
            gate.Reset();
    }

    public void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged) { }

    public void OnMasterClientSwitched(PhotonPlayer newMasterClient) { }

    public void OnFriendListUpdate(List<FriendInfo> friendList) { }

    public void OnCreatedRoom() { }

    public void OnCreateRoomFailed(short returnCode, string message) { }

    public void OnJoinRoomFailed(short returnCode, string message) { }

    public void OnJoinRandomFailed(short returnCode, string message) { }

    // ---------------------------------------------------------------- send

    void SendLocalPose(double now)
    {
        if (!VRSession.Active || !NetPoseFeature.SendPose.Value || peers.Count == 0)
            return;

        var character = Character.localCharacter;
        if (character == null)
            return;
        if (character != localCharacter)
        {
            localCharacter = character;
            character.TryGetComponent(out localDriver);
        }
        if (localDriver == null)
        {
            // The avatar feature may attach the driver after the character spawned.
            character.TryGetComponent(out localDriver);
            if (localDriver == null)
                return;
        }
        if (
            !localDriver.HasFreshPose
            || IsRagdolled(character)
            || !TryGetFrame(character, out var root, out float yaw)
        )
            return;

        var world = FromAvatarPose(localDriver.Pose);
        var local = PoseFrame.ToLocal(world, root, yaw);

        gate.Rate = NetPoseFeature.SendRate.Value;
        if (!gate.ShouldSend(now, local))
            return;

        local.Sequence = ++sequence;
        int length = PoseCodec.Encode(local, encodeScratch);
        if (length == 0)
            return;
        var payload = sendBuffers[(length - 4) / 10] ??= new byte[length];
        Buffer.BlockCopy(encodeScratch, 0, payload, 0, length);

        sendOptions.TargetActors = peers.Targets;
        PhotonNetwork.RaiseEvent(PoseEventCode, payload, sendOptions, SendOptions.SendUnreliable);
        gate.MarkSent(now, local);
    }

    static PosePacket FromAvatarPose(in AvatarPose pose)
    {
        var p = new PosePacket { Flags = PoseFlags.VrActive };
        if (pose.HasHead)
        {
            p.Flags |= PoseFlags.Head;
            p.HeadPosition = N(pose.HeadPosition);
            p.HeadRotation = N(pose.HeadRotation);
        }
        if (pose.HasLeftHand)
        {
            p.Flags |= PoseFlags.LeftHand;
            p.LeftHandPosition = N(pose.LeftHandPosition);
            p.LeftHandRotation = N(pose.LeftHandRotation);
        }
        if (pose.HasRightHand)
        {
            p.Flags |= PoseFlags.RightHand;
            p.RightHandPosition = N(pose.RightHandPosition);
            p.RightHandRotation = N(pose.RightHandRotation);
        }
        if (pose.HasLeftHand || pose.HasRightHand)
        {
            p.Flags |= PoseFlags.Fingers;
            p.LeftFingers = pose.LeftFingers;
            p.RightFingers = pose.RightFingers;
        }
        if (pose.Seated)
            p.Flags |= PoseFlags.Seated;
        // Hold flags: which hand carries the item and whether the other hand supports it. Pure flag bits, so peers
        // on an older build decode the same packet and ignore them (see PoseCodec remarks).
        if (pose.ItemInLeftHand)
            p.Flags |= PoseFlags.ItemInLeftHand;
        if (pose.TwoHandHold)
            p.Flags |= PoseFlags.TwoHandHold;
        return p;
    }

    // ---------------------------------------------------------------- receive

    public void OnEvent(EventData photonEvent)
    {
        if (photonEvent.Code != PoseEventCode)
            return;
        try
        {
            Receive(photonEvent);
        }
        catch (Exception e)
        {
            if (loggedErrors++ < 5)
                Plugin.Log.LogWarning($"[net.pose] dropped a bad pose packet: {e.Message}");
        }
    }

    void Receive(EventData photonEvent)
    {
        int sender = photonEvent.Sender;
        var localPlayer = PhotonNetwork.LocalPlayer;
        // Only handshake peers in VR: a player who just left VR must not keep driving its avatar.
        if (
            sender <= 0
            || localPlayer == null
            || sender == localPlayer.ActorNumber
            || !peers.IsVr(sender)
        )
            return;

        byte[] data;
        int length;
        switch (photonEvent.CustomData)
        {
            case byte[] bytes:
                data = bytes;
                length = bytes.Length;
                break;
            case ByteArraySlice slice:
                bool fits =
                    slice.Buffer != null
                    && slice.Count <= receiveScratch.Length
                    && slice.Offset >= 0
                    && slice.Offset + slice.Count <= slice.Buffer.Length;
                if (fits)
                    Buffer.BlockCopy(slice.Buffer, slice.Offset, receiveScratch, 0, slice.Count);
                length = slice.Count;
                // A pooled slice (UseByteArraySlicePoolForEvents) goes back to the pool once copied.
                slice.Release();
                if (!fits)
                    return;
                data = receiveScratch;
                break;
            default:
                return;
        }

        if (!remotes.TryGetValue(sender, out var remote))
        {
            if (remotes.Count >= MaxRemotes || PhotonNetwork.CurrentRoom?.GetPlayer(sender) == null)
                return;
            remote = new Remote();
            remotes.Add(sender, remote);
        }
        remote.Stream.Accept(data, length, Time.realtimeSinceStartupAsDouble);
    }

    void ApplyRemotePoses(double now)
    {
        if (!NetPoseFeature.ShowRemotePoses.Value)
            return;
        double delay = NetPoseFeature.InterpolationDelayMs.Value * 0.001;

        foreach (var entry in remotes)
        {
            var remote = entry.Value;
            // Silence: stop feeding the driver so it hands the arms back to vanilla animation.
            if (now - remote.Stream.LastAcceptTime > AvatarPoseDriver.StaleAfter)
                continue;

            var character = Resolve(entry.Key, ref remote.Character);
            if (
                character == null
                || IsRagdolled(character)
                || !TryGetFrame(character, out var root, out float yaw)
            )
                continue;

            remote.Stream.Buffer.Delay = delay;
            if (!remote.Stream.Buffer.Sample(now, out var local))
                continue;

            if (remote.Driver == null || remote.Driver.gameObject != character.gameObject)
                remote.Driver = AvatarPoseDriver.For(character);
            var world = PoseFrame.ToWorld(local, root, yaw);
            remote.Driver.SetPose(ToAvatarPose(world));
        }
    }

    static AvatarPose ToAvatarPose(in PosePacket p) =>
        new()
        {
            HasHead = p.HasHead,
            HeadPosition = U(p.HeadPosition),
            HeadRotation = U(p.HeadRotation),
            HasLeftHand = p.HasLeftHand,
            LeftHandPosition = U(p.LeftHandPosition),
            LeftHandRotation = U(p.LeftHandRotation),
            LeftFingers = p.LeftFingers,
            HasRightHand = p.HasRightHand,
            RightHandPosition = U(p.RightHandPosition),
            RightHandRotation = U(p.RightHandRotation),
            RightFingers = p.RightFingers,
            Seated = p.Seated,
            ItemInLeftHand = p.ItemInLeftHand,
            TwoHandHold = p.TwoHandHold,
        };

    /// <summary>
    /// The player character owned by <paramref name="actor"/>. Respawning registers a new Character and
    /// deactivates the old one (PlayerHandler.RegisterCharacter, PlayerHandler.cs:167), and scene loads
    /// destroy it, so the cache is checked before use and rebuilt from Character.AllCharacters on a miss.
    /// </summary>
    static Character Resolve(int actor, ref Character cached)
    {
        if (
            cached != null
            && cached.gameObject.activeInHierarchy
            && cached.photonView.OwnerActorNr == actor
        )
            return cached;
        cached = null;
        var all = Character.AllCharacters;
        for (int i = 0; i < all.Count; i++)
        {
            var c = all[i];
            if (
                c != null
                && c.IsPlayerControlled
                && c.gameObject.activeInHierarchy
                && c.photonView.OwnerActorNr == actor
            )
            {
                cached = c;
                break;
            }
        }
        return cached;
    }

    /// <summary>
    /// Dead, passed out, falling or carried characters are ragdolls (CharacterData.GetTargetRagdollControll,
    /// CharacterData.cs:485); a tracked pose would fight the physics, so it is not applied or sent.
    /// </summary>
    static bool IsRagdolled(Character c)
    {
        var d = c.data;
        return d == null
            || d.dead
            || d.passedOut
            || d.fullyPassedOut
            || d.fallSeconds > 0f
            || d.isCarried;
    }

    /// <summary>The wire frame (see <see cref="PoseFrame"/>): physical hip position and look yaw.</summary>
    static bool TryGetFrame(Character c, out NVector3 root, out float yawDeg)
    {
        var hip = c.refs?.hip;
        var rig = hip != null ? hip.Rig : null;
        if (rig == null)
        {
            root = default;
            yawDeg = 0f;
            return false;
        }
        root = N(rig.position);
        yawDeg = c.data.lookValues.x;
        return true;
    }

    static NVector3 N(Vector3 v) => new(v.x, v.y, v.z);

    static NQuaternion N(Quaternion q) => new(q.x, q.y, q.z, q.w);

    static Vector3 U(NVector3 v) => new(v.X, v.Y, v.Z);

    static Quaternion U(NQuaternion q) => new(q.X, q.Y, q.Z, q.W);
}
