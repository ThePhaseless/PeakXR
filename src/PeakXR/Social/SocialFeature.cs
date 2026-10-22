using PeakXR.Framework;
using PeakXR.Interact;

namespace PeakXR.Social;

/// <summary>
/// <c>interact.social</c>: player-to-player interactions by hand. Grip a friend to help them up, carry or drop
/// them, open their pack from behind or pluck thorns and arrows (yours too); hold food at their mouth to feed
/// them; shake free of webs; grasp haptics; and a guard so a grip cannot speed up giving up while passed out.
/// Targets come from hand proximity, actions from the game's own interact, reach and use-on-friend paths.
/// </summary>
public sealed class SocialFeature : Feature
{
    public override string Id => "interact.social";

    // After interact.physical (10), before inventory.vr (20): a grip on a friend wins over a nearby body slot.
    public override int Order => 15;

    protected internal override void Install()
    {
        SocialConfig.Bind(Plugin.Settings);
        Patch(typeof(SocialPatches.SampleFirst));
        Patch(typeof(SocialPatches.SampleTick));
        Patch(typeof(SocialPatches.SampleLast));
        Patch(typeof(SocialPatches.Raycasts));
        Patch(typeof(SocialPatches.HandPick));
        Patch(typeof(SocialPatches.PackPick));
        Patch(typeof(SocialPatches.CarryDropsPack));
        Patch(typeof(SocialPatches.FeedAngle));
        Patch(typeof(SocialPatches.Grasp));
        GripHooks.Register(SocialHands.Instance);
    }
}
