using PeakXR.Core;
using PeakXR.UI.Common;
using PeakXR.UI.World;

namespace PeakXR.UI.Spatial;

/// <summary>
/// Low stamina (<c>CharacterData.currentStamina</c>) or a new affliction (<c>CharacterAfflictions.currentStatuses</c>,
/// A/CharacterAfflictions.cs:58) pulses the status bar and ticks the controller that carries it, so a climber with
/// eyes on the wall still notices. Read-only: nothing in the game changes.
/// </summary>
internal sealed class StatusAlerts
{
    // Weight comes and goes with every pickup and drop.
    private readonly StatusAlert alert = new()
    {
        Ignore = (int)CharacterAfflictions.STATUSTYPE.Weight,
    };

    private static bool raise;

    /// <summary>A stamina refusal (<c>Affordance.DenyStamina</c>): pulse the bar and tick its wrist on the next
    /// tick, as a low-stamina alert would, so the wrist comes up.</summary>
    public static void Raise() => raise = true;

    public void Release()
    {
        alert.Reset();
        HudSplit.WristPulse = 0f;
    }

    public void Tick(Character c, float dt)
    {
        if (c == null)
            return;
        if (raise)
        {
            raise = false;
            alert.Kick();
        }
        bool fire = alert.Update(c.data.currentStamina, c.refs.afflictions.currentStatuses, dt);
        HudSplit.WristPulse = alert.Pulse;
        if (!fire)
            return;
        var cfg = UIWorldFeature.Settings;
        if (cfg.Hud.Value == HudMode.Wrist)
            UIHaptics.Tick(cfg.WristLeft, 0.4f, 0.06f);
        else
        {
            UIHaptics.Tick(true, 0.3f, 0.05f);
            UIHaptics.Tick(false, 0.3f, 0.05f);
        }
    }
}
