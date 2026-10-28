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
    internal static float FollowGap => Follow.Offset.Length();

    internal static bool FollowStalled => Follow.Stalled;
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
    private static bool lastCanFollow;
    private static readonly System.Action<Vector3> AbsorbNowAction = AbsorbNow;

    /// <summary>rig.camera's <see cref="RigDriver.BodyAnchorMoved"/>: absorbs the eye anchor's travel into the room
    /// anchor in the frame it moves, so the origin placed right after carries no frame of body motion (run 32: the
    /// view moved 0.044 m at the end of a far-lean walk). The next <see cref="RoomScale"/> then sees no delta.</summary>
    private static void AbsorbNow(Vector3 anchor)
    {
        var rig = VRRig.Instance;
        if (!hadBody || !lastCanFollow || rig == null || rig.Origin == null || !VRInput.HeadTracked)
            return;
        var delta = rig.Origin.InverseTransformVector(anchor - lastBody);
        lastBody = anchor;
        Follow.Anchor = rig.RoomAnchor;
        Follow.Absorb(
            new NVector2(VRInput.HeadPosition.x, VRInput.HeadPosition.z),
            new NVector2(delta.x, delta.z)
        );
        rig.RoomAnchor = Follow.Anchor;
    }

    private static bool warnedFar;
    private static bool pointTrigger;
    private static Character watchGoal;
    private static float watchNextPress;
    private static int watchPressesLeft;
    private static readonly Reservation DropReserve = new();
    private static readonly Reservation TalkReserve = new();
    private static readonly Reservation[] ChordReserve = { new(), new(), new(), new() };

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
        DropReserve.Take(owns, InteractConfig.Instance.DropButton, dominantLeft);
        var talk = VoiceFeature.Settings?.PushToTalkButton;
        TalkReserve.Take(talk != null, talk?.Value, dominantLeft);
        ChordReserve[0].Take(Diagnostics.DebugGesture.MenuTaken(true), true, VRControl.Menu);
        ChordReserve[1].Take(Diagnostics.DebugGesture.MenuTaken(false), false, VRControl.Menu);
        ChordReserve[2].Take(Diagnostics.DebugGesture.StickTaken(true), true, VRControl.StickClick);
        ChordReserve[3]
            .Take(Diagnostics.DebugGesture.StickTaken(false), false, VRControl.StickClick);
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
        Mapper.Update(Controller(VRInput.Left), Controller(VRInput.Right), Time.unscaledDeltaTime);

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

    private static ControllerInput Controller(in HandState h) =>
        new()
        {
            Trigger = h.Trigger,
            Grip = h.Grip,
            Primary = h.Primary,
            Secondary = h.Secondary,
            StickClick = h.StickClick,
            Menu = h.Menu,
            Stick = Num2(h.Stick),
        };

    private static NVector2 MoveStick(bool dominantLeft, float dt)
    {
        // One-handed, the one stick also turns (LookPatches): it walks only with the forward/back part.
        bool one = VRInput.OneHanded;
        ref var hand = ref VRInput.Hand(one ? dominantLeft : !dominantLeft);
        var raw = Num2(hand.Stick);
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
    /// Room-scale: the camera feature keeps <see cref="VRRig.RoomAnchor"/> over the body, so stepping moves the view
    /// exactly like the real head. Past the lean radius the body is walked after the head with stick input, and the
    /// anchor follows the body as it arrives, so the view never moves on its own. A blocked body (wall, ledge,
    /// climbing) past 1 m gets one fade-out, recenter, fade-in. No teleport, no rig pulling.
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
            RigDriver.LeanNeutral = NVector2.Zero;
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
        bool canFollow =
            hadBody
            && LocomotionConfig.RoomScale
            && stickIdle
            // A walking stride can leave the ground for a frame; that must not end a follow (and its settle) or
            // drop what the anchor absorbed, which hands the walk's travel to the view.
            && (data.isGrounded || data.sinceGrounded < 0.25f)
            && data.fullyConscious
            && !data.isClimbingAnything
            && data.currentClimbHandle == null
            // The top-out walk moves the body onto the ledge under its own input: a follow toward the head behind
            // it fought that walk, and its stall's recenter fade moved the gripping hands (vr.54, run 54).
            && !PhysicalInteraction.Mantling
            && data.carrier == null;
        var head = new NVector2(VRInput.HeadPosition.x, VRInput.HeadPosition.z);
        Follow.LeanRadius = LocomotionConfig.LeanRadius;
        Follow.Anchor = rig.RoomAnchor;
        // No lean left to stand up from (RoomScaleFollow.Update): the head within 3 cm of the calibrated standing
        // eye height and the avatar not bending into a lean. Run 56's held far lean sat only 6 cm low with a 16
        // degree bend; read as standing, it lost the stand-up's exemption and standing up walked the body back.
        var pose = AvatarPoseDriver.Of(c);
        bool standingTall =
            Mathf.Abs(VRInput.HeadPosition.y - RigMath.TrackedEyeHeight(rig.HeightOffset)) < 0.03f
            && (pose == null || pose.LeanDegrees < 1f);
        var request = Follow.Update(
            head,
            new NVector2(delta.x, delta.z),
            canFollow,
            dt,
            standingTall
        );
        if (!canFollow && stickIdle && Follow.Offset.Length() > Follow.LeanRadius)
            FollowDrops++;
        lastCanFollow = canFollow;
        WarnFar(c, canFollow, stickIdle);
        Fade.DrawnFade = UI.Common.FadeView.Alpha;
        if (Fade.Update(Follow.RecenterNeeded, dt))
            Follow.Recenter(head);
        rig.RoomAnchor = Follow.Anchor;
        RigDriver.LeanNeutral = LocomotionConfig.RoomScale ? Follow.Neutral : NVector2.Zero;
        RigDriver.FollowWalking = LocomotionConfig.RoomScale && canFollow && Follow.Walking;
        if (Fade.Target > 0f)
            VRFade.Set(FadeOwner, Fade.Target);
        else
            VRFade.Clear(FadeOwner);
        var stick = MoveInput.TrackingToStick(request, HeadYaw());
        if (RigDriver.LeanTraceDue(ref nextLeanTrace))
            Plugin.Log.LogInfo(
                System.FormattableString.Invariant(
                    $"[player.locomotion] room t{Time.unscaledTime:0.00} head-anchor ({Follow.Offset.X:0.000},{Follow.Offset.Y:0.000}) body-delta ({delta.x:0.000},{delta.z:0.000}) follow {canFollow} stick fwd {stick.Y:0.00} right {stick.X:0.00} neutral ({Follow.Neutral.X:0.000},{Follow.Neutral.Y:0.000}) head y {VRInput.HeadPosition.y:0.000} vs standing {RigMath.TrackedEyeHeight(rig.HeightOffset):0.000} lean {(pose != null ? pose.LeanDegrees : 0f):0.0} deg standing-tall {standingTall} stand-up travel {Follow.FollowTravel:0.000} stalled {Follow.Stalled}"
                )
            );
        return stick;
    }

    private static float nextLeanTrace;

    /// <summary>Head this far (m) from a body that cannot follow: warned before the 1 m recenter fade.</summary>
    private const float FarWarning = 0.8f;

    /// <summary>
    /// The head drifted from a body that cannot step after it (climbing, hanging, airborne, carried), or the body's
    /// follow walk stalled against something: before the recenter fade, a NotNow refusal on the dominant hand with a ring at the body's feet, the spot to come back to.
    /// Once per drift; armed again back inside half the distance.
    /// </summary>
    private static void WarnFar(Character c, bool canFollow, bool stickIdle)
    {
        float far = Follow.Offset.Length();
        if (far < FarWarning * 0.5f)
            warnedFar = false;
        // A follow the body could not finish (RoomScaleFollow.Stalled) also fades: it gets the same reason.
        bool stalled = Follow.Stalled;
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
    /// WS-D4: while spectating, point the dominant hand at a teammate and pull its trigger to watch them. Vanilla
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
        ref var hand = ref VRInput.Hand(dominantLeft);
        bool down = spectating && hand.Tracked && hand.Trigger > (pointTrigger ? 0.3f : 0.7f);
        bool pressed = down && !pointTrigger;
        pointTrigger = down;
        if (!spectating)
        {
            watchGoal = null;
            return;
        }
        var spec = MainCameraMovement.specCharacter;
        if (pressed)
        {
            var pick = PointedTeammate(c, dominantLeft);
            if (pick == null)
                Affordance.DenyAtHand(dominantLeft, DenyReason.CantSwitch);
            else if (pick == spec)
                Affordance.Contact(dominantLeft);
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
            Affordance.Contact(dominantLeft);
            watchGoal = null;
            return;
        }
        // Your own body cannot be switched away from (MainCameraMovement.cs:189).
        if (!watchGoal.data.canBeSpectated || watchPressesLeft <= 0 || spec == c)
        {
            Affordance.DenyAtHand(dominantLeft, DenyReason.CantSwitch);
            watchGoal = null;
            return;
        }
        if (Time.unscaledTime < watchNextPress)
            return;
        input.spectateRightWasPressed = true;
        watchPressesLeft--;
        watchNextPress = Time.unscaledTime + WatchStep;
    }

    /// <summary>The watchable teammate nearest the dominant hand's pointing ray, within <see cref="PointCone"/>.</summary>
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
