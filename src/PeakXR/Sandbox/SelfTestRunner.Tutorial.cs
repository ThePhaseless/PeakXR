using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using PeakXR.Core;
using PeakXR.UI.Guide;
using PeakXR.UI.Settings;
using PeakXR.VR;
using UnityEngine;
using Zorro.UI;

namespace PeakXR.Sandbox;

/// <summary>
/// ui.guide steps, on the title before Play Solo: the VR Guide plays two demos beside the settings panel, then the
/// practice starts from the comfort-first view, scripted hands pass its first station (turning) and touch the exit
/// orb, and the run ends back on the title.
/// </summary>
internal sealed partial class SelfTestRunner
{
    IEnumerator TutorialSteps()
    {
        if (!Framework.FeatureManager.IsActive<GuideFeature>())
            yield break;
        yield return VRStep("vr.30-guide", 40f, GuideStep());
        yield return VRStep("vr.31-practice", 300f, PracticeStep());
    }

    /// <summary>vr.30: the title's settings (MainMenuMainPage.cs:215), the VR tab, the demo list opened, and the Grab and
    /// Climb demos each played for a loop beside their row with their cue sounds.</summary>
    IEnumerator GuideStep()
    {
        var handler = FindAnyObjectByType<MainMenuPageHandler>();
        if (handler == null)
        {
            Fail("no MainMenuPageHandler on the title");
            yield break;
        }
        var page = handler.TransistionToPage<MainMenuSettingsPage>(new SetActivePageTransistion());
        yield return Wait(0.5f);
        if (!GuideStage.OpenVRTab(page != null ? page.SharedSettingsMenu : null))
        {
            Fail("VR tab not found on the title settings page");
            yield break;
        }
        yield return Wait(1f);
        var expand = GuideRowOf(GuideAction.Expand, GuideTopic.Count);
        if (expand == null)
        {
            Fail("no VR Guide rows in the VR tab");
            yield break;
        }
        GuideRows.Click(expand);
        yield return Wait(1f);

        var fails = new List<string>();
        var notes = new List<string>();
        foreach (var topic in new[] { GuideTopic.Grab, GuideTopic.Climb })
        {
            var row = GuideRowOf(GuideAction.Demo, topic);
            if (row == null || row.Cell == null)
            {
                fails.Add($"no {topic} row");
                continue;
            }
            GuideRows.Click(row);
            yield return Wait(GuideTable.Get(topic).Loop + 0.3f);
            var demo = GuideStage.Instance != null ? GuideStage.Instance.Demo : null;
            if (demo == null || !demo.Alive || demo.Topic != topic)
            {
                fails.Add($"{topic} demo not showing");
                continue;
            }
            float d = Vector3.Distance(demo.Root.position, row.Cell.transform.position);
            notes.Add($"{topic}: {demo.SoundsPlayed} cue sounds, {d:0.00} m from its row");
            if (demo.SoundsPlayed < 1)
                fails.Add($"{topic} played no cue sound in a loop");
            if (d > 1.5f)
                fails.Add($"{topic} demo {d:0.00} m from its row");
            yield return Shot("vr-30-guide-" + topic);
        }
        GuideRows.Click(expand);
        Conclude(fails, notes);
    }

    /// <summary>vr.31: Practice, the comfort-first rows (turn mode, vignette strength, seated), Start; Play Solo loads the
    /// airport; the right stick flicks until the turn station passes; a hand rests in the exit orb; the title returns.</summary>
    IEnumerator PracticeStep()
    {
        var practice = GuideRowOf(GuideAction.Practice, GuideTopic.Count);
        if (practice == null)
        {
            Fail("no Practice row");
            yield break;
        }
        GuideRows.Click(practice);
        yield return Wait(1f);
        var turnMode = new ConfigDefinition("Comfort", "TurnMode");
        bool comfortRow =
            Plugin.Settings.ContainsKey(turnMode)
            && SettingPreviews.CellOf(Plugin.Settings[turnMode]) != null;
        var begin = GuideRowOf(GuideAction.Begin, GuideTopic.Count);
        if (!GuideRows.ComfortFirst || begin == null || !comfortRow)
        {
            Fail(
                $"comfort-first view missing (view {GuideRows.ComfortFirst}, turn row {comfortRow}, start row {begin != null})"
            );
            yield break;
        }
        yield return Shot("vr-31-comfort-first");
        GuideRows.Click(begin);
        yield return Until(() => PracticeRun.Running, 2f, "practice did not start");
        if (!stepOk)
            yield break;
        yield return Until(
            () =>
                PracticeRun.Current == null
                || PracticeRun.Current.Flow.Phase == PracticePhase.Station,
            170f,
            "practice never reached its first station"
        );
        if (!stepOk || PracticeRun.Current == null)
        {
            if (stepOk)
                Fail("practice ended before its first station");
            yield break;
        }
        var run = PracticeRun.Current;
        yield return Wait(0.5f);
        yield return Shot("vr-31-station");

        BeginHands();
        bool turnHand = VRInput.LeftHanded;
        for (int i = 0; i < 3 && run != null && run.Flow.Completed == 0; i++)
        {
            Script(turnHand).Stick = new Vector2(1f, 0f);
            yield return Wait(0.3f);
            Script(turnHand).Stick = Vector2.zero;
            yield return Wait(0.5f);
        }
        var fails = new List<string>();
        int completed = run != null ? run.Flow.Completed : 0;
        if (completed < 1)
            fails.Add("the turn station did not pass after three stick flicks");
        yield return WaitFor(() => run == null || run.Flow.Phase == PracticePhase.Station, 3f);
        var nextStation = run != null ? run.Flow.Current.ToString() : "none";

        // Off hand into the exit orb and held there until the dwell fires.
        bool offHand = !VRInput.LeftHanded;
        if (run != null)
        {
            yield return MoveTo(
                offHand,
                () => run != null ? run.ExitOrbPosition : Vector3.zero,
                0.6f
            );
            yield return WaitFor(() => run == null || run.Flow.Phase == PracticePhase.Exiting, 3f);
        }
        bool exiting = run == null || run.Flow.Phase == PracticePhase.Exiting;
        yield return HandsRelease();
        EndHands();
        if (!exiting)
            fails.Add("touching the exit orb did not end the practice");
        yield return WaitFor(
            () =>
                !PracticeRun.Running
                && InScene(MainMenu.SceneName)
                && !LoadingScreenHandler.loading
                && FindAnyObjectByType<MainMenu>() != null,
            60f
        );
        if (PracticeRun.Running || !InScene(MainMenu.SceneName))
            fails.Add("did not return to the title within 60 s");
        if (GuideRows.ComfortFirst)
            fails.Add("comfort-first view left on");
        yield return Wait(2f);
        Conclude(
            fails,
            new List<string>
            {
                $"{completed} station passed, next was {nextStation}, back on the title",
            }
        );
    }

    static GuideRow GuideRowOf(GuideAction action, GuideTopic topic)
    {
        foreach (var hover in FindObjectsByType<GuideRowHover>(FindObjectsSortMode.None))
        {
            var row = hover.Row;
            if (
                row != null
                && row.Action == action
                && (action != GuideAction.Demo || row.Topic == topic)
            )
                return row;
        }
        return null;
    }
}
