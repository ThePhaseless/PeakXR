using PeakXR.Core;
using PeakXR.UI.Common;
using PeakXR.UI.World;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.UI.Spatial;

/// <summary>
/// U3: big announcements as a card in front of the head, like a menu's, drawn over everything. <c>SetHeroTitle</c>
/// (A/GUIManager.cs:484) shows <c>heroObject</c> (:144) with the title render texture in <c>heroImage</c> (:152);
/// <c>TheFogRises/TheLavaRises/TheGloomRises/TheGhostsRise</c> (:1195-1232) show <c>fogRises</c>... (:58-64) for 4 s;
/// the ascent title (<c>AscentUI</c>, A/AscentUI.cs:5) and the endgame countdown, win and lose cards
/// (<c>GUIManager.endgame</c>, :69; <c>EndgameCounter</c>, A/EndgameCounter.cs:7-11) get cards too. Each card is
/// head-locked a few degrees above the gaze along the head's own up and faces the eye
/// (<see cref="PanelMath.RaisedView"/>): placed from the head alone, never from the play space, and posed again right
/// before rendering so it never lags the view. It shows exactly while the game shows the part, so it times out with
/// the game's own typing and fades. Its parts sit in a <see cref="SpatialBox"/>, drawn without the depth test after
/// the world and every menu; nothing in the HUD canvas takes the laser, so it never blocks input. Rock nearer than
/// <see cref="Distance"/> pulls it in at the same angular size (<see cref="PanelMath.FitBeforeWall"/>), so the two
/// eyes agree on its depth.
/// </summary>
internal sealed class WorldBanners
{
    /// <summary>Real metres from the eye to a card, a little nearer than a menu (1.8 m), so it reads over one.</summary>
    internal const float Distance = 1.5f;

    /// <summary>Seconds between a shown card's wall tests.</summary>
    private const float WallInterval = 0.1f;

    private readonly Card hero = new("PeakXR_HeroSign", elevationDegrees: 8f);
    private readonly Card banner = new("PeakXR_BannerSign", elevationDegrees: 3f);
    private readonly Card ascent = new("PeakXR_AscentSign", elevationDegrees: 13f);
    private readonly Card endgame = new("PeakXR_EndgameSign", elevationDegrees: 5f);
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

    /// <summary>Re-poses the shown cards on the head's final pose for the frame (<see cref="LatePose"/>).</summary>
    public void Pose(VRRig rig)
    {
        var head = rig.Head;
        hero.Pose(head, false);
        banner.Pose(head, false);
        ascent.Pose(head, false);
        endgame.Pose(head, false);
    }

    private static bool Shown(GameObject go) => go != null && go.activeInHierarchy;

    private sealed class Card
    {
        public readonly SpatialBox Box;
        private readonly float elevationDegrees;
        private bool shown;
        private float scale = 1f;
        private float wallAt;

        public Card(string name, float elevationDegrees)
        {
            Box = new SpatialBox(name);
            this.elevationDegrees = elevationDegrees;
        }

        public void Start() => Box.SetVisible(true);

        /// <summary>Follows the game's show and hide; a shown card is posed and its walls tested.</summary>
        public void Tick(bool shown, Transform head)
        {
            if (shown && !this.shown)
                wallAt = 0f;
            this.shown = shown;
            Pose(head, Time.unscaledTime >= wallAt);
        }

        /// <summary>In front of the head, raised along its up, facing the eye. Allocation-free.</summary>
        public void Pose(Transform head, bool testWalls)
        {
            if (!shown || !Box.Holding || head == null)
                return;
            var (dir, up) = PanelMath.RaisedView(
                head.forward.ToNumerics(),
                head.up.ToNumerics(),
                elevationDegrees
            );
            var forward = dir.ToUnity();
            var rotation = Quaternion.LookRotation(forward, up.ToUnity());
            var wanted = Rig.RealScale.World(Distance);
            if (testWalls)
            {
                wallAt = Time.unscaledTime + WallInterval;
                var mpp = SpatialMath.MetersPerPixel(wanted, SpatialRuntime.DegreesPerPixel);
                scale =
                    ClearDistance(
                        head.position,
                        forward * wanted,
                        rotation,
                        0.5f * mpp * PixelSize()
                    ) / wanted;
            }
            var distance = wanted * scale;
            Box.Pose(
                head.position + forward * distance,
                rotation,
                SpatialMath.MetersPerPixel(distance, SpatialRuntime.DegreesPerPixel)
            );
        }

        /// <summary>The card's size in box pixels: its largest moved part. Wall tests only.</summary>
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

    /// <summary>Distance for a card wanted at the length of <paramref name="offset"/>, half extents
    /// <paramref name="half"/> metres there: just before the nearest rock in front of any part of it. Wall tests
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
