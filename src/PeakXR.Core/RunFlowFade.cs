using System;

namespace PeakXR.Core;

/// <summary>Where a <see cref="RunFlowFade"/> transition is; Idle means it requests nothing.</summary>
public enum RunFlowPhase
{
    Idle,
    FadingOut,
    Holding,
    FadingIn,
}

/// <summary>
/// Fade-out, stable black hold, fade-in around a scripted transition (teleport, segment swap, run
/// end). The hold lasts at least <see cref="MinHoldSeconds"/> and while the caller reports the
/// transition as busy, but never longer than <see cref="MaxHoldSeconds"/>, so a missed "done"
/// signal can never leave the headset black. Call <see cref="Update"/> once per frame with
/// unscaled time; <see cref="Alpha"/> is linear (0 = clear, 1 = black).
/// </summary>
public sealed class RunFlowFade
{
    public float FadeOutSeconds = 0.4f;
    public float MinHoldSeconds = 0.2f;
    public float MaxHoldSeconds = 10f;
    public float FadeInSeconds = 0.6f;

    public RunFlowPhase Phase { get; private set; }

    public float Alpha { get; private set; }

    /// <summary>Seconds spent black in the current hold.</summary>
    public float HoldTime { get; private set; }

    /// <summary>True once the last hold was ended by <see cref="MaxHoldSeconds"/>; cleared by the next request.</summary>
    public bool TimedOut { get; private set; }

    public bool Active => Phase != RunFlowPhase.Idle;

    /// <summary>
    /// Start (or extend) a transition. <paramref name="fromAlpha"/> lets the fade pick up from a level
    /// another effect already reached; 1 goes black at once. A request during the hold restarts it.
    /// </summary>
    public void Request(float fromAlpha = 0f)
    {
        Alpha = MathF.Max(Alpha, Math.Clamp(fromAlpha, 0f, 1f));
        TimedOut = false;
        if (Phase == RunFlowPhase.Holding)
        {
            HoldTime = 0f;
            return;
        }
        if (Alpha >= 1f || FadeOutSeconds <= 0f)
            EnterHold();
        else
            Phase = RunFlowPhase.FadingOut;
    }

    /// <summary>End the transition now: fade back in from wherever it is.</summary>
    public void Release()
    {
        if (Phase != RunFlowPhase.Idle)
            Phase = RunFlowPhase.FadingIn;
    }

    /// <summary>Clear at once, e.g. when VR stops.</summary>
    public void Reset()
    {
        Phase = RunFlowPhase.Idle;
        Alpha = 0f;
        HoldTime = 0f;
    }

    public float Update(float dt, bool busy)
    {
        if (dt < 0f)
            dt = 0f;
        switch (Phase)
        {
            case RunFlowPhase.FadingOut:
                Alpha = FadeOutSeconds > 0f ? Alpha + dt / FadeOutSeconds : 1f;
                if (Alpha >= 1f)
                    EnterHold();
                break;
            case RunFlowPhase.Holding:
                HoldTime += dt;
                if (HoldTime >= MaxHoldSeconds)
                {
                    TimedOut = true;
                    Phase = RunFlowPhase.FadingIn;
                }
                else if (HoldTime >= MinHoldSeconds && !busy)
                    Phase = RunFlowPhase.FadingIn;
                break;
            case RunFlowPhase.FadingIn:
                Alpha = FadeInSeconds > 0f ? Alpha - dt / FadeInSeconds : 0f;
                if (Alpha <= 0f)
                {
                    Alpha = 0f;
                    Phase = RunFlowPhase.Idle;
                }
                break;
        }
        return Alpha;
    }

    private void EnterHold()
    {
        Alpha = 1f;
        HoldTime = 0f;
        Phase = RunFlowPhase.Holding;
    }
}

/// <summary>
/// The run-start wake-up on the beach in Fade mode: hidden (loading room, then black) while the body lies on the
/// sand. One <see cref="Budget"/> from the spawn covers the room and the black hold together, so a body that
/// stays down never keeps the player out longer. A body resting off the ground ends it early: the game drains
/// its fall timer at only 0.2x there (A/Character.cs:1166-1175), up to ~35 s for Fall(7).
/// </summary>
public sealed class BeachWake
{
    /// <summary>Seconds from the spawn the wake-up may stay hidden, room and black hold together.</summary>
    public const float Budget = 12f;

    /// <summary>Seconds a body must rest off the ground before the hold ends.</summary>
    public const float SettleSeconds = 1f;

    /// <summary>Body speed (m/s) below which it counts as resting.</summary>
    public const float RestSpeed = 0.3f;

    private float elapsed;
    private float still;

    public bool Active { get; private set; }

    /// <summary>Budget left for whatever hides the wake-up next; 0 once it ended.</summary>
    public float Remaining => Active ? MathF.Max(0f, Budget - elapsed) : 0f;

    public void Start()
    {
        Active = true;
        elapsed = still = 0f;
    }

    public void Stop() => Active = false;

    /// <summary>Once per frame; returns whether the wake-up is still hidden.</summary>
    public bool Update(float dt, bool lying, bool grounded, float speed)
    {
        if (!Active)
            return false;
        elapsed += MathF.Max(0f, dt);
        still = !grounded && speed < RestSpeed ? still + dt : 0f;
        if (!lying || elapsed >= Budget || still >= SettleSeconds)
            Active = false;
        return Active;
    }
}

/// <summary>
/// Controller rumble standing in for a game camera shake (the shake itself never reaches the
/// headset). One linearly decaying pulse; a stronger pulse replaces a weaker one.
/// </summary>
public sealed class RunFlowRumble
{
    private float amplitude;
    private float duration;
    private float elapsed;

    /// <summary>Game Perlin-shake amount that maps to full rumble.</summary>
    public const float FullShake = 8f;

    public float Current => elapsed >= duration ? 0f : amplitude * (1f - elapsed / duration);

    public void Add(float amplitude, float seconds)
    {
        amplitude = Math.Clamp(amplitude, 0f, 1f);
        if (seconds <= 0f || amplitude <= Current)
            return;
        this.amplitude = amplitude;
        duration = seconds;
        elapsed = 0f;
    }

    public float Update(float dt)
    {
        if (dt > 0f && elapsed < duration)
            elapsed += dt;
        return Current;
    }

    public void Reset() => amplitude = duration = elapsed = 0f;

    /// <summary>Rumble amplitude for a game shake <paramref name="amount"/> scaled by the user's strength setting.</summary>
    public static float FromShake(float amount, float strength) =>
        Math.Clamp(amount / FullShake, 0f, 1f) * Math.Clamp(strength, 0f, 1f);
}

/// <summary>
/// A campfire segment swap never shows unfaded. <see cref="Arm"/> when the game starts a segment advance; the fade
/// starts when the game's own wait for the fog ends (<see cref="FogCaughtUp"/>; the swap follows 1 s later), however
/// long that takes; if the swap comes first, it snaps to black (<see cref="Swap"/>). <see cref="Timeout"/> only
/// disarms an advance whose swap never comes.
/// </summary>
public sealed class SegmentSwapGate
{
    public float Timeout = 600f;

    private float armedAt;
    private bool faded;

    public bool Armed { get; private set; }

    public void Arm(float now)
    {
        Armed = true;
        faded = false;
        armedAt = now;
    }

    /// <summary>The wait before the swap ended: true once, when the fade must start.</summary>
    public bool FogCaughtUp()
    {
        if (!Armed || faded)
            return false;
        faded = true;
        return true;
    }

    /// <summary>The view swaps now: true when the fade had not started, so it must snap to black.</summary>
    public bool Swap()
    {
        if (!Armed)
            return false;
        Armed = false;
        return !faded;
    }

    /// <summary>True once when an armed advance got no swap within <see cref="Timeout"/>.</summary>
    public bool Expired(float now)
    {
        if (!Armed || now - armedAt <= Timeout)
            return false;
        Armed = false;
        return true;
    }

    public void Reset() => Armed = false;
}
