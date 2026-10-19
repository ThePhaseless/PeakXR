namespace PeakXR.Avatar;

/// <summary>
/// How the local VR player holds the current item. Written by the physical interaction feature, read when the
/// local pose is built. Defaults (right hand, one-handed) match vanilla when that feature is off.
/// </summary>
internal static class LocalHold
{
    public static bool ItemInLeftHand;
    public static bool TwoHand;

    public static void Reset()
    {
        ItemInLeftHand = false;
        TwoHand = false;
    }
}
