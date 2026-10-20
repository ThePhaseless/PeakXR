using PeakXR.Framework;

namespace PeakXR.Interact.Tools;

/// <summary>
/// <c>interact.tools</c>: hand gestures for tools the game drives with scroll or a hold timer. A wrist twist or a
/// free-hand pull feeds the game's scroll input (rope spool length, scout cannon angle), strikes at the preview
/// replace the climbing spike's hold timer, and the arms steer the glider. Everything goes through the game's own
/// input fields and physics, so vanilla code and RPCs do the rest.
/// </summary>
public sealed class ToolsFeature : Feature
{
    public override string Id => "interact.tools";

    // After interact.physical (10): reads LocalHold and adds to the input it injected.
    public override int Order => 25;

    protected internal override void Install()
    {
        ToolsConfig.Bind(Plugin.Settings);
        Patch(typeof(ToolPatches.Sample));
        Patch(typeof(ToolPatches.SpikeUse));
        Patch(typeof(ToolPatches.Glide));
    }
}
