using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>What a grip on another player's body does through the game's own input.</summary>
public enum SocialKind : byte
{
    /// <summary>Not a social grip: the grip falls through to grab, climb or interact (unless refused).</summary>
    None,

    /// <summary>Hold interact on the body: drop the friend you carry, the cannibal hold.</summary>
    Interact,

    /// <summary>Hold interact on a passed-out friend to lift them.</summary>
    Carry,

    /// <summary>Hold the empty-hand reach (use secondary) to pull a friend up.</summary>
    Reach,
}

/// <summary>The game state a body grip is judged on, read from <c>CharacterData</c> by the caller.</summary>
public struct SocialFacts
{
    /// <summary>Run setting HelpingHand = 1: the reach button kicks instead (CharacterGrabbing.cs:33).</summary>
    public bool KickMode;
    public bool HoldingItem;

    /// <summary>On a wall, rope or vine.</summary>
    public bool Climbing;

    /// <summary>Climbable rock lies within the hand's climb radius: that grip is meant for the wall.</summary>
    public bool HandOnWall;
    public bool CarryingOther;
    public bool IsBot;

    /// <summary>The grip landed on the friend's hand or arm, the place a helping hand goes.</summary>
    public bool OnHandOrArm;
    public bool CarriedByMe;
    public bool Cannibalizable;

    /// <summary>Fully passed out and not dead.</summary>
    public bool PassedOut;
    public bool CarriedByOther;

    /// <summary><c>CharacterGrabbing.TargetCanBeHelped</c>: stuck, struggling or climbing below.</summary>
    public bool Helpable;
}

/// <summary>
/// Decides what a grip on a friend means, mirroring the game's own gates (CharacterGrabbing.CanGrab, CharacterInteractible
/// .CanBeCarried) so a grip that would do nothing is refused with a reason instead of silently falling through.
/// Kick mode never reaches: the kick is the two-palm <see cref="ShoveGesture"/>.
/// </summary>
public static class SocialIntent
{
    /// <summary>The kind of grip, or <see cref="SocialKind.None"/> with <paramref name="refusal"/> set when the grip
    /// is claimed only to say why nothing happens.</summary>
    public static SocialKind Body(in SocialFacts f, out DenyReason refusal)
    {
        refusal = DenyReason.None;
        if (f.IsBot)
            return SocialKind.None;
        if (f.CarriedByMe || f.Cannibalizable)
            return SocialKind.Interact;
        if (f.PassedOut)
        {
            if (f.CarriedByOther)
                refusal = DenyReason.Occupied;
            else if (f.CarryingOther)
                refusal = DenyReason.HandsFull;
            else
                return SocialKind.Carry;
            return SocialKind.None;
        }
        if (f.KickMode)
            return SocialKind.None;
        if (f.Helpable)
        {
            if (f.Climbing && f.HandOnWall)
                return SocialKind.None;
            if (f.HoldingItem)
                refusal = DenyReason.HandsFull;
            else if (f.Climbing)
                refusal = DenyReason.NotWhileClimbing;
            else
                return SocialKind.Reach;
            return SocialKind.None;
        }
        if (f.OnHandOrArm && !f.HandOnWall)
            refusal = DenyReason.NotInTrouble;
        return SocialKind.None;
    }

    /// <summary>The game's reach cone (CharacterGrabbing.Reach, CharacterGrabbing.cs:252-253): degrees between the
    /// look and the friend, and the farthest friend (m, centre to centre).</summary>
    public const float ReachConeDegrees = 60f;

    public const float ReachRange = 4f;

    /// <summary>
    /// The game's own reach would catch a friend at <paramref name="toFriend"/> (centre to centre) with the look
    /// along <paramref name="look"/>. Every client tests this from the synced look, so a hand on the friend with the
    /// look elsewhere pulls nobody up.
    /// </summary>
    public static bool ReachSees(Vector3 look, Vector3 toFriend)
    {
        float d = toFriend.Length();
        if (d > ReachRange)
            return false;
        float l = look.Length();
        if (d < 1e-5f || l < 1e-5f)
            return true;
        float cos = Vector3.Dot(look, toFriend) / (l * d);
        return cos >= MathF.Cos(ReachConeDegrees * MathF.PI / 180f);
    }

    /// <summary>Why the game's kick would not fire (CharacterGrabbing.cs:104): None when it would.</summary>
    public static DenyReason Shove(bool climbing, bool grounded, bool outOfStamina, bool kicking)
    {
        if (climbing)
            return DenyReason.NotWhileClimbing;
        if (!grounded)
            return DenyReason.NeedsGround;
        if (outOfStamina)
            return DenyReason.NoStamina;
        return kicking ? DenyReason.TooSoon : DenyReason.None;
    }
}

/// <summary>
/// Both open palms pushed forward together, fast: the kick-mode shove (D6). Arm-swing sprint moves the hands in
/// opposite directions, so it never reads as a shove. Fires once per push and re-arms when both hands slow down.
/// Allocation-free.
/// </summary>
public sealed class ShoveGesture
{
    /// <summary>Each palm's speed (m/s) for a shove.</summary>
    public float MinSpeed = 1.5f;

    /// <summary>Both hands below this speed (m/s) arm the next shove.</summary>
    public float ReArmSpeed = 0.6f;

    /// <summary>Cosine between the two hands' directions, and between the push and the level forward.</summary>
    public float Agree = 0.7f;
    public float Ahead = 0.5f;

    private bool armed = true;

    /// <summary>True on the frame a shove starts; <paramref name="direction"/> is its unit direction.</summary>
    public bool Update(
        Vector3 leftVelocity,
        Vector3 rightVelocity,
        bool palmsOpen,
        Vector3 forward,
        out Vector3 direction
    )
    {
        direction = Vector3.Zero;
        float l = leftVelocity.Length();
        float r = rightVelocity.Length();
        if (l < ReArmSpeed && r < ReArmSpeed)
            armed = true;
        if (!armed || !palmsOpen || l < MinSpeed || r < MinSpeed)
            return false;
        var a = leftVelocity / l;
        var b = rightVelocity / r;
        if (Vector3.Dot(a, b) < Agree)
            return false;
        var dir = Vector3.Normalize(a + b);
        if (Vector3.Dot(dir, forward) < Ahead)
            return false;
        armed = false;
        direction = dir;
        return true;
    }

    public void Reset() => armed = true;
}
