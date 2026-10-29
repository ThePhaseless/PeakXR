using PeakXR.VR;

namespace PeakXR.UI.Menu;

/// <summary>
/// Opens the game's pause menu when the VR runtime takes input focus during play (<c>UI.PauseOnDashboard</c>). Under
/// SteamVR, Oculus Touch controllers send their only menu button (the left one) to SteamVR's dashboard: the game never
/// sees the Menu tap, so the player had no way to pause (tester log: OpenXR VISIBLE focus losses mid-run and no pause).
/// With this the same button pauses: the dashboard opens over the game, and the pause menu waits behind it. It goes
/// through the game's own Esc path (A/GUIManager.cs:420 <c>UpdatePaused</c>), which keeps refusing over an open wheel
/// or the end screen.
/// </summary>
internal sealed class DashboardPause
{
    /// <summary>Focus at the last tick. False until the first tick of a session, which only reads it: the runtime
    /// restarting comes up unfocused (READY, SYNCHRONIZED), and a tick from before the stop must not read that as the
    /// player opening the dashboard.</summary>
    private bool hadFocus;

    /// <summary>VR (re)started: the next tick reads focus without acting on it.</summary>
    public void Reset() => hadFocus = false;

    public void Tick(bool enabled)
    {
        bool focus = VRSession.HasFocus;
        bool lost = hadFocus && !focus;
        hadFocus = focus;
        var c = Character.localCharacter;
        var gui = GUIManager.instance;
        if (
            !lost
            || !enabled
            || c == null
            || c.input == null
            || gui == null
            || GUIManager.InPauseMenu
        )
            return;
        c.input.pauseWasPressed = true;
        gui.UpdatePaused();
        // Refused (wheel, end screen): drop the press so it does not land later.
        c.input.pauseWasPressed = false;
        Plugin.Log.LogInfo(
            $"[ui.menu] runtime took focus mid-run: pause menu {(GUIManager.InPauseMenu ? "opened" : "refused by the game")}"
        );
    }
}
