using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PeakXR.Core
{
    /// <summary>How a config entry is shown in the generated VR settings tab.</summary>
    public enum RowKind
    {
        /// <summary>bool: Off/On dropdown.</summary>
        Toggle,

        /// <summary>Enum or a list of acceptable values: dropdown.</summary>
        Choice,

        /// <summary>Number with an acceptable range: slider.</summary>
        Slider,

        /// <summary>Anything else the config can parse from text: text field.</summary>
        Text,
        Unsupported,
    }

    /// <summary>Pure rules behind the generated settings rows, kept out of Unity code so they are tested.</summary>
    public static class SettingsRows
    {
        static readonly char[] Separators = { '_', ' ', '-' };

        public static RowKind Classify(Type valueType, bool hasRange, bool hasList, bool canConvert)
        {
            if (valueType == typeof(bool))
                return RowKind.Toggle;
            if (hasList)
                return RowKind.Choice;
            if (valueType.IsEnum)
            {
                if (!valueType.IsDefined(typeof(FlagsAttribute), false))
                    return RowKind.Choice;
                return canConvert ? RowKind.Text : RowKind.Unsupported;
            }
            if (hasRange && IsNumeric(valueType))
                return RowKind.Slider;
            return canConvert ? RowKind.Text : RowKind.Unsupported;
        }

        public static bool IsIntegral(Type t) =>
            t == typeof(int)
            || t == typeof(long)
            || t == typeof(short)
            || t == typeof(byte)
            || t == typeof(uint)
            || t == typeof(ulong)
            || t == typeof(ushort)
            || t == typeof(sbyte);

        static bool IsNumeric(Type t) =>
            IsIntegral(t) || t == typeof(float) || t == typeof(double) || t == typeof(decimal);

        /// <summary>"SnapTurnAngle" becomes "Snap turn angle"; acronyms such as HUD or VR stay upper case.</summary>
        public static string Prettify(string key)
        {
            var words = new List<string>();
            foreach (var part in key.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
                SplitWords(part, words);

            var sb = new StringBuilder();
            for (int i = 0; i < words.Count; i++)
            {
                string w = words[i];
                bool acronym =
                    w.Length > 1
                    && string.Equals(w.ToUpperInvariant(), w, StringComparison.Ordinal)
                    && char.IsLetter(w[0]);
                if (i > 0)
                    sb.Append(' ');
                if (acronym)
                    sb.Append(w);
                else if (i == 0)
                    sb.Append(char.ToUpperInvariant(w[0]))
                        .Append(w.Substring(1).ToLowerInvariant());
                else
                    sb.Append(w.ToLowerInvariant());
            }
            return sb.ToString();
        }

        static void SplitWords(string s, List<string> words)
        {
            int start = 0;
            for (int i = 1; i < s.Length; i++)
            {
                char prev = s[i - 1],
                    c = s[i];
                bool boundary =
                    (char.IsLower(prev) && char.IsUpper(c))
                    || (
                        char.IsUpper(prev)
                        && char.IsUpper(c)
                        && i + 1 < s.Length
                        && char.IsLower(s[i + 1])
                    )
                    || (char.IsLetter(prev) && char.IsDigit(c))
                    || (char.IsDigit(prev) && char.IsLetter(c));
                if (boundary)
                {
                    words.Add(s.Substring(start, i - start));
                    start = i;
                }
            }
            if (start < s.Length)
                words.Add(s.Substring(start));
        }

        /// <summary>Pages a list of <paramref name="rows"/> takes at <paramref name="pageRows"/> each; at least one.
        /// In VR a world panel has no screen edge to cut a long tab off, so it is shown a page at a time.</summary>
        public static int PageCount(int rows, int pageRows) =>
            rows <= 0 ? 1 : (rows + pageRows - 1) / pageRows;

        /// <summary><paramref name="page"/> limited to the pages that exist (a list that shrank keeps its last page).</summary>
        public static int ClampPage(int page, int rows, int pageRows) =>
            Math.Max(0, Math.Min(page, PageCount(rows, pageRows) - 1));

        /// <summary>First row and row count of <paramref name="page"/>, clamped.</summary>
        public static void PageSlice(int rows, int pageRows, int page, out int start, out int count)
        {
            start = ClampPage(page, rows, pageRows) * pageRows;
            count = Math.Max(0, Math.Min(pageRows, rows - start));
        }

        /// <summary>Slider label: whole numbers for integers and wide ranges, more decimals for narrow ones.</summary>
        public static string Format(float value, bool integral, float min, float max)
        {
            float span = MathF.Abs(max - min);
            string format =
                integral || span >= 100f ? "0"
                : span >= 10f ? "0.0"
                : "0.00";
            return value.ToString(format, CultureInfo.InvariantCulture);
        }
    }
}
