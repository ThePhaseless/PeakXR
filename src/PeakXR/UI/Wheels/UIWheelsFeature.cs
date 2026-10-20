using BepInEx.Configuration;
using HarmonyLib;
using PeakXR.Framework;
using PeakXR.UI.World;
using UnityEngine;

namespace PeakXR.UI.Wheels;

/// <summary>
/// ui.wheels: the emote wheel and the backpack wheel as small 3D radial menus at the hand that opened them.
/// The game still opens, closes and resolves the wheels (emote held, interact held on a backpack); only the
/// slice under the selection comes from VR: the hand flicked away from the wheel centre, or that hand's stick.
/// The game freezes camera look while a wheel is open, which in a headset would be a world that turns with
/// your head, so head look keeps running.
/// </summary>
public sealed class UIWheelsFeature : Feature
{
    public override string Id => "ui.wheels";

    public override int Order => 58;

    internal static ConfigEntry<float> Size;
    internal static ConfigEntry<float> FlickDistance;
    internal static ConfigEntry<float> Haptics;

    internal static AccessTools.FieldRef<CharacterMovement, Character> MovementCharacter;

    protected internal override void Install()
    {
        const string section = "UI.Wheels";
        Size = Plugin.Settings.Bind(
            section,
            "WheelSize",
            0.3f,
            new ConfigDescription(
                "Diameter of the emote and backpack wheels at your hand, in metres.",
                new AcceptableValueRange<float>(0.15f, 0.6f)
            )
        );
        FlickDistance = Plugin.Settings.Bind(
            section,
            "FlickDistance",
            0.05f,
            new ConfigDescription(
                "How far, in metres, you move the hand from the wheel centre to pick a slice. The stick of the same hand also works.",
                new AcceptableValueRange<float>(0.02f, 0.15f)
            )
        );
        Haptics = Plugin.Settings.Bind(
            section,
            "Haptics",
            0.3f,
            new ConfigDescription(
                "Strength of the tick when the selection moves to another slice. 0 turns it off.",
                new AcceptableValueRange<float>(0f, 1f)
            )
        );

        MovementCharacter = GameMember.Field<CharacterMovement, Character>("character");
        Patch(typeof(HeadLookWhileWheelOpenPatch));
        Patch(typeof(EmoteWheelGamepadPatch));
        Patch(typeof(BackpackWheelGamepadPatch));

        // If a wheel ever sits on a canvas of its own, that canvas must not bring up the menu laser.
        CanvasRules.AddCustom(nameof(EmoteWheel), WheelCanvasPlacement.Instance);
        CanvasRules.AddCustom(nameof(BackpackWheel), WheelCanvasPlacement.Instance);

        RadialWheels.Create();
    }
}

/// <summary>Default placement without the laser; <see cref="RadialWheels"/> moves the wheel itself to the hand.</summary>
internal sealed class WheelCanvasPlacement : ICanvasPlacement
{
    public static readonly WheelCanvasPlacement Instance = new();

    public bool Interactive => false;

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
        return PlacementResult.Default;
    }
}
