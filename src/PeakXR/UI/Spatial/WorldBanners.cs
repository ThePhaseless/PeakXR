using PeakXR.Core;
using PeakXR.UI.Common;
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
/// placed again if you turn far away from it or move far from it. A cliff in the way brings it closer, at the same
/// angular size, so the two eyes never disagree about its depth. The game's own typing and fades still play.
/// </summary>
internal sealed class WorldBanners
{
    internal const float Distance = 30f;
    internal const float NearestDistance = 4f;

    /// <summary>Re-place a shown sign once it is this far off your heading, in degrees...</summary>
    private const float ReplaceDegrees = 100f;

    /// <summary>...or once you are this far from where it was placed, in metres.</summary>
    private const float ReplaceMeters = 20f;

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
            if (shown && (!wasShown || Strayed(head.position, yaw)))
                Place(head.position, yaw, head.forward);
            wasShown = shown;
            // The HUD canvas it lives under follows the body, so the pose is set again every frame.
            Box.Pose(position, rotation, metersPerPixel);
        }

        private bool Strayed(Vector3 eye, float yaw)
        {
            var to = position - eye;
            var off = Mathf.Abs(Mathf.DeltaAngle(yaw, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg));
            return off > ReplaceDegrees
                || (eye - placedFrom).sqrMagnitude > ReplaceMeters * ReplaceMeters;
        }

        private void Place(Vector3 eye, float yaw, Vector3 forward)
        {
            var far = SpatialMath
                .Banner(eye.ToNumerics(), yaw, Distance, elevationDegrees)
                .ToUnity();
            var dir = (far - eye).normalized;
            float d = Distance;
            // Start past the player's own colliders; placement only, never per frame.
            if (
                Physics.Raycast(
                    eye + dir * 0.5f,
                    dir,
                    out var hit,
                    Distance,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore
                )
            )
                d = Mathf.Clamp(hit.distance, NearestDistance, Distance);
            position = eye + dir * d;
            placedFrom = eye;
            rotation = SpatialRuntime.FaceEyeYaw(position, eye, forward);
            metersPerPixel = SpatialMath.MetersPerPixel(d, SpatialRuntime.DegreesPerPixel);
        }
    }
}
