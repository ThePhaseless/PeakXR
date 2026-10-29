namespace PeakXR.Core;

/// <summary>
/// Which trigger is the game's primary use and which the secondary. The holding hand's is primary; empty-handed the
/// dominant hand's, like the mouse buttons. While exactly one hand holds the climber (a wall, rope, vine or handle) and
/// nothing is held, that hand is primary, so the free hand is the secondary use (the empty-hand reach to a friend,
/// Character.cs:1200). A piton from the pockets comes from a hand on the wall instead (<see cref="UseTrigger.Piton"/>).
/// </summary>
public static class UseHands
{
    public static bool PrimaryLeft(
        bool holding,
        bool itemLeft,
        bool dominantLeft,
        bool leftClimbs,
        bool rightClimbs
    ) =>
        holding ? itemLeft
        : leftClimbs != rightClimbs ? leftClimbs
        : dominantLeft;
}

/// <summary>
/// One hand's trigger as a use button. A fist that closes on a hold squeezes the trigger with the grip, so a hand whose
/// grip holds the climber is no use button: while climbing, the secondary use sets a piton from the pockets
/// (CharacterItems.cs:1019), and while hanging the uses run the held item. A trigger squeezed into a climb, or held while
/// its role (primary or secondary) changed, stays muted until it opens: no fresh press comes out of it. A press made
/// after the hand settled on the climber (<see cref="ClimbPress"/>) and kept down for <see cref="HoldSeconds"/> sets a
/// piton at that hand (<see cref="Piton"/>), so a closing fist never does.
/// </summary>
public sealed class UseTrigger
{
    /// <summary>A press this long after the hand took its hold is a deliberate one, not part of the closing fist.</summary>
    public const float SettleSeconds = 0.3f;

    /// <summary>How long a <see cref="ClimbPress"/> stays down before it sets a piton: long enough that a brushed
    /// trigger does nothing, short beside the game's own piton cast.</summary>
    public const float HoldSeconds = 0.3f;

    private bool held,
        muted,
        pressedPrimary;
    private float climbing;

    /// <summary>The trigger drives its hand's use this frame.</summary>
    public bool Down { get; private set; }

    /// <summary>This frame's press was made deliberately by a hand that has held the climber for
    /// <see cref="SettleSeconds"/>: not part of a closing fist.</summary>
    public bool ClimbPress { get; private set; }

    /// <summary>A <see cref="ClimbPress"/> is still down and its hand still holds the climber.</summary>
    public bool ClimbHold { get; private set; }

    /// <summary>Seconds the <see cref="ClimbHold"/> has lasted; 0 without one.</summary>
    public float ClimbHeld { get; private set; }

    /// <summary>The <see cref="ClimbHold"/> has lasted <see cref="HoldSeconds"/>: set a piton at this hand.</summary>
    public bool Piton => ClimbHold && ClimbHeld >= HoldSeconds;

    /// <param name="trigger">The trigger is held.</param>
    /// <param name="climbs">The hand's grip holds the climber.</param>
    /// <param name="primary">The hand's trigger is the primary use this frame (<see cref="UseHands"/>).</param>
    /// <param name="dt">Frame time.</param>
    public void Update(bool trigger, bool climbs, bool primary, float dt)
    {
        bool pressed = trigger && !held;
        ClimbPress = pressed && climbs && climbing >= SettleSeconds;
        ClimbHold = ClimbPress || (ClimbHold && trigger && climbs);
        ClimbHeld = ClimbHold && !ClimbPress ? ClimbHeld + dt : 0f;
        if (!trigger)
            muted = false;
        else if (pressed)
        {
            muted = climbs;
            pressedPrimary = primary;
        }
        else if (climbs || primary != pressedPrimary)
            muted = true;
        climbing = climbs ? climbing + dt : 0f;
        held = trigger;
        Down = trigger && !muted;
    }

    public void Reset()
    {
        held = muted = false;
        climbing = 0f;
        Down = ClimbPress = ClimbHold = false;
        ClimbHeld = 0f;
    }
}
