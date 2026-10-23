namespace PeakXR.Core.Tests;

public class PromptOcclusionTests
{
    [Fact]
    public void RockWellInFrontOfThePromptBlocksIt()
    {
        Assert.True(PromptOcclusion.Blocked(0.4f, 2f, 0.1f));
    }

    [Fact]
    public void NoHitDoesNotBlock()
    {
        Assert.False(PromptOcclusion.Blocked(-1f, 2f, 0.1f));
    }

    [Fact]
    public void HitsAtOrBeyondTheTargetSurfaceAreIgnored()
    {
        // The ledge an item lies on, or the target itself, meets the ray within the margin of the prompt.
        float margin = PromptOcclusion.MinMargin + 0.1f;
        Assert.False(PromptOcclusion.Blocked(2f - margin + 0.01f, 2f, 0.1f));
        Assert.False(PromptOcclusion.Blocked(2f, 2f, 0.1f));
        Assert.False(PromptOcclusion.Blocked(3f, 2f, 0.1f));
        Assert.True(PromptOcclusion.Blocked(2f - margin - 0.01f, 2f, 0.1f));
    }

    [Fact]
    public void ABiggerTargetWidensTheMargin()
    {
        Assert.True(PromptOcclusion.Blocked(1.5f, 2f, 0.1f));
        Assert.False(PromptOcclusion.Blocked(1.5f, 2f, 0.5f));
    }

    [Fact]
    public void ANearPromptIsNeverBlocked()
    {
        Assert.False(PromptOcclusion.Blocked(0.1f, PromptOcclusion.MinMargin * 0.5f, 0f));
    }

    [Fact]
    public void Ease_MovesTowardTheDimOrFullAlphaWithoutOvershoot()
    {
        float a = PromptOcclusion.Ease(1f, true, 0.05f);
        Assert.True(a < 1f && a > PromptOcclusion.DimAlpha);
        Assert.Equal(PromptOcclusion.DimAlpha, PromptOcclusion.Ease(1f, true, 10f));
        Assert.Equal(1f, PromptOcclusion.Ease(PromptOcclusion.DimAlpha, false, 10f));
        Assert.Equal(1f, PromptOcclusion.Ease(1f, false, 0.05f));
    }
}
