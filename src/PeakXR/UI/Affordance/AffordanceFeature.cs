using BepInEx.Configuration;
using PeakXR.Framework;

namespace PeakXR.UI.Affordances;

/// <summary>
/// ui.affordance: in VR the hands make actions look possible that the game refuses or ignores (reach measured from
/// the head, a wall too steep, no stamina, a two-handed item in one hand). On a flat screen the crosshair and prompt
/// teach those rules; in VR the action would just fail silently. Features report each refusal through
/// <see cref="Affordance"/>, which answers with a soft "denied" buzz, a red ring at the spot and a short reason by
/// the hand or target. No patches: callers detect the refusal where they already make the attempt.
/// </summary>
public sealed class AffordanceFeature : Feature
{
    public override string Id => "ui.affordance";

    // After ui.spatial, whose label sizing it shares.
    public override int Order => 58;

    internal static ConfigEntry<bool> ActionHints { get; private set; }

    protected internal override void Install()
    {
        ActionHints = Plugin.Settings.Bind(
            "UI",
            "ActionHints",
            true,
            "When something you try does nothing (out of reach, wall too steep, no stamina, needs both hands...), "
                + "the controller gives a soft triple tick and a short reason appears by your hand or the target. "
                + "Aimed tools and a pointing hand show a red end when the game would refuse. Uses the Interaction "
                + "haptic strength."
        );
        AffordanceRuntime.Create();
    }
}
