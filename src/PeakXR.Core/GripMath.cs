using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// Rigid-transform helpers for placing held items in tracked hands, in Unity conventions
/// (see <see cref="UnityQuat"/>). A pose (p, r) maps a local point x to p + r * x.
/// </summary>
public static class GripMath
{
    /// <summary>World pose of a child given its parent's pose and its local pose.</summary>
    public static void Compose(
        Vector3 parentPos,
        Quaternion parentRot,
        Vector3 localPos,
        Quaternion localRot,
        out Vector3 pos,
        out Quaternion rot
    )
    {
        pos = parentPos + UnityQuat.Rotate(parentRot, localPos);
        rot = Quaternion.Normalize(UnityQuat.Mul(parentRot, localRot));
    }

    public static void Inverse(
        Vector3 pos,
        Quaternion rot,
        out Vector3 invPos,
        out Quaternion invRot
    )
    {
        invRot = Quaternion.Inverse(rot);
        invPos = -UnityQuat.Rotate(invRot, pos);
    }

    /// <summary>
    /// Where an item sits in a controller's frame so that its grip point (pose relative to the item) lands on the
    /// hand bone (pose relative to the controller): item = bone * grip⁻¹.
    /// </summary>
    public static void ItemInHand(
        Vector3 gripPos,
        Quaternion gripRot,
        Vector3 bonePos,
        Quaternion boneRot,
        out Vector3 itemPos,
        out Quaternion itemRot
    )
    {
        Inverse(gripPos, gripRot, out var ip, out var ir);
        Compose(bonePos, boneRot, ip, ir, out itemPos, out itemRot);
    }

    /// <summary>Reflects a pose across the local YZ plane: right-hand data becomes left-hand data.</summary>
    public static void MirrorX(ref Vector3 pos, ref Quaternion rot)
    {
        pos.X = -pos.X;
        rot = new Quaternion(rot.X, -rot.Y, -rot.Z, rot.W);
    }

    /// <summary>Shortest rotation taking direction <paramref name="from"/> onto <paramref name="to"/>.</summary>
    public static Quaternion FromTo(Vector3 from, Vector3 to)
    {
        float lf = from.Length(),
            lt = to.Length();
        if (lf < 1e-6f || lt < 1e-6f)
            return Quaternion.Identity;
        from /= lf;
        to /= lt;
        float d = Vector3.Dot(from, to);
        if (d < -0.99999f)
        {
            // Opposite: any perpendicular axis works.
            var axis = Vector3.Cross(new Vector3(1f, 0f, 0f), from);
            if (axis.LengthSquared() < 1e-6f)
                axis = Vector3.Cross(new Vector3(0f, 1f, 0f), from);
            return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.PI);
        }
        var c = Vector3.Cross(from, to);
        return Quaternion.Normalize(new Quaternion(c.X, c.Y, c.Z, 1f + d));
    }

    /// <summary>
    /// Turns <paramref name="rot"/> the least amount so its local <paramref name="axisLocal"/> points along
    /// <paramref name="worldDir"/>. Used for two-hand holds: the item's grip-to-grip axis points at the second hand.
    /// </summary>
    public static Quaternion AlignAxis(Quaternion rot, Vector3 axisLocal, Vector3 worldDir)
    {
        if (worldDir.LengthSquared() < 1e-8f || axisLocal.LengthSquared() < 1e-8f)
            return rot;
        var current = UnityQuat.Rotate(rot, axisLocal);
        return Quaternion.Normalize(UnityQuat.Mul(FromTo(current, worldDir), rot));
    }

    /// <summary>
    /// A two-hand hold of an aimed item: turns <paramref name="rot"/> the least amount so its
    /// <paramref name="barrel"/> (world direction of its +Z as held in one hand) runs along the two-hand axis
    /// (<see cref="AimMath.TwoHandDirection"/>), the way a crossbow lies along the arm that steadies it. Hands
    /// closer than <paramref name="minSeparation"/> leave the one-hand hold as it is.
    /// </summary>
    public static Quaternion TwoHandAim(
        Quaternion rot,
        Vector3 barrel,
        Vector3 holdPos,
        Vector3 supportPos,
        float minSeparation
    )
    {
        var dir = AimMath.TwoHandDirection(holdPos, supportPos, barrel, minSeparation);
        return Quaternion.Normalize(UnityQuat.Mul(FromTo(barrel, dir), rot));
    }

    /// <summary>
    /// An aimed item's pose in its controller's frame (<paramref name="pos"/>, <paramref name="rot"/>) turned about the
    /// controller origin, the fist, so its barrel (+Z) runs along <paramref name="aim"/>, the controller's aim in that
    /// frame: shots then go where the controller points on any runtime. Of every such turn it takes the one that
    /// moves the fist's tube (the grip's +Z) least, its roll about the barrel free: keeping the item's up instead put
    /// the grip 40-55 degrees out of the fist (#25). The fist holds its tube either way round, so an item whose barrel
    /// leaves the bottom of the fist (the blowgun, about 100 degrees out) is held the other way round when that tilts
    /// it less (<see cref="FistTilt"/>). Returns the turn. No aim leaves the pose alone.
    /// </summary>
    public static Quaternion AlongAim(Vector3 aim, ref Vector3 pos, ref Quaternion rot)
    {
        if (aim.LengthSquared() < 1e-8f)
            return Quaternion.Identity;
        aim = Vector3.Normalize(aim);
        var barrel = UnityQuat.Rotate(rot, Vector3.UnitZ);
        var turn = LeastTilt(barrel, aim);
        var flip = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI);
        var flipped = UnityQuat.Mul(LeastTilt(UnityQuat.Rotate(flip, barrel), aim), flip);
        // A degree of preference keeps a near tie from flipping the item between frames.
        if (FistTilt(flipped) < FistTilt(turn) - 1f)
            turn = flipped;
        pos = UnityQuat.Rotate(turn, pos);
        rot = Quaternion.Normalize(UnityQuat.Mul(turn, rot));
        return turn;
    }

    /// <summary>Degrees a hold turn takes the fist's tube (the grip's +Z) off its line, either way round.</summary>
    public static float FistTilt(Quaternion turn) =>
        MathF.Acos(Math.Clamp(MathF.Abs(UnityQuat.Rotate(turn, Vector3.UnitZ).Z), 0f, 1f))
        * UnityQuat.Rad2Deg;

    /// <summary>The turn that puts <paramref name="barrel"/> on <paramref name="aim"/> (unit) and keeps +Z nearest
    /// itself: the shortest swing, then the roll about the aim that brings +Z's part across the aim onto its own.</summary>
    static Quaternion LeastTilt(Vector3 barrel, Vector3 aim)
    {
        var swing = FromTo(barrel, aim);
        var w = UnityQuat.Rotate(swing, Vector3.UnitZ);
        var across = w - Vector3.Dot(w, aim) * aim;
        var want = Vector3.UnitZ - aim.Z * aim;
        if (across.LengthSquared() < 1e-8f || want.LengthSquared() < 1e-8f)
            return swing;
        float angle = MathF.Atan2(
            Vector3.Dot(Vector3.Cross(across, want), aim),
            Vector3.Dot(across, want)
        );
        return Quaternion.Normalize(
            UnityQuat.Mul(Quaternion.CreateFromAxisAngle(aim, angle), swing)
        );
    }

    /// <summary>Same rotation axis, angle limited to <paramref name="maxDeg"/>.</summary>
    public static Quaternion LimitAngle(Quaternion q, float maxDeg)
    {
        q = Quaternion.Normalize(q);
        if (q.W < 0f)
            q = new Quaternion(-q.X, -q.Y, -q.Z, -q.W);
        float angle = 2f * MathF.Acos(Math.Clamp(q.W, -1f, 1f)) * UnityQuat.Rad2Deg;
        if (angle <= maxDeg || angle < 1e-4f)
            return q;
        return Slerp(Quaternion.Identity, q, maxDeg / angle);
    }

    public static Quaternion Slerp(Quaternion a, Quaternion b, float t) =>
        Quaternion.Slerp(a, b, t);

    /// <summary>
    /// Extra head rotation (world space, applied on top of the animated head) that turns the game's look
    /// direction (yaw/pitch, pitch positive up, no roll) into the tracked head rotation, limited in angle.
    /// </summary>
    public static Quaternion HeadDelta(
        Quaternion hmdWorld,
        float yawDeg,
        float pitchDeg,
        float maxDeg
    )
    {
        var look = UnityQuat.Euler(-pitchDeg, yawDeg, 0f);
        var delta = UnityQuat.Mul(hmdWorld, Quaternion.Inverse(look));
        return LimitAngle(delta, maxDeg);
    }
}
