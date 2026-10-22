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

        public static Vector3 MouthPoint(
            Vector3 headPosition,
            Quaternion headRotation,
            Vector3 offset
        ) => headPosition + UnityQuat.Rotate(headRotation, offset);

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

    /// <summary>
    /// A flat palm over the mouth: mute while held. The hand must be at the mouth (<see cref="MouthGesture"/>'s dwell
    /// and exit hysteresis), its fingers open and its palm turned onto the face, so a closed walkie-talkie hand at the
    /// mouth stays "talk". Call <see cref="Update"/> once per frame.
    /// </summary>
    public sealed class PalmMute
    {
        /// <summary>Mean curl of index to pinky at or under this is an open hand.</summary>
        public float OpenCurl = 0.35f;

        /// <summary>Cosine between the palm normal and the face's backward direction: about 50 degrees.</summary>
        public float FacingCos = 0.64f;

        readonly MouthGesture _mouth = new MouthGesture();

        public bool Active => _mouth.Active;

        /// <param name="palmNormal">Out of the palm, unit length.</param>
        /// <param name="curl">Mean curl of index to pinky, 0 open to 1 closed.</param>
        public bool Update(
            Vector3 headPosition,
            Quaternion headRotation,
            Vector3 hand,
            Vector3 palmNormal,
            float curl,
            float dt
        )
        {
            var back = UnityQuat.Rotate(headRotation, new Vector3(0f, 0f, -1f));
            if (curl > OpenCurl || Vector3.Dot(palmNormal, back) < FacingCos)
            {
                _mouth.Reset();
                return false;
            }
            return _mouth.Update(headPosition, headRotation, hand, dt);
        }

        public void Reset() => _mouth.Reset();
    }
}
