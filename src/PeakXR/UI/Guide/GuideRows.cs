using System.Collections.Generic;
using BepInEx.Configuration;
using PeakXR.Core;
using PeakXR.UI.Affordances;
using PeakXR.UI.Common;
using PeakXR.UI.Settings;
using PeakXR.VR;
using UnityEngine;
using UnityEngine.EventSystems;
using Zorro.Core;
using Zorro.Settings;
using Zorro.Settings.DebugUI;

namespace PeakXR.UI.Guide;

internal enum GuideAction : byte
{
    /// <summary>Play the row's demo beside the panel.</summary>
    Demo,

    /// <summary>Open the comfort-first view, or stop a running practice.</summary>
    Practice,

    /// <summary>List the demo rows (they are folded away by default so the settings stay near the top).</summary>
    Expand,

    /// <summary>Comfort chosen: start the practice.</summary>
    Begin,

    /// <summary>Leave the comfort-first view without practising.</summary>
    Back,

    /// <summary>Show the VR tab's previous page (VR shows the long tab a page at a time).</summary>
    PagePrevious,

    /// <summary>Show the VR tab's next page.</summary>
    PageNext,

    /// <summary>A button beside a setting (<see cref="VRTab.AddButtonAfter"/>): runs <see cref="GuideRow.Run"/>.</summary>
    Run,
}

/// <summary>A button row of the VR Guide section; the game's own button cell when this build has one.</summary>
internal sealed class GuideRow : ButtonSetting, IExposedSetting
{
    public readonly GuideAction Action;
    public readonly GuideTopic Topic;
    private readonly string nameKey;
    private readonly string buttonText;

    /// <summary>The spawned row, for placing the demo and the refusal cue; null until shown.</summary>
    public SettingsUICell Cell;

    /// <summary><see cref="GuideAction.Run"/>: what the button does, given the row's spot and the clicking hand.</summary>
    public System.Action<Vector3, bool> Run;

    /// <summary>Frame of the last click, so a click is never handled twice.</summary>
    public int ClickFrame = -1;

    public GuideRow(GuideAction action, GuideTopic topic, string nameKey, string buttonText)
    {
        Action = action;
        Topic = topic;
        this.nameKey = nameKey;
        this.buttonText = buttonText;
    }

    public override void OnClicked(ISettingHandler settingHandler) => GuideRows.Click(this);

    public override string GetButtonText() => buttonText;

    /// <summary>The game ships no button setting, so its build may lack the cell: then the info cell shows the text and
    /// a click anywhere on the row (<see cref="GuideRowHover"/>) does the same.</summary>
    public override GameObject GetSettingUICell()
    {
        var cell = SingletonAsset<InputCellMapper>.Instance.ButtonSettingCell;
        return cell != null ? cell : InfoCell.Template;
    }

    public string GetDisplayName() => nameKey;

    public string GetCategory() => VRTab.CategoryName;
}

/// <summary>Laser rest plays a demo row; a click anywhere on any guide row acts like its button.</summary>
internal sealed class GuideRowHover
    : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerClickHandler
{
    public GuideRow Row;

    /// <summary>Only on the info-cell fallback: a row with the game's button cell clicks through its button.</summary>
    public bool ClicksRow;

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (Row.Action == GuideAction.Demo && GuideStage.Instance != null)
            GuideStage.Instance.Hover(Row, true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (GuideStage.Instance != null)
            GuideStage.Instance.Hover(Row, false);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (ClicksRow)
            GuideRows.Click(Row);
    }
}

/// <summary>
/// The VR Guide section of the VR tab (VR only; flat play keeps the tab as it was): a Practice row, a row that lists
/// the demos, one row per mechanic, and the comfort-first view the practice starts with.
/// </summary>
internal static class GuideRows
{
    /// <summary>Loops a clicked or rested-on demo plays for.</summary>
    public const int DemoLoops = 2;

    private static readonly string[] ComfortKeys = { "TurnMode", "VignetteStrength", "SeatedMode" };

    private static bool expanded;

    /// <summary>The demo rows are listed.</summary>
    public static bool Expanded => expanded;

    /// <summary>The tab shows only the comfort choices and Start / Back before a practice.</summary>
    public static bool ComfortFirst;

    /// <summary>The settings menu the VR tab was last filled into.</summary>
    public static SharedSettingsMenu Menu;

    public static bool Shown => VRSession.Active && GuideStage.Instance != null;

    /// <summary>Header, Practice, the demo list switch and, when listed, one row per mechanic.</summary>
    public static void Append(List<Setting> rows)
    {
        if (!Shown)
        {
            Plugin.Log.LogDebug(
                $"[ui.guide] VR tab without the guide (VR {VRSession.Active}, stage {GuideStage.Instance != null})"
            );
            return;
        }
        Plugin.Log.LogDebug(
            $"[ui.guide] VR tab: guide section, demos {(expanded ? "listed" : "folded")}, practice {(PracticeRun.Running ? "running" : "idle")}"
        );
        rows.Add(Header("PEAKXR_SECTION_GUIDE", "VR Guide"));
        rows.Add(PracticeRow());
        rows.Add(
            new GuideRow(
                GuideAction.Expand,
                GuideTopic.Count,
                Loc.Set("PEAKXR_GUIDE_DEMOS", "Demos"),
                expanded ? "Hide" : "Show"
            )
        );
        if (!expanded)
            return;
        for (int i = 0; i < (int)GuideTopic.Count; i++)
            rows.Add(DemoRow((GuideTopic)i));
    }

    /// <summary>The VR Guide page of the VR layout: Practice, then every demo. The page is the guide's own, so nothing
    /// is folded away.</summary>
    public static void AppendAll(List<Setting> rows)
    {
        rows.Add(PracticeRow());
        rows.Add(Header("PEAKXR_SECTION_DEMOS", "Demos"));
        for (int i = 0; i < (int)GuideTopic.Count; i++)
            rows.Add(DemoRow((GuideTopic)i));
    }

    private static GuideRow PracticeRow() =>
        new(
            GuideAction.Practice,
            GuideTopic.Count,
            Loc.Set("PEAKXR_GUIDE_PRACTICE", "Practice"),
            PracticeRun.Running ? "Stop" : "Start"
        );

    private static GuideRow DemoRow(GuideTopic topic) =>
        new(
            GuideAction.Demo,
            topic,
            Loc.Set("PEAKXR_GUIDE_" + topic, GuideTable.Get(topic).Name),
            "Play"
        );

    /// <summary>The comfort choices with their usual previews, then Start and Back.</summary>
    public static List<Setting> Comfort(System.Func<ConfigEntryBase, Setting> rowFor)
    {
        Plugin.Log.LogDebug("[ui.guide] VR tab: comfort-first practice view");
        var rows = new List<Setting>
        {
            Header("PEAKXR_SECTION_PRACTICE", "Practice: comfort first"),
        };
        foreach (var key in ComfortKeys)
        {
            var def = new ConfigDefinition("Comfort", key);
            if (Plugin.Settings.ContainsKey(def) && rowFor(Plugin.Settings[def]) is { } row)
                rows.Add(row);
            else
                Plugin.Log.LogWarning($"[ui.guide] no Comfort.{key} row for the practice");
        }
        rows.Add(
            new GuideRow(
                GuideAction.Begin,
                GuideTopic.Count,
                Loc.Set("PEAKXR_GUIDE_BEGIN", "Begin practice"),
                "Start"
            )
        );
        rows.Add(
            new GuideRow(
                GuideAction.Back,
                GuideTopic.Count,
                Loc.Set("PEAKXR_GUIDE_BACK", "Not now"),
                "Back"
            )
        );
        return rows;
    }

    private static InfoRow Header(string key, string title) =>
        new(Loc.Set(key, title.ToUpperInvariant()), null, Color.white, true);

    /// <summary>A guide row was spawned into <paramref name="cell"/>.</summary>
    public static void Spawned(GuideRow row, SettingsUICell cell)
    {
        row.Cell = cell;
        var hover = cell.gameObject.AddComponent<GuideRowHover>();
        hover.Row = row;
        hover.ClicksRow = cell.GetComponentInChildren<UnityEngine.UI.Button>(true) == null;
    }

    public static void Click(GuideRow row)
    {
        // One click is one action, however many handlers the event reaches in a frame.
        if (row.ClickFrame == Time.frameCount)
            return;
        row.ClickFrame = Time.frameCount;
        Plugin.Log.LogDebug($"[ui.guide] row clicked: {row.Action} {row.Topic}");
        switch (row.Action)
        {
            case GuideAction.Demo:
                if (GuideStage.Instance != null)
                    GuideStage.Instance.Play(row, DemoLoops);
                break;
            case GuideAction.Expand:
                expanded = !expanded;
                // The demo list starts on the first page, under its switch.
                VRTab.Page = 0;
                Rebuild();
                break;
            case GuideAction.PagePrevious:
                VRTab.Page--;
                Rebuild();
                break;
            case GuideAction.PageNext:
                VRTab.Page++;
                Rebuild();
                break;
            case GuideAction.Practice:
                if (PracticeRun.Running)
                {
                    PracticeRun.Current.Stop();
                    Rebuild();
                }
                else if (PracticeRun.Entry() == PracticeEntry.Refused)
                    Refuse(row);
                else
                {
                    ComfortFirst = true;
                    Rebuild();
                }
                break;
            case GuideAction.Begin:
                var entry = PracticeRun.Entry();
                if (entry == PracticeEntry.Refused || !PracticeRun.Begin(entry))
                {
                    Refuse(row);
                    break;
                }
                ComfortFirst = false;
                if (entry == PracticeEntry.InPlace)
                    Rebuild();
                break;
            case GuideAction.Back:
                ComfortFirst = false;
                Rebuild();
                break;
            case GuideAction.Run:
                row.Run?.Invoke(RowSpot(row), VRInput.Left.Trigger > VRInput.Right.Trigger);
                break;
        }
    }

    /// <summary>Re-fills the VR tab after a change of view.</summary>
    public static void Rebuild()
    {
        if (Menu != null && Menu.isActiveAndEnabled)
        {
            VRTab.Refilling = true;
            Menu.ShowSettings(VRTab.Category);
        }
    }

    /// <summary>Where a cue about <paramref name="row"/> plays: the row, else the head.</summary>
    private static Vector3 RowSpot(GuideRow row) =>
        row.Cell != null ? row.Cell.transform.position
        : VRRig.Instance != null && VRRig.Instance.Head != null ? VRRig.Instance.Head.position
        : Vector3.zero;

    /// <summary>
    /// Practice cannot start here (online, in a run, loading): the refusal cue at the row, never a silent click.
    /// With action hints off, the falling note and a tick still play.
    /// </summary>
    public static void Refuse(GuideRow row)
    {
        var at = RowSpot(row);
        bool left = VRInput.Left.Trigger > VRInput.Right.Trigger;
        Plugin.Log.LogInfo(
            "[ui.guide] practice refused: it runs offline, from the title or the solo airport"
        );
        if (!Affordance.Deny(left, at, DenyReason.NotHere))
        {
            CueAudio.Play(CueSound.Refused, at, 1f);
            UIHaptics.Tick(left, 0.4f, 0.06f);
        }
    }
}
