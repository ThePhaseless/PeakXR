using System.Collections;
using System.Collections.Generic;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Interact;
using PeakXR.UI.Spatial;
using PeakXR.VR;
using UnityEngine;
using Zorro.Core;

namespace PeakXR.Sandbox;

/// <summary>ui.spatial steps: HUD parts carried to the world (U1-U4). Spectating is checked by mob.PlayerGhost (SelfTestRunner.Context).</summary>
internal sealed partial class SelfTestRunner
{
    IEnumerator SpatialSteps()
    {
        yield return HandStep("ui.01-target-prompt", 20f, TargetPromptStep());
        yield return HandStep("ui.02-button-labels", 20f, ButtonLabelStep());
        yield return HandStep("ui.03-world-banner", 25f, BannerStep());
        yield return HandStep("ui.04-rope-gauge", 28f, RopeGaugeStep());
        yield return HandStep("ui.05-wrist-hud", 15f, WristHudStep());
    }

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
            stepNote = "UI.HUD.HudMode is not Wrist";
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
        SandboxActions.AddStatus(CharacterAfflictions.STATUSTYPE.Cold, 0.25f);
        yield return MoveTo(
            left,
            // The display sits 12 cm down the forearm from the grip: the hand a little above eye level puts it in
            // the gaze (WristHud.LookDegrees), as a player looks at a watch.
            () => head.position + Flat(head.forward) * 0.35f + Vector3.up * 0.05f,
            0.5f
        );
        // Raised like a watch: fingers up, the back of the hand (grip -x left, +x right) toward the eye.
        var away = Flat(Script(left).Position - VRInput.HeadPosition);
        Script(left).Rotation = Quaternion.LookRotation(
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
        float across = hand != null ? Vector3.Angle(rest * Vector3.right, hand.forward) : 0f;
        float width = UI.World.HudSplit.WristWidthMeters;
        float degrees = 2f * Mathf.Atan2(width * 0.5f, toWrist.magnitude) * Mathf.Rad2Deg;
        var root = UI.World.HudSplit.WristRoot;
        int afflictions = 0;
        if (root != null)
            foreach (var a in root.GetComponentsInChildren<BarAffliction>(false))
                if (a.afflictionType == CharacterAfflictions.STATUSTYPE.Cold)
                    afflictions++;
        bool onTop = UI.Common.OnTop.AllOnTop(root);
        float cold = c.refs.afflictions.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Cold);
        string where = afflictions > 0 ? "" : ColdSlotWhere(root);
        yield return ShotAt("ui-05-wrist-hud", wrist);
        SandboxActions.ClearStatus(CharacterAfflictions.STATUSTYPE.Cold);
        stepNote = F(
            $"wrist HUD {width * 100f:0.0} cm ({degrees:0} deg) at {toWrist.magnitude:0.00} m, {facing:0} deg off facing the eye (turn {UI.World.HudSplit.WristFacing:0.00}), watch face {restFacing:0} deg off the eye, bar {across:0} deg to the forearm, on top {onTop}, Cold {cold:0.00}, Cold slots {afflictions}{where}"
        );
        if (!UI.World.HudSplit.WristValid)
            Fail("wrist HUD not posed");
        else if (facing > 25f)
            Fail("wrist HUD does not face the eye");
        else if (restFacing > 50f)
            Fail("watch face is not on the side of the wrist that faces the eye");
        else if (Mathf.Abs(across - 90f) > 15f)
            Fail("wrist bar does not run across the wrist");
        else if (degrees < 12f)
            Fail("wrist HUD too small to read");
        else if (!onTop)
            Fail("wrist HUD is depth tested: the arm can cover it");
        else if (afflictions == 0)
            Fail("Cold affliction not shown on the wrist");
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

    /// <summary>ui.01: an Apple in front of the eyes becomes <c>Interaction.currentHovered</c> (Interaction.cs:15); its
    /// name group (<c>GUIManager.interactName</c>, GUIManager.cs:84) leaves the HUD for a label just above it as you see it.</summary>
    IEnumerator TargetPromptStep()
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
        var world =
            SandboxActions.LastSpawned != null
                ? SandboxActions.LastSpawned.GetComponent<Item>()
                : null;
        spawned.Add(SandboxActions.LastSpawned);
        if (world == null)
        {
            Fail("spawned Apple has no Item");
            yield break;
        }
        // Deterministic hover: the rest hands do not point, so the game's ray comes from the head (Interaction.cs:196);
        // hold the Apple still on that ray, 0.8 m out, instead of letting it fall off it.
        if (world.rig != null)
            world.rig.isKinematic = true;
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
        }
        yield return Shot("ui-01-target-prompt");
        Conclude(
            fails,
            new List<string>
            {
                $"label at {(label != null ? label.position : Vector3.zero)}, {above:0.0} deg above the Apple",
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
    /// holding hand's trigger for a few seconds.</summary>
    IEnumerator ButtonLabelStep()
    {
        if (!SpatialFeature.Settings.ButtonLabels.Value)
        {
            stepNote = "UI.ButtonLabels is off";
            yield break;
        }
        yield return GiveAndHold(FindItem(new List<Item>(SandboxActions.Items), AppleFragment));
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
            $"use prompt still under '{prompt.transform.parent?.name}'"
        );
        if (!stepOk)
            yield break;
        var hand = VRRig.Instance.Hand(LocalHold.ItemInLeftHand);
        float d = Vector3.Distance(prompt.transform.parent.position, hand.position);
        yield return ShotAt("ui-02-button-labels", CentreOf(prompt.transform.parent));
        var fails = new List<string>();
        if (d > 0.2f)
            fails.Add($"label {d:0.00} m from the holding hand");
        bool onTop = UI.Common.OnTop.IsOnTop(prompt);
        if (!onTop)
            fails.Add("label is depth tested: the hand can cover it");
        // The label names its button and a leader runs from that name to the button on the controller.
        bool shown = ButtonLabels.TryShown(
            ItemPrompt.Main,
            out var glyph,
            out _,
            out var from,
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
        // A finger on the trigger lights the use label and dims the rest on that hand.
        bool left = LocalHold.ItemInLeftHand;
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
        Script(left).TriggerTouch = false;
        if (lit < 0.95f)
            fails.Add($"touching the trigger left the use label at {lit:0.00}");
        if (throwShown && dim > 0.6f)
            fails.Add($"touching the trigger left the throw label at {dim:0.00}");
        Conclude(
            fails,
            new List<string>
            {
                $"'{prompt.text}' {d:0.000} m from the hand, on top {onTop}, name '{glyph}', leader to {toHand:0.000} m from the grip, touched {lit:0.00} vs throw {(throwShown ? dim.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) : "hidden")}",
            }
        );
    }

    /// <summary>ui.03: <c>GUIManager.SetHeroTitle</c> (GUIManager.cs:484) shows the title on a sign ahead, 30 m out or
    /// nearer where terrain is in the way.</summary>
    IEnumerator BannerStep()
    {
        if (!SpatialFeature.Settings.WorldBanners.Value)
        {
            stepNote = "UI.WorldBanners is off";
            yield break;
        }
        var gui = GUIManager.instance;
        // The title coroutine reads the day counter (GUIManager.cs:499).
        if (DayNightManager.instance == null)
        {
            stepNote = "no DayNightManager here; skipped";
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
        yield return Wait(1.5f);
        var sign = gui.heroObject.transform.parent;
        var head = VRRig.Instance.Head;
        float d = sign != null ? Vector3.Distance(sign.position, head.position) : 0f;
        // The sign stands at the first solid hit along its direction, clamped to 4-30 m, else at 30 m
        // (WorldBanners.Sign.Place): repeat that ray.
        float expected = WorldBanners.Distance;
        if (sign != null && d > 0.01f)
        {
            var dir = (sign.position - head.position) / d;
            if (
                Physics.Raycast(
                    head.position + dir * 0.5f,
                    dir,
                    out var hit,
                    WorldBanners.Distance,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore
                )
            )
                expected = Mathf.Clamp(
                    hit.distance,
                    WorldBanners.NearestDistance,
                    WorldBanners.Distance
                );
        }
        yield return Shot("ui-03-world-banner");
        var fails = new List<string>();
        if (sign == null || sign.name != "PeakXR_HeroSign")
            fails.Add($"hero title under '{(sign != null ? sign.name : "none")}'");
        else if (Mathf.Abs(d - expected) > 0.75f)
            fails.Add($"sign {d:0.0} m away, its ray says {expected:0.0} m");
        // The title types out and fades over about 6 s (GUIManager.cs:496-542); later steps' frames must not show it.
        float shown = Now;
        yield return WaitFor(() => !gui.heroObject.activeSelf, 12f);
        if (gui.heroObject.activeSelf)
            fails.Add("hero title still up after 12 s");
        Conclude(
            fails,
            new List<string>
            {
                $"'{title}' sign {d:0.0} m away (ray {expected:0.0} m), gone {Now - shown + 1.5f:0.0} s after it showed",
            }
        );
    }

    /// <summary>ui.04: holding a rope spool shows <c>UI_Rope</c> (GUIManager.cs:635) next to the spool.</summary>
    IEnumerator RopeGaugeStep()
    {
        if (!SpatialFeature.Settings.HeldItemGauges.Value)
        {
            stepNote = "UI.HeldItemGauges is off";
            yield break;
        }
        yield return GiveAndHold(FindItem(new List<Item>(SandboxActions.Items), "RopeSpool"));
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
        if (box == null || box.name != "PeakXR_RopeGauge")
            fails.Add($"rope gauge under '{(box != null ? box.name : "none")}'");
        else if (d > 0.4f)
            fails.Add($"gauge {d:0.00} m from the spool");
        Conclude(fails, new List<string> { $"gauge {d:0.00} m from the spool" });
    }
}
