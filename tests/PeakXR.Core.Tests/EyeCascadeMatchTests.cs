namespace PeakXR.Core.Tests;

public class EyeCascadeMatchTests
{
    static float[] Values(float seed)
    {
        var v = new float[EyeCascadeMatch.Values];
        for (int i = 0; i < v.Length; i++)
            v[i] = seed + i * 0.5f;
        return v;
    }

    static void Cull(EyeCascadeMatch m, int frame, params float[][] cascades)
    {
        m.BeginCull(frame);
        for (int i = 0; i < cascades.Length; i++)
            m.Cascade(i, cascades[i]);
    }

    [Fact]
    public void BothEyesAlikeCountAsMatched()
    {
        var m = new EyeCascadeMatch();
        Cull(m, 1, Values(1f), Values(2f));
        Cull(m, 1, Values(1f), Values(2f));
        Cull(m, 2, Values(3f), Values(4f));
        Cull(m, 2, Values(3f), Values(4f));
        m.BeginCull(3); // closes frame 2
        Assert.Equal(2, m.StereoFrames);
        Assert.Equal(2, m.MatchedFrames);
    }

    [Fact]
    public void ACascadeFittedApartIsAMismatch()
    {
        var m = new EyeCascadeMatch();
        Cull(m, 1, Values(1f), Values(2f));
        Cull(m, 1, Values(1f), Values(2.01f));
        m.BeginCull(2);
        Assert.Equal(1, m.StereoFrames);
        Assert.Equal(0, m.MatchedFrames);
    }

    [Fact]
    public void FloatNoiseStillMatches()
    {
        var m = new EyeCascadeMatch();
        var b = Values(1000f);
        b[3] += 1e-4f;
        Cull(m, 1, Values(1000f));
        Cull(m, 1, b);
        m.BeginCull(2);
        Assert.Equal(1, m.MatchedFrames);
    }

    [Fact]
    public void AnEyeWithFewerCascadesIsAMismatch()
    {
        var m = new EyeCascadeMatch();
        Cull(m, 1, Values(1f), Values(2f));
        Cull(m, 1, Values(1f));
        m.BeginCull(2);
        Assert.Equal(1, m.StereoFrames);
        Assert.Equal(0, m.MatchedFrames);
    }

    [Fact]
    public void OneCullOrNoCascadesIsNotAStereoFrame()
    {
        var m = new EyeCascadeMatch();
        Cull(m, 1, Values(1f));
        m.BeginCull(2); // two culls, no cascades (shadows off)
        m.BeginCull(2);
        m.BeginCull(3);
        Assert.Equal(0, m.StereoFrames);
    }

    [Fact]
    public void OutOfRangeCascadesAreIgnored()
    {
        var m = new EyeCascadeMatch();
        m.BeginCull(1);
        m.Cascade(-1, Values(1f));
        m.Cascade(EyeCascadeMatch.MaxCascades, Values(1f));
        m.Cascade(0, new float[3]);
        m.BeginCull(1);
        m.BeginCull(2);
        Assert.Equal(0, m.StereoFrames);
    }
}
