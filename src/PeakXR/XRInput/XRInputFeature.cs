using BepInEx.Configuration;
using PeakXR.Core;
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
    internal static ConfigEntry<string> OneHanded;

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
        OneHanded = Plugin.Settings.Bind(
            "Accessibility",
            "OneHanded",
            "Off",
            new ConfigDescription(
                "Play with one controller, the Left or Right one: its stick walks forward and back and turns, A jumps, "
                    + "the stick click sprints (hold it to crouch), B pauses (hold it for the emote wheel), and every "
                    + "two-hand action (respawn chest lid, two-hand items, cheer, shove, soul pillar, carrying a "
                    + "friend, taking the pack off) works with that hand alone. The other controller is ignored.",
                new AcceptableValueList<string>("Off", "Left", "Right")
            )
        );
        ApplyHands();
        DominantHand.SettingChanged += (_, _) => ApplyHands();
        OneHanded.SettingChanged += (_, _) => ApplyHands();
        XRInputSampler.Install();
        GameInputIsolation.Install();
    }

    /// <summary>One-handed play makes the one controller the dominant hand, so every Dominant row lands on it.</summary>
    private static void ApplyHands()
    {
        string one = OneHanded.Value;
        VRInput.OneHanded = one is "Left" or "Right";
        VRInput.LeftHanded = VRInput.OneHanded ? one == "Left" : DominantHand.Value == "Left";
        BindingTable.Active = VRInput.OneHanded ? BindingTable.OneHanded : BindingTable.Default;
    }
}
