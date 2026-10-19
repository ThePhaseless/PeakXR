namespace PeakXR.Core.Tests;

public class StickTests
{
    [Fact]
    public void InsideInnerDeadzone_IsZero()
    {
        Assert.Equal(Vector2.Zero, Stick.Apply(new Vector2(0.1f, 0.1f), 0.2f, 0.95f));
    }

    [Fact]
    public void JustOutsideDeadzone_RescalesFromZero()
    {
        var v = Stick.Apply(new Vector2(0.25f, 0f), 0.2f, 1f);
        TestUtil.Near(0.0625f, v.X, 1e-5f);
        Assert.Equal(0f, v.Y);
    }

    [Fact]
    public void AtOrBeyondOuter_SaturatesToUnitLength()
    {
        var v = Stick.Apply(new Vector2(0.6f, 0.8f) * 0.96f, 0.2f, 0.95f);
        TestUtil.Near(1f, v.Length(), 1e-5f);
        TestUtil.Near(0.75f, v.X / v.Y, 1e-4f);
    }

    [Fact]
    public void KeepsDirection()
    {
        var v = Stick.Apply(new Vector2(-0.3f, 0.4f), 0.1f, 1f);
        TestUtil.Near(-0.75f, v.X / v.Y, 1e-4f);
    }

    [Fact]
    public void ResponseCurveExponent_ShapesMagnitude()
    {
        var v = Stick.Apply(new Vector2(0f, 0.6f), 0.2f, 1f, 2f);
        TestUtil.Near(0.25f, v.Y, 1e-5f);
    }

    [Fact]
    public void Axis_IsSignPreserving()
    {
        TestUtil.Near(-0.5f, Stick.Axis(-0.6f, 0.2f, 1f), 1e-5f);
        Assert.Equal(0f, Stick.Axis(0.15f, 0.2f, 1f));
    }
}
