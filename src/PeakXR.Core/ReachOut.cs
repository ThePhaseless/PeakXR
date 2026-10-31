using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// Reach out to help a friend up (#70): an open hand stretched toward a friend in trouble arms the game's own reach,
/// which catches anyone within 4 m and 60 degrees of the look (CharacterGrabbing.Reach, :247-280). The arm line runs
/// from a shoulder estimated under the tracked head, so a reach down over a ledge reads as stretched while a hand at
/// the face does not. Tracking space, metres.
/// </summary>
public static class ReachOut
{
    public const float NominalArm = 0.65f;
    public const float MinArm = 0.5f;
    public const float MaxArm = 0.8f;
    public const float AcquireDegrees = 15f;
    public const float HoldDegrees = 25f;

    /// <summary>How much less stretch (fraction of the arm) an acquired pick keeps.</summary>
    public const float StretchExit = 0.10f;

    /// <summary>Seconds a pick must hold before a grip counts, and an armed pick survives after it is lost.</summary>
    public const float Dwell = 0.15f;
    public const float Grace = 0.25f;

    /// <summary>A friend this hand armed within this many seconds gets no second acquire tick.</summary>
    public const float ReTick = 1f;

    /// <summary>The arm is learned only while it points within this of the body yaw.</summary>
    public const float LearnYawDegrees = 45f;

    /// <summary>A pick is acquired, and the arm learned, only with the grip below this.</summary>
    public const float OpenGrip = 0.3f;

    /// <summary>Friends up to here are offered: 4-6 m gets the OutOfReach preview.</summary>
    public const float SearchRange = SocialIntent.ReachRange + 2f;

    /// <summary>The right shoulder from the neck pivot in the body frame; x is mirrored for the left.</summary>
    public static readonly Vector3 ShoulderFromNeck = new(0.18f, -0.15f, 0f);

    public static Vector3 Shoulder(Vector3 neck, float bodyYawDeg, bool left)
    {
        float x = left ? -ShoulderFromNeck.X : ShoulderFromNeck.X;
        float r = bodyYawDeg * UnityQuat.Deg2Rad;
        float s = MathF.Sin(r);
        float c = MathF.Cos(r);
        var z = ShoulderFromNeck.Z;
        return neck + new Vector3(x * c + z * s, ShoulderFromNeck.Y, z * c - x * s);
    }

    /// <summary>The arm length to judge stretch by: nominal until a reach is learned, then clamped.</summary>
    public static float Arm(float learned) =>
        learned > 0f && !float.IsInfinity(learned)
            ? Math.Clamp(learned, MinArm, MaxArm)
            : NominalArm;

    /// <summary>Shoulder to hand as a fraction of the arm; 0 for a bad arm.</summary>
    public static float Stretch(Vector3 shoulder, Vector3 hand, float arm) =>
        arm > 0f && !float.IsInfinity(arm) ? Vector3.Distance(shoulder, hand) / arm : 0f;

    /// <summary>Degrees between the shoulder-to-hand line and the shoulder-to-target line; 180 for a zero-length leg.</summary>
    public static float AimDegrees(Vector3 shoulder, Vector3 hand, Vector3 target)
    {
        var a = hand - shoulder;
        var b = target - shoulder;
        float la = a.Length();
        float lb = b.Length();
        if (la < 1e-5f || lb < 1e-5f)
            return 180f;
        float cos = Math.Clamp(Vector3.Dot(a, b) / (la * lb), -1f, 1f);
        return MathF.Acos(cos) * UnityQuat.Rad2Deg;
    }

    public static bool Aims(
        Vector3 shoulder,
        Vector3 hand,
        Vector3 target,
        float arm,
        float minStretch,
        float maxDegrees
    ) =>
        Stretch(shoulder, hand, arm) >= minStretch
        && AimDegrees(shoulder, hand, target) <= maxDegrees;

    /// <summary>The arm points out along the body (flat, within <see cref="LearnYawDegrees"/>): a fair sample of
    /// its length from the estimated shoulder.</summary>
    public static bool Learns(Vector3 shoulder, Vector3 hand, float bodyYawDeg)
    {
        float x = hand.X - shoulder.X;
        float z = hand.Z - shoulder.Z;
        if (MathF.Sqrt(x * x + z * z) < 1e-3f)
            return false;
        float heading = MathF.Atan2(x, z) * UnityQuat.Rad2Deg;
        return MathF.Abs(BodySlotLayout.DeltaAngle(bodyYawDeg, heading)) <= LearnYawDegrees;
    }

    /// <summary>Why the game's reach would not catch this friend, or None. The plugin never runs the reach-out
    /// while climbing, so climbing is not an input.</summary>
    public static DenyReason Verdict(bool holdingItem, Vector3 look, Vector3 toFriend)
    {
        if (holdingItem)
            return DenyReason.HandsFull;
        if (toFriend.Length() > SocialIntent.ReachRange)
            return DenyReason.OutOfReach;
        return SocialIntent.ReachSees(look, toFriend) ? DenyReason.None : DenyReason.WrongAngle;
    }
}

/// <summary>
/// One hand's reach-out pick: which friend the arm line points at, with hysteresis on angle and stretch, a dwell
/// before it arms and a grace after it is lost, so an arm swing never arms and a wobble never drops a ready pick.
/// Per frame: <see cref="Begin"/>, one <see cref="Offer"/> per friend, then <see cref="Step"/>. Allocation-free.
/// </summary>
public sealed class ReachOutTracker
{
    /// <summary>The current pick (a friend id), -1 none.</summary>
    public int Friend { get; private set; } = -1;

    /// <summary><see cref="Friend"/> once it has held <see cref="ReachOut.Dwell"/>, or the armed pick lost within
    /// <see cref="ReachOut.Grace"/>; -1 none.</summary>
    public int ArmedFriend { get; private set; } = -1;

    /// <summary>This Step armed a friend this hand had not armed within <see cref="ReachOut.ReTick"/>: the tick.</summary>
    public bool Acquired { get; private set; }

    float pickAt;
    bool armed;

    bool curOffered;
    float curAngle;
    bool curInRange;

    int best = -1;
    float bestAngle;
    bool bestInRange;

    int lostFriend = -1;
    float lostAt;
    float lostPickAt;
    bool lostArmed;

    int retickFriend = -1;
    float retickAt;

    public void Begin()
    {
        curOffered = false;
        best = -1;
    }

    /// <summary>One friend this frame, at the smallest angle of its parts.</summary>
    public void Offer(int friend, float angleDeg, bool inRange)
    {
        if (friend < 0)
            return;
        if (friend == Friend)
        {
            curOffered = true;
            curAngle = angleDeg;
            curInRange = inRange;
            return;
        }
        if (
            best < 0
            || (inRange && !bestInRange)
            || (inRange == bestInRange && angleDeg < bestAngle)
        )
        {
            best = friend;
            bestAngle = angleDeg;
            bestInRange = inRange;
        }
    }

    /// <param name="stretch">Shoulder to hand over the arm.</param>
    /// <param name="enterStretch">The stretch that acquires (the setting).</param>
    /// <param name="open">The grip is open enough to acquire.</param>
    public void Step(float stretch, float enterStretch, bool open, float now)
    {
        bool holds =
            Friend >= 0
            && curOffered
            && curAngle <= ReachOut.HoldDegrees
            && stretch >= enterStretch - ReachOut.StretchExit;
        bool canAcquire =
            open && stretch >= enterStretch && best >= 0 && bestAngle <= ReachOut.AcquireDegrees;
        bool wasArmed = armed;
        if (canAcquire && (!holds || (!curInRange && bestInRange)))
        {
            // Before Lose, which overwrites lost*: B to A to B within Grace keeps B's dwell.
            float pick = best == lostFriend && now - lostAt <= ReachOut.Grace ? lostPickAt : now;
            if (Friend >= 0)
                Lose(now);
            Friend = best;
            pickAt = pick;
            wasArmed = false;
        }
        else if (!holds && Friend >= 0)
        {
            Lose(now);
            Friend = -1;
        }

        armed = Friend >= 0 && now - pickAt >= ReachOut.Dwell;
        Acquired =
            armed && !wasArmed && !(retickFriend == Friend && retickAt >= now - ReachOut.ReTick);
        if (armed)
        {
            retickFriend = Friend;
            retickAt = now;
        }
        if (armed)
            ArmedFriend = Friend;
        else if (lostArmed && now - lostAt <= ReachOut.Grace)
            ArmedFriend = lostFriend;
        else
            ArmedFriend = -1;
    }

    void Lose(float now)
    {
        lostFriend = Friend;
        lostAt = now;
        lostPickAt = pickAt;
        lostArmed = armed;
        if (armed)
        {
            retickFriend = Friend;
            retickAt = now;
        }
        armed = false;
    }

    public void Reset()
    {
        Friend = ArmedFriend = best = lostFriend = retickFriend = -1;
        Acquired = armed = lostArmed = curOffered = false;
    }
}
