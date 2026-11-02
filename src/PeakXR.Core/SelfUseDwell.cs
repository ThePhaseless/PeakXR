using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// Whether a hand has stopped: it stays within <see cref="Radius"/> of where it stopped. A displacement, not a speed,
/// so tremor and tracking noise do not read as motion, and a slow carry cannot pass as a stop (#147).
/// </summary>
public sealed class HandStill
{
    /// <summary>Metres a resting hand may wander: more than a living hand's tremor, less than a slow carry covers in a
    /// dwell (at 0.2 m/s, in 0.25 s).</summary>
    public const float Radius = 0.05f;

    private Vector3 anchor;
    private bool anchored;

    /// <summary>True while <paramref name="hand"/> stays near the last place it stopped; false on the frame it leaves,
    /// which makes this the new place. Tracking space, so walking and turning do not count.</summary>
    public bool Update(Vector3 hand)
    {
        if (anchored && Vector3.DistanceSquared(hand, anchor) <= Radius * Radius)
            return true;
        anchor = hand;
        anchored = true;
        return false;
    }

    public void Reset() => anchored = false;
}

/// <summary>
/// The one rule for a held item that starts its use on your own body with no button (chest, forearm, mouth, face), so
/// nothing is spent on the way past (#147: the belt sits inside the torso's press zone, so stowing a bandage applied it,
/// and the started cast then refused the holster). The hand has to stop there for <see cref="Dwell"/>, and a slot or
/// pack that would take the item wins outright: nothing counts there, and a use this started ends on reaching it, which
/// clears the game's mid-use switch block before the grip opens. Leaving costs nothing: the game spends only on a
/// finished cast (Item.cs:902, :918).
/// </summary>
public sealed class SelfUseDwell
{
    /// <summary>On your chest or forearm: long enough to see it coming and leave.</summary>
    public const float BodyDwell = 0.6f;

    /// <summary>At your mouth, as at a friend's: its zone is small, so a hand there means it.</summary>
    public const float MouthDwell = HandOver.MouthDwell;

    /// <summary>Eye to eye: these items kill or warp you.</summary>
    public const float FaceDwell = 0.5f;

    public float Dwell = BodyDwell;

    private readonly HandStill still = new();
    private float timer;
    private bool waitForMove;

    /// <summary>The use is held this frame.</summary>
    public bool Active { get; private set; }

    /// <summary>Resting there, more than half way to starting: time to show it. A hand in motion never gets this far.</summary>
    public bool Settling => !Active && timer >= Dwell * 0.5f;

    /// <summary>How far the dwell has run, 0 to 1.</summary>
    public float Fraction => MathF.Min(timer / Dwell, 1f);

    /// <param name="hand">The holding hand, tracking space.</param>
    /// <param name="contact">The item is at the body part this frame.</param>
    /// <param name="putAway">Opening the grip here would stow the held item.</param>
    /// <param name="dt">Seconds since the last update.</param>
    public bool Update(Vector3 hand, bool contact, bool putAway, float dt)
    {
        bool resting = still.Update(hand);
        waitForMove &= resting;
        if (!contact || putAway)
        {
            timer = 0f;
            Active = false;
            return false;
        }
        // Under way it rides out a wiping or wrapping motion; only leaving the body or reaching a slot ends it.
        if (Active)
            return true;
        if (waitForMove || !resting)
        {
            timer = 0f;
            return false;
        }
        timer += dt;
        Active = timer >= Dwell;
        return Active;
    }

    /// <summary>An item arrived at <paramref name="hand"/> (drawn from a slot, picked up): the hand did not choose this
    /// contact, so it has to move once before a rest counts. Staying on the body meanwhile is fine: a bandage drawn from
    /// the belt can go straight up to the chest.</summary>
    public void Arrive(Vector3 hand)
    {
        Reset();
        still.Update(hand);
        waitForMove = true;
    }

    public void Reset()
    {
        still.Reset();
        timer = 0f;
        waitForMove = false;
        Active = false;
    }
}
