using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.UI.Affordances;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.UI.Passport;

/// <summary>
/// Try a cosmetic on by touch: the free hand points (index straight) and pokes a cell of the hand-held passport, which
/// presses it like a finger on a page. The press is the cell's own <c>PassportButton.Click</c> (A/PassportButton.cs:63,
/// equipping through PassportManager.SetOption :277), so a locked cell still refuses through
/// <see cref="PassportLockPatch"/>, at the poking hand. An equip ticks the hand once.
/// </summary>
internal static class PassportPoke
{
    // The same fingertip the VR keyboard pokes with: 4 cm ahead of the aim pose.
    private const float TipForward = 0.04f;

    private static readonly PokeButton Poke = new();

    /// <summary>True while a poke drives the click, so the lock refusal lands on the poking hand.</summary>
    public static bool Active { get; private set; }

    public static bool Left { get; private set; }

    /// <summary>Equips by poke since start, for the self-test.</summary>
    public static int Equips { get; private set; }

    /// <summary>Index straight, the other fingers closed: the hand presses instead of swiping a page.</summary>
    public static bool Pointing(bool left) =>
        VRInput.Hand(left).Tracked && GripIntent.IsPointing(LocalAvatar.ControllerCurls(left));

    public static void Reset() => Poke.Reset();

    /// <summary>Per frame while the passport is open at the other hand; <paramref name="canvas"/> is its placed canvas.</summary>
    public static void Tick(VRRig rig, PassportManager passport, Transform canvas, bool left)
    {
        var aim = rig.Aim(left);
        if (aim == null || !Pointing(left))
        {
            Poke.Reset();
            return;
        }
        var tip = aim.position + aim.forward * TipForward;
        PassportButton hit = null;
        foreach (var b in passport.buttons)
        {
            if (b == null || !b.gameObject.activeInHierarchy)
                continue;
            var rect = (RectTransform)b.transform;
            var p = rect.InverseTransformPoint(tip);
            if (rect.rect.Contains(new Vector2(p.x, p.y)))
            {
                hit = b;
                break;
            }
        }
        // The page faces the eyes along its -Z, so "in front" is toward -Z (as the VR keyboard).
        float front = -canvas.InverseTransformPoint(tip).z * canvas.lossyScale.x;
        if (Poke.Update(front, hit != null) && hit != null)
            Press(hit, left);
    }

    private static void Press(PassportButton button, bool left)
    {
        bool locked = button.lockedIcon != null && button.lockedIcon.gameObject.activeSelf;
        Active = true;
        Left = left;
        try
        {
            button.Click();
        }
        finally
        {
            Active = false;
        }
        if (locked)
            return;
        Equips++;
        Affordance.Contact(left);
    }
}
