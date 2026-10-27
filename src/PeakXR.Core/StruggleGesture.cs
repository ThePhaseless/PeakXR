using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// Detects shaking or tearing motions of the hands, used to struggle out of webs and fly traps instead of
/// mashing jump. Each hand counts a stroke when it moves faster than <see cref="MinSpeed"/> in a direction
/// roughly opposite to its previous stroke; every stroke is one jump press. Call <see cref="Update"/> once per
/// frame with tracking-space hand velocities. No allocations.
/// </summary>
public sealed class StruggleGesture
{
    /// <summary>Hand speed (m/s) that counts as a stroke.</summary>
    public float MinSpeed = 1.2f;

    /// <summary>Shortest time between two strokes of the same hand, seconds.</summary>
    public float MinInterval = 0.12f;

    /// <summary>Cosine below which a new direction counts as reversed.</summary>
    public float ReverseCos = -0.3f;

    readonly HandState _left = new HandState();
    readonly HandState _right = new HandState();

    /// <summary>Returns the number of strokes this frame (0, 1 or 2).</summary>
    public int Update(
        Vector3 leftVelocity,
        bool leftTracked,
        Vector3 rightVelocity,
        bool rightTracked,
        float dt
    )
    {
        int strokes = 0;
        if (_left.Update(leftVelocity, leftTracked, dt, this))
            strokes++;
        if (_right.Update(rightVelocity, rightTracked, dt, this))
            strokes++;
        return strokes;
    }

    public void Reset()
    {
        _left.Reset();
        _right.Reset();
    }

    sealed class HandState
    {
        Vector3 _lastDir;
        bool _hasDir;
        float _sinceStroke = float.MaxValue;

        public bool Update(Vector3 velocity, bool tracked, float dt, StruggleGesture g)
        {
            if (_sinceStroke < float.MaxValue)
                _sinceStroke += dt;
            if (!tracked)
            {
                Reset();
                return false;
            }
            float speed = velocity.Length();
            if (speed < g.MinSpeed || _sinceStroke < g.MinInterval)
                return false;
            var dir = velocity / speed;
            if (!_hasDir)
            {
                // The first fast motion only sets the reference direction.
                _lastDir = dir;
                _hasDir = true;
                _sinceStroke = 0f;
                return false;
            }
            if (Vector3.Dot(dir, _lastDir) > g.ReverseCos)
                return false;
            _lastDir = dir;
            _sinceStroke = 0f;
            return true;
        }

        public void Reset()
        {
            _hasDir = false;
            _sinceStroke = float.MaxValue;
        }
    }
}
