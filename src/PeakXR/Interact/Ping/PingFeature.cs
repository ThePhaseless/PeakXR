using PeakXR.Framework;

namespace PeakXR.Interact.Ping;

/// <summary>
/// <c>interact.ping</c>: the game's ping, aimed by a hand instead of the flat camera, fired by a button only (the
/// layout's off-hand B/Y tap, whose hold is the emote wheel), with a marker sized for stereo viewing. The ping itself
/// still goes through vanilla PointPinger and its ReceivePoint_Rpc, so flat players see VR pings normally. While the
/// thumb rests on the button a local preview shows where it lands; a press the game would drop gets a refusal cue.
/// </summary>
public sealed class PingFeature : Feature
{
    public override string Id => "interact.ping";

    // After player.locomotion and interact.physical: the Sample postfix reads the ping tap the layout wrote.
    public override int Order => 30;

    protected internal override void Install()
    {
        PingConfig.Bind(Plugin.Settings);
        // PointPinger._timeLastPinged (PointPinger.cs:18): the cooldown cue reads it.
        PingInput.LastPinged = GameMember.Field<PointPinger, float>("_timeLastPinged");
        Patch(typeof(PingPatches.Sample));
        Patch(typeof(PingPatches.PingHit));
        Patch(typeof(PingPatches.MarkerScale));
        PingPreview.Create();
    }
}
