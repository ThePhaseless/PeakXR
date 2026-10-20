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
/// </summary>
[DefaultExecutionOrder(31050)]
internal sealed class RadialWheels : MonoBehaviour
{
    private readonly WheelDriver emote = new("PeakXR_EmoteWheel", rehoverEachFrame: true);
    private readonly WheelDriver backpack = new("PeakXR_BackpackWheel", rehoverEachFrame: false);
    private GUIManager gui;
    private EmoteWheel emoteWheel;

    public static void Create()
    {
        var go = new GameObject("PeakXR_RadialWheels");
        DontDestroyOnLoad(go);
        go.AddComponent<RadialWheels>();
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
    private readonly bool rehoverEachFrame;
    private readonly RadialSelector selector = new();
    private UIWheel wheel;
    private RectTransform direct;
    private UIWheelSlice[] slices = System.Array.Empty<UIWheelSlice>();
    private NVector2[] directions = System.Array.Empty<NVector2>();
    private bool[] enabled = System.Array.Empty<bool>();
    private bool open,
        left;
    private Vector3 centerLocal;
    private Quaternion rotationLocal;
    private Vector2 boxCenter;
    private float span = 1f;

    public WheelDriver(string name, bool rehoverEachFrame)
    {
        box = new HudBox(name);
        this.rehoverEachFrame = rehoverEachFrame;
    }

    public void Forget()
    {
        box.Forget();
        open = false;
        wheel = null;
        direct = null;
    }

    public void Restore()
    {
        box.Restore();
        open = false;
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
        Select(rig, center, rotation);
    }

    private bool Open(Transform root, UIWheel current, VRRig rig, bool handLeft)
    {
        var hand = rig.Hand(handLeft);
        var canvas = root.GetComponentInParent<Canvas>();
        if (hand == null || rig.Head == null || canvas == null)
            return false;
        wheel = current;
        left = handLeft;
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
        var offset = Quaternion.Euler(0f, 0f, SliceOffsetDegrees);
        var bounds = new Bounds();
        bool first = true;
        for (int i = 0; i < slices.Length; i++)
        {
            if (slices[i] == null)
                continue;
            var slice = slices[i].transform;
            var b = RectTransformUtility.CalculateRelativeRectTransformBounds(rect, slice);
            if (first)
                bounds = b;
            else
                bounds.Encapsulate(b);
            first = false;
            var up = offset * rect.InverseTransformDirection(slice.up);
            directions[i] = new NVector2(up.x, up.y);
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

    /// <summary>The slices the game's gamepad path considers (EmoteWheel.cs:139, BackpackWheel.cs:269).</summary>
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

    private void Select(VRRig rig, Vector3 center, Quaternion rotation)
    {
        // Same candidates as the gamepad path: active slices; greyed ones are picked and then ignore the hover.
        for (int i = 0; i < slices.Length; i++)
            enabled[i] = slices[i] != null && slices[i].gameObject.activeSelf;

        var hand = rig.Hand(left);
        var travel = hand != null ? hand.position - center : Vector3.zero;
        var handOffset = new NVector2(
            Vector3.Dot(travel, rotation * Vector3.right),
            Vector3.Dot(travel, rotation * Vector3.up)
        );
        var stick = VRInput.Hand(left).Stick;

        int previous = selector.Selected;
        selector.FlickDistance = UIWheelsFeature.FlickDistance.Value;
        int next = selector.Update(
            handOffset,
            new NVector2(stick.x, stick.y),
            directions,
            enabled,
            slices.Length
        );

        if (selector.Changed)
        {
            if (previous >= 0 && previous < slices.Length)
                Dehover(slices[previous]);
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
        else if (next >= 0 && rehoverEachFrame)
        {
            // A page flip re-inits the emote slices and clears the choice (EmoteWheel.cs:62); hovering every
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
