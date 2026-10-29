using System;

namespace PeakXR.Core;

/// <summary>One practice station: a VR mechanic the player does once, detected from game state.</summary>
public enum PracticeStation : byte
{
    Turn,
    Grab,
    Holster,
    Eat,
    Throw,
    Climb,
    Emote,

    /// <summary>Reach for an apple just out of reach: the refusal cue.</summary>
    Refused,

    /// <summary>Hold both sticks in (or Menu, where it reaches the game) to recenter.</summary>
    Recenter,

    /// <summary>Climb a hanging rope (the airport's climbing wall carries one).</summary>
    Rope,
}

public enum PracticePhase : byte
{
    Idle,

    /// <summary>Waiting for the solo airport to load after leaving the title.</summary>
    Loading,
    Station,

    /// <summary>Walking to the next station's zone; the station starts on arrival.</summary>
    Travel,

    /// <summary>The station was done: the success cue plays before the next one.</summary>
    Confirm,

    /// <summary>Every station passed or skipped: the closing cue plays.</summary>
    Done,

    /// <summary>Leaving for the title.</summary>
    Exiting,
}

/// <summary>
/// Order and timing of the practice. Comfort choices come before it, in the settings tab; skipping a station or
/// leaving is always possible, and every path ends in <see cref="PracticePhase.Exiting"/>.
/// </summary>
public sealed class PracticeFlow
{
    public const float ConfirmSeconds = 1.4f;
    public const float DoneSeconds = 2.5f;

    /// <summary>Zone by zone, nearest (most important) first; see <see cref="PracticeZones.Of"/>.</summary>
    public static readonly PracticeStation[] Stations =
    {
        PracticeStation.Turn,
        PracticeStation.Recenter,
        PracticeStation.Grab,
        PracticeStation.Refused,
        PracticeStation.Holster,
        PracticeStation.Eat,
        PracticeStation.Throw,
        PracticeStation.Climb,
        PracticeStation.Rope,
        PracticeStation.Emote,
    };

    float timer;

    public PracticePhase Phase { get; private set; }
    public int Index { get; private set; }
    public int Completed { get; private set; }
    public int Skipped { get; private set; }

    public PracticeStation Current => Stations[Math.Min(Index, Stations.Length - 1)];

    public PracticeZone Zone => PracticeZones.Of(Current);

    public static GuideTopic TopicOf(PracticeStation station) =>
        station switch
        {
            PracticeStation.Turn => GuideTopic.Turn,
            PracticeStation.Grab => GuideTopic.Grab,
            PracticeStation.Holster => GuideTopic.Holster,
            PracticeStation.Eat => GuideTopic.Eat,
            PracticeStation.Throw => GuideTopic.Throw,
            PracticeStation.Climb => GuideTopic.Climb,
            PracticeStation.Refused => GuideTopic.Refused,
            PracticeStation.Recenter => GuideTopic.PauseRecenter,
            PracticeStation.Rope => GuideTopic.Rope,
            _ => GuideTopic.Emote,
        };

    public void Begin(bool fromTitle)
    {
        Index = Completed = Skipped = 0;
        timer = 0f;
        Phase = fromTitle ? PracticePhase.Loading : PracticePhase.Station;
    }

    public void Arrived()
    {
        if (Phase == PracticePhase.Loading)
            Phase = PracticePhase.Station;
    }

    /// <summary>The player reached the zone of the station they were walking to.</summary>
    public void Reached()
    {
        if (Phase == PracticePhase.Travel)
            Phase = PracticePhase.Station;
    }

    public void Succeed()
    {
        if (Phase != PracticePhase.Station)
            return;
        Completed++;
        Phase = PracticePhase.Confirm;
        timer = ConfirmSeconds;
    }

    /// <summary>Skips the station; during a walk, only the walk (the station then starts where it is).</summary>
    public void Skip()
    {
        if (Phase == PracticePhase.Travel)
        {
            Phase = PracticePhase.Station;
            return;
        }
        if (Phase == PracticePhase.Station)
            Skipped++;
        else if (Phase != PracticePhase.Confirm)
            return;
        Next();
    }

    public void Exit()
    {
        if (Phase != PracticePhase.Idle)
            Phase = PracticePhase.Exiting;
    }

    public void Tick(float dt)
    {
        if (Phase is not (PracticePhase.Confirm or PracticePhase.Done))
            return;
        timer -= dt;
        if (timer > 0f)
            return;
        if (Phase == PracticePhase.Confirm)
            Next();
        else
            Phase = PracticePhase.Exiting;
    }

    void Next()
    {
        Index++;
        if (Index < Stations.Length)
        {
            Phase =
                PracticeZones.Of(Stations[Index]) == PracticeZones.Of(Stations[Index - 1])
                    ? PracticePhase.Station
                    : PracticePhase.Travel;
            return;
        }
        Phase = PracticePhase.Done;
        timer = DoneSeconds;
    }
}

/// <summary>Themed areas of the practice in importance order: the nearest holds the first-minute mechanics.</summary>
public enum PracticeZone : byte
{
    /// <summary>Where you start: turn, recenter, grab, and the out-of-reach refusal.</summary>
    Basics,

    /// <summary>Body and inventory: holster, eat, throw.</summary>
    Body,

    /// <summary>At a climbable wall and the rope hanging from it: climb, rope.</summary>
    Climb,

    /// <summary>Social: the emote wheel.</summary>
    Social,
}

/// <summary>
/// Which zone a station belongs to, and where a zone goes when the scene offers no anchor for it: further from the
/// start for each less important zone, so the walk between zones stays short and the order reads near to far.
/// </summary>
public static class PracticeZones
{
    /// <summary>Horizontal distance from a zone's spot that counts as there.</summary>
    public const float ArriveRadius = 1f;

    /// <summary>Least gap between a demo and the wall ahead of it.</summary>
    public const float WallGap = 0.25f;

    static readonly float[] Headings = { 0f, 30f, -30f, 60f, -60f, 90f, -90f, 135f, -135f, 180f };

    internal static int FallbackHeadings => Headings.Length;

    /// <summary>Every heading at the zone's distance, then every heading again nearer.</summary>
    public static int FallbackAttempts => Headings.Length * 2;

    public static PracticeZone Of(PracticeStation station) =>
        station switch
        {
            PracticeStation.Holster or PracticeStation.Eat or PracticeStation.Throw =>
                PracticeZone.Body,
            PracticeStation.Climb or PracticeStation.Rope => PracticeZone.Climb,
            PracticeStation.Emote => PracticeZone.Social,
            _ => PracticeZone.Basics,
        };

    /// <summary>Metres from the start spot when no zone has an anchor.</summary>
    public static float Distance(PracticeZone zone) =>
        zone switch
        {
            PracticeZone.Basics => 0f,
            PracticeZone.Body => 3f,
            PracticeZone.Climb => 5.5f,
            _ => 8f,
        };

    /// <summary>
    /// The fallback spot to try on <paramref name="attempt"/>, from the previous zone's spot: degrees off the heading
    /// outward from the start, and metres out (the gap between the two zones' <see cref="Distance"/>).
    /// </summary>
    public static (float Yaw, float Distance) Fallback(PracticeZone zone, int attempt)
    {
        float step = zone == PracticeZone.Basics ? 0f : Distance(zone) - Distance(zone - 1);
        return (
            Headings[attempt % Headings.Length],
            step * (attempt < Headings.Length ? 1f : 0.6f)
        );
    }

    public static bool Arrived(float dx, float dz) =>
        dx * dx + dz * dz <= ArriveRadius * ArriveRadius;

    /// <summary>
    /// Where a station's demo stands from the zone spot: a metre ahead and a little to the off-hand side in the open.
    /// With a wall close ahead (the climb zone faces its wall) it keeps <see cref="WallGap"/> off the wall and steps
    /// further aside, so it never sits in the wall or hides the holds.
    /// </summary>
    public static (float Ahead, float Side) DemoOffset(float clearAhead)
    {
        float ahead = MathF.Min(1f, clearAhead - WallGap);
        return ahead >= 0.75f ? (ahead, 0.3f) : (MathF.Max(0.15f, ahead), 0.6f);
    }
}

public enum PracticeEntry : byte
{
    /// <summary>Go offline with Play Solo, then practise in the airport.</summary>
    FromTitle,

    /// <summary>Already in the offline solo airport: practise here.</summary>
    InPlace,

    /// <summary>Online, in a run, or loading: never start here.</summary>
    Refused,
}

/// <summary>Where the practice may run. It never runs in an online room, and never in a solo run, which leaving would end.</summary>
public static class PracticeStart
{
    /// <summary><paramref name="enteringRoom"/> (<c>NetworkingUtilities.EnteringRoom</c>) counts only at the title:
    /// Play Solo sets it and only the title clears it again, so in the solo airport it is always stale.</summary>
    public static PracticeEntry Decide(
        bool inRoom,
        bool offline,
        bool atTitle,
        bool inAirport,
        bool loading,
        bool enteringRoom
    )
    {
        if (loading || (inRoom && !offline))
            return PracticeEntry.Refused;
        if (atTitle)
            return enteringRoom ? PracticeEntry.Refused : PracticeEntry.FromTitle;
        return inAirport && inRoom ? PracticeEntry.InPlace : PracticeEntry.Refused;
    }

    /// <summary>The once-only offer on the first VR launch, on the title's main page, never during the self-test.</summary>
    public static bool ShouldOffer(bool vr, bool offered, bool selfTest, bool titleMainPage) =>
        vr && !offered && !selfTest && titleMainPage;
}

/// <summary>A touch target that fires after the hand has stayed in it for <see cref="Seconds"/>, once per visit.</summary>
public sealed class DwellButton
{
    public readonly float Seconds;
    float held;
    bool fired;

    public DwellButton(float seconds) => Seconds = seconds;

    /// <summary>0..1 fill while the hand stays; 0 after leaving.</summary>
    public float Progress => Seconds > 0f ? MathF.Min(1f, held / Seconds) : 1f;

    public bool Tick(bool inside, float dt)
    {
        if (!inside)
        {
            held = 0f;
            fired = false;
            return false;
        }
        if (fired)
            return false;
        held += dt;
        if (held < Seconds)
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

/// <summary>Degrees turned in total, either way, from a series of yaw readings.</summary>
public sealed class TurnTracker
{
    float last;

    public float Total { get; private set; }

    public void Reset(float yaw)
    {
        last = yaw;
        Total = 0f;
    }

    public void Update(float yaw)
    {
        Total += MathF.Abs(SelfTestChecks.YawDelta(last, yaw));
        last = yaw;
    }
}
