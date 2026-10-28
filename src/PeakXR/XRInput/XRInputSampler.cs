using System;
using System.Collections.Generic;
using System.Text;
using PeakXR.Core;
using PeakXR.VR;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using UnityEngine.XR;
using NVector2 = System.Numerics.Vector2;

namespace PeakXR.XRInput;

/// <summary>
/// Fills <see cref="VRInput"/> once per frame from a custom PlayerLoop step appended to <c>EarlyUpdate</c>.
/// Why there: EarlyUpdate runs after <c>EarlyUpdate.XRUpdate</c> (fresh device state) and before FixedUpdate and
/// Update, so it precedes <c>CharacterMovement.Update</c> -> <c>CharacterInput.Sample</c> (CharacterMovement.cs:198)
/// and every physics step, whatever the script execution order or the game's Input System update mode
/// (<c>InputSystem.onBeforeUpdate</c> would follow that mode and can fire several times per frame or only in
/// fixed updates; <c>[DefaultExecutionOrder]</c> is not guaranteed for components from a runtime-loaded assembly).
/// </summary>
internal static class XRInputSampler
{
    private static readonly List<InputDevice> Found = new();
    private static readonly ControllerReader LeftReader = new(true);
    private static readonly ControllerReader RightReader = new(false);
    private static readonly HeadReader Head = new();
    private static bool dirty = true;
    private static int lastFrame = -1;

    /// <summary>Runs after each VR sample and the self-test's scripted hands: the toggle grip
    /// (<c>Interact.GripToggle</c>) and the self-test input edge log.</summary>
    internal static event Action AfterSample;

    public static void Install()
    {
        InputDevices.deviceConnected += MarkDirty;
        InputDevices.deviceDisconnected += MarkDirty;
        InputDevices.deviceConfigChanged += MarkDirty;

        var loop = PlayerLoop.GetCurrentPlayerLoop();
        var systems = loop.subSystemList;
        for (int i = 0; i < systems.Length; i++)
        {
            if (systems[i].type != typeof(EarlyUpdate))
                continue;
            var subs = new List<PlayerLoopSystem>(
                systems[i].subSystemList ?? Array.Empty<PlayerLoopSystem>()
            )
            {
                new() { type = typeof(XRInputSampler), updateDelegate = Sample },
            };
            systems[i].subSystemList = subs.ToArray();
            PlayerLoop.SetPlayerLoop(loop);
            return;
        }
        throw new InvalidOperationException("PlayerLoop has no EarlyUpdate phase");
    }

    private static void MarkDirty(InputDevice device) => dirty = true;

    /// <summary>The headset and controllers in use, by device name (which carries the interaction profile), for bug
    /// reports. Allocates.</summary>
    internal static string DeviceNames() =>
        $"head {Head.Name}, left {LeftReader.Name}, right {RightReader.Name}";

    private static void Sample()
    {
        if (Time.frameCount == lastFrame)
            return;
        lastFrame = Time.frameCount;

        if (!VRSession.Active)
        {
            VRInput.Left = default;
            VRInput.Right = default;
            VRInput.HeadTracked = false;
            dirty = true;
            return;
        }

        try
        {
            if (dirty)
            {
                dirty = false;
                Head.Acquire(Found);
                LeftReader.Acquire(Found);
                RightReader.Acquire(Found);
            }
            float dt = Time.unscaledDeltaTime;
            Head.Read();
            LeftReader.Read(ref VRInput.Left, dt);
            RightReader.Read(ref VRInput.Right, dt);
            // One-handed: the other controller may still be on (on a desk, in a lap); nothing reads it.
            if (VRInput.OneHanded)
                VRInput.Hand(!VRInput.LeftHanded) = new HandState
                {
                    Rotation = Quaternion.identity,
                };
            // The one controller's own layout: a wand has no A or B.
            BindingTable.Active =
                !VRInput.OneHanded ? BindingTable.Default
                : VRInput.Hand(VRInput.LeftHanded).WandPad ? BindingTable.OneHandedWand
                : BindingTable.OneHanded;
        }
        catch (Exception e)
        {
            // A runtime that throws from the legacy XR path must not take the frame down; retry on reconnect.
            Plugin.Log.LogError($"[xr.input] sampling failed: {e}");
            VRInput.Left.Tracked = VRInput.Right.Tracked = VRInput.HeadTracked = false;
            dirty = true;
        }
        VRInput.TestOverride?.Invoke();
        StickChordInput.Update();
        AfterSample?.Invoke();
    }

    internal static string[] UsageNames(InputDevice device, List<InputFeatureUsage> scratch)
    {
        scratch.Clear();
        if (!device.TryGetFeatureUsages(scratch))
            return Array.Empty<string>();
        var names = new string[scratch.Count];
        for (int i = 0; i < names.Length; i++)
            names[i] = scratch[i].name;
        return names;
    }

    internal static void LogDevice(string role, InputDevice device, string[] usages)
    {
        var sb = new StringBuilder();
        sb.Append("[xr.input] ").Append(role).Append(": ").Append(device.name);
        sb.Append(" (").Append(device.characteristics).Append(") usages: ");
        sb.Append(string.Join(", ", usages));
        Plugin.Log.LogInfo(sb.ToString());
    }

    /// <summary>First valid device with all of <paramref name="wanted"/>, preferring ones that also have
    /// <paramref name="prefer"/> and skipping ones with <paramref name="exclude"/>.</summary>
    internal static InputDevice Pick(
        List<InputDevice> found,
        InputDeviceCharacteristics wanted,
        InputDeviceCharacteristics prefer = InputDeviceCharacteristics.None,
        InputDeviceCharacteristics exclude = InputDeviceCharacteristics.None
    )
    {
        found.Clear();
        InputDevices.GetDevicesWithCharacteristics(wanted, found);
        InputDevice best = default;
        foreach (var d in found)
        {
            if (!d.isValid || (d.characteristics & exclude) != 0)
                continue;
            if (prefer != 0 && (d.characteristics & prefer) == prefer)
                return d;
            if (!best.isValid)
                best = d;
        }
        return best;
    }

    private sealed class HeadReader
    {
        private readonly List<InputFeatureUsage> scratch = new();
        private InputDevice device;
        private Usage<Vector3> position;
        private Usage<Quaternion> rotation;
        private Usage<bool> isTracked;

        public string Name => device.isValid ? device.name : "none";

        public void Acquire(List<InputDevice> found)
        {
            var d = Pick(found, InputDeviceCharacteristics.HeadMounted);
            if (d == device && d.isValid)
                return;
            device = d;
            if (!d.isValid)
                return;
            var names = UsageNames(d, scratch);
            LogDevice("head", d, names);
            position = Usage<Vector3>.Find(names, "CenterEyePosition", "DevicePosition");
            rotation = Usage<Quaternion>.Find(names, "CenterEyeRotation", "DeviceRotation");
            isTracked = Usage<bool>.Find(names, "IsTracked");
        }

        public void Read()
        {
            if (!device.isValid)
            {
                VRInput.HeadTracked = false;
                return;
            }
            bool tracked =
                !isTracked.Has || (device.TryGetFeatureValue(isTracked.Value, out bool t) && t);
            if (position.Has && device.TryGetFeatureValue(position.Value, out Vector3 p))
                VRInput.HeadPosition = p;
            else
                tracked = false;
            if (rotation.Has && device.TryGetFeatureValue(rotation.Value, out Quaternion q))
                VRInput.HeadRotation = q;
            else
                tracked = false;
            VRInput.HeadTracked = tracked;
        }
    }

    /// <summary>A usage looked up by name on one device; <see cref="Has"/> is false when the device lacks it.</summary>
    internal struct Usage<T>
        where T : struct
    {
        public bool Has;
        public InputFeatureUsage<T> Value;

        public static Usage<T> Find(string[] names, params string[] candidates)
        {
            var name = XRUsageNames.Find(names, candidates);
            return name == null
                ? default
                : new Usage<T> { Has = true, Value = new InputFeatureUsage<T>(name) };
        }
    }

    internal static NVector2 N(Vector2 v) => new(v.x, v.y);
}
