using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PeakXR.Core.Tests;

public class StepFilterTests
{
    static readonly string[] Known = { "vr.173-reach-ghost", "vr.230-a", "vr.230b-b", "mob.Frog" };
    static readonly string[] Dynamic = { "item.", "use.", "mob." };

    static StepFilter Parse(string spec)
    {
        Assert.True(StepFilter.TryParse(spec, out var f, out var error), error);
        return f;
    }

    [Fact]
    public void Exact_MatchesOnlyThatId()
    {
        var f = Parse("vr.173-reach-ghost");
        Assert.True(f.Matches("vr.173-reach-ghost"));
        Assert.False(f.Matches("vr.173-reach-ghost-2"));
        Assert.False(f.Matches("vr.17"));
    }

    [Fact]
    public void TrailingStar_MatchesThePrefix()
    {
        var f = Parse("vr.230*");
        Assert.True(f.Matches("vr.230-a"));
        Assert.True(f.Matches("vr.230b-b"));
        Assert.False(f.Matches("vr.23-x"));
    }

    [Fact]
    public void SeveralPatterns_AnyMatches_AndSpacesAreTrimmed()
    {
        var f = Parse(" vr.173-reach-ghost , mob.Character_* ");
        Assert.True(f.Matches("vr.173-reach-ghost"));
        Assert.True(f.Matches("mob.Character_Scoutmaster"));
        Assert.False(f.Matches("mob.Frog"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("vr.1,,vr.2")]
    [InlineData("vr.*1")]
    [InlineData("*")]
    public void BadSpecs_AreRefused(string spec)
    {
        Assert.False(StepFilter.TryParse(spec, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void PatternsMatchingNoStep_AreNamed()
    {
        var f = Parse(
            "vr.173-reach-ghost,vr.999-nope,vr.230*,vr.998*,mob.Character_Scoutmaster,use.Apple"
        );
        var none = f.MatchingNothing(Known, Dynamic);
        Assert.Equal(new[] { "vr.999-nope", "vr.998*" }, none);
    }

    [Fact]
    public void MayMatchUnder_TellsWhetherAnIdUnderAPrefixCanBeSelected()
    {
        Assert.True(Parse("mob.Frog").MayMatchUnder("mob."));
        Assert.True(Parse("mob.Character_*").MayMatchUnder("mob."));
        Assert.True(Parse("mo*").MayMatchUnder("mob."));
        Assert.False(Parse("vr.173-reach-ghost,use.Apple").MayMatchUnder("mob."));
    }

    [Fact]
    public void Complete_OnlyForExactIdsOnceAllHaveRun()
    {
        var exact = Parse("vr.173-reach-ghost,mob.Frog");
        Assert.False(exact.Complete);
        exact.MarkRun("vr.173-reach-ghost");
        Assert.False(exact.Complete);
        exact.MarkRun("mob.Frog");
        Assert.True(exact.Complete);

        var glob = Parse("vr.230*");
        glob.MarkRun("vr.230-a");
        Assert.False(glob.Complete);
    }

    [Fact]
    public void KnownIds_MatchTheRunnerSource()
    {
        // Every literal step id in the self-test runner is listed, so a filter can be checked before the run starts.
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PeakXR.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var sandbox = Path.Combine(dir.FullName, "src", "PeakXR", "Sandbox");
        var call = new Regex(
            @"\b(?:Run|VRStep|HandStep|Restoring|SocialStep|ScreenHandStep|PeakHandStep|ItemStep|Skip|MpStep|WithOn)\(\s*""([^""]+)"""
        );
        var found = Directory
            .GetFiles(sandbox, "SelfTestRunner*.cs")
            .SelectMany(f => call.Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
            .Distinct()
            .OrderBy(s => s, System.StringComparer.Ordinal)
            .ToArray();
        var missing = found.Except(SelfTestIds.Known).ToArray();
        var stale = SelfTestIds.Known.Except(found).ToArray();
        Assert.True(
            missing.Length == 0 && stale.Length == 0,
            $"SelfTestIds.Known out of date; add: {string.Join(", ", missing)}; remove: {string.Join(", ", stale)}"
        );
    }
}
