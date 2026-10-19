using System.Numerics;

namespace PeakXR.Core
{
    /// <summary>
    /// Detects a hand or held item brought to the mouth: true once the point has stayed within
    /// <see cref="Radius"/> of the mouth for <see cref="DwellTime"/>; it stays true until the point
    /// leaves <see cref="ExitRadius"/>. Call <see cref="Update"/> once per frame.
    /// </summary>
    public sealed class MouthGesture
    {
        /// <summary>Mouth position in head space (metres): below and in front of the eyes.</summary>
        public static readonly Vector3 DefaultOffset = new Vector3(0f, -0.07f, 0.08f);

        public float Radius = 0.12f;
        public float ExitRadius = 0.16f;
        public float DwellTime = 0.25f;
        public Vector3 MouthOffset = DefaultOffset;

        bool _inside;
        float _timer;

        public bool Active { get; private set; }

        public static Vector3 MouthPoint(Vector3 headPosition, Quaternion headRotation, Vector3 offset) =>
            headPosition + UnityQuat.Rotate(headRotation, offset);

        public bool Update(Vector3 headPosition, Quaternion headRotation, Vector3 point, float dt)
        {
            Vector3 mouth = MouthPoint(headPosition, headRotation, MouthOffset);
            float r = _inside ? ExitRadius : Radius;
            _inside = Vector3.DistanceSquared(point, mouth) <= r * r;
            _timer = _inside ? _timer + dt : 0f;
            Active = _inside && _timer >= DwellTime;
            return Active;
        }

        public void Reset()
        {
            _inside = false;
            _timer = 0f;
            Active = false;
        }
    }
}
