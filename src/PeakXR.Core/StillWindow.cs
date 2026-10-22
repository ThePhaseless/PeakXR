using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// The self-test's "standing still" rule, shared by the offline and MP settles: the body is fine and the hip's net
/// horizontal travel stays within <see cref="MaxDrift"/> for <see cref="Hold"/> seconds. Net travel, not speed: the
/// game's idle sway moves the hip at up to 0.1 m/s around a fixed point (MP run 9, PEAK 3.0 offline in run 42),
/// while a walk, follow, slide or lean push carries it past the limit and restarts the window.
/// </summary>
public sealed class StillWindow
{
    public StillWindow(float maxDrift, float hold)
    {
        MaxDrift = maxDrift;
        Hold = hold;
    }

    public float MaxDrift { get; }
    public float Hold { get; }

    /// <summary>Net travel (m) from the window's start, as of the last update.</summary>
    public float Drift { get; private set; }

    /// <summary>Seconds the current window has lasted.</summary>
    public float StillFor { get; private set; }

    Vector2 _start;
    float _since;
    bool _started;

    public void Reset() => _started = false;

    /// <summary>One sample: the hip's horizontal position, whether the rest of the body is fine (grounded, under
    /// control, not climbing...), and the time. True once the window has held for <see cref="Hold"/>.</summary>
    public bool Update(Vector2 hip, bool bodyOk, float now)
    {
        if (!_started)
        {
            _started = true;
            _start = hip;
            _since = now;
        }
        Drift = Vector2.Distance(hip, _start);
        if (!bodyOk || Drift > MaxDrift)
        {
            _start = hip;
            _since = now;
            StillFor = 0f;
            return false;
        }
        StillFor = now - _since;
        return StillFor >= Hold;
    }
}
