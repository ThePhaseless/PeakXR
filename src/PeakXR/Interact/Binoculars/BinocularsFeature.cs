using BepInEx.Configuration;
using PeakXR.Framework;
using PeakXR.VR;

namespace PeakXR.Interact.Binoculars;

/// <summary>
/// <c>interact.binoculars</c>: in VR the binoculars never take over the headset view (vanilla moves the main
/// camera to an override pose and narrows its FOV, which is forced camera motion). The zoomed view renders from
/// the holding hand into a render texture shown on a small lens at the eyepiece, a near-eye lens when the
/// binoculars are raised to the face, or a world panel. Vanilla scroll zoom keeps working; a wrist twist of
/// the holding hand zooms too. Flat play is untouched.
/// </summary>
public sealed class BinocularsFeature : Feature
{
    public override string Id => "interact.binoculars";

    public override int Order => 30;

    protected internal override void Install()
    {
        BinocularsConfig.Bind(Plugin.Settings);
        BinocularLens.ItemOf = GameMember.Field<ItemActionBase, Item>("item");
        BinocularLens.CamOverride = GameMember.Field<MainCamera, CameraOverride>("camOverride");
        Patch(typeof(BinocularsPatches.Toggle));
        VRSession.Started -= BinocularLens.OnVRStarted;
        VRSession.Started += BinocularLens.OnVRStarted;
        VRSession.Stopped -= BinocularLens.OnVRStopped;
        VRSession.Stopped += BinocularLens.OnVRStopped;
    }
}

/// <summary>Binocular settings (section <c>Interaction.Binoculars</c>), bound by <see cref="BinocularsFeature"/>.</summary>
internal static class BinocularsConfig
{
    public const string Lens = "Lens";
    public const string Panel = "Panel";

    private static ConfigEntry<string> mode;
    private static ConfigEntry<int> lensResolution;
    private static ConfigEntry<float> eyeDistance;
    private static ConfigEntry<bool> twistZoom;

    /// <summary>True for the world panel fallback, false for the eyepiece lens.</summary>
    public static bool PanelMode =>
        mode != null && string.Equals(mode.Value, Panel, System.StringComparison.Ordinal);

    public static int LensResolution => lensResolution != null ? lensResolution.Value : 512;
    public static float EyeDistance => eyeDistance != null ? eyeDistance.Value : 0.12f;
    public static bool TwistZoom => twistZoom == null || twistZoom.Value;

    public static void Bind(ConfigFile file)
    {
        const string s = "Interaction.Binoculars";
        mode = file.Bind(
            s,
            "Mode",
            Lens,
            new ConfigDescription(
                "Where the zoomed view shows. Lens: on the binoculars' eyepiece, and in front of your eyes when you "
                    + "raise them to your face. Panel: on a floating panel about a metre in front of you. "
                    + "Your own view is never moved or zoomed.",
                new AcceptableValueList<string>(Lens, Panel)
            )
        );
        lensResolution = file.Bind(
            s,
            "LensResolution",
            512,
            new ConfigDescription(
                "Pixel size of the zoomed image. Higher is sharper and costs more GPU time. Applies the next time "
                    + "the binoculars are raised.",
                new AcceptableValueRange<int>(256, 1024)
            )
        );
        eyeDistance = file.Bind(
            s,
            "EyeDistance",
            0.12f,
            new ConfigDescription(
                "How close (metres) the eyepiece must come to your headset to show the zoomed view in front of "
                    + "your eyes (Lens mode).",
                new AcceptableValueRange<float>(0.05f, 0.3f)
            )
        );
        twistZoom = file.Bind(
            s,
            "TwistZoom",
            true,
            new ConfigDescription(
                "Twist the wrist of the hand holding the binoculars to zoom: clockwise zooms in. "
                    + "Scroll input keeps working."
            )
        );
    }
}
