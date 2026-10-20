namespace PeakXR.Core.Tests;

public class PeripheralCueTests
{
    [Fact]
    public void Empty_IsInvisible()
    {
        var cue = new PeripheralCue();
        Assert.Equal(0f, cue.Alpha(1f));
    }

    [Fact]
    public void Alpha_IsStrongestWeightTimesScale()
    {
        var cue = new PeripheralCue();
        cue.Add(new Vector3(1f, 0f, 0f), 0.4f);
        cue.Add(new Vector3(0f, 0f, 1f), 0.8f);
        TestUtil.Near(0.4f, cue.Alpha(0.5f));
        Assert.Equal(1f, cue.Alpha(5f));
    }

    [Fact]
    public void Color_IsWeightedAverage()
    {
        var cue = new PeripheralCue();
        cue.Add(new Vector3(1f, 0f, 0f), 0.75f);
        cue.Add(new Vector3(0f, 0f, 1f), 0.25f);
        TestUtil.Near(new Vector3(0.75f, 0f, 0.25f), cue.Color);
    }

    [Fact]
    public void ZeroOrNegativeWeight_IsIgnored_AndOverOneIsClamped()
    {
        var cue = new PeripheralCue();
        cue.Add(new Vector3(1f, 1f, 1f), 0f);
        cue.Add(new Vector3(1f, 1f, 1f), -1f);
        Assert.Equal(0f, cue.Alpha(1f));
        cue.Add(new Vector3(0f, 1f, 0f), 3f);
        Assert.Equal(1f, cue.Alpha(1f));
        TestUtil.Near(new Vector3(0f, 1f, 0f), cue.Color);
    }

    [Fact]
    public void Reset_Clears()
    {
        var cue = new PeripheralCue();
        cue.Add(new Vector3(1f, 0f, 0f), 1f);
        cue.Reset();
        Assert.Equal(0f, cue.Alpha(1f));
    }

    [Fact]
    public void DyingWeight_GrowsWithDeathTimerAndStaysSubtle()
    {
        Assert.True(PeripheralCue.DyingWeight(0f) > 0f);
        Assert.True(PeripheralCue.DyingWeight(0.9f) > PeripheralCue.DyingWeight(0.1f));
        Assert.True(PeripheralCue.DyingWeight(5f) <= 0.8f);
    }

    [Fact]
    public void ColorFor_MatchesScreenFxFieldNames()
    {
        Assert.True(PeripheralCue.ColorFor("injurySVFX").X > 0.7f);
        Assert.True(PeripheralCue.ColorFor("coldSVFX").Z > 0.9f);
        Assert.Equal(PeripheralCue.ColorFor("petrifySVFX"), PeripheralCue.ColorFor("petrifySVFX2Full"));
        Assert.NotEqual(PeripheralCue.ColorFor("hotSVFX"), PeripheralCue.ColorFor("heatSVFX"));
        Assert.Equal(PeripheralCue.ColorFor("sporesSVFX"), PeripheralCue.ColorFor("sporesWarning"));
        Assert.Equal(new Vector3(1f, 1f, 1f), PeripheralCue.ColorFor("somethingNew"));
    }

    [Fact]
    public void RimMesh_StaysOutOfCentralView()
    {
        RimMesh.Build(32, 0.5f, out var vertices, out var alpha, out var triangles);
        Assert.Equal(RimMesh.RingAngles.Length * 32, vertices.Length);
        Assert.Equal(vertices.Length, alpha.Length);
        Assert.Equal((RimMesh.RingAngles.Length - 1) * 32 * 6, triangles.Length);
        for (int i = 0; i < vertices.Length; i++)
        {
            TestUtil.Near(0.5f, vertices[i].Length(), 1e-4f);
            var polar = MathF.Acos(vertices[i].Z / 0.5f) * 180f / MathF.PI;
            Assert.True(polar >= RimMesh.RingAngles[0] - 0.01f, $"vertex {i} at {polar} deg");
        }
        for (int j = 0; j < 32; j++)
            Assert.Equal(0f, alpha[j]);
        foreach (var t in triangles)
            Assert.InRange(t, 0, vertices.Length - 1);
    }
}
