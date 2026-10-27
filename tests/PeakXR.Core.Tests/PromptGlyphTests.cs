namespace PeakXR.Core.Tests;

public class PromptGlyphTests
{
    [Theory]
    [InlineData(PromptTarget.Item, PromptVerb.Grab)]
    [InlineData(PromptTarget.Container, PromptVerb.Open)]
    [InlineData(PromptTarget.Other, PromptVerb.Use)]
    [InlineData(PromptTarget.None, PromptVerb.None)]
    public void ObjectTargets_MapToTheirVerb(PromptTarget target, PromptVerb verb)
    {
        Assert.Equal(verb, PromptGlyph.Primary(target, false, false, false));
    }

    // CharacterInteractible.GetInteractionText order: drop the one you carry, else eat, else carry, else nothing.
    [Theory]
    [InlineData(true, true, true, PromptVerb.Drop)]
    [InlineData(false, true, true, PromptVerb.Eat)]
    [InlineData(false, false, true, PromptVerb.Carry)]
    [InlineData(false, false, false, PromptVerb.None)]
    public void Characters_FollowTheGamesOrder(
        bool carriedByMe,
        bool cannibal,
        bool carriable,
        PromptVerb verb
    )
    {
        Assert.Equal(
            verb,
            PromptGlyph.Primary(PromptTarget.Character, carriedByMe, cannibal, carriable)
        );
    }

    [Theory]
    [InlineData(true, PromptVerb.Piton)]
    [InlineData(false, PromptVerb.Use)]
    public void Secondary_IsThePitonWhileClimbingWithSpikes_ElseUseOnAFriend(
        bool piton,
        PromptVerb verb
    )
    {
        Assert.Equal(verb, PromptGlyph.Secondary(piton));
    }

    [Fact]
    public void EveryVerbButNone_HasAStroke_WithinTheRing()
    {
        foreach (var verb in Enum.GetValues<PromptVerb>())
        {
            var first = PromptGlyph.Stroke(verb, 0);
            Assert.Equal(verb == PromptVerb.None, first.Length == 0);
            for (int s = 0; s < 2; s++)
            {
                var stroke = PromptGlyph.Stroke(verb, s);
                Assert.True(stroke.Length <= PromptGlyph.MaxPoints, $"{verb} stroke {s}");
                foreach (var p in stroke)
                    Assert.True(p.Length() <= 1.01f, $"{verb} stroke {s} point {p}");
            }
        }
    }

    [Fact]
    public void Lunge_IsTheUpArrow()
    {
        Assert.Same(
            GlyphShape.Stroke(CueGlyph.ArrowUp, 0),
            PromptGlyph.Stroke(PromptVerb.Lunge, 0)
        );
        Assert.Same(
            GlyphShape.Stroke(CueGlyph.ArrowUp, 1),
            PromptGlyph.Stroke(PromptVerb.Lunge, 1)
        );
    }

    [Fact]
    public void Verbs_LookDifferent()
    {
        var verbs = Enum.GetValues<PromptVerb>();
        for (int a = 1; a < verbs.Length; a++)
        for (int b = a + 1; b < verbs.Length; b++)
            Assert.False(
                Same(verbs[a], verbs[b]),
                $"{verbs[a]} and {verbs[b]} draw the same strokes"
            );
    }

    private static bool Same(PromptVerb a, PromptVerb b) =>
        PromptGlyph.Stroke(a, 0).AsSpan().SequenceEqual(PromptGlyph.Stroke(b, 0))
        && PromptGlyph.Stroke(a, 1).AsSpan().SequenceEqual(PromptGlyph.Stroke(b, 1));

    [Fact]
    public void HoldFill_IsNothingAtStart_AndAClockwiseArcOnTheTrack()
    {
        var into = new System.Numerics.Vector2[17];
        Assert.Equal(0, PromptGlyph.HoldFill(0f, into));
        Assert.Equal(0, PromptGlyph.HoldFill(-1f, into));

        int n = PromptGlyph.HoldFill(1f, into);
        Assert.Equal(into.Length, n);
        Assert.Equal(0f, into[0].X, 3);
        Assert.Equal(PromptGlyph.HoldRadius, into[0].Y, 3);
        Assert.Equal(0f, into[n - 1].X, 3);
        Assert.Equal(PromptGlyph.HoldRadius, into[n - 1].Y, 3);

        // A quarter: from the top, clockwise to the right.
        n = PromptGlyph.HoldFill(0.25f, into);
        Assert.InRange(n, 2, into.Length);
        Assert.Equal(PromptGlyph.HoldRadius, into[n - 1].X, 3);
        Assert.Equal(0f, into[n - 1].Y, 3);
        for (int i = 0; i < n; i++)
            Assert.Equal(PromptGlyph.HoldRadius, into[i].Length(), 3);

        // Past done is done.
        Assert.Equal(into.Length, PromptGlyph.HoldFill(1.5f, into));
    }

    [Fact]
    public void Hold_IsTheEmptyTrackRing()
    {
        var ring = PromptGlyph.Stroke(PromptVerb.Hold, 0);
        Assert.True(ring.Length > 2);
        foreach (var p in ring)
            Assert.Equal(PromptGlyph.HoldRadius, p.Length(), 3);
    }

    [Fact]
    public void TargetNamesOff_DropsOnlyTheObviousGrab()
    {
        // UI.TargetNames off: a plain pickup shows only the game's glow; every other verb still needs its glyph.
        Assert.False(PromptGlyph.InPlace(PromptVerb.Grab, targetNames: false));
        Assert.True(PromptGlyph.InPlace(PromptVerb.Grab, targetNames: true));
        foreach (var verb in Enum.GetValues<PromptVerb>())
            if (verb != PromptVerb.Grab)
                Assert.True(PromptGlyph.InPlace(verb, targetNames: false));
    }
}
