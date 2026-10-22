using System;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// The self-test's "standing still" rule, shared by the offline and MP settles: the body is fine and the hip's net
/// horizontal travel over <see cref="Hold"/> seconds stays within <see cref="MaxDrift"/>. Net travel is measured
/// between two positions each averaged over <see cref="Smooth"/> seconds, longer than the game's idle sway period:
/// PEAK 3.0 sways a standing hip ±2.2 cm at 0.07-0.10 m/s about once a second (run 45), which a raw start-to-now
/// distance mistook for travel, while a walk, follow, slide or lean push (or a 3 cm/s creep) still travels past the
/// limit. Fixed buffer, no allocation per sample.
/// </summary>
public sealed class StillWindow
{
    public StillWindow(float maxDrift, float hold, float smooth, int capacity = 1024)
    {
        MaxDrift = maxDrift;
        Hold = hold;
        Smooth = smooth;
        _t = new float[capacity];
        _p = new Vector2[capacity];
    }

    public float MaxDrift { get; }
    public float Hold { get; }
    public float Smooth { get; }

    /// <summary>Seconds of quiet the window needs before it reports still.</summary>
    public float Required => Hold + Smooth;

    /// <summary>Net travel (m) between the smoothed oldest and newest positions, as of the last update.</summary>
    public float Drift { get; private set; }

    /// <summary>Seconds since the window last restarted (body busy) or saw travel past the limit.</summary>
    public float StillFor { get; private set; }

    readonly float[] _t;
    readonly Vector2[] _p;
    int _head;
    int _count;
    float _quietSince;

    public void Reset()
    {
        _count = 0;
        Drift = 0f;
        StillFor = 0f;
    }

    /// <summary>One sample: the hip's horizontal position, whether the rest of the body is fine (grounded, under
    /// control, not climbing...), and the time. True once quiet for <see cref="Required"/> seconds.</summary>
    public bool Update(Vector2 hip, bool bodyOk, float now)
    {
        if (!bodyOk)
        {
            Reset();
            return false;
        }
        if (_count == 0)
            _quietSince = now;
        // Ring buffer, oldest at _head; drop what is older than the window.
        if (_count == _t.Length)
            Drop();
        int i = (_head + _count) % _t.Length;
        _t[i] = now;
        _p[i] = hip;
        _count++;
        while (_count > 1 && now - _t[_head] > Required)
            Drop();

        float oldest = _t[_head];
        Vector2 early = Vector2.Zero,
            late = Vector2.Zero;
        int ne = 0,
            nl = 0;
        for (int k = 0; k < _count; k++)
        {
            int j = (_head + k) % _t.Length;
            if (_t[j] <= oldest + Smooth)
            {
                early += _p[j];
                ne++;
            }
            if (_t[j] >= now - Smooth)
            {
                late += _p[j];
                nl++;
            }
        }
        Drift = Vector2.Distance(early / Math.Max(ne, 1), late / Math.Max(nl, 1));
        if (Drift > MaxDrift)
        {
            // Real travel: the window starts again from here, so a push that stops is judged on what follows.
            _head = i;
            _count = 1;
            _quietSince = now;
        }
        StillFor = now - _quietSince;
        return StillFor >= Required;
    }

    void Drop()
    {
        _head = (_head + 1) % _t.Length;
        _count--;
    }
}
