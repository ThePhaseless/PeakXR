using BepInEx.Configuration;
using PeakXR.Framework;
using PeakXR.VR;

namespace PeakXR.XRInput;

/// <summary>
/// <c>xr.input</c>: samples <c>UnityEngine.XR.InputDevices</c> into <see cref="VRInput"/> once per frame, before
/// any game script reads input. Provider-agnostic (OpenXR or the OpenVR plugin): every usage is looked up by name
/// on the connected device, so controllers that lack a value (Vive wand, WMR, Index, Touch) just leave it at its
/// default. No Harmony patches.
/// </summary>
public sealed class XRInputFeature : Feature
{
    public override string Id => "xr.input";

    // Before every consumer, so VRInput is live from the first frame they run.
    public override int Order => -100;

    internal static ConfigEntry<string> DominantHand;

    protected internal override void Install()
    {
        DominantHand = Plugin.Settings.Bind(
            "Input",
            "DominantHand",
            "Right",
            new ConfigDescription(
                "Your main hand. Left mirrors the controller layout: move and turn sticks swap, jump and drop move "
                    + "to the left controller.",
                new AcceptableValueList<string>("Right", "Left")
            )
        );
        // One-handed play is gone (#87): two tracked controllers are always assumed. The key leaves the file either
        // way; only a saved Left or Right is worth a line.
        string oneHanded = ConfigMigration.Removed(Plugin.Settings, "Accessibility", "OneHanded");
        if (oneHanded is "Left" or "Right")
            Plugin.Log.LogInfo(
                $"[config] Accessibility.OneHanded is removed and its saved {oneHanded} is ignored: both controllers are used"
            );
        ApplyHands();
        DominantHand.SettingChanged += (_, _) => ApplyHands();
        XRInputSampler.Install();
        GameInputIsolation.Install();
    }

    private static void ApplyHands() => VRInput.LeftHanded = DominantHand.Value == "Left";
}
