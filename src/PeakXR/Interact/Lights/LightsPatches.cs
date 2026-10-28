using System.Reflection;
using HarmonyLib;
using PeakXR.Framework;

namespace PeakXR.Interact.Lights;

/// <summary>Harmony patches of <see cref="HandLightsFeature"/>. Local player only.</summary>
internal static class LightsPatches
{
    /// <summary>CharacterInput.Sample(bool) (CharacterInput.cs:212, internal): the game's once-per-frame input
    /// sampling for the local character; usePrimary fields at CharacterInput.cs:238-240 feed CharacterItems.DoUsing
    /// (CharacterItems.cs:201-211). Runs after interact.physical's postfix so its trigger use is already in.</summary>
    [HarmonyPatch]
    internal static class Sample
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(CharacterInput), "Sample", new[] { typeof(bool) });

        [HarmonyPriority(Priority.VeryLow)]
        private static void Postfix(CharacterInput __instance)
        {
            try
            {
                var c = Character.localCharacter;
                if (c != null && c.input == __instance)
                    HandLights.Tick(c, __instance);
            }
            catch (System.Exception e)
            {
                PatchGuard.Fail<HandLightsFeature>(e);
            }
        }
    }
}
