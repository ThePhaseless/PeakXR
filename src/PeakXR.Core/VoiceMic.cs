using System;
using System.Collections.Generic;

namespace PeakXR.Core;

/// <summary>
/// Picks the headset microphone in VR. The game defaults to the first capture device, which on a
/// PC with a headset is usually the desk or laptop mic; this finds a device whose name looks like an
/// HMD mic so it can be selected once, without touching a choice the player made themselves.
/// </summary>
public static class VoiceMic
{
    // Whole-word, case-insensitive. Generic words ("index", "headset") are left out on purpose: they
    // also name desk headsets and webcams. "Steam Streaming Microphone" is a silent phantom device on
    // most PCs, so it is never picked.
    private static readonly string[] headsetTokens =
    {
        "valve",
        "oculus",
        "quest",
        "rift",
        "vive",
        "reverb",
        "pico",
        "bigscreen",
        "virtual desktop",
        "alvr",
    };

    public static bool IsHeadsetMic(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        foreach (var token in headsetTokens)
        {
            if (ContainsWord(name, token))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Index of the device to switch to, or -1 to leave the setting alone. Only switches when the
    /// current selection is the game's default (first device, or none) and that default is not
    /// already a headset mic.
    /// </summary>
    public static int PreferredIndex(IReadOnlyList<string> devices, string currentId)
    {
        if (devices == null || devices.Count == 0)
            return -1;
        if (!string.IsNullOrEmpty(currentId) && currentId != devices[0])
            return -1;
        if (IsHeadsetMic(devices[0]))
            return -1;
        for (int i = 1; i < devices.Count; i++)
        {
            if (IsHeadsetMic(devices[i]))
                return i;
        }
        return -1;
    }

    private static bool ContainsWord(string text, string word)
    {
        int from = 0;
        while (from <= text.Length - word.Length)
        {
            int at = text.IndexOf(word, from, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
                return false;
            int end = at + word.Length;
            bool startOk = at == 0 || !char.IsLetter(text[at - 1]);
            bool endOk = end == text.Length || !char.IsLetter(text[end]);
            if (startOk && endOk)
                return true;
            from = at + 1;
        }
        return false;
    }
}
