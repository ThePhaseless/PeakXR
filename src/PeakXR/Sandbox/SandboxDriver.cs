using UnityEngine;
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
            SandboxWindow.IsOpen = !SandboxWindow.IsOpen;

        if (Time.unscaledTime >= nextGodTick)
        {
            nextGodTick = Time.unscaledTime + GodInterval;
            SandboxActions.GodTick();
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
