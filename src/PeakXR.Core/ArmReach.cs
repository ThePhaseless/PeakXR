using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// Arm-length calibration: maps the player's reach onto the avatar's. The scout's shoulders sit about 0.47 m under
/// and 0.2 m behind its eyes (a person's about 0.25 m and 0.08 m) with 0.605 m of arm, so hands a person holds
/// comfortably in front of the chest are out of its reach, and a lean (whose bend moves the shoulders about half
/// as far as the eyes) puts them further out: run 28 had the controllers 0.86 m from the leaned shoulders and
/// the arms locked straight. Close in, the hand stays exactly on the controller; past a knee, distance from the
/// avatar's shoulder is compressed smoothly so the player's longest measured reach lands on the avatar's
/// slightly bent full reach. The hand keeps the controller's direction from the shoulder and its rotation.
/// </summary>
public static class ArmReach
{
    /// <summary>Fraction of the straight arm the avatar reaches at most: the elbow keeps a natural ~30 degree
    /// bend instead of locking (and flipping) straight.</summary>
    public const float BentReach = 0.97f;

    /// <summary>Fraction of the bent reach within which the hand is exactly on the controller.</summary>
    public const float Knee = 0.8f;

    /// <summary>Player reach bounds, in bent avatar reaches. Below the minimum nothing needs compressing; above
    /// the maximum a sample is a warp or a lagging view, not an arm (and the curve would turn back).</summary>
    public const float MinPlayer = 1.1f;

    public const float MaxPlayer = 1.9f;

    /// <summary>
    /// Distance of the avatar's hand from its shoulder for a controller <paramref name="distance"/> from it.
    /// Identity up to the knee, then a cubic easing (slope 1 at the knee, 0 at the player's reach, monotone)
    /// up to the bent reach, which it never passes.
    /// </summary>
    public static float Map(float distance, float avatarReach, float playerReach)
    {
        float r = avatarReach * BentReach;
        float p = PlayerReach(avatarReach, playerReach);
        // The cubic stays monotone while its start slope (p - k) / (r - k) is at most 3.
        float k = MathF.Min(r * Knee, (3f * r - p) * 0.5f);
        if (distance <= k)
            return distance;
        if (distance >= p)
            return r;
        float m = (p - k) / (r - k);
        float t = (distance - k) / (p - k);
        return k + (r - k) * t * (m + t * (3f - 2f * m + t * (m - 2f)));
    }

    /// <summary>The controller distance from the shoulder at which <see cref="Map"/> reaches the bent arm: the
    /// learned <paramref name="playerReach"/> held within the player bounds.</summary>
    public static float PlayerReach(float avatarReach, float playerReach)
    {
        float r = avatarReach * BentReach;
        // NaN fails every comparison and would pass a clamp: treat it as not learned yet.
        return Math.Clamp(playerReach >= 0f ? playerReach : 0f, r * MinPlayer, r * MaxPlayer);
    }

    /// <summary>
    /// How far from the shoulder a controller may be before the avatar's hand really cannot follow it (the ghost
    /// hand's reach): the player's own reach while the map carries the hand (<paramref name="mapWeight"/> 1, an empty
    /// hand standing), the bare arm while it does not (0, holding or climbing). A resting hand past the bare arm is
    /// still on the map: the scout's low, set-back shoulders and a rig above x1 put it there (run 17).
    /// </summary>
    public static float GhostReach(float avatarReach, float playerReach, float mapWeight)
    {
        float w = mapWeight >= 0f ? MathF.Min(mapWeight, 1f) : 0f;
        return avatarReach + (PlayerReach(avatarReach, playerReach) - avatarReach) * w;
    }

    /// <summary>Longest the local player's drawn arm (avatar.local's DrawnHands, drawing only) is stretched, as a
    /// multiple of the arm. The scout's shoulders sit about 0.22 m lower and 0.12 m further back from the eyes than a
    /// person's, so hands held in front of the face are 0.75-0.8 m from them against a 0.6 m arm (a 1.3x stretch);
    /// 1.5x covers every reach short of a full overhead stretch.</summary>
    public const float MaxDrawnStretch = 1.5f;

    /// <summary>How far from the shoulder the drawn hand can follow its controller.</summary>
    public static float DrawnReach(float avatarReach) => avatarReach * MaxDrawnStretch;

    /// <summary>
    /// Factor the drawn arm is scaled by about its shoulder so a hand <paramref name="hand"/> from the shoulder is
    /// drawn where its controller is (<paramref name="controller"/> from the shoulder), out to
    /// <see cref="DrawnReach"/>: never shorter than the arm, never past <see cref="MaxDrawnStretch"/> (a hand lagging
    /// near the shoulder is not blown up), and eased in by <paramref name="weight"/> (the arms' tracked weight).
    /// </summary>
    public static float DrawnStretch(float hand, float controller, float avatarReach, float weight)
    {
        if (!(hand > 1e-4f) || !(controller > 0f) || !(weight > 0f))
            return 1f;
        float k = Math.Clamp(
            MathF.Min(controller, DrawnReach(avatarReach)) / hand,
            1f,
            MaxDrawnStretch
        );
        return 1f + (k - 1f) * MathF.Min(weight, 1f);
    }

    /// <summary><see cref="GhostReach"/> while the arm is drawn stretched: the ghost shows only past where the
    /// drawn hand can follow.</summary>
    public static float DrawnGhostReach(float avatarReach, float playerReach, float mapWeight) =>
        MathF.Max(GhostReach(avatarReach, playerReach, mapWeight), DrawnReach(avatarReach));

    /// <summary>The avatar's hand target for a controller at <paramref name="controller"/>: on the line from
    /// <paramref name="shoulder"/>, at the mapped distance.</summary>
    public static Vector3 Target(
        Vector3 shoulder,
        Vector3 controller,
        float avatarReach,
        float playerReach
    )
    {
        var off = controller - shoulder;
        float d = off.Length();
        return d < 1e-6f ? controller : shoulder + off * (Map(d, avatarReach, playerReach) / d);
    }

    /// <summary>Seconds the map takes to fade in, or out when the body stops being upright.</summary>
    public const float FadeSeconds = 0.3f;

    /// <summary>
    /// One tick of a hand's map weight. Grabbing drops it at once: the item is welded to the controller pose
    /// that same tick, so a hand still fading in from the mapped target would jump and tug. Releasing (and
    /// standing up) fades it back in, moving the hand off the controller smoothly.
    /// </summary>
    public static float StepWeight(float weight, bool holding, bool upright, float dt)
    {
        if (holding)
            return 0f;
        float to = upright ? 1f : 0f;
        float step = dt / FadeSeconds;
        return MathF.Abs(to - weight) <= step ? to : weight + MathF.Sign(to - weight) * step;
    }
}

/// <summary>
/// Learns the player's reach from controller distances to the avatar's shoulder: the longest distance held for
/// <see cref="HoldSeconds"/> (the minimum of a held span), so a one-frame pop, warp or respawn cannot pin it at
/// the cap for the session. Invalid spans (the caller's view is settling after a warp, spawn or takeover),
/// non-finite samples, implausible distances and overhead reaches (from the scout's low shoulder a hand overhead
/// looks about 1 m long) restart the span and teach nothing.
/// </summary>
public sealed class ReachLearner
{
    public const float HoldSeconds = 0.2f;

    /// <summary>Rise over distance above which a reach counts as overhead (30 degrees above the shoulder).</summary>
    public const float MaxRise = 0.5f;

    /// <summary>The learned reach (m), 0 until a span has been held.</summary>
    public float Reach { get; private set; }

    float _spanMin = float.MaxValue;
    float _spanTime;

    /// <summary>Forgets the reach, e.g. after a world-scale change: it was learned in the old world units.</summary>
    public void Reset()
    {
        Reach = 0f;
        _spanMin = float.MaxValue;
        _spanTime = 0f;
    }

    /// <summary>A world-scale change: the learned reach moves into the new world units (<paramref name="factor"/> =
    /// new scale / old), so Auto's small rescales do not throw away what was learned. A held span starts over.</summary>
    public void Rescale(float factor)
    {
        if (!(factor > 0f) || float.IsInfinity(factor))
        {
            Reset();
            return;
        }
        Reach *= factor;
        _spanMin = float.MaxValue;
        _spanTime = 0f;
    }

    /// <param name="distance">Controller to avatar shoulder (m).</param>
    /// <param name="rise">How far the controller is above the shoulder (m).</param>
    /// <param name="valid">The caller's own gates: upright, empty hand, body at rest, view settled.</param>
    public void Sample(float distance, float rise, float avatarReach, bool valid, float dt)
    {
        float max = avatarReach * ArmReach.BentReach * ArmReach.MaxPlayer;
        if (!valid || !(distance <= max) || !(rise <= MaxRise * distance) || !(dt > 0f))
        {
            _spanMin = float.MaxValue;
            _spanTime = 0f;
            return;
        }
        _spanMin = MathF.Min(_spanMin, distance);
        _spanTime += dt;
        if (_spanTime < HoldSeconds)
            return;
        Reach = MathF.Max(Reach, _spanMin);
        _spanMin = float.MaxValue;
        _spanTime = 0f;
    }
}
