using PeakXR.Framework;

namespace PeakXR.Interact.Ping;

/// <summary>
/// <c>interact.ping</c>: the game's ping, aimed by a hand instead of the flat camera, fired by a finger-gun
/// gesture or a fallback button, with a marker sized for stereo viewing. The ping itself still goes through
/// vanilla PointPinger and its ReceivePoint_Rpc, so flat players see VR pings normally.
/// </summary>
public sealed class PingFeature : Feature
{
    public override string Id => "interact.ping";

    // After interact.physical (order 10): the Sample postfix edits the use input that feature injected.
    public override int Order => 30;

    protected internal override void Install()
    {
        PingConfig.Bind(Plugin.Settings);
        Patch(typeof(PingPatches.Sample));
        Patch(typeof(PingPatches.PingHit));
        Patch(typeof(PingPatches.MarkerScale));
    }
}
