using System.Collections.Generic;
using PeakXR.Core;
using PeakXR.UI.Affordances;
using PeakXR.UI.Common;
using PeakXR.UI.World;
using PeakXR.VR;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using NVector2 = System.Numerics.Vector2;

namespace PeakXR.UI.Wheels;

/// <summary>
/// Drives the game's two wheels in VR. When the game opens a wheel (GUIManager.UpdateEmoteWheel A/GUIManager.cs:561
/// on the Emote binding, OpenBackpackWheel :546 on interact), it is lifted out of the HUD panel into a
/// <see cref="HudBox"/> and pinned, facing you, where the opening hand was: a few centimetres toward the eye on
/// the eye-to-hand line (<see cref="WheelPlane"/>) and drawn without the depth test (<see cref="OnTop"/>), so
/// the hand, forearm and a backpack under the hand never cover it; it is shrunk to look as big as a wheel at the
/// hand. Each frame the hand's travel from that spot (or the hand's stick) picks a slice through <see cref="RadialSelector"/>, and the slice's own
/// Hover/Dehover run, so releasing the button resolves through the game's Choose exactly as with a gamepad.
/// The emote wheel is picked by the hand over its drawn ring, the opening hand's stick, or the laser (shown while it
/// is open; the slices' own pointer enter/exit hover them). Its full-screen dimmer is hidden. The backpack wheel is picked by
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
    private bool emoteHeld;

    /// <summary>The controller whose menu button was down last, for a refused pause (WheelPatches.PauseRefused).</summary>
    public static bool MenuLeft { get; private set; }

    public static void Create()
    {
        var go = new GameObject("PeakXR_RadialWheels");
        DontDestroyOnLoad(go);
        go.AddComponent<RadialWheels>();
    }

    private void Awake()
    {
        instance = this;
        VRSession.Stopped += OnVRStopped;
    }

    private void OnDestroy()
    {
        VRSession.Stopped -= OnVRStopped;
        if (instance == this)
            instance = null;
    }

    private void OnVRStopped()
    {
        LaserPointer.ForceShow = false;
        emote.Restore();
        backpack.Restore();
        OnTop.DestroyClones();
    }

    /// <summary>Self-test: the drawn centre of <paramref name="wheel"/> while it is open on a VR hand.</summary>
    internal static bool TryDrawnCentre(UIWheel wheel, out Vector3 centre)
    {
        centre = default;
        if (instance == null)
            return false;
        var driver =
            instance.emote.Owns(wheel) ? instance.emote
            : instance.backpack.Owns(wheel) ? instance.backpack
            : null;
        if (driver == null)
            return false;
        centre = driver.Centre;
        return true;
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
            LaserPointer.ForceShow = false;
            emote.Restore();
            backpack.Restore();
            return;
        }

        if (VRInput.Left.Menu != VRInput.Right.Menu)
            MenuLeft = VRInput.Left.Menu;
        DenyRefusedOpen();
        emote.Update(gui.emoteWheel, emoteWheel, rig, VRAction.Emote);
        backpack.Update(
            gui.backpackWheel != null ? gui.backpackWheel.gameObject : null,
            gui.backpackWheel,
            rig,
            VRAction.Interact
        );
        // The laser over the open emote wheel, so the pointer that hovers its slices is seen (UIWorldRuntime reads it).
        // Hand travel has priority: while the hand is over the ring the laser is hidden, so the two never fight
        // over the hovered slice.
        LaserPointer.ForceShow = emote.IsOpen && !emote.HandPicking;
    }

    /// <summary>
    /// The Emote binding went down but the game keeps the wheel shut (GUIManager.UpdateEmoteWheel, A/GUIManager.cs:563-569):
    /// the opening hand gets the refusal cue instead of nothing.
    /// </summary>
    private void DenyRefusedOpen()
    {
        var c = Character.localCharacter;
        bool held = c != null && c.input.emoteIsPressed;
        if (held && !emoteHeld)
        {
            var reason = EmoteGestures.WheelRefusal(
                c.data.dead,
                c.refs.stats.won,
                c.data.isClimbing,
                gui.backpackWheel != null && gui.backpackWheel.gameObject.activeSelf,
                gui.windowBlockingInput
            );
            if (reason != DenyReason.None)
                Affordance.DenyAtHand(OpeningHandLeft(VRAction.Emote), reason);
        }
        emoteHeld = held;
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
        foreach (var b in BindingTable.Active)
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

    // A graphic this dark behind the wheel is a dimmer, not artwork (as MenuBackdrop's dimmer test).
    private const float BackdropMaxChannel = 0.35f;

    private readonly HudBox box;
    private readonly bool handDriven;
    private readonly RadialSelector selector = new();
    private readonly List<Graphic> hidden = new();
    private UIWheel wheel;
    private Transform drawn;
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
    private float drawScale = 1f;

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

    public bool IsOpen => open;

    /// <summary>Where the wheel's centre is drawn this frame.</summary>
    public Vector3 Centre { get; private set; }

    /// <summary>The hand is over the ring and its travel picks the slice; the laser stands down meanwhile.</summary>
    public bool HandPicking => open && handDriven && selector.Active;

    public void Forget()
    {
        hidden.Clear();
        box.Forget();
        open = false;
        wheel = null;
        drawn = null;
        direct = null;
        selected = -1;
    }

    public void Restore()
    {
        ShowBackdrops();
        OnTop.Set(drawn, false);
        drawn = null;
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
            {
                CueClose();
                selector.Reset();
                OnTop.Set(drawn, false);
            }
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
        Centre = center;
        // WheelSize is the diameter as seen at the hand; the wheel drawn nearer the eye is shrunk to look the same.
        var metersPerPixel = UIWheelsFeature.Size.Value * drawScale / span;
        if (direct != null)
            PoseDirect(center, rotation, metersPerPixel);
        else
            box.Pose(center, rotation, boxCenter, metersPerPixel);

        var stick = VRInput.Hand(left).Stick;
        var stickVector = new NVector2(stick.x, stick.y);
        if (handDriven)
        {
            var hand = rig.Hand(left);
            // Travel in the wheel's plane; the eye-ward shift is along its normal, so it drops out here.
            var travel = hand != null ? hand.position - center : Vector3.zero;
            var handOffset = new NVector2(
                Vector3.Dot(travel, rotation * Vector3.right),
                Vector3.Dot(travel, rotation * Vector3.up)
            );
            // The hand picks only over the drawn ring: not in its centre hole, not past its outer edge. The wheel's
            // drawn diameter is WheelSize (see Open), so the ring's radii follow from the wedge's measured shape.
            float outer = UIWheelsFeature.Size.Value * 0.5f;
            selector.FlickDistance = WheelSegment.PickStart(outer, UIWheelsFeature.PickDepth.Value);
            selector.MaxDistance = outer;
            selector.Update(handOffset, stickVector, out var direction);
            Steer(direction, authoritative: true);
        }
        else if (stickVector.LengthSquared() >= RadialSelector.StickThresholdSq)
            Steer(stickVector, authoritative: true);
    }

    /// <summary>
    /// A wheel closed. Emote (EmoteWheel.OnDisable runs Choose, EmoteWheel.cs:48-88). Nothing chosen: a soft cancel
    /// tick. A greyed slice under the hand (an on-the-ground emote while airborne, EmoteWheelSlice.cs:31-34, which
    /// ignores the hover :39) is refused, not cancelled.
    /// </summary>
    private void CueClose()
    {
        // The backpack wheel commits its hovered pocket on interact up (BackpackWheel.Update, A/BackpackWheel.cs:96-99;
        // Choose :122-126 does nothing without one). No pocket hovered: the grip let go on nothing.
        if (wheel is BackpackWheel b)
        {
            if (b.chosenSlice.IsNone)
                Affordance.Cancel(left);
            return;
        }
        if (wheel is not EmoteWheel e || e.selectedEmoteName == null)
            return;
        if (!string.IsNullOrEmpty(e.selectedEmoteName.text))
            return;
        if (
            selected >= 0
            && selected < slices.Length
            && slices[selected] is EmoteWheelSlice s
            && s.image != null
            && s.image.enabled
            && s.button != null
            && !s.button.interactable
        )
            Affordance.DenyAtHand(left, DenyReason.NeedsGround, default, s.image);
        else
            Affordance.Cancel(left);
    }

    private bool Open(Transform root, UIWheel current, VRRig rig, bool handLeft)
    {
        var hand = rig.Hand(handLeft);
        var canvas = root.GetComponentInParent<Canvas>();
        if (hand == null || rig.Head == null || canvas == null)
            return false;
        wheel = current;
        if (drawn != root)
            OnTop.Set(drawn, false);
        drawn = root;
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
        {
            box.Move(root);
            OnTop.Sort(rect);
            // The box is a nested canvas: its graphics are raycast only by a raycaster of its own. With it the laser
            // reaches the emote wheel, and WheelRaycaster keeps slice hits to their drawn wedges. The backpack
            // wheel gets none: no laser shows over it, and a ray from the hidden laser's stale pose would hover its
            // pockets behind inventory.vr's back.
            if (handDriven && !rect.TryGetComponent<WheelRaycaster>(out _))
                rect.gameObject.AddComponent<WheelRaycaster>();
        }

        // Wheel geometry in container pixels, from the slice rects alone: each is centred on the wheel and draws
        // its wedge out to WheelSegment.OuterFraction of its width. Their children (a 3000 px invisible raycast
        // mask) and the wheel object (full screen) would make the wheel a few centimetres wide.
        var sum = Vector3.zero;
        int count = 0;
        float width = 0f;
        float boxScale = Mathf.Max(1e-9f, Mathf.Abs(rect.lossyScale.x));
        for (int i = 0; i < slices.Length; i++)
        {
            if (slices[i] == null || slices[i].transform is not RectTransform slice)
                continue;
            sum += rect.InverseTransformPoint(slice.position);
            width = Mathf.Max(width, slice.rect.width * Mathf.Abs(slice.lossyScale.x) / boxScale);
            count++;
        }
        boxCenter = count > 0 ? (Vector2)(sum / count) : Vector2.zero;
        span = width > 0f ? 2f * WheelSegment.OuterFraction * width : 500f;
        HideBackdrops(root, boxScale);
        OnTop.Set(root, true);

        var eye = rig.Head.position.ToNumerics();
        var handAt = hand.position.ToNumerics();
        var nearer = WheelPlane.Centre(eye, handAt, WheelPlane.EyeOffset);
        drawScale = WheelPlane.DrawScale(eye, handAt, nearer);
        var center = nearer.ToUnity();
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

    /// <summary>
    /// The wheel's full-screen dimmer ("BG", a black 69% Image stretched over the canvas, first child of both
    /// wheel prefabs) would float at the hand as a dark slab. EmoteWheel and BackpackWheel keep no reference to it
    /// (fields A/EmoteWheel.cs:8-23, A/BackpackWheel.cs:9-25), so it is found by shape: an enabled dark Image or
    /// RawImage outside every slice and button, larger than the wheel. Disabled, so it neither draws nor catches
    /// the pointer; <see cref="Restore"/> turns it back on. Nothing found: nothing hidden.
    /// </summary>
    private void HideBackdrops(Transform root, float boxScale)
    {
        foreach (var g in root.GetComponentsInChildren<Graphic>(true))
        {
            if (
                !g.enabled
                || g is not (Image or RawImage)
                || g.GetComponentInParent<UIWheelSlice>(true) != null
                || g.GetComponentInParent<Selectable>(true) != null
                || g.TryGetComponent<Mask>(out _)
            )
                continue;
            var c = g.color;
            var size =
                g.rectTransform.rect.size * (Mathf.Abs(g.rectTransform.lossyScale.x) / boxScale);
            if (
                c.a <= 0f
                || Mathf.Max(c.r, Mathf.Max(c.g, c.b)) > BackdropMaxChannel
                || Mathf.Min(size.x, size.y) < span * 1.5f
            )
                continue;
            g.enabled = false;
            hidden.Add(g);
            Plugin.Log.LogInfo($"[ui.wheels] hid wheel backdrop '{g.name}' under '{root.name}'");
        }
    }

    private void ShowBackdrops()
    {
        foreach (var g in hidden)
            if (g != null)
                g.enabled = true;
        hidden.Clear();
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

    /// <summary>The slices the game's gamepad path considers (EmoteWheel.cs:106, BackpackWheel.cs:253).</summary>
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
            // A page flip re-inits the emote slices and clears the choice (EmoteWheel.cs:56); hovering every
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
