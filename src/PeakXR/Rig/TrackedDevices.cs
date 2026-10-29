using UnityEngine;
using UnityEngine.XR;

namespace PeakXR.Rig;

/// <summary>
/// The rig's late HMD read (again in <c>Application.onBeforeRender</c> for the lowest latency), on top of the pose
/// <see cref="VR.VRInput"/> sampled at the frame's start. The device handle is cached and only looked up again when
/// invalid, at most once per second.
/// </summary>
internal sealed class TrackedDevices
{
    private InputDevice head;
    private float nextLookup;

    private void Refresh()
    {
        if (head.isValid)
            return;
        float now = Time.unscaledTime;
        if (now < nextLookup)
            return;
        nextLookup = now + 1f;
        head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
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
}
