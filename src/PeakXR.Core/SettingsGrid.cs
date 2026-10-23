using System;

namespace PeakXR.Core
{
    /// <summary>
    /// Shared maths for the VR settings tab: how many rows fit (the single-column fallback pages by it) and which rail
    /// entry a config section belongs to. The headset layout itself is <see cref="VRTabColumn"/>.
    /// </summary>
    public static class SettingsGrid
    {
        /// <summary>Whole items of <paramref name="item"/> with <paramref name="spacing"/> between them that fit in
        /// <paramref name="extent"/>; at least one.</summary>
        public static int Fit(float extent, float item, float spacing)
        {
            float pitch = item + spacing;
            if (pitch <= 0f)
                return 1;
            return Math.Max(1, (int)Math.Floor((extent + spacing) / pitch + 1e-4f));
        }

        /// <summary>The rail entry a config section belongs to: the part before the first dot.</summary>
        public static string GroupOf(string section)
        {
            int dot = section.IndexOf('.');
            return dot < 0 ? section : section.Substring(0, dot);
        }

        /// <summary>The section's name inside its group ("UI.HUD" is "HUD"); empty for the group's own section.</summary>
        public static string SubsectionOf(string section)
        {
            int dot = section.IndexOf('.');
            return dot < 0 ? "" : section.Substring(dot + 1);
        }
    }
}
