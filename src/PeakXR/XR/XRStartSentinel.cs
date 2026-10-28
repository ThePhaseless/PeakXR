using System;
using System.IO;
using BepInEx;
using PeakXR.Core;
using UnityEngine.SceneManagement;
using Zorro.UI.Modal;

namespace PeakXR.XR;

/// <summary>
/// The file behind <see cref="XRStartGuard"/>: written before the XR runtime initializes, deleted once the session
/// runs, the start fails cleanly or the game quits. When two starts in a row never finished (a native crash in the
/// runtime), the next launch starts flat and says so once on the title screen.
/// </summary>
internal static class XRStartSentinel
{
    internal const string FileName = "PeakXR-xr-start.txt";

    private static string FilePath => Path.Combine(Paths.ConfigPath, FileName);

    /// <summary>Called once at load, before features install. True: start flat this launch.</summary>
    internal static bool StartFlat()
    {
        if (!XRStartGuard.StartFlat(Unfinished()))
            return false;
        Plugin.Log.LogError(
            $"[xr.bootstrap] VR is off for this launch: the last {XRStartGuard.FlatAfter} VR starts never finished "
                + $"(the game closed or crashed while the VR runtime started). Check SteamVR and the headset, then "
                + $"delete BepInEx/config/{FileName} to try VR again."
        );
        SceneManager.activeSceneChanged += ShowNotice;
        return true;
    }

    /// <summary>Before the runtime initializes.</summary>
    internal static void Starting()
    {
        try
        {
            File.WriteAllText(FilePath, XRStartGuard.Next(Unfinished()));
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[xr.bootstrap] Could not write {FileName}: {e.Message}");
        }
    }

    /// <summary>The session runs, the start failed cleanly, or the game quits.</summary>
    internal static void Finished()
    {
        try
        {
            File.Delete(FilePath);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[xr.bootstrap] Could not delete {FileName}: {e.Message}");
        }
    }

    private static int Unfinished()
    {
        try
        {
            return File.Exists(FilePath) ? XRStartGuard.Parse(File.ReadAllText(FilePath)) : 0;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[xr.bootstrap] Could not read {FileName}: {e.Message}");
            return 0;
        }
    }

    /// <summary>The game's own modal (Zorro Modal.OpenModal, Modal.cs:49), once, on the first title screen.</summary>
    private static void ShowNotice(Scene previous, Scene next)
    {
        if (next.name != "Title")
            return;
        SceneManager.activeSceneChanged -= ShowNotice;
        try
        {
            Modal.OpenModal(
                new DefaultHeaderModalOption(
                    "PeakXR: VR is off",
                    $"VR crashed the game on the last {XRStartGuard.FlatAfter} starts, so this launch is flat. "
                        + "Check that SteamVR (or your OpenXR runtime) and the headset work, then delete "
                        + $"BepInEx/config/{FileName} and restart to try VR again."
                ),
                new ModalButtonsOption([new ModalButtonsOption.Option("OK", null)])
            );
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning(
                $"[xr.bootstrap] Could not show the VR-off notice: {e.GetBaseException().Message}"
            );
        }
    }
}
