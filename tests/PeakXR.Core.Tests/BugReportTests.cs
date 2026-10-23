using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PeakXR.Core.Tests;

public class RingTests
{
    [Fact]
    public void KeepsTheNewest_OldestFirst()
    {
        var r = new Ring<int>(3);
        for (int i = 1; i <= 5; i++)
            r.Add(i);
        Assert.Equal(3, r.Count);
        Assert.Equal(new[] { 3, 4, 5 }, new[] { r[0], r[1], r[2] });
        Assert.Equal(5, r.Total);
    }

    [Fact]
    public void BeforeWrap_InInsertOrder()
    {
        var r = new Ring<int>(4);
        r.Add(7);
        r.Add(8);
        Assert.Equal(2, r.Count);
        Assert.Equal(7, r[0]);
        Assert.Equal(8, r[1]);
    }

    [Fact]
    public void ToArray_CopiesOldestFirst()
    {
        var r = new Ring<string>(2);
        r.Add("a");
        r.Add("b");
        r.Add("c");
        Assert.Equal(new[] { "b", "c" }, r.ToArray());
    }

    [Fact]
    public void Clear_Empties()
    {
        var r = new Ring<int>(2);
        r.Add(1);
        r.Clear();
        Assert.Equal(0, r.Count);
        Assert.Empty(r.ToArray());
    }

    [Fact]
    public void IndexOutsideCount_Throws()
    {
        var r = new Ring<int>(2);
        r.Add(1);
        Assert.Throws<ArgumentOutOfRangeException>(() => r[1]);
    }
}

public class PoseCsvTests
{
    static PoseSample Sample(string held = "Apple") =>
        new()
        {
            Wall = new DateTime(2026, 10, 23, 14, 3, 22, 125).Ticks,
            T = 12.5f,
            HeadTracked = true,
            Head = new Vector3(0.1f, 1.6f, -0.2f),
            HeadRotation = Quaternion.Identity,
            Left = new PoseHand
            {
                Tracked = true,
                Position = new Vector3(-0.2f, 1.1f, 0.3f),
                Rotation = Quaternion.Identity,
                Trigger = 0.5f,
                Grip = 1f,
                Stick = new Vector2(0f, 1f),
                Primary = true,
                Menu = true,
                Curls = new FingerCurls(0.1f, 0.2f, 0.3f, 0.4f, 0.5f),
            },
            Grounded = true,
            Stamina = 0.75f,
            Held = held,
        };

    static string Row(in PoseSample s)
    {
        var sb = new StringBuilder();
        PoseCsv.Row(sb, s);
        return sb.ToString();
    }

    [Fact]
    public void RowHasAColumnPerHeader()
    {
        int columns = PoseCsv.Header.Split(',').Length;
        Assert.Equal(columns, Row(Sample()).Split(',').Length);
    }

    [Fact]
    public void InvariantNumbers_UnderACommaDecimalCulture()
    {
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            var row = Row(Sample());
            Assert.StartsWith("14:03:22.125,12.50,1,0.100,1.600,-0.200,", row);
            Assert.Contains(",0.50,1.00,0.00,1.00,PM,", row);
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void HeldNameWithACommaIsQuoted()
    {
        var row = Row(Sample("Rope, long \"red\""));
        Assert.EndsWith(",\"Rope, long \"\"red\"\"\"", row);
    }

    [Fact]
    public void NothingHeld_EmptyLastColumn() => Assert.EndsWith(",", Row(Sample(null)));

    [Fact]
    public void NoButtons_Dash() => Assert.Contains(",-,", Row(new PoseSample()));
}

public class BugChordTests
{
    const float Dt = 0.1f;

    static BugChordEvent Hold(
        BugChord c,
        float seconds,
        bool menuL,
        bool stickL,
        bool menuR,
        bool stickR
    )
    {
        var last = BugChordEvent.None;
        for (float t = 0f; t < seconds - 1e-4f; t += Dt)
        {
            var e = c.Update(menuL, stickL, menuR, stickR, Dt);
            if (e != BugChordEvent.None)
                last = e;
        }
        return last;
    }

    [Fact]
    public void LeftMenuAndRightStick_HeldPastTheTime_Report()
    {
        var c = new BugChord();
        Assert.Equal(BugChordEvent.Report, Hold(c, 1.7f, true, false, false, true));
        Assert.True(c.MenuLeft);
    }

    [Fact]
    public void Report_FiresOnce_AndNoMarkerOnRelease()
    {
        var c = new BugChord();
        int reports = 0;
        for (int i = 0; i < 40; i++)
            if (c.Update(true, false, false, true, Dt) == BugChordEvent.Report)
                reports++;
        Assert.Equal(1, reports);
        Assert.Equal(BugChordEvent.None, c.Update(false, false, false, false, Dt));
    }

    [Fact]
    public void ShortChord_Marker()
    {
        var c = new BugChord();
        Assert.Equal(BugChordEvent.None, Hold(c, 0.5f, true, false, false, true));
        Assert.Equal(BugChordEvent.Marker, c.Update(true, false, false, false, Dt));
    }

    [Fact]
    public void RightMenuAndLeftStick_Mirror()
    {
        var c = new BugChord();
        Assert.Equal(BugChordEvent.Report, Hold(c, 1.7f, false, true, true, false));
        Assert.False(c.MenuLeft);
    }

    [Fact]
    public void MenuAlone_NothingAndNotTaken()
    {
        var c = new BugChord();
        Assert.Equal(BugChordEvent.None, Hold(c, 3f, true, false, true, false));
        Assert.False(c.MenuTaken(true));
        Assert.False(c.MenuTaken(false));
    }

    [Fact]
    public void SameControllerMenuAndStick_Nothing()
    {
        var c = new BugChord();
        Assert.Equal(BugChordEvent.None, Hold(c, 3f, true, true, false, false));
        Assert.False(c.Active);
    }

    [Fact]
    public void Taken_FromTheChordThroughTheReleaseFrame()
    {
        var c = new BugChord();
        c.Update(true, false, false, false, Dt);
        Assert.False(c.MenuTaken(true));
        c.Update(true, false, false, true, Dt);
        Assert.True(c.MenuTaken(true));
        Assert.True(c.StickTaken(false));
        Assert.False(c.StickTaken(true));
        // Stick let go first: still taken on its release frame, so a tap there never reaches the game.
        c.Update(true, false, false, false, Dt);
        Assert.True(c.StickTaken(false));
        Assert.True(c.MenuTaken(true));
        c.Update(true, false, false, false, Dt);
        Assert.False(c.StickTaken(false));
        Assert.True(c.MenuTaken(true));
        // Menu released: taken on that frame (no pause tap), free the next.
        c.Update(false, false, false, false, Dt);
        Assert.True(c.MenuTaken(true));
        c.Update(false, false, false, false, Dt);
        Assert.False(c.MenuTaken(true));
    }

    [Fact]
    public void Progress_RisesToOne()
    {
        var c = new BugChord();
        c.Update(true, false, false, true, Dt);
        Assert.Equal(0f, c.Progress, 3);
        Hold(c, 0.75f, true, false, false, true);
        Assert.InRange(c.Progress, 0.45f, 0.6f);
        Hold(c, 2f, true, false, false, true);
        Assert.Equal(1f, c.Progress, 3);
    }
}

public class BugReportTextTests
{
    static long At(int h, int m, int s, int ms) => new DateTime(2026, 10, 23, h, m, s, ms).Ticks;

    [Fact]
    public void Log_MergesBothRingsByTime_WithStacksIndented()
    {
        var mod = new[]
        {
            new LogEntry
            {
                Wall = At(10, 0, 0, 0),
                Level = "Info",
                Data = "first",
            },
            new LogEntry
            {
                Wall = At(10, 0, 2, 0),
                Level = "Info",
                Data = "third",
            },
        };
        var unity = new[]
        {
            new LogEntry
            {
                Wall = At(10, 0, 1, 500),
                Level = "Exception",
                Data = "NullReferenceException",
                Stack = "A.B ()\nC.D ()\n",
            },
        };
        var text = BugReportText.Log(mod, unity);
        var lines = text.Split('\n');
        Assert.Equal("10:00:00.000 [Info] first", lines[0]);
        Assert.Equal("10:00:01.500 [Exception] NullReferenceException", lines[1]);
        Assert.Equal("    A.B ()", lines[2]);
        Assert.Equal("    C.D ()", lines[3]);
        Assert.Equal("10:00:02.000 [Info] third", lines[4]);
    }

    [Fact]
    public void Log_NullData_Empty()
    {
        var text = BugReportText.Log(
            new[]
            {
                new LogEntry { Wall = At(1, 2, 3, 4), Level = "Info" },
            },
            Array.Empty<LogEntry>()
        );
        Assert.Equal("01:02:03.004 [Info] \n", text);
    }

    [Fact]
    public void Compose_SectionsInOrder_ThenRefusalsNotesAndMarkers()
    {
        var info = new BugReportInfo { Saved = new DateTime(2026, 10, 23, 14, 3, 22) };
        info.Add("Build", "mod", "PeakXR 0.1.0+abc");
        info.Add("Player", "grounded", "True");
        info.Add("Build", "game", "1.2");
        info.Refusals.Add(
            new RefusalEntry
            {
                Wall = At(14, 3, 1, 250),
                Left = true,
                Reason = DenyReason.OutOfReach,
                At = new Vector3(1f, 2f, 3f),
            }
        );
        info.GripNotes[1] = "R grip GrabItem";
        info.GripNoteWalls[1] = At(14, 3, 2, 0);
        info.Markers.Add(new MarkerEntry { Wall = At(14, 3, 5, 0), Number = 1 });

        var text = BugReportText.Compose(info);

        Assert.StartsWith("PeakXR bug report\nSaved 2026-10-23 14:03:22\n", text);
        int build = text.IndexOf(
            "[Build]\nmod: PeakXR 0.1.0+abc\ngame: 1.2\n",
            StringComparison.Ordinal
        );
        int player = text.IndexOf("[Player]\ngrounded: True\n", StringComparison.Ordinal);
        int refusals = text.IndexOf(
            "14:03:01.250 left OutOfReach at (1.00, 2.00, 3.00)",
            StringComparison.Ordinal
        );
        int grips = text.IndexOf("right: 14:03:02.000 R grip GrabItem", StringComparison.Ordinal);
        int markers = text.IndexOf("#1 14:03:05.000", StringComparison.Ordinal);
        Assert.True(build > 0, text);
        Assert.True(player > build, text);
        Assert.True(refusals > player, text);
        Assert.True(grips > refusals, text);
        Assert.True(markers > grips, text);
        Assert.Contains("left: none", text);
    }

    [Fact]
    public void Compose_EmptyLists_SayNone()
    {
        var text = BugReportText.Compose(new BugReportInfo());
        Assert.Contains("[Refusals, oldest first]\nnone\n", text);
        Assert.Contains("[Markers]\nnone\n", text);
    }

    [Fact]
    public void FolderName_Stamp_ThenSuffixWhenTaken()
    {
        var now = new DateTime(2026, 10, 23, 14, 3, 22);
        Assert.Equal("20261023-140322", BugReportText.FolderName(now, _ => false));
        var taken = new HashSet<string> { "20261023-140322", "20261023-140322-2" };
        Assert.Equal("20261023-140322-3", BugReportText.FolderName(now, taken.Contains));
    }

    static readonly KeyValuePair<string, string>[] Names =
    {
        new("Al", "player 2"),
        new("Ann Lee", "player 1"),
        new("Ann", "player 3"),
    };

    [Fact]
    public void Redact_WholeNamesOnly_LongestFirst()
    {
        var text = BugReportText.Redact("Character [Ann Lee : 1] met Ann; Already Al.", Names);
        Assert.Equal("Character [player 1 : 1] met player 3; Already player 2.", text);
    }

    [Fact]
    public void Redact_SteamIds()
    {
        var text = BugReportText.Redact(
            "id 76561198012345678 here",
            Array.Empty<KeyValuePair<string, string>>()
        );
        Assert.Equal("id [steam id] here", text);
    }

    [Fact]
    public void Redact_HomeFolderUserNames()
    {
        var none = Array.Empty<KeyValuePair<string, string>>();
        Assert.Equal(
            @"Z:\home\[user]\.local\share\Steam and C:\Users\[user]\AppData /home/[user]/x",
            BugReportText.Redact(
                @"Z:\home\sam\.local\share\Steam and C:\Users\Sam K\AppData /home/sam/x",
                none
            )
        );
    }

    [Fact]
    public void Redact_SkipsEmptyNames()
    {
        var text = BugReportText.Redact("abc", new[] { new KeyValuePair<string, string>("", "x") });
        Assert.Equal("abc", text);
    }
}
