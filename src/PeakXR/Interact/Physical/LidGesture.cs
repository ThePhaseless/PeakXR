using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.UI.Affordances;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Interact.Physical;

/// <summary>
/// Lid lift (G4): a grip on the top half of a closed suitcase (<c>Luggage</c>, Luggage.cs:10) grabs the lid edge.
/// The grip already holds the game's interact on it (interact.physical's grab); this caps the game's own hold timer
/// by how far the hand has risen since the grab (<see cref="LidLift"/>), through the <c>Interaction.DoInteraction</c>
/// prefix. The open itself stays vanilla: <c>Interact_CastFinished</c> sends <c>OpenLuggageRPC</c>
/// (Luggage.cs:243-247) and the lid animates on completion. Respawn chests (RespawnChest.cs:6) need both hands on
/// the lid. A grip low on the case keeps the plain vanilla hold.
/// <para>Feel (WS-W8, W10): a contact tick as the hand takes the lid edge, the progress ramp while the game's timer
/// runs, a firm tick when it opens. One hand alone on a respawn chest for 0.6 s is refused (NeedsTwoHands, the chest
/// red, a teal ring at the lid's opposite corner), or HandsFull with the held item red when the other hand is busy.
/// A chest that revives someone (RespawnChest.cs:51-55) also sounds the notice note.</para>
/// </summary>
internal static class LidGesture
{
    /// <summary>Hand rise (metres) that counts as a fully raised lid; tune so the lift takes about timeToOpen.</summary>
    public const float LiftHeight = 0.25f;

    private static readonly Luggage[] Grabbed = new Luggage[2];
    private static readonly bool[] OnLid = new bool[2];
    private static readonly float[] StartY = new float[2];
    private static readonly TwoHandGate ChestGate = new();

    private static Luggage target;
    private static float fraction;
    private static int frame = -10;

    /// <summary>The case the game's timer runs on, and whether someone was down when it started (a chest revives).</summary>
    private static Luggage opening;
    private static bool openingRevives;

    public static void Reset()
    {
        for (int h = 0; h < 2; h++)
        {
            Grabbed[h] = null;
            OnLid[h] = false;
        }
        target = null;
        opening = null;
        ChestGate.Update(0, 0f);
    }

    public static void Tick(ItemGesturesConfig cfg)
    {
        Opened();
        target = null;
        var rig = VRRig.Instance;
        if (!cfg.LiftLid || rig == null)
        {
            Reset();
            return;
        }
        float left = Track(rig, 0);
        float right = Track(rig, 1);
        var lug =
            OnLid[0] ? Grabbed[0]
            : OnLid[1] ? Grabbed[1]
            : null;
        if (lug == null)
        {
            ChestGate.Update(0, 0f);
            return;
        }
        bool both = OnLid[0] && OnLid[1] && ReferenceEquals(Grabbed[0], Grabbed[1]);
        if (lug is RespawnChest chest)
        {
            fraction = both ? Mathf.Min(left, right) : 0f;
            if (ChestGate.Update(both ? 2 : 1, Time.deltaTime))
                RefuseOneHand(rig, chest, OnLid[0]);
        }
        else
            fraction = Mathf.Max(
                OnLid[0] ? left : 0f,
                OnLid[1] && ReferenceEquals(Grabbed[1], lug) ? right : 0f
            );
        target = lug;
        frame = Time.frameCount;
        Feel(lug);
    }

    /// <summary>One hand's lift fraction of its grabbed lid, 0 when it holds none.</summary>
    private static float Track(VRRig rig, int h)
    {
        bool left = h == 0;
        var lug =
            PhysicalInteraction.HandAction(left) == GripPressAction.GrabInteractible
                ? PhysicalInteraction.HandTarget(left) as Luggage
                : null;
        if (lug != null && lug.IsOpen)
            lug = null;
        ref var state = ref VRInput.Hand(left);
        if (!ReferenceEquals(lug, Grabbed[h]))
        {
            Grabbed[h] = lug;
            var t = rig.Hand(left);
            OnLid[h] = lug != null && t != null && t.position.y >= lug.Center().y;
            // Tracking-space height, so walking or the body settling does not count as lifting.
            StartY[h] = state.Position.y;
            if (OnLid[h])
                Affordance.Contact(left);
        }
        return OnLid[h] ? LidLift.Fraction(state.Position.y - StartY[h], LiftHeight) : 0f;
    }

    /// <summary>The progress ramp on each lifting hand while the game's timer runs on this case.</summary>
    private static void Feel(Luggage lug)
    {
        var interaction = Interaction.instance;
        if (interaction == null || !ReferenceEquals(interaction.currentHeldInteractible, lug))
            return;
        if (!ReferenceEquals(opening, lug))
        {
            opening = lug;
            openingRevives = lug is RespawnChest && Character.PlayerIsDeadOrDown();
        }
        float progress = Mathf.Clamp01(interaction.constantInteractableProgress);
        for (int h = 0; h < 2; h++)
        {
            if (OnLid[h] && ReferenceEquals(Grabbed[h], lug))
                Affordance.Progress(h == 0, progress);
        }
    }

    /// <summary>The case being lifted opened (Luggage.IsOpen, Luggage.cs:37): a firm tick on the hands that lifted
    /// it; a chest that revived someone also sounds the notice note there.</summary>
    private static void Opened()
    {
        if (opening == null)
            return;
        if (!opening.IsOpen)
        {
            // Let go before it opened: nothing to report.
            if (!ReferenceEquals(Grabbed[0], opening) && !ReferenceEquals(Grabbed[1], opening))
                opening = null;
            return;
        }
        bool chest = opening is RespawnChest;
        for (int h = 0; h < 2; h++)
        {
            if (chest || OnLid[h])
                Affordance.Contact(h == 0);
        }
        if (openingRevives)
            Affordance.Play(CueSound.Notice, opening.Center());
        opening = null;
    }

    /// <summary>One hand alone on the chest lid (W10): the other hand busy with an item (HandsFull, that item red) or
    /// free (NeedsTwoHands, the chest red, the would-work ring at the lid's opposite corner).</summary>
    private static void RefuseOneHand(VRRig rig, RespawnChest chest, bool left)
    {
        var item = ItemGestures.Held;
        if (item != null && (LocalHold.TwoHand || ItemGestures.HoldLeft != left))
        {
            Affordance.DenyAtHand(left, DenyReason.HandsFull, default, item);
            return;
        }
        var hand = rig.Hand(left);
        if (hand == null)
            return;
        var center = chest.Center();
        var across = center - hand.position;
        across.y = 0f;
        var corner = center + across;
        corner.y = hand.position.y;
        Affordance.Deny(left, hand.position, DenyReason.NeedsTwoHands, DenyHint.At(corner), chest);
    }

    /// <summary>Interaction.DoInteraction prefix body: before the game adds this frame's time to the held luggage,
    /// cap it at the lid's lift.</summary>
    public static void BeforeInteraction(Interaction interaction)
    {
        if (frame != Time.frameCount || target == null)
            return;
        if (!ReferenceEquals(interaction.currentHeldInteractible, target))
            return;
        interaction.currentInteractableHeldTime = LidLift.CapHeldTime(
            interaction.currentInteractableHeldTime,
            interaction.currentConstantInteractableTime,
            fraction,
            Time.deltaTime
        );
    }
}
