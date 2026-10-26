namespace PeakXR.Core
{
    /// <summary>
    /// What to tell the player about the graphics API the game started with. On Windows, PEAK starts on Direct3D12
    /// unless told otherwise; its own Steam launch option "PEAK using DX11" passes <c>-force-d3d11</c>, Unity's most
    /// used VR path, which renders the two MultiPass eyes with less overhead. Under Proton, both Direct3D paths go
    /// through extra translation to reach the OpenXR bridge, and Vulkan is the tested one.
    /// </summary>
    public static class GraphicsAdvice
    {
        /// <param name="api">Unity's <c>GraphicsDeviceType</c> name.</param>
        /// <param name="underWine">The game runs under Wine or Proton.</param>
        /// <returns>The advice, or null when the API is fine as it is.</returns>
        public static string For(string api, bool underWine)
        {
            if (api == "Vulkan")
                return null;
            if (underWine)
                return $"{api} under Proton is untested in VR: add -force-vulkan to PEAK's Launch options "
                    + "(Steam: PEAK, Properties, General, Launch options).";
            if (api == "Direct3D11")
                return null;
            return $"{api} usually runs VR slower than Direct3D11 and is less tested: for a higher frame rate add -force-d3d11 "
                + "to PEAK's Launch options (Steam: PEAK, Properties, General, Launch options; a mod manager has its "
                + "own launch arguments setting), or pick \"PEAK using DX11\" when Steam asks.";
        }
    }
}
