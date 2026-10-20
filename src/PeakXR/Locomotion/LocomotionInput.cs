using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Interact;
using PeakXR.Rig;
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
    private static readonly RecenterFade Fade = new();
    private static readonly object FadeOwner = new();

    /// <summary>interact.physical is installed and handles interact, use and drop.</summary>
    public static bool PhysicalOwnsHands;

    private static int lastFrame = -10;
    private static int backpackFrame = -10;
    private static bool wroteSticky;
    private static Vector3 lastBody;
    private static bool hadBody;
    private static bool hasReserved;
    private static bool reservedLeft;
    private static VRControl reservedControl;

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

    /// <summary>Keeps layout actions off the button interact.physical uses for drop, so one press never drops and
    /// pings (e.g. a fixed <c>RightSecondary</c> for a left-handed player).</summary>
    private static void ReserveDropButton(bool owns, bool dominantLeft)
    {
        if (hasReserved)
            Mapper.SetReserved(reservedLeft, reservedControl, false);
        hasReserved =
            owns
            && ButtonName.TryParse(
                InteractConfig.Instance.DropButton,
                dominantLeft,
                out reservedLeft,
                out reservedControl
            );
        if (hasReserved)
            Mapper.SetReserved(reservedLeft, reservedControl, true);
    }

    /// <param name="active">The game's <c>playerMovementActive</c>: no menu window or wheel blocks input.</param>
    public static void Apply(Character c, CharacterInput input, bool active)
    {
        float dt = Time.deltaTime;
        lastFrame = Time.frameCount;
        bool dominantLeft = VRInput.LeftHanded;
        var item = c.data.currentItem;

        Mapper.DominantLeft = dominantLeft;
        Mapper.ItemInLeft = item != null ? LocalHold.ItemInLeftHand : dominantLeft;
        bool owns = PhysicalOwnsHands;
        Mapper.SetDisabled(VRAction.Interact, owns);
        Mapper.SetDisabled(VRAction.UsePrimary, owns);
        Mapper.SetDisabled(VRAction.UseSecondary, owns);
        Mapper.SetDisabled(VRAction.Drop, owns);
        ReserveDropButton(owns, dominantLeft);
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

        // Vanilla samples these even while input is blocked (CharacterInput.cs:215-219, :262-264): the emote wheel
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
        input.unselectSlotWasPressed |= canSwitch && Mapper.Pressed(VRAction.PutAway);
        if (Mapper.Pressed(VRAction.Backpack))
        {
            input.selectBackpackWasPressed = true;
            backpackFrame = Time.frameCount;
        }
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
        ref var hand = ref VRInput.Hand(!dominantLeft);
        var stick = Stick.Apply(Num2(hand.Stick), LocomotionConfig.Deadzone, 0.95f);
        if (LocomotionConfig.HandRelative && hand.Tracked && VRInput.HeadTracked)
            stick = MoveInput.HandRelative(
                stick,
                HeadYaw(),
                MoveInput.Yaw((hand.Rotation * Vector3.forward).N())
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
        var body = c.refs.hip != null ? c.refs.hip.transform.position : c.Center;
        if (rig == null || rig.Origin == null || !VRInput.HeadTracked)
        {
            lastBody = body;
            hadBody = false;
            return NVector2.Zero;
        }

        var delta = hadBody ? rig.Origin.InverseTransformVector(body - lastBody) : Vector3.zero;
        lastBody = body;
        hadBody = true;

        var data = c.data;
        bool canFollow =
            LocomotionConfig.RoomScale
            && stickIdle
            && data.isGrounded
            && data.fullyConscious
            && !data.isClimbing
            && !data.isRopeClimbing
            && !data.isVineClimbing
            && data.carrier == null;
        var head = new NVector2(VRInput.HeadPosition.x, VRInput.HeadPosition.z);
        Follow.LeanRadius = LocomotionConfig.LeanRadius;
        Follow.Anchor = rig.RoomAnchor;
        var request = Follow.Update(head, new NVector2(delta.x, delta.z), canFollow);
        if (Fade.Update(Follow.RecenterNeeded, dt))
            Follow.Recenter(head);
        rig.RoomAnchor = Follow.Anchor;
        if (Fade.Target > 0f)
            VRFade.Set(FadeOwner, Fade.Target);
        else
            VRFade.Clear(FadeOwner);
        return MoveInput.TrackingToStick(request, HeadYaw());
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
    /// ResetInput does not clear these while input is blocked (CharacterInput.cs:297 vs :212), so a press written on
    /// the last unblocked frame would repeat every frame a menu stays open. Clear them once on that transition.
    /// </summary>
    private static void ClearSticky(CharacterInput input)
    {
        if (!wroteSticky)
            return;
        wroteSticky = false;
        input.pingWasPressed = false;
        input.selectBackpackWasPressed = false;
        input.scrollForwardWasPressed = input.scrollForwardIsPressed = false;
        input.scrollBackwardWasPressed = input.scrollBackwardIsPressed = false;
        input.spectateLeftWasPressed = input.spectateRightWasPressed = false;
        input.sprintToggleIsPressed = false;
    }

    private static NVector2 Num2(Vector2 v) => new(v.x, v.y);

    private static Vector2 ToUnity(NVector2 v) => new(v.X, v.Y);
}
