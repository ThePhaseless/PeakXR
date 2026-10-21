using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.UI.Affordances;

/// <summary>
/// What WOULD work, carried with a refusal: a spot where the action succeeds (the far end of the real reach, the
/// item's second grip, a friend's mouth) and how far the player still has to close. Drawn as a soft teal ring with a
/// thin line from the refused spot to it, so the next try goes there; <see cref="Meters"/> also goes into the label.
/// </summary>
internal readonly struct DenyHint
{
    public readonly bool HasSpot;
    public readonly Vector3 Spot;
    public readonly float Meters;

    /// <summary>When set, the spot moves with this transform (a grip on a held item, the wrist status bar).</summary>
    public readonly Transform Follow;

    private DenyHint(bool hasSpot, Vector3 spot, float meters, Transform follow = null)
    {
        HasSpot = hasSpot;
        Spot = spot;
        Meters = meters;
        Follow = follow;
    }

    /// <summary>Where the spot is this frame.</summary>
    public Vector3 SpotNow => Follow != null ? Follow.position : Spot;

    /// <summary>The action works at <paramref name="spot"/>.</summary>
    public static DenyHint At(Vector3 spot) => new(true, spot, 0f);

    /// <summary>The action works at <paramref name="target"/>, which moves; null gives no spot.</summary>
    public static DenyHint On(Transform target) =>
        target != null ? new(true, target.position, 0f, target) : default;

    /// <summary>The stamina bar on the wrist (ui.world <c>HudMode.Wrist</c>), for "rest first" refusals.</summary>
    public static DenyHint Wrist()
    {
        var rig = VRRig.Instance;
        var ui = PeakXR.UI.World.UIWorldFeature.Settings;
        if (rig == null || ui == null || ui.Hud.Value != PeakXR.UI.World.HudMode.Wrist)
            return default;
        return On(rig.Hand(ui.WristLeft));
    }

    /// <summary>A range ends at <paramref name="limit"/>, <paramref name="meters"/> short of the refused spot.</summary>
    public static DenyHint Closer(Vector3 limit, float meters) => new(true, limit, meters);

    /// <summary>The reach from <paramref name="origin"/> ends at <paramref name="max"/> on the way to
    /// <paramref name="target"/>.</summary>
    public static DenyHint Range(Vector3 origin, Vector3 target, float max)
    {
        var limit = DenyRange.Boundary(origin.N(), target.N(), max).U();
        return new(true, limit, Vector3.Distance(origin, target) - max);
    }
}

/// <summary>
/// The one place a VR refusal is reported. <see cref="Deny"/> is for an attempt that just failed (a grip, a trigger
/// press, a release); <see cref="Hover"/> is the pre-emptive "can't" state, called every frame while it holds. Both
/// are cheap no-ops in flat play, with <c>UI.ActionHints</c> off, or if ui.affordance did not install, so callers
/// never need to check first. Allocation-free.
/// </summary>
internal static class Affordance
{
    /// <summary>Colour of a "can't" cue (aim line end, ring, label text). Muted, never a full-saturation flash.</summary>
    public static readonly Color DenyColor = new(1f, 0.42f, 0.36f, 0.85f);

    private static readonly DenyGate Gate = new();

    /// <summary>Accepted denials since start, for the self-test.</summary>
    public static int Count { get; private set; }

    public static DenyReason LastReason { get; private set; }
    public static bool LastLeft { get; private set; }
    public static Vector3 LastPosition { get; private set; }

    /// <summary>ui.affordance installed (its runtime exists), whatever the setting.</summary>
    public static bool Installed => AffordanceFeature.ActionHints != null;

    public static bool Enabled =>
        VRSession.Active
        && AffordanceFeature.ActionHints != null
        && AffordanceFeature.ActionHints.Value;

    /// <summary>
    /// An attempt by the <paramref name="left"/> hand at <paramref name="worldPos"/> did nothing because of
    /// <paramref name="reason"/>. Rate-limited per reason and hand. Returns true when it was shown.
    /// </summary>
    public static bool Deny(bool left, Vector3 worldPos, DenyReason reason) =>
        Deny(left, worldPos, reason, default);

    /// <summary>As above, with what would work instead (<see cref="DenyHint"/>).</summary>
    public static bool Deny(bool left, Vector3 worldPos, DenyReason reason, DenyHint hint)
    {
        if (!Enabled || !Gate.Allow(reason, left, Time.unscaledTime))
            return false;
        Count++;
        LastReason = reason;
        LastLeft = left;
        LastPosition = worldPos;
        Plugin.Log.LogDebug(
            $"[ui.affordance] denied {reason} ({(left ? "left" : "right")} hand) at {worldPos}"
        );
        AffordanceRuntime.Show(left, worldPos, reason, hint);
        return true;
    }

    /// <summary>Deny at the hand itself, for refusals with no spot in the world (two-handed use, a throw).</summary>
    public static bool DenyAtHand(bool left, DenyReason reason, DenyHint hint = default)
    {
        var rig = VRRig.Instance;
        var hand = rig != null ? rig.Hand(left) : null;
        return hand != null && Deny(left, hand.position, reason, hint);
    }

    /// <summary>
    /// This frame the <paramref name="left"/> hand points or aims at <paramref name="worldPos"/>, where the game would
    /// refuse. Shows a red ring there while called; after a steady moment, the reason too. No haptic.
    /// </summary>
    public static void Hover(
        bool left,
        Vector3 worldPos,
        DenyReason reason,
        DenyHint hint = default
    )
    {
        if (Enabled)
            AffordanceRuntime.Hover(left, worldPos, reason, hint);
    }

    /// <summary>Colour of the "this would work" part of a cue (the valid spot, the end of the real range).</summary>
    public static readonly Color HintColor = new(0.45f, 0.9f, 0.82f, 0.75f);

    /// <summary>The self-test starts from a clean rate limit so its denials are not swallowed.</summary>
    public static void ResetGate() => Gate.Reset();
}
