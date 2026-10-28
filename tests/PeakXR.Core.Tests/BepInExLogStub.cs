namespace BepInEx.Logging;

/// <summary>Stands in for BepInEx's log source in the linked preloader sources (see the csproj).</summary>
internal sealed class ManualLogSource(string name) : IDisposable
{
    public string Name { get; } = name;

    public void LogDebug(object data) => Write(data);

    public void LogInfo(object data) => Write(data);

    public void LogWarning(object data) => Write(data);

    public void LogError(object data) => Write(data);

    void Write(object data) => Console.WriteLine($"[{Name}] {data}");

    public void Dispose() { }
}
