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
            // Before WP1 this setting switched off every refusal cue; now refusals always cue and it covers only the
            // "would work" hints. The key and the player's saved value are kept as they are (no flip): someone who
            // turned it off still gets fewer marks, and never a silent failure.
            "Show where an action would work: after a refusal, a double ring, a rising note and a chevron moving "
                + "toward the spot that works, and the reach ray (ReachRay). Refusals themselves always cue, on or "
                + "off: a soft triple tick, a quiet falling note and a ring with a shape (slash: no, bar: full, "
                + "sweeping arc: wait, bolt: stamina...), with the refused thing turned red."
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
                + "haptics already say what is wrong and what would work; this adds words."
        );
        DenyTint.ItemBlock = GameMember.Field<Item, UnityEngine.MaterialPropertyBlock>("mpb");
        AffordanceRuntime.Create();
    }
}
