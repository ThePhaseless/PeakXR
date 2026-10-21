using BepInEx.Configuration;
using PeakXR.Framework;

namespace PeakXR.UI.Affordances;

/// <summary>
/// ui.affordance: in VR the hands make actions look possible that the game refuses or ignores (reach measured from
/// the head, a wall too steep, no stamina, a two-handed item in one hand). On a flat screen the crosshair and prompt
/// teach those rules; in VR the action would just fail silently. Features report each refusal through
/// <see cref="Affordance"/>, which answers with a soft "denied" buzz, a quiet spatial sound, a ring with a shape at
/// the spot and, where it knows, a marked spot that would work (text only with <c>UI.ActionHintText</c>). No patches: callers detect the refusal where they already make the attempt.
/// </summary>
public sealed class AffordanceFeature : Feature
{
    public override string Id => "ui.affordance";

    // After ui.spatial, whose label sizing it shares.
    public override int Order => 58;

    internal static ConfigEntry<bool> ActionHints { get; private set; }

    internal static ConfigEntry<bool> ReachRay { get; private set; }

    internal static ConfigEntry<bool> ActionHintText { get; private set; }

    protected internal override void Install()
    {
        ActionHints = Plugin.Settings.Bind(
            "UI",
            "ActionHints",
            true,
            "When something you try does nothing (out of reach, wall too steep, no stamina, needs both hands...), "
                + "the controller gives a soft triple tick, a quiet falling note plays there and a ring marks it "
                + "(slash: no, bar: full, sweeping arc: wait). Where it would work gets a double ring, a rising note "
                + "and a chevron moving toward it. Uses the Interaction haptic strength and the game's sound volume."
        );
        ReachRay = Plugin.Settings.Bind(
            "UI",
            "ReachRay",
            true,
            "While a hand points to interact, a faint ray shows how far the game's interaction reaches from that "
                + "hand, and something just past it gets a 'Too far' ring. Needs ActionHints."
        );
        ActionHintText = Plugin.Settings.Bind(
            "UI",
            "ActionHintText",
            false,
            "Also show a short text reason and next step by the hand or target. The rings, shapes, sounds and "
                + "haptics already say what is wrong and what would work; this adds words. Needs ActionHints."
        );
        DenyTint.ItemBlock = GameMember.Field<Item, UnityEngine.MaterialPropertyBlock>("mpb");
        AffordanceRuntime.Create();
    }
}
