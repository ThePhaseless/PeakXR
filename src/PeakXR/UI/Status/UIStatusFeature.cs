using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using PeakXR.Framework;

namespace PeakXR.UI.Status;

/// <summary>
/// ui.status: where the stamina and status HUD lives in VR and how status effects are felt. ui.world puts the
/// HUD on the body panel or the wrist (HudMode); this feature replaces the game's full-screen status flashes
/// (poison, injury, cold, spore warning, antigravity...) with a calm coloured glow at the edge of the view,
/// adds a steady peripheral tint while passed out, and turns stamina gains into controller pulses.
/// </summary>
public sealed class UIStatusFeature : Feature
{
    public override string Id => "ui.status";

    public override int Order => 55;

    internal static ConfigEntry<float> Intensity;
    internal static ConfigEntry<bool> DyingTint;
    internal static ConfigEntry<float> Haptics;

    /// <summary>Public ScreenVFX fields on GUIManager (A/GUIManager.cs:170-204), with their names for tinting.</summary>
    internal static FieldInfo[] ScreenFxFields = System.Array.Empty<FieldInfo>();

    protected internal override void Install()
    {
        const string section = "UI.Status";
        Intensity = Plugin.Settings.Bind(
            section,
            "PeripheralIntensity",
            0.5f,
            new ConfigDescription(
                "Strength of the edge glow that replaces full-screen status flashes (UI > StatusOverlays = Peripheral). 0 hides it.",
                new AcceptableValueRange<float>(0f, 1f)
            )
        );
        DyingTint = Plugin.Settings.Bind(
            section,
            "DyingTint",
            true,
            "While passed out, tint the edge of your view with the dying bar colour. It deepens as time runs out and never flashes."
        );
        Haptics = Plugin.Settings.Bind(
            section,
            "Haptics",
            0.5f,
            new ConfigDescription(
                "Controller pulse when you gain bonus stamina or a campfire morale boost. 0 turns it off.",
                new AcceptableValueRange<float>(0f, 1f)
            )
        );

        var fields = new List<FieldInfo>();
        foreach (var f in typeof(GUIManager).GetFields(BindingFlags.Public | BindingFlags.Instance))
            if (f.FieldType == typeof(ScreenVFX))
                fields.Add(f);
        if (fields.Count == 0)
            throw new System.MissingFieldException(nameof(GUIManager), "ScreenVFX fields");
        ScreenFxFields = fields.ToArray();

        Patch(typeof(MoraleBoostPatch));
        Patch(typeof(MoraleBoostTextPatch));
        Patch(typeof(ExtraStaminaPatch));

        StatusRuntime.Create();
    }
}
