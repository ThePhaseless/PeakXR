namespace PeakXR.Core;

/// <summary>
/// OpenXR session states (XrSessionState, OpenXR spec) and which of them give the app input focus. Named in the
/// logs because a session that never reaches FOCUSED pauses input and holds the 0.6 focus fade: every step
/// that needs input then fails for that one reason.
/// </summary>
public static class XRFocus
{
    /// <summary>FOCUSED (5), or 0 before the runtime reports a state.</summary>
    public static bool Focused(int state) => state is 0 or 5;

    /// <summary>VISIBLE (4) or FOCUSED (5), or 0 before the runtime reports a state: the only states whose frames the
    /// headset shows (XrFrameState.shouldRender). In the others Unity's OpenXR provider stops filling render passes
    /// and, once the session ends (headset off), keeps handing out the last frame's swapchain image, which belongs to
    /// the runtime again.</summary>
    public static bool Shown(int state) => state is 0 or 4 or 5;

    /// <summary>
    /// The wear sensor says the headset came off: true from a worn to not-worn reading until it reads worn again (or
    /// stops reporting). Only a sensor that has read worn can pause, so a driver stuck at "not worn" never freezes play.
    /// </summary>
    /// <param name="paused">The last result.</param>
    /// <param name="was">The previous reading (null: not reported).</param>
    /// <param name="now">This reading.</param>
    public static bool TakenOff(bool paused, bool? was, bool? now) =>
        now == false && (paused || was == true);

    /// <summary>Frame rate the game paces itself to while the headset shows none of its frames (<see cref="Shown"/>
    /// false): the runtime stops pacing then and the desktop mirror is black, so a low rate only saves the GPU.</summary>
    public const int IdleHz = 30;

    /// <summary>Milliseconds to sleep so a frame that took <paramref name="elapsedSeconds"/> lasts 1/<see cref="IdleHz"/> s.</summary>
    public static int IdleSleepMs(double elapsedSeconds) =>
        (int)((1.0 / IdleHz - System.Math.Clamp(elapsedSeconds, 0.0, 1.0 / IdleHz)) * 1000.0);

    public static string Name(int state) =>
        state switch
        {
            0 => "not reported",
            1 => "IDLE",
            2 => "READY",
            3 => "SYNCHRONIZED",
            4 => "VISIBLE",
            5 => "FOCUSED",
            6 => "STOPPING",
            7 => "LOSS_PENDING",
            8 => "EXITING",
            _ => "state " + state,
        };
}
