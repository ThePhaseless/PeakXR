using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.UI.Affordances;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Interact.Physical;

/// <summary>
/// Revival statue touch (issue #4): the statue before a biome's last campfire is a <c>RespawnChest</c>
/// (RespawnChest.cs:6), a <c>Luggage</c> with no lid. A hand laid on it, palm toward it with the grip open or closed,
/// holds the game's own interact on it (the vanilla cast, timeToOpen; nothing new on the network) while it stays
/// there; taking the hand off cancels, and a grip that took the statue holds the cast at 0 once the hand is off it
/// (<see cref="StatueTouch.HoldsAtZero"/>). The touching hand gets the hold ring and the progress ramp, a contact tick
/// on touch and at the end. A spent statue is refused at the hand (UsedUp, statue red); someone down in a run without
/// revives gets the NoRevive notice as the cast starts, since it then only gives items. A statue that revived someone
/// gives the Revived notice. With nobody down the cast gives items, as in the flat game (RespawnChest.cs:56-60).
/// </summary>
internal static class RespawnStatue
{
    /// <summary>How often the nearest statue is looked up, and how near the body its centre must be.</summary>
    private const float LookSeconds = 1f;
    private const float Near = 5f;

    /// <summary>A grip that took the statue stays on it this far past the grab reach.</summary>
    private const float GripGive = 0.05f;

    private static readonly ContactDwell Touch = new()
    {
        Enter = 0f,
        Exit = 0.5f,
        Dwell = 0.15f,
    };

    private static RespawnChest statue;
    private static Collider[] colliders = System.Array.Empty<Collider>();
    private static float nextLook;
    private static int touchFrame = -10;
    private static bool touchLeft;
    private static bool casting;
    private static bool revives;

    /// <summary>The statue a hand is on this frame, for the self-test (vr.275).</summary>
    internal static RespawnChest Touched => Touch.Active ? statue : null;

    public static void Reset()
    {
        Touch.Reset();
        statue = null;
        colliders = System.Array.Empty<Collider>();
        casting = false;
    }

    public static void Tick(Character c, VRRig rig, ItemGesturesConfig cfg)
    {
        if (!cfg.TouchStatue)
        {
            Reset();
            return;
        }
        Look(c);
        Finished();
        int hand =
            statue == null ? -1
            : On(c, rig, true) ? 0
            : On(c, rig, false) ? 1
            : -1;
        bool was = Touch.Active;
        if (!Touch.Update(hand >= 0 ? 0f : 1f, Time.deltaTime))
            return;
        if (hand >= 0)
            touchLeft = hand == 0;
        touchFrame = Time.frameCount;
        bool down = Character.PlayerIsDeadOrDown();
        var reason = StatueTouch.Reason(statue.IsSpent, down, Ascents.canReviveDead);
        if (!was)
        {
            if (reason == DenyReason.UsedUp)
                Affordance.DenyAtHand(touchLeft, reason, default, statue);
            else
            {
                Affordance.Contact(touchLeft);
                if (reason == DenyReason.NoRevive)
                    Affordance.Inform(touchLeft, statue.Center(), reason, default);
            }
        }
        if (reason == DenyReason.UsedUp)
            return;
        GestureRequests.Interact(statue, touchLeft);
        var interaction = Interaction.instance;
        if (interaction != null && ReferenceEquals(interaction.currentHeldInteractible, statue))
        {
            casting = true;
            revives = down && Ascents.canReviveDead;
            Affordance.Progress(touchLeft, Mathf.Clamp01(interaction.constantInteractableProgress));
        }
    }

    /// <summary>The nearest statue within <see cref="Near"/> of the body, looked up once a second (a scene search,
    /// never per frame); its colliders are read once per statue.</summary>
    private static void Look(Character c)
    {
        float now = Time.unscaledTime;
        if (now < nextLook)
            return;
        nextLook = now + LookSeconds;
        RespawnChest best = null;
        float bestSq = Near * Near;
        foreach (var s in Object.FindObjectsByType<RespawnChest>(FindObjectsSortMode.None))
        {
            float sq = (s.Center() - c.Center).sqrMagnitude;
            if (sq < bestSq)
            {
                best = s;
                bestSq = sq;
            }
        }
        if (ReferenceEquals(best, statue))
            return;
        statue = best;
        colliders =
            best != null
                ? best.GetComponentsInChildren<Collider>()
                : System.Array.Empty<Collider>();
        Touch.Reset();
    }

    /// <summary>The cast ran out on the statue (it is spent now): a contact tick, and Revived when it brought
    /// someone back (RespawnChest.cs:51-55).</summary>
    private static void Finished()
    {
        if (!casting || statue == null)
        {
            casting = false;
            return;
        }
        var interaction = Interaction.instance;
        if (statue.IsSpent)
        {
            casting = false;
            Affordance.Contact(touchLeft);
            if (revives)
                Affordance.Inform(touchLeft, statue.Center(), DenyReason.Revived, default);
        }
        else if (
            interaction == null
            || !ReferenceEquals(interaction.currentHeldInteractible, statue)
        )
            casting = false;
    }

    /// <summary>The <paramref name="left"/> hand on the statue: a grip that took it, near its surface; or a hand
    /// without an item or other grip action, palm toward it (<see cref="StatueTouch.HandOn"/>).</summary>
    private static bool On(Character c, VRRig rig, bool left)
    {
        var t = rig.Hand(left);
        if (t == null || !VRInput.Hand(left).Tracked)
            return false;
        var pos = t.position;
        var surface = Surface(pos);
        var action = PhysicalInteraction.HandAction(left);
        if (
            action == GripPressAction.GrabInteractible
            && ReferenceEquals(PhysicalInteraction.HandTarget(left), statue)
        )
            return (surface - pos).sqrMagnitude
                <= Sq(InteractConfig.Instance.GrabRadius + GripGive);
        bool free =
            action == GripPressAction.None
            && (
                c.data.currentItem == null
                || (!LocalHold.TwoHand && LocalHold.ItemInLeftHand != left)
            );
        // Palm normal: +X of the left grip pose, -X of the right (HandRig.cs:62).
        var palm = left ? t.right : -t.right;
        return StatueTouch.HandOn(palm.N(), pos.N(), surface.N(), free);
    }

    private static float Sq(float x) => x * x;

    /// <summary>The nearest point of the statue's solid colliders to <paramref name="p"/>; far away when none.</summary>
    private static Vector3 Surface(Vector3 p)
    {
        var best = p + Vector3.up * 100f;
        float bestSq = float.MaxValue;
        foreach (var col in colliders)
        {
            if (col == null || !col.enabled || col.isTrigger)
                continue;
            var q = PhysicalInteraction.SurfacePoint(col, p);
            float sq = (q - p).sqrMagnitude;
            if (sq < bestSq)
            {
                best = q;
                bestSq = sq;
            }
        }
        return best;
    }

    /// <summary>Interaction.DoInteraction prefix body: a grip that took the statue holds its cast at 0 while the
    /// hand is off it, so pulling away cancels. A pointed or head-aimed hold keeps the vanilla hold.</summary>
    public static void BeforeInteraction(Interaction interaction)
    {
        var cfg = ItemGesturesConfig.Instance;
        if (
            interaction.currentHeldInteractible is not RespawnChest held
            || !VRSession.Active
            || cfg == null
            || !cfg.TouchStatue
        )
            return;
        bool touching = ReferenceEquals(held, statue) && Time.frameCount - touchFrame <= 1;
        bool grip = GripOn(held, true) || GripOn(held, false);
        if (StatueTouch.HoldsAtZero(touching, grip))
            interaction.currentInteractableHeldTime = 0f;
    }

    private static bool GripOn(RespawnChest held, bool left) =>
        PhysicalInteraction.HandAction(left) == GripPressAction.GrabInteractible
        && ReferenceEquals(PhysicalInteraction.HandTarget(left), held);
}
