using System.Collections;
using System.Collections.Generic;
using PeakXR.Core;
using PeakXR.UI.World;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Sandbox;

/// <summary>Menu legibility and lazy follow, from a tester's report on v0.1.1: "follows the head, but with a
/// terrible delay, and I think it's too small".</summary>
internal sealed partial class SelfTestRunner
{
    /// <summary>Frame and wait slack on top of <see cref="LazyFollow.CatchUpBudget"/> in game.</summary>
    const float CatchUpSlack = 0.2f;

    /// <summary>Share of <see cref="ReadableText.MinMenuDegrees"/> the smallest visible text may fall to: labels the
    /// game auto-sizes down and text near a 67 degree panel's edge, seen obliquely (cos 33 deg = 0.84).</summary>
    const float SmallestShare = 0.75f;

    static readonly List<float> textDegrees = new();

    /// <summary>
    /// vr.248: the pause menu's text, on its main page and its settings page, measured as drawn from the eye (em
    /// height in degrees): the median at least <see cref="ReadableText.MinMenuDegrees"/> and the smallest at least
    /// <see cref="SmallestShare"/> of it. Then, with <c>UI.MenuFollow</c> LazyFollow, a scripted head sways
    /// +-10 degrees for 2 s (the menu must not move) and turns 60 degrees: the menu must be back in front within
    /// <see cref="LazyFollow.CatchUpBudget"/>.
    /// </summary>
    IEnumerator MenuFollowTextStep()
    {
        var gui = GUIManager.instance;
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        if (gui == null || c == null || rig == null || rig.Head == null)
        {
            Fail("no GUIManager, character or rig");
            yield break;
        }
        // Hands low and pointing down, so the laser is never on the menu (it holds the menu still).
        yield return HandsAside();
        c.input.pauseWasPressed = true;
        gui.UpdatePaused();
        yield return Until(
            () => GUIManager.InPauseMenu && FindAnyObjectByType<PauseMenuHandler>() != null,
            5f,
            "pause menu did not open"
        );
        if (!stepOk)
            yield break;
        var handler = FindAnyObjectByType<PauseMenuHandler>();
        var root = RootCanvasOf(handler);
        // Past the page's scale-in (ScaleOnEnable, 0.25 s) and the wall retest.
        yield return Wait(1f);
        var notes = new List<string>();
        string bad = JudgeText(root, rig.Head.position, "main page", notes);
        handler.TransistionToPage<PauseMenuSettingsMenuPage>();
        yield return Wait(1f);
        bad ??= JudgeText(root, rig.Head.position, "settings", notes);

        if (UIWorldFeature.Settings.MenuFollow.Value != MenuFollowMode.LazyFollow)
            notes.Add("follow not checked: UI.MenuFollow is Fixed");
        else if (bad == null)
        {
            var home = VRInput.HeadPosition;
            var level = VRInput.HeadRotation;
            try
            {
                ScriptHead(home, level);
                yield return Wait(0.3f);
                if (!WorldCanvases.TryMenuCentre(root, out var start))
                {
                    Fail("the pause menu is not a placed menu");
                    yield break;
                }
                for (float t0 = Now, t = 0f; t < 2f; t = Now - t0)
                {
                    ScriptHead(home, Quaternion.Euler(0f, 10f * Mathf.Sin(t * 6f), 0f) * level);
                    yield return null;
                }
                WorldCanvases.TryMenuCentre(root, out var swayed);
                float drift = Vector3.Distance(start, swayed);
                if (drift > 0.01f)
                    bad = $"the menu moved {drift:0.000} m while the head swayed +-10 deg";

                ScriptHead(home, Quaternion.Euler(0f, 60f, 0f) * level);
                float turned = Now;
                yield return WaitFor(() => MenuOffHead(root, rig.Head) <= 3f, 3f);
                float latency = Now - turned;
                float off = MenuOffHead(root, rig.Head);
                notes.Add(
                    $"swayed +-10 deg: moved {drift:0.000} m; turned 60 deg: back in front in {latency:0.00} s "
                        + $"({off:0.0} deg off)"
                );
                if (bad == null && latency > LazyFollow.CatchUpBudget + CatchUpSlack)
                    bad =
                        $"the menu took {latency:0.00} s to follow a 60 deg turn "
                        + $"(budget {LazyFollow.CatchUpBudget:0.00} s; {off:0.0} deg off at the end)";
            }
            finally
            {
                ReleaseHead();
            }
        }
        if (bad != null)
            Fail(bad + "; " + string.Join("; ", notes));
        else
            stepNote = string.Join("; ", notes);
    }

    /// <summary>Degrees between the head's heading and the menu centre's, seen from the head; 180 when it is not
    /// placed.</summary>
    static float MenuOffHead(Canvas root, Transform head)
    {
        if (root == null || !WorldCanvases.TryMenuCentre(root, out var centre))
            return 180f;
        var to = centre - head.position;
        return Vector3.Angle(Flat(head.forward), Flat(to));
    }

    /// <summary>Em heights in degrees of the visible text under <paramref name="root"/>, as drawn from
    /// <paramref name="eye"/>; a note with the smallest and the median, and a failure under the floors.</summary>
    static string JudgeText(Canvas root, Vector3 eye, string page, List<string> notes)
    {
        if (root == null)
            return "no pause menu canvas";
        textDegrees.Clear();
        TMPro.TMP_Text smallest = null;
        float least = float.MaxValue;
        foreach (var text in root.GetComponentsInChildren<TMPro.TMP_Text>())
        {
            if (
                string.IsNullOrWhiteSpace(text.text)
                || text.color.a * text.canvasRenderer.GetInheritedAlpha() < 0.1f
            )
                continue;
            float deg = ReadableText.Degrees(
                text.fontSize * text.transform.lossyScale.y,
                Vector3.Distance(eye, text.transform.position)
            );
            textDegrees.Add(deg);
            if (deg < least)
            {
                least = deg;
                smallest = text;
            }
        }
        if (textDegrees.Count == 0)
            return $"{page}: no visible text";
        textDegrees.Sort();
        float median = textDegrees[textDegrees.Count / 2];
        notes.Add(
            $"{page}: text em smallest {least:0.00} deg ('{smallest.name}'), median {median:0.00} deg of "
                + textDegrees.Count
        );
        if (median < ReadableText.MinMenuDegrees)
            return $"{page}: median text {median:0.00} deg < {ReadableText.MinMenuDegrees:0.00}";
        if (least < SmallestShare * ReadableText.MinMenuDegrees)
            return $"{page}: '{smallest.name}' text is {least:0.00} deg tall "
                + $"< {SmallestShare * ReadableText.MinMenuDegrees:0.00}";
        return null;
    }
}
