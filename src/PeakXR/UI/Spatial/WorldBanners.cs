using PeakXR.Core;
using PeakXR.UI.Common;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.UI.Spatial;

/// <summary>
/// U3: the biome title and the "fog rises" banners as world-locked signs far ahead. <c>SetHeroTitle</c>
/// (A/GUIManager.cs:484) shows <c>heroObject</c> (:144) with the title render texture in <c>heroImage</c> (:152);
/// <c>TheFogRises/TheLavaRises/TheGloomRises/TheGhostsRise</c> (:1195-1232) show <c>fogRises</c>... (:58-64) for 4 s.
/// Each sign is placed once, when the game shows it, along your head yaw at that moment and slightly above the
/// horizon, so it stays out of the lower view while climbing; the game's own typing and fades still play.
/// </summary>
internal sealed class WorldBanners
{
    private const float Distance = 30f;
    private const float HeroElevation = 8f;
    private const float BannerElevation = 3f;

    private readonly Sign hero = new("PeakXR_HeroSign", HeroElevation);
    private readonly Sign banner = new("PeakXR_BannerSign", BannerElevation);
    private readonly GameObject[] banners = new GameObject[4];

    public void Release()
    {
        hero.Box.Restore();
        banner.Box.Restore();
    }

    public void Forget()
    {
        hero.Box.Forget();
        banner.Box.Forget();
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
            hero.Box.SetVisible(true);
            hero.WasShown = false;
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
            banner.Box.SetVisible(true);
            banner.WasShown = false;
        }

        hero.Tick(gui.heroObject != null && gui.heroObject.activeSelf, rig.Head);
        bool any = false;
        foreach (var b in banners)
            any |= b != null && b.activeSelf;
        banner.Tick(any, rig.Head);
    }

    private sealed class Sign
    {
        public readonly SpatialBox Box;
        public bool WasShown;
        private readonly float elevation;
        private Vector3 position;
        private Quaternion rotation = Quaternion.identity;

        public Sign(string name, float elevation)
        {
            Box = new SpatialBox(name);
            this.elevation = elevation;
        }

        /// <summary>Places the sign when the game shows it, then holds it there in the world.</summary>
        public void Tick(bool shown, Transform head)
        {
            if (!Box.Holding)
                return;
            if (shown && !WasShown)
            {
                var eye = head.position;
                position = SpatialMath
                    .Banner(
                        eye.ToNumerics(),
                        PanelMath.YawOf(head.forward.ToNumerics(), head.up.ToNumerics()),
                        Distance,
                        elevation
                    )
                    .ToUnity();
                rotation = SpatialRuntime.FaceEyeYaw(position, eye, head.forward);
            }
            WasShown = shown;
            // The HUD canvas it lives under follows the body, so the pose is set again every frame.
            Box.Pose(
                position,
                rotation,
                SpatialMath.MetersPerPixel(Distance, SpatialRuntime.DegreesPerPixel)
            );
        }
    }
}
