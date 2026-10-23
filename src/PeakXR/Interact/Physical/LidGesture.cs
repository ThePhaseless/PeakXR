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
/// A chest that revives someone (RespawnChest.cs:51-55) also gives the Revived notice, or NoRevive when the ascent allows no revives.</para>
/// </summary>
internal static class LidGesture
{
    /// <summary>Hand rise (metres) that counts as a fully raised lid; tune so the lift takes about timeToOpen.</summary>
    public const float LiftHeight = 0.25f;

    private static readonly Luggage[] Grabbed = new Luggage[2];
    private static readonly bool[] OnLid = new bool[2];
    private static readonly float[] StartY = new float[2];
    private static readonly TwoHandGate ChestGate = new();

    /// <summary>Per hand: a low grip held long enough for the "grab the lid" hint.</summary>
    private static readonly ContactDwell[] Low =
    {
        new()
        {
            Enter = 0f,
            Exit = 0.5f,
            Dwell = 0.5f,
        },
        new()
        {
            Enter = 0f,
            Exit = 0.5f,
            Dwell = 0.5f,
        },
    };

    /// <summary>Height of the lid edge over the case's render centre (Luggage.Center, Luggage.cs:148); the hint
    /// ring sits there above the low hand. Tune against the prefabs in game.</summary>
    private const float LidEdgeUp = 0.1f;

    /// <summary>Open time (<c>timeToOpen</c>, Luggage.cs:214) from which the low-grip hint is worth showing.</summary>
    internal const float LowGripMinOpen = 1.5f;

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
        LowGrip(rig, 0);
        LowGrip(rig, 1);
        // One-handed play (Accessibility.OneHanded) has no second hand: one hand on the lid lifts a respawn chest.
        bool one = VRInput.OneHanded;
        bool both = one
            ? OnLid[0] || OnLid[1]
            : OnLid[0] && OnLid[1] && ReferenceEquals(Grabbed[0], Grabbed[1]);
        // A respawn chest takes both hands on its lid; a hand anywhere else on it counts as alone too (a low grip
        // holds it at 0 in BeforeInteraction), so it gets the same refusal instead of a silent hold.
        var gripped = Grabbed[0] as RespawnChest ?? Grabbed[1] as RespawnChest;
        if (gripped != null)
        {
            if (ChestGate.Update(both ? 2 : 1, Time.deltaTime))
                RefuseOneHand(rig, gripped, ReferenceEquals(Grabbed[0], gripped));
        }
        else
            ChestGate.Update(0, 0f);
        var lug =
            OnLid[0] ? Grabbed[0]
            : OnLid[1] ? Grabbed[1]
            : null;
        if (lug == null)
            return;
        if (lug is RespawnChest && !one)
            fraction = both ? Mathf.Min(left, right) : 0f;
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

    /// <summary>
    /// A grip low on a closed case keeps the plain hold, but lifting the lid is the faster, physical way (WS-W8): held
    /// for 0.5 s, it gets a teal "higher" ring (ArrowUp) on the lid edge above the hand, once as a notice and then
    /// while the grip stays. A "would work" hint, so only with <c>UI.ActionHints</c>.
    /// </summary>
    private static void LowGrip(VRRig rig, int h)
    {
        var lug = Grabbed[h];
        // A quick case (LuggageSmall opens in 0.5 s, vr33) is open before the hint could help.
        bool low =
            lug != null
            && !OnLid[h]
            && Affordance.Hints
            && Character.localCharacter != null
            && lug.GetInteractTime(Character.localCharacter) >= LowGripMinOpen;
        bool was = Low[h].Active;
        if (!Low[h].Update(low ? 0f : 1f, Time.deltaTime))
            return;
        bool left = h == 0;
        var hand = rig.Hand(left);
        if (hand == null)
            return;
        var lid = hand.position;
        lid.y = lug.Center().y + LidEdgeUp;
        if (!was)
            Affordance.Inform(left, hand.position, DenyReason.AimHigher, DenyHint.At(lid));
        Affordance.Mark(left, hand.position, DenyReason.AimHigher, DenyHint.At(lid));
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
    /// it; a chest that revived someone also gives the Revived or NoRevive notice there.</summary>
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
        // RespawnChest.SpawnItems (RespawnChest.cs:51-55) revives only when the ascent allows it (Ascents.cs:207);
        // otherwise it gives items, and the notice says why nobody came back.
        if (openingRevives)
            Affordance.Inform(
                ReferenceEquals(Grabbed[0], opening) || OnLid[0],
                opening.Center(),
                Ascents.canReviveDead ? DenyReason.Revived : DenyReason.NoRevive,
                default
            );
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
    /// <summary>The chest whose untracked hold was refused, so it is refused once per hold.</summary>
    private static Luggage refusedHold;

    /// <summary>The hand whose grip presses the game's interact as the fallback, else the dominant hand.</summary>
    private static bool InteractingHand()
    {
        if (PhysicalInteraction.HandAction(true) == GripPressAction.Interact)
            return true;
        if (PhysicalInteraction.HandAction(false) == GripPressAction.Interact)
            return false;
        return VRInput.LeftHanded;
    }

    public static void BeforeInteraction(Interaction interaction)
    {
        var held = interaction.currentHeldInteractible as Luggage;
        var cfg = ItemGesturesConfig.Instance;
        if (held == null || !VRSession.Active || cfg == null || !cfg.LiftLid)
        {
            refusedHold = null;
            return;
        }
        // Decided here, before the game adds this frame's time (LidCap). A respawn chest whose lid the hands are
        // not both lifting right now holds at 0 however its hold started, so a frame where the hand tracking lags
        // the grip can no longer let one hand run it (run 49: one hand ran it to 0.09).
        bool tracked = ReferenceEquals(held, target) && Time.frameCount - frame <= 1;
        bool twoHands = held is RespawnChest;
        // With grabbing by hand off nothing can lift the lid: the plain vanilla hold opens the chest.
        bool physicalGrab = InteractConfig.Instance.PhysicalGrab;
        if (!LidCap.TryFraction(tracked, fraction, twoHands, physicalGrab, out float cap))
            return;
        // A hold no hand is on (pointed, head-aimed, out of reach) is held at 0 too; the one-hand gate only sees
        // hands on the chest, so it is refused here, once per hold, instead of silently doing nothing.
        bool handOnCase = ReferenceEquals(Grabbed[0], held) || ReferenceEquals(Grabbed[1], held);
        if (!LidCap.RefusesUntracked(tracked, twoHands, physicalGrab, handOnCase))
            refusedHold = null;
        else if (!ReferenceEquals(refusedHold, held))
        {
            refusedHold = held;
            Affordance.Deny(
                WorldHolds.HandOn(held, InteractingHand()),
                held.Center(),
                DenyReason.NeedsTwoHands,
                default,
                held
            );
        }
        interaction.currentInteractableHeldTime = LidLift.CapHeldTime(
            interaction.currentInteractableHeldTime,
            interaction.currentConstantInteractableTime,
            cap,
            Time.deltaTime
        );
    }
}
