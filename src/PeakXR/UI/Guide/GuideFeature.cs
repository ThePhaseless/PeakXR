using System;
using BepInEx.Configuration;
using PeakXR.Framework;
using PeakXR.UI.Settings;
using UnityEngine;

namespace PeakXR.UI.Guide;

/// <summary>
/// ui.guide: a VR Guide section in the VR settings tab (a looping ghost demonstration per VR mechanic, beside the
/// panel) and a short practice in the offline solo airport, offered once on the first VR launch. Teaches only what
/// PeakXR adds or changes in VR. No patches: the rows ride on ui.settings and the practice reads game state.
/// </summary>
public sealed class GuideFeature : Feature
{
    public override string Id => "ui.guide";

    // After ui.settings, whose tab it adds rows to.
    public override int Order => 61;

    internal static ConfigEntry<bool> Offered { get; private set; }

    protected internal override void Install()
    {
        if (!FeatureManager.IsActive<UISettingsFeature>())
            throw new InvalidOperationException("ui.settings is not active");
        Offered = Plugin.Settings.Bind(
            "Guide",
            "PracticeOffered",
            false,
            new ConfigDescription(
                "Internal: the VR practice offer on the title screen was answered (practised or dismissed).",
                null,
                VRTab.HiddenTag
            )
        );
        GuideScene.Install();
        var go = new GameObject("PeakXR_Guide");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<GuideStage>();
    }
}
