using UnityEngine;

namespace PeakXR.Interact;

/// <summary>
/// Tells the VR player why a gesture did nothing when PeakXR holds it to what a vanilla player could do (reach,
/// feed angle, piton time). A clamp must never fail silently. This is the single hook for those refusals: for now a
/// distinct "denied" haptic (low and long, unlike the short strong success ticks) and a debug log line; the shared
/// affordance service (src/PeakXR/UI/Affordance/) takes it over for the red outline and the reason label.
/// Callers may call it every frame while the refusal holds: it fires at most once per <see cref="Repeat"/> per reason.
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

    private static float tooFarAt = float.NegativeInfinity;
    private static float wrongAngleAt = float.NegativeInfinity;
    private static float keepHoldingAt = float.NegativeInfinity;

    /// <summary>A refusal at <paramref name="worldPos"/> for the hand that attempted it.</summary>
    public static void Denied(bool left, Vector3 worldPos, string reason)
    {
        ref float last = ref Slot(reason);
        float now = Time.unscaledTime;
        if (now - last < Repeat)
            return;
        last = now;
        Haptics.Pulse(left, 0.3f, 0.18f);
        Plugin.Log.LogDebug(
            $"[parity] denied {reason} ({(left ? "left" : "right")} hand) at {worldPos}"
        );
    }

    private static ref float Slot(string reason)
    {
        if (reason == TooFar)
            return ref tooFarAt;
        if (reason == WrongAngle)
            return ref wrongAngleAt;
        return ref keepHoldingAt;
    }
}
