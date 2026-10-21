namespace PeakXR.Core;

/// <summary>
/// Why a VR action did nothing. In VR the hands make actions look possible that the game refuses (it measures from
/// the head, wants a free hand, stamina, an angle); each refusal gets one of these so the player hears and reads why.
/// </summary>
public enum DenyReason : byte
{
    None,
    OutOfReach,
    NoWall,
    Overhang,
    TooFlat,
    NoStamina,
    NeedsTwoHands,
    CantThrow,
    CantUse,
    UsedUp,
    CantHolster,
    WrongAngle,
    KeepHolding,
    MovedAway,
    NotWhileClimbing,
    HandsFull,
    TooSoon,
    PackFull,
    CantSwitch,
    NeedsGround,
    AimLower,
    AimHigher,
    FriendsFar,
    PocketsFull,
    StuckToHand,
    ItemWouldDrop,

    /// <summary>The VR practice was started online or in a run; it runs only from the title or the solo airport.</summary>
    NotHere,

    /// <summary>A notice, not a refusal: the held item went to its belt slot when a climb started.</summary>
    Stowed,

    /// <summary>A notice: the held item fell when a climb or carry started; marks where it lies.</summary>
    Dropped,

    /// <summary>A held torch, lantern, candle or flare is not lit, so touching it to a fire or fuse does nothing.</summary>
    NotLit,

    /// <summary>The laser shows a control through an invisible blocker in front (a window fading in) that takes the click.</summary>
    Blocked,

    /// <summary>Number of reasons; not a reason.</summary>
    Count,
}

/// <summary>Short English labels (20 characters at most) shown by the hand or target.</summary>
public static class DenyText
{
    // shortcut: English only; the game has no matching LocalizedText keys. Upgrade when a translation table exists.
    public static string Label(DenyReason reason) =>
        reason switch
        {
            DenyReason.OutOfReach => "Too far",
            DenyReason.NoWall => "No wall in reach",
            DenyReason.Overhang => "Overhang",
            DenyReason.TooFlat => "Too flat to climb",
            DenyReason.NoStamina => "No stamina",
            DenyReason.NeedsTwoHands => "Needs both hands",
            DenyReason.CantThrow => "Can't throw this",
            DenyReason.CantUse => "Can't use this now",
            DenyReason.UsedUp => "Used up",
            DenyReason.CantHolster => "Can't holster this",
            DenyReason.WrongAngle => "Wrong angle",
            DenyReason.KeepHolding => "Keep holding",
            DenyReason.MovedAway => "Moved too far",
            DenyReason.NotWhileClimbing => "Not while climbing",
            DenyReason.HandsFull => "Hands full",
            DenyReason.TooSoon => "Too soon",
            DenyReason.PackFull => "Backpack full",
            DenyReason.CantSwitch => "Can't switch now",
            DenyReason.NeedsGround => "Feet on the ground",
            DenyReason.AimLower => "Too steep up",
            DenyReason.AimHigher => "Too steep down",
            DenyReason.FriendsFar => "Friends too far",
            DenyReason.PocketsFull => "Pockets full",
            DenyReason.StuckToHand => "Stuck to your hand",
            DenyReason.ItemWouldDrop => "Climbing drops it",
            DenyReason.NotHere => "Not here",
            DenyReason.Stowed => "Stowed on your belt",
            DenyReason.Dropped => "Your item fell here",
            DenyReason.NotLit => "Not lit",
            DenyReason.Blocked => "Blocked",
            _ => "",
        };

    /// <summary>What would work instead: the next step, so a refusal teaches the rule (24 characters at most).</summary>
    public static string Hint(DenyReason reason) =>
        reason switch
        {
            DenyReason.OutOfReach => "Move closer",
            DenyReason.NoWall => "Aim at a closer wall",
            DenyReason.Overhang => "Grip the side instead",
            DenyReason.TooFlat => "Walk up it",
            DenyReason.NoStamina => "Rest to recover",
            DenyReason.NeedsTwoHands => "Grip with the other hand",
            DenyReason.CantThrow => "Open slowly to drop it",
            DenyReason.CantHolster => "Carry or drop it",
            DenyReason.PackFull => "Free a pocket first",
            DenyReason.CantSwitch => "Finish what you're doing",
            DenyReason.NeedsGround => "Stand on solid ground",
            DenyReason.AimLower => "Aim lower",
            DenyReason.AimHigher => "Aim higher",
            DenyReason.FriendsFar => "Gather everyone here",
            DenyReason.PocketsFull => "Drop or stash an item",
            DenyReason.StuckToHand => "Swing hard to throw it",
            DenyReason.ItemWouldDrop => "Put it down first",
            DenyReason.NotHere => "Start it from the title",
            DenyReason.Dropped => "Pick it up at the ring",
            DenyReason.WrongAngle => "Face them from the ring",
            DenyReason.KeepHolding => "Hold the trigger",
            DenyReason.MovedAway => "Stay by the piton",
            DenyReason.NotWhileClimbing => "Stand on the ground",
            DenyReason.HandsFull => "Put your item away",
            DenyReason.TooSoon => "Wait a moment",
            DenyReason.NotLit => "Pull trigger to light it",
            DenyReason.Blocked => "Use the window in front",
            _ => "",
        };

    /// <summary>
    /// The label's two lines: the reason, then the next step in smaller, softer text. A distance the player can close
    /// (<paramref name="meters"/> over 5 cm) replaces a generic "Move closer" with how far. Allocates; call on change.
    /// </summary>
    public static string Compose(DenyReason reason, float meters)
    {
        var label = Label(reason);
        var hint =
            meters > 0.05f
            && reason is DenyReason.OutOfReach or DenyReason.NoWall or DenyReason.MovedAway
                ? "Move "
                    + meters.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                    + " m closer"
                : Hint(reason);
        return hint.Length == 0
            ? label
            : label + "\n<size=72%><color=#d8f4ee>" + hint + "</color></size>";
    }
}

/// <summary>
/// Rate limit for denials: one per reason and hand per <see cref="SameSeconds"/>, so a refusal checked every frame
/// never spams. Another reason or hand is never held back, so a one-shot denial is never lost.
/// </summary>
public sealed class DenyGate
{
    public const float SameSeconds = 0.9f;

    private readonly float[] last = new float[(int)DenyReason.Count * 2];

    public DenyGate() => Reset();

    public bool Allow(DenyReason reason, bool left, float now)
    {
        if (reason <= DenyReason.None || reason >= DenyReason.Count)
            return false;
        int i = (int)reason * 2 + (left ? 0 : 1);
        if (now - last[i] < SameSeconds)
            return false;
        last[i] = now;
        return true;
    }

    public void Reset()
    {
        for (int i = 0; i < last.Length; i++)
            last[i] = float.NegativeInfinity;
    }
}

/// <summary>One impulse of a haptic pattern, <see cref="At"/> seconds after it starts.</summary>
public readonly struct HapticPulse
{
    public readonly float At;
    public readonly float Amplitude;
    public readonly float Seconds;

    public HapticPulse(float at, float amplitude, float seconds)
    {
        At = at;
        Amplitude = amplitude;
        Seconds = seconds;
    }
}

/// <summary>
/// The "denied" feel: three soft, quick ticks. Success cues in PeakXR are single pulses, and the hazard hold is two
/// strong beats, so a soft triple never reads as either. <see cref="Slip"/> fades out, for a hand sliding off a
/// surface it cannot hold.
/// </summary>
public static class DenyHaptics
{
    public static readonly HapticPulse[] Deny =
    {
        new(0f, 0.32f, 0.02f),
        new(0.07f, 0.32f, 0.02f),
        new(0.14f, 0.32f, 0.02f),
    };

    public static readonly HapticPulse[] Slip =
    {
        new(0f, 0.4f, 0.04f),
        new(0.07f, 0.25f, 0.04f),
        new(0.14f, 0.12f, 0.04f),
    };

    public static HapticPulse[] For(DenyReason reason) =>
        reason is DenyReason.Overhang or DenyReason.TooFlat ? Slip : Deny;
}

/// <summary>Plays a <see cref="HapticPulse"/> pattern over frames. Allocation-free.</summary>
public sealed class HapticPatternPlayer
{
    private HapticPulse[] pattern;
    private int next;
    private float start;

    public bool Playing => pattern != null && next < pattern.Length;

    public void Start(HapticPulse[] pulses, float now)
    {
        pattern = pulses;
        next = 0;
        start = now;
    }

    /// <summary>The next pulse that is due by <paramref name="now"/>, once each.</summary>
    public bool TryNext(float now, out HapticPulse pulse)
    {
        pulse = default;
        if (!Playing || now - start < pattern[next].At)
            return false;
        pulse = pattern[next++];
        return true;
    }
}

/// <summary>Opacity of a reason label over its life: a short ramp in, a hold, a slow fade. Never a flash.</summary>
public static class DenyFade
{
    public const float In = 0.12f;
    public const float Hold = 1.1f;
    public const float Out = 0.4f;
    public const float Duration = In + Hold + Out;

    public static float Alpha(float age)
    {
        if (age <= 0f || age >= Duration)
            return 0f;
        if (age < In)
            return age / In;
        if (age < In + Hold)
            return 1f;
        return 1f - (age - In - Hold) / Out;
    }

    /// <summary>
    /// As <see cref="Alpha(float)"/>, but the attempt that was refused stayed held for <paramref name="heldFor"/>
    /// seconds after it (equal to <paramref name="age"/> while still held): the cue holds at full until the release,
    /// then fades, so a player holding the trigger never watches the reason vanish mid-attempt.
    /// </summary>
    /// <summary>A refusal held by a grip that is also carrying an item would stay up for the whole carry: cap it.</summary>
    public const float CarryCap = 1.5f;

    /// <summary>
    /// Longest an attempt's hold keeps its cue up: unlimited while the refused trigger, or a grip on nothing, is held;
    /// <see cref="CarryCap"/> for a grip that also carries an item.
    /// </summary>
    public static float HoldCap(bool byTrigger, bool carrying) =>
        byTrigger || !carrying ? float.PositiveInfinity : CarryCap;

    public static float Alpha(float age, float heldFor)
    {
        if (age <= 0f)
            return 0f;
        if (age < In)
            return age / In;
        float fadeAt = System.MathF.Max(In + Hold, heldFor);
        if (age < fadeAt)
            return 1f;
        float a = 1f - (age - fadeAt) / Out;
        return a > 0f ? a : 0f;
    }
}

/// <summary>Range check for a pre-emptive "can't" cue with hysteresis on the "can" side only: it never shows
/// "can't" inside the game's limit, and once in range it turns out only past <paramref name="margin"/>, so the cue
/// does not flicker. A refused press is checked against the exact limit, not this.</summary>
public static class DenyRange
{
    /// <summary>The point <paramref name="max"/> along the line from <paramref name="origin"/> toward
    /// <paramref name="target"/>: where a range that starts at the origin ends. The target itself when in range.</summary>
    public static System.Numerics.Vector3 Boundary(
        System.Numerics.Vector3 origin,
        System.Numerics.Vector3 target,
        float max
    )
    {
        var to = target - origin;
        float d = to.Length();
        return d <= max || d < 1e-6f ? target : origin + to * (System.MathF.Max(max, 0f) / d);
    }

    /// <summary>
    /// <paramref name="dir"/> (unit) tilted the least so its upward component lies in
    /// [<paramref name="minUp"/>, <paramref name="maxUp"/>], keeping its heading: where an aim limited by elevation
    /// (the vine shooter's <c>Dot(up, aim)</c> window) would be accepted. Straight up or down keeps <paramref name="dir"/>'s
    /// fallback heading +Z.
    /// </summary>
    public static System.Numerics.Vector3 ClampElevation(
        System.Numerics.Vector3 dir,
        float minUp,
        float maxUp
    )
    {
        float up = System.Math.Clamp(dir.Y, minUp, maxUp);
        if (up == dir.Y)
            return dir;
        var flat = new System.Numerics.Vector3(dir.X, 0f, dir.Z);
        float len = flat.Length();
        flat = len > 1e-5f ? flat / len : System.Numerics.Vector3.UnitZ;
        float side = System.MathF.Sqrt(System.MathF.Max(0f, 1f - up * up));
        return flat * side + System.Numerics.Vector3.UnitY * up;
    }

    public static bool Within(float distance, float max, bool wasWithin, float margin) =>
        wasWithin ? distance <= max + margin : distance <= max;
}
