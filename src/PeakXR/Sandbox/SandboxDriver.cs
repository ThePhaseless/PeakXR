using PeakXR.VR;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zorro.Core.CLI;

namespace PeakXR.Sandbox;

/// <summary>
/// Per-frame glue for the sandbox: console unlock, window hotkey, god-mode timer and the IMGUI window.
/// Kept allocation-free; the checks are a few static reads.
/// </summary>
internal sealed class SandboxDriver : MonoBehaviour
{
    const float GodInterval = 0.25f;

    SandboxWindowHost host;
    bool wasActive;
    bool commandsRegistered;
    float nextGodTick;

    void Awake()
    {
        host = gameObject.AddComponent<SandboxWindowHost>();
        host.enabled = false;
        SceneManager.activeSceneChanged += OnScene;
    }

    void OnDestroy() => SceneManager.activeSceneChanged -= OnScene;

    // Sandbox run settings belong to the run they were set in; back at the title the next run starts clean. The
    // progress guard's run mark ends at the title or the airport too (SandboxRunMark).
    static void OnScene(Scene previous, Scene next)
    {
        if (next.name == "Title")
            SandboxActions.RestoreRunSettings();
        bool wasMarked = SandboxActions.RunMark.Marked;
        SandboxActions.RunMark.SceneLoaded(next.name, SandboxActions.GodMode);
        if (wasMarked && !SandboxActions.RunMark.Marked)
            Plugin.Log.LogInfo($"[sandbox] new run at {next.name}: progress is saved again");
    }

    void Update()
    {
        Tick();
        // A disabled behaviour gets no OnGUI calls, so IMGUI costs nothing while the window is closed.
        if (host.enabled != SandboxWindow.IsOpen)
            host.enabled = SandboxWindow.IsOpen;
    }

    void Tick()
    {
        if (!SandboxActions.Enabled && !wasActive)
            return;

        bool active = SandboxActions.Active;
        if (active != wasActive)
        {
            wasActive = active;
            // Only touch the flag on a change so another console-unlock mod is not overridden every frame.
            DebugUIHandler.AllowOpen = active;
            if (!active)
            {
                SandboxActions.Deactivate();
                SandboxWindow.IsOpen = false;
                return;
            }
        }
        if (!active)
            return;

        if (!commandsRegistered)
            commandsRegistered = SandboxCommands.Register();

        if (Input.GetKeyDown(SandboxFeature.WindowKeyEntry.Value))
        {
            // The IMGUI window draws only on the desktop mirror, yet it blocks input: in VR it would freeze the player.
            if (VRSession.Active && !SandboxWindow.IsOpen)
                Plugin.Log.LogInfo(
                    "[sandbox] window key ignored in VR: the window shows only on the desktop; use the console."
                );
            else
                SandboxWindow.IsOpen = !SandboxWindow.IsOpen;
        }

        if (Time.unscaledTime >= nextGodTick)
        {
            nextGodTick = Time.unscaledTime + GodInterval;
            SandboxActions.GodTick();
            // The game's own console, which the sandbox unlocks, can spawn and warp too.
            if (DebugUIHandler.IsOpen)
                SandboxActions.MarkRun();
            SandboxActions.TellRunMark();
        }
    }
}

/// <summary>Draws the sandbox window; <see cref="SandboxDriver"/> enables it only while the window is open.</summary>
internal sealed class SandboxWindowHost : MonoBehaviour
{
    readonly SandboxWindow window = new();

    void OnGUI()
    {
        if (SandboxWindow.IsOpen)
            window.Draw();
    }
}
