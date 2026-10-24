using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Interact;
using PeakXR.UI.Affordances;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Social;

/// <summary>
/// Two-body interactions of the local VR player, driven by hand proximity instead of head aim. A grip on a
/// friend, a thorn, a tick or a worn pack selects that target for the game's own interaction (the
/// <c>Interaction.DoInteractableRaycasts</c> result) and holds the game's interact or reach input, so carrying,
/// dropping, eating, thorn removal, pack opening and helping up all run through vanilla code and RPCs; vanilla
/// peers see the normal result. Feeding holds the game's use-on-friend while a feedable item stays at a friend's
/// mouth. In kick mode a two-palm shove taps the game's reach (its kick). A grip the game would ignore is refused
/// with a cue (<see cref="SocialIntent"/>). Grips arrive through <see cref="IGripExtension"/> from
/// <c>interact.physical</c>.
/// </summary>
internal sealed class SocialHands : IGripExtension
{
    public static readonly SocialHands Instance = new();

    private const float FeedRadius = 0.22f;
    private const float FeedExitRadius = 0.3f;
    private const float GraspPulseInterval = 0.3f;

    /// <summary>How far (m) the hand pulls away from a gripped thorn before the game's removal hold starts.</summary>
    private const float PluckPull = 0.08f;

    /// <summary>How long (s) a shove holds the reach button, so the game's FixedUpdate sees it (Character.cs:1200).</summary>
    private const float ShoveTap = 0.1f;

    /// <summary>How near (m) an open hand must be to a helpable friend's hand for the "grab here" ring.</summary>
    private const float GlowRadius = 0.45f;

    /// <summary>Carried sway: one light tick per <see cref="SwayPeriod"/> s, alternating hands, while moving.</summary>
    private const float SwayPeriod = 0.6f;
    private const float SwayMinSpeed = 0.5f;
    private const float SwayStrength = 0.15f;

    /// <summary>Progress-ramp strength of the pal-jump pressure (a friend underfoot or on your back).</summary>
    private const float PalPressure = 0.3f;

    private readonly SocialKind[] kinds = new SocialKind[2];
    private Vector3 lastCenter;
    private float swayAt;
    private bool swayLeft;
    private bool palBoosted;
    private readonly IInteractible[] targets = new IInteractible[2];
    private readonly Character[] characters = new Character[2];
    private readonly ButtonEdge interactEdge = new();
    private readonly ButtonEdge secondaryEdge = new();
    private readonly StruggleGesture struggle = new();
    private readonly ShoveGesture shove = new();

    private Character feedCandidate;
    private bool wasSticky;
    private float lastGraspPulse = -10f;
    private readonly float[] carryAlone = new float[2];
    private readonly float[] pluckFrom = new float[2];
    private readonly bool[] plucking = new bool[2];
    private readonly bool[] grasped = new bool[2];
    private readonly bool[] gripDown = new bool[2];
    private float shoveHold;
    private bool vanillaInteract;
    private bool vanillaInteractPressed;

    /// <summary>Interactible chosen by a hand this frame, handed to the game's interaction raycast.</summary>
    public IInteractible RaycastTarget { get; private set; }

    /// <summary>The friend a held item is being fed to; relaxes the face-each-other check.</summary>
    public CharacterInteractible FeedTarget { get; private set; }

    /// <summary>The item has been held at <see cref="FeedTarget"/> for the dwell time: the player means to feed.</summary>
    public bool FeedReady { get; private set; }

    /// <summary>Lifts the local VR player started (RPCA_StartCarry run for them), for the MP test: a lift and drop
    /// within one frame never shows in <c>carriedPlayer</c>.</summary>
    public static int CarryStarts;

    /// <summary>The hand that last gripped a passed-out friend: where the dropped-pack notice goes.</summary>
    public bool CarryLeft { get; private set; }

    /// <summary>This hand's grip is on a passed-out friend to lift them (<see cref="GripToggle"/> latches it).</summary>
    public bool CarryGrip(bool left) => kinds[left ? 0 : 1] == SocialKind.Carry;

    private static bool Ready(Character c) =>
        VRSession.Active && SocialConfig.Instance.HandInteract && c == Character.localCharacter;

    // ------------------------------------------------------------------ grip hooks

    public bool ClaimPress(in GripHookContext ctx) =>
        Ready(ctx.Character)
        && !ctx.ThisHandHolds
        && Find(ctx.Character, ctx.HandPosition, out _, out _, out _, out _, out _);

    public void BeginPress(in GripHookContext ctx)
    {
        int h = ctx.Left ? 0 : 1;
        if (
            !Find(
                ctx.Character,
                ctx.HandPosition,
                out var kind,
                out var target,
                out var who,
                out var refusal,
                out var at
            )
        )
            return;
        grasped[h] = false;
        lookCued[h] = false;
        lookOff[h] = 0f;
        if (refusal != DenyReason.None)
        {
            // The grip stays owned (nothing falls through to a grab or the head's interact) and says why. A held
            // item turns red for HandsFull; a friend's body never does (D10).
            Affordance.Deny(
                ctx.Left,
                at,
                refusal,
                default,
                refusal == DenyReason.HandsFull ? ctx.HeldItem : null
            );
            return;
        }
        kinds[h] = kind;
        targets[h] = target;
        characters[h] = who;
        if (target is Item or Bugfix)
            PhysicalInteraction.ExpectItemIn(ctx.Left);
        if (kind == SocialKind.Carry)
            CarryLeft = ctx.Left;
        plucking[h] = target is ThornOnMe && SocialConfig.Instance.PullToPluck;
        if (plucking[h])
            pluckFrom[h] = Vector3.Distance(HandAt(ctx.Left, ctx.HandPosition), at);
        Affordance.Contact(ctx.Left);
    }

    public bool EndPress(in GripHookContext ctx)
    {
        int h = ctx.Left ? 0 : 1;
        // A reach the game never caught on (no GUIManager.Grasp) was called off.
        if (kinds[h] == SocialKind.Reach && !grasped[h])
            Affordance.Cancel(ctx.Left);
        Clear(h);
        return true;
    }

    public bool TryHeldRelease(in GripHookContext ctx) => false;

    private void Clear(int h)
    {
        kinds[h] = SocialKind.None;
        targets[h] = null;
        characters[h] = null;
        plucking[h] = false;
    }

    /// <summary>
    /// What a hand at <paramref name="pos"/> (world) can act on, nearest small targets first: thorns and arrows on
    /// anyone (CharacterAfflictions.physicalThorns, CharacterAfflictions.cs:162), ticks (Bugfix.AllAttachedBugs,
    /// Bugfix.cs:31), items stuck to a body (StickyItemComponent.ALL_STUCK_ITEMS, StickyItemComponent.cs:8), then
    /// another character's worn pack (from behind) or body (<see cref="SocialIntent.Body"/>). True claims the grip;
    /// with <paramref name="refusal"/> set it only says why nothing happens, at <paramref name="at"/>.
    /// </summary>
    private static bool Find(
        Character c,
        Vector3 pos,
        out SocialKind kind,
        out IInteractible target,
        out Character who,
        out DenyReason refusal,
        out Vector3 at
    )
    {
        kind = SocialKind.None;
        target = null;
        who = null;
        refusal = DenyReason.None;
        at = pos;
        float reach = SocialConfig.Instance.Reach;
        float small = reach * 0.5f;
        float bestSq = small * small;
        var all = Character.AllCharacters;

        for (int i = 0; i < all.Count; i++)
        {
            var ch = all[i];
            if (ch == null || !ch.gameObject.activeInHierarchy || ch.refs.afflictions == null)
                continue;
            var thorns = ch.refs.afflictions.physicalThorns;
            if (thorns == null)
                continue;
            for (int t = 0; t < thorns.Count; t++)
            {
                var thorn = thorns[t];
                if (thorn == null || !thorn.stuckIn)
                    continue;
                float d = (thorn.transform.position - pos).sqrMagnitude;
                if (d < bestSq)
                {
                    bestSq = d;
                    target = thorn;
                    who = ch;
                }
            }
        }
        foreach (var bug in Bugfix.AllAttachedBugs)
        {
            if (bug.Key == null || bug.Value == null)
                continue;
            float d = (bug.Key.transform.position - pos).sqrMagnitude;
            if (d < bestSq)
            {
                bestSq = d;
                target = bug.Key;
                who = bug.Value;
            }
        }
        var stuck = StickyItemComponent.ALL_STUCK_ITEMS;
        for (int i = 0; i < stuck.Count; i++)
        {
            var s = stuck[i];
            if (
                s == null
                || s.item == null
                || s.stuckToCharacter == null
                || !s.item.IsInteractible(c)
            )
                continue;
            float d = (s.item.Center() - pos).sqrMagnitude;
            if (d < bestSq)
            {
                bestSq = d;
                target = s.item;
                who = s.stuckToCharacter;
            }
        }
        if (target != null)
        {
            at = target.Center();
            // ThornOnMe.IsInteractible (ThornOnMe.cs:123) refuses while stuck; its aim-angle part follows the hand
            // (SocialPatches.HandPick).
            if (target is ThornOnMe && c.IsStuck())
            {
                refusal = DenyReason.Stuck;
                target = null;
                return true;
            }
            kind = SocialKind.Interact;
            return true;
        }

        // A worn pack sits behind the back, often farther than the reach from any body part (MP run 7: a grip on the
        // host's pack fell through to the head's interact), so packs are found on their own, before bodies.
        var pack = NearestPack(c, pos, reach * reach * 1.6f, out var wearer);
        if (pack != null)
        {
            who = wearer;
            // A worn rocket already lit or flying: the hold would end in a light the game ignores
            // (CharacterBackpackHandler.RPCA_Light, :209-212).
            var movement = wearer.refs.movement;
            if (
                pack.backpackType == BackpackSlot.BackpackType.Rocketpack
                && movement != null
                && (movement.rocketLit || movement.rocketActive)
            )
            {
                refusal = DenyReason.UsedUp;
                at = pack.transform.position;
                return true;
            }
            kind = SocialKind.Interact;
            target = pack;
            return true;
        }

        var other = NearestBody(c, pos, reach, out var part);
        if (other == null)
            return Skeleton(c, pos, small, ref refusal, ref at);
        who = other;
        at = part.transform.position;

        var od = other.data;
        var cd = c.data;
        bool climbing = cd.isClimbing || cd.isRopeClimbing || cd.isVineClimbing;
        bool handOrArm =
            part.partType
            is BodypartType.Hand_L
                or BodypartType.Hand_R
                or BodypartType.Arm_L
                or BodypartType.Arm_R
                or BodypartType.Elbow_L
                or BodypartType.Elbow_R;
        var facts = new SocialFacts
        {
            KickMode = KickMode,
            HoldingItem = cd.currentItem != null,
            Climbing = climbing,
            // Grip edges only, so one sphere check is fine: a grip by rock is meant for the wall.
            HandOnWall =
                (climbing || handOrArm)
                && Physics.CheckSphere(
                    pos,
                    InteractConfig.Instance.ClimbRadius,
                    HelperFunctions.terrainMapMask,
                    QueryTriggerInteraction.Ignore
                ),
            CarryingOther = cd.carriedPlayer != null && cd.carriedPlayer != other,
            IsBot = other.isBot || other.refs.interactible == null,
            OnHandOrArm = handOrArm,
            CarriedByMe = od.carrier == c,
            Cannibalizable = other.refs.customization.isCannibalizable,
            PassedOut = od.fullyPassedOut && !od.dead,
            CarriedByOther = od.carrier != null && od.carrier != c,
            // CharacterGrabbing.TargetCanBeHelped (CharacterGrabbing.cs:293).
            Helpable = CharacterGrabbing.TargetCanBeHelped(other, c),
        };
        kind = SocialIntent.Body(facts, out refusal);
        if (kind is SocialKind.Interact or SocialKind.Carry)
            target = other.refs.interactible;
        return kind != SocialKind.None || refusal != DenyReason.None;
    }

    private static readonly Collider[] Touching = new Collider[16];

    /// <summary>
    /// A dead friend's body is gone (inactive); what is left is their skeleton (Character.SpawnSkeletonOnDeath,
    /// Character.cs:763). A bare grip on it does nothing in the game: only a held Book of Bones with uses revives
    /// (Skelleton.IsInteractible, Skelleton.cs:58-63; interact.items handles the book). Refused as CantUse, ring only.
    /// </summary>
    private static bool Skeleton(
        Character c,
        Vector3 pos,
        float radius,
        ref DenyReason refusal,
        ref Vector3 at
    )
    {
        int n = Physics.OverlapSphereNonAlloc(
            pos,
            radius,
            Touching,
            ~0,
            QueryTriggerInteraction.Ignore
        );
        for (int i = 0; i < n; i++)
        {
            var skeleton = Touching[i].GetComponentInParent<Skelleton>();
            if (skeleton == null || skeleton.IsInteractible(c))
                continue;
            refusal = DenyReason.CantUse;
            at = skeleton.Center();
            return true;
        }
        return false;
    }

    /// <summary>Run setting HelpingHand = 1 (CharacterGrabbing.isKickMode, CharacterGrabbing.cs:33).</summary>
    private static bool KickMode => RunSettings.GetValue(RunSettings.SETTINGTYPE.HelpingHand) == 1;

    /// <summary>
    /// A pack shows on their back: one in the backpack slot, not in hand (CharacterBackpackHandler.LateUpdate,
    /// :68-72). <c>activeBackpackVisuals</c> (:37-57) answers the plain backpack's visuals even with the slot empty,
    /// and that component stays enabled (only its view is hidden), so it alone says nothing (MP run 13: a grip behind
    /// a host with no pack held an invisible pack, and a carry grip at their back went to it instead of the lift).
    /// </summary>
    internal static bool WearsPack(Character ch)
    {
        var items = ch.refs.items;
        return ch.player != null
            && ch.player.backpackSlot.backpackType != BackpackSlot.BackpackType.None
            && !(
                items != null
                && items.currentSelectedSlot.IsSome
                && items.currentSelectedSlot.Value == 3
            );
    }

    /// <summary>The nearest pack another character wears (CharacterBackpackHandler.activeBackpackVisuals) within
    /// <paramref name="maxSq"/> of a hand behind that character (a hand in front reaches their chest, not the pack).</summary>
    private static BackpackOnBackVisuals NearestPack(
        Character self,
        Vector3 pos,
        float maxSq,
        out Character wearer
    )
    {
        BackpackOnBackVisuals best = null;
        wearer = null;
        var all = Character.AllCharacters;
        for (int i = 0; i < all.Count; i++)
        {
            var ch = all[i];
            if (
                ch == null
                || ch == self
                || !ch.gameObject.activeInHierarchy
                || ch.refs.backpackHandler == null
                || !WearsPack(ch)
            )
                continue;
            var pack = ch.refs.backpackHandler.activeBackpackVisuals;
            if (pack == null || !pack.isActiveAndEnabled)
                continue;
            float sq = (pack.transform.position - pos).sqrMagnitude;
            if (sq < maxSq && Vector3.Dot(pos - ch.Center, ch.data.lookDirection_Flat) < 0f)
            {
                maxSq = sq;
                best = pack;
                wearer = ch;
            }
        }
        return best;
    }

    /// <summary>Another player character with a bodypart within <paramref name="radius"/> of a world point.</summary>
    private static Character NearestBody(
        Character self,
        Vector3 pos,
        float radius,
        out Bodypart bestPart
    )
    {
        Character best = null;
        bestPart = null;
        float bestSq = radius * radius;
        var all = Character.AllCharacters;
        for (int i = 0; i < all.Count; i++)
        {
            var ch = all[i];
            if (
                ch == null
                || ch == self
                || !ch.gameObject.activeInHierarchy
                || ch.refs.ragdoll == null
                || (ch.Center - pos).sqrMagnitude > 4f
            )
                continue;
            var parts = ch.refs.ragdoll.partList;
            for (int p = 0; p < parts.Count; p++)
            {
                var part = parts[p];
                if (part == null)
                    continue;
                float d = (part.transform.position - pos).sqrMagnitude;
                if (d < bestSq)
                {
                    bestSq = d;
                    best = ch;
                    bestPart = part;
                }
            }
        }
        return best;
    }

    // ------------------------------------------------------------------ per frame

    /// <summary>First <c>CharacterInput.Sample</c> postfix: remembers the game's own interact before any hand adds to it.</summary>
    public void CaptureVanilla(CharacterInput input)
    {
        vanillaInteract = input.interactIsPressed;
        vanillaInteractPressed = input.interactWasPressed;
    }

    /// <summary>Per-frame injection, after <c>interact.physical</c>'s grip edges.</summary>
    public void Tick(Character c, CharacterInput input, float dt)
    {
        RaycastTarget = null;
        FeedTarget = null;
        FeedReady = false;
        if (!VRSession.Active || c != Character.localCharacter)
        {
            Clear(0);
            Clear(1);
            return;
        }
        var cfg = SocialConfig.Instance;
        Validate(c, 0);
        Validate(c, 1);
        UpdatePluck(0);
        UpdatePluck(1);

        int activeHand = 0;
        IInteractible active = ActiveInteract(cfg, 0);
        if (active == null)
        {
            activeHand = 1;
            active = ActiveInteract(cfg, 1);
        }
        NudgeCarry(cfg, 0, dt);
        NudgeCarry(cfg, 1, dt);
        WarnPack(c, 0);
        WarnPack(c, 1);
        bool reaching =
            c.data.currentItem == null
            && (kinds[0] == SocialKind.Reach || kinds[1] == SocialKind.Reach);

        bool feeding = false;
        if (active == null && cfg.FeedAtMouth)
            feeding = UpdateFeed(c, dt);
        else
            ResetFeed();

        RaycastTarget = active ?? FeedTarget;
        FeedReady = feeding;

        interactEdge.Update(active != null, dt);
        input.interactWasPressed |= interactEdge.Pressed;
        input.interactIsPressed |= interactEdge.Held;
        input.interactWasReleased |= interactEdge.Released;
        if (active != null)
            HoldProgress(activeHand == 0, active);
        else if (feeding && c.data.currentItem != null && c.data.currentItem.isUsingSecondary)
            Affordance.Progress(LocalHold.ItemInLeftHand, c.data.currentItem.progress);

        UpdateShove(c, cfg, dt);
        secondaryEdge.Update(reaching || feeding || shoveHold > 0f, dt);
        input.useSecondaryWasPressed |= secondaryEdge.Pressed;
        input.useSecondaryIsPressed |= secondaryEdge.Held;
        input.useSecondaryWasReleased |= secondaryEdge.Released;

        UpdateStruggle(c, input, cfg, dt);
        UpdateStickyCue(c);
        UpdateReachLook(c, 0, dt);
        UpdateReachLook(c, 1, dt);
        UpdateGlow(c, cfg);
        UpdateCarried(c, cfg, dt);
        UpdatePalJump(c, cfg);
    }

    /// <summary>Seconds a reaching hand may hold a friend with the look outside the game's cone before the cue: the
    /// head is usually still turning as the grip lands.</summary>
    private const float ReachLookDwell = 0.4f;

    private readonly bool[] lookCued = new bool[2];
    private readonly float[] lookOff = new float[2];

    /// <summary>
    /// A hand holds a friend for the reach, but the look is past the game's cone (<see cref="SocialIntent.ReachSees"/>,
    /// CharacterGrabbing.cs:252-253): every client tests the synced look, so nobody is pulled up. Instead of a silent
    /// grip: WrongAngle at the hand once per grip, with a would-work ring on the friend to look at. Nothing once the
    /// look comes round or the game catches them (GUIManager.Grasp). Local only.
    /// </summary>
    private void UpdateReachLook(Character c, int h, float dt)
    {
        var who = characters[h];
        if (kinds[h] != SocialKind.Reach || grasped[h] || lookCued[h] || who == null)
            return;
        if (SocialIntent.ReachSees(c.data.lookDirection.N(), (who.Center - c.Center).N()))
        {
            lookOff[h] = 0f;
            return;
        }
        lookOff[h] += dt;
        if (lookOff[h] < ReachLookDwell)
            return;
        lookCued[h] = true;
        var rig = VRRig.Instance;
        var hand = rig != null ? rig.Hand(h == 0) : null;
        Affordance.Deny(
            h == 0,
            hand != null ? hand.position : who.Center,
            DenyReason.WrongAngle,
            DenyHint.At(who.Center)
        );
    }

    /// <summary>
    /// "Grab here": an open, empty hand within <see cref="GlowRadius"/> of the hand of a friend the game would let you
    /// pull up (CharacterGrabbing.TargetCanBeHelped, :293) gets a teal ring on that friend's hand. A hint, so only with
    /// <c>UI.ActionHints</c>; never in kick mode, where a grip does not reach.
    /// </summary>
    private void UpdateGlow(Character c, SocialConfig cfg)
    {
        var rig = VRRig.Instance;
        var cd = c.data;
        if (
            !Affordance.Hints
            || !cfg.HandInteract
            || rig == null
            || cd.currentItem != null
            || cd.isClimbing
            || cd.isRopeClimbing
            || cd.isVineClimbing
        )
            return;
        var all = Character.AllCharacters;
        for (int h = 0; h < 2; h++)
        {
            var hand = rig.Hand(h == 0);
            if (kinds[h] != SocialKind.None || hand == null)
                continue;
            float bestSq = GlowRadius * GlowRadius;
            Vector3 best = default;
            bool found = false;
            for (int i = 0; i < all.Count; i++)
            {
                var ch = all[i];
                if (
                    ch == null
                    || ch == c
                    || ch.refs.ragdoll == null
                    || !ch.gameObject.activeInHierarchy
                    || (ch.Center - hand.position).sqrMagnitude > 4f
                    || !CharacterGrabbing.TargetCanBeHelped(ch, c)
                )
                    continue;
                found |= Closer(ch, BodypartType.Hand_L, hand.position, ref bestSq, ref best);
                found |= Closer(ch, BodypartType.Hand_R, hand.position, ref bestSq, ref best);
            }
            if (found && !KickMode)
                Affordance.Mark(h == 0, best, DenyReason.NeedsFriend, DenyHint.At(best));
        }
    }

    private static bool Closer(
        Character ch,
        BodypartType type,
        Vector3 from,
        ref float bestSq,
        ref Vector3 best
    )
    {
        if (!ch.refs.ragdoll.partDict.TryGetValue(type, out var part))
            return false;
        var p = part.transform.position;
        float sq = (p - from).sqrMagnitude;
        if (sq >= bestSq)
            return false;
        bestSq = sq;
        best = p;
        return true;
    }

    /// <summary>
    /// Carried by a friend (CharacterData.carrier, CharacterCarrying.cs:76) or a condor (carrierCondor,
    /// Condor.cs:300): a slow, light sway felt in the hands, left then right, while the carrier moves. Never camera
    /// motion: the view stays the game's spectate takeover.
    /// </summary>
    private void UpdateCarried(Character c, SocialConfig cfg, float dt)
    {
        var d = c.data;
        bool carried = d.carrier != null || d.carrierCondor != null;
        var center = c.Center;
        float speed = dt > 0f ? (center - lastCenter).magnitude / dt : 0f;
        lastCenter = center;
        if (!cfg.CarriedSway || !carried || speed < SwayMinSpeed)
        {
            swayAt = 0f;
            return;
        }
        swayAt += dt;
        if (swayAt < SwayPeriod)
            return;
        swayAt = 0f;
        swayLeft = !swayLeft;
        Affordance.Progress(swayLeft, SwayStrength);
    }

    /// <summary>
    /// Pal jump (CharacterMovement.StandOnPlayer :992-1022, CheckForPalJump :1024-1034): standing on a crouched friend,
    /// or a friend standing on you, is a light pressure ramp in both hands; the boost itself (JumpRpc with
    /// isPalJump, :626-635, sincePalJump reset) is a thump in both hands and a rising note.
    /// </summary>
    private void UpdatePalJump(Character c, SocialConfig cfg)
    {
        if (!cfg.PalJumpFeel)
            return;
        var d = c.data;
        bool boosted = d.sincePalJump < 0.2f;
        if (boosted && !palBoosted)
        {
            Affordance.Contact(true);
            Affordance.Contact(false);
            Affordance.Play(CueSound.WouldWork, c.Center);
        }
        palBoosted = boosted;
        bool pressed = d.lastStoodOnPlayer != null && d.sinceStandOnPlayer < 0.3f;
        var all = Character.AllCharacters;
        for (int i = 0; !pressed && i < all.Count; i++)
        {
            var ch = all[i];
            pressed =
                ch != null
                && ch != c
                && ch.data.lastStoodOnPlayer == c
                && ch.data.sinceStandOnPlayer < 0.3f;
        }
        if (!pressed)
            return;
        Affordance.Progress(true, PalPressure);
        Affordance.Progress(false, PalPressure);
    }

    /// <summary>The game's hold on a hand-chosen constant interactible (Interaction.cs:119-138) fills: ramp on that hand.</summary>
    private static void HoldProgress(bool left, IInteractible active)
    {
        var interaction = Interaction.instance;
        if (
            interaction != null
            && interaction.currentHeldInteractible != null
            && ReferenceEquals(interaction.currentHeldInteractible, active)
        )
            Affordance.Progress(left, interaction.constantInteractableProgress);
    }

    /// <summary>A gripped thorn comes out as a pull: the removal hold starts once the hand has drawn back
    /// <see cref="PluckPull"/> from where it gripped.</summary>
    private void UpdatePluck(int h)
    {
        if (!plucking[h] || targets[h] is not ThornOnMe thorn)
            return;
        var rig = VRRig.Instance;
        if (
            rig != null
            && Vector3.Distance(HandAt(h == 0, thorn.Center()), thorn.Center())
                >= pluckFrom[h] + PluckPull
        )
            plucking[h] = false;
    }

    /// <summary>The rig hand's world position (the same point at grip and while pulling), or the fallback.</summary>
    private static Vector3 HandAt(bool left, Vector3 fallback)
    {
        var rig = VRRig.Instance;
        var hand = rig != null ? rig.Hand(left) : null;
        return hand != null ? hand.position : fallback;
    }

    /// <summary>Last <c>CharacterInput.Sample</c> postfix: while the item is at a friend's mouth or chest, its trigger
    /// is the hand-over's squeeze, so it must not also start the item's own use (eating it yourself,
    /// usePrimary).</summary>
    public void GuardHandOver(CharacterInput input)
    {
        if (!VRSession.Active || !handOver.SuppressPrimary)
            return;
        input.usePrimaryWasPressed = false;
        input.usePrimaryIsPressed = false;
    }

    /// <summary>Last <c>CharacterInput.Sample</c> postfix: while passed out a grip must not count as holding interact,
    /// which speeds up dying (Character.cs:1174). Both grips and both triggers give up on purpose; a single grip says
    /// so, and holding all four ramps on both hands as the death timer fills.</summary>
    public void GuardGiveUp(Character c, CharacterInput input)
    {
        if (!VRSession.Active || !SocialConfig.Instance.GiveUpGuard || !c.data.fullyPassedOut)
        {
            gripDown[0] = gripDown[1] = false;
            return;
        }
        bool deliberate =
            VRInput.Left.Grip > 0.6f
            && VRInput.Right.Grip > 0.6f
            && VRInput.Left.Trigger > 0.6f
            && VRInput.Right.Trigger > 0.6f;
        input.interactIsPressed = vanillaInteract || deliberate;
        input.interactWasPressed =
            vanillaInteractPressed || (deliberate && input.interactWasPressed);
        for (int h = 0; h < 2; h++)
        {
            bool down = VRInput.Hand(h == 0).Grip > 0.6f;
            if (deliberate)
                Affordance.Progress(h == 0, Mathf.Clamp01(c.data.deathTimer));
            else if (down && !gripDown[h])
                Affordance.DenyAtHand(h == 0, DenyReason.NeedsTwoHands);
            gripDown[h] = down;
        }
    }

    /// <summary>
    /// One hand on a passed-out friend does nothing with <c>CarryBothHands</c> (<see cref="ActiveInteract"/>): after a
    /// moment, show once where the other hand goes, on their body at the free hand's side.
    /// </summary>
    private void NudgeCarry(SocialConfig cfg, int h, float dt)
    {
        var who = characters[h];
        bool alone =
            kinds[h] == SocialKind.Carry
            && cfg.CarryBothHands
            && who != null
            && !(kinds[1 - h] == SocialKind.Carry && characters[1 - h] == who);
        carryAlone[h] = alone ? carryAlone[h] + dt : 0f;
        var rig = VRRig.Instance;
        if (carryAlone[h] < 0.6f || rig == null || rig.Head == null)
            return;
        var side = rig.Head.right * (h == 0 ? 0.22f : -0.22f);
        Affordance.DenyAtHand(h == 0, DenyReason.NeedsTwoHands, DenyHint.At(who.Center + side));
        // Once per grip: latched until this hand stops being the only one on them.
        carryAlone[h] = float.NegativeInfinity;
    }

    /// <summary>Lifting a friend drops the worn pack (CharacterCarrying.cs:59-67): while a carry grip is on, a red
    /// ring on the pack says so before the lift.</summary>
    private void WarnPack(Character c, int h)
    {
        if (kinds[h] != SocialKind.Carry || c.player == null || c.player.backpackSlot.IsEmpty())
            return;
        var visuals =
            c.refs.backpackHandler != null ? c.refs.backpackHandler.activeBackpackVisuals : null;
        var at = visuals != null ? visuals.transform.position : c.Center;
        Affordance.Hover(h == 0, at, DenyReason.PackWouldDrop);
    }

    private IInteractible ActiveInteract(SocialConfig cfg, int h)
    {
        switch (kinds[h])
        {
            case SocialKind.Interact:
                return plucking[h] ? null : targets[h];
            case SocialKind.Carry:
                if (!cfg.CarryBothHands)
                    return targets[h];
                return kinds[1 - h] == SocialKind.Carry && characters[1 - h] == characters[h]
                    ? targets[h]
                    : null;
            default:
                return null;
        }
    }

    private void Validate(Character c, int h)
    {
        if (kinds[h] == SocialKind.None)
            return;
        var who = characters[h];
        bool gone = who == null || !who.gameObject.activeInHierarchy;
        if (targets[h] is Object unityTarget && unityTarget == null)
            gone = true;
        // The thorn came out: a pop on the hand that pulled it.
        if (targets[h] is ThornOnMe thorn && !thorn.stuckIn)
        {
            Affordance.Contact(h == 0);
            gone = true;
        }
        if (kinds[h] == SocialKind.Reach && !gone && !CharacterGrabbing.TargetCanBeHelped(who, c))
            gone = true;
        if (gone)
            Clear(h);
    }

    /// <summary>
    /// Kick mode (D6): both open palms pushed at a friend within the game's kick reach tap the reach button, which
    /// the game turns into its kick (CharacterGrabbing.cs:104, KickCast :190). What the game would refuse is cued
    /// on the dominant hand instead.
    /// </summary>
    private void UpdateShove(Character c, SocialConfig cfg, float dt)
    {
        shoveHold = Mathf.Max(0f, shoveHold - dt);
        var rig = VRRig.Instance;
        if (!cfg.ShoveKick || rig == null || rig.Origin == null)
        {
            shove.Reset();
            return;
        }
        var forward = VRInput.HeadRotation * Vector3.forward;
        forward.y = 0f;
        // One-handed play (Accessibility.OneHanded): the one open palm pushed is the shove, fed in as both hands.
        bool one = VRInput.OneHanded;
        ref var l = ref VRInput.Hand(one ? VRInput.LeftHanded : true);
        ref var r = ref VRInput.Hand(one ? VRInput.LeftHanded : false);
        bool open = l.Grip < 0.3f && r.Grip < 0.3f;
        if (
            !l.Tracked
            || !r.Tracked
            || !shove.Update(
                l.Velocity.N(),
                r.Velocity.N(),
                open,
                forward.normalized.N(),
                out var dirTracking
            )
            || !KickMode
        )
            return;
        var grabbing = c.refs.grabbing;
        var friend = ShoveTarget(c, rig.Origin.TransformDirection(dirTracking.U()), grabbing);
        if (friend == null)
            return;
        bool left = VRInput.LeftHanded;
        var d = c.data;
        var why = SocialIntent.Shove(
            d.isClimbingAnything,
            d.isGrounded,
            !c.infiniteStam && d.currentStamina < 0.005f,
            d.isKicking
        );
        if (why == DenyReason.NoStamina)
            Affordance.DenyStamina(left, friend.Center);
        else if (why != DenyReason.None)
            Affordance.Deny(left, friend.Center, why);
        // The game kicks along the head's look (CharacterGrabbing.cs:212-218): a friend outside that cone is missed.
        else if (Vector3.Angle(d.lookDirection_Flat, friend.Center - c.Center) > grabbing.kickAngle)
            Affordance.Deny(left, friend.Center, DenyReason.WrongAngle);
        else
            shoveHold = ShoveTap;
    }

    /// <summary>The nearest other character within the game's kick distance roughly where the palms went.</summary>
    private static Character ShoveTarget(Character c, Vector3 direction, CharacterGrabbing grabbing)
    {
        Character best = null;
        float bestSq = grabbing.kickDistance * grabbing.kickDistance;
        var all = Character.AllCharacters;
        for (int i = 0; i < all.Count; i++)
        {
            var ch = all[i];
            if (ch == null || ch == c || !ch.gameObject.activeInHierarchy)
                continue;
            var to = ch.Center - c.Center;
            float sq = to.sqrMagnitude;
            if (sq < bestSq && Vector3.Angle(direction, to) <= 60f)
            {
                bestSq = sq;
                best = ch;
            }
        }
        return best;
    }

    /// <summary>
    /// Feed or heal as a hand-over (<see cref="HandOver"/>): a canUseOnFriend item brought to another player's head
    /// (a healing item: their torso), then the holding hand's trigger squeezed, holds the game's use-on-friend while
    /// the trigger stays down. Proximity alone never uses it (external review row 9: standing close to a friend
    /// spent a bandage). A teal ring marks the mouth or chest while in range; any other item there is refused on the
    /// squeeze, not on proximity (CharacterInteractible.HasItemCanUseOnFriend, :90).
    /// </summary>
    private bool UpdateFeed(Character c, float dt)
    {
        var item = c.data.currentItem;
        if (item == null)
        {
            ResetFeed();
            return false;
        }
        var point = item.Center();
        // Healing items go on the body (interact.items PressToBody), food at the mouth.
        var part =
            Interact.Physical.ItemGestures.StatusItem
            && Interact.Physical.ItemGesturesConfig.Instance.PressToBody
                ? BodypartType.Torso
                : BodypartType.Head;
        float grow = part == BodypartType.Torso ? 0.08f : 0f;
        // The friend being fed stays the target through a short wobble out of the zone (HandOver.Grace).
        var friend = feedCandidate;
        if (
            friend != null
            && (
                !friend.gameObject.activeInHierarchy
                || !NearPart(friend, part, point, FeedExitRadius + grow)
            )
        )
            feedCandidate = null;
        if (feedCandidate == null)
            feedCandidate = NearestPart(c, part, point, FeedRadius + grow);
        bool inZone = feedCandidate != null;
        var who = inZone ? feedCandidate : friend;
        if (who == null || !who.gameObject.activeInHierarchy)
        {
            handOver.Reset();
            atFriendMouth = false;
            return false;
        }
        bool left = LocalHold.ItemInLeftHand;
        bool usable = item.canUseOnFriend && who.refs.interactible != null;
        atFriendMouth = inZone && !usable;
        var state = handOver.Update(
            inZone,
            VRInput.Hand(left).Trigger > HandOverTrigger,
            usable,
            item.CanUsePrimary(),
            dt
        );
        if (!inZone && state == HandOverState.Apply)
            feedCandidate = who;
        if (usable && (inZone || state == HandOverState.Apply))
            FeedTarget = who.refs.interactible;
        var spot = PartPosition(who, part, point);
        switch (state)
        {
            case HandOverState.Hint:
                Affordance.Mark(left, spot, DenyReason.NeedsFriend, DenyHint.At(spot));
                return false;
            case HandOverState.Refuse:
                Affordance.Deny(left, point, DenyReason.CantUse, default, item);
                return false;
            case HandOverState.Apply:
                return FeedTarget != null;
            default:
                return false;
        }
    }

    /// <summary>Trigger value that counts as the hand-over squeeze.</summary>
    private const float HandOverTrigger = 0.6f;

    private readonly HandOver handOver = new();

    /// <summary>The hand-over owns the holding hand's trigger this frame (<see cref="HandOver.SuppressPrimary"/>).</summary>
    public bool HandOverOwnsTrigger => handOver.SuppressPrimary;

    private static Vector3 PartPosition(Character ch, BodypartType part, Vector3 fallback) =>
        ch.refs.ragdoll != null && ch.refs.ragdoll.partDict.TryGetValue(part, out var body)
            ? body.transform.position
            : fallback;

    /// <summary>A held item the friend cannot take sits at their mouth or chest this frame: any refusal of its own
    /// use at the holding hand is said as CantUse (<see cref="HandOver.ContextReason"/>, via
    /// <see cref="Affordance.ReasonContext"/>).</summary>
    private bool atFriendMouth;

    /// <summary>Installed as <see cref="Affordance.ReasonContext"/> by <see cref="SocialFeature"/>.</summary>
    internal static DenyReason RefusalContext(bool left, DenyReason reason) =>
        left == LocalHold.ItemInLeftHand && Instance.atFriendMouth
            ? HandOver.ContextReason(reason, true)
            : reason;

    private void ResetFeed()
    {
        atFriendMouth = false;
        feedCandidate = null;
        handOver.Reset();
    }

    private static bool NearPart(Character ch, BodypartType part, Vector3 point, float radius)
    {
        if (ch.refs.ragdoll == null || !ch.refs.ragdoll.partDict.TryGetValue(part, out var body))
            return false;
        return (body.transform.position - point).sqrMagnitude <= radius * radius;
    }

    private static Character NearestPart(
        Character self,
        BodypartType part,
        Vector3 point,
        float radius
    )
    {
        Character best = null;
        float bestSq = radius * radius;
        var all = Character.AllCharacters;
        for (int i = 0; i < all.Count; i++)
        {
            var ch = all[i];
            if (
                ch == null
                || ch == self
                || ch.isBot
                || ch.data.dead
                || !ch.gameObject.activeInHierarchy
                || ch.refs.ragdoll == null
                || !ch.refs.ragdoll.partDict.TryGetValue(part, out var body)
            )
                continue;
            float d = (body.transform.position - point).sqrMagnitude;
            if (d < bestSq)
            {
                bestSq = d;
                best = ch;
            }
        }
        return best;
    }

    /// <summary>Webs and fly traps (CharacterMovement.cs:657): each shake stroke is one jump press, felt as a tick on
    /// both hands that gets lighter as the web or trap lets go.</summary>
    private void UpdateStruggle(Character c, CharacterInput input, SocialConfig cfg, float dt)
    {
        var a = c.refs.afflictions;
        if (!cfg.Struggle || a == null || !a.isStruggling)
        {
            struggle.Reset();
            return;
        }
        int strokes = struggle.Update(
            VRInput.Left.Velocity.N(),
            VRInput.Left.Tracked,
            VRInput.Right.Velocity.N(),
            VRInput.Right.Tracked,
            dt
        );
        if (strokes <= 0)
            return;
        input.jumpWasPressed = true;
        float hold = Mathf.Clamp01(
            a.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Web)
                + a.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.FlyTrap)
        );
        Affordance.Progress(true, hold);
        Affordance.Progress(false, hold);
    }

    /// <summary>A sticky item glued to the hand (CharacterData.cs:99) cannot be put away: say so once.</summary>
    private void UpdateStickyCue(Character c)
    {
        bool sticky = c.data.currentStickyItem != null;
        if (sticky && !wasSticky)
            Haptics.Pulse(LocalHold.ItemInLeftHand, 0.7f, 0.2f);
        wasSticky = sticky;
    }

    /// <summary>GUIManager.Grasp: the local reach caught a friend, or a ledge's helping hand caught us.</summary>
    public void OnGrasp()
    {
        for (int h = 0; h < 2; h++)
        {
            if (kinds[h] == SocialKind.Reach)
                grasped[h] = true;
        }
        if (!VRSession.Active || !SocialConfig.Instance.GraspHaptics)
            return;
        if (Time.time - lastGraspPulse < GraspPulseInterval)
            return;
        lastGraspPulse = Time.time;
        bool any = false;
        for (int h = 0; h < 2; h++)
        {
            if (kinds[h] == SocialKind.Reach)
            {
                Haptics.Pulse(h == 0, 0.6f, 0.08f);
                any = true;
            }
        }
        if (!any)
        {
            Haptics.Pulse(true, 0.4f, 0.06f);
            Haptics.Pulse(false, 0.4f, 0.06f);
        }
    }
}
