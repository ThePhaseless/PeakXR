using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.Interact.Physical;
using PeakXR.UI.Affordances;
using PeakXR.UI.Common;
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
            notes.Add(
                $"{topic}: {demo.SoundsPlayed} cue sounds, {d:0.00} m past its row's edge, {DemoInView(demo, topic, fails)}"
            );
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

        var fails = new List<string>();
        var zoneNote = PracticeZoneCheck(run, stationAt, fails);

        BeginHands();
        bool turnHand = VRInput.LeftHanded;
        for (int i = 0; i < 3 && run != null && run.Flow.Completed == 0; i++)
        {
            Script(turnHand).Stick = new Vector2(1f, 0f);
            yield return Wait(0.3f);
            Script(turnHand).Stick = Vector2.zero;
            yield return Wait(0.5f);
        }
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
                zoneNote,
            }
        );
    }

    /// <summary>
    /// vr.31's layout check at the first station: the basics zone is where the player stands, its demo within 2 m and
    /// not inside scenery, the climb zone is tied to the airport's climbing wall (RopeAnchorWithRope), and every zone
    /// has room for a body. The note lists each zone's anchor and distance from the start.
    /// </summary>
    static string PracticeZoneCheck(PracticeRun run, Transform stationAt, List<string> fails)
    {
        var head = VRRig.Instance.Head.position;
        if (!run.TryZone(PracticeZone.Basics, out var start))
        {
            fails.Add("practice zones not laid out at the first station");
            return "no zones";
        }
        if (PracticeLayout.Flat(start.Feet - head).magnitude > 0.6f)
            fails.Add("basics zone is not where the player stands");
        if (stationAt != null)
        {
            if ((stationAt.position - head).magnitude > 2f)
                fails.Add(F($"first demo {(stationAt.position - head).magnitude:0.0} m away"));
            if (PracticeLayout.InScenery(stationAt.position, 0.1f))
                fails.Add("first demo inside scenery");
        }
        var note = new System.Text.StringBuilder("zones:");
        for (var z = PracticeZone.Basics; z <= PracticeZone.Social; z++)
        {
            run.TryZone(z, out var spot);
            // Only spots the layout chose: the start (and a zone that fell back onto it) is where the body already
            // stands, and a sphere there touches the walls of a cramped spawn or the held item, not a bad spot.
            if (
                spot.Feet != start.Feet
                && PracticeLayout.InScenery(spot.Feet + Vector3.up * 1.3f, 0.25f)
            )
                fails.Add($"{z} zone spot inside scenery");
            note.Append(
                F(
                    $" {z} {PracticeLayout.Flat(spot.Feet - start.Feet).magnitude:0.0} m ({spot.Anchor});"
                )
            );
        }
        run.TryZone(PracticeZone.Climb, out var climb);
        if (!climb.Fixed && FindAnyObjectByType<RopeAnchorWithRope>() != null)
            fails.Add("climb zone not at the airport's climbing wall");
        return note.ToString();
    }

    /// <summary>
    /// vr.30: the demo draws something, and its drawn bounds sit in the player's view at a size they can follow
    /// (<see cref="Legibility"/>); a demo that only exists and plays sounds would pass otherwise. The frame alone
    /// cannot tell: <see cref="ShotAt"/> re-aims the camera at it.
    /// </summary>
    static string DemoInView(GhostDemo demo, GuideTopic topic, List<string> fails)
    {
        var bounds = default(Bounds);
        bool any = false;
        // The figure's body: the material most of its parts draw with (torso, arms, rings).
        var uses = new Dictionary<Material, int>();
        Material body = null;
        foreach (var r in demo.Root.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled)
                continue;
            if (any)
                bounds.Encapsulate(r.bounds);
            else
                bounds = r.bounds;
            any = true;
            var m = r.sharedMaterial;
            if (m == null)
                continue;
            uses[m] = uses.TryGetValue(m, out int n) ? n + 1 : 1;
            if (body == null || uses[m] > uses[body])
                body = m;
        }
        if (!any)
        {
            fails.Add($"{topic} demo draws nothing");
            return "nothing drawn";
        }
        var head = VRRig.Instance.Head;
        var to = bounds.center - head.position;
        // A figure, not a sheet: it has no face to tilt away, so it faces along the line of sight.
        var why = Legibility.Check(
            head.position.ToNumerics(),
            head.forward.ToNumerics(),
            bounds.center.ToNumerics(),
            to.ToNumerics(),
            bounds.size.y,
            out float degrees,
            out float distance,
            out float off,
            out _
        );
        if (why != null)
            fails.Add($"{topic} demo {why}");
        // Fully shown, the body draws at GhostDemo.Ghost's 0.32 alpha times the demo fade (1 while it plays):
        // under 0.25 the fade never came in. Contrast against the scene behind is not measured.
        float alpha = body != null ? body.color.a : 0f;
        if (alpha < 0.25f)
            fails.Add(F($"{topic} demo body faded (alpha {alpha:0.00})"));
        return F(
            $"drawn {degrees:0.0} deg tall at {distance:0.00} m, {off:0} deg off the view, body alpha {alpha:0.00}"
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
        // Glyphs, names and the hold ring are drawn by the target prompt: run with it on, as a player has it.
        var prompt = SpatialFeature.Settings.TargetPrompt;
        yield return WithOn("vr.121-prompt-glyphs", 25f, PromptGlyphStep(), prompt);
        yield return WithOn("vr.125-hold-glyph", 40f, HoldGlyphStep(), prompt);
        yield return WithOn("vr.133-target-names", 25f, TargetNamesStep(false), prompt);
        yield return WithOn("vr.221-colorblind-names", 25f, TargetNamesStep(true), prompt);
        var names = SpatialFeature.Settings.TargetNames;
        yield return WithOn("vr.240-text-scale", 25f, TextScaleStep(), prompt, names);
        yield return WithOn("vr.242-prompt-occlusion", 25f, PromptOcclusionStep(), prompt, names);
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
        // Put back after the step, a timeout too (Restoring): Guard never runs a dropped step's finally.
        if (hintText != null)
        {
            hintText.Value = false;
            bodyRestore += () => hintText.Value = hintWas;
        }
        yield return WarpFacing(lug.Center(), 0.8f);
        var top = LidPoint(lug);
        yield return MoveTo(left, () => top, 0.5f);
        float mark = CueMark();
        yield return GripTo(left, 1f, 0.1f);
        yield return WaitFor(() => ReferenceEquals(interaction.currentHeldInteractible, lug), 2f);
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
        // Read as the player does, looking at the suitcase they hold open.
        string read = GlyphReadable(
            "hold ring",
            PromptVerb.Hold,
            lug.Center() - VRRig.Instance.Head.position,
            fails
        );
        notes.Add($"hold ring {ring}, text hidden {hidden}, {read}");
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
        var head = VRRig.Instance.Head;
        var spot = head.position + Flat(head.forward) * 0.8f + Vector3.down * 0.2f;
        var world = SpawnTestItem(AppleFragment, spot);
        if (world == null)
            yield break;
        var gui = GUIManager.instance;
        var verbText = gui.interactPromptText.canvasRenderer;
        var fails = new List<string>();
        var notes = new List<string>();
        var hintText = AffordanceFeature.ActionHintText;
        bool hintWas = hintText != null && hintText.Value;
        // Off until the step ends, the struggle check included (its glyph stands in for the text only with the
        // text off); put back after the step, a timeout too (Restoring).
        if (hintText != null)
        {
            hintText.Value = false;
            bodyRestore += () => hintText.Value = hintWas;
        }
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

        // The grab glyph shows only beside the name (PromptGlyph.InPlace): with names on, it must read from the eye.
        var names = SpatialFeature.Settings.TargetNames;
        bool namesWas = names.Value;
        if (!namesWas)
        {
            names.Value = true;
            bodyRestore += () => names.Value = namesWas;
        }
        for (float t = Now + 0.3f; Now < t; )
        {
            HoldOnHeadRay(world, head);
            yield return null;
        }
        // The Apple sits on the head's ray, so the gaze is the view.
        notes.Add(GlyphReadable("grab glyph", PromptVerb.Grab, head.forward, fails));
        names.Value = namesWas;

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
        var head = VRRig.Instance.Head;
        var c = Character.localCharacter;
        var right = Vector3.Cross(Vector3.up, Flat(head.forward)).normalized;
        var spot = head.position + Flat(head.forward) * 0.35f + right * 0.3f;
        spot.y = c.Center.y;
        var world = SpawnTestItem(AppleFragment, spot);
        if (world == null)
            yield break;
        var gui = GUIManager.instance;
        var names = SpatialFeature.Settings.TargetNames;
        bool namesWas = names.Value;
        var cb = SpatialFeature.Settings.ColorBlindNames;
        var cbWas = cb.Value;
        var fails = new List<string>();
        var notes = new List<string>();
        // Put back after the step, a timeout too (Restoring).
        names.Value = false;
        // Names off means off here, whatever the game's colour-blind setting says.
        cb.Value = colourBlind ? ColorBlindNames.On : ColorBlindNames.Off;
        bodyRestore += () =>
        {
            names.Value = namesWas;
            cb.Value = cbWas;
        };
        if (colourBlind)
        {
            yield return HoverOnHeadRay(world, head);
            bool cbShown = NameShown(gui);
            notes.Add($"UI.TargetNames off, UI.ColorBlindNames On: name shown {cbShown}");
            if (!ReferenceEquals(Interaction.instance.currentHovered, world))
                fails.Add("the Apple never became the hovered target");
            else if (!cbShown)
                fails.Add("no name in colour-blind mode");
            yield return ShotAt("vr-221-colorblind-names", world.Center());
            Conclude(fails, notes);
            yield break;
        }
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

    /// <summary>
    /// vr.121, vr.125: the glyph for <paramref name="verb"/> is drawn where a player looking along
    /// <paramref name="gaze"/> can make it out (<see cref="Readable(string, Vector3, Vector3, float, float, Vector3, List{string})"/>):
    /// its stroke points' larger span as its size, its white stroke's opacity. <c>Drawn</c> alone passes a glyph posed
    /// off-screen or at zero size.
    /// </summary>
    static string GlyphReadable(string what, PromptVerb verb, Vector3 gaze, List<string> fails)
    {
        var root = TargetPrompt.DrawnRoot(verb);
        if (root == null)
        {
            fails.Add(what + " not drawn");
            return what + " not drawn";
        }
        float size = 0f,
            alpha = 0f;
        foreach (var line in root.GetComponentsInChildren<LineRenderer>())
        {
            if (!line.enabled || line.positionCount < 2)
                continue;
            var points = new Vector3[line.positionCount];
            line.GetPositions(points);
            var min = points[0];
            var max = points[0];
            foreach (var p in points)
            {
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            var t = line.transform;
            size = Mathf.Max(
                size,
                t.TransformVector(max.x - min.x, 0f, 0f).magnitude,
                t.TransformVector(0f, max.y - min.y, 0f).magnitude
            );
            alpha = Mathf.Max(alpha, line.sharedMaterial.color.a * line.startColor.a);
        }
        return Readable(what, root.position, root.forward, size, alpha, gaze, fails);
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
