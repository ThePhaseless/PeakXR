using PeakXR.Core;
using PeakXR.Social;
using PeakXR.UI.Affordances;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Interact;

/// <summary>
/// Tells the VR player why a gesture did nothing when PeakXR holds it to what a vanilla player could do (reach,
/// feed angle, piton time). A clamp must never fail silently. Forwards to the shared affordance service
/// (<see cref="Affordance"/>: denied haptic, red ring, reason label) with what would work instead: the end of the
/// real reach, or where to stand to feed. With UI.ActionHints off or ui.affordance not installed it falls back to a bare haptic, so a clamp never fails silently.
/// Callers may call it every frame while the refusal holds; the service rate-limits it.
/// </summary>
internal static class ParityFeedback
{
    /// <summary>The interaction point is farther from the head than vanilla's 2 m. Label: "Too far".</summary>
    public const string TooFar = "too_far";

    /// <summary>The friend is outside the item's vanilla feed angle. Label: "Wrong angle".</summary>
    public const string WrongAngle = "wrong_angle";

    /// <summary>Every strike is in, but the vanilla hold time has not run yet. Label: "Keep holding".</summary>
    public const string KeepHolding = "keep_holding";

    private const float Repeat = 0.75f;
    private static float fallbackAt = float.NegativeInfinity;

    /// <summary>A refusal at <paramref name="worldPos"/> for the hand that attempted it.</summary>
    public static void Denied(bool left, Vector3 worldPos, string reason)
    {
        if (!Affordance.Enabled)
        {
            float now = Time.unscaledTime;
            if (now - fallbackAt < Repeat)
                return;
            fallbackAt = now;
            Haptics.Pulse(left, 0.3f, 0.18f);
            Plugin.Log.LogDebug($"[parity] denied {reason} at {worldPos}");
            return;
        }
        if (reason == TooFar)
            Affordance.Deny(left, worldPos, DenyReason.OutOfReach, ReachHint(left, worldPos));
        else if (reason == WrongAngle)
            Affordance.Deny(left, worldPos, DenyReason.WrongAngle, FeedHint());
        else
            Affordance.Deny(left, worldPos, DenyReason.KeepHolding);
    }

    /// <summary>The hand ray's real reach (InteractPatches.Raycasts: <c>Interaction.distance</c> minus the hand's
    /// offset from the camera) ends here on the way to the target.</summary>
    private static DenyHint ReachHint(bool left, Vector3 target)
    {
        var rig = VRRig.Instance;
        var aim = rig != null ? rig.Aim(left) : null;
        var cam = MainCamera.instance;
        var interaction = Interaction.instance;
        if (aim == null || cam == null || interaction == null)
            return default;
        float reach = Mathf.Max(
            0f,
            interaction.distance - Vector3.Distance(cam.transform.position, aim.position)
        );
        return DenyHint.Range(aim.position, target, reach);
    }

    /// <summary>
    /// Where to stand to feed: vanilla compares the friend's flat look with the reverse of yours
    /// (CharacterInteractible.cs:164-176) against the item's <c>overrideFeedAngleMinMax</c>. Standing 0.7 m from
    /// them on the line of the window's middle angle, facing them, satisfies it.
    /// </summary>
    private static DenyHint FeedHint()
    {
        var friend = SocialHands.Instance.FeedTarget;
        var c = Character.localCharacter;
        var item = c != null ? c.data.currentItem : null;
        if (friend == null || friend.character == null || item == null)
            return default;
        var look = HelperFunctions.ZeroY(friend.character.data.lookDirection);
        if (look.sqrMagnitude < 1e-4f)
            return default;
        float mid = item.overrideFeedAngle
            ? (item.overrideFeedAngleMinMax.x + item.overrideFeedAngleMinMax.y) * 0.5f
            : 0f;
        var stand =
            friend.character.Center + Quaternion.Euler(0f, mid, 0f) * look.normalized * 0.7f;
        return DenyHint.At(stand);
    }
}
