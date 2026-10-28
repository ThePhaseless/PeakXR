using PeakXR.Core;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.XRInput;

/// <summary>
/// The both-sticks chord (Core <see cref="StickChord"/>), owned by input: sprint and crouch read their stick clicks
/// through its window (<see cref="Click"/>) and rig.camera's recenter reads its hold (<see cref="Held"/>), so neither
/// depends on the diagnostics feature. Updated by <see cref="XRInputSampler"/> right after each VR sample, before
/// <c>AfterSample</c>; debug.report only reads it (<see cref="ReportNow"/>, the report hold's progress) and tells it
/// when its Menu chord holds a stick (<see cref="MenuChordActive"/>).
/// </summary>
internal static class StickChordInput
{
    /// <summary>The chord's window: long enough for two thumbs pressed "together", short enough that a lone sprint or
    /// crouch click does not feel late.</summary>
    private const float ChordWindow = 0.18f;

    internal static readonly StickChord Sticks = new();

    /// <summary>debug.report's Menu chord holds a stick (set after its own update, read the next frame): the sticks
    /// chord does not start meanwhile, so one gesture never saves two reports.</summary>
    internal static bool MenuChordActive;

    /// <summary>This frame the sticks were held through the recenter and the report hold: save a report.</summary>
    internal static bool ReportNow { get; private set; }

    /// <summary>The stick click the controller layout reads: held back for the chord window, never while a chord
    /// holds it.</summary>
    public static bool Click(bool left) => Sticks.Click(left);

    /// <summary>This frame the chord owns that controller's stick click (its release frame included).</summary>
    public static bool Taken(bool left) => Sticks.StickTaken(left);

    /// <summary>Both stick clicks are held as one chord: rig.camera's recenter hold.</summary>
    public static bool Held => Sticks.Active;

    internal static void Update()
    {
        Sticks.RecenterSeconds = Rig.CameraFeature.RecenterSeconds;
        // One-handed play has one stick: nothing to wait for.
        Sticks.Window = VRInput.OneHanded ? 0f : ChordWindow;
        ReportNow =
            Sticks.Update(
                VRInput.Left.StickClick,
                VRInput.Right.StickClick,
                MenuChordActive,
                Time.unscaledDeltaTime
            ) == BugChordEvent.Report;
    }
}
