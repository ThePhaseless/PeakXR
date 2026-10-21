using System.Collections;
using PeakXR.Core;
using PeakXR.Locomotion;
using PeakXR.VR;
using PeakXR.XR;
using UnityEngine;

namespace PeakXR.Sandbox;

/// <summary>
/// Body-gesture steps of the scripted-hands run (player.locomotion <see cref="BodyGestures"/>): a scripted head dip
/// must crouch the character, and alternating arm swings with the stick forward must sprint. Rope, vine and piton
/// grips (interact.physical) need a hanging rope, a jungle vine or a placed piton and stay a headset check.
/// </summary>
internal sealed partial class SelfTestRunner
{
    IEnumerator BodySteps()
    {
        yield return HandStep("vr.40-physical-crouch", 15f, CrouchStep());
        yield return HandStep("vr.41-arm-swing-sprint", 20f, ArmSwingStep());
    }

    /// <summary>vr.40: the head held 40 % of standing height low sets <c>CharacterData.isCrouching</c>
    /// (CharacterMovement.cs:241); back up, it clears.</summary>
    IEnumerator CrouchStep()
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        if (!BodyGestures.CrouchOn || rig.SeatedMode)
        {
            stepSkip = "Locomotion.PhysicalCrouch off or seated mode";
            yield break;
        }
        float eye = RigMath.TrackedEyeHeight(rig.HeightOffset);
        float standing = XRRuntimeInfo.FloorTracking
            ? eye
            : HeightCalibration.DefaultStandingEyeHeight;
        float low = eye - standing * 0.4f;
        var hands = applyHands;
        VRInput.TestOverride = () =>
        {
            hands();
            var p = VRInput.HeadPosition;
            p.y = low;
            VRInput.HeadPosition = p;
        };
        yield return WaitFor(() => c.data.isCrouching, 2f);
        bool crouched = c.data.isCrouching;
        yield return Wait(0.6f);
        float lift = rig.CrouchLift;
        yield return Shot("vr-40-crouch");
        VRInput.TestOverride = hands;
        yield return WaitFor(() => !c.data.isCrouching, 2f);
        var note =
            $"head to {low:0.00} m (eye {eye:0.00}), crouched {crouched}, view lift {lift:0.00} m, "
            + $"crouching after standing {c.data.isCrouching}";
        if (!crouched || c.data.isCrouching)
            Fail(note);
        else
            stepNote = note;
    }

    /// <summary>vr.41: with <c>Locomotion.ArmSwingSprint</c> on and the stick forward, alternating swings set
    /// <c>CharacterData.isSprinting</c> (CharacterMovement.cs:263); still hands with the same stick do not.</summary>
    IEnumerator ArmSwingStep()
    {
        var c = Character.localCharacter;
        var setting = BodyGestures.ArmSwingSetting;
        if (setting == null)
        {
            stepSkip = "player.locomotion not installed";
            yield break;
        }
        bool was = setting.Value;
        setting.Value = true;
        bool moveLeft = !VRInput.LeftHanded;
        HeadFrame(out var head, out var forward, out var right);
        var restLeft = Rest(head, forward, -right).Position;
        var restRight = Rest(head, forward, right).Position;
        Script(moveLeft).Stick = new Vector2(0f, 1f);
        yield return Wait(0.8f);
        bool sprintStill = c.data.isSprinting;
        bool sprintSwing = false;
        float start = Now;
        while (Now - start < 2f)
        {
            float z = 0.25f * Mathf.Sin(2f * Mathf.PI * 1.6f * (Now - start));
            SetHand(true, restLeft + forward * z);
            SetHand(false, restRight - forward * z);
            sprintSwing |= c.data.isSprinting;
            yield return null;
        }
        Script(moveLeft).Stick = Vector2.zero;
        SetHand(true, restLeft);
        SetHand(false, restRight);
        setting.Value = was;
        yield return Wait(0.5f);
        var note = $"sprinting with still hands {sprintStill}, while swinging {sprintSwing}";
        if (sprintStill || !sprintSwing)
            Fail(note);
        else
            stepNote = note;
    }
}
