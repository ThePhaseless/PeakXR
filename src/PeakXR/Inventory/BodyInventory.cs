using System;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Interact;
using PeakXR.UI.Affordances;
using PeakXR.VR;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;

namespace PeakXR.Inventory;

/// <summary>What a body slot marker shows.</summary>
internal enum SlotLook
{
    Hidden,
    Empty,
    Filled,

    /// <summary>This slot's item is in a hand.</summary>
    Selected,

    /// <summary>The held item sits in the overflow slot: there is no room on the belt.</summary>
    Full,
}

/// <summary>
/// Body-anchored hotbar of the local VR player. Anchors live in tracking space (neck pivot plus a body yaw that
/// ignores head pitch, see <see cref="BodySlotLayout"/>), so they follow the tracked head height and turn but not
/// snap turns or looking down. Draws and holsters are fed to the game as one-frame hotbar key presses
/// (<c>CharacterInput.SelectSlotWasPressed</c>), so <c>CharacterItems.DoSwitching</c> applies every vanilla gate and
/// calls the networked <c>EquipSlot</c>. Grips arrive through <see cref="IGripExtension"/> from
/// <c>interact.physical</c>.
/// </summary>
internal sealed class BodyInventory : IGripExtension
{
    public static readonly BodyInventory Instance = new();

    /// <summary><c>CharacterItems.lockedFromSwitching</c> getter (CharacterItems.cs:84, private).</summary>
    public static Func<CharacterItems, bool> LockedFromSwitching;

    /// <summary><c>Character.CanDoInput</c> (Character.cs:1714, internal).</summary>
    public static Func<Character, bool> CanDoInput;

    /// <summary><c>BackpackWheel.TestSelectSliceGamepad</c> (BackpackWheel.cs:253, protected): hovers the slice
    /// nearest a direction.</summary>
    public static Action<BackpackWheel, Vector2> SelectWheelSlice;

    /// <summary>Most anchors a layout has: the hotbar keys plus the two shoulder anchors.</summary>
    public const int MaxSlots = BodySlotLayout.MaxHotbar + 2;
    private const byte PackSlot = 3;
    private const byte OverflowSlot = 250;
    private const float WheelDeadzone = 0.06f;

    private BodySlot[] slots = BodySlotLayout.Create(
        BodySlotLayout.Belt,
        BodySlotLayout.MaxHotbar,
        0.12f,
        0f
    );
    private readonly NVector3[] anchors = new NVector3[MaxSlots];
    private readonly BodyYaw bodyYaw = new();
    private int revision = -1;
    private int hotbarCount = -1;

    private int pendingKey = -1;
    private int pendingFrame;
    private bool pendingLeft;

    // Per hand (0 left, 1 right).
    private readonly bool[] shoulderGrip = new bool[2];
    private readonly int[] drawSlot = { -1, -1 };
    private readonly bool[] leftDrawSlot = new bool[2];
    private readonly int[] nearSlot = { -1, -1 };
    private readonly NVector3[] wheelStart = new NVector3[2];

    private int wheelHand = -1;
    private bool wheelWasOpen;
    private float secondPulseAt = -1f;
    private bool secondPulseLeft;

    /// <summary>Anchors are valid for this frame (VR active, head tracked, feature on).</summary>
    public bool Valid { get; private set; }

    /// <summary>Anchors in the current layout: the game's hotbar slots, then the two shoulder anchors.</summary>
    public int Count => slots.Length;

    public BodySlot Slot(int i) => slots[i];

    /// <summary>A tracked hand is inside this slot.</summary>
    public bool Hovered(int i) => nearSlot[0] == i || nearSlot[1] == i;

    /// <summary>Tracking-space heading the slot faces out along: body yaw plus its place on the arc.</summary>
    public Quaternion Facing(int i) => Quaternion.Euler(0f, bodyYaw.Yaw + slots[i].Heading, 0f);

    /// <summary>Anchor in tracking space (relative to <see cref="VRRig.Origin"/>).</summary>
    public Vector3 Anchor(int i) => anchors[i].U();

    /// <summary>Per-frame update from the <c>CharacterInput.Sample</c> postfix, after <c>interact.physical</c>.</summary>
    public void Tick(Character c, CharacterInput input, float dt)
    {
        var cfg = InventoryConfig.Instance;
        Valid = VRSession.Active && cfg.BodySlots && VRInput.HeadTracked;
        if (secondPulseAt > 0f && Time.time >= secondPulseAt)
        {
            secondPulseAt = -1f;
            Haptics.Pulse(secondPulseLeft, 0.5f, 0.04f);
        }
        if (pendingKey >= 0 && Time.frameCount - pendingFrame > 1)
        {
            // DoSwitching returned early on one of its gates (mid-use, climbing, cooldown...): say so.
            pendingKey = -1;
            DoublePulse(pendingLeft, DenyReason.CantSwitch);
        }
        if (!Valid)
        {
            wheelHand = -1;
            shoulderGrip[0] = shoulderGrip[1] = false;
            drawSlot[0] = drawSlot[1] = nearSlot[0] = nearSlot[1] = -1;
            bodyYaw.Reset();
            return;
        }

        UpdateAnchors(c, cfg, dt);
        UpdateHand(c, 0);
        UpdateHand(c, 1);

        // Hold the game's interact while the shoulder grip that opened the wheel stays closed: the wheel commits the
        // hovered pocket when interact is released (BackpackWheel.Update, BackpackWheel.cs:92).
        if (wheelHand >= 0)
        {
            if (c.data.usingBackpackWheel)
                input.interactIsPressed = true;
            else
                wheelHand = -1;
        }
        DriveWheel(c, cfg);
    }

    private void UpdateAnchors(Character c, InventoryConfig cfg, float dt)
    {
        // One belt slot per game hotbar slot (Player.itemSlots, Player.cs:17), capped at the keys DoSwitching reads.
        int hotbar = c.player != null ? c.player.itemSlots.Length : BodySlotLayout.MaxHotbar;
        if (revision != cfg.Revision || hotbar != hotbarCount)
        {
            revision = cfg.Revision;
            hotbarCount = hotbar;
            slots = BodySlotLayout.Create(cfg.Layout, hotbar, cfg.Radius, cfg.Height);
            // Indices into the old array mean nothing now.
            nearSlot[0] = nearSlot[1] = drawSlot[0] = drawSlot[1] = -1;
        }
        var headPos = VRInput.HeadPosition.N();
        var headRot = VRInput.HeadRotation.N();
        float yaw = bodyYaw.Update(
            BodySlotLayout.HeadYawDeg(headRot),
            BodySlotLayout.HeadPitchDeg(headRot),
            dt
        );
        var neck = BodySlotLayout.Neck(headPos, headRot);
        for (int i = 0; i < slots.Length; i++)
            anchors[i] = BodySlotLayout.Anchor(neck, yaw, slots[i]);
    }

    private void UpdateHand(Character c, int hand)
    {
        ref var state = ref VRInput.Hand(hand == 0);
        int near = -1;
        if (state.Tracked)
        {
            // Hysteresis: the slot a hand is in lets go only 10% past its radius, so an edge does not flicker.
            int was = nearSlot[hand];
            float r = was >= 0 ? slots[was].Radius * 1.1f : 0f;
            near =
                was >= 0 && NVector3.DistanceSquared(state.Position.N(), anchors[was]) <= r * r
                    ? was
                    : Find(state.Position.N(), 1f);
        }
        if (near >= 0 && near != nearSlot[hand])
        {
            // Tick on entering a slot this hand can use: a filled one to draw, any one to put the held item in.
            var look = Look(c, near);
            bool holds = c.data.currentItem != null && LocalHold.ItemInLeftHand == (hand == 0);
            if (look is SlotLook.Filled or SlotLook.Full || (holds && look != SlotLook.Hidden))
                Haptics.Pulse(hand == 0, 0.15f, 0.02f);
        }
        nearSlot[hand] = near;

        // A drawn item may be holstered by the same grip only after the hand has left the slot once.
        int drawn = drawSlot[hand];
        if (drawn >= 0 && !leftDrawSlot[hand] && state.Tracked)
        {
            float r = slots[drawn].Radius * 1.5f;
            leftDrawSlot[hand] =
                NVector3.DistanceSquared(state.Position.N(), anchors[drawn]) > r * r;
        }
    }

    /// <summary>Distance from the nearer tracked hand to an anchor, in metres.</summary>
    public float NearestHandDistance(int i)
    {
        float best = float.MaxValue;
        if (VRInput.Left.Tracked)
            best = Math.Min(best, NVector3.Distance(VRInput.Left.Position.N(), anchors[i]));
        if (VRInput.Right.Tracked)
            best = Math.Min(best, NVector3.Distance(VRInput.Right.Position.N(), anchors[i]));
        return best;
    }

    public SlotLook Look(Character c, int i)
    {
        if (c == null || c.player == null)
            return SlotLook.Hidden;
        var slot = slots[i];
        var selected = c.refs.items.currentSelectedSlot;
        bool holding = c.data.currentItem != null;
        if (slot.GameSlot == PackSlot)
        {
            if (!InventoryConfig.Instance.BackpackOverShoulder)
                return SlotLook.Hidden;
            if (holding && selected.IsSome && selected.Value == PackSlot)
                return SlotLook.Selected;
            return c.player.backpackSlot.IsEmpty() ? SlotLook.Hidden : SlotLook.Filled;
        }
        if (holding && selected.IsSome && selected.Value == OverflowSlot)
            return SlotLook.Full;
        if (holding && selected.IsSome && selected.Value == slot.GameSlot)
            return SlotLook.Selected;
        var itemSlot = c.player.GetItemSlot(slot.GameSlot);
        return itemSlot == null || itemSlot.IsEmpty() ? SlotLook.Empty : SlotLook.Filled;
    }

    private int Find(NVector3 trackingPos, float scale) =>
        BodySlotLayout.Find(trackingPos, anchors, slots, slots.Length, scale);

    private static int HandIndex(bool left) => left ? 0 : 1;

    // The hotbar keys put the backpack at 3 (Player.cs:47, CharacterItems.cs:794); stay inert if the game changes that.
    private bool Ready(Character c) =>
        Valid
        && c == Character.localCharacter
        && c.player != null
        && c.player.backpackSlot.itemSlotID == PackSlot;

    // ------------------------------------------------------------------ grip hooks

    public bool ClaimPress(in GripHookContext ctx)
    {
        var c = ctx.Character;
        if (!Ready(c))
            return false;
        int i = Find(VRInput.Hand(ctx.Left).Position.N(), 1f);
        if (i < 0)
            return false;
        var slot = slots[i];
        if (slot.GameSlot == PackSlot)
        {
            // A holding hand at the shoulder keeps its own grip: opening it there stashes (TryHeldRelease).
            if (!InventoryConfig.Instance.BackpackOverShoulder || ctx.ThisHandHolds)
                return false;
            return c.data.carriedPlayer != null || !c.player.backpackSlot.IsEmpty();
        }
        var look = Look(c, i);
        // Drawing needs a filled slot whose item is not already in a hand; reaching to an empty slot stays free for
        // grabbing or climbing. The game refuses switches while climbing (CharacterItems.cs:735), so a climbing
        // hand over a slot catches the wall instead.
        return look == SlotLook.Filled && !c.data.isClimbing;
    }

    public void BeginPress(in GripHookContext ctx)
    {
        var c = ctx.Character;
        int hand = HandIndex(ctx.Left);
        int i = Find(VRInput.Hand(ctx.Left).Position.N(), 1f);
        if (i < 0)
            return;
        if (slots[i].GameSlot == PackSlot)
        {
            BeginShoulder(c, ctx.Left);
            return;
        }
        if (SwitchBlocked(c))
        {
            DoublePulse(ctx.Left, BlockReason(c));
            return;
        }
        drawSlot[hand] = i;
        leftDrawSlot[hand] = false;
        PressKey(slots[i].GameSlot, ctx.Left);
        PhysicalInteraction.ExpectItemIn(ctx.Left);
        Haptics.Pulse(ctx.Left, 0.5f, 0.05f);
    }

    public bool EndPress(in GripHookContext ctx)
    {
        int hand = HandIndex(ctx.Left);
        if (shoulderGrip[hand])
        {
            shoulderGrip[hand] = false;
            if (wheelHand == hand)
                wheelHand = -1;
            return true;
        }
        int drawn = drawSlot[hand];
        bool leftSlot = leftDrawSlot[hand];
        drawSlot[hand] = -1;
        if (!ctx.ThisHandHolds)
            return true;
        // Changed your mind: the drawn item goes back if the grip opens over a slot after leaving it.
        if (drawn >= 0 && leftSlot && TryHeldRelease(ctx))
            return true;
        return false;
    }

    public bool TryHeldRelease(in GripHookContext ctx)
    {
        var c = ctx.Character;
        if (!Ready(c) || !InventoryConfig.Instance.HolsterOnRelease || !ctx.ThisHandHolds)
            return false;
        int i = Find(VRInput.Hand(ctx.Left).Position.N(), 1.25f);
        if (i < 0)
            return false;
        if (slots[i].GameSlot == PackSlot)
            return ReleaseAtShoulder(c, ctx.Left);
        Holster(c, ctx.Left);
        return true;
    }

    // ------------------------------------------------------------------ actions

    private void Holster(Character c, bool left)
    {
        var item = c.data.currentItem;
        var selected = c.refs.items.currentSelectedSlot;
        // Vanilla drops overflow and non-pocketable items instead of stowing them (CharacterItems.cs:495); keep them
        // in hand and say no.
        if (
            item == null
            || selected.IsNone
            || selected.Value > PackSlot
            || !item.UIData.canPocket
            || SwitchBlocked(c)
        )
        {
            DoublePulse(
                left,
                item != null
                && (selected.IsNone || selected.Value > PackSlot || !item.UIData.canPocket)
                    ? DenyReason.CantHolster
                    : BlockReason(c)
            );
            return;
        }
        // Pressing the selected slot again holsters (CharacterItems.cs:794).
        PressKey(selected.Value, left);
        Haptics.Pulse(left, 0.4f, 0.04f);
    }

    private bool ReleaseAtShoulder(Character c, bool left)
    {
        if (!InventoryConfig.Instance.BackpackOverShoulder)
            return false;
        var selected = c.refs.items.currentSelectedSlot;
        if (selected.IsSome && selected.Value == PackSlot)
        {
            // The pack itself is in hand: put it back on.
            if (SwitchBlocked(c))
                DoublePulse(left, BlockReason(c));
            else
                PressKey(PackSlot, left);
            return true;
        }
        if (c.player.backpackSlot.IsEmpty())
            return false;
        int free = FreePocket(c);
        if (
            free < 0
            || selected.IsNone
            || selected.Value > PackSlot
            || SwitchBlocked(c)
            || c.player.backpackSlot.backpackType == BackpackSlot.BackpackType.Rocketpack
            // The wheel refuses these too (BackpackWheelSlice.cs:84, Item.cs:69).
            || c.data.currentItem == null
            || !c.data.currentItem.UIData.canBackpack
        )
        {
            DoublePulse(
                left,
                SwitchBlocked(c) ? BlockReason(c)
                    : free < 0 ? DenyReason.PackFull
                    : DenyReason.CantHolster
            );
            return true;
        }
        // The wheel's stash path for a worn pack (BackpackWheel.TryStash -> CharacterBackpackHandler.cs:127).
        c.refs.backpackHandler.StashInBackpack(c, (byte)free);
        Haptics.Pulse(left, 0.5f, 0.05f);
        return true;
    }

    private void BeginShoulder(Character c, bool left)
    {
        int hand = HandIndex(left);
        shoulderGrip[hand] = true;

        // Hotbar 4 while carrying drops the carried friend (CharacterItems.cs:821).
        if (c.data.carriedPlayer != null)
        {
            PressKey(PackSlot, left);
            Haptics.Pulse(left, 0.5f, 0.05f);
            return;
        }
        if (c.player.backpackSlot.IsEmpty())
            return;

        if (shoulderGrip[1 - hand])
        {
            // Both hands over the shoulders: take the pack off into the hands (slot 4, IC-25).
            wheelHand = -1;
            if (c.data.usingBackpackWheel && GUIManager.instance != null)
                GUIManager.instance.CloseBackpackWheel();
            if (SwitchBlocked(c))
            {
                DoublePulse(left, BlockReason(c));
                return;
            }
            PressKey(PackSlot, left);
            PhysicalInteraction.ExpectItemIn(left);
            Haptics.Pulse(left, 0.6f, 0.06f);
            return;
        }

        var visuals = c.refs.backpackHandler.activeBackpackVisuals;
        var type = c.player.backpackSlot.backpackType;
        // A worn rocket pack has no pockets: its interact lights it. Never light it by reaching over the shoulder.
        if (
            visuals == null
            || type == BackpackSlot.BackpackType.Rocketpack
            || c.data.usingWheel
            || CanDoInput == null
            || !CanDoInput(c)
        )
        {
            DoublePulse(left, DenyReason.CantSwitch);
            return;
        }
        // Same call the worn pack's own interaction makes (BackpackOnBackVisuals.cs:162): opens the wheel.
        visuals.Interact_CastFinished(c);
        if (c.data.usingBackpackWheel)
        {
            wheelHand = hand;
            Haptics.Pulse(left, 0.5f, 0.05f);
        }
    }

    private static int FreePocket(Character c)
    {
        var visuals = c.refs.backpackHandler.activeBackpackVisuals;
        var data = BackpackReference.GetFromEquippedBackpack(c).GetData();
        if (visuals == null || data == null)
            return -1;
        int count = Math.Min(visuals.slotCount, data.itemSlots.Length);
        for (int i = 0; i < count; i++)
        {
            if (data.itemSlots[i] != null && data.itemSlots[i].IsEmpty())
                return i;
        }
        return -1;
    }

    /// <summary>Hovers wheel pockets by hand movement while any backpack wheel is open and a grip is held.</summary>
    private void DriveWheel(Character c, InventoryConfig cfg)
    {
        var gui = GUIManager.instance;
        var wheel = gui != null ? gui.backpackWheel : null;
        bool open = wheel != null && c.data.usingBackpackWheel && wheel.gameObject.activeSelf;
        if (open && !wheelWasOpen)
        {
            wheelStart[0] = VRInput.Left.Position.N();
            wheelStart[1] = VRInput.Right.Position.N();
        }
        wheelWasOpen = open;
        if (!open || !cfg.WheelByHand || SelectWheelSlice == null)
            return;

        int hand = wheelHand;
        if (hand < 0)
        {
            float best = WheelDeadzone * WheelDeadzone;
            for (int h = 0; h < 2; h++)
            {
                ref var s = ref VRInput.Hand(h == 0);
                if (!s.Tracked || s.Grip < 0.5f)
                    continue;
                float d = NVector3.DistanceSquared(s.Position.N(), wheelStart[h]);
                if (d > best)
                {
                    best = d;
                    hand = h;
                }
            }
        }
        if (hand < 0)
            return;
        ref var state = ref VRInput.Hand(hand == 0);
        if (!state.Tracked)
            return;
        var local =
            Quaternion.Inverse(VRInput.HeadRotation) * (state.Position - wheelStart[hand].U());
        var dir = new Vector2(local.x, local.y);
        if (dir.sqrMagnitude < WheelDeadzone * WheelDeadzone)
            return;
        SelectWheelSlice(wheel, dir.normalized);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>The <c>CharacterItems.DoSwitching</c> gates (CharacterItems.cs:735), checked up front for feedback.</summary>
    private static bool SwitchBlocked(Character c)
    {
        var item = c.data.currentItem;
        if (item != null && (item.progress > 0f || item.lastFinishedCast + 0.1f > Time.time))
            return true;
        return !c.data.fullyConscious
            || (CanDoInput != null && !CanDoInput(c))
            || (LockedFromSwitching != null && LockedFromSwitching(c.refs.items))
            || c.data.isClimbing
            || c.data.isRopeClimbing
            || c.data.currentStickyItem != null
            || c.input.itemSwitchBlocked;
    }

    private void PressKey(int key, bool left)
    {
        pendingKey = key;
        pendingFrame = Time.frameCount;
        pendingLeft = left;
    }

    /// <summary>Hotbar key press for <c>SelectSlotWasPressed</c>: true once for the pending key, within two frames.</summary>
    public bool ConsumeKey(CharacterInput input, int key)
    {
        if (pendingKey != key || Time.frameCount - pendingFrame > 1 || input.itemSwitchBlocked)
            return false;
        var c = Character.localCharacter;
        if (c == null || c.input != input)
            return false;
        pendingKey = -1;
        return true;
    }

    /// <summary>Hand-lock cue: two short pulses instead of a silent failure (IC-05).</summary>
    /// <summary>Why <see cref="SwitchBlocked"/> holds: climbing (CharacterItems.cs:735-741), mid-use or just used
    /// (the cooldown at :72-84), else a generic busy state.</summary>
    private static DenyReason BlockReason(Character c)
    {
        if (c.data.isClimbing || c.data.isRopeClimbing)
            return DenyReason.NotWhileClimbing;
        var item = c.data.currentItem;
        if (item != null && (item.progress > 0f || item.lastFinishedCast + 0.1f > Time.time))
            return DenyReason.TooSoon;
        return DenyReason.CantSwitch;
    }

    /// <summary>The body slots' "no": the shared denied cue with its reason when ui.affordance runs, else the old
    /// double pulse.</summary>
    private void DoublePulse(bool left, DenyReason reason)
    {
        if (Affordance.Enabled)
        {
            Affordance.DenyAtHand(left, reason);
            return;
        }
        Haptics.Pulse(left, 0.5f, 0.04f);
        secondPulseAt = Time.time + 0.12f;
        secondPulseLeft = left;
    }
}
