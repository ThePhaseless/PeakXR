namespace PeakXR.Core;

/// <summary>
/// Why the game would ignore a rope, vine, handle, lunge, pull or wall-grab attempt, so a VR hand gets the matching
/// refusal cue instead of nothing. Mirrors the game's own checks; <see cref="DenyReason.None"/> means it would work.
/// </summary>
public static class TraversalRefusal
{
    /// <summary>
    /// <c>RopeSegment.IsInteractible</c> (RopeSegment.cs:49-58): an anchored rope (<c>Rope.isClimbable</c>), a segment
    /// within <c>maxRopeAngle * 0.6</c> of vertical either way, and stamina left. Unanchored wins: resting would not help.
    /// </summary>
    public static DenyReason Rope(
        float segmentAngleFromUp,
        float maxRopeAngle,
        bool anchored,
        bool outOfStamina
    )
    {
        if (!anchored)
            return DenyReason.Unanchored;
        float limit = maxRopeAngle * 0.6f;
        if (!(segmentAngleFromUp < limit || 180f - segmentAngleFromUp < limit))
            return DenyReason.TooFlat;
        return outOfStamina ? DenyReason.NoStamina : DenyReason.None;
    }

    /// <summary><c>JungleVine.IsInteractible</c> (JungleVine.cs:96-108) for a capsule vine: stamina only.</summary>
    public static DenyReason Vine(bool outOfStamina) =>
        outOfStamina ? DenyReason.NoStamina : DenyReason.None;

    /// <summary><c>ClimbHandle.IsInteractible</c> is <c>hanger == null</c> (ClimbHandle.cs:99-102).</summary>
    public static DenyReason Handle(bool taken, bool takenByMe) =>
        taken && !takenByMe ? DenyReason.Occupied : DenyReason.None;

    /// <summary>
    /// The climb jump gate (CharacterClimbing.cs:122): more than 1 s since the last jump and under 0.5 s out of
    /// stamina. Stamina wins, since the cooldown ends by itself.
    /// </summary>
    public static DenyReason Lunge(float sinceClimbJump, float outOfStaminaFor)
    {
        if (outOfStaminaFor >= 0.5f)
            return DenyReason.NoStamina;
        return sinceClimbJump > 1f ? DenyReason.None : DenyReason.TooSoon;
    }

    /// <summary>Stick Y below which an upward arm pull is noise, not an attempt.</summary>
    public const float PullThreshold = 0.3f;

    /// <summary>
    /// An upward arm pull the game drops (CharacterClimbing.cs:297-310): a slide-only surface slides the climber down,
    /// and with no stamina the up input is ignored. Downward pulls always work.
    /// </summary>
    public static DenyReason Pull(float wantedStickY, bool slideOnly, bool outOfStamina)
    {
        if (wantedStickY < PullThreshold)
            return DenyReason.None;
        if (slideOnly)
            return DenyReason.TooSteep;
        return outOfStamina ? DenyReason.NoStamina : DenyReason.None;
    }

    /// <summary>
    /// Decision D3: the wall grab is cast from the hand, but only for a hold within the game's reach from the camera
    /// (<c>TryToStartWallClimb</c>'s 1.25 m, CharacterClimbing.cs:534), so a VR climber reaches no farther than a
    /// flat one.
    /// </summary>
    public static DenyReason WallReach(float eyeToHold, float reach) =>
        eyeToHold > reach ? DenyReason.OutOfReach : DenyReason.None;
}
