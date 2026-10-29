using PeakXR.Core;
using PeakXR.UI.Common;
using PeakXR.UI.World;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.UI.RunFlow;

/// <summary>
/// Canvas_End (EndScreen: scout report, timeline, unlock popups, WaitingForPlayersUI; R2 §2.1) as a
/// wide world-locked panel. It is placed once in front of the player when the end screen opens and
/// only re-placed if they end up more than 4 m away, so the dense animated report never swims with
/// the head. The game's own menu width is too narrow for the timeline. Rock nearer than
/// <c>EndScreenDistance</c> pulls it in at the same angular size, drawn on top at the comfort floor
/// (<see cref="PanelMath.FitBeforeWall"/>).
/// </summary>
internal sealed class EndScreenPlacement : ICanvasPlacement
{
    private const float ReplaceDistance = 4f;
    private const float Tilt = 5f;

    private readonly RunFlowSettings cfg;
    private bool wasOpen;
    private Canvas placedFor;
    private Vector3 panelPosition;
    private Quaternion panelRotation = Quaternion.identity;
    private float scale = 1f;
    private Vector3 eye;
    private readonly LazyFollow follow = new();

    public EndScreenPlacement(RunFlowSettings cfg) => this.cfg = cfg;

    public bool Interactive => true;

    public PlacementResult Place(
        Canvas canvas,
        out Vector3 position,
        out Quaternion rotation,
        out float widthMeters
    )
    {
        position = default;
        rotation = Quaternion.identity;
        widthMeters = 0f;

        var rig = VRRig.Instance;
        var head = rig != null ? rig.Head : null;
        if (head == null)
            return PlacementResult.Default;

        var gui = GUIManager.instance;
        var open = gui != null && gui.endScreen != null && gui.endScreen.isOpen;
        var far = (panelPosition - head.position).sqrMagnitude > ReplaceDistance * ReplaceDistance;
        var headYaw = PanelMath.YawOf(head.forward.ToNumerics(), head.up.ToNumerics());
        if (open && (!wasOpen || far))
        {
            eye = head.position;
            follow.Snap(headYaw);
            Seat(canvas);
            placedFor = canvas;
        }
        // UI.MenuFollow LazyFollow: glides back in front once you look past its edge; walls are tested where it stops.
        var ui = UIWorldFeature.Settings;
        if (
            open
            && placedFor == canvas
            && ui != null
            && ui.MenuFollow.Value == MenuFollowMode.LazyFollow
        )
        {
            float halfSpan =
                Mathf.Atan(0.5f * cfg.EndScreenWidth.Value / cfg.EndScreenDistance.Value)
                * Mathf.Rad2Deg;
            follow.Threshold = LazyFollow.ThresholdFor(ui.MenuFollowAngle.Value, halfSpan);
            var dt = Time.unscaledDeltaTime;
            if (follow.Update(headYaw, 0f, LaserPointer.Engaged, dt))
            {
                eye = Vector3.Lerp(
                    eye,
                    head.position,
                    ExpSmooth.Factor(1f / follow.SmoothTime, dt)
                );
                if (follow.Gliding)
                    Pose(cfg.EndScreenDistance.Value * scale);
                else
                    Seat(canvas);
            }
        }
        wasOpen = open;
        // Closed: keep the last pose of this canvas (a new scene's canvas starts unplaced); ui.world
        // drops the laser once the window is no longer open.
        if (placedFor != canvas)
            return PlacementResult.Default;

        position = panelPosition;
        rotation = panelRotation;
        widthMeters = cfg.EndScreenWidth.Value * scale;
        return PlacementResult.Placed;
    }

    /// <summary>Places the panel along the followed heading from <see cref="eye"/>, pulled in front of rock at the same
    /// angular size.</summary>
    private void Seat(Canvas canvas)
    {
        var wanted = cfg.EndScreenDistance.Value;
        var offset = PanelMath.InFront(follow.Yaw, wanted, Tilt).ToUnity();
        var size = ((RectTransform)canvas.transform).rect.size;
        var half =
            0.5f
            * cfg.EndScreenWidth.Value
            * new Vector2(1f, size.x >= 1f ? size.y / size.x : 9f / 16f);
        var depth = WorldCanvases.WallDepth(
            eye,
            offset,
            Quaternion.LookRotation(offset, Vector3.up),
            half
        );
        var (clear, onTop) = PanelMath.FitBeforeWall(wanted, depth);
        scale = clear / wanted;
        Pose(clear);
        OnTop.SetWall(canvas.transform, onTop);
    }

    private void Pose(float distance)
    {
        var offset = PanelMath.InFront(follow.Yaw, distance, Tilt).ToUnity();
        panelRotation = Quaternion.LookRotation(offset, Vector3.up);
        panelPosition = eye + offset;
    }
}
