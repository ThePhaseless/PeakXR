using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// When the held item may ride the ragdoll hand it is welded to, and when it must leave it. The avatar's arm stops
/// at its own reach from a shoulder at standing height, so a hand stooped toward the ground (a fuse, a fire, a build
/// spot) drags the item away from the controller. The rule reads the arm, not the item: the weld lets go only when
/// the ragdoll hand itself stays more than <see cref="Release"/> from its controller for <see cref="ReleaseHold"/>
/// (long enough for a physical crouch to lower the shoulder, vr61 vr.230; an item's grip offset never counts, vr53).
/// It rejoins once the hand is within <see cref="Rejoin"/> of its controller and the free item is within
/// <see cref="Release"/> of the hand (the rejoin puts the hand on the item's grip), or once the hand has stopped
/// closing in short of <see cref="Release"/> (an arm out of reach, which a weld would keep), and a fresh weld stays for
/// <see cref="Settle"/> before it may let go again, so the rejoin's own jolt never flips it back. An item the world
/// holds back (a wall the walk or the hand presses it on) never leaves the hand for it, as in vanilla (#17); the arm
/// then reaches no further than <see cref="ItemFollow.HeldBackReach"/>, so the kept item never pushes the body (#116).
/// </summary>
public sealed class WeldGate
{
    /// <summary>Ragdoll hand to its controller that releases the weld, metres: past what a crouched arm falls short
    /// at a deep stoop (about 0.13 m, vr65 vr.230), well short of an arm that cannot reach at all (0.5-1.2 m, vr50).</summary>
    public float Release = 0.2f;

    /// <summary>Ragdoll hand to its controller that rejoins it, metres: within a stooped arm's own shortfall, so a
    /// free item rejoins without standing up.</summary>
    public float Rejoin = 0.1f;

    /// <summary>How long the arm must stay out of reach before the weld lets go, seconds: a stuck arm, not one catching
    /// up with a fast swing or a jump of the controller (mp31: 0.2 s let the passport flap while standing). The ghost
    /// hand shows the real hand meanwhile.</summary>
    public float ReleaseHold = 0.9f;

    /// <summary>The hold when terrain lies between the shoulder and the controller: stuck at once.</summary>
    public float BlockedHold = 0.2f;

    /// <summary>How long the arm must be back before the item rejoins, seconds.</summary>
    public float RejoinHold = 0.08f;

    /// <summary>A fresh weld holds at least this long, seconds.</summary>
    public float Settle = 0.5f;

    /// <summary>How long a free hand short of <see cref="Release"/> must stop closing in before the item rejoins it,
    /// seconds: an arm that cannot reach its controller settled 0.15 m past its shortfall and never got within
    /// <see cref="Rejoin"/>, so its item stayed free 22 s (live run of 0e8ad7e).</summary>
    public float SettledHold = 0.3f;

    /// <summary>Closing in by less than this (m) over <see cref="SettledHold"/> counts as settled.</summary>
    public float SettledStep = 0.01f;

    private float timer;
    private float settle;
    private float still;
    private float best = float.PositiveInfinity;

    public bool Released { get; private set; }

    /// <param name="handToController">Ragdoll hand to where its controller puts the hand bone, metres.</param>
    /// <param name="handToItem">Ragdoll hand to where the item's grip puts the hand bone, metres.</param>
    /// <param name="shortfall">How far the arm cannot reach its controller right now (<see cref="Shortfall"/>):
    /// release and rejoin are judged past it, so an arm that cannot physically get there keeps its weld
    /// (vr69 vr.230: crouched at a stoop the welded hand sat 0.33 m off).</param>
    /// <param name="blocked">Terrain between the shoulder and the controller (the hand cannot get through).</param>
    /// <param name="itemBlocked">The world holds the item itself back from where its controller puts it (a wall the
    /// walk presses it on, #17): a free carry could not get it there either, and a snap would put it inside the wall.
    /// The weld never lets go of it, and a free item rejoins its hand; the ghost hand shows the controller.</param>
    public bool Update(
        float handToController,
        float handToItem,
        float dt,
        float shortfall = 0f,
        bool blocked = false,
        bool itemBlocked = false
    )
    {
        if (settle > 0f)
            settle -= dt;
        float reach = MathF.Max(shortfall, 0f);
        float lag = handToController - reach;
        // The item is judged past the shortfall too: free, it sits on the controller the arm cannot reach.
        float itemLag = handToItem - reach;
        bool toward = Released
            ? itemBlocked || (lag < Rejoin && itemLag < Release)
            : !itemBlocked && settle <= 0f && lag > Release;
        timer = toward ? timer + dt : 0f;
        if (Released && lag < Release && itemLag < Release)
        {
            if (lag < best - SettledStep)
            {
                best = lag;
                still = 0f;
            }
            else
                still += dt;
        }
        else
        {
            best = float.PositiveInfinity;
            still = 0f;
        }
        if (
            timer
                >= (
                    Released ? RejoinHold
                    : blocked ? BlockedHold
                    : ReleaseHold
                )
            || still >= SettledHold
        )
        {
            Released = !Released;
            timer = 0f;
            still = 0f;
            best = float.PositiveInfinity;
            if (!Released)
                settle = Settle;
        }
        return Released;
    }

    /// <summary>Whether to ask if the world holds the item back: for a free item, and for a welded one whose hand
    /// lags its controller (<paramref name="lag"/>, past the arm's shortfall) once the equip has settled, as the gate
    /// itself waits. Before that the arm is on its way and the item can be metres from its place (#121).</summary>
    public bool ProbesHeldBack(float lag, bool equipSettled) =>
        Released || (equipSettled && lag > Rejoin);

    public void Reset()
    {
        Released = false;
        timer = 0f;
        settle = 0f;
        still = 0f;
        best = float.PositiveInfinity;
    }

    /// <summary>How far a straight arm of <paramref name="armLength"/> from <paramref name="shoulder"/> falls short of
    /// <paramref name="target"/>, metres; 0 within reach.</summary>
    public static float Shortfall(Vector3 shoulder, Vector3 target, float armLength) =>
        MathF.Max(0f, Vector3.Distance(shoulder, target) - armLength);
}

/// <summary>A held item carried by its rigidbody's velocity onto the controller-held pose: the hand's own motion plus a
/// fast pull onto the target, so it neither lags a moving hand nor springs past it.</summary>
public static class ItemFollow
{
    /// <summary>Time constant of the pull onto the target, seconds.</summary>
    public const float Tau = 0.06f;

    /// <summary>Fastest the item is carried, m/s (a hand swing; a thrown item leaves the hand first).</summary>
    public const float MaxSpeed = 15f;

    /// <summary>Fastest spin, rad/s.</summary>
    public const float MaxSpin = 30f;

    /// <summary>A free item farther than this from its place is put there at once, metres: a chase at
    /// <see cref="MaxSpeed"/> falls behind a target that jumps (a recentre, a warp) or something knocks it off.</summary>
    public const float SnapGap = 0.3f;

    public static bool Snaps(float gap) => gap > SnapGap;

    /// <summary>Farthest (m) the arm reaches past its ragdoll hand while the weld keeps an item the world holds back
    /// (<see cref="HeldBackReach"/>).</summary>
    public const float HeldBackPress = 0.05f;

    /// <summary>A character's <paramref name="body"/> stops the <paramref name="holder"/>'s held item as a wall does:
    /// any body but the holder's own, which the item passes, and the one riding with the holder, always in the way of
    /// its hands (#119).</summary>
    public static bool BodyHoldsItemBack(
        object body,
        object holder,
        object carried,
        object carrier
    ) =>
        body != null
        && !ReferenceEquals(body, holder)
        && !ReferenceEquals(body, carried)
        && !ReferenceEquals(body, carrier);

    /// <summary>
    /// Where the arm reaches while the world holds its welded item back: <paramref name="target"/> (the controller's
    /// hand bone), but never more than <see cref="HeldBackPress"/> past the ragdoll <paramref name="hand"/>. The arm's
    /// muscles push on the hip by half of each pull (Bodypart.cs:193-205), so reaching for a controller the item
    /// cannot get to pushed the body off the item's contact: a torch kept on the scout cannon lifted the body onto it
    /// (#116, vr.87). The item stays in the hand as in vanilla; the ghost hand shows the controller.
    /// </summary>
    public static Vector3 HeldBackReach(Vector3 hand, Vector3 target)
    {
        var d = target - hand;
        float len = d.Length();
        return len > HeldBackPress ? hand + d * (HeldBackPress / len) : target;
    }

    public static Vector3 Velocity(Vector3 position, Vector3 target, Vector3 handVelocity, float dt)
    {
        var v = handVelocity + (target - position) * Gain(dt);
        float speed = v.Length();
        return speed > MaxSpeed ? v * (MaxSpeed / speed) : v;
    }

    /// <summary>Angular velocity (rad/s, world axis) that turns <paramref name="rotation"/> onto
    /// <paramref name="target"/>.</summary>
    public static Vector3 Spin(Quaternion rotation, Quaternion target, float dt)
    {
        var delta = Quaternion.Normalize(target * Quaternion.Inverse(rotation));
        if (delta.W < 0f)
            delta = new Quaternion(-delta.X, -delta.Y, -delta.Z, -delta.W);
        float sin = MathF.Sqrt(delta.X * delta.X + delta.Y * delta.Y + delta.Z * delta.Z);
        if (sin < 1e-6f)
            return Vector3.Zero;
        float angle = 2f * MathF.Atan2(sin, delta.W);
        var axis = new Vector3(delta.X, delta.Y, delta.Z) / sin;
        return axis * MathF.Min(angle * Gain(dt), MaxSpin);
    }

    /// <summary>
    /// A hand-carried item's per-tick drag, applied to its motion against the body (<paramref name="bodyVelocity"/>)
    /// only. Vanilla drags the held item like a free body (velocity times <paramref name="drag"/>) and balances that
    /// with the walk's own push on it; the two cancel only while the body walks unhindered. Against a wall, a slope
    /// or the walk's ramp the push alone (5 kg at about 27 m/s^2) shoved the welded hand 0.3 m off its controller.
    /// </summary>
    public static Vector3 BodyDrag(Vector3 velocity, Vector3 bodyVelocity, float drag) =>
        bodyVelocity + (velocity - bodyVelocity) * drag;

    /// <summary>Never more than the whole gap in one tick.</summary>
    private static float Gain(float dt) => dt > 0f ? MathF.Min(1f / Tau, 1f / dt) : 1f / Tau;
}
