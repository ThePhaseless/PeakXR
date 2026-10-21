using PeakXR.Core;
using PeakXR.UI.World;
using PeakXR.VR;
using Portningsbolaget;
using UnityEngine;

namespace PeakXR.UI.Spatial;

/// <summary>
/// Small status icons that would otherwise ride the body panel go on the wrist, just below the status bar: the
/// quicksave toast (<c>GUIManager.quickSave</c>, A/GUIManager.cs:252, shown at :1267), the mute icon
/// (<c>muteIcon</c>, :78, set at :381) and the voice indicators (<c>VoiceIndicatorUiHandler</c>,
/// A/Portningsbolaget/VoiceIndicatorUiHandler.cs). Only in wrist mode while the wrist is tracked; otherwise they
/// stay on the body panel.
/// </summary>
internal sealed class WristExtras
{
    private const float Spacing = 4f;
    private const float Below = 0.035f;

    private readonly SpatialBox box = new("PeakXR_WristExtras");
    private int shownMask = -1;

    public void Release()
    {
        box.Restore();
        shownMask = -1;
    }

    public void Forget()
    {
        box.Forget();
        shownMask = -1;
    }

    public void Tick(GUIManager gui, RectTransform hudRoot, VRRig rig)
    {
        if (!box.Holding)
        {
            if (gui.quickSave != null)
                box.Move(hudRoot, gui.quickSave.transform, Vector2.zero);
            if (gui.muteIcon != null)
                box.Move(hudRoot, gui.muteIcon.transform, Vector2.zero);
            var voice = gui.hudCanvas.GetComponentInChildren<VoiceIndicatorUiHandler>(true);
            if (voice != null)
                box.Move(hudRoot, voice.transform, Vector2.zero);
            if (!box.Holding)
                return;
            box.SetVisible(true);
        }
        int mask = 0;
        for (int i = 0; i < box.Count && i < 31; i++)
        {
            var item = box.Item(i);
            if (item != null && item.gameObject.activeSelf)
                mask |= 1 << i;
        }
        if (mask != shownMask)
        {
            shownMask = mask;
            box.Stack(Spacing);
        }
        Pose(rig);
    }

    public void Pose(VRRig rig)
    {
        if (!box.Holding || !HudSplit.WristValid)
            return;
        var rotation = HudSplit.WristRotation;
        var p = HudSplit.WristPosition + rotation * (Vector3.down * Below);
        var mpp = SpatialMath.MetersPerPixel(
            Vector3.Distance(p, rig.Head.position),
            SpatialRuntime.DegreesPerPixel * 0.8f
        );
        box.Pose(p, rotation, mpp);
    }
}
