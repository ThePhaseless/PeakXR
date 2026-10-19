using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace PeakXR.UI.World;

/// <summary>How a converted canvas is placed in the world.</summary>
internal enum CanvasKind
{
    /// <summary>Interactive menu: world-locked panel spawned in front of the head when it opens.</summary>
    Menu,
    /// <summary>Passive overlay (toasts, unknown text-only canvases): rides along with the HUD.</summary>
    Info,
    /// <summary>The game HUD: body-locked, lazily following head yaw.</summary>
    Hud,
    /// <summary>Player name tags: converted, children are placed in the world per tag.</summary>
    Names,
    /// <summary>Left as a screen overlay (mirror window only): letterbox, debug, version string.</summary>
    Hidden,
    /// <summary>Screen fades: left as overlay, their alpha drives <see cref="VR.VRFade"/>.</summary>
    Fade,
    /// <summary>Placed by another feature through <see cref="ICanvasPlacement"/>.</summary>
    Custom,
}

public enum PlacementResult
{
    /// <summary>Hide the canvas this frame.</summary>
    Hidden,
    /// <summary>Use the default menu placement (in front of the head on open).</summary>
    Default,
    /// <summary>Use the pose returned by the placement.</summary>
    Placed,
}

/// <summary>
/// Placement for canvases that need a special spot, registered by other UI features
/// (title menu on the beach, loading panel in the loading space).
/// </summary>
public interface ICanvasPlacement
{
    /// <summary>Whether the laser pointer should be offered while this canvas is shown.</summary>
    bool Interactive { get; }

    /// <summary>Called every frame while the canvas is converted.</summary>
    PlacementResult Place(Canvas canvas, out Vector3 position, out Quaternion rotation, out float widthMeters);
}

/// <summary>
/// Override table for canvases that should not get the default treatment. Rules match a component
/// type name on the canvas, its parents or its children, so they keep working when the game moves things in the hierarchy; a type
/// that no longer exists is skipped. Anything unmatched is a menu if it has selectables, else info.
/// </summary>
public static class CanvasRules
{
    private sealed class Rule
    {
        public string TypeName;
        public Type Type;
        public bool Resolved;
        public CanvasKind Kind;
        public ICanvasPlacement Placement;
        public bool OnlyWithoutSelectables;
    }

    private static readonly List<Rule> custom = new();
    private static readonly List<Rule> defaults = new()
    {
        new Rule { TypeName = "Transitions", Kind = CanvasKind.Fade },
        new Rule { TypeName = "UIPlayerNames", Kind = CanvasKind.Names },
        new Rule { TypeName = "MapDebugUI", Kind = CanvasKind.Hidden },
        new Rule { TypeName = "VersionString", Kind = CanvasKind.Hidden, OnlyWithoutSelectables = true },
        new Rule { TypeName = "Zorro.UI.Modal.Modal", Kind = CanvasKind.Menu },
        new Rule { TypeName = "LoadingScreen", Kind = CanvasKind.Info },
    };

    /// <summary>Give canvases that carry <paramref name="componentType"/> (on the canvas, a parent or a child) a
    /// custom placement. Custom rules win over the built-in table.</summary>
    public static void AddCustom(string componentType, ICanvasPlacement placement)
        => custom.Add(new Rule { TypeName = componentType, Kind = CanvasKind.Custom, Placement = placement });

    internal static CanvasKind Classify(Canvas canvas, bool hasSelectables, bool hasWindows, out ICanvasPlacement placement)
    {
        placement = null;
        var gui = GUIManager.instance;
        if (gui != null)
        {
            if (canvas == gui.hudCanvas)
                return CanvasKind.Hud;
            if (canvas == gui.letterboxCanvas || canvas == gui.binocularOverlay)
                return CanvasKind.Hidden;
        }

        if (Match(custom, canvas, hasSelectables, out var rule) || Match(defaults, canvas, hasSelectables, out rule))
        {
            placement = rule.Placement;
            return rule.Kind;
        }
        return hasSelectables || hasWindows ? CanvasKind.Menu : CanvasKind.Info;
    }

    private static bool Match(List<Rule> rules, Canvas canvas, bool hasSelectables, out Rule match)
    {
        foreach (var rule in rules)
        {
            if (!rule.Resolved)
            {
                rule.Type = AccessTools.TypeByName(rule.TypeName);
                rule.Resolved = true;
                if (rule.Type == null)
                    Plugin.Log.LogWarning($"[ui.world] canvas rule type '{rule.TypeName}' not found; rule skipped");
            }
            if (rule.Type == null || (rule.OnlyWithoutSelectables && hasSelectables))
                continue;

            if (canvas.GetComponentInParent(rule.Type, true) != null || canvas.GetComponentInChildren(rule.Type, true) != null)
            {
                match = rule;
                return true;
            }
        }
        match = null;
        return false;
    }
}
