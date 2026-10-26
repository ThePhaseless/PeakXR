using UnityEngine;
#if PEAKXR_OPENVR
using Valve.VR;
#endif

namespace PeakXR.XR;

/// <summary>
/// The OpenXR grip pose for the OpenVR fallback. Unity's OpenVR plugin reports a controller's raw tracking pose as
/// DevicePosition/DeviceRotation: its controller update converts the device pose with no component offset
/// (libXRSDKOpenVR 1.2.4, <c>OpenVRInputProvider::Internal_UpdateDeviceState</c> passes <c>std::nullopt</c> to
/// <c>OpenVRToUnityTracking</c>), and a raw pose's axes differ per controller model. SteamVR render models carry the
/// grip OpenXR defines as the component <c>openxr_grip</c>, so this reads that component's pose in the raw frame
/// (IVRRenderModels::GetComponentState, <c>mTrackingToComponentLocal</c>) and xr.input applies it, so every runtime
/// hands PeakXR the same grip (<see cref="Core.GripFrame"/>). Looked up once per controller and model, retried at most
/// once a second; nothing per frame allocates.
/// </summary>
internal static class OpenVRGrip
{
    private struct Hand
    {
        public uint Device;
        public string Model;
        public bool Valid;
        public Vector3 Position;
        public Quaternion Rotation;
        public float NextCheck;
    }

    private static Hand left = new() { Device = uint.MaxValue };
    private static Hand right = new() { Device = uint.MaxValue };

    /// <summary>The grip pose in the controller's raw frame (Unity axes); false when it is not known (OpenXR, no
    /// render model, a model without the component), and the raw pose is used as it is.</summary>
    public static bool TryGet(bool isLeft, out Vector3 position, out Quaternion rotation)
    {
        ref var h = ref isLeft ? ref left : ref right;
#if PEAKXR_OPENVR
        if (Time.unscaledTime >= h.NextCheck)
        {
            h.NextCheck = Time.unscaledTime + 1f;
            Refresh(ref h, isLeft);
        }
#endif
        position = h.Position;
        rotation = h.Rotation;
        return h.Valid;
    }

#if PEAKXR_OPENVR
    private static readonly System.Text.StringBuilder Name = new(256);

    private static void Refresh(ref Hand h, bool isLeft)
    {
        var system = OpenVR.System;
        var models = OpenVR.RenderModels;
        if (system == null || models == null)
        {
            h.Valid = false;
            return;
        }
        uint device = system.GetTrackedDeviceIndexForControllerRole(
            isLeft ? ETrackedControllerRole.LeftHand : ETrackedControllerRole.RightHand
        );
        if (device == OpenVR.k_unTrackedDeviceIndexInvalid)
        {
            h.Valid = false;
            h.Device = device;
            return;
        }
        var error = ETrackedPropertyError.TrackedProp_Success;
        Name.Clear();
        system.GetStringTrackedDeviceProperty(
            device,
            ETrackedDeviceProperty.Prop_RenderModelName_String,
            Name,
            (uint)Name.Capacity,
            ref error
        );
        if (error != ETrackedPropertyError.TrackedProp_Success || Name.Length == 0)
        {
            h.Valid = false;
            return;
        }
        // Same controller and model: the component does not move (once a second, this compares without allocating).
        if (device == h.Device && h.Model != null && SameName(h.Model))
            return;
        h.Device = device;
        h.Model = Name.ToString();
        var controller = default(VRControllerState_t);
        var mode = default(RenderModel_ControllerMode_State_t);
        var component = default(RenderModel_ComponentState_t);
        h.Valid = models.GetComponentState(
            h.Model,
            "openxr_grip",
            ref controller,
            ref mode,
            ref component
        );
        if (h.Valid)
            ToUnity(component.mTrackingToComponentLocal, out h.Position, out h.Rotation);
        Plugin.Log.LogInfo(
            h.Valid
                ? $"[xr.input] OpenVR {(isLeft ? "left" : "right")} '{h.Model}': grip from openxr_grip at {h.Position:F3}, {h.Rotation.eulerAngles:F0}"
                : $"[xr.input] OpenVR {(isLeft ? "left" : "right")} '{h.Model}': no openxr_grip component, raw pose kept (hands may sit at an angle)"
        );
    }

    private static bool SameName(string model)
    {
        if (model.Length != Name.Length)
            return false;
        for (int i = 0; i < model.Length; i++)
            if (model[i] != Name[i])
                return false;
        return true;
    }

    /// <summary>An OpenVR right-handed 3x4 pose (x right, y up, -z forward) in Unity's left-handed axes: Z negated on
    /// both sides, S M S with S = diag(1, 1, -1).</summary>
    private static void ToUnity(HmdMatrix34_t m, out Vector3 position, out Quaternion rotation)
    {
        position = new Vector3(m.m3, m.m7, -m.m11);
        var forward = new Vector3(-m.m2, -m.m6, m.m10);
        var up = new Vector3(m.m1, m.m5, -m.m9);
        rotation = Quaternion.LookRotation(forward, up);
    }
#endif
}
