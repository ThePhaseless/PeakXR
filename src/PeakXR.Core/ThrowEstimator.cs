using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// Estimates a hand's release velocity from recent (position, time) samples with a least-squares
/// line fit over the last <see cref="Window"/> seconds. Fixed-size ring buffer; no per-frame allocation.
/// </summary>
public sealed class ThrowEstimator
{
    /// <summary>Seconds of history, counted back from the newest sample, used for the fit.</summary>
    public float Window = 0.08f;

    /// <summary>Upper bound on the returned speed, m/s.</summary>
    public float MaxSpeed = 20f;

    readonly Vector3[] _positions;
    readonly float[] _times;
    int _head;
    int _count;

    public ThrowEstimator(int capacity = 32)
    {
        if (capacity < 2)
            capacity = 2;
        _positions = new Vector3[capacity];
        _times = new float[capacity];
    }

    /// <summary>Adds a sample. A time earlier than the newest sample (clock reset) clears the history first.</summary>
    public void AddSample(Vector3 position, float time)
    {
        if (_count > 0 && time < _times[Newest()])
            Clear();
        _positions[_head] = position;
        _times[_head] = time;
        _head = (_head + 1) % _positions.Length;
        if (_count < _positions.Length)
            _count++;
    }

    public void Clear()
    {
        _head = 0;
        _count = 0;
    }

    /// <summary>Velocity in units/s from samples within <see cref="Window"/> of the newest; zero if fewer than 2.</summary>
    public Vector3 GetReleaseVelocity()
    {
        if (_count < 2)
            return Vector3.Zero;

        int cap = _positions.Length;
        float newest = _times[Newest()];
        float cutoff = newest - Window - 1e-5f;

        // Times are taken relative to the newest sample to keep float precision.
        int n = 0;
        float sumT = 0f;
        Vector3 sumP = Vector3.Zero;
        for (int k = 0; k < _count; k++)
        {
            int i = (_head - 1 - k + cap) % cap;
            if (_times[i] < cutoff)
                break;
            sumT += _times[i] - newest;
            sumP += _positions[i];
            n++;
        }
        if (n < 2)
            return Vector3.Zero;

        float meanT = sumT / n;
        Vector3 meanP = sumP / n;
        float sxx = 0f;
        Vector3 sxy = Vector3.Zero;
        for (int k = 0; k < n; k++)
        {
            int i = (_head - 1 - k + cap) % cap;
            float dt = _times[i] - newest - meanT;
            sxx += dt * dt;
            sxy += (_positions[i] - meanP) * dt;
        }
        if (sxx < 1e-10f)
            return Vector3.Zero;

        Vector3 v = sxy / sxx;
        float speed = v.Length();
        if (speed > MaxSpeed && speed > 0f)
            v *= MaxSpeed / speed;
        return v;
    }

    int Newest() => (_head - 1 + _positions.Length) % _positions.Length;
}
