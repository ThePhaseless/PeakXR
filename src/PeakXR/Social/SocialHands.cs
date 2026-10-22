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
    private const float FeedDwell = 0.25f;
    private const float GraspPulseInterval = 0.3f;

    /// <summary>A non-feedable item held this long at a friend's mouth is a refused feed, not a passing hand.</summary>
    private const float FeedRefuseDwell = 0.5f;

    /// <summary>How far (m) the hand pulls away from a gripped thorn before the game's removal hold starts.</summary>
    private const float PluckPull = 0.08f;

    /// <summary>How long (s) a shove holds the reach button, so the game's FixedUpdate sees it (Character.cs:1203).</summary>
    private const float ShoveTap = 0.1f;

    private readonly SocialKind[] kinds = new SocialKind[2];
    private readonly IInteractible[] targets = new IInteractible[2];
    private readonly Character[] characters = new Character[2];
    private readonly ButtonEdge interactEdge = new();
    private readonly ButtonEdge secondaryEdge = new();
    private readonly StruggleGesture struggle = new();
    private readonly ShoveGesture shove = new();

    private Character feedCandidate;
    private float feedTimer;
    private bool feedRefused;
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

    /// <summary>The hand that last gripped a passed-out friend: where the dropped-pack notice goes.</summary>
    public bool CarryLeft { get; private set; }

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
        if (refusal != DenyReason.None)
        {
            // The grip stays owned (nothing falls through to a grab or the head's interact) and says why. A held
            // item turns red for HandsFull; a friend's body never does (D10).
            Affordance.Deny(ctx.Left, at, refusal, default, ctx.HeldItem);
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
            pluckFrom[h] = Vector3.Distance(ctx.HandPosition, at);
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
                refusal = DenyReason.StuckToHand;
                target = null;
                return true;
            }
            kind = SocialKind.Interact;
            return true;
        }

        var other = NearestBody(c, pos, reach, out var part);
        if (other == null)
            return false;
        who = other;
        at = part.transform.position;

        var pack =
            other.refs.backpackHandler != null
                ? other.refs.backpackHandler.activeBackpackVisuals
                : null;
        if (
            pack != null
            && pack.isActiveAndEnabled
            && (pack.transform.position - pos).sqrMagnitude < reach * reach * 1.6f
            && Vector3.Dot(pos - other.Center, other.data.lookDirection_Flat) < 0f
        )
        {
            kind = SocialKind.Interact;
            target = pack;
            return true;
        }

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

    /// <summary>Run setting HelpingHand = 1 (CharacterGrabbing.isKickMode, CharacterGrabbing.cs:33).</summary>
    private static bool KickMode => RunSettings.GetValue(RunSettings.SETTINGTYPE.HelpingHand) == 1;

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
        var hand = rig != null ? rig.Hand(h == 0) : null;
        if (hand != null && Vector3.Distance(hand.position, thorn.Center()) >= pluckFrom[h] + PluckPull)
            plucking[h] = false;
    }

    /// <summary>Last <c>CharacterInput.Sample</c> postfix: while passed out a grip must not count as holding interact,
    /// which speeds up dying (Character.cs:1177). Both grips and both triggers give up on purpose; a single grip says
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
        if (
            kinds[h] == SocialKind.Reach
            && !gone
            && !CharacterGrabbing.TargetCanBeHelped(who, c)
        )
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
        bool open = VRInput.Left.Grip < 0.3f && VRInput.Right.Grip < 0.3f;
        if (
            !VRInput.Left.Tracked
            || !VRInput.Right.Tracked
            || !shove.Update(
                VRInput.Left.Velocity.N(),
                VRInput.Right.Velocity.N(),
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

    /// <summary>Feed: a canUseOnFriend item held at another player's head (a healing item: at their torso) for a
    /// moment. Any other item held there is refused (CharacterInteractible.HasItemCanUseOnFriend, :90).</summary>
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
        if (feedCandidate != null)
        {
            if (
                !feedCandidate.gameObject.activeInHierarchy
                || !NearPart(feedCandidate, part, point, FeedExitRadius + grow)
            )
                ResetFeed();
        }
        if (feedCandidate == null)
        {
            feedCandidate = NearestPart(c, part, point, FeedRadius + grow);
            feedTimer = 0f;
            if (feedCandidate == null)
                return false;
        }
        feedTimer += dt;
        if (!item.canUseOnFriend)
        {
            if (!feedRefused && feedTimer >= FeedRefuseDwell)
            {
                feedRefused = true;
                Affordance.Deny(LocalHold.ItemInLeftHand, point, DenyReason.CantUse, default, item);
            }
            return false;
        }
        FeedTarget = feedCandidate.refs.interactible;
        return feedTimer >= FeedDwell && FeedTarget != null;
    }

    private void ResetFeed()
    {
        feedCandidate = null;
        feedTimer = 0f;
        feedRefused = false;
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

    /// <summary>Webs and fly traps (CharacterMovement.cs:663): each shake stroke is one jump press, felt as a tick on
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

    /// <summary>A sticky item glued to the hand (CharacterData.cs:98) cannot be put away: say so once.</summary>
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
