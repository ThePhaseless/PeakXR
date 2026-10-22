using System;
using System.Text;

namespace PeakXR.Core
{
    /// <summary>How many hands an item wants.</summary>
    public enum Handedness
    {
        /// <summary>Held and used with either hand; the other hand stays free.</summary>
        OneHanded,

        /// <summary>Carried in one hand, but only usable while the second hand also grips it (long or heavy items).</summary>
        TwoHanded,

        /// <summary>Usable one-handed; a second hand steadies and aims it.</summary>
        OptionalTwoHand,
    }

    /// <summary>Finger shape a hand takes around a held item.</summary>
    public enum GripPreset
    {
        Cylinder,
        Pinch,
        FlatPalm,
        TwoHandPole,
    }

    /// <summary>Facts about an item gathered from its components and size, so unknown (future) items still get a
    /// sensible hold profile.</summary>
    [Flags]
    public enum ItemTraits
    {
        None = 0,
        RightHandOnly = 1,
        Food = 2,
        Shooter = 4,

        /// <summary>Longest side of 0.9 m or more.</summary>
        Elongated = 8,

        /// <summary>Longest side under 0.15 m.</summary>
        Small = 16,

        /// <summary>Thin slab: a book, map or similar.</summary>
        Flat = 32,
        Backpack = 64,
    }

    public readonly struct HoldProfile : IEquatable<HoldProfile>
    {
        public readonly Handedness Handedness;
        public readonly GripPreset Grip;

        public HoldProfile(Handedness handedness, GripPreset grip)
        {
            Handedness = handedness;
            Grip = grip;
        }

        public bool Equals(HoldProfile other) =>
            Handedness == other.Handedness && Grip == other.Grip;

        public override bool Equals(object obj) => obj is HoldProfile p && Equals(p);

        public override int GetHashCode() => ((int)Handedness * 16) + (int)Grip;

        public override string ToString() => $"{Handedness}/{Grip}";

        public static bool operator ==(HoldProfile a, HoldProfile b) => a.Equals(b);

        public static bool operator !=(HoldProfile a, HoldProfile b) => !a.Equals(b);
    }

    /// <summary>
    /// Per-item handedness and grip. Known items are listed by name; anything else is inferred from
    /// <see cref="ItemTraits"/>, so items added by later game versions still behave.
    /// </summary>
    public static class ItemHoldTable
    {
        // Keys are normalized names (lowercase letters and digits only) matched as substrings, first match wins.
        static readonly (string Key, HoldProfile Profile)[] Known =
        {
            ("antiropecannon", new HoldProfile(Handedness.OptionalTwoHand, GripPreset.Cylinder)),
            ("ropecannon", new HoldProfile(Handedness.OptionalTwoHand, GripPreset.Cylinder)),
            ("ropeshooter", new HoldProfile(Handedness.OptionalTwoHand, GripPreset.Cylinder)),
            ("chainshooter", new HoldProfile(Handedness.OptionalTwoHand, GripPreset.Cylinder)),
            ("chainlauncher", new HoldProfile(Handedness.OptionalTwoHand, GripPreset.Cylinder)),
            ("vineshooter", new HoldProfile(Handedness.OptionalTwoHand, GripPreset.Cylinder)),
            ("antizooka", new HoldProfile(Handedness.TwoHanded, GripPreset.TwoHandPole)),
            ("blowgun", new HoldProfile(Handedness.OptionalTwoHand, GripPreset.Cylinder)),
            ("ropespool", new HoldProfile(Handedness.OptionalTwoHand, GripPreset.Cylinder)),
            ("rescuehook", new HoldProfile(Handedness.TwoHanded, GripPreset.TwoHandPole)),
            ("rescueclaw", new HoldProfile(Handedness.TwoHanded, GripPreset.TwoHandPole)),
            ("backpack", new HoldProfile(Handedness.OptionalTwoHand, GripPreset.FlatPalm)),
            ("guidebook", new HoldProfile(Handedness.OptionalTwoHand, GripPreset.FlatPalm)),
            ("book", new HoldProfile(Handedness.OptionalTwoHand, GripPreset.FlatPalm)),
            ("passport", new HoldProfile(Handedness.OneHanded, GripPreset.FlatPalm)),
            ("binocular", new HoldProfile(Handedness.OptionalTwoHand, GripPreset.Cylinder)),
            // A flag pole is planted with one hand; a second steadies it (inferred long items need two).
            ("checkpoint", new HoldProfile(Handedness.OptionalTwoHand, GripPreset.TwoHandPole)),
            ("compass", new HoldProfile(Handedness.OneHanded, GripPreset.FlatPalm)),
            ("firstaid", new HoldProfile(Handedness.OptionalTwoHand, GripPreset.FlatPalm)),
            ("medkit", new HoldProfile(Handedness.OptionalTwoHand, GripPreset.FlatPalm)),
            ("bandage", new HoldProfile(Handedness.OneHanded, GripPreset.Pinch)),
            ("parasol", new HoldProfile(Handedness.OneHanded, GripPreset.Cylinder)),
            ("torch", new HoldProfile(Handedness.OneHanded, GripPreset.Cylinder)),
            ("lantern", new HoldProfile(Handedness.OneHanded, GripPreset.Cylinder)),
            ("flare", new HoldProfile(Handedness.OneHanded, GripPreset.Cylinder)),
            ("bugle", new HoldProfile(Handedness.OneHanded, GripPreset.Cylinder)),
            ("piton", new HoldProfile(Handedness.OneHanded, GripPreset.Cylinder)),
            ("climbingspike", new HoldProfile(Handedness.OneHanded, GripPreset.Cylinder)),
            ("dynamite", new HoldProfile(Handedness.OneHanded, GripPreset.Cylinder)),
            ("balloon", new HoldProfile(Handedness.OneHanded, GripPreset.Pinch)),
            ("berry", new HoldProfile(Handedness.OneHanded, GripPreset.Pinch)),
            ("mushroom", new HoldProfile(Handedness.OneHanded, GripPreset.Pinch)),
            ("shroom", new HoldProfile(Handedness.OneHanded, GripPreset.Pinch)),
            ("marshmallow", new HoldProfile(Handedness.OneHanded, GripPreset.Pinch)),
            ("pill", new HoldProfile(Handedness.OneHanded, GripPreset.Pinch)),
            ("egg", new HoldProfile(Handedness.OneHanded, GripPreset.Pinch)),
            ("coconut", new HoldProfile(Handedness.OptionalTwoHand, GripPreset.FlatPalm)),
            ("conch", new HoldProfile(Handedness.OptionalTwoHand, GripPreset.FlatPalm)),
            ("bingbong", new HoldProfile(Handedness.OneHanded, GripPreset.Cylinder)),
            ("frisbee", new HoldProfile(Handedness.OneHanded, GripPreset.FlatPalm)),
        };

        /// <summary>Profile for an item: the name table first, then <see cref="Infer"/>.</summary>
        public static HoldProfile Classify(string itemName, ItemTraits traits)
        {
            string key = Normalize(itemName);
            if (key.Length > 0)
            {
                for (int i = 0; i < Known.Length; i++)
                {
                    if (key.Contains(Known[i].Key))
                        return Known[i].Profile;
                }
            }
            return Infer(traits);
        }

        /// <summary>Default profile from component and size facts alone.</summary>
        public static HoldProfile Infer(ItemTraits traits)
        {
            bool has(ItemTraits t) => (traits & t) != 0;

            if (has(ItemTraits.Elongated))
                return new HoldProfile(Handedness.TwoHanded, GripPreset.TwoHandPole);
            if (has(ItemTraits.Backpack))
                return new HoldProfile(Handedness.OptionalTwoHand, GripPreset.FlatPalm);
            if (has(ItemTraits.Shooter))
                return new HoldProfile(Handedness.OptionalTwoHand, GripPreset.Cylinder);

            var grip =
                has(ItemTraits.Flat) ? GripPreset.FlatPalm
                : has(ItemTraits.Small) ? GripPreset.Pinch
                : GripPreset.Cylinder;

            if (has(ItemTraits.RightHandOnly) || has(ItemTraits.Food) || has(ItemTraits.Small))
                return new HoldProfile(Handedness.OneHanded, grip);
            // Vanilla holds everything else with both hands, so a second hand is welcome but not required.
            return new HoldProfile(Handedness.OptionalTwoHand, grip);
        }

        /// <summary>Whether primary/secondary use is allowed right now.</summary>
        public static bool CanUse(Handedness handedness, bool secondHandOnItem) =>
            handedness != Handedness.TwoHanded || secondHandOnItem;

        /// <summary>Whether the free hand gripping the held item becomes a two-hand hold.</summary>
        public static bool AcceptsSecondHand(Handedness handedness) =>
            handedness != Handedness.OneHanded;

        /// <summary>Lowercase letters and digits only, with a Unity "(Clone)" suffix removed.</summary>
        public static string Normalize(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "";
            var sb = new StringBuilder(name.Length);
            foreach (char ch in name)
            {
                if (char.IsLetterOrDigit(ch))
                    sb.Append(char.ToLowerInvariant(ch));
            }
            string s = sb.ToString();
            return s.EndsWith("clone", StringComparison.Ordinal) ? s.Substring(0, s.Length - 5) : s;
        }
    }
}
