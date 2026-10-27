namespace PeakXR.Core.Tests;

public class ContrastTests
{
    [Fact]
    public void BlackOnWhite_Is21To1() =>
        Assert.Equal(21f, Contrast.Ratio(Vector3.Zero, Vector3.One), 2);

    [Fact]
    public void GreyOnCream_FailsTheMinimum() =>
        // The review's dropdown value: grey text on the cream box, about 1.7:1.
        Assert.True(Contrast.Ratio(new Vector3(0.62f), new Vector3(0.93f, 0.89f, 0.8f)) < 4.5f);

    [Theory]
    [InlineData(0.93f, 0.89f, 0.8f)] // cream dropdown box
    [InlineData(0f, 0f, 0f)]
    [InlineData(1f, 1f, 1f)]
    [InlineData(0.46f, 0.46f, 0.46f)] // mid grey, the hardest box
    [InlineData(0.2f, 0.5f, 0.9f)]
    [InlineData(0.8f, 0.2f, 0.2f)]
    public void TextFor_AnyBox_Reaches4p5To1(float r, float g, float b)
    {
        var box = new Vector3(r, g, b);
        Assert.True(Contrast.Ratio(Contrast.TextFor(box), box) >= Contrast.MinText);
    }

    [Fact]
    public void TextFor_LightBox_IsDark() =>
        Assert.True(Contrast.Luminance(Contrast.TextFor(new Vector3(0.93f, 0.89f, 0.8f))) < 0.1f);
}
