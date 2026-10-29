namespace PeakXR.Core;

/// <summary>What a hand is doing with the held item.</summary>
public enum HandRole
{
    /// <summary>Not touching the held item: fingers follow the controller.</summary>
    Free,

    /// <summary>The hand the item is attached to.</summary>
    Holding,

    /// <summary>The second hand of a two-hand hold.</summary>
    Support,
}

/// <summary>
/// Procedural hand poses: finger curl presets per grip type and per-joint bend angles. Pure data, so the same
/// shapes are produced for the local player and for remote players whose curls arrive over the network.
/// </summary>
public static class HandPose
{
    public const int JointsPerFinger = 3;

    static readonly FingerCurls Cylinder = new FingerCurls(0.55f, 0.75f, 0.8f, 0.8f, 0.8f);
    static readonly FingerCurls Pinch = new FingerCurls(0.6f, 0.55f, 0.35f, 0.3f, 0.25f);
    static readonly FingerCurls FlatPalm = new FingerCurls(0.2f, 0.15f, 0.15f, 0.15f, 0.15f);
    static readonly FingerCurls Pole = new FingerCurls(0.7f, 0.85f, 0.9f, 0.9f, 0.9f);

    // Bend in degrees per joint at curl 1, a natural fist, about each joint's bend axis (FingerJoints). The scout's
    // fingers fan out of one point in the palm (joint 0), then bend at the knuckle (1) and once more (2): the game's
    // own fist (A_Scout_Idle1, A_Scout_Emote_Fist) turns them 24/51/122 degrees. The thumb swings in front of the palm
    // (0), then folds across it over the curled fingers (1, 2).
    static readonly float[] FingerMax = { 24f, 51f, 122f };
    static readonly float[] ThumbMax = { 25f, 55f, 50f };

    /// <summary>The scout's middle finger, knuckle to tip, metres (Character prefab, Middle_1_R to
    /// Middle_3_R_end).</summary>
    public const float FingerLength = 0.134f;

    /// <summary>
    /// Curl at which fingers hug a held item of <paramref name="radius"/> metres instead of passing into it: a
    /// finger laid round a cylinder bends through its length over the radius (radians), out of the fingers' whole
    /// bend. 1 (no limit) for a handle thinner than a fist, or an unknown size.
    /// </summary>
    public static float WrapCurl(float radius)
    {
        if (radius <= 0f)
            return 1f;
        float total = (FingerMax[0] + FingerMax[1] + FingerMax[2]) * UnityQuat.Deg2Rad;
        return FingerCurlMapper.Clamp01(FingerLength / radius / total);
    }

    public static FingerCurls Preset(GripPreset grip)
    {
        switch (grip)
        {
            case GripPreset.Pinch:
                return Pinch;
            case GripPreset.FlatPalm:
                return FlatPalm;
            case GripPreset.TwoHandPole:
                return Pole;
            default:
                return Cylinder;
        }
    }

    /// <summary>
    /// Curls to show for a hand. A free hand mirrors the controller. A holding hand takes the item's grip
    /// preset, and the index may curl further with the trigger so using the item still shows. A supporting
    /// hand wraps a pole or, for anything else, cups it with a cylinder grip. Either stops at
    /// <paramref name="wrap"/> (<see cref="WrapCurl"/> of the item's size), where the fingers meet its surface.
    /// </summary>
    public static FingerCurls Fingers(
        FingerCurls controller,
        HandRole role,
        GripPreset grip,
        float wrap = 1f
    )
    {
        if (role == HandRole.Free)
            return controller;
        var preset =
            role == HandRole.Support && grip != GripPreset.TwoHandPole ? Cylinder : Preset(grip);
        if (controller.Index > preset.Index)
            preset.Index = controller.Index;
        return Wrap(preset, wrap);
    }

    /// <summary><paramref name="curls"/> with every finger stopped at <paramref name="wrap"/>.</summary>
    public static FingerCurls Wrap(FingerCurls curls, float wrap)
    {
        for (int f = 0; f < FingerCurls.Count; f++)
        {
            if (curls[f] > wrap)
                curls[f] = wrap;
        }
        return curls;
    }

    public static float MaxJointAngle(int finger, int joint) =>
        finger == 0 ? ThumbMax[joint] : FingerMax[joint];

    /// <summary>Bend in degrees of one joint (0 = proximal) for a curl in [0, 1].</summary>
    public static float JointAngle(int finger, int joint, float curl) =>
        FingerCurlMapper.Clamp01(curl) * MaxJointAngle(finger, joint);
}
