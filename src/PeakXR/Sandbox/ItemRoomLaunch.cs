using UnityEngine;
using UnityEngine.SceneManagement;

namespace PeakXR.Sandbox;

/// <summary>
/// The launch option <c>-peakxr-itemroom</c>: opens the item room once, as the VR tab's button would, as soon as the
/// title (or the solo airport) has been up for <see cref="Settle"/> seconds, so a tester skips the menus. Needs
/// <c>Sandbox.Enabled</c> like the button. Looks twice a second until then, and goes once it has tried.
/// </summary>
internal sealed class ItemRoomLaunch : MonoBehaviour
{
    public const string Argument = "-peakxr-itemroom";
    private const float Settle = 3f;
    private const float Every = 0.5f;
    private const float GiveUp = 600f;

    private float next;
    private float ready;

    public static void Install()
    {
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), Argument) < 0)
            return;
        var go = new GameObject("PeakXR_ItemRoomLaunch") { hideFlags = HideFlags.HideAndDontSave };
        DontDestroyOnLoad(go);
        go.AddComponent<ItemRoomLaunch>();
        Plugin.Log.LogInfo(
            $"[sandbox.room] {Argument}: opening the item room once the title is up"
        );
    }

    private void Update()
    {
        if (Time.unscaledTime < next)
            return;
        next = Time.unscaledTime + Every;
        if (ItemRoom.Current != null || Time.realtimeSinceStartup > GiveUp)
        {
            Destroy(gameObject);
            return;
        }
        var scene = SceneManager.GetActiveScene().name ?? "";
        bool here =
            !LoadingScreenHandler.loading
            && (
                (scene == MainMenu.SceneName && FindAnyObjectByType<MainMenu>() != null)
                || (
                    scene.Contains("Airport", System.StringComparison.Ordinal)
                    && Character.localCharacter != null
                )
            );
        ready = here ? ready + Every : 0f;
        if (ready < Settle)
            return;
        var c = Character.localCharacter;
        ItemRoom.Toggle(c != null ? c.Center : Vector3.zero, false);
        Destroy(gameObject);
    }
}
