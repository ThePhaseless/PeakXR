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

    /// <summary>Most of a release's whole travel one frame may carry and still be an ease: the
    /// <see cref="EaseSeconds"/> smoothstep carries about a quarter per 40 ms frame, a snap all of it.</summary>
    public const float ReleaseJumpShare = 0.4f;

    /// <summary>An item let go into its one-hand grip jumped: its <paramref name="largestStep"/> in one frame is more than
    /// <paramref name="minJump"/> and more than <see cref="ReleaseJumpShare"/> of its whole <paramref name="travel"/>.
    /// A fixed per-frame limit reads the ease itself as a jump at long frames (vr.14b, #15).</summary>
    public static bool ReleaseJumped(float largestStep, float travel, float minJump) =>
        largestStep > minJump && largestStep > ReleaseJumpShare * travel;
}
