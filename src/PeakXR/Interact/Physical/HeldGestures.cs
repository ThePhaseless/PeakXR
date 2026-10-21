using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Interact.Physical;

/// <summary>
/// Held-item gestures that end in the item's own primary use (<c>CharacterItems.DoUsing</c>, CharacterItems.cs:197-215):
/// <list type="bullet">
/// <item>Bugle (G5): the holding hand at the mouth holds primary, so <c>BugleSFX.UpdateTooting</c> starts the toot
/// (BugleSFX.cs:43-61). Its pitch argument comes from the bell direction via <see cref="BeforeTooting"/>.</item>
/// <item>Body press (G8): a healing item held against your own torso or other forearm holds primary, extending
/// eat-at-mouth (interact.physical) to non-food status items. Feeding a friend's body is in <c>SocialHands</c>.</item>
/// </list>
/// The holding hand's tracked grip is used, not the physics item: held items collide with the holder's own body.
/// </summary>
internal static class HeldGestures
{
    private static readonly MouthGesture Mouth = new()
    {
        Radius = 0.16f,
        ExitRadius = 0.22f,
        DwellTime = 0.1f,
    };
    private static readonly ContactDwell Body = new()
    {
        Enter = 0f,
        Exit = 0.05f,
        Dwell = 0.35f,
    };

    /// <summary>Torso radius around the spine, with the hand's own thickness (tune on headset).</summary>
    private const float TorsoRadius = 0.22f;

    /// <summary>Forearm press distance, metres (audit G8: 15 cm).</summary>
    private const float ForearmRadius = 0.12f;

    private static bool pressing;
    private static bool tooting;

    public static void Reset()
    {
        Mouth.Reset();
        Body.Reset();
        pressing = false;
        tooting = false;
    }

    public static void Tick(Character c, VRRig rig, ItemGesturesConfig cfg, float dt)
    {
        var hand = ItemGestures.HoldingHand(rig);
        if (ItemGestures.ItemChanged)
        {
            Mouth.Reset();
            // An item drawn from a chest slot starts at the chest: it must leave once before a press counts.
            Body.Disarm();
            pressing = tooting = false;
        }
        if (hand == null || rig.Head == null || !VRInput.HeadTracked)
        {
            Mouth.Reset();
            Body.Reset();
            return;
        }
        UpdateBugle(rig, cfg, hand, dt);
        UpdateBody(rig, cfg, hand, dt);
    }

    private static void UpdateBugle(VRRig rig, ItemGesturesConfig cfg, Transform hand, float dt)
    {
        if (!cfg.BugleAtMouth || ItemGestures.Bugle == null)
        {
            Mouth.Reset();
            tooting = false;
            return;
        }
        bool at = Mouth.Update(rig.Head.position.N(), rig.Head.rotation.N(), hand.position.N(), dt);
        if (at && !tooting)
            Haptics.Pulse(ItemGestures.HoldLeft, 0.3f, 0.05f);
        tooting = at;
        if (at)
            GestureRequests.Primary();
    }

    private static void UpdateBody(VRRig rig, ItemGesturesConfig cfg, Transform hand, float dt)
    {
        if (!cfg.PressToBody || !ItemGestures.StatusItem)
        {
            Body.Reset();
            pressing = false;
            return;
        }
        var head = rig.Head;
        var flat = head.forward;
        flat.y = 0f;
        flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
        var point = hand.position.N();
        float distance = BodyZones.TorsoDistance(point, head.position.N(), flat.N(), TorsoRadius);

        // The other forearm, unless both hands are on the item.
        bool onForearm = false;
        var other = rig.Hand(!ItemGestures.HoldLeft);
        if (!LocalHold.TwoHand && other != null && VRInput.Hand(!ItemGestures.HoldLeft).Tracked)
        {
            float arm =
                BodyZones.ForearmDistance(point, other.position.N(), other.forward.N())
                - ForearmRadius;
            if (arm < distance)
            {
                distance = Mathf.Max(arm, 0f);
                onForearm = true;
            }
        }

        bool on = Body.Update(distance, dt);
        if (on && !pressing)
        {
            // A short wrap: the holding hand, and the arm being wrapped.
            Haptics.Pulse(ItemGestures.HoldLeft, 0.35f, 0.12f);
            if (onForearm)
                Haptics.Pulse(!ItemGestures.HoldLeft, 0.35f, 0.12f);
        }
        pressing = on;
        if (on)
            GestureRequests.Primary();
    }

    private static bool lookSwapped;
    private static Vector3 savedLook;

    /// <summary>
    /// <c>BugleSFX.UpdateTooting</c> prefix body (BugleSFX.cs:43): the game reads the pitch from the holder's
    /// <c>lookDirection</c> against up (BugleSFX.cs:59-60) and sends it as the <c>RPC_StartToot</c> pitch argument.
    /// For the local VR holder that direction becomes the bell's (the holding hand's aim) for this one call;
    /// <see cref="AfterTooting"/> restores it from the finalizer.
    /// </summary>
    public static void BeforeTooting(BugleSFX bugle)
    {
        lookSwapped = false;
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        if (
            !VRSession.Active
            || !ItemGesturesConfig.Instance.BugleAtMouth
            || c == null
            || rig == null
            || !ReferenceEquals(bugle, ItemGestures.Bugle)
            || c.data.currentItem == null
            || !VRInput.Hand(ItemGestures.HoldLeft).Tracked
        )
            return;
        var aim = rig.Aim(ItemGestures.HoldLeft);
        if (aim == null)
            return;
        savedLook = c.data.lookDirection;
        c.data.lookDirection = aim.forward;
        lookSwapped = true;
    }

    public static void AfterTooting()
    {
        var c = Character.localCharacter;
        if (lookSwapped && c != null)
            c.data.lookDirection = savedLook;
        lookSwapped = false;
    }
}
