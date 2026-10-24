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

    /// <summary><c>CharacterClimbing.sinceLastClimbStarted</c> (CharacterClimbing.cs:52, private), resolved in Install.</summary>
    public static HarmonyLib.AccessTools.FieldRef<CharacterClimbing, float> SinceLastClimbStarted;

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

        /// <summary>This Interact grip is a pointed aim at an item taker (<see cref="GripContext.PointedAtTaker"/>).</summary>
        public bool AimedTaker;

        /// <summary>This grip pressed the game's interact on the last frame <c>Inject</c> ran.</summary>
        public bool Injected;

        /// <summary>A <see cref="GripPressAction.GrabItem"/> the game has taken (or refused with a cue): no pending
        /// success pulse.</summary>
        public bool Confirmed;

        /// <summary>When an unconfirmed pickup presses interact once more (the 0.25 s equip cooldown, Item.cs:580).</summary>
        public float RetryAt;
        public bool Retried;

        /// <summary>This pickup is a free hand's while another item is held: the game pockets it (Item.cs:578-591).</summary>
        public bool Pocketing;

        /// <summary>When a forced detach's second "torn off" beat is due on this hand.</summary>
        public float TornAt = -10f;

        /// <summary>A release of this grip may drop or throw the held item (<see cref="ReleaseArming"/>): false for an
        /// item that arrived while the grip was already closed.</summary>
        public bool Armed = true;

        /// <summary>When this hand last let an item go (released its grip on it, or the item left it).</summary>
        public float ItemLeftAt = -10f;

        public Hand(bool left) => Left = left;
    }

    /// <summary>What the last grip press decided and why (self-test failure notes, e.g. vr.15).</summary>
    internal static string LastGripNote = "";

    private static readonly Hand LeftHand = new(true);
    private static readonly Hand RightHand = new(false);
    private static readonly Hand[] Hands = { LeftHand, RightHand };
    private static readonly ButtonEdge InteractEdge = new();
    private static readonly WheelGripLatch WheelHold = new();
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
    private static readonly SurfaceTexture Texture = new();

    /// <summary>When the last top-out started its mantle (cue clock), for the self-test (vr.54).</summary>
    internal static float LastMantleAt { get; private set; } = float.NegativeInfinity;

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

    /// <summary>The top-out walk is carrying the body onto a ledge (room-scale follow stays out of it).</summary>
    internal static bool Mantling => Time.time < mantleUntil;
    private static Vector3 mantleDir;

    /// <summary>Last grip press's hold probe (<see cref="ProbeHold"/>): a surface was found, and the game would refuse it.</summary>
    private static bool probeHit,
        probeRefused;
    private static Vector3 probePoint;
    private static Vector3 probeNormal;
    private static float probeAngle;

    /// <summary>The game's wall-grab reach from the camera (<c>TryToStartWallClimb</c>, CharacterClimbing.cs:534).</summary>
    private const float ClimbReach = 1.25f;

    /// <summary>Seconds after a pickup grip before an untaken item gets one more interact press: past the game's 0.25 s
    /// equip cooldown (Item.cs:580, CharacterItems.cs:488).</summary>
    private const float PickupRetry = 0.3f;

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

    /// <summary>The last throw's parts before scaling, for the self-test's vr.13 note: the fitted hand velocity
    /// (world, relative to the body) and the hip velocity added to it.</summary>
    internal static Vector3 LastThrowHand { get; private set; }

    internal static Vector3 LastThrowBody { get; private set; }

    /// <summary>Hand throws since start; the VR practice counts its throw station with it.</summary>
    public static int ThrowCount { get; private set; }

    public static void ConsumeThrow() => throwFrame = -10;

    /// <summary>What this hand's grip is doing, for gesture features (lid lift, parasol runner).</summary>
    public static GripPressAction HandAction(bool left) => (left ? LeftHand : RightHand).Action;

    /// <summary>
    /// This hand holds something through its grip, so opening it lets go: a wall climb (through its top-out walk), a
    /// rope, vine or handle, the second hand on the held item, the held item itself when opening drops it
    /// (<c>ReleaseToDrop</c>, armed), or a friend it lifts. What <see cref="GripToggle"/> may latch.
    /// </summary>
    internal static bool GripHolds(bool left)
    {
        var c = Character.localCharacter;
        if (c == null)
            return false;
        var hand = left ? LeftHand : RightHand;
        if (hand.Action == GripPressAction.Climb && (c.data.isClimbing || Mantling))
            return true;
        if (hand.Action == GripPressAction.SecondHand && LocalHold.TwoHand)
            return true;
        if (
            c.data.currentItem != null
            && LocalHold.ItemInLeftHand == left
            && hand.Armed
            && InteractConfig.Instance.ReleaseToDrop
        )
            return true;
        return Climb.ClimbHands.HandAnchored(left) || Social.SocialHands.Instance.CarryGrip(left);
    }

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
            Climb.ClimbHover.Hide(true);
            Climb.ClimbHover.Hide(false);
            return;
        }
        var cfg = InteractConfig.Instance;
        float dt = Time.deltaTime;
        float now = Time.time;

        bool wheelAtStart = c.data.usingBackpackWheel;
        TrackHeldItem(c, now);
        TrackFall(c, dt);
        UpdateHand(LeftHand, c, rig, cfg, active, dt, now);
        UpdateHand(RightHand, c, rig, cfg, active, dt, now);
        UpdateHover(LeftHand, c, rig, cfg, now);
        UpdateHover(RightHand, c, rig, cfg, now);
        ClimbStow.Tick(c, rig, cfg);
        UpdateAim(cfg);
        UpdateClimb(c, rig, cfg, dt, now);
        Climb.ClimbHands.Update(c, rig, cfg, active, dt);
        if (hazardPulseTime > 0f && now >= hazardPulseTime)
        {
            hazardPulseTime = -10f;
            Haptics.Pulse(hazardPulseLeft, 1f, 0.06f, HapticPriority.Alert);
        }
        TornBeat(LeftHand, now);
        TornBeat(RightHand, now);
        // A grip on a ground backpack opens its wheel (Backpack.Interact, Backpack.cs:34), which makes CanDoInput
        // false (Character.cs:1713) so Inject stops; the wheel then closes on interact up (BackpackWheel.cs:96) and
        // the still-closed grip reopens it every other frame. The grip that opened it holds interact until it opens.
        // A wheel opened during this frame's grips (the shoulder reach, BodyInventory) holds interact itself.
        bool wheel = c.data.usingBackpackWheel;
        WheelHold.Update(
            wheel,
            wheel && !wheelAtStart,
            LeftHand.Injected,
            RightHand.Injected,
            LeftHand.Grip.Held,
            RightHand.Grip.Held
        );
        if (active)
            Inject(c, input, rig, cfg, dt, now);
        else
        {
            lungePending = false;
            if (WheelHold.Holds)
                input.interactIsPressed = true;
        }
    }

    private static void TrackHeldItem(Character c, float now)
    {
        var item = c.data.currentItem;
        if (item == lastItem)
            return;
        if (lastItem != null && item != lastItem)
            (LocalHold.ItemInLeftHand ? LeftHand : RightHand).ItemLeftAt = now;
        if (lastItem != null && item == null)
            LogItemLeft(c, lastItem);
        lastItem = item;
        LocalHold.TwoHand = false;
        EndAction(LeftHand, GripPressAction.SecondHand);
        EndAction(RightHand, GripPressAction.SecondHand);
        Mouth.Reset();
        dropPending = false;
        if (item != null)
        {
            LocalHold.ItemInLeftHand = now - pendingTime < 3f ? pendingLeft : VRInput.LeftHanded;
            var into = LocalHold.ItemInLeftHand ? LeftHand : RightHand;
            into.Armed = ReleaseArming.OnArrive(
                into.Grip.Held,
                into.Action,
                into.Target is Item wanted && SameInstance(wanted, item)
            );
        }
        pendingTime = -10f;
    }

    private static readonly System.Collections.Generic.List<Collider> HandleColliders = new();

    /// <summary>Hand to the nearest point of the handle's colliders (bounds for concave meshes), else to its origin.</summary>
    private static float HandleSurfaceDistance(ClimbHandle handle, Vector3 pos)
    {
        handle.GetComponentsInChildren(HandleColliders);
        float best = float.PositiveInfinity;
        foreach (var col in HandleColliders)
        {
            if (col == null || !col.enabled)
                continue;
            var p = col is MeshCollider { convex: false }
                ? col.bounds.ClosestPoint(pos)
                : col.ClosestPoint(pos);
            best = Mathf.Min(best, Vector3.Distance(p, pos));
        }
        HandleColliders.Clear();
        return best < float.PositiveInfinity
            ? best
            : Vector3.Distance(pos, handle.transform.position);
    }

    /// <summary>The same item, though a pickup equips a new object for it (CharacterItems.cs:516-534): the ground copy
    /// and the held one share their instance data (Item.cs:191, passed through Player.AddItem, Item.cs:617). A
    /// same-type item handed over has its own.</summary>
    private static bool SameInstance(Item a, Item b) =>
        a == b || (a.data != null && b.data != null && a.data.guid == b.data.guid);

    /// <summary>One Info line whenever the held item leaves the hand, naming the likely cause: this mod's release
    /// (drop or throw), a climb, rope, vine or carry putting it away (CharacterClimbing.cs:569), or the game.</summary>
    private static float releaseDropAt = -10f;

    private static void LogItemLeft(Character c, Item was)
    {
        var d = c.data;
        string cause =
            Time.time - releaseDropAt < 1f ? "a grip release dropped or threw it"
            : d.isClimbingAnything || d.carriedPlayer != null
                ? "a climb, rope, vine or carry put it away"
            : "the game (no release, no climb)";
        Plugin.Log.LogInfo(
            System.FormattableString.Invariant(
                $"[interact] held item left the hand: {(was != null ? was.name : "destroyed")}, {cause}; hanging {d.currentClimbHandle != null}, grips L {LeftHand.Action}/{LeftHand.Grip.Held} R {RightHand.Action}/{RightHand.Grip.Held}"
            )
        );
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
        // While the hand keeps pointing it is aiming a ping (the aim preview invites a long hold): the pending
        // interact waits, so a ping aimed at an item never picks it up.
        // A pointed aim at an item taker with its item in hand is the interact, not a ping: it waits only the
        // finger-gun window (a trigger squeeze in it still pings), never longer.
        else if (
            hand.Action == GripPressAction.Interact
            && !hand.AimedTaker
            && now < hand.InteractAt
            && PingAims()
        )
            hand.InteractAt = GripIntent.DeferPointedInteract(
                hand.InteractAt,
                GripIntent.IsFingerGun(LocalAvatar.ControllerCurls(hand.Left)),
                now,
                FingerGun.DefaultWindow
            );

        if (hand.Grip.Pressed && active && rig.Hand(hand.Left) != null)
            OnGripPress(hand, c, rig, cfg, now);
        else if (hand.Grip.Released)
            OnGripRelease(hand, c, rig, cfg, active);
    }

    /// <summary>
    /// Terrain or map geometry within <paramref name="radius"/> of the hand, not counting the held item: some items
    /// carry colliders on those layers, and run 38's held cactus read as rock under its own hand.
    /// </summary>
    private static bool NearRock(Vector3 pos, float radius, Item held)
    {
        int n = Physics.OverlapSphereNonAlloc(
            pos,
            radius,
            Overlap,
            HelperFunctions.terrainMapMask,
            QueryTriggerInteraction.Ignore
        );
        bool rock = false;
        for (int i = 0; i < n; i++)
        {
            var col = Overlap[i];
            Overlap[i] = null;
            if (!rock && col != null && (held == null || col.GetComponentInParent<Item>() != held))
                rock = true;
        }
        return rock;
    }

    private static Framework.Feature pingFeature;
    private static bool pingLooked;

    /// <summary>The finger-gun ping is live (<c>interact.ping</c> active, <c>Ping.FingerGunPing</c> on), so a pointing
    /// hand is aiming it. The feature is looked up once, not per frame.</summary>
    private static bool PingAims()
    {
        if (!pingLooked)
        {
            pingLooked = true;
            foreach (var f in Framework.FeatureManager.Features)
            {
                if (f.Id == "interact.ping")
                    pingFeature = f;
            }
        }
        return pingFeature != null
            && pingFeature.State == Framework.FeatureState.Active
            && Ping.PingConfig.Instance.FingerGun;
    }

    /// <summary>
    /// What a pointing hand's aim ray hits within the hand-aimed interact's reach (the game's
    /// <c>Interaction.distance</c> from the camera less the hand's offset, InteractPatches; Interaction.cs:7, :203),
    /// for <see cref="GripContext.Aimed"/>. Item takers are an explicit allow-list of targets that take the interact
    /// only because of the held item: the scout statue with an amulet (Peak/ScoutStatue.cs:251-295) and a skeleton
    /// with the Book of Bones (Skelleton.cs:44-63, :104-107). Everything else is <see cref="AimedTarget.Other"/>, so an
    /// ordinary grip keeps its candidate, climb and fallback order. One raycast per pointing grip press.
    /// </summary>
    private static AimedTarget AimedAt(
        bool left,
        bool pointing,
        VRRig rig,
        Character c,
        InteractConfig cfg,
        out IInteractible aimed
    )
    {
        aimed = null;
        if (!cfg.HandAimedInteract || !pointing)
            return AimedTarget.None;
        // What the game's own interact ray picked last frame from this hand's aim, or the head's while no hand aims
        // (InteractPatches swaps the camera
        // to the aiming hand; vanilla's line check plus its sphere-cast fallback, Interaction.cs:196-260): the same
        // pick the press will get. A single raycast of our own met the statue's rock first when standing against it
        // (vr56: 0.7 m back, hovered statue, not classified).
        var interaction = Interaction.instance;
        if (interaction != null && (AimHand == (left ? 0 : 1) || AimHand < 0))
        {
            var picked = Classify(interaction.currentHovered, c);
            if (picked != AimedTarget.None)
            {
                aimed = interaction.currentHovered;
                return picked;
            }
        }
        var aim = rig.Aim(left);
        var cam = MainCamera.instance;
        if (aim == null || cam == null || interaction == null)
            return AimedTarget.None;
        float reach = interaction.distance - Vector3.Distance(cam.transform.position, aim.position);
        if (
            reach <= 0f
            || !Physics.Raycast(
                aim.position,
                aim.forward,
                out var hit,
                reach,
                HelperFunctions.GetMask(HelperFunctions.LayerType.AllPhysical),
                QueryTriggerInteraction.Collide
            )
        )
            return AimedTarget.None;
        var target = hit.collider.GetComponentInParent<IInteractible>();
        var kind = Classify(target, c);
        if (kind != AimedTarget.None)
            aimed = target;
        return kind;
    }

    private static AimedTarget Classify(IInteractible target, Character c)
    {
        if (
            target == null
            || (target is Object unityObject && unityObject == null)
            || ReferenceEquals(target, c.data.currentItem)
            || !target.IsInteractible(c)
        )
            return AimedTarget.None;
        return target is Peak.ScoutStatue or Skelleton ? AimedTarget.ItemTaker : AimedTarget.Other;
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
        hand.Armed = ReleaseArming.OnPress();
        bool holding = Holding(c, out var item);
        bool thisHolds = holding && LocalHold.ItemInLeftHand == hand.Left;

        var ctx = new GripContext
        {
            PhysicalGrab = cfg.PhysicalGrab,
            PhysicalClimb = cfg.PhysicalClimb,
            HoldingItem = holding,
            ThisHandHolds = thisHolds,
            HeldHandedness = holding ? Hold.Profile.Handedness : Handedness.OneHanded,
            NearHeldItem = holding && !thisHolds && NearItem(item, pos, HeldSurfaceReach),
            AtSecondGrip = holding && !thisHolds && AtSecondGrip(hand.Left, pos),
            NearClimbable =
                cfg.PhysicalClimb && NearRock(pos, cfg.ClimbRadius, holding ? item : null),
            BodyClimbing = c.data.isClimbing || !c.data.isGrounded,
        };
        ctx.Pointing = GripIntent.IsPointing(LocalAvatar.ControllerCurls(hand.Left));
        ctx.Aimed = AimedAt(hand.Left, ctx.Pointing, rig, c, cfg, out var aimedTarget);
        // Right at the held item's second grip, within the grab radius: the one place a pointing hand still joins it.
        ctx.AtSecondGripTight =
            holding && !thisHolds && AtSecondGrip(hand.Left, pos, cfg.GrabRadius);
        var kind = HoldKind.Normal;
        probeHit = probeRefused = false;
        if (ctx.NearClimbable)
        {
            bool holdOk = ProbeHold(rig, pos, t, cfg.ClimbRadius, out kind);
            if (!holdOk && !c.data.isClimbing)
                ctx.NearClimbable = false;
            ctx.HoldConfirmed = holdOk && probeHit;
        }
        IInteractible target = null;
        if (cfg.PhysicalGrab)
            ctx.Candidate = FindCandidate(c, Eye(rig), pos, t.forward, cfg.GrabRadius, out target);
        ctx.CandidateIsAimed = target != null && ReferenceEquals(target, aimedTarget);
        ctx.CandidateIsHandle = target is ClimbHandle;
        if (target is ClimbHandle near)
        {
            // To the handle's surface, not its origin (which can sit at the rock face, VR run 60).
            ctx.HandleDistance = HandleSurfaceDistance(near, pos);
            ctx.RockDistance = probeHit
                ? Vector3.Distance(pos, probePoint)
                : float.PositiveInfinity;
        }
        ctx.OneHanded = VRInput.OneHanded;

        var action = GripIntent.OnPress(ctx);
        IGripExtension extension = null;
        var hook = new GripHookContext(c, hand.Left, pos, holding ? item : null, thisHolds);
        if (action != GripPressAction.SecondHand)
        {
            extension = GripHooks.Claim(hook);
            ctx.ExtensionClaim = extension != null;
            action = GripIntent.OnPress(ctx);
        }
        // The curls the grip's intent read (finger gun, point), so a pose left over from another step shows.
        var curls = LocalAvatar.ControllerCurls(hand.Left);
        LastGripNote = System.FormattableString.Invariant(
            $"{(hand.Left ? "L" : "R")} grip {action}: near climbable {ctx.NearClimbable} (probe hit {probeHit}, refused {probeRefused}), pointed at a taker {ctx.PointedAtTaker}, body climbing {ctx.BodyClimbing}, eye-hand {Vector3.Distance(Eye(rig), pos):0.00} m, candidate {ctx.Candidate} ({lastReachable} in reach, {lastHidden} out of sight), curls thumb {curls.Thumb:0.00} index {curls.Index:0.00} middle {curls.Middle:0.00}, pointing {ctx.Pointing}, second grip {(ctx.AtSecondGripTight ? "tight" : ctx.AtSecondGrip ? "loose" : "no")}, candidate aimed {ctx.CandidateIsAimed}{(ctx.CandidateIsHandle ? $", handle {ctx.HandleDistance:0.00} m vs rock {ctx.RockDistance:0.00} m" : "")}"
        );
        Diagnostics.DebugLog.GripNote(hand.Left, LastGripNote);
        switch (action)
        {
            case GripPressAction.Extension:
                hand.Extension = extension;
                extension.BeginPress(hook);
                break;
            case GripPressAction.GrabItem:
                hand.Target = target;
                // A free hand's grab while another item is held goes to the pockets: the held hand stays as it is.
                if (!holding)
                {
                    pendingLeft = hand.Left;
                    pendingTime = now;
                }
                BeginPickup(hand, c, target, now, holding);
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
                // The fall this grab stops, read before the climb start resets the body's motion.
                float fall = c.data.isClimbing ? 0f : recentFall;
                LastCatchFall = fall;
                if (StartClimb(c, rig, pos, cfg))
                    CatchPulse(hand.Left, kind, fall, cfg);
                else
                {
                    action = GripPressAction.None;
                    DenyClimb(c, rig, cfg, hand.Left, pos);
                }
                break;
            case GripPressAction.Regrip:
                Haptics.Pulse(hand.Left, 0.2f, 0.03f);
                // The item hand closed on a piton or handle: it keeps its item and does not hang. Say so, and show
                // the handle as the free hand's (HandsFull, the held item red).
                if (ctx.CandidateIsHandle && target is ClimbHandle handle)
                    Affordance.DenyAtHand(
                        hand.Left,
                        DenyReason.HandsFull,
                        Affordance.Hints ? DenyHint.On(handle.transform) : default
                    );
                break;
            case GripPressAction.Interact:
                // A finger gun (pointing, thumb up) may be the start of a ping: wait out its trigger window first.
                // Pointing with the thumb resting is a pointed interact, at once (external review #13).
                hand.InteractAt = GripIntent.PointedInteractWaits(
                    LocalAvatar.ControllerCurls(hand.Left),
                    PingAims()
                )
                    ? now + FingerGun.DefaultWindow
                    : now;
                break;
        }
        hand.Action = action;
        hand.AimedTaker = action == GripPressAction.Interact && ctx.PointedAtTaker;
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
        // Closed short of a climbable wall: the hand stops at arm's length, so rock just past the grab range would
        // otherwise get no cue at all (VR run 64). The grab itself stays capped; this only says "too far".
        else if (
            cfg.PhysicalClimb
            && !ctx.NearClimbable
            && action is GripPressAction.None or GripPressAction.Interact
            && (Interaction.instance == null || Interaction.instance.currentHovered == null)
            && ReachAttempt(hand, c, rig, ctx, holding && thisHolds, pos, now)
            && RockJustBeyond(rig, pos, t, cfg, out var rock)
        )
            Affordance.Deny(
                hand.Left,
                rock,
                DenyReason.OutOfReach,
                DenyHint.Range(pos, rock, cfg.ClimbRadius)
            );
    }

    /// <summary>
    /// The grip is a real climb attempt (<see cref="TraversalRefusal.ReachAttempt"/>): not pointing, no item in this
    /// hand or let go within 0.5 s, no item use or interact hold running, and the hand reaching toward the wall or
    /// held out at full stretch.
    /// </summary>
    private static bool ReachAttempt(
        Hand hand,
        Character c,
        VRRig rig,
        in GripContext ctx,
        bool thisHolds,
        Vector3 pos,
        float now
    )
    {
        var held = c.data.currentItem;
        bool casting =
            (
                held != null
                && (held.isUsingPrimary || held.isUsingSecondary || held.castProgress > 0f)
            )
            || (
                Interaction.instance != null && Interaction.instance.currentHeldInteractible != null
            );
        var eye = Eye(rig);
        var dir = pos - eye;
        float eyeToHand = dir.magnitude;
        float approach =
            eyeToHand > 1e-3f ? Vector3.Dot(HandVelocity(hand, rig), dir / eyeToHand) : 0f;
        return TraversalRefusal.ReachAttempt(
            ctx.Pointing,
            thisHolds || now - hand.ItemLeftAt < 0.5f,
            casting,
            approach,
            eyeToHand
        );
    }

    /// <summary>
    /// Climbable rock along the reach (eye through hand) past the grab radius but within
    /// <see cref="TraversalRefusal.ReachProbe"/> beyond it (<see cref="TraversalRefusal.ReachBand"/>). Only at a
    /// grip press: one ray.
    /// </summary>
    private static bool RockJustBeyond(
        VRRig rig,
        Vector3 handPos,
        Transform hand,
        InteractConfig cfg,
        out Vector3 rock
    )
    {
        rock = default;
        var dir = handPos - Eye(rig);
        dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : hand.forward;
        if (
            !Physics.Raycast(
                handPos,
                dir,
                out var hit,
                cfg.ClimbRadius + TraversalRefusal.ReachProbe,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            )
        )
            return false;
        bool climbable =
            hit.collider.TryGetComponent<ClimbModifierSurface>(out _)
            || ClimbRules.GrabAngleOk(Vector3.Angle(hit.normal, Vector3.up));
        if (
            !climbable
            || TraversalRefusal.ReachBand(
                hit.distance,
                cfg.ClimbRadius,
                TraversalRefusal.ReachProbe
            ) == DenyReason.None
        )
            return false;
        rock = hit.point;
        return true;
    }

    /// <summary>
    /// A pickup grip, before the game decides (Item.Interact, Item.cs:578-596). A pickup it would ignore gets its
    /// refusal now: no free slot, or a non-pocketable item while climbing (it only falls, :584-588). Otherwise the
    /// success tick waits until the item is really taken (<see cref="ConfirmPickup"/>), so the hand never feels a
    /// grab that did not happen. A ground backpack opens its wheel instead (Backpack.cs:32-35): ticked at once.
    /// </summary>
    private static void BeginPickup(
        Hand hand,
        Character c,
        IInteractible target,
        float now,
        bool pocketing
    )
    {
        hand.Pocketing = pocketing;
        hand.Confirmed = true;
        hand.Retried = false;
        hand.RetryAt = now + PickupRetry;
        // A worn backpack's slot is the one HasEmptySlot checks for a Backpack (Player.cs:278); the ground one is no pickup.
        if (target is Backpack)
            Haptics.Pulse(hand.Left, 0.6f, 0.05f);
        else if (target is Item wanted && c.player != null && !c.player.HasEmptySlot(wanted.itemID))
            Affordance.Deny(hand.Left, wanted.Center(), DenyReason.PocketsFull, default, wanted);
        else if (target is Item loose && c.data.isClimbing && !loose.UIData.canPocket)
            Affordance.Deny(hand.Left, loose.Center(), DenyReason.NotWhileClimbing, default, loose);
        else
        {
            hand.Confirmed = false;
            if (pocketing)
                pocketingUntil = now + 3f;
        }
    }

    /// <summary>Until when the master's answer to a free hand's pickup counts as a pocketing one.</summary>
    private static float pocketingUntil = -10f;

    /// <summary>
    /// A free hand's pocketing pickup is pending: true once, for the game's answer to it, which must then not equip
    /// the new item over the held one (<see cref="InteractPatches.KeepHeldOnPocket"/>).
    /// </summary>
    internal static bool TakePocketing()
    {
        if (!VRSession.Active || Time.time > pocketingUntil)
            return false;
        pocketingUntil = -10f;
        return true;
    }

    /// <summary>
    /// Per frame while a pickup grip waits: the success tick once the item is in hand or gone from the world (pocketed,
    /// Item.cs:589). Returns true while this grip should keep pressing interact; one release-and-press after
    /// <see cref="PickupRetry"/> retries a grab the equip cooldown swallowed.
    /// </summary>
    private static bool ConfirmPickup(Hand hand, Character c, Item grabbed, float now)
    {
        bool taken =
            grabbed == null
            || grabbed == c.data.currentItem
            || !grabbed.gameObject.activeInHierarchy;
        if (taken)
        {
            if (!hand.Confirmed)
                PickupDone(hand);
            hand.Confirmed = true;
            return false;
        }
        if (!hand.Confirmed && !hand.Retried && now >= hand.RetryAt)
        {
            hand.Retried = true;
            return false;
        }
        return true;
    }

    /// <summary>
    /// The grip touched climbable rock but the game's climb start refused it (CharacterClimbing.cs:534-562): the hold
    /// is beyond its reach from the camera (1.25 m, :553; decision D3 keeps that cap for the hand-cast grab), a
    /// climber out of stamina cannot grab again within 1 s of the last grab (:562), and a condor's prey cannot grab
    /// at all (:562). Says which, and for reach marks where the head's reach ends.
    /// </summary>
    private static void DenyClimb(
        Character c,
        VRRig rig,
        InteractConfig cfg,
        bool left,
        Vector3 handPos
    )
    {
        if (!c.refs.climbing.CanClimb())
            return;
        var eye = Eye(rig);
        var wall = probeHit ? probePoint : handPos;
        if (c.data.carrierCondor)
        {
            Affordance.Deny(
                left,
                wall,
                DenyReason.Carried,
                Affordance.Hints ? NearestHandle(handPos) : default
            );
            return;
        }
        float distance = Vector3.Distance(eye, wall);
        if (distance > TraversalRefusal.GameWallReach)
            Affordance.Deny(
                left,
                wall,
                DenyReason.OutOfReach,
                DenyHint.Range(eye, wall, ClimbReach)
            );
        else if (
            distance <= ClimbReach
            && SinceLastClimbStarted != null
            && SinceLastClimbStarted(c.refs.climbing) <= 1f
            && OutOfStamina(c)
        )
            Affordance.DenyStamina(left, wall);
    }

    /// <summary><c>Character.OutOfStamina</c> (Character.cs:1680-1690, internal): no stamina and no bonus stamina.</summary>
    internal static bool OutOfStamina(Character c) =>
        !c.infiniteStam && c.data.currentStamina < 0.005f && c.data.extraStamina < 0.001f;

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
        if (c.data.currentItem != null && LocalHold.ItemInLeftHand == hand.Left)
            hand.ItemLeftAt = Time.time;

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
                    action != GripPressAction.Extension && GripHooks.HeldRelease(hook),
                    hand.Armed
                );
                // Item.ItemUIData.canThrow (Item.cs:71; the flat game hides its throw prompt, GUIManager.cs:1183), and
                // Item.progress (Item.cs:346): mid-cast, CharacterItems.DoDropping (CharacterItems.cs:254-258) keeps it.
                var refusal = GripIntent.ReleaseRefusal(
                    result,
                    item.UIData.canThrow,
                    item.progress
                );
                if (refusal != DenyReason.None)
                {
                    result = GripReleaseAction.Keep;
                    Affordance.DenyAtHand(hand.Left, refusal);
                }
                if (result == GripReleaseAction.Throw)
                {
                    ThrowVelocity = ReleaseVelocity(handVelocity, c) * cfg.ThrowScale;
                    LastThrowHand = handVelocity;
                    LastThrowBody =
                        c.refs.hip != null ? c.refs.hip.Rig.linearVelocity : Vector3.zero;
                    throwFrame = Time.frameCount;
                    ThrowCount++;
                    dropPending = true;
                    releaseDropAt = Time.time;
                    Haptics.Pulse(hand.Left, 0.5f, 0.05f);
                }
                else if (result == GripReleaseAction.Drop)
                {
                    dropPending = true;
                    releaseDropAt = Time.time;
                }
                break;
        }
    }

    private static Hand Other(Hand hand) => hand.Left ? RightHand : LeftHand;

    /// <summary>A two-handed item counts as held in both hands: the free hand joined it, or one-handed play has no
    /// second hand to join it with (Accessibility.OneHanded), so its use is never refused for want of one.</summary>
    private static bool SecondHand => LocalHold.TwoHand || VRInput.OneHanded;

    /// <summary>One-handed: this trigger pull started with the stick clicked in, so it is the secondary use.</summary>
    private static bool secondaryChord;

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

    /// <summary>How close (metres) to the held item's surface a free hand counts as on it.</summary>
    private const float HeldSurfaceReach = 0.2f;

    /// <summary>How close (metres) to the held item's off-hand grip a free hand takes it.</summary>
    private const float SecondGripReach = 0.25f;

    /// <summary>
    /// The hand is within <paramref name="radius"/> of the item's solid colliders, measured to their real surface. An
    /// axis-aligned box around a long item held at a slant, or a large trigger on it, reached well past the item:
    /// VR run 51's grab on a chain 0.45 m from the held launcher joined the launcher.
    /// </summary>
    private static bool NearItem(Item item, Vector3 pos, float radius)
    {
        float r2 = radius * radius;
        bool any = false;
        foreach (var col in item.colliders)
        {
            if (col == null || !col.enabled || col.isTrigger)
                continue;
            any = true;
            // Collider.ClosestPoint is exact for primitives and convex meshes only; a concave mesh uses its bounds.
            var closest = col is MeshCollider { convex: false }
                ? col.bounds.ClosestPoint(pos)
                : col.ClosestPoint(pos);
            if ((closest - pos).sqrMagnitude <= r2)
                return true;
        }
        return !any && (item.Center() - pos).sqrMagnitude <= r2;
    }

    /// <summary>The free hand is at the held item's grip for it (<c>Hand_L</c> for the left hand, <c>Hand_R</c> for
    /// the right, ItemHoldInfo); items without one have no second grip point.</summary>
    private static bool AtSecondGrip(bool left, Vector3 pos, float reach = SecondGripReach)
    {
        var grip = left ? Hold.GripLeft : Hold.GripRight;
        return grip != null && (grip.position - pos).sqrMagnitude <= reach * reach;
    }

    /// <summary>Where the game's own aim rays start: the camera, or the tracked head before it exists. While the head
    /// is inside a wall, rig.camera keeps the camera on the body's side of it (RigDriver.CheckHeadInWall), so these
    /// rays cannot start behind geometry.</summary>
    private static Vector3 Eye(VRRig rig) =>
        MainCamera.instance != null ? MainCamera.instance.transform.position : rig.Head.position;

    /// <summary>What a grip of the <paramref name="left"/> hand would take right now: the same search as a grip press
    /// (<see cref="FindCandidate"/>), for the pre-grip glow (ui.spatial GripGlow). Null when nothing, or physical grab
    /// is off.</summary>
    internal static IInteractible GripPreview(bool left)
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        var cfg = InteractConfig.Instance;
        var t = rig != null ? rig.Hand(left) : null;
        if (c == null || t == null || rig.Head == null || !cfg.PhysicalGrab)
            return null;
        FindCandidate(c, Eye(rig), t.position, t.forward, cfg.GrabRadius, out var target);
        return target;
    }

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
        lastReachable = lastHidden = 0;
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
            // rock fin does not grab what lies behind it. Seen when any of the point nearest the hand, the centre or
            // the top is clear (GrabSelector.SightPoints): an item partly sunk into uneven ground hides the first two
            // (VR run 50's vr.10, vr.11 and vr.32 ground pickups failed with the hand on the Apple).
            var bounds = col.bounds;
            var point = bounds.ClosestPoint(pos);
            GrabSelector.SightPoints(point.N(), bounds.center.N(), bounds.extents.N(), SightPoints);
            bool seen = false;
            for (int s = 0; s < GrabSelector.SightPointCount && !seen; s++)
                seen = Visible(eye, SightPoints[s].U(), it);
            // The hand already on it sees it, and the eye sees the hand (no fin between): VR mp19's compass 3 cm from
            // the hand on rocky ground hid from the stooped eye on every point.
            if (!seen)
                seen = GrabSelector.Seen(false, Visible(pos, point, it), Visible(eye, pos, it));
            lastReachable++;
            if (!seen)
            {
                lastHidden++;
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

    private static readonly NVector3[] SightPoints = new NVector3[GrabSelector.SightPointCount];

    /// <summary>The last grab search's interactible colliders in reach, and how many of them the eye could not see
    /// (for <see cref="LastGripNote"/>).</summary>
    private static int lastReachable,
        lastHidden;

    /// <summary>No terrain or map geometry between the eye and <paramref name="point"/>, stopping 5 cm short so a
    /// resting item's floor does not count; the target's own map-layer collider (luggage, kiosks) does not block.</summary>
    private static bool Visible(Vector3 eye, Vector3 point, IInteractible it)
    {
        var toPoint = point - eye;
        float len = toPoint.magnitude;
        if (len <= 0.05f)
            return true;
        var block = HelperFunctions
            .LineCheck(
                eye,
                eye + toPoint * ((len - 0.05f) / len),
                HelperFunctions.LayerType.TerrainMap
            )
            .collider;
        return block == null || block.GetComponentInParent<IInteractible>() == it;
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

        if (cfg.ClimbFromHand)
            return StartClimbFromHand(c, rig, handPos, cfg);
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
    /// Decision D3: the game's climb ray starts at <c>MainCamera.instance</c> (CharacterClimbing.cs:541), so for this one
    /// call the camera sits just behind the hand and casts straight into the hold under it. The hold must still lie
    /// within the game's 1.25 m of the real camera (<see cref="TraversalRefusal.WallReach"/>). <c>StartClimbRpc</c>
    /// carries only the hit point and normal (:575), so other players see a normal climb.
    /// </summary>
    private static bool StartClimbFromHand(
        Character c,
        VRRig rig,
        Vector3 handPos,
        InteractConfig cfg
    )
    {
        var eye = Eye(rig);
        var hold = probeHit ? probePoint : handPos;
        if (
            TraversalRefusal.WallReach(Vector3.Distance(eye, hold), TraversalRefusal.GameWallReach)
            != DenyReason.None
        )
            return false;
        var dir = probeHit ? -probeNormal : handPos - eye;
        if (dir.sqrMagnitude < 1e-4f)
            return false;
        dir.Normalize();
        const float Behind = 0.3f;
        var swap = CameraSwap.Begin(handPos - dir * Behind, Quaternion.LookRotation(dir));
        try
        {
            StartWallClimb(c.refs.climbing, true, dir, false, Behind + cfg.ClimbRadius + 0.15f);
        }
        finally
        {
            swap.End();
        }
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
        if (!CastHold(Eye(rig), handPos, hand.forward, radius, out var hit))
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
        probeNormal = hit.normal;
        probeAngle = Vector3.Angle(hit.normal, Vector3.up);
        bool ok = hasMod || ClimbRules.GrabAngleOk(probeAngle);
        // Ground under the feet is not a climbing attempt: only steep slopes and ceilings count as refused.
        probeRefused = !ok && probeAngle >= SlopeRefusedFrom;
        return ok;
    }

    /// <summary>The surface under the hand: a short ray through the hand from the eye, then along the hand.</summary>
    private static bool CastHold(
        Vector3 eye,
        Vector3 handPos,
        Vector3 handForward,
        float radius,
        out RaycastHit hit
    )
    {
        var dir = handPos - eye;
        dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : handForward;
        return Physics.Raycast(
                handPos - dir * 0.15f,
                dir,
                out hit,
                0.3f + radius,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            )
            || Physics.Raycast(
                handPos - handForward * 0.1f,
                handForward,
                out hit,
                0.2f + radius,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            );
    }

    /// <summary>
    /// The "can grab here" cue that replaces the game's climb reticle (<c>sinceCanClimb</c>, CharacterClimbing.cs:566):
    /// an open, empty hand within <c>ClimbGrabRadius</c> of rock the game would let it grab, within reach, gets the
    /// chalk ring (<see cref="Climb.ClimbHover"/>) and one contact tick as it first meets the rock. Per frame, two
    /// casts at most, no allocation.
    /// </summary>
    private static void UpdateHover(
        Hand hand,
        Character c,
        VRRig rig,
        InteractConfig cfg,
        float now
    )
    {
        var t = rig.Hand(hand.Left);
        ref var state = ref VRInput.Hand(hand.Left);
        bool holdsHere = c.data.currentItem != null && LocalHold.ItemInLeftHand == hand.Left;
        if (
            !cfg.PhysicalClimb
            || !cfg.ClimbHoverRing
            || t == null
            || !state.Tracked
            || hand.Grip.Held
            || hand.Action != GripPressAction.None
            || holdsHere
            || c.data.carrierCondor
            || !c.refs.climbing.CanClimb()
        )
        {
            Climb.ClimbHover.Hide(hand.Left);
            return;
        }
        var pos = t.position;
        var eye = Eye(rig);
        if (
            !Physics.CheckSphere(
                pos,
                cfg.ClimbRadius,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            )
            || !CastHold(eye, pos, t.forward, cfg.ClimbRadius, out var hit)
            || TraversalRefusal.WallReach(
                Vector3.Distance(eye, hit.point),
                TraversalRefusal.GameWallReach
            ) != DenyReason.None
            || !(
                hit.collider.TryGetComponent<ClimbModifierSurface>(out _)
                || ClimbRules.GrabAngleOk(Vector3.Angle(hit.normal, Vector3.up))
            )
        )
        {
            Climb.ClimbHover.Hide(hand.Left);
            return;
        }
        if (Climb.ClimbHover.Show(hand.Left, hit.point, hit.normal, now))
            Affordance.Contact(hand.Left);
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
    /// vanilla gate (CharacterClimbing.cs:122, <see cref="TraversalRefusal.Lunge"/>) so a refused jump does not
    /// stretch the grace, and says why: still recovering from the last jump (TooSoon) or out of stamina.
    /// </summary>
    private static void TryLunge(Hand hand, Character c, VRRig rig, InteractConfig cfg)
    {
        var d = c.data;
        if (!d.isClimbing)
            return;
        var stick = ClimbInput.Lunge(
            HandVelocity(hand, rig).N(),
            d.climbNormal.N(),
            cfg.LungeSpeed
        );
        if (stick == System.Numerics.Vector2.Zero)
            return;
        // A fling, not a reach: let go at or above the shoulder (LungeIntent), then confirmed once the arm is seen
        // still rising a moment later (ConfirmLunge).
        float below = VRInput.HeadPosition.y - VRInput.Hand(hand.Left).Position.y;
        if (!LungeIntent.AtRelease(HandVelocity(hand, rig).y, cfg.LungeSpeed, below))
            return;
        lungeHand = hand;
        lungeStick = stick;
        lungeCheckAt = Time.time + LungeIntent.ConfirmDelay;
        Grace.Extend(LungeIntent.ConfirmDelay + 0.05f);
    }

    private static Hand lungeHand;
    private static System.Numerics.Vector2 lungeStick;
    private static float lungeCheckAt = -10f;

    /// <summary>
    /// <see cref="LungeIntent.ConfirmDelay"/> after a fling-like release: the arm still rising and the grip still
    /// open, so it was a throw and not a reach arriving at its next hold. Only then is it refused or turned into the
    /// game's climb jump.
    /// </summary>
    private static void ConfirmLunge(Character c, VRRig rig, InteractConfig cfg, float now)
    {
        if (lungeCheckAt < 0f || now < lungeCheckAt)
            return;
        lungeCheckAt = -10f;
        var hand = lungeHand;
        lungeHand = null;
        var d = c.data;
        if (hand == null || !d.isClimbing)
            return;
        if (!LungeIntent.Confirm(HandVelocity(hand, rig).y, cfg.LungeSpeed, hand.Grip.Held))
            return;
        var stick = lungeStick;
        switch (TraversalRefusal.Lunge(d.sinceClimbJump, d.outOfStaminaFor))
        {
            case DenyReason.NoStamina:
                Affordance.DenyStamina(hand.Left);
                return;
            case DenyReason.TooSoon:
                Affordance.DenyAtHand(hand.Left, DenyReason.TooSoon);
                return;
        }
        lungeInput = new Vector2(stick.X, stick.Y);
        lungePending = true;
        Climb.ClimbCues.Sound(
            Climb.ClimbCue.Whoosh,
            ClimbSound.Whoosh,
            rig.Hand(hand.Left) != null ? rig.Hand(hand.Left).position : c.Center
        );
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
            Texture.Reset();
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
        ConfirmLunge(c, rig, cfg, now);
        if (physicalClimb && !c.data.isClimbing)
        {
            // The game ended the climb (top-out, jump, stamina, ragdoll): grips must close again to climb.
            bool mantled =
                climbInput.y > 0.3f
                && c.data.sinceClimbJump > 0.5f
                && c.data.outOfStaminaFor <= 0f
                && TryStartMantle(c, now);
            if (!mantled && c.data.sinceClimbJump > 0.5f)
            {
                TornOff(LeftHand, now);
                TornOff(RightHand, now);
            }
            // Why the game let go, once per ended climb (VR run 60's vr.15 lost its climb mid-pull with no torn-off cue).
            // The face 0.5 m above the body: while the stick (the arm pull) moves, the game lets go of any face
            // flatter than 50 degrees from up (SampleWall, CharacterClimbing.cs:458-470), its top-out.
            float faceAbove = Physics.Raycast(
                c.Center + Vector3.up * 0.5f,
                -c.data.climbNormal,
                out var above,
                1.5f,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            )
                ? Vector3.Angle(above.normal, Vector3.up)
                : -1f;
            Plugin.Log.LogInfo(
                System.FormattableString.Invariant(
                    $"[interact.climb] climb ended by the game: grips L {LeftHand.Action}/{LeftHand.Grip.Held} R {RightHand.Action}/{RightHand.Grip.Held}, stick y {climbInput.y:0.00}, mantled {mantled}, since jump {c.data.sinceClimbJump:0.00} s, out of stamina {c.data.outOfStaminaFor:0.00} s, ragdoll control {c.data.currentRagdollControll:0.00}, grounded {c.data.isGrounded}, use released {c.input.usePrimaryWasReleased} (forced off while climbing), face 0.5 m up {faceAbove:0} deg from up"
                )
            );
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
        DenyPull(TraversalRefusal.Pull(target.Y, slideOnly, outOfStamina), leftGrip, rightGrip);

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
                    Haptics.Pulse(true, amplitude, 0.04f, HapticPriority.Ambient);
                if (rightGrip)
                    Haptics.Pulse(false, amplitude, 0.04f, HapticPriority.Ambient);
                return;
            }
        }
        SurfaceFeel(c, cfg, leftGrip, rightGrip);
    }

    /// <summary>
    /// C9: the rock under the climber, felt while holding it (<see cref="SurfaceTexture"/>): the current
    /// <c>climbMod</c> (CharacterClimbing.cs:140-143) and the wind's <c>slippy</c> (WindChillZone.cs:211). Quiet while
    /// the stamina beat speaks.
    /// </summary>
    private static void SurfaceFeel(Character c, InteractConfig cfg, bool leftGrip, bool rightGrip)
    {
        if (!cfg.ClimbSurfaceFeel || !(leftGrip || rightGrip))
            return;
        var mod = c.data.climbMod;
        bool hasMod = mod != null;
        var kind = ClimbRules.Kind(
            hasMod,
            hasMod && mod.onlySlideDown,
            hasMod && (mod.applyStatus || mod.applyPetrify),
            hasMod ? mod.staminaUsageMultiplier : 1f
        );
        if (!Texture.TryTick(kind, c.data.slippy, Time.time, out var p))
            return;
        if (leftGrip)
            Climb.ClimbCues.Pulse(Climb.ClimbCue.Texture, true, p.Amplitude, p.Seconds);
        if (rightGrip)
            Climb.ClimbCues.Pulse(Climb.ClimbCue.Texture, false, p.Amplitude, p.Seconds);
    }

    /// <summary>
    /// C4: a grab that stops a fall (the game turns it into a slide down the wall from the same
    /// <c>avarageVelocity</c>, <c>StartClimbRpc</c>, CharacterClimbing.cs:371-374): the hold ratchets, longer and
    /// harder the faster the fall (<see cref="ClimbFeel.Catch"/>). A clean grab keeps its surface pulse.
    /// </summary>
    /// <summary>The fall speed the last wall grab stopped (0 when already climbing), for the self-test (vr.51b).</summary>
    internal static float LastCatchFall { get; private set; }

    /// <summary>The fastest recent fall, fading at 8 m/s per second: a hand that brushes the rock on the way in brakes
    /// the body before the grip closes (run 36 read 0.7 m/s at the grab after a 3.7 m/s fall), so the catch is felt
    /// from the speed the body actually fell at.</summary>
    private static float recentFall;

    private static void TrackFall(Character c, float dt)
    {
        recentFall = c.data.isClimbingAnything ? 0f : Mathf.Max(FallSpeed(c), recentFall - 8f * dt);
    }

    /// <summary>Downward speed now: the faster of the game's smoothed <c>avarageVelocity</c> and the hip's own, so a
    /// grab mid-fall is not read from a lagging average.</summary>
    private static float FallSpeed(Character c)
    {
        float fall = -c.data.avarageVelocity.y;
        if (c.refs.hip != null)
            fall = Mathf.Max(fall, -c.refs.hip.Rig.linearVelocity.y);
        return fall;
    }

    private static void CatchPulse(bool left, HoldKind kind, float fallSpeed, InteractConfig cfg)
    {
        var p = cfg.ClimbSurfaceFeel ? ClimbFeel.Catch(fallSpeed) : default;
        if (p.Amplitude > 0f)
            Climb.ClimbCues.Pulse(Climb.ClimbCue.Catch, left, p.Amplitude, p.Seconds);
        else
            HoldPulse(left, kind);
    }

    /// <summary>
    /// The pickup really happened: the success tick in hand, or for a free hand's grab the game pocketed, the Stowed
    /// notice at that hand (pin glyph, notice bell, light tick), so the item never vanishes silently.
    /// </summary>
    private static void PickupDone(Hand hand)
    {
        var rig = VRRig.Instance;
        var t = rig != null ? rig.Hand(hand.Left) : null;
        if (hand.Pocketing && t != null)
            Affordance.Inform(hand.Left, t.position, DenyReason.Stowed, default);
        else
            Haptics.Pulse(hand.Left, 0.6f, 0.05f);
    }

    private static readonly Collider[] HandleHits = new Collider[32];

    /// <summary>The "would work" spot for a condor's prey: the nearest free piton or handle within 5 m.</summary>
    private static DenyHint NearestHandle(Vector3 from)
    {
        int n = Physics.OverlapSphereNonAlloc(
            from,
            5f,
            HandleHits,
            Physics.AllLayers,
            QueryTriggerInteraction.Collide
        );
        ClimbHandle best = null;
        float bestSq = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            var handle =
                HandleHits[i] != null ? HandleHits[i].GetComponentInParent<ClimbHandle>() : null;
            HandleHits[i] = null;
            if (handle == null || handle.hanger != null)
                continue;
            float d = (handle.transform.position - from).sqrMagnitude;
            if (d < bestSq)
            {
                bestSq = d;
                best = handle;
            }
        }
        return best != null ? DenyHint.On(best.transform) : default;
    }

    /// <summary>
    /// The game has no mantle: a climber pushing up lets go of the wall when it samples the flat top
    /// (CharacterClimbing.cs:470, then :269) and walks over the lip with the same held stick. An arm pull that
    /// tops out onto a walkable ledge keeps walking toward it for a moment while a grip stays closed.
    /// </summary>
    private static bool TryStartMantle(Character c, float now)
    {
        var into = -c.data.climbNormal;
        into.y = 0f;
        if (into.sqrMagnitude < 1e-4f)
            return false;
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
            return false;
        mantleDir = into;
        mantleUntil = now + MantleSeconds;
        LastMantleAt = UI.Affordances.CueLog.Now;
        // Stepped up: a settle tick on the hands still on the lip, so a push-down on the ledge is learnt as the move.
        if (LeftHand.Grip.Held)
            Affordance.Contact(true);
        if (RightHand.Grip.Held)
            Affordance.Contact(false);
        return true;
    }

    /// <summary>
    /// An upward arm pull the game drops (C3): no stamina, or a slide-only surface. The lower gripping hand, the one
    /// doing the pull, gets the refusal: the stamina cue with the wrist bar, or a slip. Rate-limited by the cue gate.
    /// </summary>
    private static void DenyPull(DenyReason reason, bool leftGrip, bool rightGrip)
    {
        if (reason == DenyReason.None || !(leftGrip || rightGrip))
            return;
        bool left = leftGrip && (!rightGrip || VRInput.Left.Position.y <= VRInput.Right.Position.y);
        if (reason == DenyReason.NoStamina)
            Affordance.DenyStamina(left);
        else
            Affordance.DenyAtHand(left, reason);
    }

    /// <summary>
    /// The game ended a climb this hand was still gripping, and not by a jump or a top-out: an overhang, a ragdoll, a
    /// frog or a pull tore it off (CharacterClimbing.cs:265-271, :626-638). A hard beat now and the cancel feel right
    /// after (<see cref="TornBeat"/>), so it never feels like a voluntary release.
    /// </summary>
    private static void TornOff(Hand hand, float now)
    {
        if (hand.Action == GripPressAction.Climb && hand.Grip.Held)
            TearOff(hand.Left, now);
    }

    /// <summary>A hold was torn from this hand (a wall, rope, vine or handle): the hard beat, then the cancel feel.</summary>
    internal static void TearOff(bool left, float now)
    {
        Haptics.Pulse(left, 1f, 0.06f);
        (left ? LeftHand : RightHand).TornAt = now + 0.1f;
    }

    private static void TornBeat(Hand hand, float now)
    {
        if (hand.TornAt < 0f || now < hand.TornAt)
            return;
        hand.TornAt = -10f;
        Affordance.Cancel(hand.Left);
    }

    private static void UpdateAim(InteractConfig cfg)
    {
        AimHand = -1;
        if (!cfg.HandAimedInteract)
            return;
        bool l = VRInput.Left.Tracked && GripIntent.IsPointing(LocalAvatar.ControllerCurls(true));
        bool r = VRInput.Right.Tracked && GripIntent.IsPointing(LocalAvatar.ControllerCurls(false));
        // The hand whose grip runs the interact aims it, whichever side: a hand holding an item with its finger off
        // the trigger also reads as pointing, and took the aim from the left's pointed grip (vr58: the amulet hand
        // aimed, the statue went unhovered).
        if (l && LeftHand.Action == GripPressAction.Interact)
            AimHand = 0;
        else if (r && RightHand.Action == GripPressAction.Interact)
            AimHand = 1;
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
            hand.Injected = false;
            switch (hand.Action)
            {
                case GripPressAction.GrabItem:
                case GripPressAction.GrabInteractible:
                    // Picked up: a grip still closed on it stops holding interact, so a cook or light gesture gets a
                    // fresh press (Interaction.cs:116 only starts a hold after interact was up).
                    if (hand.Target is Item grabbed && !ConfirmPickup(hand, c, grabbed, now))
                        break;
                    interactDown = hand.Injected = true;
                    GrabTarget ??= hand.Target;
                    break;
                case GripPressAction.Interact:
                    if (now < hand.InteractAt)
                        break;
                    interactDown = hand.Injected = true;
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
        // One-handed, an empty hand's trigger is that hand's (it climbs), and the trigger pulled with the stick
        // clicked in is the secondary use (use on a friend, Item.cs:819; the empty-hand reach, Character.cs:1200),
        // latched for the whole pull.
        bool one = VRInput.OneHanded;
        var primaryHand =
            holding ? (LocalHold.ItemInLeftHand ? LeftHand : RightHand)
            : one && VRInput.LeftHanded ? LeftHand
            : RightHand;
        if (primaryHand.Trigger.Pressed)
            secondaryChord = one && VRInput.Hand(primaryHand.Left).StickClick;
        bool chord = one && secondaryChord;
        // A lethal or warping item takes its gesture, not a bare trigger (interact.items owns the route).
        bool primaryDown =
            cfg.TriggerUse
            && primaryHand.Trigger.Held
            && !chord
            && !(holding && Physical.ItemGestures.TriggerRefused(c.data.currentItem));
        bool secondaryDown =
            cfg.TriggerUse
            && (one ? chord && primaryHand.Trigger.Held : Other(primaryHand).Trigger.Held);

        bool atMouth = false;
        var mouthHand = rig.Hand(primaryHand.Left);
        if (
            cfg.EatAtMouth
            && holding
            && Physical.ItemGestures.AtMouth(c.data.currentItem)
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
            bool handsOk = ItemHoldTable.CanUse(
                Hold.Profile.Handedness,
                SecondHand,
                cfg.RequireTwoHands
            );
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
            // The spool's own tie-off refused: at the hand, so the held spool turns red while the press is held.
            else if (handsOk && UseDenial.SpoolRefused(c, held, out var hint))
                Affordance.DenyAtHand(primaryHand.Left, DenyReason.NoWall, hint);
            else if (handsOk)
                Haptics.Pulse(primaryHand.Left, 0.3f, 0.03f);
        }

        if (
            holding
            && !ItemHoldTable.CanUse(Hold.Profile.Handedness, SecondHand, cfg.RequireTwoHands)
        )
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
