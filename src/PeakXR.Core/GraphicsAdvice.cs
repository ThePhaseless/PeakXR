namespace PeakXR.Core;

/// <summary>
/// What to tell the player about the graphics API the game started with. On Windows, PEAK starts on Direct3D12
/// unless told otherwise; its own Steam launch option "PEAK using DX11" passes <c>-force-d3d11</c>, Unity's most
/// used VR path, which renders the two MultiPass eyes with less overhead. Under Proton, Vulkan is the tested path on
/// AMD and Intel. On NVIDIA, Proton's OpenXR bridge hands SteamVR the wrong Vulkan entry point and VR crashes at the
/// first swapchain, while Direct3D11 works; Proton's Wayland driver (<c>PROTON_ENABLE_WAYLAND=1</c>) never advertises
/// the OpenXR device extensions, so every Direct3D path then fails to start VR.
/// </summary>
public static class GraphicsAdvice
{
    private const string Where =
        "(Steam: PEAK, Properties, General, Launch options; a mod manager has its own launch arguments setting)";

    /// <param name="api">Unity's <c>GraphicsDeviceType</c> name.</param>
    /// <param name="underWine">The game runs under Wine or Proton.</param>
    /// <param name="nvidia">The GPU is an NVIDIA one (vendor id 0x10DE).</param>
    /// <param name="waylandDriver">Proton runs its Wayland driver (<c>PROTON_ENABLE_WAYLAND</c> set and not 0).</param>
    /// <returns>The advice, or null when the API is fine as it is.</returns>
    public static string For(
        string api,
        bool underWine,
        bool nvidia = false,
        bool waylandDriver = false
    )
    {
        if (underWine && nvidia)
        {
            if (api == "Vulkan")
                return "Vulkan VR crashes on NVIDIA under Proton: remove -force-vulkan and add -force-d3d11 to PEAK's "
                    + $"Launch options {Where}, with PROTON_ENABLE_WAYLAND=0 before %command%.";
            if (waylandDriver)
                return $"{api} cannot start VR on Proton's Wayland driver: put PROTON_ENABLE_WAYLAND=0 before %command% "
                    + $"in PEAK's Launch options {Where}.";
            return api == "Direct3D11"
                ? null
                : $"{api} under Proton is less tested than Direct3D11 on NVIDIA: add -force-d3d11 to PEAK's Launch options {Where}.";
        }
        if (api == "Vulkan")
            return null;
        if (underWine)
            return $"{api} under Proton is untested in VR: add -force-vulkan to PEAK's Launch options {Where}.";
        if (api == "Direct3D11")
            return null;
        return $"{api} usually runs VR slower than Direct3D11 and is less tested: for a higher frame rate add -force-d3d11 "
            + $"to PEAK's Launch options {Where}, or pick \"PEAK using DX11\" when Steam asks.";
    }
}
