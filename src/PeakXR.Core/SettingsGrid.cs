using System;
using System.Collections.Generic;

namespace PeakXR.Core
{
    /// <summary>One page of the VR settings grid: item indices per column, top to bottom. A section header continued
    /// from an earlier column appears again at the top of the next.</summary>
    public sealed class GridPage
    {
        private readonly List<List<int>> columns = new List<List<int>>();

        public IReadOnlyList<IReadOnlyList<int>> Columns => columns;

        internal List<int> Add(int capacity)
        {
            var column = new List<int>(capacity);
            columns.Add(column);
            return column;
        }
    }

    /// <summary>
    /// Layout maths for the VR settings tab: a category rail beside a grid of the game's own row cells, paged so
    /// every page fits the settings window. In VR the window is a world panel with no screen edge to cut a long list
    /// off, so rows that do not fit would hang below it into the scene.
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

        /// <summary>Columns of at least <paramref name="minColumn"/> that fit <paramref name="width"/>, 1 to
        /// <paramref name="max"/>.</summary>
        public static int Columns(float width, float minColumn, float gap, int max) =>
            Math.Max(1, Math.Min(max, Fit(width, minColumn, gap)));

        public static float ColumnWidth(float width, int columns, float gap) =>
            columns <= 1 ? width : (width - gap * (columns - 1)) / columns;

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

        /// <summary>
        /// Splits a list into pages of <paramref name="columns"/> columns of <paramref name="rows"/> slots, filled
        /// column by column. A header never ends a column (it moves on with its first row), and a column that
        /// continues a section starts with that section's header again.
        /// </summary>
        public static IReadOnlyList<GridPage> Paginate(
            IReadOnlyList<bool> isHeader,
            int rows,
            int columns
        )
        {
            rows = Math.Max(1, rows);
            columns = Math.Max(1, columns);
            var pages = new List<GridPage>();
            GridPage page = null;
            List<int> column = null;
            int header = -1;

            void NextColumn()
            {
                if (page == null || page.Columns.Count == columns)
                {
                    page = new GridPage();
                    pages.Add(page);
                }
                column = page.Add(rows);
            }

            NextColumn();
            for (int i = 0; i < isHeader.Count; i++)
            {
                if (isHeader[i])
                {
                    header = i;
                    if (column.Count > 0 && column.Count + 2 > rows)
                        NextColumn();
                    column.Add(i);
                    continue;
                }
                if (column.Count >= rows)
                {
                    NextColumn();
                    if (header >= 0 && rows >= 2)
                        column.Add(header);
                }
                column.Add(i);
            }
            return pages;
        }

        /// <summary>Which side a row's preview opens on: left of the panel when a later column on the page holds rows
        /// (the right side is taken), otherwise right, into the free column or past the panel.</summary>
        public static bool OpensLeft(GridPage page, int column)
        {
            for (int c = column + 1; c < page.Columns.Count; c++)
                if (page.Columns[c].Count > 0)
                    return true;
            return false;
        }

        /// <summary>The longest stretch of [<paramref name="bottom"/>, <paramref name="top"/>] that no obstacle
        /// (<paramref name="lows"/>[i] to <paramref name="highs"/>[i]) covers; empty at <paramref name="top"/> when all
        /// of it is covered.</summary>
        public static void FreeSpan(
            float bottom,
            float top,
            IReadOnlyList<float> lows,
            IReadOnlyList<float> highs,
            out float lo,
            out float hi
        )
        {
            lo = hi = top;
            float best = -1f;
            float cursor = bottom;
            int n = Math.Min(lows.Count, highs.Count);
            var done = new bool[n];
            for (int pass = 0; pass < n; pass++)
            {
                // Next obstacle by its low end; n is a handful of buttons.
                int next = -1;
                for (int i = 0; i < n; i++)
                    if (!done[i] && (next < 0 || lows[i] < lows[next]))
                        next = i;
                done[next] = true;
                float a = Math.Max(bottom, lows[next]);
                float b = Math.Min(top, highs[next]);
                if (b <= a)
                    continue;
                if (a > cursor && a - cursor > best)
                {
                    best = a - cursor;
                    lo = cursor;
                    hi = a;
                }
                cursor = Math.Max(cursor, b);
            }
            if (top - cursor > best)
            {
                lo = cursor;
                hi = top;
            }
        }

        /// <summary>Category rail: one column of chips at up to <paramref name="maxPitch"/> each; split into up to three
        /// columns when that would make them thinner than <paramref name="minPitch"/>.</summary>
        public static void Rail(
            float height,
            int count,
            float maxPitch,
            float minPitch,
            out int columns,
            out float pitch
        )
        {
            count = Math.Max(1, count);
            columns = 1;
            pitch = Math.Min(maxPitch, height / count);
            while (pitch < minPitch && columns < 3)
            {
                columns++;
                int perColumn = (count + columns - 1) / columns;
                pitch = Math.Min(maxPitch, height / perColumn);
            }
        }
    }
}
