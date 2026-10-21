namespace PeakXR.Core.Tests;

public class VRKeyboardTests
{
    [Fact]
    public void Layout_HasEverySpecialKeyOnce()
    {
        foreach (
            var kind in new[]
            {
                KeyKind.Shift,
                KeyKind.Backspace,
                KeyKind.Space,
                KeyKind.Done,
                KeyKind.Cancel,
            }
        )
        {
            int count = 0;
            foreach (var row in KeyboardLayout.Rows)
            foreach (var key in row)
                if (key.Kind == kind)
                    count++;
            Assert.Equal(1, count);
        }
    }

    [Fact]
    public void Layout_CharKeysAreUniqueInBothCases()
    {
        var lower = new System.Collections.Generic.HashSet<char>();
        var upper = new System.Collections.Generic.HashSet<char>();
        foreach (var row in KeyboardLayout.Rows)
        foreach (var key in row)
        {
            if (key.Kind != KeyKind.Character)
                continue;
            Assert.True(lower.Add(key.Lower), $"duplicate {key.Lower}");
            Assert.True(upper.Add(key.Upper), $"duplicate {key.Upper}");
        }
        for (char c = 'a'; c <= 'z'; c++)
            Assert.Contains(c, lower);
        for (char c = '0'; c <= '9'; c++)
            Assert.Contains(c, lower);
    }

    [Fact]
    public void Key_ShiftGivesUpperCaseAndSymbols()
    {
        var a = Find('a');
        Assert.Equal('a', a.Output(false));
        Assert.Equal('A', a.Output(true));
        Assert.Equal("A", a.Label(true));
        Assert.Equal('!', Find('1').Output(true));
    }

    [Fact]
    public void Key_LabelsAreCached()
    {
        var q = Find('q');
        Assert.Same(q.Label(false), q.Label(false));
    }

    [Fact]
    public void InputLine_ShowsTypedTextWithACaret()
    {
        Assert.Equal("ab|", InputLine.Show("ab", masked: false));
        Assert.Equal("**|", InputLine.Show("ab", masked: true));
        Assert.Equal("|", InputLine.Show(string.Empty, masked: false));
        Assert.Equal("|", InputLine.Show(null, masked: true));
    }

    [Fact]
    public void TextEntry_RespectsLimit()
    {
        var t = new TextEntry { Limit = 3 };
        Assert.True(t.Insert('a'));
        t.Insert('b');
        t.Insert('c');
        Assert.False(t.Insert('d'));
        Assert.Equal("abc", t.Text);
    }

    [Fact]
    public void TextEntry_BackspaceAndClear()
    {
        var t = new TextEntry { Limit = 10 };
        Assert.False(t.Backspace());
        t.Insert('x');
        t.Insert('y');
        Assert.True(t.Backspace());
        Assert.Equal("x", t.Text);
        t.Clear();
        Assert.Equal(string.Empty, t.Text);
    }

    [Fact]
    public void TextEntry_ZeroLimitMeansUnlimited()
    {
        var t = new TextEntry { Limit = 0 };
        for (int i = 0; i < 50; i++)
            Assert.True(t.Insert('a'));
        Assert.Equal(50, t.Text.Length);
    }

    [Fact]
    public void Poke_PressesOnceWhenFingerReachesPanelFromFront()
    {
        var p = new PokeButton();
        Assert.False(p.Update(0.10f, true));
        Assert.False(p.Update(0.03f, true));
        Assert.True(p.Update(0.005f, true));
        Assert.False(p.Update(-0.01f, true));
        Assert.False(p.Update(0.005f, true));
    }

    [Fact]
    public void Poke_RearmsAfterPullingBack()
    {
        var p = new PokeButton();
        p.Update(0.1f, true);
        Assert.True(p.Update(0f, true));
        p.Update(0.05f, true);
        Assert.True(p.Update(0f, true));
    }

    [Fact]
    public void Poke_IgnoresFingerOutsidePanelOrFromBehind()
    {
        var p = new PokeButton();
        p.Update(0.1f, false);
        Assert.False(p.Update(0f, false));
        var q = new PokeButton();
        q.Update(-0.2f, true);
        Assert.False(q.Update(0f, true));
    }

    private static KeyDef Find(char lower)
    {
        foreach (var row in KeyboardLayout.Rows)
        foreach (var key in row)
            if (key.Kind == KeyKind.Character && key.Lower == lower)
                return key;
        throw new InvalidOperationException($"no key {lower}");
    }
}
