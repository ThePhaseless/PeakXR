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
        if (open && (!wasOpen || far))
        {
            var yaw = PanelMath.YawOf(head.forward.ToNumerics(), head.up.ToNumerics());
            var wanted = cfg.EndScreenDistance.Value;
            var offset = PanelMath.InFront(yaw, wanted, Tilt).ToUnity();
            panelRotation = Quaternion.LookRotation(offset, Vector3.up);
            var size = ((RectTransform)canvas.transform).rect.size;
            var half =
                0.5f
                * cfg.EndScreenWidth.Value
                * new Vector2(1f, size.x >= 1f ? size.y / size.x : 9f / 16f);
            var depth = WorldCanvases.WallDepth(head.position, offset, panelRotation, half);
            var (clear, onTop) = PanelMath.FitBeforeWall(wanted, depth);
            scale = clear / wanted;
            panelPosition = head.position + offset * scale;
            placedFor = canvas;
            OnTop.SetWall(canvas.transform, onTop);
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
}
