using System;

namespace PeakXR.Core;

/// <summary>
/// The real-life motion that starts a held item's primary use in VR. Only some have a gesture route of their own
/// (mouth, body, face); the rest name the system that already drives the item (aim, spool, lens, runner) so it is
/// kept off the wrong route. The trigger stays the fallback for every one except <see cref="Commune"/>.
/// </summary>
public enum UseGesture : byte
{
    /// <summary>No motion of its own: the trigger, or no primary use at all.</summary>
    Button,
    Eat,
    Drink,

    /// <summary>Pressed to your own chest or forearm (bandage, heat pack, chalk).</summary>
    Apply,

    /// <summary>Held open in front of your eyes for the cast.</summary>
    Read,

    /// <summary>Held eye to eye for the cast: an item that kills or warps you, so a bare trigger never starts it.</summary>
    Commune,

    /// <summary>Planted at the hand-aimed preview; never at the mouth.</summary>
    Build,
    Aim,
    Light,

    /// <summary>A bugle at the mouth (its own route, with the bell's pitch).</summary>
    Blow,

    /// <summary>A balloon blown up at the mouth.</summary>
    Inflate,
    Look,
    Open,
    Spool,
    Activate,

    /// <summary>Number of gestures; not a gesture.</summary>
    Count,
}

/// <summary>What an item's prefab carries, read once per item from its components.</summary>
[Flags]
public enum ItemParts : uint
{
    None = 0,

    /// <summary><c>Action_Consume</c> or <c>Action_ConsumeAndSpawn</c>.</summary>
    Consume = 1 << 0,

    /// <summary><c>Action_RestoreHunger</c>.</summary>
    Hunger = 1 << 1,

    /// <summary>Heals or sets a status on the user: a negative <c>Action_ModifyStatus</c>,
    /// <c>Action_ClearAllStatus</c>, <c>Action_ApplyAffliction</c>, <c>Action_AddOrRemoveThorns</c>.</summary>
    SelfCare = 1 << 2,
    Construct = 1 << 3,

    /// <summary>Rope or vine shooter, rescue claw, dart, antizooka.</summary>
    Shooter = 1 << 4,

    /// <summary>Lantern, torch, candle or flare.</summary>
    Light = 1 << 5,
    Bugle = 1 << 6,
    Balloon = 1 << 7,
    Binoculars = 1 << 8,
    Parasol = 1 << 9,
    Spool = 1 << 10,

    /// <summary>Guidebook or passport, which have their own book in VR.</summary>
    Book = 1 << 11,

    /// <summary><c>Action_Die</c>.</summary>
    Lethal = 1 << 12,
    Warp = 1 << 13,

    /// <summary>Strange gem or amulet (<c>AmuletBase</c>).</summary>
    Gem = 1 << 14,

    /// <summary>At least one item action fires on a press, hold, cast or consume; an item whose actions never fire
    /// does nothing whatever its prompt says.</summary>
    Acts = 1 << 15,
}

/// <summary>The game data a classification reads: <c>UIData.hasMainInteract</c> and <c>mainInteractPrompt</c>,
/// <c>ItemUseFeedback.useAnimation</c> and the components.</summary>
public readonly record struct ItemUseFacts(
    bool HasMain,
    string Prompt,
    string Animation,
    ItemParts Parts
);

/// <summary>
/// Picks each item's real-life use gesture from the game's own data rather than item names, so items from future
/// game versions land on a sensible route: the item's own system first (build, aim, light...), then the use
/// animation (Heal, Drink, Eat), then the prompt verb, then the actions (self-care is applied, food is eaten).
/// </summary>
public static class ItemUse
{
    public static UseGesture Classify(ItemUseFacts f)
    {
        if (!f.HasMain)
            return UseGesture.Button;
        var parts = f.Parts;
        string prompt = Norm(f.Prompt);
        string anim = Norm(f.Animation);

        if (Has(parts, ItemParts.Lethal) || prompt == "commune")
            return UseGesture.Commune;
        // Constructables carry Action_Consume: checked before any food rule.
        if (Has(parts, ItemParts.Construct))
            return UseGesture.Build;
        if (Has(parts, ItemParts.Shooter))
            return UseGesture.Aim;
        if (Has(parts, ItemParts.Spool))
            return UseGesture.Spool;
        if (Has(parts, ItemParts.Binoculars))
            return UseGesture.Look;
        if (Has(parts, ItemParts.Bugle))
            return UseGesture.Blow;
        if (Has(parts, ItemParts.Light))
            return UseGesture.Light;
        if (Has(parts, ItemParts.Parasol | ItemParts.Book))
            return UseGesture.Open;
        if (Has(parts, ItemParts.Balloon))
            return UseGesture.Inflate;
        if (Has(parts, ItemParts.Warp | ItemParts.Gem))
            return UseGesture.Activate;

        if (anim == "heal" || prompt == "spray")
            return UseGesture.Apply;
        if (anim == "drink" || prompt == "drink")
            return UseGesture.Drink;
        bool hunger = Has(parts, ItemParts.Hunger);
        if (prompt is "eat" or "lick" || (anim == "eat" && hunger))
            return UseGesture.Eat;
        if (prompt == "read" && Has(parts, ItemParts.Acts))
            return UseGesture.Read;
        if (prompt.StartsWith("open", StringComparison.Ordinal))
            return UseGesture.Open;
        if (Has(parts, ItemParts.SelfCare) && !hunger)
            return UseGesture.Apply;
        if (hunger || Has(parts, ItemParts.Consume))
            return UseGesture.Eat;
        return UseGesture.Button;
    }

    /// <summary>Raised to the mouth: eating, drinking, blowing up a balloon. Not the bugle, which has its own route.</summary>
    public static bool AtMouth(UseGesture g) =>
        g is UseGesture.Eat or UseGesture.Drink or UseGesture.Inflate;

    /// <summary>The bare trigger is refused: only the gesture (held for the game's own cast) starts the use.</summary>
    public static bool NeedsGesture(UseGesture g) => g == UseGesture.Commune;

    private static bool Has(ItemParts parts, ItemParts any) => (parts & any) != 0;

    private static string Norm(string s) =>
        string.IsNullOrWhiteSpace(s) ? "" : s.Trim().ToLowerInvariant();
}

/// <summary>
/// One end-stop thunk when a gesture pushes a clamped value (spool length, zoom, cannon angle) into its limit; no
/// repeat while it keeps pushing, so the knob feels dead there. Stopping or pushing the other way re-arms it.
/// </summary>
public sealed class LimitStop
{
    public float Epsilon = 1e-3f;

    private bool held;

    /// <summary>Pushing into the limit right now: the gesture's own step tick stays quiet (a dead knob).</summary>
    public bool Pressing => held;

    /// <param name="push">Direction the gesture pushes this frame: -1 toward <paramref name="min"/>, +1 toward
    /// <paramref name="max"/>, 0 none.</param>
    public bool Update(float value, float min, float max, int push)
    {
        bool pressing =
            (push < 0 && value <= min + Epsilon) || (push > 0 && value >= max - Epsilon);
        bool fire = pressing && !held;
        held = pressing;
        return fire;
    }

    public void Reset() => held = false;
}

/// <summary>Whether one more cook harms an item (ItemCooking.cs:174-257): wrecked at once, exploded by an additional
/// behaviour, or, with the default behaviour, burnt past level 2 (less hunger, no stamina bonus, then poison).</summary>
public static class CookRisk
{
    /// <param name="cooked">Current cook level.</param>
    /// <param name="wreck"><c>wreckWhenCooked</c>.</param>
    /// <param name="explodeAt">Lowest <c>cookedAmountToTrigger</c> of an explode behaviour, 0 when none.</param>
    /// <param name="defaults">The default stat changes apply (<c>!ignoreDefaultCookBehavior</c>).</param>
    public static bool NextHarms(int cooked, bool wreck, int explodeAt, bool defaults)
    {
        int next = cooked + 1;
        return wreck || (explodeAt > 0 && next >= explodeAt) || (defaults && next > 2);
    }
}
