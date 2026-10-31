namespace PeakXR.Core.Tests;

public class JsonLineTests
{
    [Fact]
    public void WritesFieldsInOrder()
    {
        var line = new JsonLine()
            .Str("name", "grab")
            .Num("t", 12.5)
            .Bool("ok", true)
            .Vec("pos", 1f, -2.25f, 3f)
            .Raw("extra", "[1,2]")
            .ToString();

        Assert.Equal(
            "{\"name\":\"grab\",\"t\":12.5,\"ok\":true,\"pos\":[1,-2.25,3],\"extra\":[1,2]}",
            line
        );
    }

    [Fact]
    public void EscapesQuotesBackslashesAndControlChars()
    {
        var line = new JsonLine().Str("note", "a \"b\" \\ c\nd\u0001").ToString();

        Assert.Equal("{\"note\":\"a \\\"b\\\" \\\\ c\\nd\\u0001\"}", line);
    }

    [Fact]
    public void NullStringAndNonFiniteNumberAreNull()
    {
        var line = new JsonLine().Str("a", null).Num("b", double.NaN).ToString();

        Assert.Equal("{\"a\":null,\"b\":null}", line);
    }

    [Fact]
    public void ReadsBackWhatItWrote()
    {
        var line = new JsonLine()
            .Str("name", "say \"hi\"")
            .Num("itemId", 42)
            .Vec("landing", 1.5f, 2f, -3f)
            .ToString();

        Assert.Equal("say \"hi\"", JsonLine.ReadString(line, "name"));
        Assert.True(JsonLine.TryReadNumber(line, "itemId", out var id));
        Assert.Equal(42, id);
        Assert.True(JsonLine.TryReadVec(line, "landing", out var x, out var y, out var z));
        Assert.Equal((1.5f, 2f, -3f), (x, y, z));
    }

    [Fact]
    public void MissingKeysReadAsAbsent()
    {
        var line = new JsonLine().Str("name", "x").Str("pos", null).ToString();

        Assert.Null(JsonLine.ReadString(line, "other"));
        Assert.False(JsonLine.TryReadNumber(line, "other", out _));
        Assert.False(JsonLine.TryReadVec(line, "pos", out _, out _, out _));
    }
}

public class MpDirTests
{
    [Fact]
    public void FlagWins()
    {
        var args = new[] { "PEAK.exe", "-mpobs", "-mpdir", "/srv/mp" };

        Assert.Equal("/srv/mp", MpDir.Resolve(args, "/home/u", "C:/fallback"));
    }

    [Fact]
    public void DefaultIsUnderTheFlatpakSteamDataDir()
    {
        Assert.Equal(
            "/home/u/.var/app/com.valvesoftware.Steam/peakxr-mp",
            MpDir.Resolve(new[] { "PEAK.exe" }, "/home/u/", "C:/fallback")
        );
    }

    [Fact]
    public void NoHomeAndNoFlagUsesFallback()
    {
        Assert.Equal(
            "C:/fallback",
            MpDir.Resolve(new[] { "PEAK.exe", "-mpdir" }, "", "C:/fallback")
        );
    }

    [Fact]
    public void UnixHomeComesFromHomeElseFromWinesHomeDir()
    {
        Assert.Equal("/home/u", MpDir.UnixHome("/home/u", @"\??\Z:\home\other"));
        Assert.Equal("/home/u", MpDir.UnixHome(null, @"\??\Z:\home\u"));
        Assert.Equal("/home/u", MpDir.UnixHome(@"C:\users\steamuser", @"\??\Z:\home\u"));
        Assert.Null(MpDir.UnixHome(null, null));
        Assert.Null(MpDir.UnixHome(null, @"C:\users\steamuser"));
    }

    [Theory]
    [InlineData("/home/u/mp", false, false, true)] // about to be created
    [InlineData("/home/u/mp", true, true, true)] // made by an earlier run
    [InlineData("/home/u/Documents", true, false, false)] // someone's folder
    [InlineData("/home/u/.var/app/com.valvesoftware.Steam/peakxr-mp", true, false, true)] // the default
    [InlineData(@"Z:\home\u\.var\app\com.valvesoftware.Steam\peakxr-mp\", true, false, true)]
    [InlineData("/", true, true, false)]
    [InlineData(@"C:\", true, true, false)]
    [InlineData("/home/u/", true, true, false)]
    [InlineData(@"Z:\home\u", true, true, false)]
    [InlineData("", false, false, false)]
    public void ClearsOldFilesOnlyInItsOwnFolder(
        string dir,
        bool exists,
        bool marked,
        bool clear
    ) => Assert.Equal(clear, MpDir.MayClear(dir, "/home/u", exists, marked));

    [Fact]
    public void UnixPathMapsToWineZDriveOnWindowsOnly()
    {
        Assert.Equal(@"Z:\home\u\mp", MpDir.ToNative("/home/u/mp", windows: true));
        Assert.Equal("/home/u/mp", MpDir.ToNative("/home/u/mp", windows: false));
        Assert.Equal(@"C:\mp", MpDir.ToNative(@"C:\mp", windows: true));
    }
}

public class LogCursorTests
{
    static string[] Lines(params string[] complete) =>
        (string.Join("\n", complete) + "\n").Split('\n');

    [Fact]
    public void ResumesAfterTheLinesAlreadyRead()
    {
        var cursor = new LogCursor();
        cursor.Sync(Lines("a1", "a2"));
        cursor.Next = 2;

        cursor.Sync(Lines("a1", "a2", "a3"));

        Assert.Equal(2, cursor.Next);
    }

    [Fact]
    public void StartsOverWhenTheFileWasWrittenAfresh()
    {
        // The previous run's lines, then the new run's file, already longer than the old count (#99).
        var cursor = new LogCursor();
        cursor.Sync(Lines("old1", "old2", "old3"));
        cursor.Next = 3;

        cursor.Sync(Lines("new1", "new2", "new3", "new4"));

        Assert.Equal(0, cursor.Next);
    }

    [Fact]
    public void AnEmptyFileKeepsThePlaceUntilItsFirstLine()
    {
        var cursor = new LogCursor();
        cursor.Sync(Lines("a1"));
        cursor.Next = 1;

        cursor.Sync([""]);
        Assert.Equal(1, cursor.Next);

        cursor.Sync(Lines("b1"));
        Assert.Equal(0, cursor.Next);
    }
}
