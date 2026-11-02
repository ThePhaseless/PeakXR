namespace PeakXR.Core;

/// <summary>What the controllers do this frame on the game's rebinding prompt.</summary>
public enum RebindPromptStep
{
    None,
    Cancel,
    RefuseLeft,
    RefuseRight,
}

/// <summary>
/// The controllers on the game's rebinding prompt, which takes only a keyboard key or gamepad button: each new press
/// is refused with a cue, and a controller-only player leaves with a Menu tap or, where Menu belongs to the runtime
/// (Oculus Touch under SteamVR), by keeping the dashboard open for <see cref="DashboardSeconds"/>. A headset coming off
/// and going back on passes through the dashboard's session state, and a player lifting the headset to reach the
/// keyboard is still answering the prompt: the wait covers the way off, and from the headset off the dashboard counts
/// only once it has been seen closed. Controls held as the prompt opens are no press.
/// </summary>
public sealed class RebindPromptInput
{
    public const float DashboardSeconds = 1f;

    private bool watching;
    private bool left;
    private bool right;

    /// <summary>How long the dashboard has been open; negative until the prompt has seen it closed with the headset on.</summary>
    private float dashboardFor = -1f;

    /// <summary>The prompt is not up: the next <see cref="Step"/> starts over.</summary>
    public void Reset()
    {
        watching = false;
        dashboardFor = -1f;
    }

    /// <summary>One frame on the prompt. Two hands pressing together are refused on consecutive frames.</summary>
    public RebindPromptStep Step(
        bool menuTap,
        bool dashboardOpen,
        bool headsetOff,
        bool leftDown,
        bool rightDown,
        float dt
    )
    {
        left = leftDown && (left || !watching);
        right = rightDown && (right || !watching);
        watching = true;
        if (headsetOff)
            dashboardFor = -1f;
        else if (!dashboardOpen)
            dashboardFor = 0f;
        else if (dashboardFor >= 0f)
            dashboardFor += dt;
        if (menuTap || dashboardFor >= DashboardSeconds)
            return RebindPromptStep.Cancel;
        if (leftDown && !left)
        {
            left = true;
            return RebindPromptStep.RefuseLeft;
        }
        if (rightDown && !right)
        {
            right = true;
            return RebindPromptStep.RefuseRight;
        }
        return RebindPromptStep.None;
    }
}
