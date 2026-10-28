using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// Points of a held item read from its render bounds in its own frame, so a gesture's trigger point is the part of
/// the item that does the touching (a bugle's mouthpiece, a flare's tip, the food itself) rather than the
/// controller, and items added by later game versions get one too.
/// </summary>
public static class HeldItemShape
{
    /// <summary>Centre of the bounds face that ends the longest side, at the end farther from
    /// <paramref name="from"/>: a bugle's mouthpiece away from its bell, a flare's tip away from its grip.</summary>
    public static Vector3 FarEnd(Vector3 min, Vector3 max, Vector3 from)
    {
        var size = max - min;
        var end = (min + max) * 0.5f;
        if (size.X >= size.Y && size.X >= size.Z)
            end.X = from.X > end.X ? min.X : max.X;
        else if (size.Y >= size.Z)
            end.Y = from.Y > end.Y ? min.Y : max.Y;
        else
            end.Z = from.Z > end.Z ? min.Z : max.Z;
        return end;
    }

    /// <summary>Centre of the box's top (+Y) face: a drink's opening. Every drink the game has stands along its own
    /// +Y with the neck, cap or carton ridge on top (within 2 cm of this point) and the grip off-centre by as little
    /// as 2 cm, so the model's up axis is surer than the end away from the grip.</summary>
    public static Vector3 Opening(Vector3 min, Vector3 max) =>
        new((min.X + max.X) * 0.5f, max.Y, (min.Z + max.Z) * 0.5f);

    /// <summary>The point of the box nearest <paramref name="p"/>; <paramref name="p"/> itself when inside.</summary>
    public static Vector3 Closest(Vector3 min, Vector3 max, Vector3 p) =>
        Vector3.Clamp(p, min, max);

    /// <summary>The box drawn <paramref name="scale"/> times its size about <paramref name="pivot"/> (the grip).</summary>
    public static void ScaleAbout(ref Vector3 min, ref Vector3 max, Vector3 pivot, float scale)
    {
        min = HeldBookSize.ScaleAbout(min, pivot, scale);
        max = HeldBookSize.ScaleAbout(max, pivot, scale);
    }
}
