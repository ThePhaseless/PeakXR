using System.Text;

namespace PeakXR.Core
{
    public enum KeyKind
    {
        Character,
        Shift,
        Backspace,
        Space,
        Done,
        Cancel,
    }

    /// <summary>One key of the in-headset keyboard. Labels are built once so redrawing never allocates.</summary>
    public sealed class KeyDef
    {
        readonly string _lowerLabel;
        readonly string _upperLabel;

        public readonly KeyKind Kind;
        public readonly char Lower;
        public readonly char Upper;

        /// <summary>Width in key units (a letter key is 1).</summary>
        public readonly float Width;

        KeyDef(KeyKind kind, char lower, char upper, string label, float width)
        {
            Kind = kind;
            Lower = lower;
            Upper = upper;
            Width = width;
            _lowerLabel = label ?? lower.ToString();
            _upperLabel = label ?? upper.ToString();
        }

        public static KeyDef Letter(char lower, char upper) =>
            new KeyDef(KeyKind.Character, lower, upper, null, 1f);

        public static KeyDef Special(KeyKind kind, string label, float width) =>
            new KeyDef(kind, '\0', '\0', label, width);

        public char Output(bool shift) => shift ? Upper : Lower;

        public string Label(bool shift) => shift ? _upperLabel : _lowerLabel;
    }

    /// <summary>QWERTY layout for room codes, report reasons and other short game text fields.</summary>
    public static class KeyboardLayout
    {
        public static readonly KeyDef[][] Rows = Build();

        static KeyDef[][] Build()
        {
            return new[]
            {
                Row("1234567890", "!@#$%^&*()", KeyDef.Special(KeyKind.Backspace, "Back", 1.6f)),
                Row("qwertyuiop", "QWERTYUIOP", null),
                Row("asdfghjkl'", "ASDFGHJKL\"", null),
                Concat(
                    KeyDef.Special(KeyKind.Shift, "Shift", 1.6f),
                    Row("zxcvbnm,.-", "ZXCVBNM?:_", null)
                ),
                new[]
                {
                    KeyDef.Special(KeyKind.Cancel, "Close", 2f),
                    KeyDef.Special(KeyKind.Space, "Space", 5f),
                    KeyDef.Special(KeyKind.Done, "Done", 2f),
                },
            };
        }

        static KeyDef[] Row(string lower, string upper, KeyDef tail)
        {
            var keys = new KeyDef[lower.Length + (tail != null ? 1 : 0)];
            for (int i = 0; i < lower.Length; i++)
                keys[i] = KeyDef.Letter(lower[i], upper[i]);
            if (tail != null)
                keys[lower.Length] = tail;
            return keys;
        }

        static KeyDef[] Concat(KeyDef head, KeyDef[] rest)
        {
            var keys = new KeyDef[rest.Length + 1];
            keys[0] = head;
            rest.CopyTo(keys, 1);
            return keys;
        }
    }

    /// <summary>The keyboard's input line: what has been typed (masked for passwords) and a caret, so an empty
    /// field still shows where the text goes.</summary>
    public static class InputLine
    {
        public const char Caret = '|';

        public static string Show(string text, bool masked)
        {
            if (string.IsNullOrEmpty(text))
                return "|";
            return (masked ? new string('*', text.Length) : text) + Caret;
        }
    }

    /// <summary>Text typed into a platform text dialog that has no game input field behind it.</summary>
    public sealed class TextEntry
    {
        readonly StringBuilder _text = new StringBuilder();
        string _cached = string.Empty;

        /// <summary>Maximum length; 0 or less means unlimited.</summary>
        public int Limit;

        public string Text => _cached ??= _text.ToString();

        public bool Insert(char c)
        {
            if (Limit > 0 && _text.Length >= Limit)
                return false;
            _text.Append(c);
            _cached = null;
            return true;
        }

        public bool Backspace()
        {
            if (_text.Length == 0)
                return false;
            _text.Length--;
            _cached = null;
            return true;
        }

        public void Clear()
        {
            _text.Clear();
            _cached = string.Empty;
        }
    }

    /// <summary>
    /// Press detection for poking a flat panel with a fingertip. The finger must first be at least
    /// <see cref="ArmDistance"/> in front of the panel, then reach <see cref="PressDepth"/> over it; one press
    /// per approach, so resting a finger on the panel or reaching through it from behind never repeats.
    /// </summary>
    public sealed class PokeButton
    {
        public float PressDepth = 0.01f;
        public float ArmDistance = 0.025f;
        public float BehindLimit = 0.04f;

        bool _armed;

        /// <param name="frontDistance">Fingertip distance in front of the panel in metres (negative behind it).</param>
        /// <param name="inside">Whether the fingertip is over the panel area.</param>
        /// <returns>True on the frame of a press.</returns>
        public bool Update(float frontDistance, bool inside)
        {
            if (frontDistance >= ArmDistance)
            {
                _armed = true;
                return false;
            }
            if (frontDistance < -BehindLimit)
            {
                _armed = false;
                return false;
            }
            if (_armed && inside && frontDistance <= PressDepth)
            {
                _armed = false;
                return true;
            }
            return false;
        }

        public void Reset() => _armed = false;
    }
}
