using PeakXR.Core;
using PeakXR.UI.Common;
using PeakXR.UI.World;
using PeakXR.VR;
using UnityEngine;
using UnityEngine.EventSystems;
using NVector2 = System.Numerics.Vector2;

namespace PeakXR.UI.Wheels;

/// <summary>
/// Drives the game's two wheels in VR. When the game opens a wheel (GUIManager.UpdateEmoteWheel A/GUIManager.cs:561
/// on the Emote binding, OpenBackpackWheel :546 on interact), it is lifted out of the HUD panel into a
/// <see cref="HudBox"/> and pinned, facing you, where the opening hand was. Each frame the hand's travel from
/// that spot (or the hand's stick) picks a slice through <see cref="RadialSelector"/>, and the slice's own
/// Hover/Dehover run, so releasing the button resolves through the game's Choose exactly as with a gamepad.
/// The emote wheel is picked by hand travel or the opening hand's stick. The backpack wheel is picked by
/// inventory.vr (hand movement, <c>Inventory.WheelByHand</c>) through the game's own
/// <c>TestSelectSliceGamepad</c>; here it only gets a place at the hand, plus its stick as an alternative.
/// Both go through the TestSelectSliceGamepad patches (WheelPatches.cs), which read such a call in the wheel's own plane.
/// </summary>
[DefaultExecutionOrder(31050)]
internal sealed class RadialWheels : MonoBehaviour
{
    private static RadialWheels instance;

    private readonly WheelDriver emote = new("PeakXR_EmoteWheel", handDriven: true);
    private readonly WheelDriver backpack = new("PeakXR_BackpackWheel", handDriven: false);
    private GUIManager gui;
    private EmoteWheel emoteWheel;

    public static void Create()
    {
        var go = new GameObject("PeakXR_RadialWheels");
        DontDestroyOnLoad(go);
        go.AddComponent<RadialWheels>();
    }

    private void Awake() => instance = this;

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    /// <summary>
    /// A call to a wheel's <c>TestSelectSliceGamepad</c> while it is on a VR hand: <paramref name="direction"/> is
    /// read in the wheel's plane (x right, y up) instead of the game's world axes, which only fit a wheel facing
    /// +Z. Returns false when the wheel is not managed here and the game's own code should run.
    /// </summary>
    public static bool Steer(UIWheel wheel, Vector2 direction)
    {
        if (instance == null)
            return false;
        if (instance.emote.Owns(wheel))
            instance.emote.Steer(new NVector2(direction.x, direction.y), authoritative: false);
        else if (instance.backpack.Owns(wheel))
            instance.backpack.Steer(new NVector2(direction.x, direction.y), authoritative: false);
        else
            return false;
        return true;
    }

    private void LateUpdate()
    {
        var current = GUIManager.instance;
        if (current != gui)
        {
            emote.Forget();
            backpack.Forget();
            gui = current;
            // GUIManager.emoteWheel is the GameObject the game toggles (A/GUIManager.cs:567); the wheel may sit below it.
            emoteWheel =
                gui != null && gui.emoteWheel != null
                    ? gui.emoteWheel.GetComponentInChildren<EmoteWheel>(true)
                    : null;
        }

        var rig = VRRig.Instance;
        if (!UIWorldRuntime.Ready || gui == null || rig == null || rig.Origin == null)
        {
            emote.Restore();
            backpack.Restore();
            return;
        }

        emote.Update(gui.emoteWheel, emoteWheel, rig, VRAction.Emote);
        backpack.Update(
            gui.backpackWheel != null ? gui.backpackWheel.gameObject : null,
            gui.backpackWheel,
            rig,
            VRAction.Interact
        );
    }

    /// <summary>
    /// The controller that opened a wheel. Emote comes from the binding table (Y hold by default). The backpack
    /// wheel opens on interact, which physical interaction presses with a closed grip, so a single closed grip
    /// names the hand; otherwise the Interact binding's hand.
    /// </summary>
    public static bool OpeningHandLeft(VRAction action)
    {
        if (action == VRAction.Interact)
        {
            bool leftGrip = VRInput.Left.Grip > 0.5f,
                rightGrip = VRInput.Right.Grip > 0.5f;
            if (leftGrip != rightGrip)
                return leftGrip;
        }
        var role = BindingHand.Dominant;
        foreach (var b in BindingTable.Default)
            if (b.Action == action)
            {
                role = b.Hand;
                break;
            }
        return BindingTable.ResolveLeft(role, VRInput.LeftHanded, VRInput.LeftHanded);
    }
}

/// <summary>One wheel: placement, slice directions and the selection it feeds back to the game.</summary>
internal sealed class WheelDriver
{
    /// <summary>UIWheelSlice.offsetRotation (A/UIWheelSlice.cs:8): slice graphics point 22.5 degrees off their up.</summary>
    private const float SliceOffsetDegrees = 22.5f;
    private const float HoverTickSeconds = 0.02f;

    private readonly HudBox box;
    private readonly bool handDriven;
    private readonly RadialSelector selector = new();
    private UIWheel wheel;
    private RectTransform direct;
    private UIWheelSlice[] slices = System.Array.Empty<UIWheelSlice>();
    private NVector2[] directions = System.Array.Empty<NVector2>();
    private bool[] enabled = System.Array.Empty<bool>();
    private bool open,
        left;
    private int selected = -1;
    private Vector3 centerLocal;
    private Quaternion rotationLocal;
    private Vector2 boxCenter;
    private float span = 1f;

    /// <param name="handDriven">The hand's travel from the wheel centre picks the slice (emote). Off: only the
    /// stick does here, and another feature steers by hand (backpack, inventory.vr).</param>
    public WheelDriver(string name, bool handDriven)
    {
        box = new HudBox(name);
        this.handDriven = handDriven;
    }

    /// <summary>The wheel is open, on a VR hand, and is <paramref name="candidate"/>.</summary>
    public bool Owns(UIWheel candidate) => open && wheel == candidate;

    private RectTransform Plane => direct != null ? direct : box.Rect;

    public void Forget()
    {
        box.Forget();
        open = false;
        wheel = null;
        direct = null;
        selected = -1;
    }

    public void Restore()
    {
        box.Restore();
        open = false;
        selected = -1;
    }

    /// <param name="root">The object the game shows and hides; it is what moves to the hand.</param>
    /// <param name="current">The wheel component on or below it.</param>
    public void Update(GameObject root, UIWheel current, VRRig rig, VRAction opener)
    {
        var isOpen = root != null && current != null && root.activeInHierarchy;
        if (!isOpen)
        {
            if (open)
                selector.Reset();
            open = false;
            selected = -1;
            return;
        }
        if (!open || current != wheel)
        {
            if (!Open(root.transform, current, rig, RadialWheels.OpeningHandLeft(opener)))
                return;
            open = true;
        }

        var origin = rig.Origin;
        var center = origin.TransformPoint(centerLocal);
        var rotation = origin.rotation * rotationLocal;
        var metersPerPixel = UIWheelsFeature.Size.Value / span;
        if (direct != null)
            PoseDirect(center, rotation, metersPerPixel);
        else
            box.Pose(center, rotation, boxCenter, metersPerPixel);

        var stick = VRInput.Hand(left).Stick;
        var stickVector = new NVector2(stick.x, stick.y);
        if (handDriven)
        {
            var hand = rig.Hand(left);
            var travel = hand != null ? hand.position - center : Vector3.zero;
            var handOffset = new NVector2(
                Vector3.Dot(travel, rotation * Vector3.right),
                Vector3.Dot(travel, rotation * Vector3.up)
            );
            selector.FlickDistance = UIWheelsFeature.FlickDistance.Value;
            selector.Update(handOffset, stickVector, out var direction);
            Steer(direction, authoritative: true);
        }
        else if (stickVector.LengthSquared() >= RadialSelector.StickThresholdSq)
            Steer(stickVector, authoritative: true);
    }

    private bool Open(Transform root, UIWheel current, VRRig rig, bool handLeft)
    {
        var hand = rig.Hand(handLeft);
        var canvas = root.GetComponentInParent<Canvas>();
        if (hand == null || rig.Head == null || canvas == null)
            return false;
        wheel = current;
        left = handLeft;
        selected = -1;
        slices = CollectSlices(current);
        if (directions.Length != slices.Length)
        {
            directions = new NVector2[slices.Length];
            enabled = new bool[slices.Length];
        }

        // Normally the wheel lives inside the HUD canvas and moves into a container there. If it turns out to be
        // a root canvas of its own, that canvas is posed directly, after ui.world placed it.
        var rootCanvas = (RectTransform)canvas.rootCanvas.transform;
        direct = rootCanvas == root ? rootCanvas : null;
        var rect = direct != null ? direct : box.Ensure(rootCanvas);
        if (direct == null)
            box.Move(root);

        // Wheel geometry in container pixels: its centre and width come from the slices themselves, because the
        // wheel object may be a full-screen container.
        var bounds = new Bounds();
        bool first = true;
        for (int i = 0; i < slices.Length; i++)
        {
            if (slices[i] == null)
                continue;
            var b = RectTransformUtility.CalculateRelativeRectTransformBounds(
                rect,
                slices[i].transform
            );
            if (first)
                bounds = b;
            else
                bounds.Encapsulate(b);
            first = false;
        }
        boxCenter = first ? Vector2.zero : (Vector2)bounds.center;
        span = Mathf.Max(1f, first ? 500f : bounds.size.x);

        var center = hand.position;
        var facing = center - rig.Head.position;
        if (facing.sqrMagnitude < 1e-4f)
            facing = rig.Head.forward;
        var origin = rig.Origin;
        centerLocal = origin.InverseTransformPoint(center);
        rotationLocal =
            Quaternion.Inverse(origin.rotation) * Quaternion.LookRotation(facing, Vector3.up);
        selector.Reset();
        return true;
    }

    private void PoseDirect(Vector3 center, Quaternion rotation, float metersPerPixel)
    {
        var parent = direct.parent != null ? direct.parent.lossyScale.x : 1f;
        var s = Mathf.Abs(parent) > 1e-6f ? metersPerPixel / parent : metersPerPixel;
        direct.localScale = new Vector3(s, s, s);
        direct.SetPositionAndRotation(
            center - rotation * ((Vector3)boxCenter * metersPerPixel),
            rotation
        );
    }

    /// <summary>The slices the game's gamepad path considers (EmoteWheel.cs:120, BackpackWheel.cs:253).</summary>
    private static UIWheelSlice[] CollectSlices(UIWheel current)
    {
        switch (current)
        {
            case EmoteWheel e:
                return e.slices;
            case BackpackWheel b:
                var all = new UIWheelSlice[b.slices.Length + (b.jetpackSlice != null ? 1 : 0)];
                for (int i = 0; i < b.slices.Length; i++)
                    all[i] = b.slices[i];
                if (b.jetpackSlice != null)
                    all[^1] = b.jetpackSlice;
                return all;
            default:
                return System.Array.Empty<UIWheelSlice>();
        }
    }

    /// <summary>
    /// Hovers the slice nearest <paramref name="input"/> (x right, y up in the wheel's plane), like the game's
    /// gamepad path does. A zero or short input clears the choice only for the driver itself; another caller
    /// (the game's resting stick) leaves it alone, so it cannot undo a choice made by hand.
    /// </summary>
    public void Steer(NVector2 input, bool authoritative)
    {
        var plane = Plane;
        if (plane == null || slices.Length == 0)
            return;
        int next = -1;
        if (input.LengthSquared() >= RadialSelector.StickThresholdSq)
        {
            // Same candidates as the gamepad path: active slices; greyed ones are picked and then ignore the hover.
            var offset = Quaternion.Euler(0f, 0f, SliceOffsetDegrees);
            for (int i = 0; i < slices.Length; i++)
            {
                enabled[i] = slices[i] != null && slices[i].gameObject.activeSelf;
                if (!enabled[i])
                    continue;
                var up = offset * plane.InverseTransformDirection(slices[i].transform.up);
                directions[i] = new NVector2(up.x, up.y);
            }
            next = RadialSelector.Nearest(input, directions, enabled, slices.Length);
        }
        else if (!authoritative)
            return;

        if (next != selected)
        {
            if (selected >= 0 && selected < slices.Length)
                Dehover(slices[selected]);
            selected = next;
            var system = EventSystem.current;
            if (next >= 0)
            {
                if (system != null && slices[next].button != null)
                    system.SetSelectedGameObject(slices[next].button.gameObject);
                Hover(slices[next]);
                UIHaptics.Tick(left, UIWheelsFeature.Haptics.Value, HoverTickSeconds);
            }
            else if (system != null)
                system.SetSelectedGameObject(null);
        }
        else if (next >= 0 && handDriven)
        {
            // A page flip re-inits the emote slices and clears the choice (EmoteWheel.cs:60); hovering every
            // frame like the gamepad path picks the new page's emote up.
            Hover(slices[next]);
        }
    }

    private static void Hover(UIWheelSlice slice)
    {
        if (slice is EmoteWheelSlice e)
            e.Hover();
        else if (slice is BackpackWheelSlice b)
            b.Hover();
    }

    private static void Dehover(UIWheelSlice slice)
    {
        if (slice is EmoteWheelSlice e)
            e.Dehover();
        else if (slice is BackpackWheelSlice b)
            b.Dehover();
    }
}
