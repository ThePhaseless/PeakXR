using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.UI.Common;
using PeakXR.UI.World;
using PeakXR.VR;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeakXR.UI.Spatial;

/// <summary>
/// U6 and spectating: while you watch a friend (<c>MainCameraMovement.specCharacter</c>, A/MainCameraMovement.cs:49;
/// <c>Character.observedCharacter</c>, A/Character.cs:209), what the HUD says about them goes on them. Their name
/// (<c>spectatingNameText</c>, A/GUIManager.cs:136, set in <c>UpdateSpectate</c> :599) and the status bar, which
/// already shows the observed character (A/StaminaBar.cs:104-181, A/BarAffliction.cs:42), float above their head as
/// a nameplate; their hotbar (<c>Player.itemSlots</c>, temporary slot and <c>backpackSlot</c>, A/Player.cs:17-21, the slots
/// <c>GUIManager.UpdateItems</c> :1109 shows) becomes icons around their belt; the switch hint
/// (<c>spectatingInputs</c>, :138, shown while dead at :614) becomes up/down arrows on the stick that switches.
/// Your own dying bar stays on your wrist (ui.world). The name goes with its whole "spectating" block
/// (<c>spectatingObject</c>, :136) minus the monitor pill behind it. Watching your own body (solo, or the last one
/// alive) shows just that block, "spectating YOURSELF", over where you fell. A dead body is parked far out of the
/// world (<c>Character.DeathPos</c>, A/Character.cs:558), so a dead player's plate sits over
/// <c>Character.VirtualCenter</c> (:256) and gets no belt.
/// </summary>
internal sealed class SpectateView
{
    private const float PlateLift = 0.45f;

    /// <summary>Rough radius of a body lying where it fell: its label goes just above that, as you see it.</summary>
    private const float BodyRadius = 0.45f;

    /// <summary>Smallest label text, in HUD pixels, as for a target label: the game's "spectating" line is sized
    /// for a monitor and read as a speck a few metres out.</summary>
    private const float MinFontPixels = 30f;
    private const float BeltRadius = 0.38f;
    private const float BeltSpacing = 32f;
    private const float IconDegrees = 4f;
    private const float ArrowDegrees = 2.2f;
    private const int Slots = 5;

    /// <summary>Belt order, left to right as you see it: hotbar 0-2, the overflow slot, the backpack
    /// (<c>Player.GetItemSlot</c>, A/Player.cs:250: 3 is the backpack, 250 the temporary slot).</summary>
    private static readonly byte[] SlotIds = { 0, 1, 2, 250, 3 };

    /// <summary>The nameplate holds the status bar: ui.world's HUD split leaves it alone meanwhile.</summary>
    public static bool ClaimsStatus { get; set; }

    /// <summary>The spectated friend's items are drawn on them, so the 2D hotbar can hide (inventory.vr).</summary>
    public static bool BeltShowing { get; set; }

    private static Mesh quadMesh;
    private static Texture2D arrowTexture;

    private readonly SpatialBox plate = new("PeakXR_SpectatePlate");
    private readonly SpatialBox inputs = new("PeakXR_SpectateInputs");
    private readonly Transform[] icons = new Transform[Slots];
    private readonly Material[] iconMats = new Material[Slots];
    private readonly Item[] shownItems = new Item[Slots];
    private Transform next,
        previous;
    private GameObject root;
    private bool grabbedSelf;

    /// <summary>Box pixels from the plate's origin down to its lower edge (half the status bar, or 0).</summary>
    private float plateBelow;

    public void Release()
    {
        plate.Restore();
        inputs.Restore();
        if (root != null && root.activeSelf)
            root.SetActive(false);
    }

    public void Forget()
    {
        plate.Forget();
        inputs.Forget();
        for (int i = 0; i < Slots; i++)
            shownItems[i] = null;
    }

    public void Tick(GUIManager gui, RectTransform hudRoot, VRRig rig)
    {
        var spec = MainCameraMovement.specCharacter;
        // Watching your own body: the label only; your status bar stays on your wrist.
        bool self = spec != null && spec == Character.localCharacter;
        if (plate.Holding && self != grabbedSelf)
            Release();
        if (!plate.Holding)
            Grab(gui, hudRoot, self);
        EnsureQuads();
        if (!root.activeSelf)
            root.SetActive(true);

        // A dead body is parked far out of the world (Character.DeathPos, Character.cs:558): no belt there.
        var player = spec != null && !self && !spec.data.dead ? spec.player : null;
        for (int i = 0; i < Slots; i++)
        {
            var slot = player != null ? player.GetItemSlot(SlotIds[i]) : null;
            var item = slot != null && !slot.IsEmpty() ? slot.prefab : null;
            if (item != shownItems[i])
            {
                shownItems[i] = item;
                iconMats[i].mainTexture = item != null ? item.UIData.GetIcon() : null;
            }
            bool on = item != null && iconMats[i].mainTexture != null;
            if (icons[i].gameObject.activeSelf != on)
                icons[i].gameObject.SetActive(on);
        }

        // Your own body cannot be switched away from (MainCameraMovement.HandleSpecSelection, :189).
        bool arrows = !self && gui.spectatingInputs != null && gui.spectatingInputs.activeSelf;
        if (next.gameObject.activeSelf != arrows)
        {
            next.gameObject.SetActive(arrows);
            previous.gameObject.SetActive(arrows);
        }
        Pose(rig);
    }

    public void Pose(VRRig rig)
    {
        var spec = MainCameraMovement.specCharacter;
        if (spec == null || root == null)
            return;
        var eye = rig.Head.position;
        var dpp = SpatialRuntime.DegreesPerPixel;

        if (plate.Holding)
        {
            // Dead: over where they fell (VirtualCenter, Character.cs:256), not the parked body; just above the body
            // as you see it and in front of it, like a target label, so it never lands on a wall behind it. A fixed
            // 1.2 m lift put it on the wall 1.5 m above a body lying on the floor.
            var p = spec.data.dead
                ? SpatialMath
                    .TargetLabel(
                        spec.VirtualCenter.ToNumerics(),
                        eye.ToNumerics(),
                        BodyRadius,
                        plateBelow * dpp
                    )
                    .ToUnity()
                : spec.Head + Vector3.up * PlateLift;
            plate.Pose(
                p,
                SpatialRuntime.FaceEye(p, eye),
                SpatialMath.MetersPerPixel(Vector3.Distance(p, eye), dpp)
            );
        }

        var hip = spec.refs.hip != null ? spec.refs.hip.transform.position : spec.Center;
        var toEye = eye - hip;
        var yaw = Mathf.Atan2(toEye.x, toEye.z) * Mathf.Rad2Deg;
        var selected = spec.refs.items.currentSelectedSlot;
        for (int i = 0; i < Slots; i++)
        {
            var icon = icons[i];
            if (!icon.gameObject.activeSelf)
                continue;
            var p = hip + SpatialMath.ArcSlot(i, Slots, yaw, BeltRadius, BeltSpacing).ToUnity();
            var size = SpatialMath.SpanMeters(Vector3.Distance(p, eye), IconDegrees);
            bool held = selected.IsSome && selected.Value == SlotIds[i];
            if (held)
                size *= 1.3f;
            icon.SetPositionAndRotation(p, SpatialRuntime.FaceEye(p, eye));
            icon.localScale = new Vector3(size, size, size);
            iconMats[i].color = new Color(1f, 1f, 1f, held ? 1f : 0.75f);
        }

        if (next.gameObject.activeSelf)
        {
            PoseArrow(next, VRAction.SpectateNext, rig, eye, 1f);
            PoseArrow(previous, VRAction.SpectatePrevious, rig, eye, -1f);
        }
    }

    private static void PoseArrow(
        Transform arrow,
        VRAction action,
        VRRig rig,
        Vector3 eye,
        float sign
    )
    {
        if (
            !ControllerLabels.ForAction(
                action,
                VRInput.LeftHanded,
                LocalHold.ItemInLeftHand,
                out var left,
                out var control
            )
        )
            return;
        var hand = rig.Hand(left);
        if (hand == null)
            return;
        var p = hand.TransformPoint(ControllerLabels.Offset(control, left).ToUnity());
        // Points along the controller: forward for stick up, back for stick down.
        var to = p - eye;
        arrow.SetPositionAndRotation(
            p,
            to.sqrMagnitude > 1e-8f
                ? Quaternion.LookRotation(to, hand.forward * sign)
                : hand.rotation
        );
        var size = SpatialMath.SpanMeters(to.magnitude, ArrowDegrees);
        arrow.localScale = new Vector3(size, size, size);
    }

    private void Grab(GUIManager gui, RectTransform hudRoot, bool self)
    {
        grabbedSelf = self;
        plateBelow = 0f;
        if (gui.spectatingInputs != null)
        {
            // The game's hint shows keyboard or gamepad keys; the arrows on the stick replace it. Taken first, so
            // it is out of the label block below if it sits inside it.
            inputs.Move(hudRoot, gui.spectatingInputs.transform, Vector2.zero);
            inputs.SetVisible(false);
        }
        Transform barGroup = null;
        if (!self)
        {
            barGroup = gui.bar != null ? HudSplit.GroupOf(gui.bar.transform, hudRoot) : null;
            var numbGroup =
                gui.mushroomsCanvasGroup != null
                    ? HudSplit.GroupOf(gui.mushroomsCanvasGroup.transform, hudRoot)
                    : null;
            // A group still inside another box (the wrist, when it shares the dying bar's group) stays with its owner.
            if (barGroup != null && barGroup.parent != hudRoot)
                barGroup = null;
            if (numbGroup != null && numbGroup.parent != hudRoot)
                numbGroup = null;
            if (barGroup != null)
            {
                var barCentre = CentreIn(hudRoot, barGroup);
                plate.Move(hudRoot, barGroup, Vector2.zero);
                MoveAfflictions(gui.bar, hudRoot, barCentre);
            }
            if (numbGroup != null && numbGroup != barGroup)
                plate.Move(hudRoot, numbGroup, Vector2.zero);
        }
        // The whole "spectating NAME" block (spectatingObject, GUIManager.cs:132), without the dark pill it sat on
        // on a monitor: outlined text over the player, its lower edge just above the status bar.
        var label =
            gui.spectatingObject != null ? gui.spectatingObject.transform
            : gui.spectatingNameText != null ? gui.spectatingNameText.transform
            : null;
        if (label is RectTransform labelRect)
        {
            plate.Move(hudRoot, labelRect, Vector2.zero);
            plate.HideBackings(labelRect);
            SpatialBox.Enlarge(labelRect, MinFontPixels);
            var barTop = barGroup is RectTransform r ? r.rect.height * 0.5f : 0f;
            plateBelow = barTop;
            var content = plate.ContentBounds(labelRect);
            labelRect.anchoredPosition += new Vector2(
                -content.center.x,
                barTop + (barGroup != null ? 10f : 0f) - content.yMin
            );
        }
        plate.SetVisible(true);
    }

    /// <summary>The stamina bar finds its affliction segments anywhere under its parent (A/StaminaBar.cs:85), so
    /// moving the bar's group can leave them on the panel. Moves their groups onto the plate at the same offset from
    /// the bar as on the HUD (the bar's group sits at the plate origin). Groups on the wrist stay with it.</summary>
    private void MoveAfflictions(StaminaBar bar, RectTransform hudRoot, Vector2 barCentre)
    {
        if (bar.afflictions == null)
            return;
        var wrist = HudSplit.WristRoot;
        foreach (var a in bar.afflictions)
        {
            if (a == null || (plate.Rect != null && a.transform.IsChildOf(plate.Rect)))
                continue;
            var group = HudSplit.AfflictionGroup(a, bar, hudRoot);
            if (group == null || (wrist != null && group.IsChildOf(wrist)))
                continue;
            plate.Move(hudRoot, group, CentreIn(hudRoot, group) - barCentre);
        }
    }

    /// <summary>Centre of <paramref name="t"/>'s rect in <paramref name="hudRoot"/> pixels.</summary>
    private static Vector2 CentreIn(RectTransform hudRoot, Transform t) =>
        t is RectTransform r
            ? (Vector2)hudRoot.InverseTransformPoint(r.TransformPoint(r.rect.center))
            : Vector2.zero;

    private void EnsureQuads()
    {
        if (root != null)
            return;
        if (quadMesh == null)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quadMesh = quad.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(quad);
        }
        if (arrowTexture == null)
            arrowTexture = MakeArrow(32);
        root = new GameObject("PeakXR_SpectateMarks");
        Object.DontDestroyOnLoad(root);
        for (int i = 0; i < Slots; i++)
        {
            iconMats[i] = OnTopTextured(null);
            icons[i] = UIMaterials
                .MeshObject("Icon" + i, quadMesh, iconMats[i], root.transform)
                .transform;
            icons[i].gameObject.SetActive(false);
        }
        var arrowMat = OnTopTextured(arrowTexture);
        next = UIMaterials.MeshObject("Next", quadMesh, arrowMat, root.transform).transform;
        previous = UIMaterials.MeshObject("Previous", quadMesh, arrowMat, root.transform).transform;
        next.gameObject.SetActive(false);
        previous.gameObject.SetActive(false);
    }

    /// <summary>Textured, alpha-blended and drawn over the world, on a shader every player build ships.</summary>
    private static Material OnTopTextured(Texture texture)
    {
        var shader = Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default");
        var material = new Material(shader) { mainTexture = texture, renderQueue = 3100 };
        material.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
        return material;
    }

    /// <summary>White upward chevron on a clear background.</summary>
    private static Texture2D MakeArrow(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "PeakXR.SpectateArrow",
            wrapMode = TextureWrapMode.Clamp,
        };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // Distance below the chevron's two arms (an upside-down V), 4 px thick.
                float u = (x + 0.5f) / size - 0.5f;
                float v = (y + 0.5f) / size;
                float arm = 0.8f - Mathf.Abs(u) * 1.2f;
                float a = Mathf.Clamp01(1f - Mathf.Abs(v - arm) * size / 4f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255 * a));
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return tex;
    }
}
