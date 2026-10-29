using System.Collections.Generic;

namespace PeakXR.Core.Tests;

public class ExceptionDigestTests
{
    const string Ours =
        "PeakXR.Core.ShelfLayout.Pack (...) (at <ada54e49>:0)\nPeakXR.Sandbox.ItemRoom.Update () (at <ada54e49>:0)\n";
    const string Vanilla = "CharacterMovement.Update () (at <41d29b35>:0)\n";

    [Fact]
    public void OnlyStacksWithPeakXRFramesAreOurs()
    {
        Assert.True(ExceptionDigest.IsOurs(Ours));
        Assert.True(
            ExceptionDigest.IsOurs(
                "(wrapper dynamic-method) CharacterMovement.DMD<Update>\nPeakXR.Locomotion.LocomotionPatches.Postfix ()\n"
            )
        );
        Assert.False(ExceptionDigest.IsOurs(Vanilla));
        Assert.False(ExceptionDigest.IsOurs(""));
        Assert.False(ExceptionDigest.IsOurs(null));
    }

    [Fact]
    public void AStackIsReportedTheFirstTimeThenAtEachPowerOfTen()
    {
        var digest = new ExceptionDigest();
        var reported = new List<int>();
        for (int i = 0; i < 1000; i++)
        {
            int n = digest.Record(Ours);
            if (n > 0)
                reported.Add(n);
        }

        Assert.Equal(new[] { 1, 10, 100, 1000 }, reported);
    }

    [Fact]
    public void EachDistinctStackCountsOnItsOwn()
    {
        var digest = new ExceptionDigest();
        Assert.Equal(1, digest.Record(Ours));
        Assert.Equal(0, digest.Record(Ours));
        Assert.Equal(1, digest.Record(Ours + "x"));
    }

    [Fact]
    public void PastItsCapacityNewStacksStayQuiet()
    {
        var digest = new ExceptionDigest();
        for (int i = 0; i < ExceptionDigest.MaxStacks; i++)
            Assert.Equal(1, digest.Record(Ours + i));

        Assert.Equal(0, digest.Record(Ours + "one too many"));
        Assert.Equal(0, digest.Record(Ours + 0));
    }
}
