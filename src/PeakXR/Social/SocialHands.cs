using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Interact;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Social;

/// <summary>
/// Two-body interactions of the local VR player, driven by hand proximity instead of head aim. A grip on a
/// friend, a thorn or a worn pack selects that target for the game's own interaction (the
/// <c>Interaction.DoInteractableRaycasts</c> result) and holds the game's interact or reach input, so carrying,
/// dropping, eating, thorn removal, pack opening and helping up all run through vanilla code and RPCs; vanilla
/// peers see the normal result. Feeding holds the game's use-on-friend while a feedable item stays at a friend's
/// mouth. Grips arrive through <see cref="IGripExtension"/> from <c>interact.physical</c>.
/// </summary>
internal sealed class SocialHands : IGripExtension
{
    public static readonly SocialHands Instance = new();

    private enum Kind
    {
        None,

        /// <summary>Hold interact on <see cref="targets"/>: thorns, stuck items, packs, drop, cannibal hold.</summary>
        Interact,

        /// <summary>Hold interact on a passed-out friend; may need both hands.</summary>
        Carry,

        /// <summary>Hold the empty-hand reach (use secondary) while gripping a friend who can be helped.</summary>
        Reach,
    }

    private const float FeedRadius = 0.22f;
    private const float FeedExitRadius = 0.3f;
    private const float FeedDwell = 0.25f;
    private const float GraspPulseInterval = 0.3f;

    private readonly Kind[] kinds = new Kind[2];
    private readonly IInteractible[] targets = new IInteractible[2];
    private readonly Character[] characters = new Character[2];
    private readonly ButtonEdge interactEdge = new();
    private readonly ButtonEdge secondaryEdge = new();
    private readonly StruggleGesture struggle = new();

    private Character feedCandidate;
    private float feedTimer;
    private bool wasSticky;
    private float lastGraspPulse = -10f;
    private bool vanillaInteract;
    private bool vanillaInteractPressed;

    /// <summary>Interactible chosen by a hand this frame, handed to the game's interaction raycast.</summary>
    public IInteractible RaycastTarget { get; private set; }

    /// <summary>The friend a held item is being fed to; relaxes the face-each-other check.</summary>
    public CharacterInteractible FeedTarget { get; private set; }

    private static bool Ready(Character c) =>
        VRSession.Active && SocialConfig.Instance.HandInteract && c == Character.localCharacter;

    // ------------------------------------------------------------------ grip hooks

    public bool ClaimPress(in GripHookContext ctx) =>
        Ready(ctx.Character)
        && !ctx.ThisHandHolds
        && Find(ctx.Character, ctx.HandPosition, out _, out _, out _);

    public void BeginPress(in GripHookContext ctx)
    {
        int h = ctx.Left ? 0 : 1;
        if (!Find(ctx.Character, ctx.HandPosition, out var kind, out var target, out var who))
            return;
        kinds[h] = kind;
        targets[h] = target;
        characters[h] = who;
        if (target is Item)
            PhysicalInteraction.ExpectItemIn(ctx.Left);
        Haptics.Pulse(ctx.Left, 0.5f, 0.05f);
    }

    public bool EndPress(in GripHookContext ctx)
    {
        Clear(ctx.Left ? 0 : 1);
        return true;
    }

    public bool TryHeldRelease(in GripHookContext ctx) => false;

    private void Clear(int h)
    {
        kinds[h] = Kind.None;
        targets[h] = null;
        characters[h] = null;
    }

    /// <summary>
    /// What a hand at <paramref name="pos"/> (world) can act on, nearest small targets first: thorns and arrows on
    /// anyone (CharacterAfflictions.physicalThorns, CharacterAfflictions.cs:162), items stuck to a body
    /// (StickyItemComponent.ALL_STUCK_ITEMS, StickyItemComponent.cs:8), then another character's worn pack (from
    /// behind) or body.
    /// </summary>
    private static bool Find(
        Character c,
        Vector3 pos,
        out Kind kind,
        out IInteractible target,
        out Character who
    )
    {
        kind = Kind.None;
        target = null;
        who = null;
        float reach = SocialConfig.Instance.Reach;
        float small = reach * 0.5f;
        float bestSq = small * small;
        var all = Character.AllCharacters;
        // ThornOnMe.IsInteractible (ThornOnMe.cs:123) refuses while stuck; its aim-angle part is what the hand replaces.
        bool canPluck = !c.IsStuck();

        for (int i = 0; canPluck && i < all.Count; i++)
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
            kind = Kind.Interact;
            return true;
        }

        var other = NearestBody(c, pos, reach);
        if (other == null)
            return false;
        who = other;

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
            kind = Kind.Interact;
            target = pack;
            return true;
        }

        var body = other.refs.interactible;
        if (body != null && !other.isBot)
        {
            var d = other.data;
            if (d.carrier == c || other.refs.customization.isCannibalizable)
            {
                kind = Kind.Interact;
                target = body;
                return true;
            }
            if (d.fullyPassedOut && !d.dead && d.carrier == null)
            {
                kind = Kind.Carry;
                target = body;
                return true;
            }
        }
        // CharacterGrabbing.CanGrab gates (CharacterGrabbing.cs:319): no reach while on a wall, rope or vine, so a
        // climbing hand near a friend still catches the wall.
        var cd = c.data;
        if (
            cd.currentItem == null
            && !cd.isClimbing
            && !cd.isRopeClimbing
            && !cd.isVineClimbing
            && CharacterGrabbing.TargetCanBeHelped(other, c)
        )
        {
            kind = Kind.Reach;
            return true;
        }
        return false;
    }

    /// <summary>Another player character with a bodypart within <paramref name="radius"/> of a world point.</summary>
    private static Character NearestBody(Character self, Vector3 pos, float radius)
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
        if (!VRSession.Active || c != Character.localCharacter)
        {
            Clear(0);
            Clear(1);
            return;
        }
        var cfg = SocialConfig.Instance;
        Validate(c, 0);
        Validate(c, 1);

        IInteractible active = ActiveInteract(cfg, 0) ?? ActiveInteract(cfg, 1);
        bool reaching =
            c.data.currentItem == null && (kinds[0] == Kind.Reach || kinds[1] == Kind.Reach);

        bool feeding = false;
        if (active == null && cfg.FeedAtMouth)
            feeding = UpdateFeed(c, dt);
        else
            ResetFeed();

        RaycastTarget = active ?? FeedTarget;

        interactEdge.Update(active != null, dt);
        input.interactWasPressed |= interactEdge.Pressed;
        input.interactIsPressed |= interactEdge.Held;
        input.interactWasReleased |= interactEdge.Released;

        secondaryEdge.Update(reaching || feeding, dt);
        input.useSecondaryWasPressed |= secondaryEdge.Pressed;
        input.useSecondaryIsPressed |= secondaryEdge.Held;
        input.useSecondaryWasReleased |= secondaryEdge.Released;

        UpdateStruggle(c, input, cfg, dt);
        UpdateStickyCue(c);
    }

    /// <summary>Last <c>CharacterInput.Sample</c> postfix: while passed out a grip must not count as holding interact,
    /// which speeds up dying (Character.cs:1177). Both grips and both triggers give up on purpose.</summary>
    public void GuardGiveUp(Character c, CharacterInput input)
    {
        if (!VRSession.Active || !SocialConfig.Instance.GiveUpGuard || !c.data.fullyPassedOut)
            return;
        bool deliberate =
            VRInput.Left.Grip > 0.6f
            && VRInput.Right.Grip > 0.6f
            && VRInput.Left.Trigger > 0.6f
            && VRInput.Right.Trigger > 0.6f;
        input.interactIsPressed = vanillaInteract || deliberate;
        input.interactWasPressed =
            vanillaInteractPressed || (deliberate && input.interactWasPressed);
    }

    private IInteractible ActiveInteract(SocialConfig cfg, int h)
    {
        switch (kinds[h])
        {
            case Kind.Interact:
                return targets[h];
            case Kind.Carry:
                if (!cfg.CarryBothHands)
                    return targets[h];
                return kinds[1 - h] == Kind.Carry && characters[1 - h] == characters[h]
                    ? targets[h]
                    : null;
            default:
                return null;
        }
    }

    private void Validate(Character c, int h)
    {
        if (kinds[h] == Kind.None)
            return;
        var who = characters[h];
        bool gone = who == null || !who.gameObject.activeInHierarchy;
        if (targets[h] is Object unityTarget && unityTarget == null)
            gone = true;
        if (kinds[h] == Kind.Reach && !gone && !CharacterGrabbing.TargetCanBeHelped(who, c))
            gone = true;
        if (gone)
            Clear(h);
    }

    /// <summary>Feed: a canUseOnFriend item held at another player's head for a moment.</summary>
    private bool UpdateFeed(Character c, float dt)
    {
        var item = c.data.currentItem;
        if (item == null || !item.canUseOnFriend)
        {
            ResetFeed();
            return false;
        }
        var point = item.Center();
        if (feedCandidate != null)
        {
            if (
                !feedCandidate.gameObject.activeInHierarchy
                || !NearHead(feedCandidate, point, FeedExitRadius)
            )
                ResetFeed();
        }
        if (feedCandidate == null)
        {
            feedCandidate = NearestHead(c, point, FeedRadius);
            feedTimer = 0f;
            if (feedCandidate == null)
                return false;
        }
        feedTimer += dt;
        FeedTarget = feedCandidate.refs.interactible;
        return feedTimer >= FeedDwell && FeedTarget != null;
    }

    private void ResetFeed()
    {
        feedCandidate = null;
        feedTimer = 0f;
    }

    private static bool NearHead(Character ch, Vector3 point, float radius)
    {
        if (
            ch.refs.ragdoll == null
            || !ch.refs.ragdoll.partDict.TryGetValue(BodypartType.Head, out var head)
        )
            return false;
        return (head.transform.position - point).sqrMagnitude <= radius * radius;
    }

    private static Character NearestHead(Character self, Vector3 point, float radius)
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
                || !ch.refs.ragdoll.partDict.TryGetValue(BodypartType.Head, out var head)
            )
                continue;
            float d = (head.transform.position - point).sqrMagnitude;
            if (d < bestSq)
            {
                bestSq = d;
                best = ch;
            }
        }
        return best;
    }

    /// <summary>Webs and fly traps (CharacterMovement.cs:663): each shake stroke is one jump press.</summary>
    private void UpdateStruggle(Character c, CharacterInput input, SocialConfig cfg, float dt)
    {
        if (!cfg.Struggle || c.refs.afflictions == null || !c.refs.afflictions.isStruggling)
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
        if (strokes > 0)
            input.jumpWasPressed = true;
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
        if (!VRSession.Active || !SocialConfig.Instance.GraspHaptics)
            return;
        if (Time.time - lastGraspPulse < GraspPulseInterval)
            return;
        lastGraspPulse = Time.time;
        bool any = false;
        for (int h = 0; h < 2; h++)
        {
            if (kinds[h] == Kind.Reach)
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
