using BepInEx.Configuration;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.UI.Common;
using UnityEngine;

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

    internal static ConfigEntry<bool> FirstTimeRefusalLabels { get; private set; }

    /// <summary>One bit per <see cref="Core.DenyReason"/> already labelled (<see cref="Core.FirstRefusalLabel"/>).</summary>
    internal static ConfigEntry<ulong> SeenRefusals { get; private set; }

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
        FirstTimeRefusalLabels = Plugin.Settings.Bind(
            "UI",
            "FirstTimeRefusalLabels",
            true,
            "The first time each kind of refusal happens, its short reason shows as text next to the ring, even "
                + "with ActionHintText off. After that only the ring's shape shows. The button below starts over."
        );
        SeenRefusals = Plugin.Settings.Bind(
            "UI",
            "SeenRefusals",
            0ul,
            new ConfigDescription(
                "Internal: which refusal reasons have shown their first-time label (one bit each).",
                null,
                Settings.VRTab.HiddenTag
            )
        );
        Settings.VRTab.AddButtonAfter(
            FirstTimeRefusalLabels,
            "PEAKXR_RESET_REFUSAL_LABELS",
            "Refusal labels seen",
            "Reset",
            ResetSeen
        );
        DenyTint.ItemBlock = GameMember.Field<Item, UnityEngine.MaterialPropertyBlock>("mpb");
        AffordanceRuntime.Create();
    }

    /// <summary>The reset button: every refusal shows its label once more. A notice at the button says it worked.</summary>
    private static void ResetSeen(Vector3 at, bool left)
    {
        SeenRefusals.Value = 0ul;
        Plugin.Log.LogInfo("[ui.affordance] first-time refusal labels reset");
        if (!Affordance.Inform(left, at, DenyReason.HappenedHere, DenyHint.At(at), "Labels reset"))
            UIHaptics.Tick(left, 0.25f, 0.04f);
    }
}
