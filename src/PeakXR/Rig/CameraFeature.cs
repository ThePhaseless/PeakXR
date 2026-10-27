using BepInEx.Configuration;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.VR;

namespace PeakXR.Rig;

/// <summary>Which eye the desktop window mirrors.</summary>
public enum MirrorEye
{
    Left,
    Right,
    Off,
}

/// <summary>
/// Feature <c>rig.camera</c>: creates <see cref="VRRig"/> (its own DontDestroyOnLoad root) when VR starts and
/// turns the game's <c>MainCamera</c> into the head-tracked stereo camera. The game's camera code keeps running
/// for its side effects (spectate selection, ghost, transitions) but never moves the view; shake is zeroed.
/// When the game takes the camera (ragdoll, spectate, rocket, scripted shot, end cutscene) the view fades to
/// black and switches to a comfort view chosen by <see cref="TakeoverViewEntry"/>.
/// </summary>
public sealed class CameraFeature : Feature
{
    internal static ConfigEntry<TakeoverView> TakeoverViewEntry;
    internal static ConfigEntry<bool> DetachOnRagdoll;
    internal static ConfigEntry<float> ThirdPersonDistance;
    internal static ConfigEntry<float> ThirdPersonHeight;
    internal static ConfigEntry<float> ThirdPersonFollow;
    internal static ConfigEntry<float> BodySmoothing;
    internal static ConfigEntry<float> FadeSeconds;
    internal static ConfigEntry<float> RecenterHoldSeconds;
    internal static ConfigEntry<float> NearClip;
    internal static ConfigEntry<MirrorEye> Mirror;
    internal static ConfigEntry<float> WorldScaleEntry;
    internal static ConfigEntry<WorldScaleMode> WorldScaleModeEntry;

    /// <summary><c>Camera.WorldScale</c>, or its default before the feature binds it.</summary>
    internal static float WorldScaleValue => WorldScaleEntry?.Value ?? WorldScale.Default;

    /// <summary><c>Camera.WorldScaleMode</c>, or its default (Auto) before the feature binds it.</summary>
    internal static WorldScaleMode WorldScaleModeValue =>
        WorldScaleModeEntry?.Value ?? WorldScaleMode.Auto;

    /// <summary>The menu hold that recenters. Also the pause tap limit (player.locomotion), so the two never
    /// overlap and no hold length is dead.</summary>
    internal static float RecenterSeconds => RecenterHoldSeconds?.Value ?? 1f;

    public override string Id => "rig.camera";

    /// <summary>Right after xr.bootstrap, so the rig exists before features that read it subscribe.</summary>
    public override int Order => -900;

    protected internal override void Install()
    {
        const string section = "Camera";
        TakeoverViewEntry = Plugin.Settings.Bind(
            section,
            "TakeoverView",
            TakeoverView.ThirdPerson,
            new ConfigDescription(
                "View when the game takes the camera (ragdoll, passed out, spectating, rocket). ThirdPerson: a "
                    + "detached view behind the body that follows slowly. FirstPerson: stay at the eyes with the "
                    + "horizon locked. Both keep your own rotation and switch behind a fade. Comfort warning: "
                    + "FirstPerson rides the body while it tumbles, and a teammate's head while spectating, so the "
                    + "view moves without you; ThirdPerson is the comfortable choice."
            )
        );
        DetachOnRagdoll = Plugin.Settings.Bind(
            section,
            "DetachOnRagdoll",
            true,
            new ConfigDescription(
                "With TakeoverView ThirdPerson, also leave the body while it tumbles (falls, knock-downs)."
            )
        );
        ThirdPersonDistance = Plugin.Settings.Bind(
            section,
            "ThirdPersonDistance",
            3f,
            new ConfigDescription(
                "Metres behind the followed body in the detached view.",
                new AcceptableValueRange<float>(1f, 8f)
            )
        );
        ThirdPersonHeight = Plugin.Settings.Bind(
            section,
            "ThirdPersonHeight",
            1f,
            new ConfigDescription(
                "Metres above the followed body in the detached view.",
                new AcceptableValueRange<float>(0f, 4f)
            )
        );
        ThirdPersonFollow = Plugin.Settings.Bind(
            section,
            "ThirdPersonFollow",
            0.8f,
            new ConfigDescription(
                "How quickly the detached view follows the body (per second). 0 keeps it still and re-places "
                    + "it behind a fade when the body gets far away.",
                new AcceptableValueRange<float>(0f, 5f)
            )
        );
        BodySmoothing = Plugin.Settings.Bind(
            section,
            "BodySmoothing",
            20f,
            new ConfigDescription(
                "Filters the physics jitter of the body the view rides on (per second; higher follows tighter). "
                    + "The view never trails the body by more than 10 cm.",
                new AcceptableValueRange<float>(5f, 60f)
            )
        );
        FadeSeconds = Plugin.Settings.Bind(
            section,
            "FadeSeconds",
            0.25f,
            new ConfigDescription(
                "Fade duration when the view switches (out and in).",
                new AcceptableValueRange<float>(0.05f, 1f)
            )
        );
        RecenterHoldSeconds = Plugin.Settings.Bind(
            section,
            "RecenterHoldSeconds",
            1f,
            new ConfigDescription(
                "Hold the menu button this long to recenter (position, heading and eye height). Shorter presses "
                    + "pause.",
                new AcceptableValueRange<float>(0.5f, 3f)
            )
        );
        NearClip = Plugin.Settings.Bind(
            section,
            "NearClip",
            0.03f,
            new ConfigDescription(
                "Near clip plane in metres while in VR, so hands and held items close to the face stay visible.",
                new AcceptableValueRange<float>(0.01f, 0.1f)
            )
        );
        WorldScaleEntry = Plugin.Settings.Bind(
            section,
            "WorldScale",
            WorldScale.Default,
            new ConfigDescription(
                "With WorldScaleMode Manual: how big the world looks in VR. At 1 you see it at the game's own size; "
                    + "lower makes everything look smaller by growing you (higher by shrinking you): your eye "
                    + "separation, head motion and hand reach all scale together, and your eyes stay at your "
                    + "character's. Only you see it. Changing it applies behind a short fade.",
                new AcceptableValueRange<float>(WorldScale.Min, WorldScale.Max)
            )
        );
        WorldScaleModeEntry = Plugin.Settings.Bind(
            section,
            "WorldScaleMode",
            WorldScaleMode.Auto,
            new ConfigDescription(
                "Auto sizes the world to your height, measured at each recenter: your eyes stay at your character's "
                    + "and the game's floor sits on your real floor, so your head is as high above the ground in the "
                    + "game as in your room. Seated mode uses a standing adult's height instead. Manual uses "
                    + "WorldScale."
            )
        );
        // The old fixed default (0.87) becomes Auto; a scale the player chose stays, as Manual.
        if (
            Framework.ConfigMigration.Once("Camera.WorldScaleMode.FromWorldScale")
            && !UnityEngine.Mathf.Approximately(WorldScaleEntry.Value, WorldScale.Default)
        )
        {
            WorldScaleModeEntry.Value = WorldScaleMode.Manual;
            Plugin.Log.LogInfo(
                $"[config] Camera.WorldScale {WorldScaleEntry.Value} was chosen by hand: WorldScaleMode Manual"
            );
        }
        Mirror = Plugin.Settings.Bind(
            section,
            "DesktopMirror",
            MirrorEye.Left,
            new ConfigDescription("Eye shown in the desktop window. Off saves a little GPU time.")
        );

        CameraPatches.Resolve();
        Patch(typeof(CameraPatches.MovementLateUpdate));

        VRSession.Started += RigDriver.OnVRStarted;
        VRSession.Stopped += RigDriver.OnVRStopped;
        if (VRSession.Active)
            RigDriver.OnVRStarted();
    }
}
