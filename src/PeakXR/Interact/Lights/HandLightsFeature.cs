using PeakXR.Framework;

namespace PeakXR.Interact.Lights;

/// <summary>
/// <c>interact.lights</c>: hand-held flare, candle, lantern and torch in VR. Strike a flare or a candle's wick with
/// the free hand to light it through the item's own primary use (the trigger still works), get a haptic pulse when
/// the held light comes on, and keep the visible VR arm from shadowing the scene from the light it holds.
/// The lights themselves already follow the tracked hand because they are carried by the item.
/// </summary>
public sealed class HandLightsFeature : Feature
{
    public override string Id => "interact.lights";

    // After interact.physical (order 10): its trigger use is injected first.
    public override int Order => 30;

    protected internal override void Install()
    {
        LightsConfig.Bind(Plugin.Settings);
        Patch(typeof(LightsPatches.Sample));
    }
}
