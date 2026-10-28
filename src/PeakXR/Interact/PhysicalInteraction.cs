using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Rig;
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

        /// <summary>How long the last grip outlives lost tracking (<see cref="BlinkHold"/>).</summary>
        public readonly BlinkHold Blink = new();
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

        /// <summary>Until when a climb grip that met the game's regrab cooldown retries while it stays closed.</summary>
        public float ClimbRetryUntil = -10f;

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
    private static float probeAllowance;
    private static HoldVerdict probeVerdict;

    /// <summary>The game's wall-grab ray length from the camera (<c>TryToStartWallClimb</c>, CharacterClimbing.cs:534).</summary>
    private const float GameClimbRay = 1.25f;

    /// <summary>How far behind the hand, toward the eye, the hold probe starts: a tracked hand passes into rock, and a
    /// ray started inside a collider never meets the face it went through.</summary>
    private const float ProbeBack = 0.3f;

    /// <summary>Refused faces flatter than this (degrees from up) get no red ring before the grip: walkable ground
    /// (CharacterMovement.maxAngle 50, CharacterMovement.cs:132) only looks climbable near that limit.</summary>
    private const float HoverTooFlatFrom = 40f;

    /// <summary><c>ClimbModifierSurface.hasAlwaysClimbableRange</c> and <c>alwaysClimbableRange</c>
    /// (ClimbModifierSurface.cs:31-33, internal), resolved in Install.</summary>
    internal static HarmonyLib.AccessTools.FieldRef<ClimbModifierSurface, bool> ModHasRange;
    internal static HarmonyLib.AccessTools.FieldRef<ClimbModifierSurface, float> ModRange;

    /// <summary>Seconds after a pickup grip before an untaken item gets one more interact press: past the game's 0.25 s
    /// equip cooldown (Item.cs:580, CharacterItems.cs:488).</summary>
    private const float PickupRetry = 0.3f;

    /// <summary>What a grabbing hand selected; the interaction raycast patch hands it to the game.</summary>
    public static IInteractible GrabTarget { get; private set; }

    /// <summary>A grip is acting as the interact button (nothing grabbable was in reach).</summary>
    public static bool GripInteractActive { get; private set; }

    /// <summary>Hand whose aim ray replaces the head for interaction this frame: -1 none, 0 left, 1 right.</summary>
    public static int AimHand { get; private set; } = -1;
    public static bool ThrowPending => Time.frameCount - throwFrame <= 2;

    /// <summary>The pending hand throw's release velocity relative to the body (world axes), times
    /// <c>ThrowVelocityScale</c>. InteractPatches.Dropping turns it into the launch.</summary>
    public static Vector3 ThrowHandVelocity { get; private set; }

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
            // VR stopped or the rig is gone (scene load): nothing a hand held carries over.
            foreach (var hand in Hands)
            {
                hand.Action = GripPressAction.None;
                hand.Target = null;
            }
            physicalClimb = false;
            climbInput = Vector2.zero;
            lastItem = null;
            LocalHold.Reset();
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
        RetryClimb(LeftHand, c, rig, cfg, now);
        RetryClimb(RightHand, c, rig, cfg, now);
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
        // By reference: a held item PhotonNetwork.Destroy'd (eaten, used up) is Unity-equal to null, so `==` never
        // saw it leave. A destroyed one still in currentItem counts as gone.
        var item = c.data.currentItem != null ? c.data.currentItem : null;
        if (ReferenceEquals(item, lastItem))
            return;
        if ((object)lastItem != null)
        {
            (LocalHold.ItemInLeftHand ? LeftHand : RightHand).ItemLeftAt = now;
            // A pocketing pickup still pending belongs to the item that left (TakePocketing).
            pocketingUntil = -10f;
        }
        if ((object)lastItem != null && item == null)
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
            best = Mathf.Min(best, Vector3.Distance(SurfacePoint(col, pos), pos));
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

    /// <summary>The hand's controller is gone (<see cref="BlinkHold"/>: disconnected or untracked for long): its grip opens like a slow release, so the held item drops, or stays with
    /// <c>ReleaseToDrop</c> off. The stale throw samples are dropped so it never flies off, and a soft note at the
    /// hand says the grip opened (the controller itself may be off, so no buzz).</summary>
    private static void LostForGood(Hand hand, VRRig rig, bool disconnected)
    {
        hand.Throw.Clear();
        if (!hand.Grip.Held)
            return;
        Plugin.Log.LogInfo(
            $"[interact.physical] {(hand.Left ? "left" : "right")} controller {BlinkHold.Why(disconnected)}: its grip opens"
        );
        var at = rig != null ? rig.Hand(hand.Left) : null;
        if (at != null)
            Affordance.Play(CueSound.Notice, at.position, 0.5f);
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
        // Lost tracking keeps the last grip and trigger, as ClimbHands and BodyInventory do: opening the hand there
        // dropped or threw the item from stale throw samples, then the grip closing again regrabbed it. A latched
        // Toggle grip already reads 1 through it (GripLatch keeps its latch untracked). One-handed play's unused
        // controller is never tracked: it reads open. A controller that is gone (disconnected, or untracked past
        // BlinkHold.MaxSeconds) must not hold forever: it reads open too, a release without a throw. Never while the
        // runtime has focus (dashboard open): multiplayer does not pause, and the player is not playing.
        bool unused = VRInput.OneHanded && hand.Left != VRInput.LeftHanded;
        if (
            hand.Blink.Update(
                state.Tracked || unused,
                state.Disconnected && !unused,
                !VRSession.HasFocus,
                dt
            )
        )
            LostForGood(hand, rig, state.Disconnected);
        bool open = unused || hand.Blink.Expired;
        hand.Grip.Update(
            state.Tracked ? state.Grip
                : open ? 0f
                : hand.Grip.Value,
            dt
        );
        hand.Trigger.Update(
            state.Tracked ? state.Trigger
                : open ? 0f
                : hand.Trigger.Value,
            dt
        );
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
                // A light trigger pull keeps the ping aim up (FingerGun triggerResting), so it keeps waiting too.
                GripIntent.IsFingerGun(LocalAvatar.ControllerCurls(hand.Left))
                    || Ping.PingInput.Aiming(hand.Left),
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

    /// <summary>
    /// The hand whose trigger drives the primary (or <paramref name="secondary"/>) use: the holding hand is primary,
    /// the dominant hand when empty-handed (one-handed: the one controller), the other hand secondary. One-handed, the
    /// same hand does both (secondary is its trigger with the stick clicked in). The one rule for every reader of a
    /// use's hand.
    /// </summary>
    internal static bool UseHandLeft(Character c, bool secondary)
    {
        bool one = VRInput.OneHanded;
        bool primaryLeft =
            c != null && Holding(c, out _) ? LocalHold.ItemInLeftHand : VRInput.LeftHanded;
        return secondary && !one ? !primaryLeft : primaryLeft;
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
            // Vanilla joins the left hand to every item that is not rightHandOnly (CharacterItems.cs:641-651), the
            // parasol too; with its runner on, the free hand on a held parasol is the runner's grip extension, which
            // a join here would pre-empt (extensions are never asked once the press is SecondHand). Only while the
            // runner can claim it (ParasolRunner.ClaimPress: grounded, not climbing): mid-glide the hand joins.
            HeldBothHandsInGame =
                holding
                && !item.rightHandOnly
                && !(
                    Physical.ItemGestures.Parasol != null
                    && Physical.ItemGesturesConfig.Instance.ParasolRunner
                    && c.data.isGrounded
                    && !c.data.isClimbing
                ),
            NearHeldItem = holding && !thisHolds && NearItem(item, pos, HeldSurfaceReach),
            AtSecondGrip = holding && !thisHolds && AtSecondGrip(pos),
            BodyClimbing = c.data.isClimbing || !c.data.isGrounded,
        };
        ctx.Pointing = GripIntent.IsPointing(LocalAvatar.ControllerCurls(hand.Left));
        ctx.Aimed = AimedAt(hand.Left, ctx.Pointing, rig, c, cfg, out var aimedTarget);
        // Right at the held item's second grip, within the grab radius: the one place a pointing hand still joins it.
        ctx.AtSecondGripTight = holding && !thisHolds && AtSecondGrip(pos, cfg.GrabRadius);
        var kind = HoldKind.Normal;
        UseProbe(default);
        if (cfg.PhysicalClimb)
        {
            var hold = ProbeHold(c, rig, pos, t, cfg, holding ? item : null);
            UseProbe(hold);
            kind = hold.Kind;
            // The same verdict drew the chalk ring, so a ringed hold is a grip that climbs. No surface along the reach
            // but rock right at the hand (beside it): the game's own start check decides.
            ctx.NearClimbable =
                hold.Verdict == HoldVerdict.Grab
                || (!hold.Hit && NearRock(pos, cfg.ClimbRadius, holding ? item : null));
            ctx.HoldConfirmed = hold.Verdict == HoldVerdict.Grab;
        }
        IInteractible target = null;
        if (cfg.PhysicalGrab)
            ctx.Candidate = FindCandidate(
                c,
                Eye(rig),
                pos,
                t.Fingers(),
                cfg.GrabRadius,
                out target
            );
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
            $"{(hand.Left ? "L" : "R")} grip {action}: near climbable {ctx.NearClimbable} (probe {probeVerdict}, hit {probeHit}, {probeAngle:0} deg, allowance {probeAllowance:0.00} m), pointed at a taker {ctx.PointedAtTaker}, body climbing {ctx.BodyClimbing}, eye-hand {Vector3.Distance(Eye(rig), pos):0.00} m, candidate {ctx.Candidate} ({lastReachable} in reach, {lastHidden} out of sight), curls thumb {curls.Thumb:0.00} index {curls.Index:0.00} middle {curls.Middle:0.00}, pointing {ctx.Pointing}, second grip {(ctx.AtSecondGripTight ? "tight" : ctx.AtSecondGrip ? "loose" : "no")}, candidate aimed {ctx.CandidateIsAimed}{(ctx.CandidateIsHandle ? $", handle {ctx.HandleDistance:0.00} m vs rock {ctx.RockDistance:0.00} m" : "")}"
        );
        Diagnostics.DebugLog.GripNote(hand.Left, LastGripNote);
        switch (action)
        {
            case GripPressAction.Extension:
                hand.Extension = extension;
                extension.BeginPress(hook);
                break;
            case GripPressAction.GrabItem:
                // A free hand's pocketing grab with every pocket full would land in the game's temporary slot
                // (Player.cs:137-141), which vanilla only fills with the item it then puts in hand (CharacterItems.cs:457).
                if (holding && target is Item stow && stow is not Backpack && !PocketFree(c.player))
                {
                    Affordance.Deny(
                        hand.Left,
                        stow.Center(),
                        DenyReason.PocketsFull,
                        default,
                        stow
                    );
                    hand.Action = GripPressAction.None;
                    hand.AimedTaker = false;
                    return;
                }
                hand.Target = target;
                FreeInteract(hand);
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
                FreeInteract(hand);
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
                    // CanClimb is false for 0.2 s after any climb ends (CharacterClimbing.cs:580-584,
                    // Character.cs:1140-1142): a quick regrab after a drop would be lost, so it waits that out.
                    if (RegrabCooldown(c))
                        hand.ClimbRetryUntil = now + RegrabWait;
                    else
                        DenyClimb(c, rig, hand.Left, pos);
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
        bool unclaimed =
            action is GripPressAction.None or GripPressAction.Interact
            && (Interaction.instance == null || Interaction.instance.currentHovered == null);
        // A wall-like surface the game will not grab, and nothing else took the grip: the hand slides off it.
        if (probeRefused && unclaimed)
            Affordance.Deny(
                hand.Left,
                probePoint,
                probeAngle > 90f ? DenyReason.Overhang : DenyReason.TooFlat
            );
        // On rock past the game's reach from the camera: "too far", with where that reach ends.
        else if (probeVerdict == HoldVerdict.OutOfReach && unclaimed)
            Affordance.Deny(
                hand.Left,
                probePoint,
                DenyReason.OutOfReach,
                DenyHint.Range(Eye(rig), probePoint, GameClimbRay)
            );
        // Closed short of a climbable wall: rock just past the grab reach would otherwise get no cue at all (VR run
        // 64). The faint ring showed it while the hand came in; the grip says "too far" and where the reach ends.
        else if (
            probeVerdict == HoldVerdict.Approach
            && unclaimed
            && ReachAttempt(hand, c, rig, ctx, holding && thisHolds, pos, now)
        )
            Affordance.Deny(
                hand.Left,
                probePoint,
                DenyReason.OutOfReach,
                DenyHint.Range(pos, probePoint, cfg.ClimbRadius + probeAllowance)
            );
    }

    /// <summary>One frame of interact up before a new grab presses it (<see cref="Inject"/>).</summary>
    private static bool interactGap;

    /// <summary>
    /// A grab is starting while the other hand's fallback grip already holds the game's interact on nothing being
    /// held: the game only acts on an interact pressed after a release (<c>readyToInteract</c>, Interaction.cs:109-124),
    /// so that closed fist silently swallowed every grab of this hand. The fist lets go of its interact (it pressed it
    /// on nothing a hold uses) and interact is up for one frame. A running hold interaction (a lid, a pillar) keeps it.
    /// </summary>
    private static void FreeInteract(Hand hand)
    {
        var other = Other(hand);
        var interaction = Interaction.instance;
        if (
            other.Action != GripPressAction.Interact
            || (interaction != null && interaction.currentHeldInteractible != null)
        )
            return;
        other.Action = GripPressAction.None;
        interactGap = true;
    }

    /// <summary>
    /// The game's interact, pressed by a grip that fell back to it (<see cref="GripPressAction.Interact"/>), is picking
    /// up <paramref name="item"/> (InteractPatches.Raycasts, the frame Interaction.DoInteraction calls Item.Interact,
    /// Interaction.cs:116-124). The item belongs to that grip like one it grabbed: it lands in that hand, and opening the
    /// hand drops or throws it. Before this a pickup by the head's or a pointing hand's aim went to the dominant hand
    /// and stayed there until the grip closed once more.
    /// </summary>
    internal static void InteractPicking(Item item)
    {
        var c = Character.localCharacter;
        if (item == null || c == null || c.data.currentItem != null)
            return;
        foreach (var hand in Hands)
        {
            if (hand.Action != GripPressAction.Interact || !hand.Injected)
                continue;
            hand.Target = item;
            pendingLeft = hand.Left;
            pendingTime = Time.time;
            return;
        }
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
            pickupHand = hand;
            pickupItem = target as Item;
            pickupUntil = now + 3f;
        }
    }

    /// <summary>One of the hotbar pockets (<c>Player.itemSlots</c>, Player.cs:17) is empty: the temporary slot does not
    /// count.</summary>
    private static bool PocketFree(Player p)
    {
        if (p == null)
            return true;
        foreach (var slot in p.itemSlots)
        {
            if (slot != null && slot.IsEmpty())
                return true;
        }
        return false;
    }

    /// <summary>Until when the master's answer to a free hand's pickup counts as a pocketing one.</summary>
    private static float pocketingUntil = -10f;

    /// <summary>The grip whose item pickup waits for the master's answer (<see cref="PickupAccepted"/>,
    /// <see cref="PickupDenied"/>), and the ground item it asked for.</summary>
    private static Hand pickupHand;

    private static Item pickupItem;
    private static float pickupUntil = -10f;

    /// <summary>The master accepted this player's pickup (CharacterItems.OnPickupAccepted, CharacterItems.cs:440): the
    /// success tick or Stowed notice now, not when the item merely left the world locally (Item.cs:589).
    /// <paramref name="cue"/> false: the caller gives its own cue.</summary>
    /// <returns>Whether the hand that asked is the left one; null when no hand's pickup was waiting.</returns>
    internal static bool? PickupAccepted(bool cue)
    {
        var hand = pickupHand;
        pickupHand = null;
        pickupItem = null;
        if (hand == null || Time.time > pickupUntil || hand.Confirmed)
            return null;
        hand.Confirmed = true;
        if (cue)
            PickupDone(hand);
        return hand.Left;
    }

    /// <summary>Pockets full while holding something: the game equipped the picked item over the held one
    /// (<see cref="InteractPatches.KeepHeldOnPocket"/>). A notice at the hand that asked, the item in hand changed;
    /// never the success tick. The notice waits while a refusal shows at that hand or one just played there; then the
    /// hand still gets the notice note and a tick, so the swap is never silent.</summary>
    internal static void PickupSwapped(bool left)
    {
        var rig = VRRig.Instance;
        var t = rig != null ? rig.Hand(left) : null;
        if (
            t != null
            && Affordance.Inform(
                left,
                t.position,
                DenyReason.HappenedHere,
                default,
                "Pockets full: swapped into your hand"
            )
        )
            return;
        if (t != null)
            Affordance.Play(CueSound.Notice, t.position, 0.6f);
        Haptics.Pulse(left, 0.3f, 0.05f);
    }

    /// <summary>The master refused the pickup (Item.DenyPickupRPC, Item.cs:598-602, the item back in the world): the
    /// refusal cue at the hand that asked, even if it has opened since.</summary>
    internal static void PickupDenied(Item item)
    {
        var hand = pickupHand;
        if (hand == null || item == null || item != pickupItem || Time.time > pickupUntil)
            return;
        pickupHand = null;
        pickupItem = null;
        hand.Confirmed = true;
        Affordance.Deny(hand.Left, item.Center(), DenyReason.PocketsFull, default, item);
    }

    /// <summary>
    /// A free hand's pocketing pickup is pending: true once, for the game's answer to it, which must then not equip
    /// the new item over the held one (<see cref="InteractPatches.KeepHeldOnPocket"/>).
    /// </summary>
    internal static bool TakePocketing()
    {
        var c = Character.localCharacter;
        // With nothing in hand there is nothing to keep: a stale window must not pocket an ordinary pickup.
        bool take =
            VRSession.Active
            && Time.time <= pocketingUntil
            && c != null
            && c.data.currentItem != null;
        pocketingUntil = -10f;
        return take;
    }

    /// <summary>
    /// Per frame while a pickup grip waits: the success tick once the item is in hand or gone from the world (pocketed,
    /// Item.cs:589). Returns true while this grip should keep pressing interact; one release-and-press after
    /// <see cref="PickupRetry"/> retries a grab the equip cooldown swallowed.
    /// </summary>
    private static bool ConfirmPickup(Hand hand, Character c, Item grabbed, float now)
    {
        // Gone from the world (destroyed after the master's answer) or in hand: done. The tick itself comes from the
        // master's answer (PickupAccepted); this only stops pressing.
        if (grabbed == null || grabbed == c.data.currentItem)
        {
            if (!hand.Confirmed && grabbed != null)
                PickupDone(hand);
            hand.Confirmed = true;
            return false;
        }
        // Hidden while the master decides (Item.cs:589): no press, no retry and no tick until it answers.
        if (!grabbed.gameObject.activeInHierarchy)
            return false;
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
    private static void DenyClimb(Character c, VRRig rig, bool left, Vector3 handPos)
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
                DenyHint.Range(eye, wall, GameClimbRay)
            );
        else if (
            distance <= GameClimbRay
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
        // A fallback grip whose interact picked up the item now in this hand lets go of it like a grab
        // (InteractPicking): never a pointed grip's own held item handed to a statue or skeleton.
        if (
            action == GripPressAction.Interact
            && hand.Target is Item picked
            && c.data.currentItem != null
            && SameInstance(picked, c.data.currentItem)
        )
            action = GripPressAction.GrabItem;
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
                // The primary hand opens while the other hand still grips the item as its second hand: the item
                // stays held and passes to that hand, which becomes the primary (no drop, throw or holster).
                if (
                    thisHolds
                    && HoldHands.HandOff(
                        LocalHold.TwoHand,
                        Other(hand).Grip.Held,
                        Other(hand).Action == GripPressAction.SecondHand
                    )
                )
                {
                    HandOff(Other(hand), item);
                    break;
                }
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
                    ThrowHandVelocity = handVelocity * cfg.ThrowScale;
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

    /// <summary>
    /// The held item passes to <paramref name="to"/>, the hand that kept gripping it as the second hand, which is its
    /// primary from now on: use, release to drop, throw and holster follow that hand, and the avatar eases the item
    /// onto its grip (AvatarPoseDriver.TrackHandoff). Local state only: the game still holds the same item, so nothing
    /// goes on the network but the pose flags modded peers already read.
    /// </summary>
    private static void HandOff(Hand to, Item item)
    {
        LocalHold.ItemInLeftHand = to.Left;
        LocalHold.TwoHand = false;
        to.Action = GripPressAction.GrabItem;
        to.Target = item;
        to.Confirmed = true;
        to.Pocketing = false;
        // Its grip closed on the item: opening it lets go of it like any grab.
        to.Armed = true;
        Haptics.Pulse(to.Left, 0.3f, 0.04f);
        Plugin.Log.LogInfo(
            $"[interact] {item.name} handed to the {(to.Left ? "left" : "right")} hand, now its primary"
        );
    }

    /// <summary>A two-handed item counts as held in both hands: the free hand joined it, or one-handed play has no
    /// second hand to join it with (Accessibility.OneHanded), so its use is never refused for want of one.</summary>
    private static bool SecondHand => LocalHold.TwoHand || VRInput.OneHanded;

    /// <summary>One-handed: this trigger pull started with the stick clicked in, so it is the secondary use.</summary>
    private static bool secondaryChord;

    /// <summary>The tracked hand's fitted release velocity, rotated from tracking space to world: relative to the
    /// body, since the rig moves with it. Real metres per second (Camera.WorldScale): throw, lunge and push speeds
    /// are the player's own arm speed, so a tall player under the Auto scale (rig below x1) throws as hard.</summary>
    private static Vector3 HandVelocity(Hand hand, VRRig rig)
    {
        var local = hand.Throw.GetReleaseVelocity().U();
        return rig.Origin != null ? rig.Origin.rotation * local : local;
    }

    /// <summary>How close (metres) to the held item's surface a free hand counts as on it.</summary>
    private const float HeldSurfaceReach = 0.2f;

    /// <summary>How close (metres) to the held item's off-hand grip a free hand takes it.</summary>
    private const float SecondGripReach = 0.25f;

    /// <summary>
    /// The hand is within <paramref name="radius"/> of the item, measured to its colliders' real surface or its drawn
    /// parts where they disagree (<see cref="ItemGrabShape"/>). An axis-aligned box around a long item held at a
    /// slant, or a large trigger on it, reached well past the item: VR run 51's grab on a chain 0.45 m from the held
    /// launcher joined the launcher. The held item is drawn HeldItemScale smaller about its grip, so the hand is
    /// measured against the item as drawn.
    /// </summary>
    private static bool NearItem(Item item, Vector3 pos, float radius) =>
        ItemGrabShape.Distance(item, HeldItemScale.FromVisual(item, pos), out _)
        <= radius / HeldItemScale.Factor(item);

    /// <summary>The free (secondary) hand is at the held item's grip for it: <c>Hand_L</c> under a right-primary
    /// hold, its mirror under a left-primary one (ItemHoldInfo.SecondGrip), never the primary's own grip. Items
    /// without <c>Hand_L</c> have no second grip point.</summary>
    private static bool AtSecondGrip(Vector3 pos, float reach = SecondGripReach) =>
        Hold.SecondGrip(LocalHold.ItemInLeftHand, out var grip)
        && (grip - pos).sqrMagnitude <= reach * reach;

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
        FindCandidate(c, Eye(rig), t.position, t.Fingers(), cfg.GrabRadius, out var target);
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
            // Items whose drawn parts and colliders disagree are measured in the pass below, to what is drawn.
            if (it is Item shaped && ItemGrabShape.Corrected(shaped, out _))
                continue;
            // The collider's real surface, as the overlap itself measured it: a bounding box's corner of a long item
            // held at a slant, or of Bing Bong's limbs, lies in empty air, ranked the wrong item first and sighted a
            // point the item does not reach. Concave meshes have no exact query and keep their box.
            var point = SurfacePoint(col, pos);
            if (!InSight(eye, pos, point, col.bounds, it))
                continue;
            Candidates[count] = it;
            CandidatePositions[count] = point.N();
            count++;
        }

        // Items measured to their drawn parts (ItemGrabShape): a hand on a rescue hook's claw, 0.6 m past its
        // collider, is out of the overlap's reach; one over a glow mushroom's tall collider is not on the mushroom.
        float cull = radius + ItemGrabShape.MaxReach;
        foreach (var item in Item.ALL_ITEMS)
        {
            if (count >= Candidates.Length)
                break;
            if (
                item == null
                || item == c.data.currentItem
                || (item.transform.position - pos).sqrMagnitude > cull * cull
                || !ItemGrabShape.Corrected(item, out float reach)
                || (item.transform.position - pos).sqrMagnitude
                    > (radius + reach) * (radius + reach)
                || !item.IsInteractible(c)
                || ItemGrabShape.Distance(item, pos, out var point) > radius
            )
                continue;
            if (!InSight(eye, pos, point, SolidBounds(item, point), item))
                continue;
            Candidates[count] = item;
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
            target = SunkInto(c, pos, radius);

        if (target == null)
            return GrabCandidate.None;
        return target is Item ? GrabCandidate.Item : GrabCandidate.Interactible;
    }

    /// <summary>
    /// Line of sight like the game's interact ray (Interaction.cs:214): a hand pushed through a crate or a rock fin
    /// does not grab what lies behind it. Seen when any of the point nearest the hand, the centre or the top is clear
    /// (GrabSelector.SightPoints): an item partly sunk into uneven ground hides the first two (VR run 50's vr.10,
    /// vr.11 and vr.32 ground pickups failed with the hand on the Apple). Counts <see cref="lastReachable"/> and
    /// <see cref="lastHidden"/>.
    /// </summary>
    private static bool InSight(
        Vector3 eye,
        Vector3 pos,
        Vector3 point,
        Bounds bounds,
        IInteractible it
    )
    {
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
            lastHidden++;
        return seen;
    }

    /// <summary>World bounds of the item's active solid colliders, grown to <paramref name="point"/>.</summary>
    private static Bounds SolidBounds(Item item, Vector3 point)
    {
        var b = new Bounds(point, Vector3.zero);
        foreach (var col in item.colliders)
            if (col != null && col.enabled && !col.isTrigger && col.gameObject.activeInHierarchy)
                b.Encapsulate(col.bounds);
        return b;
    }

    /// <summary>Nearest point of the collider to <paramref name="pos"/>: exact where Collider.ClosestPoint is (boxes,
    /// spheres, capsules, convex meshes), else the bounds (a concave mesh).</summary>
    internal static Vector3 SurfacePoint(Collider col, Vector3 pos) =>
        ExactShape(col) ? col.ClosestPoint(pos) : col.bounds.ClosestPoint(pos);

    private static bool ExactShape(Collider col) =>
        col is BoxCollider or SphereCollider or CapsuleCollider or MeshCollider { convex: true };

    private static readonly NVector3[] SightPoints = new NVector3[GrabSelector.SightPointCount];
    private static readonly System.Collections.Generic.List<Collider> SunkColliders = new();

    /// <summary>The hovered interactible (Interaction.cs:15, :95) when the hand is inside, or within the grab radius of,
    /// the bounds of one of its enabled solid concave mesh colliders. Nothing stops a tracked hand going into a case, and an overlap
    /// sphere inside a concave mesh's hollow meets no triangle (VR runs 75 and 77's vr.82b: grip "Interact on nothing"
    /// with LuggageAncient hovered). Only the hovered one, which the game's own ray found, so nothing behind a wall.</summary>
    private static IInteractible SunkInto(Character c, Vector3 pos, float radius)
    {
        var interaction = Interaction.instance;
        var it = interaction != null ? interaction.currentHovered : null;
        if (
            it is not Component comp
            || comp == null
            || it is CharacterInteractible
            || (it is Item item && item == c.data.currentItem)
            || !it.IsInteractible(c)
        )
            return null;
        comp.GetComponentsInChildren(false, SunkColliders);
        IInteractible found = null;
        foreach (var col in SunkColliders)
        {
            // Only a concave mesh: the overlap sphere already finds a hand inside or at any primitive or convex
            // collider, and a box around those reached well past the item's shape.
            if (
                col.enabled
                && !col.isTrigger
                && !ExactShape(col)
                && col.bounds.SqrDistance(pos) <= radius * radius
            )
            {
                found = it;
                break;
            }
        }
        SunkColliders.Clear();
        return found;
    }

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
            Mathf.Min(dist + cfg.ClimbRadius + probeAllowance + 0.15f, GameClimbRay)
        );
        if (!c.data.isClimbing)
            return false;
        BeginPhysicalClimb();
        return true;
    }

    /// <summary>
    /// Decision D3: the game's climb ray starts at <c>MainCamera.instance</c> (CharacterClimbing.cs:541), so for this one
    /// call the camera sits just off the hold the probe found and casts straight into it: the same face, point and
    /// angle the chalk ring judged, even when the reach allowance put the hold past the hand. The hold must still lie
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
        var swap = CameraSwap.Begin(hold - dir * Behind, Quaternion.LookRotation(dir));
        try
        {
            StartWallClimb(
                c.refs.climbing,
                true,
                dir,
                false,
                Behind + (probeHit ? 0f : cfg.ClimbRadius) + 0.15f
            );
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

    /// <summary>Seconds a climb grip waits out the game's regrab cooldown while it stays closed.</summary>
    private const float RegrabWait = 0.25f;

    /// <summary>Only the 0.2 s after a climb ended keeps <c>CanClimb</c> false (CharacterClimbing.cs:580-595), not a
    /// rope or vine.</summary>
    private static bool RegrabCooldown(Character c) =>
        c.data.sinceClimb < 0.2f && !c.data.isRopeClimbing && !c.data.isVineClimbing;

    /// <summary>
    /// A climb grip that met the regrab cooldown climbs once the cooldown is over, if it is still closed on a hold
    /// (probed again where the hand is now). Otherwise it gets the usual refusal.
    /// </summary>
    private static void RetryClimb(Hand hand, Character c, VRRig rig, InteractConfig cfg, float now)
    {
        if (hand.ClimbRetryUntil < 0f)
            return;
        if (!hand.Grip.Held || hand.Action != GripPressAction.None || now > hand.ClimbRetryUntil)
        {
            hand.ClimbRetryUntil = -10f;
            return;
        }
        if (!c.data.isClimbing && !c.refs.climbing.CanClimb())
            return;
        hand.ClimbRetryUntil = -10f;
        var t = rig.Hand(hand.Left);
        if (t == null)
            return;
        var hold = ProbeHold(c, rig, t.position, t, cfg, c.data.currentItem);
        UseProbe(hold);
        if (hold.Verdict != HoldVerdict.Grab)
            return;
        if (StartClimb(c, rig, t.position, cfg))
        {
            hand.Action = GripPressAction.Climb;
            CatchPulse(hand.Left, hold.Kind, recentFall, cfg);
        }
        else
            DenyClimb(c, rig, hand.Left, t.position);
    }

    /// <summary>What a grip at the hand would meet: the one probe behind the chalk ring, the grip and its refusals.</summary>
    private struct HoldProbe
    {
        public HoldVerdict Verdict;
        public bool Hit;
        public Vector3 Point;
        public Vector3 Normal;
        public float Angle;
        public float Allowance;
        public HoldKind Kind;
    }

    private static void UseProbe(in HoldProbe hold)
    {
        probeVerdict = hold.Verdict;
        probeHit = hold.Hit;
        probePoint = hold.Point;
        probeNormal = hold.Normal;
        probeAngle = hold.Angle;
        probeAllowance = hold.Allowance;
        probeRefused = hold.Verdict is HoldVerdict.TooFlat or HoldVerdict.Overhang;
    }

    /// <summary>
    /// Looks for the hold a grip at the hand would take, along the reach (eye through hand) and then along the hand,
    /// and judges it as the game's climb start would (<see cref="ClimbReach.Judge"/>): the angle through the surface's
    /// climb modifier (CharacterClimbing.cs:511-532, ClimbModifierSurface.cs:63-70) and the reach from the camera
    /// (:552-561). An outstretched arm reaches <c>ClimbReachAssist</c> farther along the reach.
    /// </summary>
    private static HoldProbe ProbeHold(
        Character c,
        VRRig rig,
        Vector3 handPos,
        Transform hand,
        InteractConfig cfg,
        Item held
    )
    {
        var probe = default(HoldProbe);
        var eye = Eye(rig);
        // Judged on how far your real arm is out, added in real metres (Camera.WorldScale).
        probe.Allowance = RealScale.World(
            ClimbReach.Allowance(
                RealScale.Real(Vector3.Distance(eye, handPos)),
                cfg.ClimbReachAssist
            )
        );
        if (
            !CastHold(
                eye,
                handPos,
                hand.Fingers(),
                cfg.ClimbRadius,
                probe.Allowance,
                held,
                out var hit,
                out float beyond
            )
        )
            return probe;

        // Same lookup as CharacterClimbing.SampleWall (CharacterClimbing.cs:445).
        var mod = hit.collider.GetComponent<ClimbModifierSurface>();
        bool hasMod = mod != null;
        probe.Kind = ClimbRules.Kind(
            hasMod,
            hasMod && mod.onlySlideDown,
            hasMod && (mod.applyStatus || mod.applyPetrify),
            hasMod ? mod.staminaUsageMultiplier : 1f
        );
        probe.Hit = true;
        probe.Point = hit.point;
        probe.Normal = hit.normal;
        probe.Angle = Vector3.Angle(hit.normal, Vector3.up);
        if (
            hasMod
            && ModHasRange != null
            && ModHasRange(mod)
            && Vector3.Distance(mod.transform.position, c.Center) < ModRange(mod)
        )
            probe.Angle = 90f;
        probe.Verdict = ClimbReach.Judge(
            beyond,
            cfg.ClimbRadius,
            probe.Allowance,
            Vector3.Distance(eye, hit.point),
            probe.Angle,
            c.data.isClimbing
        );
        return probe;
    }

    /// <summary>
    /// The surface for a grip: along the reach from <see cref="ProbeBack"/> behind the hand to the grab reach plus the
    /// "too far" band past it; failing a hold within the grab radius there, along the hand. <paramref name="beyond"/>
    /// is the surface's distance past the hand (negative inside the rock). The held item's own colliders never count
    /// (VR run 38's held cactus read as rock).
    /// </summary>
    private static bool CastHold(
        Vector3 eye,
        Vector3 handPos,
        Vector3 handForward,
        float radius,
        float allowance,
        Item held,
        out RaycastHit hit,
        out float beyond
    )
    {
        var dir = handPos - eye;
        float eyeToHand = dir.magnitude;
        dir = eyeToHand > 1e-2f ? dir / eyeToHand : handForward;
        float back = Mathf.Clamp(eyeToHand - 0.05f, 0f, ProbeBack);
        float reach = radius + allowance;
        bool along =
            Physics.Raycast(
                handPos - dir * back,
                dir,
                out hit,
                back + reach + TraversalRefusal.ReachProbe,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            ) && !OnHeld(hit, held);
        beyond = along ? hit.distance - back : float.PositiveInfinity;
        if (along && beyond <= reach)
            return true;
        if (
            Physics.Raycast(
                handPos - handForward * 0.1f,
                handForward,
                out var ahead,
                0.1f + radius,
                HelperFunctions.terrainMapMask,
                QueryTriggerInteraction.Ignore
            ) && !OnHeld(ahead, held)
        )
        {
            hit = ahead;
            beyond = ahead.distance - 0.1f;
            return true;
        }
        return along;
    }

    private static bool OnHeld(in RaycastHit hit, Item held) =>
        held != null && hit.collider.GetComponentInParent<Item>() == held;

    /// <summary>A grip of this hand would start or hold a climb right now (<see cref="ProbeHold"/>), for cues that
    /// warn before it does (<see cref="ClimbStow"/>).</summary>
    internal static bool WouldCatch(Character c, bool left)
    {
        var rig = VRRig.Instance;
        var t = rig != null ? rig.Hand(left) : null;
        return t != null
            && ProbeHold(c, rig, t.position, t, InteractConfig.Instance, c.data.currentItem).Verdict
                == HoldVerdict.Grab;
    }

    /// <summary>
    /// The "can grab here" cue that replaces the game's climb reticle (<c>sinceCanClimb</c>, CharacterClimbing.cs:566),
    /// from the same probe and verdict as the grip (<see cref="ProbeHold"/>): a solid chalk ring and one contact tick
    /// where a grip now would catch, a faint ring on climbable rock just past the reach, and the red refusal ring
    /// under an outstretched hand on rock the game will not take. Per frame, two casts at most, no allocation.
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
            // Out of stamina, the game refuses a new grab within 1 s of the last (CharacterClimbing.cs:562).
            || (
                !c.data.isClimbing
                && SinceLastClimbStarted != null
                && SinceLastClimbStarted(c.refs.climbing) <= 1f
                && OutOfStamina(c)
            )
        )
        {
            Climb.ClimbHover.Hide(hand.Left);
            return;
        }
        var hold = ProbeHold(c, rig, t.position, t, cfg, c.data.currentItem);
        if (hold.Verdict is HoldVerdict.Grab or HoldVerdict.Approach)
        {
            bool ready = hold.Verdict == HoldVerdict.Grab;
            if (Climb.ClimbHover.Show(hand.Left, hold.Point, hold.Normal, ready, now))
                Affordance.Contact(hand.Left);
            return;
        }
        Climb.ClimbHover.Hide(hand.Left);
        // Rock the game will not take, under a hand held out for it: the refusal shows before the grip, not after.
        var eye = Eye(rig);
        if (Vector3.Distance(eye, t.position) < ClimbReach.BentArm)
            return;
        if (hold.Verdict == HoldVerdict.Overhang)
            Affordance.Hover(hand.Left, hold.Point, DenyReason.Overhang);
        else if (hold.Verdict == HoldVerdict.TooFlat && hold.Angle >= HoverTooFlatFrom)
            Affordance.Hover(hand.Left, hold.Point, DenyReason.TooFlat);
        else if (hold.Verdict == HoldVerdict.OutOfReach)
            Affordance.Hover(
                hand.Left,
                hold.Point,
                DenyReason.OutOfReach,
                DenyHint.Range(eye, hold.Point, GameClimbRay)
            );
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
                    Haptics.Beat(true, amplitude, StaminaBeat.Priority);
                if (rightGrip)
                    Haptics.Beat(false, amplitude, StaminaBeat.Priority);
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

    /// <summary>Grows when a search fills it (once, kept for the session), so no handle is cut off its end.</summary>
    private static Collider[] handleHits = new Collider[32];

    /// <summary>The "would work" spot for a condor's prey: the nearest free piton or handle within 5 m.</summary>
    private static DenyHint NearestHandle(Vector3 from)
    {
        int n;
        while (true)
        {
            n = Physics.OverlapSphereNonAlloc(
                from,
                5f,
                handleHits,
                Physics.AllLayers,
                QueryTriggerInteraction.Collide
            );
            if (n < handleHits.Length || handleHits.Length >= 1024)
                break;
            handleHits = new Collider[handleHits.Length * 2];
        }
        ClimbHandle best = null;
        float bestSq = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            var handle =
                handleHits[i] != null ? handleHits[i].GetComponentInParent<ClimbHandle>() : null;
            handleHits[i] = null;
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
        // Both pointing: the dominant hand aims (Input.DominantHand mirrors the layout, docs/controls.md).
        else if (l && (!r || VRInput.LeftHanded))
            AimHand = 0;
        else if (r)
            AimHand = 1;
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
        float d = -1f;
        if (rig.Head != null && mouthHand != null)
        {
            // The real mouth under the eyes, in real metres (Camera.WorldScale).
            var mouth = RealScale.World(
                MouthGesture
                    .MouthPoint(rig.Head.position.N(), rig.Head.rotation.N(), Mouth.MouthOffset)
                    .U(),
                rig.Head.position
            );
            d = RealScale.Real(
                Vector3.Distance(
                    mouth,
                    Physical.ItemGestures.MouthContact(
                        item,
                        mouthHand,
                        LocalHold.ItemInLeftHand,
                        mouth
                    )
                )
            );
        }
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
                + $"item {d:0.000} m from the mouth (inside {inside}), atMouth {atMouth}, trigger {primaryDown}, "
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
                    // A put-away that dropped the held item (a climb start) has not landed yet: its RPC would stash the
                    // item this pickup equips (PutAwayGuard). Hold the press, and its retry, until it has.
                    if (hand.Target is Item && !hand.Confirmed && PutAwayGuard.Pending)
                    {
                        hand.RetryAt = now + PickupRetry;
                        break;
                    }
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
        // A grab that started while a fallback fist held interact: up for this frame, so the game takes the press.
        if (interactGap)
        {
            interactGap = false;
            if (InteractEdge.Held)
                interactDown = false;
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
        var primaryHand = UseHandLeft(c, secondary: false) ? LeftHand : RightHand;
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
            // The item carried by the tracked grip, not the physics item: the eye sits inside the holder's own head
            // collider (a 0.38 m sphere on the Character layer, which held items collide with), so the physics item
            // never gets near the mouth. The food itself, not the fist: the game's food is big (an apple 0.42 m
            // across, its middle 0.26 m from the grip), so the fist at the mouth held it beside the cheek. A drink by its
            // opening only: by its nearest point a long bottle drank with its base at the cheek.
            // Your own mouth and reach, in real metres around the eyes (Camera.WorldScale).
            var head = rig.Head.position;
            var mouth = RealScale.World(
                MouthGesture.MouthPoint(head.N(), rig.Head.rotation.N(), Mouth.MouthOffset).U(),
                head
            );
            atMouth = Mouth.Update(
                head.N(),
                rig.Head.rotation.N(),
                RealScale
                    .Real(
                        Physical.ItemGestures.MouthContact(
                            c.data.currentItem,
                            mouthHand,
                            primaryHand.Left,
                            mouth
                        ),
                        head
                    )
                    .N(),
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
                Affordance.DenyAtHand(
                    left,
                    DenyReason.NeedsTwoHands,
                    Hold.SecondGrip(left, out var grip)
                        ? DenyHint.At(grip)
                        : DenyHint.On(c.data.currentItem.transform)
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
