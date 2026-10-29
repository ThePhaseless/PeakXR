using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>One eye for culling: its pose (looking along the rotation's +Z, up +Y) and the tangents of its
/// projection's edges (x/z at the left and right edge, y/z at the bottom and top; left and bottom negative).</summary>
public readonly struct EyeView
{
    public readonly Vector3 Position;
    public readonly Quaternion Rotation;
    public readonly float Left;
    public readonly float Right;
    public readonly float Bottom;
    public readonly float Top;

    public EyeView(
        Vector3 position,
        Quaternion rotation,
        float left,
        float right,
        float bottom,
        float top
    )
    {
        Position = position;
        Rotation = rotation;
        Left = left;
        Right = right;
        Bottom = bottom;
        Top = top;
    }
}

/// <summary>A culling frustum: pose, edge tangents as in <see cref="EyeView"/>, and its clip distances.</summary>
public readonly struct CullView
{
    public readonly Vector3 Position;
    public readonly Quaternion Rotation;
    public readonly float Left;
    public readonly float Right;
    public readonly float Bottom;
    public readonly float Top;
    public readonly float Near;
    public readonly float Far;

    public CullView(
        Vector3 position,
        Quaternion rotation,
        float left,
        float right,
        float bottom,
        float top,
        float near,
        float far
    )
    {
        Position = position;
        Rotation = rotation;
        Left = left;
        Right = right;
        Bottom = bottom;
        Top = top;
        Near = near;
        Far = far;
    }

    /// <summary>The point lies inside, with <paramref name="slack"/> of tangent and relative depth to spare.</summary>
    public bool Contains(Vector3 point, float slack)
    {
        var p = Vector3.Transform(point - Position, Quaternion.Conjugate(Rotation));
        if (!(p.Z > 0f) || p.Z < Near * (1f - slack) || p.Z > Far * (1f + slack))
            return false;
        float x = p.X / p.Z;
        float y = p.Y / p.Z;
        return x >= Left - slack && x <= Right + slack && y >= Bottom - slack && y <= Top + slack;
    }
}

/// <summary>
/// One frustum that holds both eyes, for culling a MultiPass frame once-shaped. Unity's XR display gives each MultiPass
/// eye its own culling pass, so URP culls each eye with that eye's frustum and fits the directional shadow cascades,
/// shadow caster culling, LOD choice and visible lights to it: shadows then land on different texels and cascades in
/// each eye. Culling both eyes with this union makes all of that identical, as single-pass stereo does. The apex sits
/// behind the eyes where their outer edges meet, so the union is no wider than the outer eye edges; every corner of
/// both eye frusta is inside by construction (canted displays only widen it).
/// </summary>
public static class StereoCull
{
    /// <summary>Edge tangents of an OpenGL-style projection from its m00, m02, m11 and m12.</summary>
    public static (float Left, float Right, float Bottom, float Top) Tangents(
        float m00,
        float m02,
        float m11,
        float m12
    )
    {
        if (m00 == 0f)
            throw new ArgumentException("not a perspective projection", nameof(m00));
        if (m11 == 0f)
            throw new ArgumentException("not a perspective projection", nameof(m11));
        return ((m02 - 1f) / m00, (m02 + 1f) / m00, (m12 - 1f) / m11, (m12 + 1f) / m11);
    }

    public static bool TryUnion(
        in EyeView a,
        in EyeView b,
        float near,
        float far,
        out CullView union
    )
    {
        union = default;
        if (!(near > 0f) || !(far > near))
            return false;
        var qb =
            Quaternion.Dot(a.Rotation, b.Rotation) < 0f
                ? Quaternion.Negate(b.Rotation)
                : b.Rotation;
        var rot = Quaternion.Normalize(
            new Quaternion(
                a.Rotation.X + qb.X,
                a.Rotation.Y + qb.Y,
                a.Rotation.Z + qb.Z,
                a.Rotation.W + qb.W
            )
        );
        var inv = Quaternion.Conjugate(rot);
        var mid = (a.Position + b.Position) * 0.5f;
        Span<Vector3> corners = stackalloc Vector3[16];
        Corners(a, inv, mid, near, far, corners.Slice(0, 8));
        Corners(b, inv, mid, near, far, corners.Slice(8, 8));
        var la = Vector3.Transform(a.Position - mid, inv);
        var lb = Vector3.Transform(b.Position - mid, inv);
        bool aLeft = la.X <= lb.X;
        float outLeft = Outer(
            aLeft ? corners.Slice(0, 8) : corners.Slice(8, 8),
            aLeft ? la : lb,
            -1f
        );
        float outRight = Outer(
            aLeft ? corners.Slice(8, 8) : corners.Slice(0, 8),
            aLeft ? lb : la,
            1f
        );
        float spread = outLeft + outRight;
        float back = spread > 1e-3f ? MathF.Abs(lb.X - la.X) / spread : 0f;

        float minZ = float.MaxValue;
        foreach (var c in corners)
            minZ = MathF.Min(minZ, c.Z);
        back = MathF.Max(back, 1e-4f - minZ);

        float left = float.MaxValue,
            right = float.MinValue,
            bottom = float.MaxValue,
            top = float.MinValue,
            nearZ = float.MaxValue,
            farZ = float.MinValue;
        foreach (var c in corners)
        {
            float z = c.Z + back;
            float x = c.X / z;
            float y = c.Y / z;
            left = MathF.Min(left, x);
            right = MathF.Max(right, x);
            bottom = MathF.Min(bottom, y);
            top = MathF.Max(top, y);
            nearZ = MathF.Min(nearZ, z);
            farZ = MathF.Max(farZ, z);
        }
        var position = mid + Vector3.Transform(new Vector3(0f, 0f, -back), rot);
        if (
            !Finite(left + right + bottom + top + nearZ + farZ)
            || !Finite(position.X + position.Y + position.Z)
            || !(left < right)
            || !(bottom < top)
            || !(farZ > nearZ)
        )
            return false;
        union = new CullView(position, rot, left, right, bottom, top, nearZ, farZ);
        return true;
    }

    /// <summary>The eye's near and far corners in the head frame (<paramref name="inv"/> about <paramref name="mid"/>).</summary>
    static void Corners(
        in EyeView e,
        Quaternion inv,
        Vector3 mid,
        float near,
        float far,
        Span<Vector3> into
    )
    {
        int i = 0;
        for (int d = 0; d < 2; d++)
        {
            float z = d == 0 ? near : far;
            for (int h = 0; h < 2; h++)
            for (int v = 0; v < 2; v++)
            {
                var local = new Vector3(
                    (h == 0 ? e.Left : e.Right) * z,
                    (v == 0 ? e.Bottom : e.Top) * z,
                    z
                );
                // Eye minus mid first: far from the origin a world-space corner loses the near plane's millimetres.
                into[i++] = Vector3.Transform(
                    e.Position - mid + Vector3.Transform(local, e.Rotation),
                    inv
                );
            }
        }
    }

    /// <summary>How far an eye sees outward (sign -1 left, +1 right) as a tangent from its own position.</summary>
    static float Outer(ReadOnlySpan<Vector3> corners, Vector3 eye, float sign)
    {
        float best = 0f;
        foreach (var c in corners)
        {
            float z = c.Z - eye.Z;
            if (z > 1e-5f)
                best = MathF.Max(best, sign * (c.X - eye.X) / z);
        }
        return best;
    }

    static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
}
