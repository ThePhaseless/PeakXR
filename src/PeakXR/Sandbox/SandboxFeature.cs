using BepInEx.Configuration;
using PeakXR.Framework;
using UnityEngine;

namespace PeakXR.Sandbox;

/// <summary>
/// Creative/test mode (feature id <c>sandbox</c>): unlocks the game's built-in console, adds sandbox
/// console commands, god mode, item and entity spawning, and a flat IMGUI window, so every item,
/// interaction and mob can be exercised in VR and flat. Opt-in via <c>Sandbox.Enabled</c>, and it only acts
/// offline or as the master client (see <see cref="SandboxActions.Active"/>). Runs in flat play too.
/// The actions live in <see cref="SandboxActions"/> so the VR panel can call them later.
/// </summary>
public sealed class SandboxFeature : Feature
{
    internal static ConfigEntry<bool> EnabledEntry;
    internal static ConfigEntry<KeyCode> WindowKeyEntry;
    internal static ConfigEntry<float> SpawnDistanceEntry;

    internal static SandboxDriver Driver { get; private set; }

    internal static float SpawnDistance => SpawnDistanceEntry?.Value ?? 2f;

    public override string Id => "sandbox";

    public override bool VROnly => false;

    protected internal override void Install()
    {
        EnabledEntry = Plugin.Settings.Bind(
            "Sandbox",
            "Enabled",
            false,
            new ConfigDescription(
                "Creative/test mode: unlocks the built-in console and adds spawners and god mode. "
                    + "Only active offline or when you are the host."
            )
        );
        WindowKeyEntry = Plugin.Settings.Bind(
            "Sandbox",
            "WindowKey",
            KeyCode.F7,
            new ConfigDescription(
                "Key that toggles the flat sandbox window. Avoid F1 (console), F5 and Insert (other mods)."
            )
        );
        SpawnDistanceEntry = Plugin.Settings.Bind(
            "Sandbox",
            "SpawnDistance",
            2f,
            new ConfigDescription(
                "How far in front of you spawned items appear, in meters.",
                new AcceptableValueRange<float>(0.5f, 10f)
            )
        );

        // Required: without these the god mode and free camera would silently do nothing.
        FallDamagePatch.CharacterRef = GameMember.Field<CharacterMovement, Character>("character");
        SandboxActions.GodCamRef = GameMember.Field<MainCameraMovement, bool>("isGodCam");
        Patch(typeof(FallDamagePatch));

        // Optional: the window still works without them, it just cannot free the mouse or block game input.
        TryPatch(typeof(WindowInputPatch));
        TryPatch(typeof(WindowCursorPatch));

        var go = new GameObject("PeakXR.Sandbox") { hideFlags = HideFlags.HideAndDontSave };
        Driver = go.AddComponent<SandboxDriver>();

        SandboxCommands.Register();
    }

    void TryPatch(System.Type patchClass)
    {
        try
        {
            Patch(patchClass);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning(
                $"[sandbox] optional patch {patchClass.Name} skipped: {e.GetBaseException().Message}"
            );
        }
    }
}
