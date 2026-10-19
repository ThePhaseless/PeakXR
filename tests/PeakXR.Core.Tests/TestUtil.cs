namespace PeakXR.Core.Tests;

internal static class TestUtil
{
    public static void Near(Vector3 expected, Vector3 actual, float tol = 1e-4f)
    {
        Assert.True(Vector3.Distance(expected, actual) <= tol, $"expected {expected}, got {actual}");
    }

    public static void Near(float expected, float actual, float tol = 1e-4f)
    {
        Assert.True(MathF.Abs(expected - actual) <= tol, $"expected {expected}, got {actual}");
    }

    /// <summary>Angle between two rotations in degrees (q and -q are equal).</summary>
    public static float AngleDeg(Quaternion a, Quaternion b)
    {
        float d = MathF.Abs(Quaternion.Dot(Quaternion.Normalize(a), Quaternion.Normalize(b)));
        return 2f * MathF.Acos(MathF.Min(1f, d)) * 180f / MathF.PI;
    }

    public static void SameRotation(Quaternion expected, Quaternion actual, float tolDeg = 0.01f)
    {
        float angle = AngleDeg(expected, actual);
        Assert.True(angle <= tolDeg, $"expected {expected}, got {actual} ({angle} deg apart)");
    }
}
