using System.Reflection;
using System.Text;
using UnityEngine;
using Zorro.Core.CLI;

namespace PeakXR.Sandbox;

/// <summary>
/// Sandbox commands for the game's built-in console (unlocked by the sandbox). The console finds
/// <c>[ConsoleCommand]</c> methods by scanning all assemblies once (decomp Zorro.Core.Runtime/Zorro.Core.CLI/ConsoleHandler.cs:40):
/// in <c>GameHandler.Awake</c> up to 2.6.b, before the splash screen since 3.0 (Assembly-CSharp/PlatformBootstrap.cs:13-21).
/// <see cref="Register"/> also adds them to <c>ConsoleHandler.ConsoleCommands</c> in case that scan ran before this plugin loaded.
/// Type commands as <c>Sandbox.Spawn Rope</c>. Parameters are limited to what the console parses
/// (string, int, float, bool, enums, <c>Item</c>).
/// </summary>
[ConsoleClassCustomizer("Sandbox")]
public static class SandboxCommands
{
    [ConsoleCommand]
    public static void Spawn(Item item) => SandboxActions.SpawnItem(item);

    [ConsoleCommand]
    public static void Give(Item item) => SandboxActions.GiveItem(item);

    [ConsoleCommand]
    public static void SpawnAll() => SandboxActions.SpawnTestLine();

    [ConsoleCommand]
    public static void ListItems()
    {
        var sb = new StringBuilder();
        foreach (var item in SandboxActions.Items)
            sb.Append(item.name).Append(", ");
        SandboxActions.Say($"{SandboxActions.Items.Count} items: {sb}");
    }

    [ConsoleCommand]
    public static void ListMobs()
    {
        var sb = new StringBuilder();
        foreach (var mob in SandboxActions.Mobs)
            sb.Append(mob.Name).Append(", ");
        SandboxActions.Say($"{SandboxActions.Mobs.Count} entities: {sb}");
    }

    [ConsoleCommand]
    public static void ScanMobs() => SandboxActions.RefreshMobs(deep: true);

    [ConsoleCommand]
    public static void Mob(string name) => SandboxActions.SpawnMob(name);

    [ConsoleCommand]
    public static void God() => SandboxActions.ToggleGodMode();

    [ConsoleCommand]
    public static void Revive() => SandboxActions.Revive();

    [ConsoleCommand]
    public static void Warp() => SandboxActions.WarpToSpawn();

    [ConsoleCommand]
    public static void Jump(Segment segment) => SandboxActions.JumpToSegment(segment);

    [ConsoleCommand]
    public static void SetTime(float hours) => SandboxActions.SetTimeOfDay(hours);

    [ConsoleCommand]
    public static void FreeCam() => SandboxActions.ToggleFreeCam();

    [ConsoleCommand]
    public static void Stamina(float current, float extra) =>
        SandboxActions.SetStamina(current, extra);

    [ConsoleCommand]
    public static void MaxStamina(float max) => SandboxActions.SetMaxStamina(max);

    [ConsoleCommand]
    public static void AddStatus(CharacterAfflictions.STATUSTYPE type, float amount) =>
        SandboxActions.AddStatus(type, amount);

    [ConsoleCommand]
    public static void ClearStatus(CharacterAfflictions.STATUSTYPE type) =>
        SandboxActions.ClearStatus(type);

    [ConsoleCommand]
    public static void PassOut() => SandboxActions.PassOut();

    [ConsoleCommand]
    public static void RunSetting(RunSettings.SETTINGTYPE type, int value) =>
        SandboxActions.SetRunSetting(type, value);

    [ConsoleCommand]
    public static void GiveLeft(Item item) => SandboxActions.GiveItem(item, left: true);

    [ConsoleCommand]
    public static void GiveRight(Item item) => SandboxActions.GiveItem(item, left: false);

    [ConsoleCommand]
    public static void Wall() => SandboxActions.PlaceWall();

    /// <summary>Add the commands above to the live console list. False until the console has initialised.</summary>
    internal static bool Register()
    {
        var list = ConsoleHandler.ConsoleCommands;
        if (list == null)
            return false;

        foreach (
            var method in typeof(SandboxCommands).GetMethods(
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly
            )
        )
        {
            if (method.GetCustomAttribute<ConsoleCommandAttribute>() == null)
                continue;

            bool present = false;
            foreach (var existing in list)
            {
                if (
                    existing.MethodInfo.DeclaringType == method.DeclaringType
                    && existing.MethodInfo.Name == method.Name
                )
                {
                    present = true;
                    break;
                }
            }
            if (!present)
                list.Add(new ConsoleCommand(method));
        }
        return true;
    }
}
