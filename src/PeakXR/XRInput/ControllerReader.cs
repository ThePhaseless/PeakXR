using System.Collections.Generic;
using PeakXR.Core;
using PeakXR.VR;
using UnityEngine;
using UnityEngine.XR;
using NVector3 = System.Numerics.Vector3;

namespace PeakXR.XRInput;

/// <summary>
/// Reads one controller (and, when present, the same side's hand-tracking device) into a <see cref="HandState"/>.
/// Usages are resolved by name on connect; which usage feeds which button per profile is Core
/// <see cref="ControllerMap"/>. Here: velocity from the position change when missing, tracking from device
/// validity, and per-finger curls from a curl usage on the controller or from hand-tracking joints.
/// </summary>
internal sealed class ControllerReader
{
    // Summed joint bend that counts as a closed finger: thumb has a shorter range than the others.
    private const float ThumbFullCurl = 120f;
    private const float FingerFullCurl = 240f;

    private readonly bool left;
    private readonly InputDeviceCharacteristics side;
    private readonly List<InputFeatureUsage> scratch = new();
    private readonly List<Bone> bones = new();
    private readonly NVector3[] joints = new NVector3[8];
    private readonly float[] curls = new float[FingerCurls.Count];
    private readonly XRInputSampler.Usage<float>[] fingerUsages = new XRInputSampler.Usage<float>[
        FingerCurls.Count
    ];

    private InputDevice device;
    private InputDevice handDevice;
    private bool anyFingerUsage;
    private bool haptics;
    private ControllerLayout layout;
    private Vector3 lastPosition;
    private bool hadPosition;

    private XRInputSampler.Usage<float> trigger,
        grip,
        padForce;
    private XRInputSampler.Usage<bool> triggerButton,
        gripButton,
        triggerTouch,
        primary,
        primaryTouch,
        secondary,
        secondaryTouch,
        stickClick,
        stickTouch,
        menu,
        padClick,
        isTracked;
    private XRInputSampler.Usage<Vector2> stick,
        pad;
    private XRInputSampler.Usage<Vector3> position,
        velocity,
        angularVelocity;
    private XRInputSampler.Usage<Quaternion> rotation;
    private XRInputSampler.Usage<UnityEngine.XR.Hand> handData;

    public ControllerReader(bool left)
    {
        this.left = left;
        side = left ? InputDeviceCharacteristics.Left : InputDeviceCharacteristics.Right;
    }

    public void Acquire(List<InputDevice> found)
    {
        var d = XRInputSampler.Pick(
            found,
            InputDeviceCharacteristics.Controller | side,
            prefer: InputDeviceCharacteristics.HeldInHand,
            exclude: InputDeviceCharacteristics.HandTracking
        );
        if (d != device || !d.isValid)
        {
            device = d;
            hadPosition = false;
            if (d.isValid)
                Resolve(d);
        }

        var h = XRInputSampler.Pick(found, InputDeviceCharacteristics.HandTracking | side);
        if (h != handDevice)
        {
            handDevice = h;
            handData = default;
            if (h.isValid)
            {
                var names = XRInputSampler.UsageNames(h, scratch);
                XRInputSampler.LogDevice(
                    left ? "left hand tracking" : "right hand tracking",
                    h,
                    names
                );
                handData = XRInputSampler.Usage<UnityEngine.XR.Hand>.Find(names, "HandData");
            }
        }
    }

    private void Resolve(InputDevice d)
    {
        var n = XRInputSampler.UsageNames(d, scratch);
        XRInputSampler.LogDevice(left ? "left controller" : "right controller", d, n);

        trigger = XRInputSampler.Usage<float>.Find(n, "Trigger");
        grip = XRInputSampler.Usage<float>.Find(n, "Grip");
        triggerButton = XRInputSampler.Usage<bool>.Find(n, "TriggerButton");
        gripButton = XRInputSampler.Usage<bool>.Find(n, "GripButton");
        triggerTouch = XRInputSampler.Usage<bool>.Find(n, "TriggerTouch", "IndexTouch");
        primary = XRInputSampler.Usage<bool>.Find(n, "PrimaryButton");
        primaryTouch = XRInputSampler.Usage<bool>.Find(n, "PrimaryTouch");
        secondary = XRInputSampler.Usage<bool>.Find(n, "SecondaryButton");
        secondaryTouch = XRInputSampler.Usage<bool>.Find(n, "SecondaryTouch");
        stick = XRInputSampler.Usage<Vector2>.Find(n, "Primary2DAxis");
        stickClick = XRInputSampler.Usage<bool>.Find(n, "Primary2DAxisClick");
        stickTouch = XRInputSampler.Usage<bool>.Find(n, "Primary2DAxisTouch");
        menu = XRInputSampler.Usage<bool>.Find(n, "MenuButton", "Menu");
        position = XRInputSampler.Usage<Vector3>.Find(n, "DevicePosition");
        rotation = XRInputSampler.Usage<Quaternion>.Find(n, "DeviceRotation");
        velocity = XRInputSampler.Usage<Vector3>.Find(n, "DeviceVelocity");
        angularVelocity = XRInputSampler.Usage<Vector3>.Find(n, "DeviceAngularVelocity");
        isTracked = XRInputSampler.Usage<bool>.Find(n, "IsTracked");

        pad = XRInputSampler.Usage<Vector2>.Find(n, "Secondary2DAxis");
        padClick = XRInputSampler.Usage<bool>.Find(n, "Secondary2DAxisClick");
        padForce = XRInputSampler.Usage<float>.Find(n, "Secondary2DAxisForce");
        layout = ControllerLayout.From(n);

        anyFingerUsage = false;
        for (int i = 0; i < fingerUsages.Length; i++)
            fingerUsages[i] = default;
        foreach (var name in n)
        {
            int f = XRUsageNames.FingerIndex(name);
            if (f >= 0 && !fingerUsages[f].Has)
            {
                fingerUsages[f] = XRInputSampler.Usage<float>.Find(n, name);
                anyFingerUsage = true;
            }
        }

        haptics = d.TryGetHapticCapabilities(out var caps) && caps.supportsImpulse;
    }

    public void Read(ref HandState s, float dt)
    {
        s = default;
        if (!device.isValid)
            return;

        s.Haptics = haptics;
        s.Tracked = !isTracked.Has || Bool(isTracked);
        if (position.Has && device.TryGetFeatureValue(position.Value, out Vector3 p))
            s.Position = p;
        else
            s.Tracked = false;
        s.Rotation =
            rotation.Has && device.TryGetFeatureValue(rotation.Value, out Quaternion q)
                ? q
                : Quaternion.identity;
        if (velocity.Has && device.TryGetFeatureValue(velocity.Value, out Vector3 v))
            s.Velocity = v;
        else if (hadPosition && dt > 0f)
            s.Velocity = (s.Position - lastPosition) / dt;
        if (angularVelocity.Has && device.TryGetFeatureValue(angularVelocity.Value, out Vector3 av))
            s.AngularVelocity = av;
        hadPosition = s.Tracked;
        lastPosition = s.Position;

        var raw = new ControllerSample
        {
            Trigger = Float(trigger),
            Grip = Float(grip),
            PadForce = Float(padForce),
            TriggerButton = Bool(triggerButton),
            GripButton = Bool(gripButton),
            TriggerTouch = Bool(triggerTouch),
            Primary = Bool(primary),
            PrimaryTouch = Bool(primaryTouch),
            Secondary = Bool(secondary),
            SecondaryTouch = Bool(secondaryTouch),
            StickClick = Bool(stickClick),
            StickTouch = Bool(stickTouch),
            Menu = Bool(menu),
            PadClick = Bool(padClick),
            Stick = XRInputSampler.N(Vec2(stick)),
            Pad = XRInputSampler.N(Vec2(pad)),
        };
        var b = ControllerMap.Map(layout, raw, moveHand: left != VRInput.LeftHanded);
        s.Trigger = b.Trigger;
        s.Grip = b.Grip;
        s.Stick = new Vector2(b.Stick.X, b.Stick.Y);
        s.TriggerTouch = b.TriggerTouch;
        s.Primary = b.Primary;
        s.Secondary = b.Secondary;
        s.PrimaryTouch = b.PrimaryTouch;
        s.SecondaryTouch = b.SecondaryTouch;
        s.StickClick = b.StickClick;
        s.StickTouch = b.StickTouch;
        s.Menu = b.Menu;

        s.SkeletalCurls = ReadCurls() ? curls : null;
    }

    private bool ReadCurls()
    {
        bool any = false;
        if (anyFingerUsage)
        {
            for (int i = 0; i < curls.Length; i++)
            {
                curls[i] = float.NaN;
                if (
                    fingerUsages[i].Has
                    && device.TryGetFeatureValue(fingerUsages[i].Value, out float c)
                )
                {
                    curls[i] = c;
                    any = true;
                }
            }
        }
        if (
            !any
            && handData.Has
            && handDevice.isValid
            && handDevice.TryGetFeatureValue(handData.Value, out UnityEngine.XR.Hand hand)
        )
        {
            for (int i = 0; i < curls.Length; i++)
            {
                curls[i] = float.NaN;
                bones.Clear();
                if (!hand.TryGetFingerBones((HandFinger)i, bones))
                    continue;
                int count = 0;
                foreach (var bone in bones)
                {
                    if (count < joints.Length && bone.TryGetPosition(out Vector3 jp))
                        joints[count++] = new NVector3(jp.x, jp.y, jp.z);
                }
                curls[i] = JointCurl.Curl(joints, count, i == 0 ? ThumbFullCurl : FingerFullCurl);
                any |= !float.IsNaN(curls[i]);
            }
        }
        return any;
    }

    private bool Bool(XRInputSampler.Usage<bool> u) =>
        u.Has && device.TryGetFeatureValue(u.Value, out bool b) && b;

    private float Float(XRInputSampler.Usage<float> u) =>
        u.Has && device.TryGetFeatureValue(u.Value, out float f) ? f : 0f;

    private Vector2 Vec2(XRInputSampler.Usage<Vector2> u) =>
        u.Has && device.TryGetFeatureValue(u.Value, out Vector2 v) ? v : Vector2.zero;
}
