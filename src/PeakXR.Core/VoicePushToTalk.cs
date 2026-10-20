namespace PeakXR.Core;

/// <summary>
/// Merges the VR push-to-talk button into the game's <c>pushToTalkPressed</c> field after the game
/// sampled its own input. Kept pure because of one trap: the game only re-reads push-to-talk while
/// movement input is allowed, so a VR press released during a menu would otherwise stay "held".
/// </summary>
public static class VoicePushToTalk
{
    /// <param name="vanilla">Field value after the game's own sampling.</param>
    /// <param name="held">VR push-to-talk held this frame (button or gesture).</param>
    /// <param name="movementActive">Whether the game re-read push-to-talk this frame.</param>
    /// <param name="forced">State between frames: true while the field holds a value we wrote.</param>
    /// <returns>The value to store back into the field.</returns>
    public static bool Merge(bool vanilla, bool held, bool movementActive, ref bool forced)
    {
        if (held)
        {
            forced = true;
            return true;
        }
        if (!forced)
            return vanilla;
        forced = false;
        return movementActive && vanilla;
    }
}
