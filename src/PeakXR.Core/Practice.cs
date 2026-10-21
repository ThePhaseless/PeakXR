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
}

public enum PracticePhase : byte
{
    Idle,

    /// <summary>Waiting for the solo airport to load after leaving the title.</summary>
    Loading,
    Station,

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

    public static readonly PracticeStation[] Stations =
    {
        PracticeStation.Turn,
        PracticeStation.Grab,
        PracticeStation.Holster,
        PracticeStation.Eat,
        PracticeStation.Throw,
        PracticeStation.Climb,
        PracticeStation.Emote,
    };

    float timer;

    public PracticePhase Phase { get; private set; }
    public int Index { get; private set; }
    public int Completed { get; private set; }
    public int Skipped { get; private set; }

    public PracticeStation Current => Stations[Math.Min(Index, Stations.Length - 1)];

    public static GuideTopic TopicOf(PracticeStation station) =>
        station switch
        {
            PracticeStation.Turn => GuideTopic.Turn,
            PracticeStation.Grab => GuideTopic.Grab,
            PracticeStation.Holster => GuideTopic.Holster,
            PracticeStation.Eat => GuideTopic.Eat,
            PracticeStation.Throw => GuideTopic.Throw,
            PracticeStation.Climb => GuideTopic.Climb,
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

    public void Succeed()
    {
        if (Phase != PracticePhase.Station)
            return;
        Completed++;
        Phase = PracticePhase.Confirm;
        timer = ConfirmSeconds;
    }

    public void Skip()
    {
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
            Phase = PracticePhase.Station;
            return;
        }
        Phase = PracticePhase.Done;
        timer = DoneSeconds;
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
    public static PracticeEntry Decide(
        bool inRoom,
        bool offline,
        bool atTitle,
        bool inAirport,
        bool loading
    )
    {
        if (loading || (inRoom && !offline))
            return PracticeEntry.Refused;
        if (atTitle)
            return PracticeEntry.FromTitle;
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
