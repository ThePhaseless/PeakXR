using System;

namespace PeakXR.Core;

/// <summary>
/// Which hand holds the item is set per item: the primary hand is the one whose grip started the hold, the
/// secondary the other one, whichever side each is. The game only knows a right-hand hold (its <c>Hand_R</c> grip,
/// <c>Hand_L</c> for the other hand); a left-primary hold is its mirror image about one of the item's own planes,
/// kept on the item (<see cref="LeftHold"/>).
/// </summary>
public static class HoldHands
{
    /// <summary>
    /// The primary hand opens: the item passes to the other hand, which becomes the primary, when that hand still
    /// grips it as the second hand of a two-hand hold. Otherwise the release drops, throws or keeps it as usual.
    /// </summary>
    public static bool HandOff(bool twoHand, bool otherGripClosed, bool otherIsSecondHand) =>
        twoHand && otherGripClosed && otherIsSecondHand;

    /// <summary>Seconds a handoff eases the item from where it was onto the new primary's grip.</summary>
    public const float EaseSeconds = 0.25f;

    /// <summary>Weight of the new hold, 0 at the handoff and 1 once it has settled (smoothstep).</summary>
    public static float Ease(float since, float seconds)
    {
        if (since < 0f || seconds <= 0f || since >= seconds)
            return 1f;
        float t = since / seconds;
        return t * t * (3f - 2f * t);
    }

    /// <summary>Metres a release frame may move past what the ease itself could move in it: hand and weld jitter.</summary>
    public const float ReleaseJumpMargin = 0.015f;

    /// <summary>
    /// An item let go into its one-hand grip jumped in a frame of <paramref name="dt"/> seconds: its
    /// <paramref name="step"/> is more than the <see cref="EaseSeconds"/> smoothstep could carry of the release's
    /// <paramref name="net"/> start-to-end distance in that time, plus <see cref="ReleaseJumpMargin"/>. The bound is the
    /// ease's fastest rate (1.5 x net / EaseSeconds, mid-ease), as a frame's place in the ease is not known to the frame;
    /// it scales with the frame, so a long frame (a hitch) carries more without reading as a jump (vr.14b, #15).
    /// </summary>
    public static bool ReleaseJumped(float step, float dt, float net) =>
        step > net * MathF.Min(1f, 1.5f * MathF.Max(dt, 0f) / EaseSeconds) + ReleaseJumpMargin;
}
