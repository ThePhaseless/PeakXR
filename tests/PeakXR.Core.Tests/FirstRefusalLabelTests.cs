namespace PeakXR.Core.Tests;

public class FirstRefusalLabelTests
{
    [Fact]
    public void EveryReasonFitsTheSeenMask() => Assert.True((int)DenyReason.Count <= 64);

    [Fact]
    public void FirstFire_ShowsTheLabel_SecondDoesNot()
    {
        ulong seen = 0;
        Assert.True(FirstRefusalLabel.Show(false, true, ref seen, DenyReason.Overhang));
        Assert.False(FirstRefusalLabel.Show(false, true, ref seen, DenyReason.Overhang));
        Assert.True(FirstRefusalLabel.Show(false, true, ref seen, DenyReason.TooSteep));
    }

    [Fact]
    public void SettingOff_NoLabel_AndNothingMarked()
    {
        ulong seen = 0;
        Assert.False(FirstRefusalLabel.Show(false, false, ref seen, DenyReason.Overhang));
        Assert.Equal(0ul, seen);
    }

    [Fact]
    public void ActionHintText_AlwaysShows_AndStillMarks()
    {
        ulong seen = 0;
        Assert.True(FirstRefusalLabel.Show(true, true, ref seen, DenyReason.Locked));
        Assert.True(FirstRefusalLabel.Show(true, true, ref seen, DenyReason.Locked));
        Assert.True(FirstRefusalLabel.Seen(seen, DenyReason.Locked));
    }

    [Fact]
    public void NoneAndOutOfRange_NeverShowNorMark()
    {
        ulong seen = 0;
        Assert.False(FirstRefusalLabel.Show(false, true, ref seen, DenyReason.None));
        Assert.False(FirstRefusalLabel.Show(false, true, ref seen, DenyReason.Count));
        Assert.Equal(0ul, seen);
    }
}
