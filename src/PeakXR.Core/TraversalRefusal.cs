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

    /// <summary>
    /// How far from the camera a hold the game's climb start can still catch: its 1.25 m ray (CharacterClimbing.cs:552)
    /// retried as a line check thickened up to 0.1 m (:553-561), so surface points up to 1.35 m away are hit.
    /// </summary>
    public const float GameWallReach = 1.35f;
}

/// <summary>
/// A climb lunge only on a clear fling (external review #16): vanilla never jumps without a press, and a climb jump
/// costs 20 % stamina (CharacterClimbing.cs:238-241), so a brisk hand-over-hand reach must never set one off. The
/// last climbing hand must let go at or above the shoulder while moving up fast, and still be rising
/// <see cref="ConfirmDelay"/> later without closing on a new hold.
/// </summary>
public static class LungeIntent
{
    /// <summary>Seconds after the release that the hand must still be rising.</summary>
    public const float ConfirmDelay = 0.08f;

    /// <summary>How far below the head (metres) the hand may let go: about the shoulder.</summary>
    public const float ShoulderDrop = 0.25f;

    /// <summary>The release looks like a fling: fast upward, at or above the shoulder.</summary>
    public static bool AtRelease(float upSpeed, float minUpSpeed, float handBelowHead) =>
        upSpeed >= minUpSpeed && handBelowHead <= ShoulderDrop;

    /// <summary><see cref="ConfirmDelay"/> later: the arm carried on up (half the lunge speed or more) and the grip
    /// did not close again on a hold, as a reach does when it arrives.</summary>
    public static bool Confirm(float upSpeedNow, float minUpSpeed, bool gripClosedAgain) =>
        !gripClosedAgain && upSpeedNow >= 0.5f * minUpSpeed;
}
