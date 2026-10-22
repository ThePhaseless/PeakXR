using System.IO;

namespace PeakXR.Core.Tests;

public sealed class FileFingerprintTests : System.IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("peakxr-fp").FullName;

    string PathOf(string name) => Path.Combine(dir, name);

    public void Dispose() => Directory.Delete(dir, true);

    [Fact]
    public void UnchangedFileMatches()
    {
        var p = PathOf("save");
        File.WriteAllText(p, "run 1");
        var before = FileFingerprint.Of(p);
        Assert.True(before.Same(FileFingerprint.Of(p)));
    }

    [Fact]
    public void RewrittenContentDiffers()
    {
        var p = PathOf("save");
        File.WriteAllText(p, "run 1");
        var before = FileFingerprint.Of(p);
        File.WriteAllText(p, "run 2");
        Assert.False(before.Same(FileFingerprint.Of(p)));
    }

    [Fact]
    public void DeletedFileDiffers()
    {
        var p = PathOf("save");
        File.WriteAllText(p, "run 1");
        var before = FileFingerprint.Of(p);
        File.Delete(p);
        Assert.False(before.Same(FileFingerprint.Of(p)));
    }

    [Fact]
    public void MissingStaysMissing()
    {
        var p = PathOf("none");
        Assert.True(FileFingerprint.Of(p).Same(FileFingerprint.Of(p)));
        Assert.False(FileFingerprint.Of(p).Exists);
    }

    [Fact]
    public void CreatedFileDiffers()
    {
        var p = PathOf("save");
        var before = FileFingerprint.Of(p);
        File.WriteAllText(p, "new");
        Assert.False(before.Same(FileFingerprint.Of(p)));
    }
}
