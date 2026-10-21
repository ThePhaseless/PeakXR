using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.UI.Affordances;
using PeakXR.VR;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;

namespace PeakXR.Interact;

/// <summary>
/// Per-frame hand logic for the local VR player, run right after the game samples its input
/// (<c>CharacterInput.Sample</c> postfix). It turns grips, triggers and gestures into the game's own inputs, so
/// every effect goes through vanilla paths and RPCs: interact (pickup), use, drop/throw and wall climbing.
/// Hands are independent: each has its own grip state and action; only one item is held at a time.
/// Grip climbing adds hand-checked holds with surface haptics, a hand-over-hand regrip grace, stamina and slide
/// haptics, an arm lunge into the game's climb jump and a carry over ledges, all through the game's own inputs.
/// </summary>
internal static class PhysicalInteraction
{
    public delegate void TryStartClimb(
        CharacterClimbing self,
        bool forceAttempt,
        Vector3 overide,
        bool botGrab,
        float raycastDistance
    );

    /// <summary><c>CharacterClimbing.TryToStartWallClimb</c> (CharacterClimbing.cs:534, private), resolved in Install.</summary>
    public static TryStartClimb StartWallClimb;

    private sealed class Hand
    {
        public readonly bool Left;
        public readonly AnalogButton Grip = new(0.6f, 0.4f);
        public readonly AnalogButton Trigger = new(0.6f, 0.4f);
        public readonly ThrowEstimator Throw = new();
        public GripPressAction Action;
        public IInteractible Target;
        public IGripExtension Extension;

        /// <summary>When a grip <see cref="GripPressAction.Interact"/> starts pressing the game's interact.</summary>
        public float InteractAt;

        public Hand(bool left) => Left = left;
    }

    private static readonly Hand LeftHand = new(true);
    private static readonly Hand RightHand = new(false);
    private static readonly Hand[] Hands = { LeftHand, RightHand };
    private static readonly ButtonEdge InteractEdge = new();
    private static readonly ButtonEdge PrimaryEdge = new();
    private static readonly ButtonEdge SecondaryEdge = new();
    private static readonly ButtonEdge DropEdge = new();
    private static readonly MouthGesture Mouth = new();
    private static readonly ArmLocomotion Arms = new();
    private static readonly ItemHoldInfo Hold = new();
    private static readonly Collider[] Overlap = new Collider[32];
    private static readonly NVector3[] CandidatePositions = new NVector3[32];
    private static readonly IInteractible[] Candidates = new IInteractible[32];
    private static readonly ClimbGrace Grace = new();
    private static readonly StaminaHaptics StaminaBeat = new();

    /// <summary>Seconds a lunge keeps the climb alive with no hand on the wall, so hands can catch it.</summary>
    private const float LungeGrace = 0.6f;

    /// <summary>Seconds a top-out keeps walking toward the ledge while a grip stays closed.</summary>
    private const float MantleSeconds = 0.6f;

    private static Item lastItem;
    private static bool pendingLeft;
    private static float pendingTime = -10f;
    private static bool physicalClimb;
    private static Vector2 climbInput;
    private static bool dropPending;
    private static bool useBlocked;
    private static int throwFrame = -10;
    private static bool lungePending;
    private static Vector2 lungeInput;
    private static bool hazardPulseLeft;
    private static float hazardPulseTime = -10f;
    private static float mantleUntil = -10f;
    private static Vector3 mantleDir;

    /// <summary>Last grip press's hold probe (<see cref="ProbeHold"/>): a surface was found, and the game would refuse it.</summary>
    private static bool probeHit,
        probeRefused;
    private static Vector3 probePoint;
    private static float probeAngle;

    /// <summary>Slopes flatter than this are ground, not a refused climb (the game refuses below 50).</summary>
    private const float SlopeRefusedFrom = 30f;

    /// <summary>What a grabbing hand selected; the interaction raycast patch hands it to the game.</summary>
    public static IInteractible GrabTarget { get; private set; }

    /// <summary>A grip is acting as the interact button (nothing grabbable was in reach).</summary>
    public static bool GripInteractActive { get; private set; }

    /// <summary>Hand whose aim ray replaces the head for interaction this frame: -1 none, 0 left, 1 right.</summary>
    public static int AimHand { get; private set; } = -1;
    public static bool ThrowPending => Time.frameCount - throwFrame <= 2;
    public static Vector3 ThrowVelocity { get; private set; }

    public static void ConsumeThrow() => throwFrame = -10;

    /// <summary>What this hand's grip is doing, for gesture features (lid lift, parasol runner).</summary>
    public static GripPressAction HandAction(bool left) => (left ? LeftHand : RightHand).Action;

    /// <summary>The interactible this hand's grip selected (grab), or null.</summary>
    public static IInteractible HandTarget(bool left) => (left ? LeftHand : RightHand).Target;

    /// <summary>
    /// The next held item (within 3 s) belongs in this hand. For extensions that equip through the game's own path
    /// (drawing from a body slot, taking from a backpack), so the item lands in the hand that reached for it.
    /// </summary>
    public static void ExpectItemIn(bool left)
    {
        pendingLeft = left;
        pendingTime = Time.time;
    }

    public static void Tick(Character c, CharacterInput input, bool active)
    {
        var rig = VRRig.Instance;
        if (!VRSession.Active || rig == null || c != Character.localCharacter)
        {
            AimHand = -1;
            lungePending = false;
            return;
        }
        var cfg = InteractConfig.Instance;
        float dt = Time.deltaTime;
        float now = Time.time;

        TrackHeldItem(c, now);
        UpdateHand(LeftHand, c, rig, cfg, active, dt, now);
        UpdateHand(RightHand, c, rig, cfg, active, dt, now);
        ClimbStow.Tick(c, rig, cfg);
        UpdateAim(cfg);
        UpdateClimb(c, rig, cfg, dt, now);
        Climb.ClimbHands.Update(c, rig, cfg, active, dt);
        if (hazardPulseTime > 0f && now >= hazardPulseTime)
        {
            hazardPulseTime = -10f;
            Haptics.Pulse(hazardPulseLeft, 1f, 0.06f);
        }
        if (active)
            Inject(c, input, rig, cfg, dt, now);
        else
            lungePending = false;
    }

    private static void TrackHeldItem(Character c, float now)
    {
        var item = c.data.currentItem;
        if (item == lastItem)
            return;
        lastItem = item;
        LocalHold.TwoHand = false;
        EndAction(LeftHand, GripPressAction.SecondHand);
        EndAction(RightHand, GripPressAction.SecondHand);
        Mouth.Reset();
        dropPending = false;
        if (item != null)
            LocalHold.ItemInLeftHand = now - pendingTime < 3f ? pendingLeft : VRInput.LeftHanded;
        pendingTime = -10f;
    }

    private static void EndAction(Hand hand, GripPressAction action)
    {
        if (hand.Action == action)
            hand.Action = GripPressAction.None;
    }

    private static void UpdateHand(
        Hand hand,
        Character c,
        VRRig rig,
        InteractConfig cfg,
        bool active,
        float dt,
        float now
    )
    {
        ref var state = ref VRInput.Hand(hand.Left);
        hand.Grip.Update(state.Tracked ? state.Grip : 0f, dt);
        hand.Trigger.Update(state.Tracked ? state.Trigger : 0f, dt);
        if (state.Tracked)
            hand.Throw.AddSample(state.Position.N(), now);
        // The trigger squeeze of a finger-gun ping cancels the pointing grip's pending interact.
        if (
            hand.Action == GripPressAction.Interact
            && now < hand.InteractAt
            && hand.Trigger.Pressed
        )
            hand.Action = GripPressAction.None;

        if (hand.Grip.Pressed && active && rig.Hand(hand.Left) != null)
            OnGripPress(hand, c, rig, cfg, now);
        else if (hand.Grip.Released)
            OnGripRelease(hand, c, rig, cfg, active);
    }

    private static bool Holding(Character c, out Item item)
    {
        item = c.data.currentItem;
        return item != null && Hold.Bind(item);
    }

    private static void OnGripPress(
        Hand hand,
        Character c,
        VRRig rig,
        InteractConfig cfg,
        float now
    )
    {
        var t = rig.Hand(hand.Left);
        var pos = t.position;
        bool holding = Holding(c, out var item);
        bool thisHolds = holding && LocalHold.ItemInLeftHand == hand.Left;

        var ctx = new GripContext
        {
            PhysicalGrab = cfg.PhysicalGrab,
            PhysicalClimb = cfg.PhysicalClimb,
            HoldingItem = holding,
            ThisHandHolds = thisHolds,
            HeldHandedness = holding ? Hold.Profile.Handedness : Handedness.OneHanded,
            NearHeldItem = holding && !thisHolds && NearItem(item, pos, cfg.GrabRadius * 1.5f),
            NearClimbable =
                cfg.PhysicalClimb
                && Physics.CheckSphere(
                    pos,
                    cfg.ClimbRadius,
                    HelperFunctions.terrainMapMask,
                    QueryTriggerInteraction.Ignore
                ),
            BodyClimbing = c.data.isClimbing || !c.data.isGrounded,
        };
        var kind = HoldKind.Normal;
        probeHit = probeRefused = false;
        if (
            ctx.NearClimbable
            && !ProbeHold(rig, pos, t, cfg.ClimbRadius, out kind)
            && !c.data.isClimbing
        )
            ctx.NearClimbable = false;
        IInteractible target = null;
        if (cfg.PhysicalGrab)
            ctx.Candidate = FindCandidate(c, Eye(rig), pos, t.forward, cfg.GrabRadius, out target);

        var action = GripIntent.OnPress(ctx);
        IGripExtension extension = null;
        var hook = new GripHookContext(c, hand.Left, pos, holding ? item : null, thisHolds);
        if (action != GripPressAction.SecondHand)
        {
            extension = GripHooks.Claim(hook);
            ctx.ExtensionClaim = extension != null;
            action = GripIntent.OnPress(ctx);
        }
        switch (action)
        {
            case GripPressAction.Extension:
                hand.Extension = extension;
                extension.BeginPress(hook);
                break;
            case GripPressAction.GrabItem:
                hand.Target = target;
                pendingLeft = hand.Left;
                pendingTime = now;
                // Item.Interact (Item.cs:578-587) ignores a pickup with no free slot, and drops a non-pocketable item
                // grabbed while climbing: say so instead of the success tick.
                if (
                    target is Item wanted
                    && c.player != null
                    && !c.player.HasEmptySlot(wanted.itemID)
                )
                    Affordance.Deny(hand.Left, wanted.Center(), DenyReason.PocketsFull);
                else if (target is Item loose && c.data.isClimbing && !loose.UIData.canPocket)
                    Affordance.Deny(hand.Left, loose.Center(), DenyReason.NotWhileClimbing);
                else
                    Haptics.Pulse(hand.Left, 0.6f, 0.05f);
                break;
            case GripPressAction.GrabInteractible:
                hand.Target = target;
                Haptics.Pulse(hand.Left, 0.6f, 0.05f);
                break;
            case GripPressAction.SecondHand:
                LocalHold.TwoHand = true;
                Haptics.Pulse(hand.Left, 0.4f, 0.04f);
                break;
            case GripPressAction.Climb:
                if (StartClimb(c, rig, pos, cfg))
                    HoldPulse(hand.Left, kind);
                else
                {
                    action = GripPressAction.None;
                    DenyClimb(c, rig, hand.Left, pos);
                }
                break;
            case GripPressAction.Regrip:
                Haptics.Pulse(hand.Left, 0.2f, 0.03f);
                break;
            case GripPressAction.Interact:
                // A pointing grip may be the start of a finger-gun ping: wait out its trigger window first.
                hand.InteractAt = GripIntent.IsPointing(LocalAvatar.ControllerCurls(hand.Left))
                    ? now + FingerGun.DefaultWindow
                    : now;
                break;
        }
        hand.Action = action;
        // A wall-like surface the game will not grab, and nothing else took the grip: the hand slides off it.
        if (
            probeRefused
            && action is GripPressAction.None or GripPressAction.Interact
            && (Interaction.instance == null || Interaction.instance.currentHovered == null)
        )
            Affordance.Deny(
                hand.Left,
                probePoint,
                probeAngle > 90f ? DenyReason.Overhang : DenyReason.TooFlat
            );
    }

    /// <summary>
    /// The grip touched climbable rock but the game's climb start refused it (CharacterClimbing.cs:534-560): its ray
    /// runs from the camera, at most 1.25 m (:553), so a hand on the wall with the head farther back finds nothing;
    /// and a climber out of stamina cannot grab again within 1 s of the last grab (:557). Says which, and for reach
    /// marks where the head's reach ends.
    /// </summary>
    private static void DenyClimb(Character c, VRRig rig, bool left, Vector3 handPos)
    {
        if (!c.refs.climbing.CanClimb() || c.data.carrierCondor)
            return;
        var eye = Eye(rig);
        var wall = probeHit ? probePoint : handPos;
        if (Vector3.Distance(eye, wall) > 1.25f)
            Affordance.Deny(left, wall, DenyReason.OutOfReach, DenyHint.Range(eye, wall, 1.25f));
        else if (c.data.outOfStaminaFor > 0f)
            Affordance.Deny(left, wall, DenyReason.NoStamina, DenyHint.Wrist());
    }

    private static void OnGripRelease(
        Hand hand,
        Character c,
        VRRig rig,
        InteractConfig cfg,
        bool active
    )
    {
        var action = hand.Action;
        var extension = hand.Extension;
        hand.Action = GripPressAction.None;
        hand.Target = null;
        hand.Extension = null;

        switch (action)
        {
            case GripPressAction.SecondHand:
                LocalHold.TwoHand = false;
                break;
            case GripPressAction.Climb:
                // The climb itself ends in UpdateClimb once the regrip grace runs out with no hand on the wall.
                if (Other(hand).Action != GripPressAction.Climb && cfg.ArmLunge && physicalClimb)
                    TryLunge(hand, c, rig, cfg);
                break;
            case GripPressAction.Extension:
            case GripPressAction.GrabItem:
            case GripPressAction.Regrip:
                bool holdingNow = Holding(c, out var item);
                bool thisHolds = holdingNow && LocalHold.ItemInLeftHand == hand.Left;
                var hook = new GripHookContext(
                    c,
                    hand.Left,
                    rig.Hand(hand.Left) != null ? rig.Hand(hand.Left).position : Vector3.zero,
                    holdingNow ? item : null,
                    thisHolds
                );
                // An extension's own grip ends first; if it leaves the release alone and the hand now holds an
                // item (drawn from a slot), the release counts as that hand letting go of its item.
                if (
                    action == GripPressAction.Extension
                    && extension != null
                    && extension.EndPress(hook)
                )
                    break;
                // No drop or throw while a wheel or menu blocks input: Inject would fire it later, maybe on another item.
                if (!thisHolds || !active)
                    break;
                // Body-relative speed decides a throw, so walking or falling does not turn a release into one.
                var handVelocity = HandVelocity(hand, rig);
                var result = GripIntent.OnRelease(
                    true,
                    item.UIData.canDrop,
                    handVelocity.magnitude,
                    cfg.ThrowSpeed,
                    cfg.PhysicalThrow,
                    cfg.ReleaseToDrop,
                    action != GripPressAction.Extension && GripHooks.HeldRelease(hook)
                );
                if (result == GripReleaseAction.Throw && !item.UIData.canThrow)
                {
                    // Item.ItemUIData.canThrow (Item.cs:71; the flat game hides its throw prompt, GUIManager.cs:1183).
                    result = GripReleaseAction.Keep;
                    Affordance.DenyAtHand(hand.Left, DenyReason.CantThrow);
                }
                if (result == GripReleaseAction.Throw)
                {
                    ThrowVelocity = ReleaseVelocity(handVelocity, c) * cfg.ThrowScale;
                    throwFrame = Time.frameCount;
                    dropPending = true;
                    Haptics.Pulse(hand.Left, 0.5f, 0.05f);
                }
                else if (result == GripReleaseAction.Drop)
                {
                    dropPending = true;
                }
                break;
        }
    }

    private static Hand Other(Hand hand) => hand.Left ? RightHand : LeftHand;

    /// <summary>World release velocity: the hand's velocity relative to the body plus the body's own velocity.</summary>
    private static Vector3 ReleaseVelocity(Vector3 handVelocity, Character c)
    {
        var world = handVelocity;
        if (c.refs.hip != null)
            world += c.refs.hip.Rig.linearVelocity;
        return world;
    }

    /// <summary>The tracked hand's fitted release velocity, rotated from tracking space to world: relative to the
    /// body, since the rig moves with it.</summary>
    private static Vector3 HandVelocity(Hand hand, VRRig rig)
    {
        var local = hand.Throw.GetReleaseVelocity().U();
        return rig.Origin != null ? rig.Origin.TransformVector(local) : local;
    }

    private static bool NearItem(Item item, Vector3 pos, float radius)
    {
        float r2 = radius * radius;
        foreach (var col in item.colliders)
        {
            if (
                col != null
                && col.enabled
                && (col.bounds.ClosestPoint(pos) - pos).sqrMagnitude <= r2
            )
                return true;
        }
        return (item.Center() - pos).sqrMagnitude <= 4f * r2;
    }

    /// <summary>Where the game's own aim rays start: the camera, or the tracked head before it exists. While the head
    /// is inside a wall, rig.camera keeps the camera on the body's side of it (RigDriver.CheckHeadInWall), so these
    /// rays cannot start behind geometry.</summary>
    private static Vector3 Eye(VRRig rig) =>
        MainCamera.instance != null ? MainCamera.instance.transform.position : rig.Head.position;

    private static GrabCandidate FindCandidate(
        Character c,
        Vector3 eye,
        Vector3 pos,
        Vector3 forward,
        float radius,
        out IInteractible target
    )
    {
        target = null;
        int hits = Physics.OverlapSphereNonAlloc(
            pos,
            radius,
            Overlap,
            HelperFunctions.AllPhysicalExceptCharacter,
            QueryTriggerInteraction.Collide
        );
        int count = 0;
        for (int i = 0; i < hits && count < Candidates.Length; i++)
        {
            var col = Overlap[i];
            Overlap[i] = null;
            if (col == null)
                continue;
            var it = col.GetComponentInParent<IInteractible>();
            if (it == null || it is CharacterInteractible)
                continue;
            if (it is Item held && held == c.data.currentItem)
                continue;
            if (!it.IsInteractible(c))
                continue;
            // Line of sight like the game's interact ray (Interaction.cs:214): a hand pushed through a crate or a
            // rock fin does not grab what lies behind it. Stops 5 cm short so a resting item's floor does not count;
            // the target's own map-layer collider (luggage, kiosks) does not block it.
            var point = col.bounds.ClosestPoint(pos);
            var toPoint = point - eye;
            float len = toPoint.magnitude;
            if (len > 0.05f)
            {
                var block = HelperFunctions
                    .LineCheck(
                        eye,
                        eye + toPoint * ((len - 0.05f) / len),
                        HelperFunctions.LayerType.TerrainMap
                    )
                    .collider;
                if (block != null && block.GetComponentInParent<IInteractible>() != it)
                    continue;
            }
            Candidates[count] = it;
            CandidatePositions[count] = point.N();
            count++;
        }

        int best = GrabSelector.Select(
            pos.N(),
            forward.N(),
            CandidatePositions,
            null,
            count,
            radius
        );
        if (best >= 0)
            target = Candidates[best];
        for (int i = 0; i < count; i++)
            Candidates[i] = null;

        if (target == null)
            return GrabCandidate.None;
        return target is Item ? GrabCandidate.Item : GrabCandidate.Interactible;
    }

    private static bool StartClimb(Character c, VRRig rig, Vector3 handPos, InteractConfig cfg)
    {
        if (c.data.isClimbing)
        {
            BeginPhysicalClimb();
            return true;
        }
        if (StartWallClimb == null || !c.refs.climbing.CanClimb())
            return false;

        // Vanilla casts from the camera along a direction; aim it through the gripping hand, no farther than the
        // game's own 1.25 m (CharacterClimbing.cs:534).
        var dir = handPos - Eye(rig);
        float dist = dir.magnitude;
        if (dist < 0.01f)
            return false;
        StartWallClimb(
            c.refs.climbing,
            true,
            dir / dist,
            false,
            Mathf.Min(dist + cfg.ClimbRadius + 0.15f, 1.25f)
        );
        if (!c.data.isClimbing)
            return false;
        BeginPhysicalClimb();
        return true;
    }

    /// <summary>
    /// Looks for the hold under the hand: a short ray through the hand from the head, then along the hand.
    /// Returns false for a surface the game would refuse to grab (CharacterClimbing.cs:511), so the grip falls
    /// back to its other actions. A surface with a climb modifier may override its angle (internal always-climbable
    /// range), so the game decides those itself. No clear surface: also left to the game's own start check.
    /// </summary>
    private static bool ProbeHold(
        VRRig rig,
        Vector3 handPos,
        Transform hand,
        float radius,
        out HoldKind kind
    )
    {
        kind = HoldKind.Normal;
        var dir = handPos - Eye(rig);
        dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : hand.forward;
        if (
            !Physics.Raycast(
                handPos - dir * 0.15f,
                dir,
                out var hit,
                0.3f + radius,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            )
            && !Physics.Raycast(
                handPos - hand.forward * 0.1f,
                hand.forward,
                out hit,
                0.2f + radius,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            )
        )
            return true;

        // Same lookup as CharacterClimbing.SampleWall (CharacterClimbing.cs:445); only read at grip press.
        var mod = hit.collider.GetComponent<ClimbModifierSurface>();
        bool hasMod = mod != null;
        kind = ClimbRules.Kind(
            hasMod,
            hasMod && mod.onlySlideDown,
            hasMod && (mod.applyStatus || mod.applyPetrify),
            hasMod ? mod.staminaUsageMultiplier : 1f
        );
        probeHit = true;
        probePoint = hit.point;
        probeAngle = Vector3.Angle(hit.normal, Vector3.up);
        bool ok = hasMod || ClimbRules.GrabAngleOk(probeAngle);
        // Ground under the feet is not a climbing attempt: only steep slopes and ceilings count as refused.
        probeRefused = !ok && probeAngle >= SlopeRefusedFrom;
        return ok;
    }

    /// <summary>Grab feel by surface: soft for easy holds, a buzz for slippery ones, a strong double beat for hazards.</summary>
    private static void HoldPulse(bool left, HoldKind kind)
    {
        switch (kind)
        {
            case HoldKind.Easy:
                Haptics.Pulse(left, 0.4f, 0.04f);
                break;
            case HoldKind.Slippery:
                Haptics.Pulse(left, 0.5f, 0.25f);
                break;
            case HoldKind.Hazard:
                Haptics.Pulse(left, 1f, 0.06f);
                hazardPulseLeft = left;
                hazardPulseTime = Time.time + 0.12f;
                break;
            default:
                Haptics.Pulse(left, 0.8f, 0.06f);
                break;
        }
    }

    /// <summary>
    /// The last climbing hand thrown upward as it lets go: queue the game's climb jump in that direction. Mirrors the
    /// vanilla gate (CharacterClimbing.cs:122) so a refused jump does not stretch the grace.
    /// </summary>
    private static void TryLunge(Hand hand, Character c, VRRig rig, InteractConfig cfg)
    {
        var d = c.data;
        if (!d.isClimbing || d.sinceClimbJump <= 1f)
            return;
        var stick = ClimbInput.Lunge(
            HandVelocity(hand, rig).N(),
            d.climbNormal.N(),
            cfg.LungeSpeed
        );
        if (stick == System.Numerics.Vector2.Zero)
            return;
        if (d.outOfStaminaFor >= 0.5f)
        {
            // The throw was a lunge, but the game would refuse its climb jump without stamina.
            Affordance.DenyAtHand(hand.Left, DenyReason.NoStamina, DenyHint.Wrist());
            return;
        }
        lungeInput = new Vector2(stick.X, stick.Y);
        lungePending = true;
        Grace.Extend(LungeGrace);
        Haptics.Pulse(hand.Left, 0.6f, 0.05f);
    }

    private static void BeginPhysicalClimb()
    {
        if (!physicalClimb)
        {
            Arms.Reset();
            Grace.Reset();
            StaminaBeat.Reset();
        }
        physicalClimb = true;
        mantleUntil = -10f;
    }

    private static void EndClimb(Character c)
    {
        if (physicalClimb && c.data.isClimbing && !PrimaryEdge.Held)
            c.refs.climbing.StopClimbing();
        physicalClimb = false;
        climbInput = Vector2.zero;
        Arms.Reset();
    }

    private static void UpdateClimb(Character c, VRRig rig, InteractConfig cfg, float dt, float now)
    {
        if (physicalClimb && !c.data.isClimbing)
        {
            // The game ended the climb (top-out, jump, stamina, ragdoll): grips must close again to climb.
            if (climbInput.y > 0.3f && c.data.sinceClimbJump > 0.5f && c.data.outOfStaminaFor <= 0f)
                TryStartMantle(c, now);
            physicalClimb = false;
            climbInput = Vector2.zero;
            EndAction(LeftHand, GripPressAction.Climb);
            EndAction(RightHand, GripPressAction.Climb);
            Arms.Reset();
        }
        if (!physicalClimb || dt <= 0f)
            return;

        bool leftGrip = LeftHand.Action == GripPressAction.Climb;
        bool rightGrip = RightHand.Action == GripPressAction.Climb;
        Grace.Grace = cfg.ClimbRegripGrace;
        if (!Grace.Update(leftGrip || rightGrip, dt))
        {
            EndClimb(c);
            return;
        }

        bool l = leftGrip && VRInput.Left.Tracked;
        bool r = rightGrip && VRInput.Right.Tracked;
        var d = Arms.Update(l, VRInput.Left.Position.N(), r, VRInput.Right.Position.N(), dt).U();
        var world = (rig.Origin != null ? rig.Origin.TransformVector(d) : d) * cfg.ClimbGain;
        var climbing = c.refs.climbing;
        var mod = c.data.climbMod;
        var target = ClimbInput.FromBodyVelocity(
            (world / dt).N(),
            c.data.climbNormal.N(),
            ClimbInput.WallSpeed(
                climbing.climbSpeed,
                climbing.climbSpeedMod
                    * c.data.staminaMod
                    * (mod != null ? mod.speedMultiplier : 1f)
            ),
            0.1f
        );
        var smoothed = ClimbInput.Smooth(
            new System.Numerics.Vector2(climbInput.x, climbInput.y),
            target,
            12f,
            dt
        );
        smoothed = ClimbInput.Settle(smoothed, target);
        climbInput = new Vector2(smoothed.X, smoothed.Y);

        // Slide-only surface or no stamina: the game slides the climber down (CharacterClimbing.cs:297, :301, :263).
        // Upward pulls would only burn stamina (CharacterClimbing.cs:132), so they are dropped.
        bool slideOnly = mod != null && mod.onlySlideDown;
        bool outOfStamina = c.data.outOfStaminaFor > 0f;
        if ((slideOnly || outOfStamina) && climbInput.y > 0f)
            climbInput.y = 0f;

        if (cfg.StaminaHaptics && (leftGrip || rightGrip))
        {
            float slide = ClimbRules.SlideSpeed(
                climbing.playerSlide.y,
                slideOnly,
                mod != null ? mod.speedMultiplier : 1f,
                climbing.climbSpeed * climbing.climbSpeedMod
            );
            float amplitude = StaminaBeat.Update(c.GetTotalStamina(), outOfStamina, slide, dt);
            if (amplitude > 0f)
            {
                if (leftGrip)
                    Haptics.Pulse(true, amplitude, 0.04f);
                if (rightGrip)
                    Haptics.Pulse(false, amplitude, 0.04f);
            }
        }
    }

    /// <summary>
    /// The game has no mantle: a climber pushing up lets go of the wall when it samples the flat top
    /// (CharacterClimbing.cs:470, then :269) and walks over the lip with the same held stick. An arm pull that
    /// tops out onto a walkable ledge keeps walking toward it for a moment while a grip stays closed.
    /// </summary>
    private static void TryStartMantle(Character c, float now)
    {
        var into = -c.data.climbNormal;
        into.y = 0f;
        if (into.sqrMagnitude < 1e-4f)
            return;
        into.Normalize();
        if (
            !Physics.Raycast(
                c.Center + Vector3.up + into * 0.5f,
                Vector3.down,
                out var hit,
                2f,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            ) || !ClimbRules.IsLedgeTop(Vector3.Angle(hit.normal, Vector3.up))
        )
            return;
        mantleDir = into;
        mantleUntil = now + MantleSeconds;
    }

    private static void UpdateAim(InteractConfig cfg)
    {
        AimHand = -1;
        if (!cfg.HandAimedInteract)
            return;
        bool l = VRInput.Left.Tracked && GripIntent.IsPointing(LocalAvatar.ControllerCurls(true));
        bool r = VRInput.Right.Tracked && GripIntent.IsPointing(LocalAvatar.ControllerCurls(false));
        if (l && LeftHand.Action == GripPressAction.Interact)
            AimHand = 0;
        else if (r)
            AimHand = 1;
        else if (l)
            AimHand = 0;
    }

    private static int eatTrace = -1;
    private static int eatTraceLines;

    /// <summary>Self-test diagnosis of eat-at-mouth (vr.12): one Debug line whenever a gate on the way from the grip
    /// at the mouth to the game's primary use flips, at most 80 per held item. The item fields show the previous
    /// frame's <c>CharacterItems.DoUsing</c> (CharacterItems.cs:196): its cancels (:209-216) zero castProgress.</summary>
    private static void TraceEat(
        Character c,
        CharacterInput input,
        VRRig rig,
        InteractConfig cfg,
        bool holding,
        Transform mouthHand,
        bool atMouth,
        bool primaryDown
    )
    {
        var item = c.data.currentItem;
        if (!holding || item == null)
        {
            eatTrace = -1;
            eatTraceLines = 0;
            return;
        }
        float d =
            rig.Head != null && mouthHand != null
                ? NVector3.Distance(
                    MouthGesture.MouthPoint(
                        rig.Head.position.N(),
                        rig.Head.rotation.N(),
                        Mouth.MouthOffset
                    ),
                    mouthHand.position.N()
                )
                : -1f;
        bool inside = d >= 0f && d <= Mouth.ExitRadius;
        bool canUse = item.CanUsePrimary();
        var gui = GUIManager.instance;
        bool guiBlocks = gui != null && (gui.windowBlockingInput || gui.wheelActive);
        int mask =
            (cfg.EatAtMouth ? 1 : 0)
            | (Hold.Edible ? 2 : 0)
            | (VRInput.HeadTracked ? 4 : 0)
            | (inside ? 8 : 0)
            | (atMouth ? 16 : 0)
            | (primaryDown ? 32 : 0)
            | (PrimaryEdge.Held ? 64 : 0)
            | (useBlocked ? 128 : 0)
            | (input.usePrimaryIsPressed ? 256 : 0)
            | (input.usePrimaryWasReleased ? 512 : 0)
            | (input.useSecondaryIsPressed ? 1024 : 0)
            | (input.useSecondaryWasReleased ? 2048 : 0)
            | (item.isUsingPrimary ? 4096 : 0)
            | (item.isUsingSecondary ? 8192 : 0)
            | (canUse ? 16384 : 0)
            | (guiBlocks ? 32768 : 0);
        if (mask == eatTrace || eatTraceLines >= 80)
            return;
        eatTrace = mask;
        eatTraceLines++;
        Plugin.Log.LogDebug(
            $"[interact.eat] f{Time.frameCount} {item.name}: eatAtMouth {cfg.EatAtMouth}, edible {Hold.Edible}, head tracked {VRInput.HeadTracked}, "
                + $"grip {d:0.000} m from the mouth (inside {inside}), atMouth {atMouth}, trigger {primaryDown}, "
                + $"edge pressed {PrimaryEdge.Pressed} held {PrimaryEdge.Held}, blocked {useBlocked}; input primary "
                + $"{input.usePrimaryWasPressed}/{input.usePrimaryIsPressed}/{input.usePrimaryWasReleased} secondary "
                + $"{input.useSecondaryWasPressed}/{input.useSecondaryIsPressed}/{input.useSecondaryWasReleased} (pressed/held/released); "
                + $"item using {item.isUsingPrimary}/{item.isUsingSecondary} cast {item.castProgress:0.000} canUse {canUse}, gui blocks {guiBlocks}"
        );
    }

    private static void Inject(
        Character c,
        CharacterInput input,
        VRRig rig,
        InteractConfig cfg,
        float dt,
        float now
    )
    {
        // Interact: grabs and the grip fallback press the game's interact button while the grip stays closed.
        GrabTarget = null;
        GripInteractActive = false;
        bool interactDown = false;
        foreach (var hand in Hands)
        {
            switch (hand.Action)
            {
                case GripPressAction.GrabItem:
                case GripPressAction.GrabInteractible:
                    // Picked up: a grip still closed on it stops holding interact, so a cook or light gesture gets a
                    // fresh press (Interaction.cs:116 only starts a hold after interact was up).
                    if (hand.Target is Item grabbed && grabbed == c.data.currentItem)
                        break;
                    interactDown = true;
                    GrabTarget ??= hand.Target;
                    break;
                case GripPressAction.Interact:
                    if (now < hand.InteractAt)
                        break;
                    interactDown = true;
                    GripInteractActive = true;
                    break;
            }
        }
        if (Physical.GestureRequests.TryInteract(out var gestureTarget))
        {
            // An active cook or light gesture wins over a grip's target: it is what the held item is touching.
            interactDown = true;
            GrabTarget = gestureTarget;
        }
        InteractEdge.Update(interactDown, dt);
        input.interactWasPressed |= InteractEdge.Pressed;
        input.interactIsPressed |= InteractEdge.Held;
        input.interactWasReleased |= InteractEdge.Released;

        // Use: the holding hand's trigger (right when empty-handed) is primary, the other trigger secondary.
        bool holding = Holding(c, out _);
        var primaryHand = holding && LocalHold.ItemInLeftHand ? LeftHand : RightHand;
        bool primaryDown = cfg.TriggerUse && primaryHand.Trigger.Held;
        bool secondaryDown = cfg.TriggerUse && Other(primaryHand).Trigger.Held;

        bool atMouth = false;
        var mouthHand = rig.Hand(primaryHand.Left);
        if (
            cfg.EatAtMouth
            && holding
            && Hold.Edible
            && VRInput.HeadTracked
            && rig.Head != null
            && mouthHand != null
        )
        {
            bool was = Mouth.Active;
            // The tracked grip, not the physics item: the eye sits inside the holder's own head collider (a 0.38 m
            // sphere on the Character layer, which held items collide with), so the item never gets near the mouth.
            atMouth = Mouth.Update(
                rig.Head.position.N(),
                rig.Head.rotation.N(),
                mouthHand.position.N(),
                dt
            );
            if (atMouth && !was)
                Haptics.Pulse(primaryHand.Left, 0.3f, 0.05f);
        }
        else
        {
            Mouth.Reset();
        }
        PrimaryEdge.Update(primaryDown || atMouth || Physical.GestureRequests.PrimaryHeld, dt);
        SecondaryEdge.Update(secondaryDown, dt);
        if (PrimaryEdge.Pressed && holding)
        {
            var held = c.data.currentItem;
            bool handsOk = ItemHoldTable.CanUse(Hold.Profile.Handedness, LocalHold.TwoHand);
            // A press the game will not act on gets the denied cue instead of the success tick (a one-handed hold of
            // a two-handed item is denied below).
            if (!Affordance.Enabled)
                Haptics.Pulse(primaryHand.Left, 0.3f, 0.03f);
            // Pitons are used through UpdateClimbingSpikeUse (CharacterItems.cs:981), which never reads
            // CanUsePrimary; SpikeCue reports their refusals.
            else if (
                handsOk
                && !held.CanUsePrimary()
                && !held.TryGetComponent<ClimbingSpikeComponent>(out _)
            )
                UseDenial.Deny(c, held, primaryHand.Left);
            else if (handsOk)
                Haptics.Pulse(primaryHand.Left, 0.3f, 0.03f);
        }

        if (holding && !ItemHoldTable.CanUse(Hold.Profile.Handedness, LocalHold.TwoHand))
        {
            // Two-handed item held in one hand: carry only. Cancel a use that was in progress.
            bool wasUsing =
                input.usePrimaryIsPressed
                || input.useSecondaryIsPressed
                || PrimaryEdge.Held
                || SecondaryEdge.Held;
            input.usePrimaryWasPressed = input.usePrimaryIsPressed = false;
            input.useSecondaryWasPressed = input.useSecondaryIsPressed = false;
            input.usePrimaryWasReleased = input.useSecondaryWasReleased = !useBlocked && wasUsing;
            useBlocked = true;
            // Show where the free hand goes: the item's own grip for it (Hand_L/Hand_R), else its centre.
            if (PrimaryEdge.Pressed || SecondaryEdge.Pressed)
            {
                bool left = LocalHold.ItemInLeftHand;
                var grip = left ? Hold.GripRight : Hold.GripLeft;
                Affordance.DenyAtHand(
                    left,
                    DenyReason.NeedsTwoHands,
                    grip != null ? DenyHint.On(grip) : DenyHint.On(c.data.currentItem.transform)
                );
            }
        }
        else
        {
            useBlocked = false;
            input.usePrimaryWasPressed |= PrimaryEdge.Pressed;
            input.usePrimaryIsPressed |= PrimaryEdge.Held;
            input.usePrimaryWasReleased |= PrimaryEdge.Released;
            input.useSecondaryWasPressed |= SecondaryEdge.Pressed;
            input.useSecondaryIsPressed |= SecondaryEdge.Held;
            input.useSecondaryWasReleased |= SecondaryEdge.Released;
        }
        // Only the self-test's scripted hand steps, not its idle rest pose, so the trace budget lasts until the eat step.
        if (VRInput.TestOverride is { } scripted && scripted != Sandbox.SelfTestRunner.IdleHands)
            TraceEat(c, input, rig, cfg, holding, mouthHand, atMouth, primaryDown);

        // Drop: button fallback with the game's hold-to-charge, or a one-frame press+release from a hand release.
        DropEdge.Update(cfg.DropButtonDown(), dt);
        input.dropWasPressed |= DropEdge.Pressed;
        input.dropIsPressed |= DropEdge.Held;
        input.dropWasReleased |= DropEdge.Released;
        if (dropPending)
        {
            dropPending = false;
            input.dropWasPressed = true;
            input.dropWasReleased = true;
            input.dropIsPressed = false;
        }

        // Climbing: arm pulls become the game's climb stick input; a released trigger must not end a grip climb.
        if (physicalClimb)
        {
            input.movementInput = Vector2.ClampMagnitude(input.movementInput + climbInput, 1f);
            input.usePrimaryWasReleased = false;
        }
        // Rope and vine: arm pulls along the line become the game's rope or vine climb stick.
        input.movementInput = Vector2.ClampMagnitude(
            input.movementInput + Climb.ClimbHands.StickInput,
            1f
        );

        // Top-out: walk toward the ledge while a grip stays closed, as a held stick would.
        if (now < mantleUntil && !c.data.isClimbing && (LeftHand.Grip.Held || RightHand.Grip.Held))
        {
            var walk = ClimbInput.WalkStick(
                mantleDir.N(),
                c.data.lookDirection.N(),
                c.data.lookDirection_Right.N()
            );
            input.movementInput = Vector2.ClampMagnitude(
                input.movementInput + new Vector2(walk.X, walk.Y),
                1f
            );
        }
        else
        {
            mantleUntil = -10f;
        }

        // Lunge: one frame of the game's sprint press with the throw direction, read by its climb jump
        // (CharacterClimbing.cs:118-124, RPCA_ClimbJump at :238).
        if (lungePending)
        {
            lungePending = false;
            if (c.data.isClimbing)
            {
                input.sprintWasPressed = true;
                input.sprintIsPressed = true;
                input.movementInput = lungeInput;
            }
        }
    }
}
