using System;
using System.IO;
using BepInEx;
using PeakXR.Core;
using UnityEngine.SceneManagement;
using Zorro.UI.Modal;

namespace PeakXR.XR;

/// <summary>
/// The file behind <see cref="XRStartGuard"/>: written once per launch right before the first runtime's native init
/// and deleted once a runtime runs and reports a frame shown (OpenVR's frame present count, OpenXR's VISIBLE or FOCUSED
/// state; at least three frames after the session is set up, else after <see cref="XRStartGuard.ShownTimeoutSeconds"/>
/// with a warning), or every runtime failed without crashing, or the game quits normally. Everything in between
/// counts: the native init and subsystem start of each runtime tried (OpenXR, then the OpenVR fallback), the wait for
/// the display (up to <c>XR.StartTimeoutSeconds</c> per runtime), the session setup and those first frames. So a process that ends in
/// that window any other way counts as a crashed start, a kill included (Steam's Stop, closing the window from the
/// task manager while it waits for the headset). After two such starts in a row, launches start flat and say so on
/// the title screen, until the VR tab's "Try VR again" deletes the file. Test and compat launches (<c>-peakxr-selftest</c>, <c>-peakxr-mptest</c>, <c>-peakxr-compat</c>) never
/// read or write it: their harness relaunches them and must not silently get a flat game.
/// </summary>
internal static class XRStartSentinel
{
    internal const string FileName = "PeakXR-xr-start.txt";

    private static readonly bool Ignored = IsTestLaunch(Environment.GetCommandLineArgs());

    private static string FilePath => Path.Combine(Paths.ConfigPath, FileName);

    /// <summary>This launch is flat because of the guard; the VR tab offers to try again.</summary>
    internal static bool Tripped { get; private set; }

    private static bool IsTestLaunch(string[] args) =>
        Array.IndexOf(args, "-peakxr-selftest") >= 0
        || Array.IndexOf(args, "-peakxr-mptest") >= 0
        || Array.IndexOf(args, "-peakxr-compat") >= 0;

    /// <summary>Called once at load, before features install. True: start flat this launch.</summary>
    internal static bool StartFlat()
    {
        if (Ignored || !XRStartGuard.StartFlat(Unfinished()))
            return false;
        Tripped = true;
        Plugin.Log.LogError(
            $"[xr.bootstrap] VR is off for this launch: the last {XRStartGuard.FlatAfter} launches ended while VR "
                + "was starting (a crash, or the game was killed). Check SteamVR and the headset, then press Try VR again in the VR settings "
                + $"tab (or delete BepInEx/config/{FileName}) and restart."
        );
        SceneManager.activeSceneChanged += ShowNotice;
        return true;
    }

    /// <summary>The VR tab's "Try VR again": forget the crashed starts, so VR starts on the next launch.</summary>
    internal static void Retry()
    {
        Finished();
        Tripped = false;
        Plugin.Log.LogInfo(
            "[xr.bootstrap] crash guard cleared from the VR tab; VR starts on the next launch"
        );
        Notice("PeakXR: VR on next launch", "Restart the game to start VR.");
    }

    /// <summary>Right before the first runtime's native init calls, once per launch.</summary>
    internal static void Starting()
    {
        if (Ignored)
            return;
        try
        {
            File.WriteAllText(FilePath, XRStartGuard.Next(Unfinished()));
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[xr.bootstrap] Could not write {FileName}: {e.Message}");
        }
    }

    /// <summary>The start came back: a runtime runs and reported a frame shown (or rendered until the timeout), every
    /// runtime failed cleanly, or the game quit normally.</summary>
    internal static void Finished()
    {
        if (Ignored)
            return;
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

    /// <summary>Once, on the first title screen.</summary>
    private static void ShowNotice(Scene previous, Scene next)
    {
        if (next.name != "Title")
            return;
        SceneManager.activeSceneChanged -= ShowNotice;
        Notice(
            "PeakXR: VR is off",
            $"The game closed while VR was starting on the last {XRStartGuard.FlatAfter} launches, so this launch is flat. Check that "
                + "SteamVR (or your OpenXR runtime) and the headset work, then press Try VR again in the VR "
                + "settings tab and restart."
        );
    }

    /// <summary>The game's own modal (Zorro Modal.OpenModal, Modal.cs:49).</summary>
    private static void Notice(string title, string text)
    {
        try
        {
            Modal.OpenModal(
                new DefaultHeaderModalOption(title, text),
                new ModalButtonsOption([new ModalButtonsOption.Option("OK", null)])
            );
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning(
                $"[xr.bootstrap] Could not show a notice: {e.GetBaseException().Message}"
            );
        }
    }
}
