using UnityEngine;
using UnityEngine.XR;

namespace PeakXR.Rig;

/// <summary>
/// Late pose reads for the rig: the HMD (re-read in <c>Application.onBeforeRender</c> for the lowest
/// latency) and the controllers' aim (pointer) poses, which <see cref="VR.VRInput"/> does not carry. Device
/// handles are cached and only looked up again when invalid, at most once per second.
/// </summary>
internal sealed class TrackedDevices
{
    private static readonly InputFeatureUsage<Vector3> PointerPosition = new("PointerPosition");
    private static readonly InputFeatureUsage<Quaternion> PointerRotation = new("PointerRotation");

    private InputDevice head;
    private InputDevice left;
    private InputDevice right;
    private float nextLookup;

    private void Refresh()
    {
        if (head.isValid && left.isValid && right.isValid)
            return;
        float now = Time.unscaledTime;
        if (now < nextLookup)
            return;
        nextLookup = now + 1f;
        if (!head.isValid)
            head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
        if (!left.isValid)
            left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        if (!right.isValid)
            right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
    }

    /// <summary>HMD centre-eye pose in tracking space.</summary>
    public bool TryGetHead(out Vector3 position, out Quaternion rotation)
    {
        Refresh();
        rotation = Quaternion.identity;
        if (!head.isValid)
        {
            position = default;
            return false;
        }
        return (
                head.TryGetFeatureValue(CommonUsages.centerEyePosition, out position)
                || head.TryGetFeatureValue(CommonUsages.devicePosition, out position)
            )
            && (
                head.TryGetFeatureValue(CommonUsages.centerEyeRotation, out rotation)
                || head.TryGetFeatureValue(CommonUsages.deviceRotation, out rotation)
            );
    }

    /// <summary>Controller aim pose in tracking space, when the provider reports one (OpenXR does).</summary>
    public bool TryGetAim(bool isLeft, out Vector3 position, out Quaternion rotation)
    {
        Refresh();
        var device = isLeft ? left : right;
        rotation = Quaternion.identity;
        position = default;
        return device.isValid
            && device.TryGetFeatureValue(PointerPosition, out position)
            && device.TryGetFeatureValue(PointerRotation, out rotation);
    }
}
