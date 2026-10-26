using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.VR;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace PeakXR.UI.Spatial;

/// <summary>
/// U4: readouts that belong to the held item ride on it. The rope gauge (<c>UI_Rope</c>, A/UI_Rope.cs:5, shown by
/// <c>GUIManager.UpdateRope</c>, A/GUIManager.cs:635, while a <c>RopeSpool</c> is held) moves next to the spool, and
/// the item's fuel (<c>DataEntryKey.UseRemainingPercentage</c>, the value the hotbar fuel bar reads in
/// <c>InventoryItemUI.TrySetFuel</c>, A/InventoryItemUI.cs:159) shows as an arc by the holding hand. The arc reuses
/// the progress ring's sprite (A/UI_UseItemProgress.cs:7) and is redrawn only when the value changes. The throw
/// charge bar (<c>throwGO</c>, A/GUIManager.cs:112, button fallback only) sits above the throwing hand.
/// </summary>
internal sealed class HeldGauges
{
    private const float FuelDegrees = 2.6f;
    private const float FuelPixels = 100f;

    private readonly SpatialBox rope = new("PeakXR_RopeGauge");
    private readonly SpatialBox throwBar = new("PeakXR_ThrowBar");
    private GameObject fuelRoot;
    private Image fuelImage;
    private GUIManager spriteFrom;
    private float shownFuel = -1f;
    private bool fuelVisible,
        ropeShown;
    private int ropeCentre;

    /// <summary>Dark plate behind the throw charge bar, so the translucent bar reads over the hand, sleeve or item
    /// behind it instead of looking sunk into them.</summary>
    private Image throwPlate;
    private const float ThrowPlatePad = 10f;

    public void Release()
    {
        ropeShown = false;
        rope.Restore();
        throwBar.Restore();
        if (throwPlate != null)
            throwPlate.gameObject.SetActive(false);
        SetFuel(false);
    }

    public void Forget()
    {
        ropeShown = false;
        rope.Forget();
        throwBar.Forget();
        spriteFrom = null;
    }

    public void Tick(GUIManager gui, RectTransform hudRoot, VRRig rig)
    {
        if (!rope.Holding && gui.ui_rope != null)
        {
            rope.Move(hudRoot, gui.ui_rope.transform, Vector2.zero);
            rope.SetVisible(true);
        }
        if (!throwBar.Holding && gui.throwGO != null)
        {
            throwBar.Move(hudRoot, gui.throwGO.transform, Vector2.zero);
            throwBar.SetVisible(true);
        }
        bool throwShown =
            throwBar.Holding && throwBar.Item(0) != null && throwBar.Item(0).gameObject.activeSelf;
        if (throwShown && (throwPlate == null || throwPlate.transform.parent != throwBar.Rect))
            throwPlate = ThrowPlate(throwBar.Rect, throwBar.Item(0).rect.size);
        if (throwPlate != null && throwPlate.gameObject.activeSelf != throwShown)
            throwPlate.gameObject.SetActive(throwShown);
        // UI_Rope draws its coil and length text far from its rect's centre (they sat 0.6 m off, on the ground, while
        // the rect was beside the spool): centre the drawn text on the box origin each time it shows, for a few
        // frames while the game writes the length. Stack is idempotent once centred.
        bool ropeOn = rope.Holding && rope.Item(0) != null && rope.Item(0).gameObject.activeSelf;
        if (ropeOn && !ropeShown)
            ropeCentre = 3;
        ropeShown = ropeOn;
        if (ropeOn && ropeCentre > 0)
        {
            ropeCentre--;
            rope.Stack(0f);
        }
        if (spriteFrom != gui)
        {
            spriteFrom = gui;
            EnsureFuel(gui);
        }

        var c = Character.localCharacter;
        var item = c != null ? c.data.currentItem : null;
        float fuel = -1f;
        if (
            item != null
            && item.data != null
            && item.UIData != null
            && !item.UIData.hideFuel
            && item.data.TryGetDataEntry<FloatItemData>(
                DataEntryKey.UseRemainingPercentage,
                out var value
            )
        )
            fuel = Mathf.Clamp01(value.Value);
        SetFuel(fuel >= 0f && fuelImage != null);
        if (fuelVisible && Mathf.Abs(fuel - shownFuel) > 0.002f)
        {
            shownFuel = fuel;
            fuelImage.fillAmount = fuel;
        }
        Pose(rig);
    }

    /// <summary>The throw bar's plate: first in its box, the bar's size plus a margin, drawn on top like the bar.</summary>
    private static Image ThrowPlate(RectTransform box, Vector2 size)
    {
        var go = new GameObject("PeakXR_ThrowPlate", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(box, false);
        rt.SetAsFirstSibling();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size + 2f * ThrowPlatePad * Vector2.one;
        var image = go.AddComponent<Image>();
        image.sprite = World.BackplateView.PlateSprite();
        image.type = Image.Type.Sliced;
        image.raycastTarget = false;
        var c = MenuBackplate.Color;
        image.color = new Color(c.X, c.Y, c.Z, ButtonLabelStyle.PlateOpacity);
        Common.OnTop.Set(go.transform, true);
        return image;
    }

    public void Pose(VRRig rig)
    {
        var c = Character.localCharacter;
        var item = c != null ? c.data.currentItem : null;
        if (item == null)
            return;
        var eye = rig.Head.position;
        if (throwBar.Holding && throwBar.Item(0) != null && throwBar.Item(0).gameObject.activeSelf)
        {
            // The button-held throw charge (A/GUIManager.cs:656): just above the throwing hand.
            var hand = rig.Hand(LocalHold.ItemInLeftHand);
            if (hand != null)
            {
                // Clear of the game's chunky hand and the held item, toward the eye.
                var p = hand.position + Vector3.up * 0.14f;
                p += (eye - p).normalized * 0.06f;
                throwBar.Pose(
                    p,
                    SpatialRuntime.FaceEye(p, eye),
                    SpatialMath.MetersPerPixel(
                        Vector3.Distance(p, eye),
                        SpatialRuntime.DegreesPerPixel * 0.8f
                    )
                );
            }
        }
        if (rope.Holding && rope.Item(0) != null && rope.Item(0).gameObject.activeSelf)
        {
            // Above the spool and a little toward you, clear of the rope that pays out of it.
            var p = item.Center() + Vector3.up * 0.09f;
            p += (eye - p).normalized * 0.05f;
            rope.Pose(
                p,
                SpatialRuntime.FaceEye(p, eye),
                SpatialMath.MetersPerPixel(
                    Vector3.Distance(p, eye),
                    SpatialRuntime.DegreesPerPixel * 0.8f
                )
            );
        }
        if (fuelVisible)
        {
            bool left = LocalHold.ItemInLeftHand;
            var hand = rig.Hand(left);
            if (hand == null)
                return;
            // Outside the holding hand, below the ui.hand progress ring.
            var p = hand.TransformPoint(new Vector3(left ? -0.06f : 0.06f, 0.02f, -0.02f));
            var size = SpatialMath.SpanMeters(Vector3.Distance(p, eye), FuelDegrees);
            fuelRoot.transform.SetPositionAndRotation(p, SpatialRuntime.FaceEye(p, eye));
            var s = size / FuelPixels;
            fuelRoot.transform.localScale = new Vector3(s, s, s);
        }
    }

    private void SetFuel(bool visible)
    {
        if (fuelVisible == visible)
            return;
        fuelVisible = visible;
        shownFuel = -1f;
        if (fuelRoot != null)
            fuelRoot.SetActive(visible);
    }

    /// <summary>Builds the fuel arc once (a small world canvas of our own), taking the ring sprite and the fuel
    /// colour from the current HUD.</summary>
    private void EnsureFuel(GUIManager gui)
    {
        var ring = gui.hudCanvas.GetComponentInChildren<UI_UseItemProgress>(true);
        var sprite = ring != null && ring.fill != null ? ring.fill.sprite : null;
        if (sprite == null)
            return;
        if (fuelRoot == null)
        {
            fuelRoot = new GameObject("PeakXR_FuelArc", typeof(RectTransform), typeof(Canvas));
            Object.DontDestroyOnLoad(fuelRoot);
            var fuelCanvas = fuelRoot.GetComponent<Canvas>();
            fuelCanvas.renderMode = RenderMode.WorldSpace;
            // Hand UI (OnTop's rule): no depth test below, sorted after smoke and under the comfort overlays.
            fuelCanvas.sortingOrder = DrawOrder.HandUi;
            ((RectTransform)fuelRoot.transform).sizeDelta = new Vector2(FuelPixels, FuelPixels);
            var go = new GameObject("Arc", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(fuelRoot.transform, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            fuelImage = go.GetComponent<Image>();
            fuelImage.type = Image.Type.Filled;
            fuelImage.fillMethod = Image.FillMethod.Radial360;
            fuelImage.fillOrigin = (int)Image.Origin360.Top;
            fuelImage.fillClockwise = false;
            fuelImage.raycastTarget = false;
            var material = new Material(fuelImage.material) { name = "PeakXR_FuelArc" };
            material.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
            fuelImage.material = material;
            fuelRoot.SetActive(fuelVisible);
        }
        fuelImage.sprite = sprite;
        var bar =
            gui.items != null && gui.items.Length > 0 && gui.items[0] != null
                ? gui.items[0].fuelBarFill
                : null;
        fuelImage.color = bar != null ? bar.color : new Color(1f, 0.75f, 0.3f, 1f);
        shownFuel = -1f;
    }
}
