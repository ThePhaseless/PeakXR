using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Interact;
using PeakXR.Rig;
using PeakXR.UI.Affordances;
using PeakXR.Voice;
using PeakXR.VR;
using UnityEngine;
using NVector2 = System.Numerics.Vector2;

namespace PeakXR.Locomotion;

/// <summary>
/// Maps the controllers onto the game's <see cref="CharacterInput"/> right after vanilla samples it, through the
/// binding table in PeakXR.Core (docs/controls.md). Values are OR-ed into the vanilla fields, so a keyboard or pad
/// still works next to the controllers. When interact.physical is active it owns grip and trigger (interact, use,
/// drop and throw) and this class leaves those actions alone; otherwise it maps them as button fallbacks.
/// Also runs room-scale following: the character walks after the real head instead of the view being pulled.
/// </summary>
internal static class LocomotionInput
{
    private const byte BackpackSlot = 3;

    private static readonly ActionMapper Mapper = new();
    private static readonly InputRamp Ramp = new();
    private static readonly RoomScaleFollow Follow = new();

    /// <summary>Head-to-body gap, the stall, and frames the body could not follow while the head was past the lean
    /// (self-test: vr.140's stalled follow).</summary>
    internal static float FollowGap { get; private set; }

    internal static bool FollowStalled => Stance.Blocked;

    /// <summary>The stance walk still owes a walk: the body can follow and is past the start distance, unblocked
    /// (self-test: a step judges the stance once none is owed).</summary>
    internal static bool StanceOwed { get; private set; }

    /// <summary><c>Time.realtimeSinceStartup</c> the last stance walk started (self-test: vr.45 checks a held lean
    /// is stepped under only after the walk's delay).</summary>
    internal static float StanceWalkStartedAt { get; private set; } = float.NegativeInfinity;
    internal static int FollowDrops { get; private set; }
    private static readonly RecenterFade Fade = new();
    private static readonly object FadeOwner = new();

    /// <summary>interact.physical is installed and handles interact, use and drop.</summary>
    public static bool PhysicalOwnsHands;

    private static int lastFrame = -10;
    private static int backpackFrame = -10;
    private static bool wroteSticky;
    private static Vector3 lastBody;
    private static bool hadBody;

    /// <summary>The body's travel at the last input update was PeakXR's own (<see cref="OwnMotion"/>): the view keeps
    /// it out.</summary>
    private static bool lastOwn;

    private static readonly OwnMotion Own = new();

    /// <summary><c>Time.time</c> of the last outside force on the local body (<see cref="LocomotionPatches.Push"/>,
    /// <see cref="LocomotionPatches.Impulse"/>).</summary>
    internal static float OutsideForceAt = float.NegativeInfinity;

    /// <summary>Seconds after an outside force during which the body is still moving from it: none of its travel is
    /// kept out of the view, and no walk starts (a push is never walked back).</summary>
    private const float OutsideSeconds = 1f;

    /// <summary>Seconds after a warp (Character.WarpPlayer, Character.cs:1849-1898) the body is still being placed and
    /// settling: no walk, and the view goes onto the body.</summary>
    private const float WarpSettleSeconds = 1f;
    private static readonly System.Action<Vector3> AbsorbNowAction = AbsorbNow;

    /// <summary>rig.camera's <see cref="RigDriver.BodyAnchorMoved"/>: absorbs the eye anchor's level travel into the
    /// room anchor in the frame it moves while that travel is PeakXR's own (<see cref="OwnMotion"/>: the walk, its
    /// coast to a stop, the body's shift under the head's turn or look down), so the origin placed right after carries
    /// no frame of it (run 9: 0.02-0.04 m as a coast outlived the walk's settle). An outside force in the same frame
    /// keeps it all in the view.</summary>
    private static void AbsorbNow(Vector3 anchor)
    {
        var rig = VRRig.Instance;
        if (!hadBody || rig == null || rig.Origin == null || !VRInput.HeadTracked)
            return;
        var delta = rig.Origin.InverseTransformVector(anchor - lastBody);
        lastBody = anchor;
        if (!lastOwn || Time.time - OutsideForceAt < OutsideSeconds)
            return;
        Follow.Anchor = rig.RoomAnchor;
        Follow.Absorb(new NVector2(delta.x, delta.z));
        rig.RoomAnchor = Follow.Anchor;
    }

    /// <summary>The one walk that puts the body under the head (<see cref="RigDriver.StanceDebt"/>): turns of every
    /// kind, steps and leans in the room, and the step back while looking down.</summary>
    private static readonly StanceWalk Stance = new();

    private static bool warnedFar;
    private static bool pointTriggerLeft;
    private static bool pointTriggerRight;

    /// <summary>The hand that pulled the trigger for <see cref="watchGoal"/>: its cues land there.</summary>
    private static bool watchLeft;
    private static Character watchGoal;
    private static float watchNextPress;
    private static int watchPressesLeft;
    private static readonly Reservation DropReserve = new();
    private static readonly Reservation TalkReserve = new();
    private static readonly Reservation[] ChordReserve = { new(), new(), new(), new() };
    private static readonly Reservation[] SlideReserve = { new(), new() };

    /// <summary>
    /// One-shot backpack press for the hotbar-key patch. Valid this frame and the next, because CharacterItems may
    /// update before CharacterMovement samples input in a frame; consumed when read.
    /// </summary>
    public static bool ConsumeBackpack(CharacterInput input)
    {
        if (
            Time.frameCount - backpackFrame > 1
            || input.itemSwitchBlocked
            || Character.localCharacter == null
            || Character.localCharacter.input != input
        )
            return false;
        backpackFrame = -10;
        return true;
    }

    public static bool IsBackpackSlot(int key)
    {
        // The game's own backpack slot id (Player.cs:47), so a changed hotbar size does not select the wrong slot.
        var p = Player.localPlayer;
        return key == (p != null ? p.backpackSlot.itemSlotID : BackpackSlot);
    }

    /// <summary>Keeps layout actions off the buttons other features own, so one press never does two jobs: the
    /// button interact.physical uses for drop (e.g. a fixed <c>RightSecondary</c> for a left-handed player, which
    /// would also ping) and <c>Voice.PushToTalkButton</c> (which would also jump, ping or open a wheel while
    /// talking), and the Menu and stick click the bug-report chord holds (<c>Diagnostics.DebugGesture</c>), so it
    /// never pauses, sprints or crouches. All are cleared before any is set, so a shared control stays reserved.</summary>
    private static void ReserveButtons(bool owns, bool dominantLeft)
    {
        DropReserve.Clear();
        TalkReserve.Clear();
        foreach (var r in ChordReserve)
            r.Clear();
        foreach (var r in SlideReserve)
            r.Clear();
        DropReserve.Take(owns, InteractConfig.Instance.DropButton, dominantLeft);
        var talk = VoiceFeature.Settings?.PushToTalkButton;
        TalkReserve.Take(talk != null, talk?.Value, dominantLeft);
        ChordReserve[0].Take(Diagnostics.DebugGesture.MenuTaken(true), true, VRControl.Menu);
        ChordReserve[1].Take(Diagnostics.DebugGesture.MenuTaken(false), false, VRControl.Menu);
        ChordReserve[2].Take(Diagnostics.DebugGesture.StickTaken(true), true, VRControl.StickClick);
        ChordReserve[3]
            .Take(Diagnostics.DebugGesture.StickTaken(false), false, VRControl.StickClick);
        // A Vive wand centre click (B/Y) that slid out ends here: reserved on that frame, its release is no ping tap.
        SlideReserve[0].Take(VRInput.Left.SecondaryCancelled, true, VRControl.Secondary);
        SlideReserve[1].Take(VRInput.Right.SecondaryCancelled, false, VRControl.Secondary);
    }

    /// <summary>One control reserved in <see cref="Mapper"/> by name (<see cref="ButtonName"/>).</summary>
    private sealed class Reservation
    {
        private bool has;
        private bool left;
        private VRControl control;

        public void Clear()
        {
            if (has)
                Mapper.SetReserved(left, control, false);
            has = false;
        }

        public void Take(bool want, string name, bool dominantLeft)
        {
            has =
                want
                && name != null
                && ButtonName.TryParse(name, dominantLeft, out left, out control);
            if (has)
                Mapper.SetReserved(left, control, true);
        }

        public void Take(bool want, bool isLeft, VRControl which)
        {
            has = want;
            left = isLeft;
            control = which;
            if (has)
                Mapper.SetReserved(left, control, true);
        }
    }

    /// <param name="active">The game's <c>playerMovementActive</c>: no menu window or wheel blocks input.</param>
    public static void Apply(Character c, CharacterInput input, bool active)
    {
        float dt = Time.deltaTime;
        lastFrame = Time.frameCount;
        bool dominantLeft = VRInput.LeftHanded;
        var item = c.data.currentItem;

        Mapper.DominantLeft = dominantLeft;
        Mapper.Bindings = BindingTable.Active;
        Mapper.ItemInLeft = item != null ? LocalHold.ItemInLeftHand : dominantLeft;
        bool owns = PhysicalOwnsHands;
        Mapper.SetDisabled(VRAction.Interact, owns);
        Mapper.SetDisabled(VRAction.UsePrimary, owns);
        Mapper.SetDisabled(VRAction.UseSecondary, owns);
        Mapper.SetDisabled(VRAction.Drop, owns);
        ReserveButtons(owns, dominantLeft);
        Mapper.MenuHoldSeconds = CameraFeature.RecenterSeconds;
        // One stick, two meanings: items with a scroll function (rope spool, binoculars, guidebook, scout cannon)
        // take up/down as scroll, otherwise it cycles the hotbar.
        // Dead or fully passed out, vanilla spectates a teammate (MainCameraMovement.cs:77) and the same flick
        // picks the next or previous one instead.
        bool spectating = c.data.dead || c.data.fullyPassedOut;
        bool scroll = !spectating && item != null && WantsScroll(item);
        Mapper.SetDisabled(VRAction.HotbarNext, scroll || spectating);
        Mapper.SetDisabled(VRAction.HotbarPrevious, scroll || spectating);
        Mapper.SetDisabled(VRAction.ScrollForward, !scroll);
        Mapper.SetDisabled(VRAction.ScrollBackward, !scroll);
        Mapper.SetDisabled(VRAction.SpectateNext, !spectating);
        Mapper.SetDisabled(VRAction.SpectatePrevious, !spectating);
        // Real time, like rig.camera's recenter hold, so the menu tap and hold share one clock at any timeScale.
        Mapper.Update(
            Controller(VRInput.Left, true),
            Controller(VRInput.Right, false),
            Time.unscaledDeltaTime
        );

        // Vanilla samples these even while input is blocked (CharacterInput.cs:215-219, :269-271): the emote wheel
        // stays open only while Emote is held, and Pause must reach an open menu.
        input.pauseWasPressed |= Mapper.Pressed(VRAction.Pause);
        input.emoteIsPressed |= Mapper.Held(VRAction.Emote);
        // The voice feature merges this into pushToTalkPressed and handles blocked input itself.
        VoiceInput.PushToTalkHeld = Mapper.Held(VRAction.PushToTalk);
        if (!owns)
        {
            input.interactWasPressed |= Mapper.Pressed(VRAction.Interact);
            input.interactIsPressed |= Mapper.Held(VRAction.Interact);
            input.interactWasReleased |= Mapper.Released(VRAction.Interact);
        }
        bool canSwitch = !input.itemSwitchBlocked;
        input.selectSlotForwardWasPressed |= canSwitch && Mapper.Pressed(VRAction.HotbarNext);
        input.selectSlotBackwardWasPressed |= canSwitch && Mapper.Pressed(VRAction.HotbarPrevious);

        var move = active ? MoveStick(dominantLeft, dt) : Ramp.Update(NVector2.Zero, dt);
        var room = RoomScale(c, active && move.LengthSquared() < 0.0025f, dt);
        BodyGestures.Apply(c, input, active, move.Y, dt);

        if (!active)
        {
            ClearSticky(input);
            return;
        }

        input.movementInput = ToUnity(
            MoveInput.Combine(Num2(input.movementInput), MoveInput.Combine(move, room))
        );

        input.jumpWasPressed |= Mapper.Pressed(VRAction.Jump);
        input.jumpIsPressed |= Mapper.Held(VRAction.Jump);
        if (LocomotionConfig.SprintToggle)
        {
            input.sprintToggleWasPressed |= Mapper.Pressed(VRAction.Sprint);
            input.sprintToggleIsPressed |= Mapper.Held(VRAction.Sprint);
        }
        else
        {
            input.sprintWasPressed |= Mapper.Pressed(VRAction.Sprint);
            input.sprintIsPressed |= Mapper.Held(VRAction.Sprint);
        }
        if (LocomotionConfig.CrouchToggle)
        {
            input.crouchToggleWasPressed |= Mapper.Pressed(VRAction.Crouch);
        }
        else
        {
            input.crouchWasPressed |= Mapper.Pressed(VRAction.Crouch);
            input.crouchIsPressed |= Mapper.Held(VRAction.Crouch);
        }

        if (!owns)
        {
            input.usePrimaryWasPressed |= Mapper.Pressed(VRAction.UsePrimary);
            input.usePrimaryIsPressed |= Mapper.Held(VRAction.UsePrimary);
            input.usePrimaryWasReleased |= Mapper.Released(VRAction.UsePrimary);
            input.useSecondaryWasPressed |= Mapper.Pressed(VRAction.UseSecondary);
            input.useSecondaryIsPressed |= Mapper.Held(VRAction.UseSecondary);
            input.useSecondaryWasReleased |= Mapper.Released(VRAction.UseSecondary);
            input.dropWasPressed |= Mapper.Pressed(VRAction.Drop);
            input.dropIsPressed |= Mapper.Held(VRAction.Drop);
            input.dropWasReleased |= Mapper.Released(VRAction.Drop);
        }

        input.scrollForwardWasPressed |= Mapper.Pressed(VRAction.ScrollForward);
        input.scrollForwardIsPressed |= Mapper.Held(VRAction.ScrollForward);
        input.scrollBackwardWasPressed |= Mapper.Pressed(VRAction.ScrollBackward);
        input.scrollBackwardIsPressed |= Mapper.Held(VRAction.ScrollBackward);
        // Spectating reads its own next/previous (CharacterInput.cs:254-255).
        input.spectateRightWasPressed |= Mapper.Pressed(VRAction.SpectateNext);
        input.spectateLeftWasPressed |= Mapper.Pressed(VRAction.SpectatePrevious);
        PointToWatch(c, input, dominantLeft, spectating);
        if (spectating)
        {
            if (Mapper.Pressed(VRAction.SpectateNext))
                DenyLoneSpectate(c, VRAction.SpectateNext, dominantLeft);
            if (Mapper.Pressed(VRAction.SpectatePrevious))
                DenyLoneSpectate(c, VRAction.SpectatePrevious, dominantLeft);
        }
        input.unselectSlotWasPressed |= canSwitch && Mapper.Pressed(VRAction.PutAway);
        DenyBlockedSwitch(c, dominantLeft);
        // No selectBackpackWasPressed: the game samples it (CharacterInput.cs:257) but never reads it.
        if (Mapper.Pressed(VRAction.Backpack))
            backpackFrame = Time.frameCount;
        input.pingWasPressed |= Mapper.Pressed(VRAction.Ping);
        wroteSticky = true;
    }

    /// <summary>Called every frame by the driver: if the game stopped sampling (scene change, no character),
    /// drop held state and any recenter fade so the screen never stays black.</summary>
    public static void Watchdog()
    {
        if (Time.frameCount - lastFrame <= 1)
            return;
        if (Fade.Target > 0f)
        {
            Fade.Reset();
            VRFade.Clear(FadeOwner);
        }
        Mapper.Reset();
        VoiceInput.PushToTalkHeld = false;
        Ramp.Reset();
        hadBody = false;
        BodyGestures.Reset();
    }

    /// <summary>One controller as the layout reads it. Its stick click comes through the stick chord's window
    /// (<see cref="XRInput.StickChordInput.Click"/>), so the first stick of a recenter chord never sprints or
    /// crouches.</summary>
    private static ControllerInput Controller(in HandState h, bool left) =>
        new()
        {
            Trigger = h.Trigger,
            Grip = h.Grip,
            Primary = h.Primary,
            Secondary = h.Secondary,
            StickClick = XRInput.StickChordInput.Click(left),
            Menu = h.Menu,
            Stick = Num2(h.Stick),
        };

    private static NVector2 MoveStick(bool dominantLeft, float dt)
    {
        // One-handed, the one stick also turns (LookPatches): it walks only with the forward/back part.
        bool one = VRInput.OneHanded;
        ref var hand = ref VRInput.Hand(one ? dominantLeft : !dominantLeft);
        // A chord holding this stick's click (recenter, bug report) is not a walk: a Vive wand's rim click walks.
        var raw = Diagnostics.DebugGesture.StickTaken(one ? dominantLeft : !dominantLeft)
            ? NVector2.Zero
            : Num2(hand.Stick);
        var stick = Stick.Apply(one ? StickAxes.MoveY(raw) : raw, LocomotionConfig.Deadzone, 0.95f);
        if (LocomotionConfig.HandRelative && hand.Tracked && VRInput.HeadTracked)
            stick = MoveInput.HandRelative(
                stick,
                HeadYaw(),
                MoveInput.Yaw(GripAxes.Fingers(hand.Rotation).N())
            );
        Ramp.RiseTime = ComfortConfig.MoveSmoothing;
        return Ramp.Update(stick, dt);
    }

    private static float HeadYaw() => MoveInput.Yaw((VRInput.HeadRotation * Vector3.forward).N());

    /// <summary>
    /// Room-scale: the camera feature keeps <see cref="VRRig.RoomAnchor"/> over the body's eyes, so stepping moves the
    /// view exactly like the real head. The stance walk (<see cref="RigDriver.StanceDebt"/>, measured from the view)
    /// walks the body under the head with stick input, and the anchor takes that walk's travel, so the view never
    /// moves on its own. A blocked walk past the lean limit, or a head 1 m from a body that cannot follow, gets one
    /// fade-out, recenter, fade-in. No teleport, no rig pulling.
    /// </summary>
    private static NVector2 RoomScale(Character c, bool stickIdle, float dt)
    {
        var rig = VRRig.Instance;
        // The smoothed eye anchor the view rides, not a raw body part: absorbing anything else moves the view.
        // The anchor is from the last LateUpdate, where AbsorbNow already took its travel (delta 0 here).
        RigDriver.BodyAnchorMoved = AbsorbNowAction;
        var anchor = RigDriver.BodyAnchor;
        if (rig == null || rig.Origin == null || !VRInput.HeadTracked)
        {
            hadBody = false;
            Stance.Reset();
            lastOwn = false;
            Own.Reset();
            RigDriver.FollowWalking = false;
            StanceOwed = false;
            return NVector2.Zero;
        }

        var delta =
            hadBody && anchor != null
                ? rig.Origin.InverseTransformVector(anchor.Value - lastBody)
                : Vector3.zero;
        hadBody = anchor != null;
        if (hadBody)
            lastBody = anchor.Value;

        var data = c.data;
        // A warp places the body (respawns, segment jumps, scripted moves): nothing to walk, and the view goes with it.
        bool warped = c.warping || Time.time - c.timeLastWarped < WarpSettleSeconds;
        bool canFollow =
            hadBody
            && LocomotionConfig.RoomScale
            && stickIdle
            // A walking stride can leave the ground for a frame; that must not end a walk (and its settle), which
            // hands the walk's travel to the view.
            && (data.isGrounded || data.sinceGrounded < 0.25f)
            && data.fullyConscious
            && !data.isClimbingAnything
            && data.currentClimbHandle == null
            // The top-out walk moves the body onto the ledge under its own input: a walk toward the head behind it
            // fought that walk, and its stall's recenter fade moved the gripping hands (vr.54, run 54).
            && !PhysicalInteraction.Mantling
            && data.carrier == null
            && !warped;
        var head = new NVector2(VRInput.HeadPosition.x, VRInput.HeadPosition.z);
        Follow.Anchor = rig.RoomAnchor;
        // Wind, a push or knockback, a slide, steep ground or a moving surface under the feet: vanilla motion of body
        // and view alike, never walked back.
        bool outside =
            Time.time - OutsideForceAt < OutsideSeconds
            || data.sinceFallSlide < HoldGate.SlideCooldown
            || data.isGrounded && OnMovingOrSteepGround(c);
        if (lastOwn && canFollow && !outside)
            Follow.Absorb(new NVector2(delta.x, delta.z));
        var debt3 = rig.Origin.InverseTransformVector(RigDriver.StanceDebt);
        var debt = new NVector2(debt3.x, debt3.z);
        // The view takes the body's place over a warp, so it leaves no debt (a turn just before it) to walk off at the
        // spot it was put on (run 9, vr.44b: the walk took the body 2.3 m from the wall it was warped to).
        if (warped)
            Follow.Anchor += debt;
        Stance.AtOnce = LocomotionConfig.LeanRadius;
        Stance.InputSlew = c.refs.movement.movementTurnSpeed;
        // The walk brakes on the body's speed: it coasts on after the request stops (cf6aa3e: 0.1-0.16 m past the view).
        // The whole body's velocity, as RigDriver.ViewRoot weighs it: the game moves a part with a pull answered on its
        // parent and the hip (Bodypart.cs:193-205), so hand motion leaves this still where an arm-less sum reads the
        // recoil as walking.
        var bodyVelocity = RigDriver.ViewVelocity(c);
        // World heading, tracking scale (the walk's tuned distances): a snap or smooth turn mid-walk would turn
        // tracking-space debt against the walk's heading and use up its one walk back.
        float toTracking = 1f / Mathf.Max(rig.Scale, 0.01f);
        var worldDebt = RigDriver.StanceDebt * toTracking;
        var levelVelocity = bodyVelocity * toTracking;
        var phase = Stance.State;
        float phaseFor = Stance.PhaseSeconds;
        var worldRequest = Stance.Update(
            new NVector2(worldDebt.x, worldDebt.z),
            new NVector2(levelVelocity.x, levelVelocity.z),
            data.worldMovementInput_Lerp.magnitude,
            canFollow && !outside,
            dt
        );
        if (Stance.State != phase)
            TraceStance(phase, phaseFor, debt.Length(), worldRequest.Length());
        var request3 =
            Quaternion.Inverse(rig.Origin.rotation)
            * new Vector3(worldRequest.X, 0f, worldRequest.Y);
        var request = new NVector2(request3.x, request3.z);
        bool following = canFollow && Stance.Walking;
        if (following && !RigDriver.FollowWalking)
            StanceWalkStartedAt = Time.realtimeSinceStartup;
        RigDriver.FollowWalking = following;
        StanceOwed = canFollow && !outside && !Stance.Blocked && debt.Length() > Stance.Start;
        var level = new NVector2(bodyVelocity.x, bodyVelocity.z);
        // A body that cannot follow (stick walking, airborne, climbing, carried) is moved by something else: a coast
        // kept over that would claim its travel once it can follow again.
        if (!canFollow)
            Own.Reset();
        lastOwn =
            canFollow
            && Own.Update(
                Stance.Walking,
                level.Length(),
                new NVector2(data.lookValues.x, data.lookValues.y),
                outside,
                dt
            );
        FollowGap = NVector2.Distance(head, Follow.Anchor);
        // A walk that cannot get the body within the lean limit (a wall, a ledge, a friend) ends in the recenter.
        bool stalled = Stance.Blocked && debt.Length() > LocomotionConfig.LeanRadius;
        if (!canFollow && stickIdle && FollowGap > LocomotionConfig.LeanRadius)
            FollowDrops++;
        WarnFar(c, canFollow, stickIdle, stalled);
        Fade.DrawnFade = UI.Common.FadeView.Alpha;
        if (Fade.Update(stalled || FollowGap > Follow.RecenterDistance, dt))
        {
            Follow.Recenter(head);
            Stance.Reset();
        }
        rig.RoomAnchor = Follow.Anchor;
        if (Fade.Target > 0f)
            VRFade.Set(FadeOwner, Fade.Target);
        else
            VRFade.Clear(FadeOwner);
        var stick = MoveInput.TrackingToStick(request, HeadYaw());
        if (RigDriver.LeanTraceDue(ref nextLeanTrace))
        {
            var armless = ArmlessVelocity(c);
            Plugin.Log.LogInfo(
                System.FormattableString.Invariant(
                    $"[player.locomotion] room t{Time.unscaledTime:0.00} head-anchor {FollowGap:0.000} debt ({debt.X:0.000},{debt.Y:0.000}) body-delta ({delta.x:0.000},{delta.z:0.000}) follow {canFollow} walking {Stance.Walking} blocked {Stance.Blocked} stick fwd {stick.Y:0.00} right {stick.X:0.00} body-v ({bodyVelocity.x:0.000},{bodyVelocity.z:0.000}) armless-v ({armless.x:0.000},{armless.z:0.000})"
                )
            );
        }
        return stick;
    }

    /// <summary>Lean trace only: one line per stance walk phase change (push, settle, done, given up), so a run shows
    /// each push's length and how far the body sprang back after it (run 10: walks of a few frames, 85 % back).</summary>
    private static void TraceStance(StanceWalkPhase from, float seconds, float debt, float request)
    {
        if (Time.unscaledTime >= RigDriver.TraceLeanUntil && !Diagnostics.DebugLog.Verbose)
            return;
        Plugin.Log.LogInfo(
            System.FormattableString.Invariant(
                $"[player.locomotion] stance t{Time.unscaledTime:0.00} {from} {seconds:0.00} s -> {Stance.State}{(Stance.Blocked ? " (blocked)" : "")}: debt {debt:0.000} m, push {Stance.Pushes} from {Stance.PushFrom:0.000} m, closest {Stance.Closest:0.000} m, request {request:0.00}, push-len {Stance.PushSeconds:0.00} s, headway {Stance.Headway:0.000} m, spring-back {Stance.SpringBack:0.000} m"
            )
        );
    }

    /// <summary>Trace only: the parts' mass-weighted velocity with the arms left out (the walk's input before
    /// <see cref="RigDriver.ViewVelocity"/>), logged beside it so a run shows the arms' recoil.</summary>
    private static Vector3 ArmlessVelocity(Character c)
    {
        var parts = c.refs.ragdoll != null ? c.refs.ragdoll.partList : null;
        var sum = Vector3.zero;
        float mass = 0f;
        for (int i = 0; parts != null && i < parts.Count; i++)
        {
            var part = parts[i];
            var body = part != null ? part.Rig : null;
            if (body == null || part.partType is >= BodypartType.Arm_L and <= BodypartType.Hand_R)
                continue;
            sum += body.linearVelocity * body.mass;
            mass += body.mass;
        }
        return mass > 0f ? sum / mass : c.data.avarageVelocity;
    }

    private static readonly RaycastHit[] groundHits = new RaycastHit[4];

    /// <summary>The body stands on ground steeper than it can stand on (it slides, CharacterMovement's max angle) or on
    /// a moving body (a rolling log, a falling rock): the game moves it, not PeakXR.</summary>
    private static bool OnMovingOrSteepGround(Character c)
    {
        var d = c.data;
        var gate = AvatarPoseDriver.GateInputs(c);
        if (!(gate.SlopeDeg < gate.MaxSlopeDeg))
            return true;
        int n = Physics.RaycastNonAlloc(
            d.groundPos + Vector3.up * 0.2f,
            Vector3.down,
            groundHits,
            0.5f,
            HelperFunctions.AllPhysicalExceptCharacter,
            QueryTriggerInteraction.Ignore
        );
        for (int i = 0; i < n; i++)
        {
            var body = groundHits[i].rigidbody;
            if (body == null)
                continue;
            var v = body.GetPointVelocity(groundHits[i].point);
            if (v.x * v.x + v.z * v.z > 0.05f * 0.05f)
                return true;
        }
        return false;
    }

    private static float nextLeanTrace;

    /// <summary>Head this far (m) from a body that cannot follow: warned before the 1 m recenter fade.</summary>
    private const float FarWarning = 0.8f;

    /// <summary>
    /// The head drifted from a body that cannot step after it (climbing, hanging, airborne, carried), or the body's
    /// follow walk stalled against something: before the recenter fade, a NotNow refusal on the dominant hand with a ring at the body's feet, the spot to come back to.
    /// Once per drift; armed again back inside half the distance.
    /// </summary>
    private static void WarnFar(Character c, bool canFollow, bool stickIdle, bool stalled)
    {
        float far = FollowGap;
        if (far < FarWarning * 0.5f)
            warnedFar = false;
        // A walk the body could not finish also fades: it gets the same reason.
        if (warnedFar || !stickIdle || (!stalled && (canFollow || far < FarWarning)))
            return;
        warnedFar = true;
        var feet = c.data.isGrounded ? c.data.groundPos : c.Center;
        // One hand, and not one busy with its own action (BodyFeedback.BodyHand): one refusal ring shows at a time.
        if (BodyFeedback.BodyHand(VRInput.LeftHanded, out bool hand))
            Affordance.Deny(hand, feet, DenyReason.NotNow, DenyHint.At(feet));
    }

    /// <summary>Widest angle (degrees) between the pointing hand and a teammate it picks.</summary>
    private const float PointCone = 15f;

    /// <summary>Seconds between the stepped switches: the game's switch transition (MainCameraMovement.cs:193-201,
    /// sinceSwitch over 0.2 s) must finish before the next one counts.</summary>
    private const float WatchStep = 0.6f;

    /// <summary>
    /// WS-D4: while spectating, point either hand at a teammate and pull its trigger to watch them. Vanilla
    /// only steps through players (SwapSpecPlayer, MainCameraMovement.cs:221), so this presses its own "next" once
    /// per <see cref="WatchStep"/> until that teammate is watched: nothing new reaches the network. A contact tick
    /// when the view lands on them; CantSwitch at the hand when nobody is pointed at or they cannot be watched.
    /// </summary>
    private static void PointToWatch(
        Character c,
        CharacterInput input,
        bool dominantLeft,
        bool spectating
    )
    {
        bool pressedLeft = TriggerPressed(ref pointTriggerLeft, VRInput.Left, spectating);
        bool pressedRight = TriggerPressed(ref pointTriggerRight, VRInput.Right, spectating);
        if (!spectating)
        {
            watchGoal = null;
            return;
        }
        var spec = MainCameraMovement.specCharacter;
        if (pressedLeft || pressedRight)
        {
            // Both at once: the dominant hand points.
            watchLeft = pressedLeft && pressedRight ? dominantLeft : pressedLeft;
            var pick = PointedTeammate(c, watchLeft);
            if (pick == null)
                Affordance.DenyAtHand(watchLeft, DenyReason.CantSwitch);
            else if (pick == spec)
                Affordance.Contact(watchLeft);
            else
            {
                watchGoal = pick;
                watchPressesLeft = Character.AllCharacters.Count + 1;
                watchNextPress = 0f;
            }
        }
        if (watchGoal == null)
            return;
        if (spec == watchGoal)
        {
            Affordance.Contact(watchLeft);
            watchGoal = null;
            return;
        }
        // Your own body cannot be switched away from (MainCameraMovement.cs:189).
        if (!watchGoal.data.canBeSpectated || watchPressesLeft <= 0 || spec == c)
        {
            Affordance.DenyAtHand(watchLeft, DenyReason.CantSwitch);
            watchGoal = null;
            return;
        }
        if (Time.unscaledTime < watchNextPress)
            return;
        input.spectateRightWasPressed = true;
        watchPressesLeft--;
        watchNextPress = Time.unscaledTime + WatchStep;
    }

    /// <summary>A tracked hand's trigger crossed into a full pull this frame (0.7 in, 0.3 out).</summary>
    private static bool TriggerPressed(ref bool down, in HandState hand, bool spectating)
    {
        bool now = spectating && hand.Tracked && hand.Trigger > (down ? 0.3f : 0.7f);
        bool pressed = now && !down;
        down = now;
        return pressed;
    }

    /// <summary>The watchable teammate nearest that hand's pointing ray, within <see cref="PointCone"/>.</summary>
    private static Character PointedTeammate(Character self, bool dominantLeft)
    {
        var rig = VRRig.Instance;
        // The aim pose: the ray out of the controller's front, not the grip's +Z (along the handle).
        var aim = rig != null ? rig.Aim(dominantLeft) : null;
        if (aim == null)
            return null;
        Character best = null;
        float bestAngle = PointCone;
        foreach (var ch in Character.AllCharacters)
        {
            if (ch == null || ch == self || ch.isBot || !ch.data.canBeSpectated)
                continue;
            float angle = Vector3.Angle(aim.forward, ch.Center - aim.position);
            if (angle < bestAngle)
            {
                bestAngle = angle;
                best = ch;
            }
        }
        return best;
    }

    /// <summary>
    /// A spectate flick with nobody else to watch: your own body (solo, or the last one standing) cannot be
    /// switched away from (MainCameraMovement.HandleSpecSelection, :189), and a lone teammate has no next one
    /// (SwapSpecPlayer, :221). CantSwitch at the hand whose stick flicked.
    /// </summary>
    private static readonly VRAction[] SwitchActions =
    {
        VRAction.HotbarNext,
        VRAction.HotbarPrevious,
        VRAction.PutAway,
        VRAction.Backpack,
    };

    /// <summary>A stick item switch, X put-away or X-hold backpack the game will ignore (CharacterItems.DoSwitching,
    /// CharacterItems.cs:735: climbing, on a rope, mid-use, a sticky item, locked from switching) gets the body
    /// slots' refusal cue at the pressing hand instead of nothing.</summary>
    private static void DenyBlockedSwitch(Character c, bool dominantLeft)
    {
        foreach (var action in SwitchActions)
        {
            if (!Mapper.Pressed(action))
                continue;
            if (Inventory.BodyInventory.SwitchBlocked(c))
                Affordance.DenyAtHand(
                    ActionLeft(action, dominantLeft),
                    Inventory.BodyInventory.BlockReason(c)
                );
            return;
        }
    }

    /// <summary>The controller an action is on, for its cue (the off hand if the layout does not say).</summary>
    private static bool ActionLeft(VRAction action, bool dominantLeft) =>
        ControllerLabels.ForAction(
            action,
            dominantLeft,
            LocalHold.ItemInLeftHand,
            out var actionLeft,
            out _
        )
            ? actionLeft
            : !dominantLeft;

    private static void DenyLoneSpectate(Character c, VRAction action, bool dominantLeft)
    {
        var spec = MainCameraMovement.specCharacter;
        bool other = false;
        if (spec != null && spec != c)
        {
            foreach (var ch in PlayerHandler.GetAllPlayerCharacters())
            {
                if (ch != spec && ch.data.canBeSpectated && !ch.isBot)
                {
                    other = true;
                    break;
                }
            }
        }
        if (other)
            return;
        Affordance.DenyAtHand(ActionLeft(action, dominantLeft), DenyReason.CantSwitch);
    }

    /// <summary>
    /// The held item reacts to scroll: the game's own scroll prompt flag (GUIManager.cs:1181) or a subscribed
    /// scroll-button handler (Item.cs:170-176).
    /// </summary>
    private static bool WantsScroll(Item item) =>
        item.UIData.hasScrollingInteract
        || item.OnScrollForwardPressed != null
        || item.OnScrollBackwardPressed != null
        || item.OnScrollForwardHeld != null
        || item.OnScrollBackwardHeld != null;

    /// <summary>
    /// ResetInput does not clear these while input is blocked (CharacterInput.cs:304 vs :212), so a press written on
    /// the last unblocked frame would repeat every frame a menu stays open. Clear them once on that transition.
    /// </summary>
    private static void ClearSticky(CharacterInput input)
    {
        if (!wroteSticky)
            return;
        wroteSticky = false;
        input.pingWasPressed = false;
        input.scrollForwardWasPressed = input.scrollForwardIsPressed = false;
        input.scrollBackwardWasPressed = input.scrollBackwardIsPressed = false;
        input.spectateLeftWasPressed = input.spectateRightWasPressed = false;
        input.sprintToggleIsPressed = false;
    }

    private static NVector2 Num2(Vector2 v) => new(v.x, v.y);

    private static Vector2 ToUnity(NVector2 v) => new(v.X, v.Y);
}
