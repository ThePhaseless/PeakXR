namespace PeakXR.UI.Common;

/// <summary>Short UI ticks (menus, wheels, keyboard, wrist status). They go through <see cref="Interact.Haptics"/>,
/// which works whether or not the interaction feature installed, so they follow the same mute
/// (<c>Interaction.Haptics</c>, decision D9), the same strength slider and the same per-hand priority as every other
/// vibration. Does nothing without impulse support and never throws.</summary>
internal static class UIHaptics
{
    public static void Tick(bool left, float amplitude, float seconds) =>
        Interact.Haptics.Ui(left, amplitude, seconds);
}
