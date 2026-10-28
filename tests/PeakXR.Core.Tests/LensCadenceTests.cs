namespace PeakXR.Core.Tests;

public class LensCadenceTests
{
    [Fact]
    public void Eyepiece_RendersEveryOtherFrame()
    {
        int rendered = 0;
        for (int frame = 100; frame < 200; frame++)
            if (LensCadence.Render(eyepiece: true, retargeted: false, frame))
                rendered++;
        Assert.Equal(50, rendered);
        Assert.NotEqual(LensCadence.Render(true, false, 7), LensCadence.Render(true, false, 8));
    }

    [Fact]
    public void NearEyeAndPanel_RenderEveryFrame()
    {
        for (int frame = 0; frame < 10; frame++)
            Assert.True(LensCadence.Render(eyepiece: false, retargeted: false, frame));
    }

    [Fact]
    public void SwitchedTexture_RendersAtOnce()
    {
        // An odd frame would skip, but the texture just switched holds nothing current.
        Assert.False(LensCadence.Render(true, false, 7));
        Assert.True(LensCadence.Render(true, true, 7));
    }

    [Fact]
    public void EyepieceSize_HalfTheLens_NeverTiny()
    {
        Assert.Equal(256, LensCadence.EyepieceSize(512));
        Assert.Equal(512, LensCadence.EyepieceSize(1024));
        Assert.Equal(64, LensCadence.EyepieceSize(64));
    }
}
