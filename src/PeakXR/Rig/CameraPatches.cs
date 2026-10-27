using System;
using System.Reflection;
using HarmonyLib;
using PeakXR.Framework;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Rig;

/// <summary>Harmony patches and private game members of <see cref="CameraFeature"/>.</summary>
internal static class CameraPatches
{
    /// <summary>MainCamera.camOverride (MainCamera.cs:10, internal): scripted or binocular camera.</summary>
    internal static AccessTools.FieldRef<MainCamera, CameraOverride> CamOverride;

    /// <summary>MainCamera.cam (MainCamera.cs:8, internal).</summary>
    internal static AccessTools.FieldRef<MainCamera, Camera> Cam;

    /// <summary>MainCameraMovement.isRocketing (MainCameraMovement.cs:39, private).</summary>
    internal static AccessTools.FieldRef<MainCameraMovement, bool> IsRocketing;

    /// <summary>CharacterVoiceHandler._audioListener (CharacterVoiceHandler.cs:20, private), cached from
    /// MainCamera in Start (CharacterVoiceHandler.cs:143) and used for voice distance (:237).</summary>
    internal static AccessTools.FieldRef<CharacterVoiceHandler, AudioListener> VoiceListener;

    /// <summary>Character.GetBodypart(BodypartType) (Character.cs:1298, internal).</summary>
    internal static Func<Character, BodypartType, Bodypart> GetBodypart;

    public static void Resolve()
    {
        CamOverride = GameMember.Field<MainCamera, CameraOverride>("camOverride");
        Cam = GameMember.Field<MainCamera, Camera>("cam");
        IsRocketing = GameMember.Field<MainCameraMovement, bool>("isRocketing");
        VoiceListener = GameMember.Field<CharacterVoiceHandler, AudioListener>("_audioListener");
        GetBodypart = GameMember.Delegate<Func<Character, BodypartType, Bodypart>>(
            typeof(Character),
            "GetBodypart",
            [typeof(BodypartType)]
        );
    }

    internal struct SavedPose
    {
        public bool Valid;
        public Vector3 Position;
        public Quaternion Rotation;
        public float Fov;
    }

    /// <summary>
    /// MainCameraMovement.LateUpdate (MainCameraMovement.cs:61, [DefaultExecutionOrder(500)]). In VR the whole
    /// method still runs, so its side effects stay (spectate selection and switching at :175, ghost spawn and
    /// retarget at :141-152, spectate zoom data, StopSpectating ghost cleanup, god cam reset), but the pose
    /// and FOV it writes (CharacterCam :272, Spectate :154-172, OverrideCam :309, ragdoll roll and the
    /// GamefeelHandler.GetRotation shake :283-297, god cam's shake GodCam.cs:152 via :66) are put back afterwards.
    /// The rig then places the camera at the HMD.
    /// </summary>
    [HarmonyPatch]
    internal static class MovementLateUpdate
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(MainCameraMovement), "LateUpdate");

        private static void Prefix(MainCameraMovement __instance, out SavedPose __state)
        {
            __state = default;
            if (!VRSession.Active)
                return;
            var t = __instance.transform;
            __state.Valid = true;
            __state.Position = t.position;
            __state.Rotation = t.rotation;
            var main = MainCamera.instance;
            var cam = main != null ? Cam(main) : null;
            __state.Fov = cam != null ? cam.fieldOfView : 0f;
        }

        private static void Postfix(MainCameraMovement __instance, ref SavedPose __state)
        {
            if (!__state.Valid)
                return;
            __instance.transform.SetPositionAndRotation(__state.Position, __state.Rotation);
            var main = MainCamera.instance;
            var cam = main != null ? Cam(main) : null;
            if (cam != null && __state.Fov > 0f)
                cam.fieldOfView = __state.Fov;
        }
    }
}
