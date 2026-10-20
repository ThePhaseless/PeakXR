using BepInEx.Configuration;

namespace PeakXR.UI.Lobby;

/// <summary>Config entries for the airport kiosks: where the boarding pass and run options dock, and
/// the in-world note shown by the invite kiosk.</summary>
internal sealed class UILobbySettings
{
    private const string Section = "UI.Lobby";

    public ConfigEntry<bool> DockAtKiosk;
    public ConfigEntry<float> PassWidth;
    public ConfigEntry<float> OptionsWidth;
    public ConfigEntry<float> MaxDistance;
    public ConfigEntry<bool> InviteNote;
    public ConfigEntry<float> InviteNoteSeconds;

    public static UILobbySettings Bind(ConfigFile cfg) =>
        new()
        {
            DockAtKiosk = cfg.Bind(
                Section,
                "DockAtKiosk",
                true,
                "Show the boarding pass and the custom run options at the check-in kiosk that opened them. Off: in front of your head like other menus."
            ),
            PassWidth = cfg.Bind(
                Section,
                "PassWidth",
                0.9f,
                new ConfigDescription(
                    "Width of the boarding pass panel at the kiosk, in metres.",
                    new AcceptableValueRange<float>(0.5f, 1.6f)
                )
            ),
            OptionsWidth = cfg.Bind(
                Section,
                "OptionsWidth",
                1.1f,
                new ConfigDescription(
                    "Width of the custom run options panel at the kiosk, in metres.",
                    new AcceptableValueRange<float>(0.6f, 1.8f)
                )
            ),
            MaxDistance = cfg.Bind(
                Section,
                "MaxDistance",
                1.3f,
                new ConfigDescription(
                    "Farthest a kiosk panel floats from your eyes, in metres. Farther kiosks bring the panel toward you.",
                    new AcceptableValueRange<float>(0.7f, 2.5f)
                )
            ),
            InviteNote = cfg.Bind(
                Section,
                "InviteNote",
                true,
                "After pressing the invite kiosk, show a note at the kiosk: the Steam invite dialog opens on the desktop overlay, which may not appear in the headset."
            ),
            InviteNoteSeconds = cfg.Bind(
                Section,
                "InviteNoteSeconds",
                8f,
                new ConfigDescription(
                    "How long the invite kiosk note stays up, in seconds.",
                    new AcceptableValueRange<float>(2f, 30f)
                )
            ),
        };
}
