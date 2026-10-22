using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Interact.Tools;

/// <summary>
/// Glider steering from the arms. The game pushes the glider along the body's facing, which in VR follows the head,
/// so looking around would steer. With both grips closed on the wing, the push follows the heading of the spread
/// arms instead (<see cref="GliderSteer"/>): the player turns the glide with the arms and looks freely. The held
/// wing's pose already comes from the holding hand (avatar.local replaces the look-based item frame of
/// CharacterRagdoll.cs:234-243), so only the force needs redirecting.
/// </summary>
internal static class GliderHands
{
    private const float MinSpan = 0.3f;
    private const float BankGain = 1f;
    private const float MaxBankDegrees = 45f;
    private const float GripClosed = 0.5f;

    public static void AfterApply(CharacterMovement movement, float soarForwardForce)
    {
        if (soarForwardForce <= 0f || !VRSession.Active || !ToolsConfig.Instance.HandSteerGlider)
            return;
        var c = Character.localCharacter;
        if (c == null || c.refs.movement != movement || c.refs.hip == null)
            return;
        ref var l = ref VRInput.Left;
        ref var r = ref VRInput.Right;
        if (!l.Tracked || !r.Tracked)
            return;
        if (!LocalHold.TwoHand && (l.Grip < GripClosed || r.Grip < GripClosed))
            return;
        var rig = VRRig.Instance;
        if (rig == null || rig.LeftHand == null || rig.RightHand == null)
            return;
        if (
            !GliderSteer.TryForward(
                rig.LeftHand.position.N(),
                rig.RightHand.position.N(),
                MinSpan,
                BankGain,
                MaxBankDegrees,
                out var heading
            )
        )
            return;

        var vanilla = c.refs.hip.transform.forward;
        vanilla.y = 0f;
        var correction = (heading.U() * vanilla.magnitude - vanilla) * soarForwardForce;
        var parts = c.refs.ragdoll.partList;
        for (int i = 0; i < parts.Count; i++)
            parts[i].AddForce(correction, ForceMode.Acceleration);
    }
}
