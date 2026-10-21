using System.Collections;
using System.Collections.Generic;
using PeakXR.Avatar;
using PeakXR.Interact;
using PeakXR.UI.Spatial;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Sandbox;

/// <summary>ui.spatial steps: HUD parts carried to the world (U1-U4). Spectating is checked by mob.PlayerGhost (SelfTestRunner.Context).</summary>
internal sealed partial class SelfTestRunner
{
    IEnumerator SpatialSteps()
    {
        yield return HandStep("ui.01-target-prompt", 20f, TargetPromptStep());
        yield return HandStep("ui.02-button-labels", 20f, ButtonLabelStep());
        yield return HandStep("ui.03-world-banner", 15f, BannerStep());
        yield return HandStep("ui.04-rope-gauge", 28f, RopeGaugeStep());
    }

    /// <summary>ui.01: an Apple in front of the eyes becomes <c>Interaction.currentHovered</c> (Interaction.cs:15); its
    /// name group (<c>GUIManager.interactName</c>, GUIManager.cs:84) leaves the HUD for a label between you and it.</summary>
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
        }
        yield return Shot("ui-01-target-prompt");
        Conclude(
            fails,
            new List<string> { $"label at {(label != null ? label.position : Vector3.zero)}" }
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
        Conclude(fails, new List<string> { $"'{prompt.text}' {d:0.000} m from the hand" });
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
        gui.SetHeroTitle("PEAKXR", null);
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
        Conclude(fails, new List<string> { $"sign {d:0.0} m away (ray {expected:0.0} m)" });
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
        float d = Vector3.Distance(rope.transform.position, HeldItem().Center());
        yield return ShotAt("ui-04-rope-gauge", CentreOf(rope.transform));
        var fails = new List<string>();
        if (box == null || box.name != "PeakXR_RopeGauge")
            fails.Add($"rope gauge under '{(box != null ? box.name : "none")}'");
        else if (d > 0.4f)
            fails.Add($"gauge {d:0.00} m from the spool");
        Conclude(fails, new List<string> { $"gauge {d:0.00} m from the spool" });
    }
}
