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
    private bool fuelVisible;

    public void Release()
    {
        rope.Restore();
        throwBar.Restore();
        SetFuel(false);
    }

    public void Forget()
    {
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
                var p = hand.position + Vector3.up * 0.1f;
                p += (eye - p).normalized * 0.03f;
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
            fuelRoot.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
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
