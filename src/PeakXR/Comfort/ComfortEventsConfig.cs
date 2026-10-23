using System;
using BepInEx.Configuration;
using PeakXR.Core;

namespace PeakXR.Comfort;

/// <summary>How end cutscenes and scripted camera shots are shown in the headset.</summary>
public enum CutsceneView
{
    /// <summary>A still 3D viewpoint at the cutscene camera; you look around freely; cuts become fades.</summary>
    Viewpoint,

    /// <summary>Opt-in: the viewpoint glides after the cutscene camera (motion you did not make).</summary>
    FollowCamera,

    /// <summary>ui.runflow's world-locked cinema screen for the end cutscene; scripted shots stay a viewpoint.</summary>
    Screen,
}

/// <summary>Settings of <see cref="ComfortEventsFeature"/>: one treatment per event class plus a master intensity.</summary>
internal static class ComfortEventsConfig
{
    private const string Section = "ComfortEvents";

    private static readonly ConfigEntry<ComfortTreatment>[] treatments =
        new ConfigEntry<ComfortTreatment>[Enum.GetValues(typeof(ComfortEvent)).Length];
    private static ConfigEntry<float> intensity;
    private static ConfigEntry<bool> haptics;
    private static ConfigEntry<bool> pressure;
    private static ConfigEntry<CutsceneView> cutscene;
    private static ConfigEntry<bool> mirrors;

    public static float Intensity => intensity?.Value ?? 1f;
    public static bool Haptics => haptics?.Value ?? true;
    public static bool Pressure => pressure?.Value ?? true;
    public static CutsceneView Cutscene => cutscene?.Value ?? CutsceneView.Viewpoint;
    public static bool Mirrors => mirrors?.Value ?? true;

    public static ComfortTreatment Treatment(ComfortEvent e) =>
        treatments[(int)e]?.Value ?? ComfortPolicy.Default(e);

    public static void Bind(ConfigFile file)
    {
        intensity = file.Bind(
            Section,
            "Intensity",
            1f,
            new ConfigDescription(
                "Master strength of every event cue below (vignette pulses and rumble). 0 turns them all off.",
                new AcceptableValueRange<float>(0f, 1f)
            )
        );
        haptics = file.Bind(
            Section,
            "Haptics",
            true,
            new ConfigDescription(
                "Hits, launches, falls and shakes also rumble the controllers, whatever their treatment."
            )
        );
        pressure = file.Bind(
            Section,
            "WindPressure",
            true,
            new ConfigDescription(
                "Wind and a tornado's pull press faintly on the hand facing them: hide behind rock and it fades, "
                    + "crouch near a tornado and it eases. A light tap a few times a second, never a buzz."
            )
        );
        Bind(
            file,
            ComfortEvent.Knockback,
            "A blast, hit or bonk shoves your body (explosions, mobs, falling rocks)."
        );
        Bind(
            file,
            ComfortEvent.Launch,
            "You are thrown far (cannon, geyser, bad mushroom, jump pads). ThirdPerson watches from a "
                + "horizon-locked view behind you until you land."
        );
        Bind(
            file,
            ComfortEvent.Ragdoll,
            "Your body starts tumbling. The view itself follows Camera.TakeoverView and Camera.DetachOnRagdoll."
        );
        // A config still on the old rumble-only default gets the vignette; a player's own choice stays.
        Framework.ConfigMigration.FromOldDefault(
            treatments[(int)ComfortEvent.Ragdoll],
            ComfortTreatment.HapticPulse
        );
        Bind(
            file,
            ComfortEvent.ForcedLook,
            "The game turns your character (grabs, tongues, scripted looks). Your view never turns."
        );
        Bind(
            file,
            ComfortEvent.Shake,
            "Camera shake (explosions, quakes, item effects). The view never shakes in VR."
        );
        Bind(
            file,
            ComfortEvent.StatusFlash,
            "Full-view status flashes (damage, cold, heat, poison). Overlays are drawn per eye by ui.world."
        );
        Bind(
            file,
            ComfortEvent.PostEffect,
            "Screen effects that blur, warp or darken the view (dizzy, blind, drunk)."
        );
        Bind(
            file,
            ComfortEvent.Teleport,
            "Your body jumps somewhere else without a run transition (ui.runflow fades its own warps)."
        );
        Bind(file, ComfortEvent.WindPush, "Wind, currents and other steady pushes move your body.");
        cutscene = file.Bind(
            Section,
            "CutsceneView",
            CutsceneView.Viewpoint,
            new ConfigDescription(
                "End cutscene and scripted shots. Viewpoint: a still spot at the cutscene camera where you look "
                    + "around; camera cuts fade. FollowCamera: the spot glides after the cutscene camera (opt in, "
                    + "motion you did not make). Screen: the end cutscene plays on ui.runflow's cinema screen."
            )
        );
        mirrors = file.Bind(
            Section,
            "Mirrors",
            true,
            new ConfigDescription(
                "Mirrors reflect what a single eye between your eyes would see, so the reflection looks flat. "
                    + "Off: mirrors show no reflection."
            )
        );
    }

    private static void Bind(ConfigFile file, ComfortEvent e, string what) =>
        treatments[(int)e] = file.Bind(
            Section,
            e.ToString(),
            ComfortPolicy.Default(e),
            new ConfigDescription(
                what
                    + " None, VignettePulse (darken the edge briefly), FadeHoldFade (black, then fade back), "
                    + "HapticPulse (rumble only) or ThirdPerson (detached view)."
            )
        );
}
