using System;
using System.Numerics;

namespace PeakXR.Core
{
    /// <summary>
    /// Folds the game's full-screen status flashes (poison, injury, cold, spore warning...) into one tint for
    /// a glow at the edge of the view. The strongest effect sets the opacity and the colours blend by
    /// strength, so several effects at once read as one calm cue instead of stacked flashes.
    /// </summary>
    public sealed class PeripheralCue
    {
        Vector3 _sum;
        float _weight;
        float _max;

        public void Reset()
        {
            _sum = Vector3.Zero;
            _weight = 0f;
            _max = 0f;
        }

        /// <summary>Adds one effect. <paramref name="weight"/> is its current strength, clamped to 0..1.</summary>
        public void Add(Vector3 color, float weight)
        {
            if (!(weight > 0f))
                return;
            if (weight > 1f)
                weight = 1f;
            _sum += color * weight;
            _weight += weight;
            if (weight > _max)
                _max = weight;
        }

        /// <summary>Blend of every added colour, weighted by strength.</summary>
        public Vector3 Color => _weight > 0f ? _sum / _weight : Vector3.Zero;

        /// <summary>Opacity of the glow: the strongest effect times the user's intensity, clamped to 0..1.</summary>
        public float Alpha(float scale)
        {
            float a = _max * scale;
            return a < 0f ? 0f
                : a > 1f ? 1f
                : a;
        }

        /// <summary>Strength of the steady tint while passed out: grows as the death timer runs out, never a flash.</summary>
        public static float DyingWeight(float deathTimer)
        {
            float t =
                deathTimer < 0f ? 0f
                : deathTimer > 1f ? 1f
                : deathTimer;
            return 0.25f + 0.55f * t;
        }

        static readonly (string Prefix, Vector3 Color)[] Colors =
        {
            ("injury", new Vector3(0.85f, 0.1f, 0.1f)),
            ("cold", new Vector3(0.45f, 0.75f, 1f)),
            ("poison", new Vector3(0.35f, 0.85f, 0.2f)),
            ("sugarrush", new Vector3(1f, 0.5f, 0.9f)),
            ("hot", new Vector3(1f, 0.45f, 0.1f)),
            ("heat", new Vector3(1f, 0.3f, 0.05f)),
            ("energy", new Vector3(1f, 0.9f, 0.25f)),
            ("drowsy", new Vector3(0.55f, 0.4f, 0.85f)),
            ("curse", new Vector3(0.45f, 0.05f, 0.6f)),
            ("sunscreen", new Vector3(1f, 0.95f, 0.7f)),
            ("thorns", new Vector3(0.6f, 0.2f, 0.1f)),
            ("spores", new Vector3(0.7f, 0.35f, 0.8f)),
            ("petrify", new Vector3(0.6f, 0.6f, 0.62f)),
            ("anti", new Vector3(0.6f, 0.8f, 1f)),
        };

        /// <summary>Tint for a GUIManager ScreenVFX field (injurySVFX, sporesWarning...); white when unknown.</summary>
        public static Vector3 ColorFor(string screenFxName)
        {
            if (!string.IsNullOrEmpty(screenFxName))
                foreach (var (prefix, color) in Colors)
                    if (screenFxName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        return color;
            return Vector3.One;
        }
    }

    /// <summary>
    /// Spherical band around the view axis (+Z) for the peripheral glow: transparent at
    /// <see cref="RingAngles"/>[0] degrees off-centre and opaque toward the edge of any headset's view,
    /// so nothing is drawn over the middle of the picture.
    /// </summary>
    public static class RimMesh
    {
        public static readonly float[] RingAngles = { 35f, 50f, 65f, 115f };
        public static readonly float[] RingAlpha = { 0f, 0.55f, 1f, 1f };

        public static void Build(
            int segments,
            float radius,
            out Vector3[] vertices,
            out float[] alpha,
            out int[] triangles
        )
        {
            int rings = RingAngles.Length;
            vertices = new Vector3[rings * segments];
            alpha = new float[vertices.Length];
            for (int i = 0; i < rings; i++)
            {
                float polar = RingAngles[i] * MathF.PI / 180f;
                float s = MathF.Sin(polar),
                    c = MathF.Cos(polar);
                for (int j = 0; j < segments; j++)
                {
                    float az = j * 2f * MathF.PI / segments;
                    int v = i * segments + j;
                    vertices[v] = new Vector3(s * MathF.Cos(az), s * MathF.Sin(az), c) * radius;
                    alpha[v] = RingAlpha[i];
                }
            }

            triangles = new int[(rings - 1) * segments * 6];
            int t = 0;
            for (int i = 0; i < rings - 1; i++)
            for (int j = 0; j < segments; j++)
            {
                int a = i * segments + j;
                int b = i * segments + (j + 1) % segments;
                int c = a + segments;
                int d = b + segments;
                triangles[t++] = a;
                triangles[t++] = c;
                triangles[t++] = b;
                triangles[t++] = b;
                triangles[t++] = c;
                triangles[t++] = d;
            }
        }
    }
}
