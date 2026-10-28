namespace PeakXR.Core;

/// <summary>
/// Hold-to-use binoculars: the game only toggles them on a press, so releasing the use button must issue the
/// lowering toggle itself. Lowering through the game's own toggle keeps its network sync vanilla.
/// </summary>
public static class BinocularHold
{
    /// <summary>True when the lowering toggle should run this frame.</summary>
    /// <param name="hold">The activation setting is Hold.</param>
    /// <param name="raised">The binoculars are raised.</param>
    /// <param name="heldLocally">They are in the local player's hand; the toggle is only ours to send then.</param>
    /// <param name="usePressed">The game's primary use input is held (false once VR input stops).</param>
    public static bool ShouldLower(bool hold, bool raised, bool heldLocally, bool usePressed) =>
        hold && raised && heldLocally && !usePressed;
}
