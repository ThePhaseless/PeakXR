using System.Numerics;

namespace PeakXR.Core
{
    /// <summary>
    /// Move-stick math. PEAK reads <c>movementInput</c> relative to the look direction, and the look direction is
    /// the headset's, so a plain stick value is head-relative; these helpers re-express other frames in it.
    /// Yaw angles are degrees in Unity's convention (see <see cref="YawMath"/>).
    /// </summary>
    public static class MoveInput
    {
        /// <summary>Stick pushed relative to the hand's facing, as head-relative input.</summary>
        public static Vector2 HandRelative(Vector2 stick, float headYaw, float handYaw) =>
            YawMath.Rotate(stick, LookMath.WrapDegrees(handYaw - headYaw));

        /// <summary>A tracking-space horizontal direction (x, z) as head-relative input.</summary>
        public static Vector2 TrackingToStick(Vector2 direction, float headYaw) =>
            YawMath.Rotate(direction, -headYaw);

        /// <summary>Yaw of a forward vector; pitch and roll are ignored.</summary>
        public static float Yaw(Vector3 forward) => YawMath.Yaw(new Vector2(forward.X, forward.Z));

        /// <summary>Sum of two inputs, clamped to unit length like the game clamps its own stick.</summary>
        public static Vector2 Combine(Vector2 a, Vector2 b)
        {
            var v = a + b;
            float len = v.Length();
            return len > 1f ? v / len : v;
        }
    }

    /// <summary>
    /// Snap or smooth turning from the turn stick. Only sideways-dominant pushes turn, so up/down flicks on the same
    /// stick (hotbar) never rotate the player. Call <see cref="Update"/> once per frame.
    /// </summary>
    public sealed class TurnInput
    {
        public TurnMode Mode;

        /// <summary>Degrees per second at full deflection in smooth mode.</summary>
        public float SmoothSpeed = 120f;

        public float Deadzone = 0.15f;

        public readonly SnapTurn Snap = new SnapTurn();

        /// <summary>Smooth turning speed (deg/s) this frame; 0 for snaps, which are instant and need no vignette.</summary>
        public float SmoothRate { get; private set; }

        /// <returns>Yaw delta in degrees, positive = right.</returns>
        public float Update(Vector2 stick, float dt)
        {
            float x = StickAxes.TurnX(stick);
            if (Mode == TurnMode.Snap)
            {
                SmoothRate = 0f;
                return Snap.Update(x, dt);
            }
            Snap.Reset();
            float delta = SmoothTurn.Delta(x, SmoothSpeed, dt, Deadzone);
            SmoothRate = dt > 0f ? delta / dt : 0f;
            return delta;
        }

        public void Reset()
        {
            Snap.Reset();
            SmoothRate = 0f;
        }
    }
}
