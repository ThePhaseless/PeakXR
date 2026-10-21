using PeakXR.Core;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Interact.Physical;

/// <summary>
/// Lid lift (G4): a grip on the top half of a closed suitcase (<c>Luggage</c>, Luggage.cs:10) grabs the lid edge.
/// The grip already holds the game's interact on it (interact.physical's grab); this caps the game's own hold timer
/// by how far the hand has risen since the grab (<see cref="LidLift"/>), through the <c>Interaction.DoInteraction</c>
/// prefix. The open itself stays vanilla: <c>Interact_CastFinished</c> sends <c>OpenLuggageRPC</c>
/// (Luggage.cs:243-247) and the lid animates on completion. Respawn chests (RespawnChest.cs:6) need both hands on
/// the lid. A grip low on the case keeps the plain vanilla hold.
/// </summary>
internal static class LidGesture
{
    /// <summary>Hand rise (metres) that counts as a fully raised lid; tune so the lift takes about timeToOpen.</summary>
    public const float LiftHeight = 0.25f;

    private static readonly Luggage[] Grabbed = new Luggage[2];
    private static readonly bool[] OnLid = new bool[2];
    private static readonly float[] StartY = new float[2];

    private static Luggage target;
    private static float fraction;
    private static int frame = -10;

    public static void Reset()
    {
        for (int h = 0; h < 2; h++)
        {
            Grabbed[h] = null;
            OnLid[h] = false;
        }
        target = null;
    }

    public static void Tick(ItemGesturesConfig cfg)
    {
        target = null;
        var rig = VRRig.Instance;
        if (!cfg.LiftLid || rig == null)
        {
            Reset();
            return;
        }
        float left = Track(rig, 0);
        float right = Track(rig, 1);
        var lug =
            OnLid[0] ? Grabbed[0]
            : OnLid[1] ? Grabbed[1]
            : null;
        if (lug == null)
            return;
        bool both = OnLid[0] && OnLid[1] && ReferenceEquals(Grabbed[0], Grabbed[1]);
        if (lug is RespawnChest)
            fraction = both ? Mathf.Min(left, right) : 0f;
        else
            fraction = Mathf.Max(
                OnLid[0] ? left : 0f,
                OnLid[1] && ReferenceEquals(Grabbed[1], lug) ? right : 0f
            );
        target = lug;
        frame = Time.frameCount;
    }

    /// <summary>One hand's lift fraction of its grabbed lid, 0 when it holds none.</summary>
    private static float Track(VRRig rig, int h)
    {
        bool left = h == 0;
        var lug =
            PhysicalInteraction.HandAction(left) == GripPressAction.GrabInteractible
                ? PhysicalInteraction.HandTarget(left) as Luggage
                : null;
        if (lug != null && lug.IsOpen)
            lug = null;
        ref var state = ref VRInput.Hand(left);
        if (!ReferenceEquals(lug, Grabbed[h]))
        {
            Grabbed[h] = lug;
            var t = rig.Hand(left);
            OnLid[h] = lug != null && t != null && t.position.y >= lug.Center().y;
            // Tracking-space height, so walking or the body settling does not count as lifting.
            StartY[h] = state.Position.y;
            if (OnLid[h])
                Haptics.Pulse(left, 0.5f, 0.03f);
        }
        return OnLid[h] ? LidLift.Fraction(state.Position.y - StartY[h], LiftHeight) : 0f;
    }

    /// <summary>Interaction.DoInteraction prefix body: before the game adds this frame's time to the held luggage,
    /// cap it at the lid's lift.</summary>
    public static void BeforeInteraction(Interaction interaction)
    {
        if (frame != Time.frameCount || target == null)
            return;
        if (!ReferenceEquals(interaction.currentHeldInteractible, target))
            return;
        interaction.currentInteractableHeldTime = LidLift.CapHeldTime(
            interaction.currentInteractableHeldTime,
            interaction.currentConstantInteractableTime,
            fraction,
            Time.deltaTime
        );
    }
}
