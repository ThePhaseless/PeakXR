using System;
using UnityEngine.SceneManagement;

namespace PeakXR.UI.Guide;

/// <summary>Which scene is active, kept on scene change so per-frame checks read two flags instead of the scene name.
/// Scene names are soft hints only (design.md rule 4): the title and the solo airport.</summary>
internal static class GuideScene
{
    public static bool Title { get; private set; }

    public static bool Airport { get; private set; }

    public static void Install()
    {
        SceneManager.activeSceneChanged += Changed;
        Changed(default, SceneManager.GetActiveScene());
    }

    private static void Changed(Scene previous, Scene next)
    {
        var name = next.name ?? "";
        Title = name == MainMenu.SceneName;
        Airport = name.Contains("Airport", StringComparison.Ordinal);
    }
}
