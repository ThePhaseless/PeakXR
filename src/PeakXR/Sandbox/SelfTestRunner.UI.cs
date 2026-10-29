using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Interact;
using PeakXR.Rig;
using PeakXR.UI.Affordances;
using PeakXR.UI.Common;
using PeakXR.UI.Spatial;
using PeakXR.VR;
using TMPro;
using UnityEngine;
using Zorro.Core;

namespace PeakXR.Sandbox;

/// <summary>ui.spatial steps: HUD parts carried to the world (U1-U4). Spectating is checked by mob.PlayerGhost (SelfTestRunner.Context).</summary>
internal sealed partial class SelfTestRunner
{
    IEnumerator SpatialSteps()
    {
        var s = SpatialFeature.Settings;
        // ui.01 reads the name: the label carries nothing else for a plain pickup (TargetPrompt.cs:25-29).
        yield return WithOn(
            "ui.01-target-prompt",
            20f,
            TargetPromptStep(),
            s.TargetPrompt,
            s.TargetNames
        );
        yield return WithOn("ui.02-button-labels", 20f, ButtonLabelStep(), s.ButtonLabels);
        yield return WithOn("ui.03-world-banner", 25f, BannerStep(), s.WorldBanners);
        yield return WithOn("ui.04-rope-gauge", 28f, RopeGaugeStep(), s.HeldItemGauges);
        yield return Restoring("ui.05-wrist-hud", 15f, WristHudStep());
        yield return Restoring("ui.06-top-stamina", 15f, TopStaminaStep());
    }

    /// <summary>
    /// ui.06: <c>UI.HUD.StaminaView</c> TopOfView moves the game's stamina bar to a small meter high in the view, drawn
    /// on top, its plate as long as the bar's frame (not the frame's 1214 px stencil mask); Both keeps the bar on the
    /// wrist and draws a copy at the top that tracks it.
    /// </summary>
    IEnumerator TopStaminaStep()
    {
        var cfg = UI.World.UIWorldFeature.Settings;
        var head = VRRig.Instance.Head;
        var was = cfg.StaminaView.Value;
        bodyRestore += () => cfg.StaminaView.Value = was;
        cfg.StaminaView.Value = UI.World.StaminaView.TopOfView;
        yield return Wait(1f);
        var root = UI.World.HudSplit.TopRoot;
        var plate = UI.World.HudSplit.TopPlate;
        var frame = GUIManager.instance.bar.staminaBarOutline;
        var toTop = frame.TransformPoint(frame.rect.center) - head.position;
        float off = Vector3.Angle(head.forward, toTop);
        float above = Vector3.SignedAngle(head.forward, toTop, head.right) * -1f;
        float degrees =
            2f
            * Mathf.Atan2(UI.World.HudSplit.TopWidthMeters * 0.5f, toTop.magnitude)
            * Mathf.Rad2Deg;
        bool onTop = UI.Common.OnTop.AllOnTop(root);
        bool barThere = WristBarFrame(root, out float frameLo, out float frameHi, out var barNote);
        bool plateShown = MeasureWristPlate(root, out var plateMin, out var plateMax, plate);
        bool plateOnBar =
            !barThere
            || (
                plateShown
                && MenuBackplate.FitsSpan(
                    plateMin.x,
                    plateMax.x,
                    frameLo,
                    frameHi,
                    UI.World.HudSplit.WristPlatePad,
                    MenuBackplate.FoldPixels + 1f
                )
            );
        yield return ShotAt("ui-06-top-stamina", head.position + toTop);

        cfg.StaminaView.Value = UI.World.StaminaView.Both;
        yield return Wait(1f);
        var bothRoot = UI.World.HudSplit.TopRoot;
        bool copyDrawn = MeasureWristDrawn(bothRoot, out _, out _, UI.World.HudSplit.TopPlate);
        bool wristKept =
            cfg.Hud.Value != UI.World.HudMode.Wrist
            || (
                UI.World.HudSplit.WristValid
                && GUIManager.instance.bar.transform.IsChildOf(UI.World.HudSplit.WristRoot)
            );
        stepNote = F(
            $"top meter {off:0} deg off the view centre, {above:0} deg above it, {degrees:0.0} deg wide, on top {onTop}; plate {plateMax.x - plateMin.x:0} px on bar frame {frameHi - frameLo:0} px{barNote}, fits {plateOnBar}; Both: copy drawn {copyDrawn}, bar kept on the wrist {wristKept}"
        );
        if (root == null)
            Fail("top stamina meter not posed");
        else if (above < 10f || off > 40f)
            Fail("top stamina meter is not high in the view");
        else if (degrees > 16f)
            Fail("top stamina meter is not small");
        else if (!onTop)
            Fail("top stamina meter is depth tested");
        else if (!barThere)
            Fail("stamina bar frame not in the top meter" + barNote);
        else if (!plateOnBar)
            Fail("top meter plate is not as long as the stamina bar's frame");
        else if (bothRoot == null || !copyDrawn)
            Fail("Both: no stamina copy drawn at the top");
        else if (!wristKept)
            Fail("Both: the stamina bar left the wrist");
    }

    /// <summary>A hand step with <paramref name="on"/> turned on, as a player who uses the feature has it: a step that
    /// passes with its feature off proves nothing. Each value goes back after the step, after a timeout too
    /// (<see cref="Restoring"/>).</summary>
    IEnumerator WithOn(string id, float timeout, IEnumerator body, params ConfigEntry<bool>[] on)
    {
        foreach (var setting in on)
        {
            if (setting == null || setting.Value)
                continue;
            setting.Value = true;
            bodyRestore += () => setting.Value = false;
        }
        yield return Restoring(id, timeout, body);
    }

    /// <summary>
    /// <paramref name="text"/> is drawn (shown, opaque, not empty) and a player looking along
    /// <paramref name="gaze"/> can read it (<see cref="Legibility"/>): near enough to see, not so near the eyes
    /// cross, in view, facing the eye, tall enough. A miss adds to <paramref name="fails"/>. Returns the measurement.
    /// </summary>
    static string Readable(string what, TMP_Text text, Vector3 gaze, List<string> fails)
    {
        if (
            text == null
            || !text.isActiveAndEnabled
            || string.IsNullOrWhiteSpace(text.GetParsedText())
        )
        {
            fails.Add(what + " not drawn");
            return what + " not drawn";
        }
        var t = text.transform;
        var bounds = text.textBounds;
        return Readable(
            what,
            t.TransformPoint(bounds.center),
            t.forward,
            t.TransformVector(0f, bounds.size.y, 0f).magnitude,
            Alpha(text),
            gaze,
            fails
        );
    }

    static string Readable(
        string what,
        Vector3 centre,
        Vector3 facing,
        float height,
        float alpha,
        Vector3 gaze,
        List<string> fails
    )
    {
        var eye = VRRig.Instance.Head.position;
        var why = Legibility.Check(
            eye.ToNumerics(),
            gaze.ToNumerics(),
            centre.ToNumerics(),
            facing.ToNumerics(),
            height,
            out var degrees,
            out var distance,
            out var off,
            out var tilt
        );
        if (why == null && alpha < 0.5f)
            why = F($"faded (alpha {alpha:0.00})");
        if (why != null)
            fails.Add(what + " " + why);
        return F(
            $"{what} {degrees:0.00} deg tall at {distance:0.00} m, {off:0} deg off the gaze, tilt {tilt:0}, alpha {alpha:0.00}"
        );
    }

    /// <summary>What reaches the screen: the colour's alpha, the renderer's own (ui.spatial hides names and verbs
    /// with it) and its canvas groups'.</summary>
    static float Alpha(UnityEngine.UI.Graphic g) =>
        g.color.a * g.canvasRenderer.GetAlpha() * g.canvasRenderer.GetInheritedAlpha();

    /// <summary>
    /// ui.05: the wrist HUD (stamina bar and afflictions, moved by ui.world's HudSplit) on a wrist raised like a watch
    /// (fingers up, the back of the hand to the eye) lies on the back of the wrist facing the eye with its bar across
    /// the wrist, turns to face the eye at a readable size, draws over the arm (OnTop), and a fresh affliction (Cold)
    /// shows in it. Cold goes on through <c>AddStatus</c>, as the game adds it, which also holds off the body's own
    /// warmth (CharacterHeatEmission.cs:23 waits 3 s after added cold; <c>SetStatus</c> does not reset that clock).
    /// </summary>
    IEnumerator WristHudStep()
    {
        var cfg = UI.World.UIWorldFeature.Settings;
        if (cfg == null || cfg.Hud.Value != UI.World.HudMode.Wrist)
        {
            // Body mode lays the whole HUD out differently; switching it mid-run is not a test-only toggle.
            stepSkip = "UI.HUD.HudMode is not Wrist";
            yield break;
        }
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        var head = rig.Head;
        bool left = cfg.WristLeft;
        // God mode locks statuses (Character.statusesLocked, which AddStatus obeys, CharacterAfflictions.cs:718) and
        // its tick clears Cold, so VR run 27 read Cold 0.00 and the segment stayed hidden (BarAffliction.cs:43-55).
        // The sandbox's status actions lift it; Run puts god mode back after the step.
        SandboxActions.ClearStatus(CharacterAfflictions.STATUSTYPE.Cold);
        if (!SandboxActions.AddStatus(CharacterAfflictions.STATUSTYPE.Cold, 0.25f))
        {
            Fail("game refused Cold: " + BodyState(Character.localCharacter));
            yield break;
        }
        // Stamina past the bar, as infinite stamina (god mode, a sugar rush) leaves it: the setter then refuses the
        // clamp (CharacterData.cs:449) and the fill's rect runs past the frame (A/StaminaBar.cs:112), hidden by the
        // bar's mask. The plate drew three bar lengths long around it (user screenshot, build 7f8e6ec); AddStatus
        // lifted god mode, so the step never saw it.
        c.infiniteStam = true;
        c.data.currentStamina = 2.5f;
        bodyRestore += () =>
        {
            c.infiniteStam = false;
            c.ClampStamina();
        };
        yield return MoveTo(
            left,
            // The display sits 12 cm down the forearm from the grip: the hand a little above eye level puts it in
            // the gaze (WristHud.LookDegrees), as a player looks at a watch.
            () => head.position + Flat(head.forward) * 0.35f + Vector3.up * 0.05f,
            0.5f
        );
        // Raised like a watch: fingers up, the back of the hand (grip -x left, +x right) toward the eye.
        var away = Flat(Script(left).Position - VRInput.HeadPosition);
        Script(left).Rotation = HandLook(
            Vector3.up,
            Vector3.Cross(away * (left ? -1f : 1f), Vector3.up)
        );
        yield return Wait(1f);
        var wrist = UI.World.HudSplit.WristPosition;
        var toWrist = wrist - head.position;
        float facing = Vector3.Angle(UI.World.HudSplit.WristRotation * Vector3.forward, toWrist);
        var rest = UI.World.HudSplit.WristRest;
        float restFacing = Vector3.Angle(rest * Vector3.forward, toWrist);
        var hand = rig.Hand(left);
        float across = hand != null ? Vector3.Angle(rest * Vector3.right, hand.Fingers()) : 0f;
        float width = UI.World.HudSplit.WristWidthMeters;
        // The bar as drawn: its frame's own world corners, read against UI.WristWidth and the wrist anchor. VR build
        // 7c3814a scaled the groups' rects (with the frame's 1214 px stencil mask) to the setting, so the 614 px frame
        // was half as wide and its centre a few cm off the anchor while WristWidthMeters still read the setting.
        bool frameMeasured = WristFrameWorld(
            UI.World.HudSplit.WristRoot,
            out float frameMeters,
            out var frameCentre
        );
        float eye = toWrist.magnitude;
        float wanted = WristHud.Width(
            WristHud.ScaledWidth(cfg.WristWidth.Value, cfg.TextScale.Value),
            eye,
            UI.World.HudSplit.WristPulse
        );
        float wantedDegrees = 2f * Mathf.Atan2(wanted * 0.5f, eye) * Mathf.Rad2Deg;
        float degrees =
            2f * Mathf.Atan2((frameMeasured ? frameMeters : width) * 0.5f, eye) * Mathf.Rad2Deg;
        float centreOff = frameMeasured
            ? Vector3.Distance(frameCentre, wrist)
            : float.PositiveInfinity;
        // Read from where the player looks: the step raises the wrist into the level gaze (WristHud.LookDegrees).
        float off = Vector3.Angle(head.forward, toWrist);
        var root = UI.World.HudSplit.WristRoot;
        int afflictions = 0;
        if (root != null)
            foreach (var a in root.GetComponentsInChildren<BarAffliction>(false))
                if (a.afflictionType == CharacterAfflictions.STATUSTYPE.Cold)
                    afflictions++;
        bool onTop = UI.Common.OnTop.AllOnTop(root);
        float cold = c.refs.afflictions.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Cold);
        string where = afflictions > 0 ? "" : ColdSlotWhere(root);
        // The plate hugs what is drawn (bar, shown segments), not the room the bar's groups reserve. Measured here
        // from the drawn graphics' and the plate's own corners, not from what the fit stored.
        bool drawn = MeasureWristDrawn(root, out var drawnMin, out var drawnMax);
        bool plateShown = MeasureWristPlate(root, out var plateMin, out var plateMax);
        var plateSize = plateMax - plateMin;
        // The fit may lag the content by up to one signature step (MenuBackplate.FoldPixels).
        float plateTolerance = MenuBackplate.FoldPixels + 1f;
        bool plateFits =
            drawn
            && plateShown
            && MenuBackplate.FitsContent(
                ((plateMin + plateMax) * 0.5f).N(),
                plateSize.N(),
                drawnMin.N(),
                drawnMax.N(),
                UI.World.HudSplit.WristPlatePad,
                plateTolerance
            );
        var groups = UI.World.HudSplit.WristGroupsSize;
        bool unshrunk =
            plateShown
            && MenuBackplate.Unshrunk(
                plateSize.N(),
                groups.N(),
                UI.World.HudSplit.WristPlatePad,
                plateTolerance
            );
        // Independent of any "is it drawn" rule: along its length the plate hugs the bar's own frame (VR build
        // 7c3814a drew it twice as long, round the bar's hidden OutlineMask, and both rules agreed).
        bool barOnly = WristBarFrame(
            root,
            out float frameLo,
            out float frameHi,
            out string barNote
        );
        bool plateOnBar =
            !barOnly
            || (
                plateShown
                && MenuBackplate.FitsSpan(
                    plateMin.x,
                    plateMax.x,
                    frameLo,
                    frameHi,
                    UI.World.HudSplit.WristPlatePad,
                    plateTolerance
                )
            );
        yield return ShotAt("ui-05-wrist-hud", wrist);
        SandboxActions.ClearStatus(CharacterAfflictions.STATUSTYPE.Cold);
        stepNote = F(
            $"wrist HUD {width * 100f:0.0} cm ({degrees:0} deg) at {toWrist.magnitude:0.00} m, {off:0} deg off the view, {facing:0} deg off facing the eye (turn {UI.World.HudSplit.WristFacing:0.00}), watch face {restFacing:0} deg off the eye, bar {across:0} deg to the forearm, on top {onTop}, stamina {c.data.currentStamina:0.00}, Cold {cold:0.00}, Cold slots {afflictions}{where}; plate {plateSize.x:0}x{plateSize.y:0} px around drawn {drawnMax.x - drawnMin.x:0}x{drawnMax.y - drawnMin.y:0} (groups reserve {groups.x:0}x{groups.y:0}), fits {plateFits}; bar frame {frameHi - frameLo:0} px{barNote}, plate on it {plateOnBar}; frame {frameMeters * 100f:0.0} cm ({degrees:0.0} deg, UI.WristWidth wants {wanted * 100f:0.0} cm, {wantedDegrees:0.0} deg), centre {centreOff * 1000f:0} mm off the wrist anchor"
        );
        if (!UI.World.HudSplit.WristValid)
            Fail("wrist HUD not posed");
        else if (off > Legibility.MaxOffViewDegrees || toWrist.magnitude < Legibility.NearestMeters)
            Fail("wrist HUD out of view or too near the eye");
        else if (facing > 25f)
            Fail("wrist HUD does not face the eye");
        else if (restFacing > 50f)
            Fail("watch face is not on the side of the wrist that faces the eye");
        else if (Mathf.Abs(across - 90f) > 15f)
            Fail("wrist bar does not run across the wrist");
        else if (!frameMeasured)
            Fail("stamina bar frame not on the wrist");
        else if (Mathf.Abs(degrees - wantedDegrees) > WristSizeTolerance * wantedDegrees)
            Fail("wrist bar is not drawn at UI.WristWidth");
        else if (centreOff > WristSizeTolerance * wanted)
            Fail("wrist bar is not centred on the wrist anchor");
        else if (degrees < 12f)
            Fail("wrist HUD too small to read");
        else if (!onTop)
            Fail("wrist HUD is depth tested: the arm can cover it");
        else if (afflictions == 0)
            Fail("Cold affliction not shown on the wrist");
        else if (!drawn)
            Fail("wrist HUD draws nothing");
        else if (!plateShown)
            Fail("wrist plate not shown");
        else if (unshrunk)
            Fail("wrist plate fills the groups' reserved room, not the drawn bar");
        else if (!plateOnBar)
            Fail("wrist plate is not as long as the stamina bar's frame");
        else if (!plateFits)
            Fail("wrist plate is not fitted to the drawn bar");
    }

    /// <summary>ui.05's slack on the bar's width and centre, as a fraction of the wanted width: the pose and the alert
    /// pulse it reads may be a frame apart (a 0.6 s sine pulse of +20 % moves the width at most about 1.2 % a frame at
    /// 90 Hz, SpatialUI.cs:314-315), while the bug it guards against was 50 % (a 614 px frame scaled as 1214 px).</summary>
    const float WristSizeTolerance = 0.03f;

    /// <summary>World width (bottom edge) and centre of the stamina bar's frame on the wrist; false when it is not
    /// there.</summary>
    static bool WristFrameWorld(RectTransform root, out float meters, out Vector3 centre)
    {
        meters = 0f;
        centre = default;
        var gui = GUIManager.instance;
        var frame = gui != null && gui.bar != null ? gui.bar.staminaBarOutline : null;
        if (
            root == null
            || frame == null
            || !frame.gameObject.activeInHierarchy
            || !frame.IsChildOf(root)
        )
            return false;
        var corners = new Vector3[4];
        frame.GetWorldCorners(corners);
        meters = Vector3.Distance(corners[0], corners[3]);
        centre = (corners[0] + corners[2]) * 0.5f;
        return true;
    }

    /// <summary>Box-space x span of the stamina bar's own art: its frame (the game sizes it to the bar,
    /// A/StaminaBar.cs:135) and the shield and campfire icons beside it when shown (A/StaminaBar.cs:180-181). False,
    /// with a note, when something else on the wrist (hotbar, extra stamina, dying bar) may set the plate's length.</summary>
    static bool WristBarFrame(RectTransform root, out float lo, out float hi, out string note)
    {
        lo = hi = 0f;
        note = "";
        var gui = GUIManager.instance;
        var bar = gui != null ? gui.bar : null;
        if (root == null || bar == null || !bar.staminaBarOutline.IsChildOf(root))
        {
            note = " (bar not on the wrist)";
            return false;
        }
        static bool Shown(Component c, Transform root) =>
            c != null && c.gameObject.activeInHierarchy && c.transform.IsChildOf(root);
        if (
            (bar.extraBar != null && bar.extraBar.gameObject.activeInHierarchy)
            || (gui.items != null && gui.items.Length > 0 && Shown(gui.items[0], root))
            || Shown(gui.backpack, root)
            || (gui.dyingBarObject != null && Shown(gui.dyingBarObject.transform, root))
        )
        {
            note = " (other wrist parts shown, not checked)";
            return false;
        }
        lo = float.MaxValue;
        hi = float.MinValue;
        var corners = new Vector3[4];
        foreach (
            var part in new[]
            {
                bar.staminaBarOutline,
                (RectTransform)bar.shield.transform,
                (RectTransform)bar.campfire.transform,
            }
        )
        {
            if (!part.gameObject.activeInHierarchy)
                continue;
            part.GetWorldCorners(corners);
            float a = root.InverseTransformPoint(corners[0]).x;
            float b = root.InverseTransformPoint(corners[2]).x;
            lo = Mathf.Min(lo, Mathf.Min(a, b));
            hi = Mathf.Max(hi, Mathf.Max(a, b));
        }
        return true;
    }

    /// <summary>Box-space bounds of every graphic under the wrist box that reaches the screen (active, enabled, visible
    /// alpha after canvas groups, not a stencil-only mask, cut to the masks above it, not empty), the plate aside. The
    /// ui.05 oracle, apart from HudSplit's own fit.</summary>
    static bool MeasureWristDrawn(
        RectTransform root,
        out Vector2 min,
        out Vector2 max,
        UnityEngine.UI.Graphic plate = null
    )
    {
        min = new Vector2(float.MaxValue, float.MaxValue);
        max = new Vector2(float.MinValue, float.MinValue);
        if (root == null)
            return false;
        if (plate == null)
            plate = UI.World.HudSplit.WristPlate;
        bool any = false;
        foreach (var g in root.GetComponentsInChildren<UnityEngine.UI.Graphic>(false))
        {
            if (
                g == plate
                || !g.enabled
                || g.color.a < 0.01f
                || g.canvasRenderer.GetAlpha() * g.canvasRenderer.GetInheritedAlpha() <= 0.01f
                || UI.World.PointerHitRule.MaskOnly(g)
                || !UI.World.HudPlate.Visible(root, g, out var a, out var b)
            )
                continue;
            if (b.X - a.X < 0.5f || b.Y - a.Y < 0.5f)
                continue;
            min = Vector2.Min(min, new Vector2(a.X, a.Y));
            max = Vector2.Max(max, new Vector2(b.X, b.Y));
            any = true;
        }
        return any;
    }

    /// <summary>Box-space bounds of the wrist plate as drawn; false when it is missing or hidden.</summary>
    static bool MeasureWristPlate(
        RectTransform root,
        out Vector2 min,
        out Vector2 max,
        UnityEngine.UI.Graphic plate = null
    )
    {
        min = max = default;
        if (plate == null)
            plate = UI.World.HudSplit.WristPlate;
        if (
            root == null
            || plate == null
            || plate.transform.parent != root
            || !plate.isActiveAndEnabled
        )
            return false;
        var corners = new Vector3[4];
        plate.rectTransform.GetWorldCorners(corners);
        Vector2 a = root.InverseTransformPoint(corners[0]);
        Vector2 b = root.InverseTransformPoint(corners[2]);
        min = Vector2.Min(a, b);
        max = Vector2.Max(a, b);
        return true;
    }

    /// <summary>Where the stamina bar's Cold segment is when the wrist does not show it, for the step note.</summary>
    static string ColdSlotWhere(Transform wristRoot)
    {
        var bar = GUIManager.instance != null ? GUIManager.instance.bar : null;
        if (bar == null || bar.afflictions == null)
            return "; no stamina bar";
        foreach (var a in bar.afflictions)
        {
            if (a == null || a.afflictionType != CharacterAfflictions.STATUSTYPE.Cold)
                continue;
            var path = a.name;
            for (
                var t = a.transform.parent;
                t != null && t.GetComponent<Canvas>() == null;
                t = t.parent
            )
                path = t.name + "/" + path;
            bool under = wristRoot != null && a.transform.IsChildOf(wristRoot);
            return $"; Cold segment {path}, on wrist {under}, active {a.gameObject.activeInHierarchy} (self {a.gameObject.activeSelf})";
        }
        return "; no Cold segment in the bar";
    }

    /// <summary>ui.01: an Apple in front of the eyes, inside the game's 2 m reach (Interaction.cs:7), becomes
    /// <c>Interaction.currentHovered</c> (Interaction.cs:15); its name group (<c>GUIManager.interactName</c>,
    /// GUIManager.cs:84) leaves the HUD for a label just above it as you see it, and its name
    /// (<c>interactNameText</c>, GUIManager.cs:1051) is drawn there, readable (run with <c>UI.TargetNames</c> on).</summary>
    IEnumerator TargetPromptStep()
    {
        var head = VRRig.Instance.Head;
        var spot = head.position + Flat(head.forward) * 0.8f + Vector3.down * 0.2f;
        // Deterministic hover: the rest hands do not point, so the game's ray comes from the head (Interaction.cs:196);
        // hold the Apple still on that ray, 0.8 m out, instead of letting it fall off it.
        var world = SpawnTestItem(AppleFragment, spot);
        if (world == null)
            yield break;
        float until = Now + 5f;
        while (Now < until && !ReferenceEquals(Interaction.instance.currentHovered, world))
        {
            HoldOnHeadRay(world, head);
            yield return null;
        }
        if (!ReferenceEquals(Interaction.instance.currentHovered, world))
        {
            Fail(
                $"the Apple never became the hovered target (aim hand {PhysicalInteraction.AimHand})"
            );
            yield break;
        }
        for (float t = Now + 0.3f; Now < t; )
        {
            HoldOnHeadRay(world, head);
            yield return null;
        }
        var label = GUIManager.instance.interactName.transform.parent;
        var fails = new List<string>();
        float above = 0f;
        string read = "";
        if (
            label == null
            || label.name != "PeakXR_TargetPrompt"
            || !label.gameObject.activeInHierarchy
        )
            fails.Add(
                $"name group under '{(label != null ? label.name : "none")}', not a shown target label"
            );
        else
        {
            float toItem = Vector3.Distance(label.position, world.Center());
            float eyeToLabel = Vector3.Distance(head.position, label.position);
            float eyeToItem = Vector3.Distance(head.position, world.Center());
            if (toItem > 0.6f || eyeToLabel > eyeToItem + 0.05f)
                fails.Add(
                    $"label {toItem:0.00} m from the Apple, {eyeToLabel:0.00} m vs {eyeToItem:0.00} m from the eye"
                );
            // Above the Apple as you see it (SpatialMath.TargetLabel), not across it.
            above = Vector3.Angle(label.position - head.position, world.Center() - head.position);
            if (above < 3f || label.position.y <= world.Center().y)
                fails.Add(
                    $"label only {above:0.0} deg off the Apple's centre: it crosses the Apple"
                );
            // The Apple sits on the head's ray, so the gaze is the view.
            read = Readable("name", GUIManager.instance.interactNameText, head.forward, fails);
        }
        yield return Shot("ui-01-target-prompt");
        Conclude(
            fails,
            new List<string>
            {
                $"label at {(label != null ? label.position : Vector3.zero)}, {above:0.0} deg above the Apple, {read}",
            }
        );
    }

    static void HoldOnHeadRay(Item item, Transform head)
    {
        if (item == null)
            return;
        var at = head.position + head.forward * 0.8f;
        item.transform.position += at - item.Center();
    }

    /// <summary>ui.02: holding an Apple shows its use prompt (<c>itemPromptMain</c>, GUIManager.cs:102) beside the
    /// holding hand's trigger for a few seconds, readable to a player who looks at that hand.</summary>
    IEnumerator ButtonLabelStep()
    {
        yield return GiveAndHold(FindItem(SandboxActions.Items, AppleFragment));
        if (!stepOk)
            yield break;
        var prompt = GUIManager.instance.itemPromptMain;
        yield return Until(
            () =>
                prompt.transform.parent != null
                && prompt.transform.parent.gameObject.activeInHierarchy
                && prompt.transform.parent.name.StartsWith(
                    "PeakXR_ButtonLabel",
                    System.StringComparison.Ordinal
                ),
            2f,
            $"use prompt still under '{Name(prompt.transform.parent)}'"
        );
        if (!stepOk)
            yield break;
        var hand = VRRig.Instance.Hand(LocalHold.ItemInLeftHand);
        float d = Vector3.Distance(prompt.transform.parent.position, hand.position);
        yield return ShotAt("ui-02-button-labels", CentreOf(prompt.transform.parent));
        var fails = new List<string>();
        if (d > 0.2f)
            fails.Add($"label {d:0.00} m from the holding hand");
        // Read the way a player does: looking at the hand (the labels show while you do, ButtonLabels.cs:22).
        var head = VRRig.Instance.Head;
        string read = Readable("use label", prompt, hand.position - head.position, fails);
        // The mod's own floor (ReadableText.MinDegrees) for the label and its button name, both on a dark plate.
        float Em(TMPro.TMP_Text t) =>
            ReadableText.Degrees(
                t.fontSize * Mathf.Abs(t.transform.lossyScale.y),
                Vector3.Distance(head.position, t.transform.position)
            );
        float labelDeg = Em(prompt);
        bool parts = ButtonLabels.TryParts(ItemPrompt.Main, out var glyphText, out var plate);
        float glyphDeg = parts ? Em(glyphText) : 0f;
        bool plateOn = parts && plate.isActiveAndEnabled && plate.color.a >= 0.9f;
        read += $", label {labelDeg:0.00} deg, button name {glyphDeg:0.00} deg, plate {plateOn}";
        if (labelDeg < ReadableText.MinDegrees - 0.05f)
            fails.Add($"label {labelDeg:0.00} deg, under the {ReadableText.MinDegrees} deg floor");
        if (parts && glyphDeg < ReadableText.MinDegrees - 0.05f)
            fails.Add(
                $"button name {glyphDeg:0.00} deg, under the {ReadableText.MinDegrees} deg floor"
            );
        if (!plateOn)
            fails.Add("no dark plate under the use label");
        bool onTop = UI.Common.OnTop.IsOnTop(prompt);
        if (!onTop)
            fails.Add("label is depth tested: the hand can cover it");
        // The label names its button and a leader runs from that name to the button on the controller.
        bool shown = ButtonLabels.TryShown(
            ItemPrompt.Main,
            out var glyph,
            out _,
            out _,
            out var to
        );
        float toHand = shown ? Vector3.Distance(to, hand.position) : -1f;
        if (!shown)
            fails.Add("no button name and leader on the use label");
        else
        {
            if (glyph != ControllerLabels.Glyph(VRControl.Trigger))
                fails.Add($"use label names '{glyph}', not the trigger");
            if (toHand > 0.1f)
                fails.Add($"leader ends {toHand:0.00} m from the hand, not on its trigger");
        }
        // A finger on the trigger lights the use label and dims the rest on that hand. The scripted rest pose already
        // touches the trigger (Rest, SelfTestRunner.Hands.cs), so lift it first and see the labels even out
        // (ControllerLabels.Emphasis: 0.85 untouched), or the touch below proves nothing.
        bool left = LocalHold.ItemInLeftHand;
        bool rested = Script(left).TriggerTouch;
        Script(left).TriggerTouch = false;
        yield return Wait(0.4f);
        bool mainUp = ButtonLabels.TryShown(ItemPrompt.Main, out _, out float idle, out _, out _);
        bool throwUp = ButtonLabels.TryShown(
            ItemPrompt.Throw,
            out _,
            out float idleThrow,
            out _,
            out _
        );
        if (mainUp && idle >= 0.95f)
            fails.Add($"the use label stays lit ({idle:0.00}) with no finger on the trigger");
        if (throwUp && idleThrow <= 0.6f)
            fails.Add(
                $"the throw label stays dimmed ({idleThrow:0.00}) with no finger on a button"
            );
        Script(left).TriggerTouch = true;
        yield return Wait(0.4f);
        ButtonLabels.TryShown(ItemPrompt.Main, out _, out float lit, out _, out _);
        bool throwShown = ButtonLabels.TryShown(
            ItemPrompt.Throw,
            out _,
            out float dim,
            out _,
            out _
        );
        Script(left).TriggerTouch = rested;
        if (lit < 0.95f)
            fails.Add($"touching the trigger left the use label at {lit:0.00}");
        if (throwShown && dim > 0.6f)
            fails.Add($"touching the trigger left the throw label at {dim:0.00}");
        Conclude(
            fails,
            new List<string>
            {
                $"'{prompt.text}' {d:0.000} m from the hand, on top {onTop}, name '{glyph}', leader to {toHand:0.000} m from the grip, untouched {idle:0.00}, touched {lit:0.00} vs throw {(throwShown ? dim.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) : "hidden")}, {read}",
            }
        );
    }

    /// <summary>What ui.03 counts as rock in front of a sign: the layers world panels keep clear of
    /// (WorldCanvases.WallMask), a hit with a rigidbody (an item, a body) skipped.</summary>
    private static readonly int BannerRockMask = LayerMask.GetMask(
        "Terrain",
        "Map",
        "Default",
        "MirrorHidden",
        "MIrror"
    );

    private static readonly RaycastHit[] bannerHits = new RaycastHit[16];
    private static readonly Collider[] bannerOverlaps = new Collider[16];

    /// <summary>
    /// ui.03's own check of where the sign stands, written apart from WorldBanners' 5 x 3 ray grid so the step does
    /// not check the rule against itself: one ray down the sign's centre line and two boxes. The sign must stand before
    /// the rock that ray meets (0.1 m margin, never nearer than 0.5 m), its face must not be in rock, and a sign
    /// nearer than the 30 m it wants must have rock just behind it within its own view. Null when it holds.
    /// </summary>
    /// <param name="half">The sign's half extents where it stands, metres.</param>
    private static string BannerPlacementFault(
        Vector3 eye,
        Vector3 at,
        Quaternion rotation,
        Vector2 half,
        out float centreHit
    )
    {
        const float Margin = 0.1f;
        const float Floor = 0.5f;
        const float Tolerance = 0.1f;
        var toSign = at - eye;
        float d = toSign.magnitude;
        var dir = toSign / d;
        var backfaces = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        try
        {
            centreHit = float.PositiveInfinity;
            int n = Physics.RaycastNonAlloc(
                eye,
                dir,
                bannerHits,
                WorldBanners.Distance,
                BannerRockMask,
                QueryTriggerInteraction.Ignore
            );
            for (int i = 0; i < n; i++)
                if (bannerHits[i].rigidbody == null)
                    centreHit = Mathf.Min(centreHit, bannerHits[i].distance);
            if (d > WorldBanners.Distance + Tolerance || d < Floor - Tolerance)
                return F($"sign {d:0.00} m away, outside {Floor:0.0}-{WorldBanners.Distance:0} m");
            if (d > Mathf.Max(Floor, centreHit - Margin) + Tolerance)
                return F(
                    $"sign {d:0.00} m away, behind the rock its centre line meets at {centreHit:0.00} m"
                );
            bool atFloor = d <= Floor + Tolerance;
            // Its face, inset a little from the edges: rock there means the sign is in it (drawn on top only at the
            // floor).
            if (
                !atFloor
                && RockIn(at, new Vector3(half.x * 0.8f, half.y * 0.8f, 0.02f), rotation)
                    is { } inside
            )
                return F($"sign {d:0.00} m away has {inside} through its face");
            // Pulled in: the rock that did it lies just behind, inside the sign's view (its angular size kept).
            if (!atFloor && d < WorldBanners.Distance - 0.75f)
            {
                const float Depth = 0.5f;
                float grow = (d + Depth) / d;
                var slab = new Vector3(half.x * grow, half.y * grow, 0.5f * Depth);
                if (RockIn(at + dir * (0.5f * Depth), slab, rotation) == null)
                    return F(
                        $"sign {d:0.00} m away, short of {WorldBanners.Distance:0} m with no rock within {Depth:0.0} m behind it"
                    );
            }
            return null;
        }
        finally
        {
            Physics.queriesHitBackfaces = backfaces;
        }

        static string RockIn(Vector3 centre, Vector3 halfExtents, Quaternion rotation)
        {
            int n = Physics.OverlapBoxNonAlloc(
                centre,
                halfExtents,
                bannerOverlaps,
                rotation,
                BannerRockMask,
                QueryTriggerInteraction.Ignore
            );
            for (int i = 0; i < n; i++)
                if (bannerOverlaps[i].attachedRigidbody == null)
                    return bannerOverlaps[i].name;
            return null;
        }
    }

    /// <summary>ui.03: <c>GUIManager.SetHeroTitle</c> (GUIManager.cs:484) shows the title on a sign ahead, 30 m out or
    /// nearer where terrain is in the way.</summary>
    IEnumerator BannerStep()
    {
        var gui = GUIManager.instance;
        // The title coroutine reads the day counter (GUIManager.cs:499).
        if (DayNightManager.instance == null)
        {
            stepSkip = "no DayNightManager here";
            yield break;
        }
        // The title the game shows on reaching this biome (MountainProgressHandler.cs:105), not a made-up one.
        var progress = Singleton<MountainProgressHandler>.Instance;
        var points = progress != null ? progress.progressPoints : null;
        var map = Singleton<MapHandler>.Instance;
        int segment = map != null ? (int)map.GetCurrentSegment() : 0;
        string title =
            points != null && points.Length > 0
                ? points[Mathf.Clamp(segment, 0, points.Length - 1)].localizedTitle
                : "PEAKXR";
        gui.SetHeroTitle(title, null);
        yield return Until(() => gui.heroObject.activeSelf, 3f, "hero title never showed");
        if (!stepOk)
            yield break;
        float shown = Now;
        // The title, the day and then the time of day type out (GUIManager.cs:513-532); the full line holds 1.5 s
        // before the fade, so check and shoot the finished sign.
        string timeOfDay = DayNightManager.instance.TimeOfDayString();
        yield return WaitFor(() => gui.heroTimeOfDayText.text == timeOfDay, 8f);
        string dayLine = gui.heroTimeOfDayText.text;
        var sign = gui.heroObject.transform.parent;
        var head = VRRig.Instance.Head;
        // Measured from the eye the sign was placed from, not the head now: the rule is checked, not re-run.
        var from = WorldBanners.HeroPlacedFrom;
        float d = sign != null ? Vector3.Distance(sign.position, from) : 0f;
        float centreHit = float.PositiveInfinity;
        string placement = null;
        if (sign != null && d > 0.01f)
        {
            var part = (RectTransform)gui.heroObject.transform;
            var mpp = SpatialMath.MetersPerPixel(
                WorldBanners.Distance,
                SpatialRuntime.DegreesPerPixel
            );
            // Its half extents at 30 m, carried to where it stands at the same angular size.
            placement = BannerPlacementFault(
                from,
                sign.position,
                sign.rotation,
                0.5f * mpp * part.localScale.x * part.rect.size * (d / WorldBanners.Distance),
                out centreHit
            );
        }
        yield return Shot("ui-03-world-banner");
        var fails = new List<string>();
        string read = "";
        if (sign == null || sign.name != "PeakXR_HeroSign")
            fails.Add($"hero title under '{(sign != null ? sign.name : "none")}'");
        else if (placement != null)
            fails.Add(placement);
        else
        {
            // The title render texture (heroImage, GUIManager.cs:152) is what the sign draws.
            var image = gui.heroImage.rectTransform;
            read = Readable(
                "title",
                image.TransformPoint(image.rect.center),
                image.forward,
                image.TransformVector(0f, image.rect.height, 0f).magnitude,
                Alpha(gui.heroImage),
                head.forward,
                fails
            );
        }
        // The title types out and fades over about 6 s (GUIManager.cs:496-542); later steps' frames must not show it.
        yield return WaitFor(() => !gui.heroObject.activeSelf, 12f - (Now - shown));
        if (gui.heroObject.activeSelf)
            fails.Add("hero title still up after 12 s");
        Conclude(
            fails,
            new List<string>
            {
                $"'{title}' sign {d:0.0} m away (centre ray rock at {centreHit:0.0} m), day line '{dayLine}' at the check, gone {Now - shown:0.0} s after it showed, {read}",
            }
        );
    }

    /// <summary>ui.04: holding a rope spool shows <c>UI_Rope</c> (GUIManager.cs:635) next to the spool, its length
    /// (<c>ropeLengthText</c>, UI_Rope.cs:68) readable to a player who looks at the spool.</summary>
    IEnumerator RopeGaugeStep()
    {
        yield return GiveAndHold(FindItem(SandboxActions.Items, "RopeSpool"));
        if (!stepOk)
            yield break;
        var rope = GUIManager.instance.ui_rope;
        yield return Until(() => rope.gameObject.activeInHierarchy, 3f, "rope gauge never showed");
        if (!stepOk)
            yield break;
        yield return Wait(0.3f);
        // ui.03's hero title runs about 6 s and its sign would stand in front of the gauge in the screenshot.
        var hero = GUIManager.instance.heroObject;
        yield return WaitFor(() => hero == null || !hero.activeInHierarchy, 8f);
        var box = rope.transform.parent;
        // What is drawn, not the rect: UI_Rope's text and coil sit far from its rect's centre.
        var text = rope.ropeLengthText;
        var drawn =
            text != null
                ? text.transform.TransformPoint(text.textBounds.center)
                : rope.transform.position;
        float d = Vector3.Distance(drawn, HeldItem().Center());
        yield return ShotAt("ui-04-rope-gauge", drawn);
        var fails = new List<string>();
        string read = "";
        if (box == null || box.name != "PeakXR_RopeGauge")
            fails.Add($"rope gauge under '{(box != null ? box.name : "none")}'");
        else if (d > 0.4f)
            fails.Add($"gauge {d:0.00} m from the spool");
        else
        {
            var eye = VRRig.Instance.Head.position;
            read = Readable("rope length", text, HeldItem().Center() - eye, fails);
            // Drawn over the depth buffer, nothing hides it; else nothing solid may stand between the eye and the
            // text (the spool, the hand). Starts clear of the head's own collider.
            bool onTop = UI.Common.OnTop.IsOnTop(text);
            var dir = (drawn - eye).normalized;
            if (
                !onTop
                && Physics.Linecast(
                    eye + dir * 0.1f,
                    drawn,
                    out var hit,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore
                )
            )
                fails.Add(
                    F($"rope length hidden behind '{hit.collider.name}' at {hit.distance:0.00} m")
                );
            read += $", on top {onTop}";
        }
        Conclude(fails, new List<string> { $"gauge {d:0.00} m from the spool, {read}" });
    }

    /// <summary>An Apple 0.8 m out on the head's ray, held still there and hovered, handed to
    /// <paramref name="found"/>; a failed step instead if the game never makes it the target.</summary>
    IEnumerator HoveredApple(System.Action<Item> found)
    {
        var head = VRRig.Instance.Head;
        var spot = head.position + Flat(head.forward) * 0.8f + Vector3.down * 0.2f;
        var world = SpawnTestItem(AppleFragment, spot);
        if (world == null)
            yield break;
        yield return HoverOnHeadRay(world, head);
        if (!ReferenceEquals(Interaction.instance.currentHovered, world))
        {
            Fail("the Apple never became the hovered target");
            yield break;
        }
        found(world);
    }

    /// <summary>Em height of <paramref name="t"/> in degrees from the eye: its font size through its world scale.</summary>
    static float EmDegrees(TMP_Text t) =>
        ReadableText.Degrees(
            t.fontSize * Mathf.Abs(t.transform.lossyScale.y),
            Vector3.Distance(VRRig.Instance.Head.position, t.transform.position)
        );

    /// <summary>vr.240: <c>UI.TextScale</c> 1.5 draws the target prompt's name (an Apple on the head's ray, with
    /// <c>UI.TargetNames</c> on) and a refusal's reason label (Locked at the right hand, with <c>UI.ActionHintText</c>
    /// on so it shows every time) 1.5 times the angular size they have at 1, both at or above
    /// <see cref="ReadableText.MinDegrees"/>. The settings go back.</summary>
    IEnumerator TextScaleStep()
    {
        var entry = UI.World.UIWorldFeature.Settings?.TextScale;
        var hintText = AffordanceFeature.ActionHintText;
        if (entry == null || !Affordance.Enabled || hintText == null)
        {
            stepSkip = "ui.world or ui.affordance not installed";
            yield break;
        }
        float saved = entry.Value;
        bool hintWas = hintText.Value;
        bodyRestore += () =>
        {
            entry.Value = saved;
            hintText.Value = hintWas;
        };
        hintText.Value = true;
        entry.Value = 1f;
        Item world = null;
        yield return HoveredApple(w => world = w);
        if (world == null)
            yield break;
        var head = VRRig.Instance.Head;
        var nameText = GUIManager.instance.interactNameText;
        var size = new float[4];
        for (int pass = 0; pass < 2 && stepOk; pass++)
        {
            entry.Value = pass == 0 ? 1f : 1.5f;
            for (float t = Now + 0.3f; Now < t; )
            {
                HoldOnHeadRay(world, head);
                yield return null;
            }
            size[pass * 2] = EmDegrees(nameText);
            Affordance.ResetGate();
            if (!Affordance.DenyAtHand(false, DenyReason.Locked))
            {
                Fail("Locked refusal not accepted");
                yield break;
            }
            yield return Until(() => AffordanceRuntime.LabelShown, 1f, "no reason label");
            if (!stepOk)
                yield break;
            yield return null;
            size[pass * 2 + 1] = EmDegrees(AffordanceRuntime.Label);
        }
        var fails = new List<string>();
        for (int i = 0; i < 2; i++)
        {
            string what = i == 0 ? "prompt name" : "reason label";
            float at1 = size[i],
                at15 = size[i + 2];
            if (at1 < ReadableText.MinDegrees - 0.05f)
                fails.Add(
                    F($"{what} {at1:0.00} deg at 1, under the {ReadableText.MinDegrees} deg floor")
                );
            if (Mathf.Abs(at15 / Mathf.Max(at1, 1e-4f) - 1.5f) > 0.08f)
                fails.Add(F($"{what} {at1:0.00} deg at 1 but {at15:0.00} deg at 1.5"));
        }
        yield return ShotAt("vr-240-text-scale", world.Center());
        Conclude(
            fails,
            new List<string>
            {
                F(
                    $"name {size[0]:0.00} -> {size[2]:0.00} deg, reason label {size[1]:0.00} -> {size[3]:0.00} deg"
                ),
            }
        );
    }

    /// <summary>vr.242 (P2-6): a slab on the game's Map layer (in <c>HelperFunctions.terrainMapMask</c>,
    /// HelperFunctions.cs:27) across the line from the eye to the target prompt of an Apple on the head's ray, but
    /// clear of the ray to the Apple so it stays hovered, dims the prompt to <see cref="PromptOcclusion.DimAlpha"/>
    /// within 0.8 s; with the slab gone it comes back to full. The Apple is held 1.3 m out (the game's interact reach
    /// is 2 m, Interaction.cs:7): at 0.8 m the prompt sits at the fruit's front, about 0.65 m away, and a slab 0.3 m
    /// out falls inside the margin <see cref="PromptOcclusion.Blocked"/> leaves for the target's own surface.</summary>
    IEnumerator PromptOcclusionStep()
    {
        Item world = null;
        yield return HoveredApple(w => world = w);
        if (world == null)
            yield break;
        var head = VRRig.Instance.Head;
        for (float t = Now + 0.4f; Now < t; )
        {
            HoldFar(world, head);
            yield return null;
        }
        var label = GUIManager.instance.interactName.transform.parent;
        if (label == null || label.name != "PeakXR_TargetPrompt")
        {
            Fail("the Apple's name is not on the target prompt");
            yield break;
        }
        int map = LayerMask.NameToLayer("Map");
        var eye = head.position;
        var toLabel = (label.position - eye).normalized;
        var toApple = (world.Center() - eye).normalized;
        // 0.3 m out on the label's line; the gap to the Apple's line there decides how low the slab may reach.
        var p = eye + toLabel * 0.3f;
        var q = eye + toApple * Vector3.Dot(p - eye, toApple);
        float gap = Vector3.Distance(p, q);
        if (map < 0 || gap < 0.008f)
        {
            Fail(F($"no Map layer ({map}) or the label sits on the Apple's line ({gap:0.000} m)"));
            yield break;
        }
        var up = (p - q) / gap;
        var bottom = q + up * (gap * 0.4f);
        var top = p + up * 0.2f;
        var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        slab.name = "PeakXR_TestOccluder";
        slab.layer = map;
        spawned.Add(slab);
        slab.transform.SetPositionAndRotation(
            (bottom + top) * 0.5f,
            Quaternion.LookRotation(toLabel, up)
        );
        slab.transform.localScale = new Vector3(0.2f, Vector3.Distance(bottom, top), 0.02f);
        Physics.SyncTransforms();
        var fails = new List<string>();
        var notes = new List<string>();
        for (float t = Now + 0.8f; Now < t; )
        {
            HoldFar(world, head);
            yield return null;
        }
        bool hovered = ReferenceEquals(Interaction.instance.currentHovered, world);
        float dimmed = TargetPrompt.Dim;
        float drawn = Alpha(GUIManager.instance.interactNameText);
        notes.Add(
            F(
                $"behind the slab: prompt alpha {dimmed:0.00}, name drawn at {drawn:0.00}, hovered {hovered}, terrain hit {TargetPrompt.LastHit:0.00} m of {TargetPrompt.LastDistance:0.00} m to the prompt, target radius {TargetPrompt.LastRadius:0.00} m"
            )
        );
        if (!hovered)
            fails.Add("the slab took the Apple's hover: the test cannot see the prompt");
        else if (
            dimmed > PromptOcclusion.DimAlpha + 0.05f
            || drawn > PromptOcclusion.DimAlpha + 0.05f
        )
            fails.Add(F($"prompt behind terrain still at {dimmed:0.00} (name {drawn:0.00})"));
        yield return ShotAt("vr-242-prompt-occlusion", world.Center());
        Object.Destroy(slab);
        yield return null;
        Physics.SyncTransforms();
        for (float t = Now + 0.8f; Now < t; )
        {
            HoldFar(world, head);
            yield return null;
        }
        float open = TargetPrompt.Dim;
        notes.Add(F($"in the open: {open:0.00}"));
        if (open < 0.95f)
            fails.Add(F($"prompt stayed at {open:0.00} with nothing in the way"));
        Conclude(fails, notes);
    }

    /// <summary>vr.242's hold: the Apple on the head's ray 1.3 m out, far enough that the prompt clears its margin.</summary>
    static void HoldFar(Item item, Transform head)
    {
        if (item != null)
            item.transform.position += head.position + head.forward * 1.3f - item.Center();
    }
}
