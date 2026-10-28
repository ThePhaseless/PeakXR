namespace PeakXR.Core;

/// <summary>
/// What to tell the player about the graphics API the game started with. On Windows, PEAK starts on Direct3D12
/// unless told otherwise; its own Steam launch option "PEAK using DX11" passes <c>-force-d3d11</c>, Unity's most
/// used VR path, which renders the two MultiPass eyes with less overhead. Under Proton, Vulkan is the tested path on
/// AMD and Intel. On NVIDIA, Proton's OpenXR bridge hands SteamVR the wrong Vulkan entry point and VR crashes at the
/// first swapchain, while Direct3D11 works; Proton's Wayland driver (<c>PROTON_ENABLE_WAYLAND=1</c>) never advertises
/// the OpenXR device extensions, so every Direct3D path then fails to start VR. Under Proton, DXVK's DXGI reports an
/// NVIDIA GPU as AMD (vendor 0x1002) unless NVAPI is on, but keeps the real device name (dxvk
/// src/dxgi/dxgi_adapter.cpp, <c>GetAdapterDesc</c>), so the name decides.
/// </summary>
public static class GraphicsAdvice
{
    private const string Where =
        "(Steam: PEAK, Properties, General, Launch options; a mod manager has its own launch arguments setting)";

    private const int NvidiaVendor = 0x10DE;

    /// <summary>Vendor ids DXVK reports for a hidden NVIDIA GPU: AMD, or 0xDEAD when AMD is hidden too.</summary>
    private static bool MaybeHidden(int vendorId) => vendorId is 0x1002 or 0xDEAD;

    private static bool Has(string name, string marker) =>
        name.Contains(marker, System.StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the GPU is an NVIDIA one: true, false, or null when Direct3D under Proton reports AMD and
    /// the device name names no vendor (a DXVK <c>customDeviceDesc</c>), so a hidden NVIDIA GPU cannot be ruled out.
    /// Vulkan is not behind DXVK's DXGI and reports the real vendor.</summary>
    /// <param name="how">How the verdict was reached, for the log.</param>
    public static bool? Nvidia(
        int vendorId,
        string deviceName,
        string api,
        bool underWine,
        out string how
    )
    {
        var name = deviceName ?? "";
        if (vendorId == NvidiaVendor)
        {
            how = "vendor id";
            return true;
        }
        if (Has(name, "NVIDIA") || Has(name, "GeForce") || Has(name, "RTX") || Has(name, "Quadro"))
        {
            how = "device name (vendor id hidden, likely DXVK without NVAPI)";
            return true;
        }
        if (
            underWine
            && api.StartsWith("Direct3D", System.StringComparison.Ordinal)
            && MaybeHidden(vendorId)
            && !Has(name, "AMD")
            && !Has(name, "Radeon")
        )
        {
            how =
                "unknown: DXVK may report an NVIDIA GPU as AMD and the device name names no vendor";
            return null;
        }
        how = "vendor id and device name";
        return false;
    }

    /// <param name="api">Unity's <c>GraphicsDeviceType</c> name.</param>
    /// <param name="underWine">The game runs under Wine or Proton.</param>
    /// <param name="nvidia">The GPU is an NVIDIA one; null when unknown (<see cref="Nvidia"/>).</param>
    /// <param name="waylandDriver">Proton runs its Wayland driver (<c>PROTON_ENABLE_WAYLAND</c> set and not 0).</param>
    /// <returns>The advice, or null when the API is fine as it is.</returns>
    public static string For(
        string api,
        bool underWine,
        bool? nvidia = false,
        bool waylandDriver = false
    )
    {
        if (underWine && nvidia == null && api != "Vulkan")
            return $"{api} under Proton: could not tell whether the GPU is NVIDIA. On NVIDIA use -force-d3d11 with "
                + "PROTON_ENABLE_WAYLAND=0 before %command%; on AMD or Intel use -force-vulkan instead, in PEAK's "
                + $"Launch options {Where}.";
        if (underWine && nvidia == true)
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
