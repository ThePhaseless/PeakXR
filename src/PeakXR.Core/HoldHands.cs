using System.Numerics;

namespace PeakXR.Core
{
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
    }
}
