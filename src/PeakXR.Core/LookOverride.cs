using System;
using System.Numerics;

namespace PeakXR.Core
{
    /// <summary>
    /// Lets scripted look writes (frog tongue, rescue hook, scoutmaster throw) win over the headset without a fight.
    /// Each frame the caller passes the look values as found before its own write: if they differ from what was
    /// written last frame, a game script wrote them, and that value is kept. When the script stops, the result
    /// blends back to the headset over <see cref="BlendTime"/>. Yaw blends the short way round.
    /// </summary>
    public sealed class LookOverride
    {
        /// <summary>Seconds the scripted value is kept after the last scripted write.</summary>
        public float HoldTime = 0.2f;

        /// <summary>Seconds to blend from the scripted value back to the headset.</summary>
        public float BlendTime = 0.6f;

        /// <summary>Change (degrees) that counts as another writer.</summary>
        public float Epsilon = 0.01f;

        Vector2 _last;
        Vector2 _scripted;
        bool _hasLast;
        float _sinceScripted = float.PositiveInfinity;

        /// <summary>1 = the scripted look is in charge, 0 = the headset is.</summary>
        public float Weight { get; private set; }

        /// <param name="current">Look values (yaw, pitch) before this frame's write.</param>
        /// <param name="hmd">Look values from the headset.</param>
        /// <returns>The look values to write.</returns>
        public Vector2 Update(Vector2 current, Vector2 hmd, float dt)
        {
            bool external =
                _hasLast
                && (
                    MathF.Abs(LookMath.WrapDegrees(current.X - _last.X)) > Epsilon
                    || MathF.Abs(current.Y - _last.Y) > Epsilon
                );
            if (external)
            {
                _sinceScripted = 0f;
                _scripted = current;
            }
            else
            {
                _sinceScripted += dt;
            }

            if (_sinceScripted <= HoldTime)
                Weight = 1f;
            else if (BlendTime > 0f)
                Weight = MathF.Max(0f, 1f - (_sinceScripted - HoldTime) / BlendTime);
            else
                Weight = 0f;

            var result =
                Weight <= 0f
                    ? new Vector2(LookMath.WrapDegrees(hmd.X), hmd.Y)
                    : new Vector2(
                        LookMath.WrapDegrees(
                            hmd.X + LookMath.WrapDegrees(_scripted.X - hmd.X) * Weight
                        ),
                        hmd.Y + (_scripted.Y - hmd.Y) * Weight
                    );
            _last = result;
            _hasLast = true;
            return result;
        }

        public void Reset()
        {
            _hasLast = false;
            _sinceScripted = float.PositiveInfinity;
            Weight = 0f;
        }
    }
}
