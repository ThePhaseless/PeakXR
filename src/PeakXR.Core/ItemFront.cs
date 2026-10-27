using System;
using System.Collections.Generic;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// The side of a held item a person turns toward their eyes to look at it: a face, a dial, a cover or a label. The
/// game's own hold shows it, since it poses each item before the camera by <c>Item.defaultPos</c> and
/// <c>defaultForward</c> (CharacterRagdoll.cs:230-245), but in VR the player's own hand carries the item. The fronts
/// come from offline data (docs/research/item-fronts.csv). Items whose use fixes how they are held (launchers, bugles,
/// torches, the parasol) and items with no side worth showing are left out and keep the authored hold, as does any
/// item a future game version adds.
/// </summary>
public static class ItemFront
{
    /// <summary>A grip this near the item's drawn middle turns the item freely about it, metres.</summary>
    public const float CentredGrip = 0.02f;

    /// <summary>How far the eyes are from the controller in the natural hold, metres (<see cref="EyePoint"/>).</summary>
    public const float ViewDistance = 0.45f;

    /// <summary>
    /// How far, in degrees, the item may tilt about the grip on top of its spin about the grip-to-middle line. A dial
    /// held by its rim faces along that line or across the arm, so no spin alone shows it: the compass reached a dot
    /// of 0.69 with the eyes in run 15, 0.92 offline with this budget.
    /// </summary>
    public const float MaxTilt = 60f;

    /// <summary>Spin-then-tilt passes: each tilt moves the item's middle, so the way from it to the eyes moves too.</summary>
    private const int Passes = 3;

    /// <summary>Item-frame front of each item that has one, by prefab name (generated from
    /// docs/research/item-fronts.csv): the face's own normal for a flat front (a dial, a medallion, a cover), which
    /// the game shows tilted (the compass dial 19 degrees, the amulets about 50), else the side the game presents.</summary>
    private static readonly Dictionary<string, Vector3> Fronts = new(StringComparer.Ordinal)
    {
        ["Airplane Food"] = new(0.015f, 0.568f, -0.823f),
        ["Amulet_Clone"] = new(-0.023f, 0.763f, -0.646f),
        ["Amulet_Healing"] = new(-0.185f, 0.685f, -0.705f),
        ["Amulet_InfiniteStamina"] = new(0.001f, 0.793f, -0.609f),
        ["Amulet_SuperJump"] = new(-0.087f, 0.772f, -0.629f),
        ["AncientIdol"] = new(0.000f, 0.000f, -1.000f),
        ["Antidote"] = new(0.000f, 0.000f, -1.000f),
        ["Backpack"] = new(0.000f, 0.233f, -0.972f),
        ["Bandages"] = new(0.000f, 0.000f, -1.000f),
        ["BingBong"] = new(0.000f, 0.000f, -1.000f),
        ["BingBong_Prop Variant"] = new(0.000f, 0.000f, -1.000f),
        ["BookOfBones"] = new(-0.006f, 0.471f, -0.882f),
        ["Cheat Compass"] = new(0.001f, 0.456f, -0.890f),
        ["Cheat Compass 1"] = new(0.001f, 0.456f, -0.890f),
        ["Compass"] = new(0.014f, 0.327f, -0.945f),
        ["Cursed Skull"] = new(0.000f, -0.196f, -0.981f),
        ["Energy Drink"] = new(0.000f, 0.100f, -0.995f),
        ["Fannypack"] = new(0.000f, 0.243f, -0.970f),
        ["FirstAidKit"] = new(0.006f, 0.302f, -0.953f),
        ["FortifiedMilk"] = new(0.000f, 0.000f, -1.000f),
        ["Granola Bar"] = new(0.003f, -0.003f, -1.000f),
        ["Guidebook"] = new(-0.002f, 0.000f, -1.000f),
        ["GuidebookPage"] = new(0.000f, 0.000f, -1.000f),
        ["GuidebookPage_0_Intro"] = new(0.000f, 0.000f, -1.000f),
        ["GuidebookPage_10_Sleepy"] = new(0.000f, 0.000f, -1.000f),
        ["GuidebookPage_11_Awake"] = new(0.000f, 0.000f, -1.000f),
        ["GuidebookPage_12_Crashout"] = new(0.000f, 0.000f, -1.000f),
        ["GuidebookPage_13_FirstTeams"] = new(0.000f, 0.000f, -1.000f),
        ["GuidebookPage_1_Mushrooms"] = new(0.000f, 0.000f, -1.000f),
        ["GuidebookPage_2_Campfire"] = new(0.000f, 0.000f, -1.000f),
        ["GuidebookPage_3_Revival"] = new(0.000f, 0.000f, -1.000f),
        ["GuidebookPage_4_Poison"] = new(0.000f, 0.000f, -1.000f),
        ["GuidebookPage_5_Zombies"] = new(0.000f, 0.000f, -1.000f),
        ["GuidebookPage_6_BodyHeat"] = new(0.000f, 0.000f, -1.000f),
        ["GuidebookPage_7_BurningSun"] = new(0.000f, 0.000f, -1.000f),
        ["GuidebookPage_8_Magma"] = new(0.000f, 0.000f, -1.000f),
        ["GuidebookPage_9_Gloom"] = new(0.000f, 0.000f, -1.000f),
        ["Heat Pack"] = new(0.000f, 0.406f, -0.914f),
        ["Item_Coconut_half"] = new(0.000f, 0.000f, -1.000f),
        ["Jetpack"] = new(0.000f, 0.000f, -1.000f),
        ["PandorasBox"] = new(0.000f, 0.000f, -1.000f),
        ["Parachute"] = new(-0.062f, 0.488f, -0.871f),
        ["Passport"] = new(0.000f, 0.000f, -1.000f),
        ["Pirate Compass"] = new(0.002f, 0.318f, -0.948f),
        ["Rocketpack"] = new(0.000f, 0.233f, -0.972f),
        ["ScoutCookies"] = new(0.001f, 0.213f, -0.977f),
        ["ScoutCookies_Vanilla"] = new(0.001f, 0.213f, -0.977f),
        ["Sports Drink"] = new(0.000f, 0.000f, -1.000f),
        ["TrailMix"] = new(-0.036f, 0.001f, -0.999f),
        ["Warp Compass"] = new(0.001f, 0.456f, -0.890f),
    };

    /// <summary>Every front in the table (tests).</summary>
    public static IEnumerable<Vector3> All => Fronts.Values;

    /// <summary>The prefab name of an item instance: <c>BingBong(Clone)</c> is <c>BingBong</c>.</summary>
    public static string Key(string name)
    {
        const string clone = "(Clone)";
        if (name != null && name.EndsWith(clone, StringComparison.Ordinal))
            name = name.Substring(0, name.Length - clone.Length).TrimEnd();
        return name;
    }

    /// <summary>The item-frame front of the item named <paramref name="name"/> (an instance or prefab name).</summary>
    public static bool TryGet(string name, out Vector3 front)
    {
        front = default;
        var key = Key(name);
        return key != null && Fronts.TryGetValue(key, out front);
    }

    /// <summary>
    /// Direction from the fist to the eyes in the controller's grip frame (<see cref="GripFrame"/>), for a hand held
    /// up before the face to look at what it holds: the eyes lie back along the forearm, a little above the fist and
    /// toward the body's middle (the palm's side). One fixed pose, so the hold never changes as the wrist turns and
    /// every peer draws it alike.
    /// </summary>
    public static Vector3 EyeInGrip(bool left) =>
        Vector3.Normalize(
            0.3f * GripFrame.Palm(left) + 0.94f * GripFrame.Forearm + 0.12f * GripFrame.ThumbSide
        );

    /// <summary>Where the eyes are in the controller's frame in the natural hold: <see cref="ViewDistance"/> along
    /// <see cref="EyeInGrip"/>.</summary>
    public static Vector3 EyePoint(bool left) => ViewDistance * EyeInGrip(left);

    /// <summary>The controller rotation of the natural hold (self-test vr.245): <see cref="EyeInGrip"/> along
    /// <paramref name="toEyes"/>, the handle's thumb end (<see cref="GripFrame.ThumbSide"/>) as near
    /// <paramref name="up"/> as that allows.</summary>
    public static Quaternion NaturalGrip(Vector3 toEyes, Vector3 up, bool left)
    {
        var e = EyeInGrip(left);
        var g2 = Vector3.Normalize(GripFrame.ThumbSide - Vector3.Dot(GripFrame.ThumbSide, e) * e);
        var w1 = Vector3.Normalize(toEyes);
        var w2 = Vector3.Normalize(up - Vector3.Dot(up, w1) * w1);
        var grip = Frame(e, g2);
        var world = Frame(w1, w2);
        return Quaternion.Normalize(UnityQuat.Mul(world, Quaternion.Inverse(grip)));

        // The rotation taking x to a and y to b (a, b orthonormal).
        static Quaternion Frame(Vector3 a, Vector3 b)
        {
            var c = Vector3.Cross(a, b);
            var m = new Matrix4x4(
                a.X,
                a.Y,
                a.Z,
                0f,
                b.X,
                b.Y,
                b.Z,
                0f,
                c.X,
                c.Y,
                c.Z,
                0f,
                0f,
                0f,
                0f,
                1f
            );
            return Quaternion.CreateFromRotationMatrix(m);
        }
    }

    /// <summary>
    /// Turns a held item, posed at <paramref name="pos"/>/<paramref name="rot"/> in the hand's frame, so its
    /// <paramref name="front"/> faces <paramref name="eyePoint"/> (hand frame) as seen from the item's middle: first a
    /// spin about the line from the grip (<paramref name="pivot"/>, item frame) through the middle
    /// (<paramref name="centre"/>), which leaves the item's body where the hand has it, then a tilt about the grip of
    /// at most <paramref name="maxTilt"/> degrees in all for what the spin cannot reach. The grip stays on the hand.
    /// The middle is where it is drawn: <paramref name="drawnScale"/> of the way out from the grip (HeldItemScale).
    /// An item centred on its grip turns straight to the eyes.
    /// </summary>
    public static void Turn(
        Vector3 pos,
        Quaternion rot,
        Vector3 pivot,
        Vector3 centre,
        float drawnScale,
        Vector3 front,
        Vector3 eyePoint,
        float maxTilt,
        out Vector3 turnedPos,
        out Quaternion turnedRot
    )
    {
        var grip = pos + UnityQuat.Rotate(rot, pivot);
        var armLocal = centre - pivot;
        bool centred = armLocal.Length() < CentredGrip;
        float budget = centred ? MathF.PI : maxTilt * UnityQuat.Deg2Rad;
        for (int pass = 0; pass < Passes; pass++)
        {
            if (!centred)
            {
                var arm = UnityQuat.Rotate(rot, armLocal);
                var axis = Vector3.Normalize(arm);
                var e = ToEyes(rot);
                var f = UnityQuat.Rotate(rot, front);
                var fa = f - Vector3.Dot(f, axis) * axis;
                var ea = e - Vector3.Dot(e, axis) * axis;
                if (fa.LengthSquared() > 1e-8f && ea.LengthSquared() > 1e-8f)
                {
                    float spin = MathF.Atan2(
                        Vector3.Dot(Vector3.Cross(fa, ea), axis),
                        Vector3.Dot(fa, ea)
                    );
                    rot = UnityQuat.Mul(Quaternion.CreateFromAxisAngle(axis, spin), rot);
                }
            }
            if (budget <= 0f)
                continue;
            var to = ToEyes(rot);
            var now = UnityQuat.Rotate(rot, front);
            var cross = Vector3.Cross(now, to);
            float angle = MathF.Atan2(cross.Length(), Vector3.Dot(now, to));
            float tilt = MathF.Min(angle, budget);
            if (tilt < 1e-5f || cross.LengthSquared() < 1e-12f)
                continue;
            rot = UnityQuat.Mul(
                Quaternion.CreateFromAxisAngle(Vector3.Normalize(cross), tilt),
                rot
            );
            budget -= tilt;
        }
        turnedRot = Quaternion.Normalize(rot);
        turnedPos = grip - UnityQuat.Rotate(turnedRot, pivot);

        Vector3 ToEyes(Quaternion r) =>
            Vector3.Normalize(eyePoint - (grip + drawnScale * UnityQuat.Rotate(r, armLocal)));
    }
}
