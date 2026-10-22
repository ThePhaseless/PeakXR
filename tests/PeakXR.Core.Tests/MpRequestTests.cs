namespace PeakXR.Core.Tests;

public class MpRequestTests
{
    [Fact]
    public void RequestLine_RoundTrips()
    {
        var line = MpRequest.Line(3, MpRequest.PassOut, 100.5);
        Assert.True(MpRequest.TryRead(line, out int id, out string what, out double t));
        Assert.Equal(3, id);
        Assert.Equal(MpRequest.PassOut, what);
        Assert.Equal(100.5, t);
    }

    [Fact]
    public void AckLine_CarriesTheResult()
    {
        var line = MpRequest.Ack(4, MpRequest.Web, false, "airport", 7.0);
        Assert.True(MpRequest.TryReadAck(line, out int id, out bool ok));
        Assert.Equal(4, id);
        Assert.False(ok);
        Assert.Equal("airport", JsonLine.ReadString(line, "note"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{\"id\":1}")]
    [InlineData("{\"do\":\"passout\"}")]
    public void Incomplete_IsNotARequest(string line) =>
        Assert.False(MpRequest.TryRead(line, out _, out _, out _));

    [Fact]
    public void UnknownKind_IsReadButNotKnown()
    {
        Assert.True(MpRequest.TryRead(MpRequest.Line(1, "dance", 1), out _, out var what, out _));
        Assert.False(MpRequest.Known(what));
        Assert.True(MpRequest.Known(MpRequest.Wake));
    }
}
