using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PeakXR.Core
{
    /// <summary>
    /// One JSON object per line for the two-client test logs (<c>-peakxr-mptest</c> and the MpObserver plugin).
    /// Unity's Mono has no JSON serializer we can rely on, and <c>tools/mp-check.sh</c> reads the lines with jq, so
    /// this writes flat objects only and reads back the few keys the observer needs from the VR side's lines.
    /// Shared as a source file by the observer, which must not reference PeakXR.
    /// </summary>
    public sealed class JsonLine
    {
        readonly StringBuilder sb = new StringBuilder("{");
        bool first = true;

        public JsonLine Str(string key, string value)
        {
            Key(key);
            if (value == null)
                sb.Append("null");
            else
                Escape(sb, value);
            return this;
        }

        public JsonLine Num(string key, double value)
        {
            Key(key);
            sb.Append(Format(value));
            return this;
        }

        public JsonLine Bool(string key, bool value)
        {
            Key(key);
            sb.Append(value ? "true" : "false");
            return this;
        }

        public JsonLine Vec(string key, float x, float y, float z)
        {
            Key(key);
            sb.Append('[')
                .Append(Format(x))
                .Append(',')
                .Append(Format(y))
                .Append(',')
                .Append(Format(z))
                .Append(']');
            return this;
        }

        /// <summary>An already serialized value (an array or a nested <see cref="JsonLine"/>).</summary>
        public JsonLine Raw(string key, string json)
        {
            Key(key);
            sb.Append(json ?? "null");
            return this;
        }

        public override string ToString() => sb.ToString() + "}";

        void Key(string key)
        {
            if (!first)
                sb.Append(',');
            first = false;
            Escape(sb, key);
            sb.Append(':');
        }

        static string Format(double v) =>
            double.IsNaN(v) || double.IsInfinity(v)
                ? "null"
                : v.ToString("0.###", CultureInfo.InvariantCulture);

        static void Escape(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char ch in s)
            {
                switch (ch)
                {
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        if (ch < ' ')
                            sb.Append("\\u")
                                .Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(ch);
                        break;
                }
            }
            sb.Append('"');
        }

        /// <summary>The string value of a top-level <paramref name="key"/> written by this class, or null.</summary>
        public static string ReadString(string line, string key)
        {
            int i = ValueStart(line, key);
            if (i < 0 || i >= line.Length || line[i] != '"')
                return null;
            var text = new StringBuilder();
            for (i++; i < line.Length; i++)
            {
                char ch = line[i];
                if (ch == '"')
                    return text.ToString();
                if (ch == '\\' && i + 1 < line.Length)
                {
                    char e = line[++i];
                    text.Append(
                        e switch
                        {
                            'n' => '\n',
                            'r' => '\r',
                            't' => '\t',
                            _ => e,
                        }
                    );
                }
                else
                    text.Append(ch);
            }
            return null;
        }

        public static bool TryReadNumber(string line, string key, out double value)
        {
            value = 0;
            int i = ValueStart(line, key);
            if (i < 0)
                return false;
            int end = i;
            while (
                end < line.Length && "+-.0123456789eE".Contains(line[end], StringComparison.Ordinal)
            )
                end++;
            return end > i
                && double.TryParse(
                    line.AsSpan(i, end - i),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value
                );
        }

        public static bool TryReadVec(
            string line,
            string key,
            out float x,
            out float y,
            out float z
        )
        {
            x = y = z = 0f;
            int i = ValueStart(line, key);
            if (i < 0 || i >= line.Length || line[i] != '[')
                return false;
            int end = line.IndexOf(']', i);
            if (end < 0)
                return false;
            var parts = line.Substring(i + 1, end - i - 1).Split(',');
            if (parts.Length != 3)
                return false;
            var f = new float[3];
            for (int k = 0; k < 3; k++)
            {
                if (
                    !float.TryParse(
                        parts[k],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out f[k]
                    )
                )
                    return false;
            }
            (x, y, z) = (f[0], f[1], f[2]);
            return true;
        }

        static int ValueStart(string line, string key)
        {
            if (line == null)
                return -1;
            var needle = "\"" + key + "\":";
            int i = line.IndexOf(needle, StringComparison.Ordinal);
            return i < 0 ? -1 : i + needle.Length;
        }
    }

    /// <summary>
    /// The directory both test clients share. Client B runs in Flatpak Steam, whose sandbox keeps its own home under
    /// <c>~/.var/app/com.valvesoftware.Steam</c> and mounts that directory at the same host path, while the rest of
    /// the real home is hidden; so a folder there is the one path both clients can read and write.
    /// </summary>
    public static class MpDir
    {
        public const string Flag = "-mpdir";
        public const string DefaultUnderHome = ".var/app/com.valvesoftware.Steam/peakxr-mp";

        /// <summary><c>-mpdir &lt;path&gt;</c>, else <c>$HOME/</c><see cref="DefaultUnderHome"/>, else <paramref name="fallback"/>.</summary>
        public static string Resolve(IList<string> args, string home, string fallback)
        {
            int i = args.IndexOf(Flag);
            if (i >= 0 && i + 1 < args.Count && !string.IsNullOrEmpty(args[i + 1]))
                return args[i + 1];
            return string.IsNullOrEmpty(home)
                ? fallback
                : home.TrimEnd('/') + "/" + DefaultUnderHome;
        }

        /// <summary>
        /// The Unix home: <c>$HOME</c> when it is a Unix path, else Wine's <c>WINEHOMEDIR</c> (<c>\??\Z:\home\u</c>),
        /// since Wine may hide <c>HOME</c> from Windows processes. Null when neither gives one.
        /// </summary>
        public static string UnixHome(string home, string wineHomeDir)
        {
            if (!string.IsNullOrEmpty(home) && home.StartsWith('/'))
                return home;
            const string Prefix = @"\??\Z:";
            if (
                wineHomeDir == null
                || !wineHomeDir.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            )
                return null;
            return wineHomeDir.Substring(Prefix.Length).Replace('\\', '/');
        }

        /// <summary>Under Proton a Unix path is reached through Wine's <c>Z:</c> drive, which maps <c>/</c>.</summary>
        public static string ToNative(string path, bool windows) =>
            windows && path.StartsWith('/') ? "Z:" + path.Replace('/', '\\') : path;
    }
}
