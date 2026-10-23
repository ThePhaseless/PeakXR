using PeakXR.Core;
using PeakXR.UI.Affordances;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Interact;

/// <summary>
/// <c>Interaction.GripMode</c> Toggle. Each hand's <see cref="GripLatch"/> rewrites <see cref="VRInput"/>'s grip right
/// after the sampler (and the self-test's scripted hands) fill it, so every grip reader, from the hands, climbing,
/// ropes and carrying to the avatar's fingers, sees a latched hand as a closed one without knowing about the
/// setting. What may latch is <see cref="PhysicalInteraction.GripHolds"/>. A latch ticks like a contact, an unlatch
/// like a cancel, so no hold ever ends unfelt.
/// </summary>
internal static class GripToggle
{
    private static readonly GripLatch LeftLatch = new();
    private static readonly GripLatch RightLatch = new();

    /// <summary>This hand's grip is latched (self-test vr.237, vr.238).</summary>
    internal static bool Latched(bool left) => (left ? LeftLatch : RightLatch).Latched;

    public static void Install() => XRInput.XRInputSampler.AfterSample += Apply;

    private static void Apply()
    {
        var mode = InteractConfig.Instance.GripMode;
        float dt = Time.unscaledDeltaTime;
        Step(LeftLatch, ref VRInput.Left, true, mode, dt);
        Step(RightLatch, ref VRInput.Right, false, mode, dt);
    }

    private static void Step(
        GripLatch latch,
        ref HandState state,
        bool left,
        GripMode mode,
        float dt
    )
    {
        // One-handed play ignores the other controller: no grip there, latched or not.
        if (VRInput.OneHanded && left != VRInput.LeftHanded)
        {
            latch.Reset();
            return;
        }
        var e = latch.Update(
            state.Grip,
            state.Tracked,
            PhysicalInteraction.GripHolds(left),
            mode,
            dt
        );
        state.Grip = latch.Grip;
        if (e == GripLatchEvent.Latched)
            Affordance.Contact(left);
        else if (e == GripLatchEvent.Unlatched)
            Affordance.Cancel(left);
    }
}
