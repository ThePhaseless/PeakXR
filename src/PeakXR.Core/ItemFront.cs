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

    /// <summary>Item-frame front of each item that has one, by prefab name: the side the game's hold shows the camera
    /// (generated from docs/research/item-fronts.csv).</summary>
    private static readonly Dictionary<string, Vector3> Fronts = new(StringComparer.Ordinal)
    {
        ["Airplane Food"] = new(0.000f, 0.000f, -1.000f),
        ["Amulet_Clone"] = new(0.000f, 0.447f, -0.894f),
        ["Amulet_Healing"] = new(0.000f, 0.000f, -1.000f),
        ["Amulet_InfiniteStamina"] = new(0.000f, 0.000f, -1.000f),
        ["Amulet_SuperJump"] = new(0.000f, 0.000f, -1.000f),
        ["AncientIdol"] = new(0.000f, 0.000f, -1.000f),
        ["Antidote"] = new(0.000f, 0.000f, -1.000f),
        ["Backpack"] = new(0.000f, 0.233f, -0.972f),
        ["Bandages"] = new(0.000f, 0.000f, -1.000f),
        ["BingBong"] = new(0.000f, 0.000f, -1.000f),
        ["BingBong_Prop Variant"] = new(0.000f, 0.000f, -1.000f),
        ["BookOfBones"] = new(0.000f, 0.000f, -1.000f),
        ["Cheat Compass"] = new(0.000f, -0.316f, -0.949f),
        ["Cheat Compass 1"] = new(0.000f, -0.316f, -0.949f),
        ["Compass"] = new(0.000f, 0.000f, -1.000f),
        ["Cursed Skull"] = new(0.000f, -0.196f, -0.981f),
        ["Energy Drink"] = new(0.000f, 0.100f, -0.995f),
        ["Fannypack"] = new(0.000f, 0.243f, -0.970f),
        ["FirstAidKit"] = new(0.000f, 0.000f, -1.000f),
        ["FortifiedMilk"] = new(0.000f, 0.000f, -1.000f),
        ["Granola Bar"] = new(0.000f, 0.000f, -1.000f),
        ["Guidebook"] = new(0.000f, 0.000f, -1.000f),
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
        ["Heat Pack"] = new(0.000f, 0.000f, -1.000f),
        ["Item_Coconut_half"] = new(0.000f, 0.000f, -1.000f),
        ["Jetpack"] = new(0.000f, 0.000f, -1.000f),
        ["PandorasBox"] = new(0.000f, 0.000f, -1.000f),
        ["Parachute"] = new(0.000f, 0.000f, -1.000f),
        ["Passport"] = new(0.000f, 0.000f, -1.000f),
        ["Pirate Compass"] = new(0.000f, 0.000f, -1.000f),
        ["Rocketpack"] = new(0.000f, 0.233f, -0.972f),
        ["ScoutCookies"] = new(0.000f, -0.287f, -0.958f),
        ["ScoutCookies_Vanilla"] = new(0.000f, -0.287f, -0.958f),
        ["Sports Drink"] = new(0.000f, 0.000f, -1.000f),
        ["TrailMix"] = new(0.000f, 0.000f, -1.000f),
        ["Warp Compass"] = new(0.000f, -0.316f, -0.949f),
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

    /// <summary>
    /// Turns a held item, posed at <paramref name="pos"/>/<paramref name="rot"/> in the hand's frame, so its
    /// <paramref name="front"/> faces <paramref name="eye"/> as nearly as a turn that leaves it in the palm can: about
    /// the line from the grip (<paramref name="pivot"/>, item frame) through the item's drawn middle
    /// (<paramref name="centre"/>), so the grip and the item's body stay where the hand has them. An item centred on
    /// its grip turns straight to the eyes.
    /// </summary>
    public static void Turn(
        Vector3 pos,
        Quaternion rot,
        Vector3 pivot,
        Vector3 centre,
        Vector3 front,
        Vector3 eye,
        out Vector3 turnedPos,
        out Quaternion turnedRot
    )
    {
        var f = UnityQuat.Rotate(rot, front);
        var arm = UnityQuat.Rotate(rot, centre - pivot);
        Quaternion turn;
        if (arm.Length() < CentredGrip)
            turn = GripMath.FromTo(f, eye);
        else
        {
            var axis = Vector3.Normalize(arm);
            var fa = f - Vector3.Dot(f, axis) * axis;
            var ea = eye - Vector3.Dot(eye, axis) * axis;
            float angle =
                fa.LengthSquared() < 1e-8f || ea.LengthSquared() < 1e-8f
                    ? 0f
                    : MathF.Atan2(Vector3.Dot(Vector3.Cross(fa, ea), axis), Vector3.Dot(fa, ea));
            turn = Quaternion.CreateFromAxisAngle(axis, angle);
        }
        var grip = pos + UnityQuat.Rotate(rot, pivot);
        turnedRot = Quaternion.Normalize(UnityQuat.Mul(turn, rot));
        turnedPos = grip + UnityQuat.Rotate(turn, pos - grip);
    }
}
