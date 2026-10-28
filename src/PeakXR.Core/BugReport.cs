using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace PeakXR.Core;

/// <summary>
/// A fixed-size ring that keeps the newest <see cref="Capacity"/> items, for the always-on bug-report history (log
/// lines, refusals, poses). <see cref="Add"/> overwrites one slot and never allocates, so it is safe on hot paths.
/// Not thread-safe: the caller locks when several threads write.
/// </summary>
public sealed class Ring<T>
{
    private readonly T[] items;
    private int next;

    public Ring(int capacity) => items = new T[Math.Max(1, capacity)];

    public int Capacity => items.Length;

    public int Count { get; private set; }

    /// <summary>Items ever added, including the ones overwritten since.</summary>
    public long Total { get; private set; }

    public void Add(in T item)
    {
        items[next] = item;
        next = (next + 1) % items.Length;
        if (Count < items.Length)
            Count++;
        Total++;
    }

    /// <summary>Item <paramref name="i"/>, oldest first.</summary>
    public T this[int i]
    {
        get
        {
            if (i < 0 || i >= Count)
                throw new ArgumentOutOfRangeException(nameof(i));
            int start = Count < items.Length ? 0 : next;
            return items[(start + i) % items.Length];
        }
    }

    /// <summary>A copy, oldest first (allocates: for saving a report, never per frame).</summary>
    public T[] ToArray()
    {
        var copy = new T[Count];
        for (int i = 0; i < Count; i++)
            copy[i] = this[i];
        return copy;
    }

    public void Clear()
    {
        Array.Clear(items, 0, items.Length);
        next = 0;
        Count = 0;
    }
}

/// <summary>One controller in a <see cref="PoseSample"/>: tracking-space pose, analog values, buttons and the curls
/// the avatar shows.</summary>
public struct PoseHand
{
    public bool Tracked;
    public Vector3 Position;
    public Quaternion Rotation;
    public float Trigger;
    public float Grip;
    public Vector2 Stick;
    public bool Primary;
    public bool Secondary;
    public bool StickClick;
    public bool Menu;
    public bool TriggerTouch;
    public FingerCurls Curls;
}

/// <summary>One 10 Hz debug-mode sample: head and hands, and the body state the game had. <see cref="Held"/> is a
/// cached name reference (looked up only when the held item changes).</summary>
public struct PoseSample
{
    /// <summary><see cref="DateTime.Ticks"/> of the local wall clock, matching the log's timestamps.</summary>
    public long Wall;

    /// <summary>The game's unscaled time.</summary>
    public float T;
    public bool HeadTracked;
    public Vector3 Head;
    public Quaternion HeadRotation;
    public PoseHand Left;
    public PoseHand Right;
    public bool Grounded;
    public bool Climbing;
    public float Stamina;
    public string Held;
}

/// <summary>The pose recorder's CSV: one header, one row per <see cref="PoseSample"/>, invariant numbers.</summary>
public static class PoseCsv
{
    private const string HandColumns =
        "tracked,x,y,z,qx,qy,qz,qw,trigger,grip,stick_x,stick_y,buttons,thumb,index,middle,ring,pinky";

    /// <summary>Buttons are letters: P primary (A/X), S secondary (B/Y), C stick click, M menu, T trigger touch;
    /// "-" for none.</summary>
    public static readonly string Header =
        "wall,t,head_tracked,head_x,head_y,head_z,head_qx,head_qy,head_qz,head_qw,"
        + Prefix("l_")
        + ","
        + Prefix("r_")
        + ",grounded,climbing,stamina,held";

    private static string Prefix(string p) => p + HandColumns.Replace(",", "," + p);

    public static void Row(StringBuilder sb, in PoseSample s)
    {
        var c = CultureInfo.InvariantCulture;
        sb.Append(new DateTime(s.Wall).ToString("HH:mm:ss.fff", c)).Append(',');
        sb.Append(s.T.ToString("0.00", c)).Append(',');
        Bit(sb, s.HeadTracked);
        Vec(sb, s.Head);
        Quat(sb, s.HeadRotation);
        Hand(sb, s.Left);
        Hand(sb, s.Right);
        Bit(sb, s.Grounded);
        Bit(sb, s.Climbing);
        sb.Append(s.Stamina.ToString("0.00", c)).Append(',');
        if (s.Held != null)
            Field(sb, s.Held);
    }

    private static void Hand(StringBuilder sb, in PoseHand h)
    {
        var c = CultureInfo.InvariantCulture;
        Bit(sb, h.Tracked);
        Vec(sb, h.Position);
        Quat(sb, h.Rotation);
        sb.Append(h.Trigger.ToString("0.00", c)).Append(',');
        sb.Append(h.Grip.ToString("0.00", c)).Append(',');
        sb.Append(h.Stick.X.ToString("0.00", c)).Append(',');
        sb.Append(h.Stick.Y.ToString("0.00", c)).Append(',');
        int n = sb.Length;
        if (h.Primary)
            sb.Append('P');
        if (h.Secondary)
            sb.Append('S');
        if (h.StickClick)
            sb.Append('C');
        if (h.Menu)
            sb.Append('M');
        if (h.TriggerTouch)
            sb.Append('T');
        if (sb.Length == n)
            sb.Append('-');
        sb.Append(',');
        for (int i = 0; i < FingerCurls.Count; i++)
            sb.Append(h.Curls[i].ToString("0.00", c)).Append(',');
    }

    private static void Bit(StringBuilder sb, bool b) => sb.Append(b ? '1' : '0').Append(',');

    private static void Vec(StringBuilder sb, Vector3 v)
    {
        var c = CultureInfo.InvariantCulture;
        sb.Append(v.X.ToString("0.000", c)).Append(',');
        sb.Append(v.Y.ToString("0.000", c)).Append(',');
        sb.Append(v.Z.ToString("0.000", c)).Append(',');
    }

    private static void Quat(StringBuilder sb, Quaternion q)
    {
        var c = CultureInfo.InvariantCulture;
        sb.Append(q.X.ToString("0.0000", c)).Append(',');
        sb.Append(q.Y.ToString("0.0000", c)).Append(',');
        sb.Append(q.Z.ToString("0.0000", c)).Append(',');
        sb.Append(q.W.ToString("0.0000", c)).Append(',');
    }

    private static readonly char[] Quoted = { ',', '"', '\n', '\r' };

    /// <summary>RFC 4180: a field with a comma, quote or line break is quoted, quotes doubled.</summary>
    private static void Field(StringBuilder sb, string s)
    {
        if (s.IndexOfAny(Quoted) < 0)
        {
            sb.Append(s);
            return;
        }
        sb.Append('"').Append(s.Replace("\"", "\"\"")).Append('"');
    }
}

public enum BugChordEvent
{
    None,

    /// <summary>The chord was let go before <see cref="BugChord.ReportSeconds"/>: drop a marker.</summary>
    Marker,

    /// <summary>The chord was held for <see cref="BugChord.ReportSeconds"/>: save a bug report.</summary>
    Report,
}

/// <summary>
/// The in-play bug-report gesture: hold Menu on one controller and click the other controller's stick. Let go
/// before <see cref="ReportSeconds"/> for a marker, hold on for a report. Menu acts only on release (pause) or after
/// a 1 s hold (recenter), so once the chord starts both its controls are taken (<see cref="MenuTaken"/>,
/// <see cref="StickTaken"/>) until each is released, its release frame included: no pause tap, recenter, sprint or
/// crouch leaks to the game. Both thumbs, so it needs two controllers; one-handed play uses the VR tab buttons.
/// </summary>
public sealed class BugChord
{
    public float ReportSeconds = 1.5f;

    private bool fired;
    private bool menuTaken;
    private bool stickTaken;
    private bool menuOut;
    private bool stickOut;

    /// <summary>The chord is held now.</summary>
    public bool Active { get; private set; }

    /// <summary>Seconds the current chord has been held.</summary>
    public float Held { get; private set; }

    /// <summary>0..1 of <see cref="ReportSeconds"/> held; 0 while no chord is held.</summary>
    public float Progress => !Active ? 0f : Math.Min(1f, Held / Math.Max(1e-3f, ReportSeconds));

    /// <summary>The current or last chord's Menu is on the left controller (its stick on the right).</summary>
    public bool MenuLeft { get; private set; }

    /// <summary>This frame, the chord owns that controller's Menu: rig.camera's recenter and the pause tap ignore it.</summary>
    public bool MenuTaken(bool left) => menuOut && left == MenuLeft;

    /// <summary>This frame, the chord owns that controller's stick click: the layout's sprint or crouch ignores it.</summary>
    public bool StickTaken(bool left) => stickOut && left != MenuLeft;

    public BugChordEvent Update(bool menuL, bool stickL, bool menuR, bool stickR, float dt)
    {
        var e = BugChordEvent.None;
        if (!Active)
        {
            bool leftMenu = menuL && stickR;
            if (leftMenu || (menuR && stickL))
            {
                Active = true;
                MenuLeft = leftMenu;
                Held = 0f;
                fired = false;
                menuTaken = stickTaken = true;
            }
        }
        else
        {
            bool menu = MenuLeft ? menuL : menuR;
            bool stick = MenuLeft ? stickR : stickL;
            if (menu && stick)
            {
                Held += dt;
                if (!fired && Held >= ReportSeconds)
                {
                    fired = true;
                    e = BugChordEvent.Report;
                }
            }
            else
            {
                Active = false;
                if (!fired)
                    e = BugChordEvent.Marker;
            }
        }
        // Each control stays taken through the frame it reads released.
        menuOut = menuTaken;
        stickOut = stickTaken;
        if (!(MenuLeft ? menuL : menuR))
            menuTaken = false;
        if (!(MenuLeft ? stickR : stickL))
            stickTaken = false;
        return e;
    }
}

/// <summary>One log line: a PeakXR BepInEx line or a Unity error with its stack. <see cref="Data"/> is the logged
/// object, kept as is and turned into text only when a report is saved.</summary>
public struct LogEntry
{
    public long Wall;
    public string Level;
    public object Data;
    public string Stack;
}

/// <summary>One accepted refusal (<c>Affordance.Deny</c>).</summary>
public struct RefusalEntry
{
    public long Wall;
    public bool Left;
    public DenyReason Reason;
    public Vector3 At;
}

/// <summary>A "something happened here" marker the player dropped.</summary>
public struct MarkerEntry
{
    public long Wall;
    public int Number;
    public string Note;
}

/// <summary>Plain data for report.txt, gathered on the main thread and composed anywhere.</summary>
public sealed class BugReportInfo
{
    public DateTime Saved;
    public readonly IList<KeyValuePair<string, IList<KeyValuePair<string, string>>>> Sections =
        new List<KeyValuePair<string, IList<KeyValuePair<string, string>>>>();
    public readonly IList<RefusalEntry> Refusals = new List<RefusalEntry>();
    public readonly IList<MarkerEntry> Markers = new List<MarkerEntry>();

    /// <summary>The last grip decision per hand (0 left, 1 right), null when none yet.</summary>
    public readonly string[] GripNotes = new string[2];
    public readonly long[] GripNoteWalls = new long[2];

    /// <summary>Adds <paramref name="key"/> to <paramref name="section"/>; sections keep their first-use order.</summary>
    public void Add(string section, string key, string value)
    {
        foreach (var s in Sections)
            if (s.Key == section)
            {
                s.Value.Add(new(key, value));
                return;
            }
        Sections.Add(new(section, new List<KeyValuePair<string, string>> { new(key, value) }));
    }
}

/// <summary>Text for the bug report files: report.txt, log.txt, the folder name and the privacy pass.</summary>
public static class BugReportText
{
    private static readonly CultureInfo C = CultureInfo.InvariantCulture;
    private static readonly Regex SteamId = new(
        @"\b7656119\d{10}\b",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1)
    );

    // The account name in a home folder path (Linux, macOS, Windows and Wine's Z: view of them).
    private static readonly Regex HomeUser = new(
        @"(?<root>[\\/](?:home|Users|users)[\\/])[^\\/\r\n:;,""'()\[\]]+",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(1)
    );

    public static string Clock(long wall) => new DateTime(wall).ToString("HH:mm:ss.fff", C);

    /// <summary>Both rings merged by time, one line each; a stack follows its line, indented.</summary>
    public static string Log(IReadOnlyList<LogEntry> mod, IReadOnlyList<LogEntry> unity)
    {
        var sb = new StringBuilder();
        int a = 0,
            b = 0;
        while (a < mod.Count || b < unity.Count)
        {
            var e =
                b >= unity.Count || (a < mod.Count && mod[a].Wall <= unity[b].Wall)
                    ? mod[a++]
                    : unity[b++];
            sb.Append(Clock(e.Wall)).Append(" [").Append(e.Level).Append("] ");
            sb.Append(e.Data?.ToString()).Append('\n');
            if (string.IsNullOrEmpty(e.Stack))
                continue;
            foreach (var line in e.Stack.Split('\n'))
                if (line.Trim().Length > 0)
                    sb.Append("    ").Append(line.TrimEnd('\r')).Append('\n');
        }
        return sb.ToString();
    }

    public static string Compose(BugReportInfo info)
    {
        var sb = new StringBuilder();
        sb.Append("PeakXR bug report\n");
        sb.Append("Saved ").Append(info.Saved.ToString("yyyy-MM-dd HH:mm:ss", C)).Append('\n');
        foreach (var section in info.Sections)
        {
            sb.Append('\n').Append('[').Append(section.Key).Append("]\n");
            foreach (var kv in section.Value)
                sb.Append(kv.Key).Append(": ").Append(kv.Value).Append('\n');
        }

        sb.Append("\n[Refusals, oldest first]\n");
        if (info.Refusals.Count == 0)
            sb.Append("none\n");
        foreach (var r in info.Refusals)
            sb.Append(Clock(r.Wall))
                .Append(r.Left ? " left " : " right ")
                .Append(r.Reason)
                .Append(" at (")
                .Append(r.At.X.ToString("0.00", C))
                .Append(", ")
                .Append(r.At.Y.ToString("0.00", C))
                .Append(", ")
                .Append(r.At.Z.ToString("0.00", C))
                .Append(")\n");

        sb.Append("\n[Last grip decision]\n");
        for (int i = 0; i < 2; i++)
        {
            sb.Append(i == 0 ? "left: " : "right: ");
            if (info.GripNotes[i] == null)
                sb.Append("none\n");
            else
                sb.Append(Clock(info.GripNoteWalls[i]))
                    .Append(' ')
                    .Append(info.GripNotes[i])
                    .Append('\n');
        }

        sb.Append("\n[Markers]\n");
        if (info.Markers.Count == 0)
            sb.Append("none\n");
        foreach (var m in info.Markers)
        {
            sb.Append('#').Append(m.Number.ToString(C)).Append(' ').Append(Clock(m.Wall));
            if (!string.IsNullOrEmpty(m.Note))
                sb.Append(' ').Append(m.Note);
            sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary><c>yyyyMMdd-HHmmss</c>, with <c>-2</c>, <c>-3</c>... when a report already took that second.</summary>
    public static string FolderName(DateTime now, Func<string, bool> taken)
    {
        string stamp = now.ToString("yyyyMMdd-HHmmss", C);
        if (!taken(stamp))
            return stamp;
        for (int i = 2; ; i++)
        {
            string name = stamp + "-" + i.ToString(C);
            if (!taken(name))
                return name;
        }
    }

    /// <summary>
    /// The privacy pass over every text file: each name (player nicknames, the OS user) becomes its label wherever it
    /// stands as a whole word, longest names first so "Ann Lee" wins over "Ann"; the account name in a home folder
    /// path becomes "[user]" and Steam IDs become "[steam id]".
    /// </summary>
    public static string Redact(string text, IReadOnlyList<KeyValuePair<string, string>> names)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        var order = new List<KeyValuePair<string, string>>(names);
        order.Sort((x, y) => (y.Key?.Length ?? 0).CompareTo(x.Key?.Length ?? 0));
        foreach (var kv in order)
            if (!string.IsNullOrEmpty(kv.Key))
                text = ReplaceWord(text, kv.Key, kv.Value);
        text = HomeUser.Replace(text, "${root}[user]");
        return SteamId.Replace(text, "[steam id]");
    }

    private static string ReplaceWord(string text, string word, string with)
    {
        var sb = new StringBuilder(text.Length);
        int from = 0;
        while (true)
        {
            int i = text.IndexOf(word, from, StringComparison.Ordinal);
            if (i < 0)
                break;
            int end = i + word.Length;
            bool whole =
                (i == 0 || !char.IsLetterOrDigit(text[i - 1]))
                && (end == text.Length || !char.IsLetterOrDigit(text[end]));
            sb.Append(text, from, i - from).Append(whole ? with : word);
            from = end;
        }
        return sb.Append(text, from, text.Length - from).ToString();
    }
}

/// <summary>
/// Both stick clicks held together: the recenter and bug-report chord that works on every controller. Under SteamVR
/// a Touch left Menu opens the dashboard and the right controller has none, so Menu alone cannot be relied on. The
/// caller recenters while <see cref="Active"/> lasts <see cref="RecenterSeconds"/>; holding on for
/// <see cref="ReportSeconds"/> more saves a report. Both sticks are taken from the chord's start through each one's
/// release frame (<see cref="StickTaken"/>), so the layout's sprint and crouch let go while it is held.
/// </summary>
public sealed class StickChord
{
    public float RecenterSeconds = 1f;
    public float ReportSeconds = 1.5f;

    private bool fired;
    private bool takenL;
    private bool takenR;
    private bool outL;
    private bool outR;

    /// <summary>Both sticks are held now, as one chord.</summary>
    public bool Active { get; private set; }

    /// <summary>Seconds the current chord has been held.</summary>
    public float Held { get; private set; }

    /// <summary>0..1 of the report hold, which starts once the recenter time has passed; 0 while not active.</summary>
    public float ReportProgress =>
        !Active
            ? 0f
            : Math.Max(0f, Math.Min(1f, (Held - RecenterSeconds) / Math.Max(1e-3f, ReportSeconds)));

    /// <summary>This frame, the chord owns that controller's stick click.</summary>
    public bool StickTaken(bool left) => left ? outL : outR;

    /// <param name="blocked">Another chord (the Menu one) holds a stick: never start.</param>
    public BugChordEvent Update(bool stickL, bool stickR, bool blocked, float dt)
    {
        var e = BugChordEvent.None;
        bool both = stickL && stickR;
        if (!Active)
        {
            if (both && !blocked)
            {
                Active = true;
                Held = 0f;
                fired = false;
                takenL = takenR = true;
            }
        }
        else if (both)
        {
            Held += dt;
            if (!fired && Held >= RecenterSeconds + ReportSeconds)
            {
                fired = true;
                e = BugChordEvent.Report;
            }
        }
        else
            Active = false;
        // Each stick stays taken through the frame it reads released.
        outL = takenL;
        outR = takenR;
        if (!stickL)
            takenL = false;
        if (!stickR)
            takenR = false;
        return e;
    }
}
