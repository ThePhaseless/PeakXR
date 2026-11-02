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
/// (Luggage.cs:243-247) and the lid animates on completion. A grip low on the case keeps the plain vanilla hold. The
/// revival statue (<c>RespawnChest</c>, RespawnChest.cs:6) is a Luggage with no lid: it is touched instead
/// (<see cref="RespawnStatue"/>).
/// <para>Feel (WS-W8): a contact tick as the hand takes the lid edge, the progress ramp while the game's timer
/// runs, a firm tick when it opens.</para>
/// </summary>
internal static class LidGesture
{
    /// <summary>Hand rise (metres) that counts as a fully raised lid; tune so the lift takes about timeToOpen.</summary>
    public const float LiftHeight = 0.25f;

    private static readonly Luggage[] Grabbed = new Luggage[2];
    private static readonly bool[] OnLid = new bool[2];
    private static readonly float[] StartY = new float[2];

    /// <summary>The grabbed case's lid (<see cref="LidFace"/>) along its own up, read once per grab.</summary>
    private static readonly float[] Face = new float[2];
    private static readonly Vector3[] CaseUp = new Vector3[2];

    /// <summary>How far under the lid's lower face a grip still takes the lid.</summary>
    internal const float LidGive = 0.05f;

    /// <summary>The last grab on a case: where the hand was and whether it took the lid, for the self-test.</summary>
    internal static string LastGrab { get; private set; } = "none";

    /// <summary>This hand's grip on a case is on its lid half, not low (the self-test's setup check).</summary>
    internal static bool GripOnLid(bool left) =>
        Grabbed[left ? 0 : 1] != null && OnLid[left ? 0 : 1];

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

    /// <summary>Height of the hint ring over the lid's lower face, along the case's up, above the low hand: on the
    /// lid's edge. Tune against the prefabs in game.</summary>
    internal const float LidEdgeUp = 0.1f;

    /// <summary>Open time (<c>timeToOpen</c>, Luggage.cs:214) from which the low-grip hint is worth showing.</summary>
    internal const float LowGripMinOpen = 1.5f;

    private static Luggage target;
    private static float fraction;
    private static int frame = -10;

    /// <summary>The case the game's timer runs on.</summary>
    private static Luggage opening;

    public static void Reset()
    {
        for (int h = 0; h < 2; h++)
        {
            Grabbed[h] = null;
            OnLid[h] = false;
        }
        target = null;
        opening = null;
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
        var lug =
            OnLid[0] ? Grabbed[0]
            : OnLid[1] ? Grabbed[1]
            : null;
        if (lug == null)
            return;
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
        if (lug != null && (lug.IsOpen || lug is RespawnChest))
            lug = null;
        ref var state = ref VRInput.Hand(left);
        if (!ReferenceEquals(lug, Grabbed[h]))
        {
            Grabbed[h] = lug;
            var t = rig.Hand(left);
            // The solid lid, along the case's own up, with a little give (#127): a case tilted on a rock has its lid's
            // low end under its centre and its base's high end over it, so world height misreads both. On a small
            // case a grip counted as low keeps the plain hold, which opens it unlifted (run 65, vr.82).
            OnLid[h] = false;
            if (lug != null && t != null)
            {
                Face[h] = LidFace(lug, out CaseUp[h]);
                OnLid[h] = LidLift.OnLid(t.position.N(), Face[h], CaseUp[h].N(), LidGive);
                float above = Vector3.Dot(t.position, CaseUp[h]) - Face[h];
                LastGrab = System.FormattableString.Invariant(
                    $"{lug.name}: {(left ? "L" : "R")} grip {above:+0.00;-0.00} m from the lid's lower face, {(OnLid[h] ? "on the lid" : "low (plain hold)")}"
                );
            }
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
        var up = CaseUp[h];
        var lid = hand.position + up * (Face[h] + LidEdgeUp - Vector3.Dot(hand.position, up));
        if (!was)
            Affordance.Inform(left, hand.position, DenyReason.AimHigher, DenyHint.At(lid));
        Affordance.Mark(left, hand.position, DenyReason.AimHigher, DenyHint.At(lid));
    }

    private static readonly System.Collections.Generic.List<BoxCollider> Boxes = new();

    /// <summary>
    /// The lower face of <paramref name="lug"/>'s closed lid, in metres along its <paramref name="up"/>. Every lidded
    /// prefab has a solid box on or under its lid bone, named LidCollider or (LuggageCursed) Lid; LidColliderOpen is
    /// inactive while closed. A case without one falls back on its render centre (Luggage.Center, Luggage.cs:148),
    /// which sits within a few cm of the seam on most upright prefabs.
    /// </summary>
    internal static float LidFace(Luggage lug, out Vector3 up)
    {
        var caseUp = lug.transform.up;
        up = caseUp;
        lug.GetComponentsInChildren(false, Boxes);
        float face = Vector3.Dot(lug.Center(), caseUp);
        foreach (var box in Boxes)
        {
            if (
                !box.enabled
                || box.isTrigger
                || !box.name.StartsWith("Lid", System.StringComparison.Ordinal)
            )
                continue;
            var t = box.transform;
            var half = box.size * 0.5f;
            face = LidLift.LowerFace(
                t.TransformPoint(box.center).N(),
                t.TransformVector(half.x, 0f, 0f).N(),
                t.TransformVector(0f, half.y, 0f).N(),
                t.TransformVector(0f, 0f, half.z).N(),
                caseUp.N()
            );
            break;
        }
        Boxes.Clear();
        return face;
    }

    /// <summary><paramref name="at"/> would read as a low grip on <paramref name="lug"/>, by at least
    /// <paramref name="margin"/> metres (the self-test's aim).</summary>
    internal static bool ReadsLow(Luggage lug, Vector3 at, float margin)
    {
        float face = LidFace(lug, out var up);
        return !LidLift.OnLid(at.N(), face, up.N(), LidGive + margin);
    }

    /// <summary>The progress ramp on each lifting hand while the game's timer runs on this case.</summary>
    private static void Feel(Luggage lug)
    {
        var interaction = Interaction.instance;
        if (interaction == null || !ReferenceEquals(interaction.currentHeldInteractible, lug))
            return;
        opening = lug;
        float progress = Mathf.Clamp01(interaction.constantInteractableProgress);
        for (int h = 0; h < 2; h++)
        {
            if (OnLid[h] && ReferenceEquals(Grabbed[h], lug))
                Affordance.Progress(h == 0, progress);
        }
    }

    /// <summary>The case being lifted opened (Luggage.IsOpen, Luggage.cs:37): a firm tick on the hands that lifted
    /// it.</summary>
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
        for (int h = 0; h < 2; h++)
        {
            if (OnLid[h])
                Affordance.Contact(h == 0);
        }
        opening = null;
    }

    /// <summary>Interaction.DoInteraction prefix body: before the game adds this frame's time to the held luggage,
    /// cap it at the lid's lift. A case no hand is lifting keeps the plain vanilla hold.</summary>
    public static void BeforeInteraction(Interaction interaction)
    {
        var held = interaction.currentHeldInteractible as Luggage;
        var cfg = ItemGesturesConfig.Instance;
        if (held == null || !VRSession.Active || cfg == null || !cfg.LiftLid)
            return;
        if (!ReferenceEquals(held, target) || Time.frameCount - frame > 1)
            return;
        interaction.currentInteractableHeldTime = LidLift.CapHeldTime(
            interaction.currentInteractableHeldTime,
            interaction.currentConstantInteractableTime,
            fraction,
            Time.deltaTime
        );
    }
}
