using PeakXR.Core;
using PeakXR.UI.Common;
using PeakXR.UI.World;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.UI.Spatial;

/// <summary>
/// U3: big announcements as world-locked signs ahead of you. <c>SetHeroTitle</c> (A/GUIManager.cs:484) shows
/// <c>heroObject</c> (:144) with the title render texture in <c>heroImage</c> (:152);
/// <c>TheFogRises/TheLavaRises/TheGloomRises/TheGhostsRise</c> (:1195-1232) show <c>fogRises</c>... (:58-64) for 4 s;
/// the ascent title (<c>AscentUI</c>, A/AscentUI.cs:5) and the endgame countdown, win and lose cards
/// (<c>GUIManager.endgame</c>, :69; <c>EndgameCounter</c>, A/EndgameCounter.cs:7-11) get signs too. A sign is placed
/// when the game shows it, along your head yaw and above the horizon (out of the lower view while climbing), and is
/// placed again, fading in, once it has been out of view for a moment (<see cref="SignDwell"/>) or you moved far
/// from it, so a glance never makes it pop. A cliff in the way brings it in to just before the
/// rock, at the same angular size (<see cref="PanelMath.FitBeforeWall"/>, the rule every world panel shares), so
/// the sign, drawn on top, never shows through nearer rock at a depth the two eyes disagree with. The game's own
/// typing and fades still play.
/// </summary>
internal sealed class WorldBanners
{
    internal const float Distance = 30f;

    /// <summary>Re-place a shown sign once you are this far from where it was placed, in metres.</summary>
    private const float ReplaceMeters = 20f;

    /// <summary>Seconds a re-placed sign takes to fade in where you now look.</summary>
    private const float FadeInSeconds = 0.3f;

    /// <summary>The eye the hero sign was last placed from (self-test ui.03 repeats the placement from there).</summary>
    internal static Vector3 HeroPlacedFrom;

    private readonly Sign hero = new("PeakXR_HeroSign", elevationDegrees: 8f);
    private readonly Sign banner = new("PeakXR_BannerSign", elevationDegrees: 3f);
    private readonly Sign ascent = new("PeakXR_AscentSign", elevationDegrees: 13f);
    private readonly Sign endgame = new("PeakXR_EndgameSign", elevationDegrees: 5f);
    private readonly GameObject[] banners = new GameObject[4];
    private AscentUI ascentUI;

    public void Release()
    {
        hero.Box.Restore();
        banner.Box.Restore();
        ascent.Box.Restore();
        endgame.Box.Restore();
    }

    public void Forget()
    {
        hero.Box.Forget();
        banner.Box.Forget();
        ascent.Box.Forget();
        endgame.Box.Forget();
        ascentUI = null;
    }

    public void Tick(GUIManager gui, RectTransform hudRoot, VRRig rig)
    {
        if (!hero.Box.Holding && gui.heroObject != null)
        {
            hero.Box.Move(hudRoot, gui.heroObject.transform, Vector2.zero);
            if (
                gui.heroImage != null
                && gui.heroImage.transform.IsChildOf(gui.heroObject.transform)
            )
                hero.Box.CenterOn(gui.heroImage.rectTransform);
            hero.Start();
        }
        if (!banner.Box.Holding)
        {
            banners[0] = gui.fogRises;
            banners[1] = gui.lavaRises;
            banners[2] = gui.gloomRises;
            banners[3] = gui.ghostsRise;
            foreach (var b in banners)
            {
                if (b != null)
                    banner.Box.Move(hudRoot, b.transform, Vector2.zero);
            }
            banner.Start();
        }
        if (!ascent.Box.Holding)
        {
            ascentUI = gui.hudCanvas.GetComponentInChildren<AscentUI>(true);
            if (ascentUI != null)
            {
                ascent.Box.Move(hudRoot, ascentUI.transform, Vector2.zero);
                ascent.Start();
            }
        }
        if (!endgame.Box.Holding && gui.endgame != null)
        {
            endgame.Box.Move(hudRoot, gui.endgame.transform, Vector2.zero);
            endgame.Start();
        }

        var head = rig.Head;
        hero.Tick(gui.heroObject != null && gui.heroObject.activeSelf, head);
        HeroPlacedFrom = hero.PlacedFrom;
        bool any = false;
        foreach (var b in banners)
            any |= b != null && b.activeSelf;
        banner.Tick(any, head);
        ascent.Tick(ascentUI != null && ascentUI.gameObject.activeSelf, head);
        var e = gui.endgame;
        endgame.Tick(
            e != null
                && (
                    Shown(e.counterGroup != null ? e.counterGroup.gameObject : null)
                    || Shown(e.winGroup != null ? e.winGroup.gameObject : null)
                    || Shown(e.loseGroup != null ? e.loseGroup.gameObject : null)
                ),
            head
        );
    }

    private static bool Shown(GameObject go) => go != null && go.activeInHierarchy;

    private sealed class Sign
    {
        public readonly SpatialBox Box;
        private readonly float elevationDegrees;
        private readonly SignDwell dwell = new();
        private float alpha = 1f;
        private bool wasShown;
        private Vector3 position,
            placedFrom;
        private Quaternion rotation = Quaternion.identity;
        private float metersPerPixel;

        public Sign(string name, float elevationDegrees)
        {
            Box = new SpatialBox(name);
            this.elevationDegrees = elevationDegrees;
        }

        public void Start()
        {
            Box.SetVisible(true);
            wasShown = false;
        }

        /// <summary>Places the sign when the game shows it, then holds it there in the world.</summary>
        public void Tick(bool shown, Transform head)
        {
            if (!Box.Holding)
                return;
            var yaw = PanelMath.YawOf(head.forward.ToNumerics(), head.up.ToNumerics());
            float dt = Time.unscaledDeltaTime;
            if (shown && !wasShown)
            {
                // The game's own typing and fades play on a first show.
                Place(head.position, yaw, head.forward);
                alpha = 1f;
            }
            else if (
                shown && dwell.Update(OffDegrees(head.position, yaw), MovedFar(head.position), dt)
            )
            {
                Place(head.position, yaw, head.forward);
                alpha = 0f;
            }
            wasShown = shown;
            alpha = Mathf.MoveTowards(alpha, 1f, dt / FadeInSeconds);
            Box.SetAlpha(alpha);
            // The HUD canvas it lives under follows the body, so the pose is set again every frame.
            Box.Pose(position, rotation, metersPerPixel);
        }

        private float OffDegrees(Vector3 eye, float yaw)
        {
            var to = position - eye;
            return Mathf.Abs(Mathf.DeltaAngle(yaw, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg));
        }

        private bool MovedFar(Vector3 eye) =>
            (eye - placedFrom).sqrMagnitude > ReplaceMeters * ReplaceMeters;

        /// <summary>The eye the sign was placed from (ui.03 repeats the placement test from there).</summary>
        public Vector3 PlacedFrom => placedFrom;

        private void Place(Vector3 eye, float yaw, Vector3 forward)
        {
            var far = SpatialMath
                .Banner(eye.ToNumerics(), yaw, Distance, elevationDegrees)
                .ToUnity();
            var mpp = SpatialMath.MetersPerPixel(Distance, SpatialRuntime.DegreesPerPixel);
            var offset = far - eye;
            var d = ClearDistance(
                eye,
                offset,
                SpatialRuntime.FaceEyeYaw(far, eye, forward),
                0.5f * mpp * PixelSize()
            );
            position = eye + offset * (d / offset.magnitude);
            placedFrom = eye;
            dwell.Reset();
            rotation = SpatialRuntime.FaceEyeYaw(position, eye, forward);
            metersPerPixel = SpatialMath.MetersPerPixel(d, SpatialRuntime.DegreesPerPixel);
        }

        /// <summary>The sign's size in box pixels: its largest moved part. Placement only.</summary>
        private Vector2 PixelSize()
        {
            var size = Vector2.zero;
            for (int i = 0; i < Box.Count; i++)
            {
                var item = Box.Item(i);
                if (item != null)
                    size = Vector2.Max(size, item.rect.size * item.localScale.x);
            }
            return size;
        }
    }

    /// <summary>Distance for a sign wanted at <see cref="Distance"/> along <paramref name="offset"/>, half extents
    /// <paramref name="half"/> metres there: just before the nearest rock in front of any part of it. Placement
    /// only.</summary>
    internal static float ClearDistance(
        Vector3 eye,
        Vector3 offset,
        Quaternion rotation,
        Vector2 half
    ) =>
        PanelMath
            .FitBeforeWall(offset.magnitude, WorldCanvases.WallDepth(eye, offset, rotation, half))
            .Distance;
}
