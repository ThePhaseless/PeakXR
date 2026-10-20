using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Voice;

/// <summary>
/// Per-frame driver for VR voice: the optional hand-to-mouth push-to-talk gesture, the one-time headset
/// mic choice and the speaking icons. Runs late, after the rig is posed, and does nothing outside VR,
/// so flat play keeps the vanilla voice chat untouched.
/// </summary>
[DefaultExecutionOrder(31000)]
internal sealed class VoiceRuntime : MonoBehaviour
{
    private readonly MouthGesture leftMouth = new();
    private readonly MouthGesture rightMouth = new();
    private readonly HeadsetMic headsetMic = new();
    private SpeakingIndicators indicators;

    public static void Create()
    {
        var go = new GameObject("PeakXR_Voice");
        DontDestroyOnLoad(go);
        go.AddComponent<VoiceRuntime>();
    }

    private void Awake() => indicators = new SpeakingIndicators(transform);

    private void LateUpdate()
    {
        var cfg = VoiceFeature.Settings;
        var rig = VRRig.Instance;
        if (!VRSession.Active || rig == null)
        {
            ClearGesture();
            indicators.HideAll();
            return;
        }

        var dt = Time.unscaledDeltaTime;
        TickGesture(cfg.HandToMouthTalk.Value, rig, dt);
        headsetMic.Tick(cfg.PreferHeadsetMic.Value);
        indicators.Tick(cfg, rig, dt);
    }

    private void TickGesture(bool enabled, VRRig rig, float dt)
    {
        var local = Character.localCharacter;
        if (
            !enabled
            || local == null
            || local.data.dead
            || rig.Head == null
            || rig.LeftHand == null
            || rig.RightHand == null
        )
        {
            ClearGesture();
            return;
        }

        // Only an empty hand talks: a held item at the mouth means eating (interact.physical).
        var holding = local.data.currentItem != null;
        var leftFree = !holding || (!LocalHold.TwoHand && !LocalHold.ItemInLeftHand);
        var rightFree = !holding || (!LocalHold.TwoHand && LocalHold.ItemInLeftHand);
        var headPos = rig.Head.position.N();
        var headRot = rig.Head.rotation.N();
        var left = Track(leftMouth, leftFree, headPos, headRot, rig.LeftHand.position, dt);
        var right = Track(rightMouth, rightFree, headPos, headRot, rig.RightHand.position, dt);
        VoiceInput.GestureHeld = left || right;
    }

    private static bool Track(
        MouthGesture gesture,
        bool free,
        System.Numerics.Vector3 headPos,
        System.Numerics.Quaternion headRot,
        Vector3 hand,
        float dt
    )
    {
        if (free)
            return gesture.Update(headPos, headRot, hand.N(), dt);
        gesture.Reset();
        return false;
    }

    private void ClearGesture()
    {
        leftMouth.Reset();
        rightMouth.Reset();
        VoiceInput.GestureHeld = false;
    }
}
