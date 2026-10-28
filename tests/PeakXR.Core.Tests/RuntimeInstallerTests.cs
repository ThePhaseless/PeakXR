using System.IO;
using BepInEx.Logging;
using PeakXR.Preloader;

namespace PeakXR.Core.Tests;

/// <summary>The preloader's file sync, on a temp game data folder: it must never leave a manifest pointing at a
/// native plugin it did not put there, and must only remove files whose bytes are still its own.</summary>
public sealed class RuntimeInstallerTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "peakxr-installer-" + Guid.NewGuid());
    readonly string data;
    readonly string deps;
    readonly ManualLogSource log = new("test");

    public RuntimeInstallerTests()
    {
        data = Directory.CreateDirectory(Path.Combine(root, "PEAK_Data")).FullName;
        deps = Path.Combine(root, "RuntimeDeps");
        var openxr = Directory.CreateDirectory(Path.Combine(deps, "OpenXR")).FullName;
        File.WriteAllText(
            Path.Combine(openxr, RuntimeInstaller.ManifestFileName),
            "{ \"libraryName\": \"UnityOpenXR\" }"
        );
        File.WriteAllText(Path.Combine(openxr, "UnityOpenXR.dll"), "ours");
        File.WriteAllText(Path.Combine(openxr, "openxr_loader.dll"), "loader");
    }

    public void Dispose()
    {
        log.Dispose();
        Directory.Delete(root, true);
    }

    string Dll(string name) => Path.Combine(data, "Plugins", "x86_64", name);

    string Manifest =>
        Path.Combine(data, "UnitySubsystems", "UnityOpenXR", RuntimeInstaller.ManifestFileName);

    string State => Path.Combine(data, RuntimeInstaller.StateFileName);

    RuntimeMode Apply(RuntimeMode mode) => new RuntimeInstaller(data, deps, log).Apply(mode);

    [Fact]
    public void InstallsDllsAndManifest()
    {
        Assert.Equal(RuntimeMode.OpenXR, Apply(RuntimeMode.OpenXR));

        Assert.Equal("ours", File.ReadAllText(Dll("UnityOpenXR.dll")));
        Assert.True(File.Exists(Manifest));
        Assert.Contains("UnityOpenXR.dll", File.ReadAllText(State), StringComparison.Ordinal);
    }

    [Fact]
    public void ForeignDllKeepsTheRuntimeOff()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Dll("x")));
        File.WriteAllText(Dll("openxr_loader.dll"), "another mod's loader");

        Assert.Equal(RuntimeMode.Disabled, Apply(RuntimeMode.OpenXR));

        Assert.Equal("another mod's loader", File.ReadAllText(Dll("openxr_loader.dll")));
        Assert.False(File.Exists(Manifest));
    }

    [Fact]
    public void ForeignDllRemovesTheManifestOfAnEarlierInstall()
    {
        Apply(RuntimeMode.OpenXR);
        File.WriteAllText(Dll("openxr_loader.dll"), "replaced by the user");

        Assert.Equal(RuntimeMode.Disabled, Apply(RuntimeMode.OpenXR));

        Assert.False(File.Exists(Manifest));
        Assert.Equal("replaced by the user", File.ReadAllText(Dll("openxr_loader.dll")));
    }

    [Fact]
    public void DisabledRemovesWhatItInstalledButNotChangedFiles()
    {
        Apply(RuntimeMode.OpenXR);
        File.WriteAllText(Dll("openxr_loader.dll"), "edited");

        Assert.Equal(RuntimeMode.Disabled, Apply(RuntimeMode.Disabled));

        Assert.False(File.Exists(Dll("UnityOpenXR.dll")));
        Assert.False(File.Exists(Manifest));
        Assert.Equal("edited", File.ReadAllText(Dll("openxr_loader.dll")));
        Assert.False(File.Exists(State));
    }

    [Fact]
    public void FilesWrittenBeforeAFailureAreTracked()
    {
        // A folder where the manifest goes makes its copy throw after the DLLs are written.
        Directory.CreateDirectory(Manifest);

        Assert.ThrowsAny<Exception>(() => Apply(RuntimeMode.OpenXR));

        var state = File.ReadAllText(State);
        Assert.Contains("Plugins/x86_64/UnityOpenXR.dll", state, StringComparison.Ordinal);
        Assert.Contains("Plugins/x86_64/openxr_loader.dll", state, StringComparison.Ordinal);
    }
}
