using System;
using BepInEx.Configuration;
using PeakXR.Framework;
using PeakXR.UI.World;

namespace PeakXR.UI.Passport;

public enum PhotoboothFlashMode
{
    /// <summary>Always use the game's photosensitive (soft) flash.</summary>
    Dimmed,

    /// <summary>No flash overlay at all.</summary>
    Off,

    /// <summary>Whatever the game's photosensitivity setting picks.</summary>
    Vanilla,
}

/// <summary>
/// ui.passport: the passport and the guidebook become books held in the hand that holds the item,
/// read at arm's length and turned by swiping the free hand across them (or through
/// <see cref="HandBook.FlipPage"/>), instead of a full-screen menu or a book glued to the camera. The
/// airport photobooth's full-screen strobe is softened, because a flash filling the headset is harsh.
/// </summary>
public sealed class PassportFeature : Feature
{
    public override string Id => "ui.passport";

    // After ui.world (canvas conversion) and ui.menu, which register their own canvas rules.
    public override int Order => 52;

    internal static PassportSettings Settings { get; private set; }

    protected internal override void Install()
    {
        Settings = PassportSettings.Bind(Plugin.Settings);

        // A/Guidebook.cs:197 internal void FlipPageRight(), :206 internal void FlipPageLeft():
        // the same calls the scroll buttons make (A/Action_GuidebookScroll.cs:31-55).
        HandBook.Init(
            GameMember.Delegate<Action<Guidebook>>(typeof(Guidebook), "FlipPageRight"),
            GameMember.Delegate<Action<Guidebook>>(typeof(Guidebook), "FlipPageLeft")
        );
        Patch(typeof(PhotoboothFlashPatch));

        // GAME/PassportManager/PassportUI/Canvas: PassportManager sits on a parent of its only canvas.
        CanvasRules.AddCustom(nameof(PassportManager), new PassportPlacement());
        HandBookRuntime.Create();
    }
}

/// <summary>Config entries for hand-held books and the photobooth. Read live every frame.</summary>
internal sealed class PassportSettings
{
    public ConfigEntry<bool> PassportAtHand,
        GuidebookAtHand,
        SwipeToFlip;
    public ConfigEntry<float> PassportWidth,
        BookLift,
        BookSide;
    public ConfigEntry<PhotoboothFlashMode> PhotoboothFlash;

    public static PassportSettings Bind(ConfigFile cfg)
    {
        const string section = "UI.Passport";
        return new PassportSettings
        {
            PassportAtHand = cfg.Bind(
                section,
                "PassportAtHand",
                true,
                "Show the passport as a book in the hand holding it. Off: a menu panel in front of you."
            ),
            PassportWidth = cfg.Bind(
                section,
                "PassportWidth",
                0.45f,
                Range("Width of the hand-held passport panel, in metres.", 0.3f, 0.8f)
            ),
            GuidebookAtHand = cfg.Bind(
                section,
                "GuidebookAtHand",
                true,
                "Hold an open guidebook or guidebook page in your hand instead of in front of your eyes."
            ),
            BookLift = cfg.Bind(
                section,
                "BookLift",
                0.15f,
                Range("Height of an open book above the hand holding it, in metres.", 0f, 0.35f)
            ),
            BookSide = cfg.Bind(
                section,
                "BookSide",
                0.08f,
                Range(
                    "Sideways shift of an open book toward your free hand, in metres.",
                    0f,
                    0.25f
                )
            ),
            SwipeToFlip = cfg.Bind(
                section,
                "SwipeToFlip",
                true,
                "Turn pages by swiping your free hand across an open book: right to left goes forward."
            ),
            PhotoboothFlash = cfg.Bind(
                section,
                "PhotoboothFlash",
                PhotoboothFlashMode.Dimmed,
                "Airport photobooth flash. Dimmed: always the soft photosensitive flash. Off: no flash. Vanilla: as the game's setting."
            ),
        };
    }

    private static ConfigDescription Range(string text, float min, float max) =>
        new(text, new AcceptableValueRange<float>(min, max));
}
