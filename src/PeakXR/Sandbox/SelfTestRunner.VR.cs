using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using BepInEx.Logging;
using PeakXR.Avatar;
using PeakXR.Comfort;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.UI.Common;
using PeakXR.UI.Menu;
using PeakXR.UI.Settings;
using PeakXR.UI.World;
using PeakXR.VR;
using PeakXR.XR;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using Zorro.Core;
using Zorro.Settings;
using Vector3 = UnityEngine.Vector3;

namespace PeakXR.Sandbox;

/// <summary>
/// The <c>vr.*</c> steps of the self-test, run only when a VR runtime was requested. Nobody can look into the
/// headset, so each step proves a VR path from state the mod exposes (rig, canvases, comfort outputs) and leaves a
/// screenshot of the desktop mirror. If VR never starts, <c>vr.00-session</c> fails, the later VR steps are SKIP and
/// the script still gives full flat coverage.
/// </summary>
internal sealed partial class SelfTestRunner
{
    /// <summary>The comfort vignette's object under the rig head (VignetteMesh.Attach, Locomotion/VignetteMesh.cs).</summary>
    const string VignetteName = "PeakXR.ComfortVignette";

    /// <summary>Just over comfort.events' knockback threshold (50, ComfortEventsPatches.ForceAtPosition): a
    /// mild shove, so the speed-based launch detection does not hold the vignette as well.</summary>
    const float KnockbackForce = 60f;

    static readonly List<InputDevice> xrDevices = new();

    // Static: one runner per process, and CA1001 would want a MonoBehaviour to be IDisposable.
    static readonly BootstrapTail bootstrapTail = new();

    bool loadWatching;
    bool loadSeen;
    bool loadShot;
    int loadShownFrames;
    int loadCulledFrames;
    int loadOpenFrames;
    string loadError;

    /// <summary>A VR runtime was requested and XR may start (not the compat probe).</summary>
    static bool VRRun => VRSession.Requested != VRRuntime.Disabled && !VRSession.CompatProbe;

    IEnumerator VRStep(string id, float timeout, IEnumerator body)
    {
        if (!VRRun)
            yield break;
        if (!VRSession.Active)
        {
            Skip(id, "VR not active");
            yield break;
        }
        yield return Run(id, timeout, body);
    }

    /// <summary>vr.00: XR display running; what xr.bootstrap logs at activation (XRBootstrapDriver.Activate).</summary>
    IEnumerator Session()
    {
        float end = Now + 58f;
        while (!VRSession.Active)
        {
            if (Now > end)
            {
                Fail(
                    $"VR not active after 58 s (running {VRSession.Running}); last xr.bootstrap line: "
                        + (bootstrapTail.Last ?? "none")
                );
                yield break;
            }
            yield return null;
        }
        // Without focus xr.bootstrap pauses input and holds a 0.6 fade by design, so every input step would fail.
        yield return WaitFor(() => VRSession.HasFocus, Mathf.Max(1f, end - Now));
        if (!VRSession.HasFocus)
            Fail(
                $"runtime never gave the app input focus ({XRBootstrapDriver.FocusSource}): input paused, fade 0.6; "
                    + "close the runtime's dashboard or put the headset on (presence)"
            );
        InputDevices.GetDevices(xrDevices);
        var names = new StringBuilder();
        foreach (var d in xrDevices)
            names.Append(names.Length > 0 ? ", '" : "'").Append(d.name).Append('\'');
        stepNote =
            $"{VRSession.Running}, eye texture {XRSettings.eyeTextureWidth}x{XRSettings.eyeTextureHeight}, "
            + $"stereo mode {XRSettings.stereoRenderingMode}, {xrDevices.Count} device(s): {names}";
    }

    /// <summary>vr.01: rig, tracked head, stereo game camera, listener at the head (Rig/RigDriver.cs BindMainCamera).</summary>
    IEnumerator RigCheck()
    {
        yield return WaitFor(
            () =>
                VRRig.Instance != null
                && VRRig.Instance.Head != null
                && MainCamera.instance != null,
            10f
        );
        var rig = VRRig.Instance;
        if (rig == null || rig.Head == null)
        {
            Fail("no VRRig.Instance with a Head");
            yield break;
        }
        var p0 = rig.Head.localPosition;
        var r0 = rig.Head.localRotation;
        yield return Wait(1f);
        var p1 = rig.Head.localPosition;
        var r1 = rig.Head.localRotation;
        bool moving = (p1 - p0).sqrMagnitude > 1e-8f || Quaternion.Angle(r0, r1) > 0.01f;
        float norm = r1.x * r1.x + r1.y * r1.y + r1.z * r1.z + r1.w * r1.w;
        bool valid =
            !float.IsNaN(p1.x + p1.y + p1.z)
            && !float.IsInfinity(p1.x + p1.y + p1.z)
            && Mathf.Abs(norm - 1f) < 0.01f;

        var fails = new List<string>();
        var notes = new List<string> { $"head {(moving ? "moving" : "static")} at {p1}" };
        if (!VRInput.HeadTracked)
            fails.Add("head not tracked");
        if (!valid)
            fails.Add("head pose invalid");

        var main = MainCamera.instance;
        var cam = main != null ? main.GetComponent<Camera>() : null;
        if (cam == null)
            fails.Add("no MainCamera.instance camera");
        else
        {
            notes.Add(
                $"camera '{cam.name}' stereoEnabled {cam.stereoEnabled} targetEye {cam.stereoTargetEye}"
                    + (rig.HeadCamera == cam ? "" : $" (rig renders '{Name(rig.HeadCamera)}')")
            );
            if (!cam.stereoEnabled || cam.stereoTargetEye != StereoTargetEyeMask.Both)
                fails.Add("game camera not stereo");
        }

        var listener = NearestListener(rig.Head.position, out float listenerDistance);
        notes.Add(
            listener != null
                ? $"listener '{listener.name}' {listenerDistance:0.00} m from the head"
                : "no enabled AudioListener"
        );
        if (listener == null || listenerDistance > 0.5f)
            fails.Add("no AudioListener at the head");
        Conclude(fails, notes);
    }

    /// <summary>vr.02: title menu converted by ui.world and the title stage holding the rig origin (UI/Menu/TitleStage.cs).</summary>
    IEnumerator TitleStageCheck()
    {
        bool onBeach = Setting("UI", "TitleOnBeach", true);
        Canvas root = null;
        yield return WaitFor(
            () =>
            {
                root = RootCanvasOf(FindAnyObjectByType<MainMenuPageHandler>());
                return root != null
                    && root.renderMode == RenderMode.WorldSpace
                    && (!onBeach || (VRRig.Instance != null && VRRig.Instance.OriginOverride));
            },
            10f
        );
        var fails = new List<string>();
        var notes = new List<string>();
        if (root == null)
            fails.Add("no MainMenuPageHandler canvas");
        else
        {
            notes.Add(
                $"canvas '{root.name}' {root.renderMode}, converted {WorldCanvases.IsConverted(root)}"
            );
            if (root.renderMode != RenderMode.WorldSpace)
                fails.Add("title menu not world space");
        }
        bool held = VRRig.Instance != null && VRRig.Instance.OriginOverride;
        notes.Add($"OriginOverride {held}, UI.TitleOnBeach {onBeach}");
        if (onBeach && !held)
            fails.Add("title stage does not hold the rig origin");
        var stage = TitleStage.Instance;
        if (onBeach && stage != null && stage.Active)
        {
            // The title's button groups come to arm's reach (UI/Menu/TitleMenuDepth.cs), measured from the stage eye.
            float reach = Plugin.Settings.TryGetEntry<float>("UI", "TitleReach", out var r)
                ? r.Value
                : TitleLayout.TouchReach;
            yield return WaitFor(() => stage.Depth.NearGroups > 0, 5f);
            float nearest = stage.Depth.NearestButton(stage.Eye);
            notes.Add(
                $"{stage.Depth.NearGroups} button groups near, nearest button {nearest:0.00} m (reach {reach:0.00})"
            );
            if (stage.Depth.NearGroups == 0)
                fails.Add("no title button group brought to reach");
            else if (nearest > reach + 0.25f)
                fails.Add(
                    $"nearest title button {nearest:0.00} m from the eye, reach is {reach:0.00}"
                );
            // Meta's 0.5-0.8 m band is neither touch nor comfortable ray; a reach set outside it must land outside.
            else if (!TitleLayout.InAvoidBand(reach) && TitleLayout.InAvoidBand(nearest))
                fails.Add(
                    $"nearest title button {nearest:0.00} m from the eye, in the 0.5-0.8 m avoid band"
                );
            // Past the depth fade (TitleMenuDepth.FadeSeconds 0.35), every labelled button on the page reads from
            // the head as it looks: one button within reach says nothing about the rest.
            yield return Wait(0.5f);
            notes.Add(TitleButtonsReadable(stage.Eye, reach + 0.25f, fails));
        }
        Conclude(fails, notes);
        yield return Shot("vr-02-title");
    }

    /// <summary>vr.02: every labelled, interactable button on the title's page that was brought within
    /// <paramref name="within"/> of the stage eye (the hand-sized groups, TitleMenuDepth) reads from the head along
    /// its view (<see cref="Readable(string, TMP_Text, Vector3, List{string})"/>). The note gives the one farthest
    /// off the view.</summary>
    static string TitleButtonsReadable(Vector3 stageEye, float within, List<string> fails)
    {
        var handler = FindAnyObjectByType<MainMenuPageHandler>();
        var page = handler != null ? handler.currentPage : null;
        if (page == null)
        {
            fails.Add("no title page");
            return "no title page";
        }
        var head = VRRig.Instance.Head;
        int count = 0;
        float worst = -1f;
        string worstNote = "";
        foreach (var button in page.GetComponentsInChildren<Selectable>(false))
        {
            if (
                !button.IsInteractable()
                || Vector3.Distance(stageEye, button.transform.position) > within
            )
                continue;
            var label = button.GetComponentInChildren<TMP_Text>(false);
            if (label == null || string.IsNullOrWhiteSpace(label.GetParsedText()))
                continue;
            count++;
            string note = Readable($"button '{label.GetParsedText()}'", label, head.forward, fails);
            float off = Vector3.Angle(head.forward, label.transform.position - head.position);
            if (off > worst)
            {
                worst = off;
                worstNote = note;
            }
        }
        if (count == 0)
            fails.Add("no labelled title button within reach");
        return $"{count} title buttons read from the head, farthest off the view: {worstNote}";
    }

    /// <summary>
    /// vr.03: every active root canvas that ui.world converts (WorldCanvases.ShouldConvert, minus the Hidden and
    /// Fade kinds of <see cref="CanvasRules.Classify"/>) is world space. Waits past ui.world's 10 s rescan.
    /// </summary>
    IEnumerator CanvasCheck()
    {
        var offenders = new List<string>();
        float end = Now + 12f;
        string counts;
        while (true)
        {
            counts = AuditCanvases(offenders);
            if (offenders.Count == 0 || Now > end)
                break;
            yield return Wait(0.5f);
        }
        if (offenders.Count > 0)
            Fail("not world space: " + string.Join(", ", offenders) + "; " + counts);
        else
            // Conversion only: placement in view is checked by ui.05 (wrist HUD) and vr.04 (menu panel and its text).
            stepNote = counts + "; placement checked by ui.05 and vr.04";
        yield return Shot("vr-03-airport");
    }

    static string AuditCanvases(List<string> offenders)
    {
        offenders.Clear();
        int world = 0,
            overlay = 0,
            camera = 0,
            exempt = 0;
        foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (!canvas.isRootCanvas || !canvas.isActiveAndEnabled)
                continue;
            switch (canvas.renderMode)
            {
                case RenderMode.WorldSpace:
                    world++;
                    continue;
                case RenderMode.ScreenSpaceCamera:
                    camera++;
                    // Drawn into a render texture: already 3D, ui.world leaves it.
                    if (canvas.worldCamera != null && canvas.worldCamera.targetTexture != null)
                        continue;
                    break;
                default:
                    overlay++;
                    break;
            }
            var kind = CanvasRules.Classify(
                canvas,
                canvas.GetComponentsInChildren<Selectable>(true).Length > 0,
                canvas.GetComponentInChildren<MenuWindow>(true) != null
                    || canvas.GetComponentInParent<MenuWindow>(true) != null,
                out _
            );
            if (kind is CanvasKind.Hidden or CanvasKind.Fade)
                exempt++;
            else
                offenders.Add($"'{canvas.name}' ({canvas.renderMode}, {kind})");
        }
        return $"{world} world, {overlay} overlay, {camera} screen-camera, {exempt} left on screen by rule";
    }

    /// <summary>vr.04: the pause menu with the VR tab is a world panel in front of the head (ui.world PlaceMenu), with
    /// its flat-screen backdrops hidden (BackdropStrip): no row strip drawn on any page of the VR tab, on the longest
    /// game tab (scrolled to its end when it scrolls), or on the VR tab again after that tab change.</summary>
    IEnumerator SettingsPanel()
    {
        yield return OpenVRTab();
        if (!stepOk)
            yield break;
        var root = RootCanvasOf(FindAnyObjectByType<PauseMenuHandler>());
        var rig = VRRig.Instance;
        if (root == null || rig == null || rig.Head == null)
            Fail("no pause menu canvas or rig head");
        else
        {
            var head = rig.Head;
            // The content centre, not the canvas pivot: a 1920x1080 canvas pivot sits far off its settings window.
            if (!WorldCanvases.TryMenuCentre(root, out var centre))
                centre = root.transform.position;
            bool front = SelfTestChecks.InFront(
                head.position.ToNumerics(),
                head.forward.ToNumerics(),
                centre.ToNumerics(),
                out float distance,
                out float angle
            );
            // The shared rule allows 60 deg; a menu read without turning the head sits within Legibility's 45.
            front &= angle <= Legibility.MaxOffViewDegrees;
            // The text a player reads, not only the panel: the row label nearest the view, its size and angle.
            var readFails = new List<string>();
            var label = RowLabelNearView(root, head);
            string read =
                label != null
                    ? Readable("row label", label, head.forward, readFails)
                    : "no row label";
            if (label == null)
                readFails.Add("no settings row label drawn");
            // The pause menu's full-canvas BG dimmer is a backdrop whatever UI.MenuPanels says.
            int hidden = WorldCanvases.HiddenBackdrops(root, out int unraycastable);
            int strips = RowStripReport.Count(root, "VR tab as opened");
            var note =
                $"canvas '{root.name}' {root.renderMode}, {distance:0.00} m, {angle:0} deg off the view, {read}, "
                + $"{hidden} backdrops hidden, {unraycastable} of them dropped from raycasts, {strips} row strips drawn";
            if (root.renderMode != RenderMode.WorldSpace)
                Fail("not world space: " + note);
            else if (!front)
                Fail("not in front of the head: " + note);
            else if (readFails.Count > 0)
                Fail(string.Join("; ", readFails) + ": " + note);
            else if (hidden == 0 || strips > 0)
                Fail("flat-screen backdrop left on the menu: " + note);
            // GraphicRaycaster skips depth -1 or culled graphics: a hidden dimmer must still block clicks behind it.
            else if (unraycastable > 0)
                Fail("hidden backdrop no longer takes raycasts: " + note);
            else
                stepNote = note;
        }
        yield return root != null
            ? ShotAt("vr-04-settings", CentreOf(root.transform))
            : Shot("vr-04-settings");
        // Every page runs even after a failure: each one logs its row strip diagnostics (RowStripReport).
        if (root != null)
        {
            yield return VRTabStrips(root);
            yield return GameTabStrips(root);
        }
        ClosePause();
        yield return Until(() => !GUIManager.InPauseMenu, 3f, "pause menu did not close");
    }

    /// <summary>vr.04: the drawn settings row label (<c>SettingsUICell.m_text</c>, SettingsUICell.cs:10) nearest the
    /// head's view, or null.</summary>
    static TMP_Text RowLabelNearView(Canvas root, Transform head)
    {
        TMP_Text best = null;
        float bestOff = float.MaxValue;
        foreach (var cell in root.GetComponentsInChildren<SettingsUICell>(false))
        {
            var text = cell.m_text;
            if (
                text == null
                || !text.isActiveAndEnabled
                || string.IsNullOrWhiteSpace(text.GetParsedText())
            )
                continue;
            float off = Vector3.Angle(head.forward, text.transform.position - head.position);
            if (off < bestOff)
            {
                bestOff = off;
                best = text;
            }
        }
        return best;
    }

    /// <summary>vr.222: the pause menu's VR tab has a dark plate behind it (<c>UI.MenuBackplate</c>, ui.world
    /// BackplateView) that hugs what the menu draws: each edge <see cref="MenuBackplate.MarginPixels"/> outside the
    /// drawn content, within 2 px, never a full-canvas 16:9 panel around smaller content (the settings window really
    /// draws about 1700x990 px of its 1920x1080 canvas, VR run 47, so its plate is that big), never wider than
    /// <see cref="MenuBackplate.MaxSpanDegrees"/> of view and never a raycast target. Checked again on the
    /// Compatibility page, whose content differs, so the plate follows a page change.</summary>
    IEnumerator BackplateCheck()
    {
        if (!UIWorldFeature.Settings.MenuBackplate.Value)
        {
            stepSkip = "UI.MenuBackplate is off";
            yield break;
        }
        yield return OpenVRTab();
        if (!stepOk)
            yield break;
        var root = RootCanvasOf(FindAnyObjectByType<PauseMenuHandler>());
        if (root == null)
        {
            Fail("no pause menu canvas");
            yield break;
        }
        var notes = new List<string>();
        yield return Until(
            () => WorldCanvases.TryBackplate(root, out _, out _, out _, out _, out _, out _),
            2f,
            "no menu backplate on the pause menu"
        );
        if (!stepOk)
            yield break;
        string bad = JudgePlate(root, "VR tab", notes);
        yield return ShotAt("vr-222-menu-backplate", CentreOf(root.transform));
        var menu = vrTabMenu;
        if (bad == null && menu != null && VRTabGrid.Shown)
        {
            string group = VRTab.Group;
            int page = VRTab.Page;
            VRTabGrid.Select(menu, VRTab.CompatGroup, 0);
            // Past one slow re-measure (0.5 s) and the page's settle.
            yield return Wait(1f);
            bad = JudgePlate(root, "Compatibility", notes);
            VRTabGrid.Select(menu, group, page);
            yield return Wait(0.25f);
        }
        if (bad != null)
            Fail(bad + "; " + string.Join("; ", notes));
        else
            stepNote = string.Join("; ", notes);
    }

    /// <summary>vr.223: the VR tab is laid out for a headset. On its longest category: the row column spans at most
    /// <see cref="VRTabColumn.MaxColumnDegrees"/> from the eye; the rail is a panel of its own (not inside the column,
    /// turned <see cref="VRTabColumn.RailDegrees"/> toward the eye, with its own plate); every text on show meets
    /// <see cref="ReadableText.MinDegrees"/>; and scrolling to the end brings the last row fully into view.</summary>
    IEnumerator VRTabLayoutCheck()
    {
        yield return OpenVRTab();
        if (!stepOk)
            yield break;
        var menu = vrTabMenu;
        var head = VRRig.Instance != null ? VRRig.Instance.Head : null;
        if (menu == null || head == null)
        {
            Fail("no settings window or rig head");
            yield break;
        }
        VRGroup longest = null;
        foreach (var g in VRTab.BuildGroups())
            if (longest == null || g.Rows.Count > longest.Rows.Count)
                longest = g;
        VRTabGrid.Select(menu, longest.Key);
        yield return Wait(0.8f);
        var layout = menu.m_settingsContentParent.GetComponent<VRTabLayout>();
        if (!VRTabGrid.Shown || layout == null || layout.Grid == null || layout.Rail == null)
        {
            Fail("the VR tab fell back to one paged column (see the [ui.settings] warning)");
            yield break;
        }
        var column = layout.Grid.transform;
        var rail = layout.Rail.transform;
        var scroll = column.GetComponentInChildren<ScrollRect>();
        var viewport = scroll != null ? scroll.viewport : null;
        if (viewport == null)
        {
            Fail("the VR column has no scroll view");
            yield break;
        }
        var eye = head.position;
        var fails = new List<string>();
        var notes = new List<string>();

        viewport.GetWorldCorners(edgeCorners);
        float wide = Vector3.Angle(
            (edgeCorners[0] + edgeCorners[1]) * 0.5f - eye,
            (edgeCorners[2] + edgeCorners[3]) * 0.5f - eye
        );
        notes.Add($"'{longest.Title}' column {wide:0.0} deg wide");
        if (wide > VRTabColumn.MaxColumnDegrees)
            fails.Add($"column {wide:0.0} deg wide, over {VRTabColumn.MaxColumnDegrees:0}");

        float turn = Quaternion.Angle(rail.rotation, column.rotation);
        bool plates =
            !UIWorldFeature.Settings.MenuBackplate.Value
            || (
                rail.TryGetComponent<PanelPlate>(out var railPlate)
                && railPlate.Shown
                && column.TryGetComponent<PanelPlate>(out var columnPlate)
                && columnPlate.Shown
            );
        notes.Add($"rail turned {turn:0.0} deg, own plates {plates}");
        if (rail.IsChildOf(column))
            fails.Add("the rail sits inside the column");
        if (Mathf.Abs(turn - VRTabColumn.RailDegrees) > 2f)
            fails.Add($"rail turned {turn:0.0} deg, not {VRTabColumn.RailDegrees:0}");
        if (!plates)
            fails.Add("the column or the rail has no plate of its own");

        float smallest = float.MaxValue;
        string smallestText = null;
        void Floor(Transform root, bool clip)
        {
            foreach (var text in root.GetComponentsInChildren<TMPro.TMP_Text>())
            {
                // Only what is drawn: VR run 57 read a '0' at 0.19 deg that no frame shows (a template, placeholder or
                // faded part); its place goes into the note if one ever fails again.
                var cr = text.canvasRenderer;
                if (
                    !text.isActiveAndEnabled
                    || string.IsNullOrWhiteSpace(text.text)
                    || text.color.a * cr.GetAlpha() * cr.GetInheritedAlpha() < 0.1f
                    || cr.cull
                    || text.textInfo == null
                    || text.textInfo.characterCount == 0
                    || text.rectTransform.rect.width * text.rectTransform.rect.height < 1f
                )
                    continue;
                // Only what its scroll view shows: rows and chips scrolled away are not read.
                var view = text.GetComponentInParent<RectMask2D>();
                if (clip && view != null)
                {
                    var local = view.rectTransform.InverseTransformPoint(text.transform.position);
                    if (!view.rectTransform.rect.Contains(local))
                        continue;
                }
                float deg = ReadableText.Degrees(
                    text.fontSize * text.transform.lossyScale.y,
                    Vector3.Distance(eye, text.transform.position)
                );
                if (deg < smallest)
                {
                    smallest = deg;
                    smallestText =
                        $"{text.text}' at {NameChain(text.transform, menu.transform)}, scale "
                        + $"{text.transform.lossyScale.y:0.0000}, font {text.fontSize:0.#}";
                }
            }
        }
        Floor(column, clip: true);
        Floor(rail, clip: true);
        notes.Add($"smallest text {smallest:0.00} deg ('{smallestText}')");
        if (smallest < ReadableText.MinDegrees - 0.05f)
            fails.Add(
                $"text '{smallestText}' is {smallest:0.00} deg, under {ReadableText.MinDegrees}"
            );

        var rows = scroll.content;
        RectTransform last = null;
        foreach (RectTransform row in rows)
            if (
                row.gameObject.activeSelf
                && (last == null || row.anchoredPosition.y < last.anchoredPosition.y)
            )
                last = row;
        if (last != null)
        {
            scroll.verticalNormalizedPosition = 0f;
            yield return Wait(0.3f);
            last.GetWorldCorners(edgeCorners);
            float top = viewport.InverseTransformPoint(edgeCorners[1]).y;
            float bottom = viewport.InverseTransformPoint(edgeCorners[0]).y;
            var view = viewport.rect;
            bool reached = bottom >= view.yMin - 1f && top <= view.yMax + 1f;
            notes.Add(
                $"{rows.childCount} rows, {rows.rect.height:0} px in a {view.height:0} px view, last row shown at the end {reached}"
            );
            yield return ShotAt("vr-223-vr-tab-end", last.position);
            scroll.verticalNormalizedPosition = 1f;
            if (!reached)
                fails.Add("scrolling to the end leaves the last row outside the view");
        }
        yield return ShotAt("vr-223-vr-tab", column.position);
        Conclude(fails, notes);
    }

    /// <summary>Names from <paramref name="root"/> down to <paramref name="t"/>, for a step's note.</summary>
    static string NameChain(Transform t, Transform root)
    {
        var chain = t.name;
        for (var up = t.parent; up != null && up != root; up = up.parent)
            chain = up.name + "/" + chain;
        return chain;
    }

    /// <summary>One vr.222 check of the plate on <paramref name="root"/>; null when it passes.</summary>
    static string JudgePlate(Canvas root, string page, List<string> notes)
    {
        if (
            !WorldCanvases.TryBackplate(
                root,
                out var plate,
                out var lo,
                out var hi,
                out var canvas,
                out bool raycast,
                out float span
            )
        )
            return $"{page}: no menu backplate shown";
        var centre = new System.Numerics.Vector2(plate.center.x, plate.center.y);
        var size = new System.Numerics.Vector2(plate.width, plate.height);
        bool fits = MenuBackplate.FitsContent(
            centre,
            size,
            new System.Numerics.Vector2(lo.x, lo.y),
            new System.Numerics.Vector2(hi.x, hi.y),
            MenuBackplate.MarginPixels,
            2f
        );
        bool full = MenuBackplate.IsFullPanel(
            size,
            new System.Numerics.Vector2(hi.x - lo.x, hi.y - lo.y),
            MenuBackplate.MarginPixels,
            new System.Numerics.Vector2(canvas.x, canvas.y)
        );
        notes.Add(
            $"{page}: plate {plate.width:0}x{plate.height:0} px around content {hi.x - lo.x:0}x{hi.y - lo.y:0} on a "
                + $"{canvas.x:0}x{canvas.y:0} canvas, {span:0} deg wide, raycast target {raycast}"
        );
        return !fits
                ? $"{page}: plate edges not {MenuBackplate.MarginPixels:0} px outside the content"
            : full ? $"{page}: plate is a full-canvas panel around smaller content"
            : raycast ? $"{page}: plate takes raycasts"
            : span > MenuBackplate.MaxSpanDegrees
                ? $"{page}: plate spans {span:0} deg, over the {MenuBackplate.MaxSpanDegrees:0} deg cap"
            : null;
    }

    /// <summary>vr.04: every page of every rail entry, each filled by a category switch or page turn, draws no row
    /// strip. The rail entry and page on show before are restored.</summary>
    IEnumerator VRTabStrips(Canvas root)
    {
        var menu = vrTabMenu;
        if (menu == null || !VRTabGrid.Shown)
        {
            stepNote += "; VR tab grid not shown, only the page as opened checked";
            yield break;
        }
        string group = VRTab.Group;
        int page = VRTab.Page;
        var bad = new List<string>();
        int pages = 0;
        foreach (var g in VRTab.BuildGroups())
        {
            for (int p = 0; ; p++)
            {
                VRTabGrid.Select(menu, g.Key, p);
                yield return Wait(0.25f);
                pages++;
                int n = RowStripReport.Count(
                    root,
                    $"VR '{g.Title}' page {p + 1}/{VRTabGrid.Pages}"
                );
                if (n > 0)
                    bad.Add($"{g.Title} p{p + 1}: {n}");
                if (p + 1 >= VRTabGrid.Pages)
                    break;
            }
        }
        VRTabGrid.Select(menu, group, page);
        yield return Wait(0.25f);
        var note = $"; {pages} VR tab pages";
        if (bad.Count > 0)
            Fail($"row strips drawn on VR tab pages: {string.Join(", ", bad)}" + note);
        else
            stepNote += note + " without strips";
    }

    /// <summary>vr.04: the longest game tab (a tab change from the VR tab) draws no row strip, scrolled to its end when
    /// its list scrolls (rows that sat below the canvas or the list's mask when the tab filled,
    /// WorldCanvases.Scrolled); then the VR tab again, after that tab change.</summary>
    IEnumerator GameTabStrips(Canvas root)
    {
        var menu = root.GetComponentInChildren<SharedSettingsMenu>();
        var tabs = menu != null ? UISettingsFeature.TabsRef(menu) : null;
        var all =
            GameHandler.Instance != null
                ? GameHandler.Instance.SettingsHandler.GetSettingsThatImplements<IExposedSetting>()
                : null;
        if (tabs == null || all == null)
        {
            Fail("vr.04 game tab: no settings tabs or settings");
            yield break;
        }
        SettingsTABSButton longest = null;
        SettingsTABSButton vr = null;
        int most = 0;
        foreach (var b in tabs.buttons)
        {
            if (b == null)
                continue;
            if (b.category == VRTab.Category)
            {
                vr = b;
                continue;
            }
            int n = 0;
            foreach (var s in all)
                if (s.GetCategory() == b.category.ToString())
                    n++;
            if (n > most)
            {
                most = n;
                longest = b;
            }
        }
        if (longest == null)
            yield break;
        tabs.Select(longest);
        yield return Wait(0.8f);
        // The game's own tabs keep their single column in VR; they are short enough to fit the window.
        int outside = RowsOutside(menu);
        int strips = RowStripReport.Count(root, $"game tab {longest.category}");
        var list =
            menu.m_settingsContentParent != null
                ? menu.m_settingsContentParent.GetComponentInParent<ScrollRect>()
                : null;
        var viewport =
            list == null ? null
            : list.viewport != null ? list.viewport
            : (RectTransform)list.transform;
        bool scrolls =
            list != null
            && list.content != null
            && list.content.rect.height > viewport.rect.height + 1f;
        if (scrolls)
        {
            list.verticalNormalizedPosition = 0f;
            yield return Wait(0.5f);
            strips += RowStripReport.Count(root, $"game tab {longest.category} scrolled");
        }
        int back = 0;
        if (vr != null)
        {
            tabs.Select(vr);
            yield return Wait(0.5f);
            back = RowStripReport.Count(root, "VR tab after a game tab");
        }
        var note =
            $"; {longest.category} tab ({most} rows, {(scrolls ? "scrolled to its end" : "no scrolling")}): "
            + $"{strips} row strips drawn, {outside} rows outside the window; VR tab again: {back}";
        if (strips > 0 || back > 0)
            Fail("row strips drawn after a tab change" + note);
        else
            stepNote += note;
    }

    /// <summary>Samples the next level load in the background for vr.05; its own coroutine, so 04-beach is untouched.</summary>
    void StartLoadWatch()
    {
        if (!VRRun || !VRSession.Active)
            return;
        loadWatching = true;
        StartCoroutine(
            Guard(
                WatchLoading(),
                Now + 300f,
                e => loadError = e == null ? "timeout" : $"{e.GetType().Name}: {e.Message}"
            )
        );
    }

    /// <summary>While <c>LoadingScreenHandler.loading</c>: is ui.menu's loading space shown, and is the game
    /// camera culled to nothing under it (LoadingSpace.HideGameCamera)?</summary>
    IEnumerator WatchLoading()
    {
        float end = Now + 30f;
        while (!LoadingScreenHandler.loading)
        {
            if (!loadWatching || Now > end)
                yield break;
            yield return null;
        }
        loadSeen = true;
        var space = FindAnyObjectByType<LoadingSpace>();
        float shownAt = -1f;
        while (LoadingScreenHandler.loading && loadWatching)
        {
            if (space != null && space.Shown)
            {
                loadShownFrames++;
                if (shownAt < 0f)
                    shownAt = Now;
                var main = MainCamera.instance;
                var cam = main != null ? main.GetComponent<Camera>() : null;
                if (cam != null)
                {
                    if (cam.cullingMask == 0)
                        loadCulledFrames++;
                    else
                        loadOpenFrames++;
                }
                // A level load runs ~15 s at a low frame rate; by 3 s the screen's own art has animated in.
                if (!loadShot && Now - shownAt > 3f)
                {
                    loadShot = true;
                    yield return Shot("vr-05-loading");
                    continue;
                }
            }
            yield return null;
        }
    }

    /// <summary>vr.05: what <see cref="WatchLoading"/> saw during 04-beach.</summary>
    IEnumerator LoadingCheck()
    {
        loadWatching = false;
        var note =
            $"loading space shown {loadShownFrames} frames, game camera mask 0 in {loadCulledFrames}, "
            + $"not 0 in {loadOpenFrames}, screenshot {loadShot}";
        if (loadError != null)
            Fail($"watch {loadError}; {note}");
        else if (!loadSeen)
            Fail("no level load seen");
        // The load already ran, so the setting cannot be forced for it: a SKIP, not a pass that proved nothing.
        else if (!Setting("UI.Loading", "LoadingSpace", true))
            stepSkip = "UI.Loading.LoadingSpace is off; " + note;
        else if (loadShownFrames == 0)
            Fail("loading space never shown while loading");
        else if (loadCulledFrames == 0 && loadOpenFrames > 0)
            Fail("game camera kept its cullingMask: " + note);
        else
            stepNote = note;
        yield break;
    }

    /// <summary>vr.06: after the beach load, every VR feature active, own avatar posed, HUD in the world, no stuck fade.</summary>
    IEnumerator FeaturesCheck()
    {
        var fails = new List<string>();
        var notes = new List<string>();
        var inactive = new List<string>();
        int vrFeatures = 0;
        foreach (var f in FeatureManager.Features)
        {
            if (!f.VROnly)
                continue;
            vrFeatures++;
            if (f.State != FeatureState.Active)
                inactive.Add($"{f.Id}={f.State}");
        }
        notes.Add($"{vrFeatures - inactive.Count}/{vrFeatures} VR features active");
        if (inactive.Count > 0)
            fails.Add("not active: " + string.Join(", ", inactive));

        var c = Character.localCharacter;
        var driver = c != null ? AvatarPoseDriver.Of(c) : null;
        if (driver == null)
            fails.Add("no AvatarPoseDriver on the local character");
        else
        {
            var pose = driver.Pose;
            notes.Add(
                $"pose {Time.time - driver.LastPoseTime:0.00} s old, head {pose.HasHead}, hands {pose.HasLeftHand}/{pose.HasRightHand}"
            );
            if (!driver.HasFreshPose)
                fails.Add("avatar pose stale");
        }

        var gui = GUIManager.instance;
        var hud = gui != null ? gui.hudCanvas : null;
        if (hud == null)
            fails.Add("no HUD canvas");
        else
        {
            // World space only: ui.05 checks the HUD is placed in view and readable.
            notes.Add("HUD " + hud.renderMode + " (placement: ui.05)");
            if (hud.renderMode != RenderMode.WorldSpace)
                fails.Add("HUD not world space");
        }

        var vignette = Vignette();
        notes.Add(
            vignette != null
                ? $"vignette present, shown {vignette.gameObject.activeSelf}"
                : "vignette not built yet (built on first show)"
        );

        yield return WaitFor(() => VRFade.Target <= 0f, 5f);
        notes.Add($"fade target {VRFade.Target:0.00}, drawn {FadeView.Alpha:0.00}");
        if (VRFade.Target > 0f)
            fails.Add("fade stuck");
        Conclude(fails, notes);
    }

    /// <summary>
    /// vr.07: a knockback through the game's RPC (<c>Character.RPCA_AddForceAtPosition</c>, decomp
    /// Assembly-CSharp/Character.cs:458, sent the way <c>AddForceAtPosition</c> :452 does) pulses comfort.events'
    /// vignette, which is open again within 2 s; then a warp to spawn (<c>Character.WarpToSpawn</c>) leaves <see cref="VRFade.Target"/> at 0.
    /// </summary>
    IEnumerator ComfortCheck()
    {
        Recover();
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        if (c == null || rig == null || rig.Head == null)
        {
            Fail("no character or rig");
            yield break;
        }
        var fails = new List<string>();
        var notes = new List<string>();
        yield return WaitFor(() => ComfortEventsRuntime.VignetteBoost <= 0f, 5f);

        var back = -Vector3.ProjectOnPlane(rig.Head.forward, Vector3.up).normalized;
        if (back.sqrMagnitude < 0.5f)
            back = Vector3.back;
        c.photonView.RPC(
            "RPCA_AddForceAtPosition",
            RpcTarget.All,
            back * KnockbackForce,
            c.Center,
            1f
        );
        var pulse = new PulseWatch();
        bool meshShown = false;
        bool shot = false;
        float prevBoost = 0f;
        float start = Now;
        while (Now - start < 3f)
        {
            float boost = ComfortEventsRuntime.VignetteBoost;
            pulse.Sample(boost, Now - start);
            var mesh = Vignette();
            bool ringUp = mesh != null && mesh.gameObject.activeInHierarchy;
            meshShown |= ringUp;
            // At the pulse's peak with the ring drawn: the first rising frame showed hardly any ring.
            if (pulse.Rose && ringUp && boost < prevBoost && !shot)
            {
                shot = true;
                yield return Shot("vr-07-knockback");
                continue;
            }
            prevBoost = boost;
            yield return null;
        }
        if (!shot)
            yield return Shot("vr-07-knockback");
        notes.Add(
            $"knockback pulse peak {pulse.Peak:0.00}, open after {pulse.SettleSeconds:0.00} s, ring shown {meshShown}"
        );
        if (!pulse.Rose)
            fails.Add("no vignette pulse");
        else if (!pulse.Settled || pulse.SettleSeconds > 2f)
            fails.Add("vignette not open within 2 s");
        else if (!meshShown)
            fails.Add("vignette ring not shown");

        // A teleport inside the segment: a segment jump here would leave the later segment loop revisiting
        // segments, which throws in vanilla SingleItemSpawner.TrySpawnItems.
        if (!SandboxActions.WarpToSpawn())
            fails.Add(SandboxActions.LastMessage);
        else
        {
            var fade = new PulseWatch();
            float warpAt = Now;
            while (Now - warpAt < 3f)
            {
                fade.Sample(VRFade.Target, Now - warpAt);
                yield return null;
            }
            float settleAt = Now;
            while (VRFade.Target > 0f && Now - settleAt < 10f)
            {
                fade.Sample(VRFade.Target, Now - warpAt);
                yield return null;
            }
            notes.Add(
                $"warp to spawn: fade peak {fade.Peak:0.00}, target {VRFade.Target:0.00} {Now - settleAt:0.0} s after"
            );
            if (VRFade.Target > 0f)
                fails.Add("VRFade.Target stuck after the warp");
            var warped = VRRig.Instance;
            if (
                warped != null
                && VRFade.Target < 0.99f
                && Rig.RigDriver.EyeInside(warped.Head.position)
            )
                fails.Add("the eye is inside rock and the view is not black after the warp");
            Recover();
        }
        Conclude(fails, notes);
    }

    // ---------------------------------------------------------------- VR helpers

    void Conclude(List<string> fails, List<string> notes)
    {
        var all = string.Join("; ", notes);
        if (fails.Count > 0)
            Fail(string.Join("; ", fails) + " | " + all);
        else
            stepNote = all;
    }

    /// <summary>Waits for a condition without failing the step; the caller judges the outcome.</summary>
    static IEnumerator WaitFor(Func<bool> condition, float seconds)
    {
        float end = Now + seconds;
        while (!condition() && Now <= end)
            yield return null;
    }

    static bool Setting(string section, string key, bool fallback) =>
        Plugin.Settings.TryGetEntry<bool>(section, key, out var entry) ? entry.Value : fallback;

    static Canvas RootCanvasOf(Component c)
    {
        if (c == null)
            return null;
        var canvas = c.GetComponentInParent<Canvas>(true);
        if (canvas == null)
            canvas = c.GetComponentInChildren<Canvas>(true);
        return canvas != null ? canvas.rootCanvas : null;
    }

    static Transform Vignette()
    {
        var rig = VRRig.Instance;
        return rig != null && rig.Head != null ? rig.Head.Find(VignetteName) : null;
    }

    static AudioListener NearestListener(Vector3 head, out float distance)
    {
        AudioListener best = null;
        distance = float.PositiveInfinity;
        foreach (var l in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
        {
            if (!l.isActiveAndEnabled)
                continue;
            float d = Vector3.Distance(l.transform.position, head);
            if (d < distance)
            {
                distance = d;
                best = l;
            }
        }
        return best;
    }

    static string Name(UnityEngine.Object o) => o != null ? o.name : "none";

    /// <summary>Keeps the last <c>[xr.bootstrap]</c> log line, so a VR start failure names the bootstrap's state.
    /// BepInEx log lines do not reach Unity's <c>Application.logMessageReceived</c>.</summary>
    sealed class BootstrapTail : ILogListener
    {
        volatile string last;

        public string Last => last;

        public void LogEvent(object sender, LogEventArgs eventArgs)
        {
            if (
                eventArgs.Data is string text
                && text.StartsWith("[xr.bootstrap]", StringComparison.Ordinal)
            )
                last = text;
        }

        public void Dispose() { }
    }
}
