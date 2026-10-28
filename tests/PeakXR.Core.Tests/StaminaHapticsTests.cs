namespace PeakXR.Core.Tests;

public class StaminaHapticsTests
{
    [Fact]
    public void PlentyOfStamina_NoPulses()
    {
        var h = new StaminaHaptics();
        for (int i = 0; i < 100; i++)
            Assert.Equal(0f, h.Update(0.8f, false, 0f, 0.02f));
    }

    static int CountPulses(StaminaHaptics h, float stamina, bool outOf, float slide, float seconds)
    {
        int n = 0;
        for (int i = 0; i < (int)(seconds * 100f); i++)
        {
            if (h.Update(stamina, outOf, slide, 0.01f) > 0f)
                n++;
        }
        return n;
    }

    [Fact]
    public void LowStamina_PulsesFasterAsItDrops()
    {
        int low = CountPulses(new StaminaHaptics(), 0.25f, false, 0f, 4f);
        int lower = CountPulses(new StaminaHaptics(), 0.05f, false, 0f, 4f);
        Assert.True(low > 0);
        Assert.True(lower > low, $"{lower} <= {low}");
    }

    [Fact]
    public void OutOfStamina_IsStrongerThanLow()
    {
        var a = new StaminaHaptics();
        var b = new StaminaHaptics();
        float low = 0f,
            outOf = 0f;
        for (int i = 0; i < 200; i++)
        {
            low = MathF.Max(low, a.Update(0.1f, false, 0f, 0.01f));
            outOf = MathF.Max(outOf, b.Update(0f, true, 0f, 0.01f));
        }
        Assert.True(outOf > low);
    }

    [Fact]
    public void Sliding_BuzzesRapidly()
    {
        int sliding = CountPulses(new StaminaHaptics(), 0.8f, false, 5f, 1f);
        Assert.InRange(sliding, 10, int.MaxValue);
    }

    [Theory]
    [InlineData(0f, true, 0f)]
    [InlineData(0.8f, false, 5f)]
    [InlineData(0.1f, false, 0f)]
    public void Warnings_SurviveEssential(float stamina, bool outOf, float slide)
    {
        var h = new StaminaHaptics();
        Assert.True(h.Update(stamina, outOf, slide, 0.01f) > 0f);
        // Essential drops only background feel; running out on a wall is a warning the hand must get.
        Assert.True(HapticScale.Plays(HapticDetail.Essential, h.Priority));
    }

    [Fact]
    public void OutOfStaminaAndSliding_AreAlerts_SoAShakeRumbleCannotCutThem()
    {
        var h = new StaminaHaptics();
        h.Update(0f, true, 0f, 0.01f);
        Assert.Equal(HapticPriority.Alert, h.Priority);
        h = new StaminaHaptics();
        h.Update(0.8f, false, 5f, 0.01f);
        Assert.Equal(HapticPriority.Alert, h.Priority);
    }

    [Fact]
    public void ModeChange_PulsesAtOnce()
    {
        // A low-stamina pulse waits up to 1 s for the next; a slide starting now must buzz now, not after that wait.
        var h = new StaminaHaptics();
        Assert.True(h.Update(0.29f, false, 0f, 0.01f) > 0f);
        Assert.True(h.Update(0.29f, false, 5f, 0.01f) > 0f);
    }
}
