using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PeakXR.Core
{
    /// <summary>
    /// Results of the unattended in-game self-test (<c>-peakxr-selftest</c>). Nobody watches that run, so each
    /// step keeps its outcome, duration and the first log line it caused; the plugin writes <see cref="Line"/>
    /// per step to a file the orchestrator reads afterwards. A logged exception fails the step; a logged error
    /// is only reported, because the game logs harmless errors.
    /// </summary>
    public sealed class SelfTestReport
    {
        public sealed class StepResult
        {
            public string Id;
            public bool Passed;
            public bool Skipped;
            public float Seconds;
            public string Note;
            public string FirstLog;
            internal bool SawException;
        }

        readonly List<StepResult> steps = new List<StepResult>();
        StepResult current;
        float startTime;

        public IReadOnlyList<StepResult> Steps => steps;

        public int Passed { get; private set; }

        public int Failed { get; private set; }

        public void Begin(string id, float time)
        {
            current = new StepResult { Id = id };
            startTime = time;
        }

        /// <summary>Attribute a logged exception to the running step; it fails the step.</summary>
        public void Exception(string message)
        {
            if (current == null)
                return;
            current.SawException = true;
            Keep(message);
        }

        /// <summary>Attribute a logged error to the running step; reported only.</summary>
        public void Error(string message)
        {
            if (current != null)
                Keep(message);
        }

        void Keep(string message)
        {
            if (current.FirstLog != null || string.IsNullOrEmpty(message))
                return;
            int nl = message.IndexOf('\n');
            current.FirstLog = (nl >= 0 ? message.Substring(0, nl) : message).Trim();
        }

        public void End(bool passed, float time, string note)
        {
            if (current == null)
                return;
            current.Passed = passed && !current.SawException;
            current.Seconds = time - startTime;
            current.Note = note;
            if (current.Passed)
                Passed++;
            else
                Failed++;
            steps.Add(current);
            current = null;
        }

        /// <summary>Record a step that was not run (no standalone spawn path); counts as neither pass nor fail.</summary>
        public void Skip(string id, string note)
        {
            current = null;
            steps.Add(
                new StepResult
                {
                    Id = id,
                    Skipped = true,
                    Note = note,
                }
            );
        }

        /// <summary><c>id PASS|FAIL|SKIP 1.23s [note] [| first log line]</c>.</summary>
        public string Line(int index)
        {
            var s = steps[index];
            var sb = new StringBuilder(s.Id)
                .Append(
                    s.Skipped ? " SKIP "
                    : s.Passed ? " PASS "
                    : " FAIL "
                )
                .Append(s.Seconds.ToString("0.00", CultureInfo.InvariantCulture))
                .Append('s');
            if (!string.IsNullOrEmpty(s.Note))
                sb.Append(' ').Append(s.Note);
            if (s.FirstLog != null)
                sb.Append(" | ").Append(s.FirstLog);
            return sb.ToString();
        }

        /// <summary>A file-name-safe form of a step or prefab name (letters, digits, '-' and '_').</summary>
        public static string SafeName(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            return sb.ToString();
        }
    }
}
