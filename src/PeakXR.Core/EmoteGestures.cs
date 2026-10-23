namespace PeakXR.Core;

/// <summary>
/// Emotes acted out with the hands instead of picked from the wheel (D5): a raised open hand swung side to side
/// waves, both open hands held over the head cheer. Also the emote wheel's own gate, so a refused open gets a cue.
/// </summary>
public static class EmoteGestures
{
    /// <summary>A hand counts as raised once it is above <paramref name="headY"/> plus <paramref name="margin"/>
    /// (negative: below the eyes, about shoulder-to-chin height).</summary>
    public static bool Raised(float handY, float headY, float margin) => handY > headY + margin;

    /// <summary>
    /// A thumbs-up on a controller: the grip closed and the index on the trigger (a fist), the thumb lifted off every
    /// button, and the thumb side of the hand (<paramref name="thumbUp"/>: the controller's up axis dotted with world
    /// up) pointing up.
    /// </summary>
    public static bool ThumbsUp(in FingerCurls c, float thumbUp) =>
        c.Thumb < 0.3f
        && c.Index >= FingerCurlMapper.TriggerTouchCurl
        && c.Middle > 0.6f
        && c.Ring > 0.6f
        && c.Pinky > 0.5f
        && thumbUp > 0.7f;

    /// <summary>A thumbs-up is held at least this far in front of the eyes (flat), clear of a fist at your side.</summary>
    public const float ThumbReach = 0.3f;

    /// <summary>...and at most this far below the eyes: chest height or above, not a fist at the hip.</summary>
    public const float ThumbDrop = 0.45f;

    /// <summary>...and slower than this (m/s) for the whole dwell: a swing or a reach is not a gesture.</summary>
    public const float StillSpeed = 0.3f;

    /// <summary>How long the pose is held before the emote plays.</summary>
    public const float ThumbDwell = 0.6f;

    /// <summary>
    /// The cheer pose this frame: both open hands over the head, or one-handed (<c>Accessibility.OneHanded</c>) one open
    /// hand over the head held still (<paramref name="oneHandSpeed"/> m/s at most <see cref="StillSpeed"/>), so a wave,
    /// which swings, never reads as a cheer.
    /// </summary>
    public static bool CheerPose(bool oneHanded, bool leftUp, bool rightUp, float oneHandSpeed) =>
        oneHanded ? (leftUp || rightUp) && oneHandSpeed <= StillSpeed : leftUp && rightUp;

    /// <summary>
    /// The body is doing something with its hands (climbing, on a rope or vine, carrying a friend, a grip that took or
    /// started something): any hand pose now is part of that, not an emote. Emotes are seen by every peer, so a false
    /// one is broadcast.
    /// </summary>
    public static bool Busy(bool climbingAnything, bool carrying, bool midGrab) =>
        climbingAnything || carrying || midGrab;

    /// <summary>
    /// A deliberate thumbs-up this frame: the <see cref="ThumbsUp"/> pose, on a controller that reports thumb touch
    /// (<paramref name="thumbSensed"/>; without the sensor every fist reads as a lifted thumb), no item held, the body
    /// not <see cref="Busy"/>, the hand held out in front (<paramref name="forward"/> metres ahead of the eyes,
    /// <paramref name="belowEyes"/> metres under them) and still (<paramref name="speed"/> m/s).
    /// </summary>
    public static bool ThumbIntent(
        bool pose,
        bool thumbSensed,
        bool holdingItem,
        bool busy,
        float forward,
        float belowEyes,
        float speed
    ) =>
        pose
        && thumbSensed
        && !holdingItem
        && !busy
        && forward >= ThumbReach
        && belowEyes <= ThumbDrop
        && speed <= StillSpeed;

    /// <summary>
    /// Why the game will not open the emote wheel (GUIManager.UpdateEmoteWheel, A/GUIManager.cs:561-569, and
    /// canEmote :312-322), or <see cref="DenyReason.None"/> when it opens.
    /// </summary>
    public static DenyReason WheelRefusal(
        bool dead,
        bool won,
        bool climbing,
        bool otherWheel,
        bool windowBlocking
    )
    {
        if (climbing && !dead && !won)
            return DenyReason.NotWhileClimbing;
        return dead || won || climbing || otherWheel || windowBlocking
            ? DenyReason.NotNow
            : DenyReason.None;
    }
}

/// <summary>
/// A wave: the raised hand swings side to side at least <see cref="Swings"/> times, each swing at least
/// <see cref="Amplitude"/> metres, all within <see cref="Window"/> seconds. Feed it the hand's sideways position
/// in the head's frame every frame. Allocation-free.
/// </summary>
public sealed class WaveGesture
{
    public float Amplitude = 0.07f;
    public int Swings = 3;
    public float Window = 1.6f;

    private bool tracking;
    private float low,
        high,
        extreme,
        sinceFirst;
    private int direction,
        swings;

    /// <returns>True on the frame the wave completes.</returns>
    public bool Update(bool raised, float lateral, float dt)
    {
        if (!raised)
        {
            Reset();
            return false;
        }
        if (swings > 0)
        {
            sinceFirst += dt;
            if (sinceFirst > Window)
                Reset();
        }
        if (!tracking)
        {
            tracking = true;
            low = high = lateral;
            return false;
        }
        if (direction == 0)
        {
            low = lateral < low ? lateral : low;
            high = lateral > high ? lateral : high;
            if (high - low < Amplitude)
                return false;
            direction = lateral >= high ? 1 : -1;
            extreme = lateral;
            swings = 1;
            sinceFirst = 0f;
        }
        else if (direction > 0)
        {
            if (lateral > extreme)
                extreme = lateral;
            else if (extreme - lateral >= Amplitude)
                Turn(-1, lateral);
        }
        else if (lateral < extreme)
            extreme = lateral;
        else if (lateral - extreme >= Amplitude)
            Turn(1, lateral);

        if (swings < Swings)
            return false;
        Reset();
        return true;
    }

    private void Turn(int to, float lateral)
    {
        direction = to;
        extreme = lateral;
        swings++;
    }

    public void Reset()
    {
        tracking = false;
        direction = swings = 0;
        sinceFirst = 0f;
    }
}

/// <summary>A pose held for <see cref="Dwell"/> seconds (both hands raised for a cheer, a thumbs-up). Fires once,
/// then waits for the pose to break before it can fire again.</summary>
public sealed class DwellGesture
{
    public float Dwell = 0.4f;

    private float held;
    private bool fired;

    /// <returns>True on the frame the cheer completes.</returns>
    public bool Update(bool bothRaised, float dt)
    {
        if (!bothRaised)
        {
            Reset();
            return false;
        }
        if (fired)
            return false;
        held += dt;
        if (held < Dwell)
            return false;
        fired = true;
        return true;
    }

    public void Reset()
    {
        held = 0f;
        fired = false;
    }
}
