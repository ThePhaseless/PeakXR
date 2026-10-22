using PeakXR.Avatar;
using PeakXR.Interact;
using PeakXR.VR;
using Zorro.Core;

namespace PeakXR.UI.Spatial;

/// <summary>
/// What an empty hand would grab glows before the grip, through the game's own highlight: <c>HoverEnter</c> /
/// <c>HoverExit</c> of the target (Item.cs:1145-1163: <c>_Interactable</c> on mainRenderer and addtlRenderers; Luggage,
/// kiosks, fuses and FakeItem have their own). Vanilla lights only <c>Interaction.currentHovered</c>
/// (GUIManager.OnInteractChange, GUIManager.cs:1031-1043), the head or pointing ray, so an item at the hand never lit.
/// The target is <see cref="PhysicalInteraction.GripPreview"/>, the same search as the grip. A target the game itself
/// hovers stays the game's to turn off; when the game's hover moves off one of ours (its HoverExit), ours is lit
/// again. Runs before ui.affordance (31300), so a red refusal tint is applied over the glow and restores it after.
/// Local presentation only.
/// </summary>
internal sealed class GripGlow
{
    private const float GripHeld = 0.5f;

    private readonly IInteractible[] lit = new IInteractible[2];
    private IInteractible lastHovered;

    public void Tick()
    {
        var c = Character.localCharacter;
        if (!VRSession.Active || c == null)
        {
            Release();
            return;
        }
        var interaction = Interaction.instance;
        var hovered = interaction != null ? interaction.currentHovered : null;
        bool hoverMoved = !ReferenceEquals(hovered, lastHovered);
        lastHovered = hovered;
        bool holding = c.data.currentItem != null;
        for (int i = 0; i < 2; i++)
        {
            bool left = i == 0;
            ref var hand = ref VRInput.Hand(left);
            bool free =
                hand.Tracked
                && hand.Grip < GripHeld
                && !(holding && (LocalHold.TwoHand || LocalHold.ItemInLeftHand == left));
            var next = free ? PhysicalInteraction.GripPreview(left) : null;
            Set(i, next, hovered, hoverMoved);
        }
    }

    public void Release()
    {
        var interaction = Interaction.instance;
        var hovered = interaction != null ? interaction.currentHovered : null;
        Set(0, null, hovered, false);
        Set(1, null, hovered, false);
        lastHovered = null;
    }

    private void Set(int slot, IInteractible next, IInteractible hovered, bool hoverMoved)
    {
        var was = lit[slot];
        if (ReferenceEquals(was, next))
        {
            // The game's hover just left it (its HoverExit turned the glow off): light it again.
            if (
                next != null
                && hoverMoved
                && !ReferenceEquals(next, hovered)
                && next.UnityObjectExists()
            )
                next.HoverEnter();
            return;
        }
        lit[slot] = next;
        if (
            was.UnityObjectExists()
            && !ReferenceEquals(was, hovered)
            && !ReferenceEquals(was, lit[1 - slot])
        )
            was.HoverExit();
        if (next.UnityObjectExists())
            next.HoverEnter();
    }
}
