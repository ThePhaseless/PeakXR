using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.Interact.Physical;
using PeakXR.UI.Affordances;
using PeakXR.UI.Guide;
using PeakXR.UI.Settings;
using PeakXR.UI.Spatial;
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
        yield return Guard(TutorialCleanup(), Now + 100f, TutorialCleanupFailed);
        yield return VRStep("vr.31-practice", 300f, PracticeStep());
        yield return Guard(TutorialCleanup(), Now + 100f, TutorialCleanupFailed);
    }

    static void TutorialCleanupFailed(System.Exception e) =>
        Plugin.Log.LogWarning(
            $"[selftest] tutorial cleanup: {(e == null ? "timeout" : e.Message)}"
        );

    /// <summary>
    /// After each step, passed or not: the scripted hands let go, the comfort-first view is off, a practice still
    /// running is stopped (it leaves to the title) and the title is back on its main page, so 01-airport starts clean.
    /// </summary>
    IEnumerator TutorialCleanup()
    {
        yield return HandsRelease();
        EndHands();
        GuideRows.ComfortFirst = false;
        if (PracticeRun.Current != null)
        {
            PracticeRun.Current.Stop();
            yield return WaitFor(() => !PracticeRun.Running, 60f);
            if (PracticeRun.Current != null)
            {
                Plugin.Log.LogWarning("[selftest] practice did not end; destroying it");
                Destroy(PracticeRun.Current.gameObject);
            }
        }
        yield return WaitFor(
            () =>
                InScene(MainMenu.SceneName)
                && !LoadingScreenHandler.loading
                && FindAnyObjectByType<MainMenuPageHandler>() != null,
            30f
        );
        var handler = FindAnyObjectByType<MainMenuPageHandler>();
        // MainMenuSettingsPage.BackClicked (MainMenuSettingsPage.cs:17) goes back the same way.
        if (handler != null && handler.currentPage is not MainMenuMainPage)
            handler.TransistionToPage<MainMenuMainPage>();
        yield return Wait(0.5f);
    }

    /// <summary>vr.30: the title's settings (MainMenuMainPage.cs:218), the VR tab on its VR Guide page (the demo list
    /// opened in the single-column fallback), and the Grab and Climb demos each played for a loop beside their row
    /// with their cue sounds.</summary>
    IEnumerator GuideStep()
    {
        yield return OpenTitleGuide();
        if (!stepOk)
            yield break;
        // The VR layout lists every demo on the guide page; the single-column fallback folds them behind Demos.
        var expand = GuideRowOf(GuideAction.Expand, GuideTopic.Count);
        if (expand == null && GuideRowOf(GuideAction.Practice, GuideTopic.Count) == null)
        {
            Fail("no VR Guide rows in the VR tab");
            yield break;
        }
        if (expand != null)
        {
            GuideRows.Click(expand);
            yield return Wait(1f);
        }

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
            var stage = GuideStage.Instance;
            var demo = stage != null ? stage.Demo : null;
            if (demo == null || !demo.Alive || demo.Topic != topic)
            {
                fails.Add(
                    $"{topic} demo not showing (showing {(demo != null ? demo.Topic.ToString() : "none")}, last stop: {(stage != null ? stage.LastStop : "no stage") ?? "none"})"
                );
                continue;
            }
            // The ghost stands 0.3 m past the row's outer edge, 0.15 m up and 0.08 m toward you (GuideStage.Place).
            VRTabGrid.RowOuterEdge(
                (RectTransform)row.Cell.transform,
                edgeCorners,
                out var edge,
                out _
            );
            float d = Vector3.Distance(demo.Root.position, edge);
            notes.Add($"{topic}: {demo.SoundsPlayed} cue sounds, {d:0.00} m past its row's edge");
            if (demo.SoundsPlayed < 1)
                fails.Add($"{topic} played no cue sound in a loop");
            if (d > 0.6f)
                fails.Add($"{topic} demo {d:0.00} m from its row's outer edge");
            // Row and demo together: the ghost stands past the panel's edge on the row's free side.
            yield return ShotAt(
                "vr-30-guide-" + topic,
                0.5f * (CentreOf(row.Cell.transform) + demo.Root.position)
            );
        }
        if (expand != null)
            GuideRows.Click(expand);
        Conclude(fails, notes);
    }

    /// <summary>vr.31: Practice, the comfort-first rows (turn mode, vignette strength, seated), Start; Play Solo loads the
    /// airport; the right stick flicks until the turn station passes; a hand rests in the exit orb; the title returns.</summary>
    IEnumerator PracticeStep()
    {
        // vr.30's cleanup put the title back on its main page: open the VR tab again (OpenTitleGuide: guide page 1).
        yield return OpenTitleGuide();
        if (!stepOk)
            yield break;
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
        var beginCanvas = RootCanvasOf(begin.Cell);
        yield return beginCanvas != null
            ? ShotAt("vr-31-comfort-first", CentreOf(beginCanvas.transform))
            : Shot("vr-31-comfort-first");
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
        var stationAt = PracticeStation(run);
        yield return ShotAt(
            "vr-31-station",
            stationAt != null ? stationAt.position : run.ExitOrbPosition
        );

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

    readonly Vector3[] edgeCorners = new Vector3[4];

    static AccessTools.FieldRef<PracticeRun, Transform> practiceStation;

    /// <summary>The practice's station root (its demo), for aiming the screenshot; null when the field moved.</summary>
    static Transform PracticeStation(PracticeRun run)
    {
        try
        {
            practiceStation ??= GameMember.Field<PracticeRun, Transform>("station");
            return practiceStation(run);
        }
        catch (System.MissingFieldException)
        {
            return null;
        }
    }

    /// <summary>The title's settings page (MainMenuMainPage.cs:218) on the VR tab, in its normal view.</summary>
    IEnumerator OpenTitleGuide()
    {
        var handler = FindAnyObjectByType<MainMenuPageHandler>();
        if (handler == null)
        {
            Fail("no MainMenuPageHandler on the title");
            yield break;
        }
        var page = handler.TransistionToPage<MainMenuSettingsPage>(new SetActivePageTransistion());
        yield return Wait(0.5f);
        GuideRows.ComfortFirst = false;
        // The VR layout's guide page, first page (Practice and the first demos).
        VRTab.Group = VRTab.GuideGroup;
        VRTab.Page = 0;
        if (!GuideStage.OpenVRTab(page != null ? page.SharedSettingsMenu : null))
        {
            Fail("VR tab not found on the title settings page");
            yield break;
        }
        yield return Wait(1f);
        int rows = FindObjectsByType<GuideRowHover>(FindObjectsSortMode.None).Length;
        Plugin.Log.LogInfo($"[selftest] title VR tab open, {rows} VR Guide rows");
        if (rows == 0)
            Fail("the VR tab has no VR Guide rows (see the [ui.guide] VR tab line)");
    }

    // ---------------------------------------------------------------- WP6 slice D: prompt glyphs (D8)

    /// <summary>Prompt steps for the level's hand steps; the lead wires <c>yield return PromptGlyphSteps();</c> into
    /// <c>CommsSteps</c>.</summary>
    IEnumerator PromptGlyphSteps()
    {
        yield return HandStep("vr.121-prompt-glyphs", 25f, PromptGlyphStep());
        yield return HandStep("vr.125-hold-glyph", 40f, HoldGlyphStep());
        yield return HandStep("vr.133-target-names", 25f, TargetNamesStep(false));
        yield return HandStep("vr.221-colorblind-names", 25f, TargetNamesStep(true));
        yield return WarpBack();
    }

    /// <summary>
    /// vr.125: a hand on the lid of the nearest closed suitcase starts its held open (Luggage is an
    /// <c>IInteractibleConstant</c>, Interaction.cs:120-123). With <c>UI.ActionHintText</c> off the OPEN text is hidden
    /// and the hold glyph (a ring) is drawn beside the open glyph; raising the lid halfway fills the ring as far as the
    /// game's own timer (<c>constantInteractableProgress</c>, Interaction.cs:57) and the hand feels the progress ramp.
    /// </summary>
    IEnumerator HoldGlyphStep()
    {
        if (!SpatialFeature.Settings.TargetPrompt.Value)
        {
            stepNote = "UI.TargetPrompt is off";
            yield break;
        }
        var c = Character.localCharacter;
        var lug = Nearest<Luggage>(c.Center, l => !l.IsOpen && l is not RespawnChest);
        if (lug == null)
        {
            stepSkip = "no closed suitcase in the active segment";
            yield break;
        }
        var interaction = Interaction.instance;
        var hintText = AffordanceFeature.ActionHintText;
        bool hintWas = hintText != null && hintText.Value;
        bool left = VRInput.LeftHanded;
        var notes = new List<string>();
        var fails = new List<string>();
        try
        {
            if (hintText != null)
                hintText.Value = false;
            yield return WarpFacing(lug.Center(), 0.8f);
            var top = LidPoint(lug);
            yield return MoveTo(left, () => top, 0.5f);
            float mark = CueMark();
            yield return GripTo(left, 1f, 0.1f);
            yield return WaitFor(
                () => ReferenceEquals(interaction.currentHeldInteractible, lug),
                2f
            );
            if (!ReferenceEquals(interaction.currentHeldInteractible, lug))
            {
                Fail(
                    $"grip did not hold the suitcase: hovered {Label(interaction.currentHovered)}, held {Label(interaction.currentHeldInteractible)}"
                );
                yield break;
            }
            yield return WaitFor(() => TargetPrompt.Drawn(PromptVerb.Hold), 1f);
            bool ring = TargetPrompt.Drawn(PromptVerb.Hold);
            bool hidden = TargetPrompt.TextHidden;
            notes.Add($"hold ring {ring}, text hidden {hidden}");
            if (!ring)
                fails.Add("no hold ring while the suitcase's open is held");
            if (!hidden)
                fails.Add("the prompt text still shows with UI.ActionHintText off");

            // Half the lid's open time (Luggage timeToOpen, Luggage.cs:214); the lift gesture caps the timer at the
            // height reached, so half the lift height.
            float openTime = GameTiming.InteractSeconds(lug, c);
            float until = Now + Mathf.Max(0.8f, 0.6f * openTime);
            var at = Script(left).Position;
            bool lift = Setting("Interaction", "LiftLidToOpen", true);
            while (Now < until && !lug.IsOpen)
            {
                if (lift)
                    SetHand(left, at + Vector3.up * (LidGesture.LiftHeight * 0.5f));
                yield return null;
            }
            float fill = TargetPrompt.HoldFillShown;
            float progress = Mathf.Clamp01(interaction.constantInteractableProgress);
            yield return ShotAt("vr-125-hold-glyph", lug.Center());
            notes.Add($"progress {progress:0.00}, ring filled {fill:0.00}");
            if (progress > 0.05f && (fill <= 0f || Mathf.Abs(fill - progress) > 0.1f))
                fails.Add("the ring does not follow the hold's progress");
            yield return ExpectFeel(left, CueFeel.Progress, mark);
            if (stepOk)
                notes.Add(cueNote);
            yield return GripTo(left, 0f, 0.1f);
        }
        finally
        {
            if (hintText != null)
                hintText.Value = hintWas;
        }
        if (stepOk)
            Conclude(fails, notes);
    }

    /// <summary>
    /// vr.121 (D8): an Apple on the head's interact ray (<c>Interaction.currentHovered</c>, Interaction.cs:15), with
    /// the defaults (<c>UI.TargetNames</c> and <c>UI.ActionHintText</c> off), shows neither the PICKUP text
    /// (Item.cs:680, GUIManager.cs:1049) nor its name nor a grab glyph: only the game's glow (<c>_Interactable</c> 1,
    /// Item.cs:1147). <c>UI.ActionHintText</c> brings the text back. Then a web (CharacterAfflictions.cs:803,
    /// the struggle prompt) draws the shake glyph on both wrists with a contact tick on each hand.
    /// </summary>
    IEnumerator PromptGlyphStep()
    {
        if (!SpatialFeature.Settings.TargetPrompt.Value)
        {
            stepNote = "UI.TargetPrompt is off";
            yield break;
        }
        var apple = FindItem(new List<Item>(SandboxActions.Items), AppleFragment);
        var head = VRRig.Instance.Head;
        var spot = head.position + Flat(head.forward) * 0.8f + Vector3.down * 0.2f;
        if (apple == null || !SandboxActions.SpawnItemAt(apple, spot))
        {
            Fail(apple == null ? "no Apple item" : SandboxActions.LastMessage);
            yield break;
        }
        spawned.Add(SandboxActions.LastSpawned);
        var world =
            SandboxActions.LastSpawned != null
                ? SandboxActions.LastSpawned.GetComponent<Item>()
                : null;
        if (world == null)
        {
            Fail("spawned Apple has no Item");
            yield break;
        }
        if (world.rig != null)
            world.rig.isKinematic = true;
        var gui = GUIManager.instance;
        var verbText = gui.interactPromptText.canvasRenderer;
        var fails = new List<string>();
        var notes = new List<string>();
        var hintText = AffordanceFeature.ActionHintText;
        bool hintWas = hintText != null && hintText.Value;
        try
        {
            if (hintText != null)
                hintText.Value = false;
            float until = Now + 5f;
            while (Now < until && !ReferenceEquals(Interaction.instance.currentHovered, world))
            {
                HoldOnHeadRay(world, head);
                yield return null;
            }
            if (!ReferenceEquals(Interaction.instance.currentHovered, world))
            {
                Fail("the Apple never became the hovered target");
                yield break;
            }
            for (float t = Now + 0.4f; Now < t; )
            {
                HoldOnHeadRay(world, head);
                yield return null;
            }
            bool hidden = TargetPrompt.TextHidden && verbText.GetAlpha() <= 0.001f;
            bool grab = TargetPrompt.Drawn(PromptVerb.Grab);
            bool namesOn = SpatialFeature.Settings.TargetNames.Value;
            bool named = NameShown(gui);
            float glow = Glow(world);
            notes.Add(
                $"text hidden {hidden}, grab glyph {grab}, name shown {named} (UI.TargetNames {namesOn}), glow {glow:0.##}"
            );
            if (!hidden)
                fails.Add("PICKUP text still drawn with UI.ActionHintText off");
            if (grab != namesOn)
                fails.Add(
                    namesOn
                        ? "no grab glyph with UI.TargetNames on"
                        : "grab glyph drawn with UI.TargetNames off"
                );
            if (named != namesOn)
                fails.Add(
                    namesOn
                        ? "the Apple's name is gone"
                        : "the Apple's name shows with UI.TargetNames off"
                );
            if (glow < 0.5f)
                fails.Add("the hovered Apple does not glow (_Interactable 0)");
            yield return ShotAt("vr-121-prompt-glyphs", world.Center());

            if (hintText != null)
            {
                hintText.Value = true;
                for (float t = Now + 0.3f; Now < t; )
                {
                    HoldOnHeadRay(world, head);
                    yield return null;
                }
                bool back = !TargetPrompt.TextHidden && verbText.GetAlpha() >= 0.99f;
                notes.Add(
                    $"with ActionHintText: text back {back}, glyph {TargetPrompt.Drawn(PromptVerb.Grab)}"
                );
                if (!back)
                    fails.Add("UI.ActionHintText on did not bring the PICKUP text back");
                if (TargetPrompt.Drawn(PromptVerb.Grab))
                    fails.Add("grab glyph drawn over the PICKUP text");
                hintText.Value = false;
            }
        }
        finally
        {
            if (hintText != null)
                hintText.Value = hintWas;
        }

        // Struggle: the game shows its prompt only for a web added from zero (CharacterAfflictions.cs:799-806).
        SandboxActions.ClearStatus(CharacterAfflictions.STATUSTYPE.Web);
        yield return Wait(0.2f);
        float mark = CueMark();
        if (!SandboxActions.AddStatus(CharacterAfflictions.STATUSTYPE.Web, 0.3f))
            fails.Add("the game refused a web: " + SandboxActions.LastMessage);
        else
        {
            yield return WaitFor(() => TargetPrompt.Drawn(PromptVerb.Struggle), 2f);
            bool shake = TargetPrompt.Drawn(PromptVerb.Struggle);
            notes.Add($"struggle glyph {shake}");
            if (!shake)
                fails.Add("no shake glyph on the wrists while webbed");
            yield return ExpectFeel(true, CueFeel.Contact, mark);
            if (stepOk)
                yield return ExpectFeel(false, CueFeel.Contact, mark);
            SandboxActions.ClearStatus(CharacterAfflictions.STATUSTYPE.Web);
            if (!stepOk)
                yield break;
            notes.Add(cueNote);
        }
        Conclude(fails, notes);
    }

    /// <summary>
    /// vr.133: an Apple off the head's ray, low beside you, with an empty hand reaching it (no grip): it glows through
    /// the game's highlight (<c>_Interactable</c> 1, Item.cs:1147) and, by default (<c>UI.TargetNames</c> off), no
    /// name shows; the hand moved away turns the glow off (Item.HoverExit, :1155). With <c>UI.TargetNames</c> on, the
    /// Apple on the head's ray shows its name.
    /// vr.221 (<paramref name="colourBlind"/>): with <c>UI.TargetNames</c> off and <c>UI.ColorBlindNames</c> On (as the
    /// game's colour-blind mode does with FollowGame), the Apple on the head's ray shows its name.
    /// </summary>
    IEnumerator TargetNamesStep(bool colourBlind)
    {
        if (!SpatialFeature.Settings.TargetPrompt.Value)
        {
            stepNote = "UI.TargetPrompt is off";
            yield break;
        }
        var apple = FindItem(new List<Item>(SandboxActions.Items), AppleFragment);
        var head = VRRig.Instance.Head;
        var c = Character.localCharacter;
        var right = Vector3.Cross(Vector3.up, Flat(head.forward)).normalized;
        var spot = head.position + Flat(head.forward) * 0.35f + right * 0.3f;
        spot.y = c.Center.y;
        if (apple == null || !SandboxActions.SpawnItemAt(apple, spot))
        {
            Fail(apple == null ? "no Apple item" : SandboxActions.LastMessage);
            yield break;
        }
        spawned.Add(SandboxActions.LastSpawned);
        var world =
            SandboxActions.LastSpawned != null
                ? SandboxActions.LastSpawned.GetComponent<Item>()
                : null;
        if (world == null)
        {
            Fail("spawned Apple has no Item");
            yield break;
        }
        if (world.rig != null)
            world.rig.isKinematic = true;
        var gui = GUIManager.instance;
        var names = SpatialFeature.Settings.TargetNames;
        bool namesWas = names.Value;
        var cb = SpatialFeature.Settings.ColorBlindNames;
        var cbWas = cb.Value;
        var fails = new List<string>();
        var notes = new List<string>();
        try
        {
            names.Value = false;
            // Names off means off here, whatever the game's colour-blind setting says.
            cb.Value = ColorBlindNames.Off;
            if (colourBlind)
            {
                cb.Value = ColorBlindNames.On;
                yield return HoverOnHeadRay(world, head);
                bool cbShown = NameShown(gui);
                notes.Add($"UI.TargetNames off, UI.ColorBlindNames On: name shown {cbShown}");
                if (!ReferenceEquals(Interaction.instance.currentHovered, world))
                    fails.Add("the Apple never became the hovered target");
                else if (!cbShown)
                    fails.Add("no name in colour-blind mode");
                yield return ShotAt("vr-221-colorblind-names", world.Center());
            }
            else
            {
                yield return Wait(0.3f);
                float before = Glow(world);
                var restAt = Script(false).Position;
                yield return MoveTo(false, () => world != null ? world.Center() : spot, 0.5f);
                yield return Wait(0.3f);
                float reached = Glow(world);
                bool headHover = ReferenceEquals(Interaction.instance.currentHovered, world);
                bool named = headHover && NameShown(gui);
                notes.Add(
                    $"glow {before:0.##} before, {reached:0.##} at the hand (head hover {headHover}), name shown {named}"
                );
                if (reached < 0.5f)
                    fails.Add("the Apple at the empty hand does not glow (_Interactable 0)");
                if (named)
                    fails.Add("the Apple's name shows with UI.TargetNames off");
                yield return ShotAt("vr-133-target-names", world.Center());
                yield return MoveTrack(false, restAt, 0.3f);
                yield return Wait(0.3f);
                float left = Glow(world);
                notes.Add($"glow {left:0.##} after the hand left");
                if (!ReferenceEquals(Interaction.instance.currentHovered, world) && left > 0.5f)
                    fails.Add("the Apple still glows after the hand left");

                names.Value = true;
                yield return HoverOnHeadRay(world, head);
                bool shown = NameShown(gui);
                notes.Add($"UI.TargetNames on: name shown {shown}");
                if (!ReferenceEquals(Interaction.instance.currentHovered, world))
                    fails.Add("the Apple never became the hovered target");
                else if (!shown)
                    fails.Add("no name with UI.TargetNames on");
            }
        }
        finally
        {
            names.Value = namesWas;
            cb.Value = cbWas;
        }
        Conclude(fails, notes);
    }

    /// <summary>Holds <paramref name="world"/> on the head's ray until it is the hovered target (5 s at most), then
    /// 0.4 s more for the prompt to settle.</summary>
    static IEnumerator HoverOnHeadRay(Item world, Transform head)
    {
        float until = Now + 5f;
        while (Now < until && !ReferenceEquals(Interaction.instance.currentHovered, world))
        {
            HoldOnHeadRay(world, head);
            yield return null;
        }
        for (float t = Now + 0.4f; Now < t; )
        {
            HoldOnHeadRay(world, head);
            yield return null;
        }
    }

    /// <summary>The target name is drawn: the game shows it (GUIManager.cs:1050) and ui.spatial has not hidden it.</summary>
    static bool NameShown(GUIManager gui) =>
        gui.interactName.activeInHierarchy && !TargetPrompt.NameHidden;

    static readonly MaterialPropertyBlock glowBlock = new();

    /// <summary>The game's highlight on <paramref name="item"/>: <c>_Interactable</c> in its main renderer's block
    /// (Item.HoverEnter, Item.cs:1145-1153).</summary>
    static float Glow(Item item)
    {
        if (item == null || item.mainRenderer == null)
            return 0f;
        item.mainRenderer.GetPropertyBlock(glowBlock);
        return glowBlock.GetFloat(Item.PROPERTY_INTERACTABLE);
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
        Plugin.Log.LogInfo($"[selftest] no VR Guide row {action} {topic} on screen");
        return null;
    }
}
