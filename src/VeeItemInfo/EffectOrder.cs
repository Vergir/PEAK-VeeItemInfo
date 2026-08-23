using System.Collections.Generic;

using STATUSTYPE = CharacterAfflictions.STATUSTYPE;

namespace VeeItemInfo;

/// <summary>
/// When a change reaches the player. This is the first ordering key, and it is a property of
/// the effect rather than a bucket a branch chooses: an <see cref="Instant"/> line lands the
/// moment the item is used, an <see cref="OverTime"/> line unfolds afterwards.
///
/// It replaces the old primary/instant/timed lists, which every call site picked by hand and
/// which conflated three different ideas. "Primary" was really an importance rule - hunger
/// and stamina lead - and that now falls out of the status order instead. "Timed" was really
/// "arrived as an Affliction object", which put the instant Affliction_ClearAllStatus at the
/// bottom of the section while the identical Action_ClearAllStatus sat at the top.
///
/// An effect that lands when a timer runs out is not a third value. It is written onto the
/// line that starts the timer, with an arrow - Energy Drink's boost and the drowsiness it
/// hands back at the end are one statement, not two.
/// </summary>
internal enum Onset
{
    /// <summary>Applied the moment the action runs.</summary>
    Instant,

    /// <summary>Applied across a duration, or held for one.</summary>
    OverTime,
}

/// <summary>
/// One rendered line, carrying everything <see cref="EffectOrder"/> needs to place it.
/// Branches say what they mean - when it lands, which status it touches, which way it moves -
/// and never where it goes.
/// </summary>
internal readonly struct EffectLine
{
    internal EffectLine(string text, Onset onset, string status = "", float amount = 0f,
        int source = 0, bool clears = false)
    {
        Text = text;
        Onset = onset;
        Status = status;
        Amount = amount;
        Source = source;
        Clears = clears;
    }

    /// <summary>The finished rich-text line.</summary>
    internal string Text { get; }

    internal Onset Onset { get; }

    /// <summary>
    /// The status this line is about, keyed as <see cref="EffectColors"/> and
    /// <see cref="StatusIcons"/> key theirs. Empty where a line belongs to no one status -
    /// a Shroomberry's roll marker, a sunscreen's bare duration.
    /// </summary>
    internal string Status { get; }

    /// <summary>
    /// Signed, so that a removal can be told from an addition. Only the sign is read, and
    /// only to break a tie between two lines about the same status.
    /// </summary>
    internal float Amount { get; }

    /// <summary>
    /// Index of the component that produced the line. The last tiebreak, so that two
    /// otherwise identical lines never trade places between frames.
    /// </summary>
    internal int Source { get; }

    /// <summary>
    /// True for a clear-all line, which drives its status to zero rather than nudging it.
    /// That absoluteness is what lets <see cref="EffectOrder"/> drop the line when something
    /// else already takes the same status down.
    /// </summary>
    internal bool Clears { get; }

    internal EffectLine WithSource(int source) => new(Text, Onset, Status, Amount, source, Clears);
}

/// <summary>
/// The one place that decides what order effect lines read in.
///
/// Three keys, applied in turn:
///
/// 1. <see cref="Onset"/> - what you feel now before what unfolds later. This is the rule
///    that got Energy Drink right: it strips 100 Drowsy on drinking and hands 25 back when
///    the boost ends, and the other order says the opposite of what happens.
/// 2. Status, by the curated order below.
/// 3. Direction, removals before additions - Napberry clears your drowsiness and then puts
///    you to sleep, and that is the order it should read in.
/// 4. Source component, purely so the result is total and nothing shuffles frame to frame.
///
/// What is deliberately *not* a key is the order components sit on the prefab, which is what
/// used to decide everything inside a bucket. It is arbitrary - the Cactus keeps its
/// CactusBall behind a Rigidbody - and it made the overlay's ordering unreviewable, because
/// no rule was being followed for anyone to disagree with.
/// </summary>
internal static class EffectOrder
{
    /// <summary>
    /// Status order, curated for reading rather than taken from the game.
    ///
    /// The names come from <see cref="STATUSTYPE"/> rather than being spelled out again, so
    /// a member the game renames or drops breaks the build here instead of silently falling
    /// to the bottom of every list. Only the sequence is ours.
    ///
    /// Hunger and Extra Stamina lead because they are the two figures a player looks for
    /// first; that is the whole job the old "primary" bucket was doing. Poison sits beside
    /// Spores and Cold beside Hot because curing one so often touches the other. Petrify and
    /// Curse trail because they are costs rather than effects. The mod's own keys - Shield,
    /// Numb, Float - have no STATUSTYPE and are named as the rest of the mod names them.
    /// </summary>
    private static readonly string[] Order =
    {
        STATUSTYPE.Hunger.ToString(),
        "Extra Stamina",
        STATUSTYPE.Injury.ToString(),
        STATUSTYPE.Poison.ToString(),
        STATUSTYPE.Spores.ToString(),
        STATUSTYPE.Cold.ToString(),
        STATUSTYPE.Hot.ToString(),
        STATUSTYPE.Drowsy.ToString(),
        STATUSTYPE.Thorns.ToString(),
        STATUSTYPE.Web.ToString(),
        STATUSTYPE.FlyTrap.ToString(),
        STATUSTYPE.Crab.ToString(),
        STATUSTYPE.Arrow.ToString(),
        "Shield",
        "Numb",
        "Float",
        STATUSTYPE.Curse.ToString(),
        STATUSTYPE.Petrify.ToString(),
        STATUSTYPE.Weight.ToString(),
    };

    private static readonly Dictionary<string, int> Ranks = BuildRanks();

    private static Dictionary<string, int> BuildRanks()
    {
        Dictionary<string, int> ranks = new();
        for (int i = 0; i < Order.Length; i++)
        {
            ranks[Order[i]] = i;
        }

        return ranks;
    }

    /// <summary>
    /// Where a status sorts. Anything unranked - including the empty key a line with no
    /// status carries - trails the rest rather than throwing, the same forgiving rule
    /// <see cref="EffectColors.Get"/> follows.
    /// </summary>
    internal static int Rank(string status) =>
        Ranks.TryGetValue(status, out int rank) ? rank : Order.Length;

    internal static void Sort(List<EffectLine> lines)
    {
        DropRedundantClears(lines);
        lines.Sort(Compare);
    }

    /// <summary>
    /// Drops a clear-all's line for any status something else already takes down in the same
    /// breath. Napberry restores 100 hunger *and* clears all status, which is two lines both
    /// reading "-100 {hunger}" - the second says nothing the first did not.
    ///
    /// Only a clear is ever dropped, and only against another *removal*. Two ordinary
    /// removals of the same status are left alone because they genuinely stack: an item can
    /// carry two Action_GiveExtraStamina and hand out both. And a clear standing against an
    /// *addition* is kept deliberately - Napberry wipes your drowsiness and then puts you to
    /// sleep, and both halves of that are worth reading, removal first.
    /// </summary>
    private static void DropRedundantClears(List<EffectLine> lines)
    {
        HashSet<string> removedAnyway = new();
        foreach (EffectLine line in lines)
        {
            if (!line.Clears && line.Amount < 0f)
            {
                removedAnyway.Add(Key(line));
            }
        }

        if (removedAnyway.Count > 0)
        {
            lines.RemoveAll(line => line.Clears && removedAnyway.Contains(Key(line)));
        }
    }

    /// <summary>Identifies the one statement a line makes: this status, at this onset.</summary>
    private static string Key(EffectLine line) => line.Onset + "|" + line.Status;

    private static int Compare(EffectLine a, EffectLine b)
    {
        // Petrify last, under everything, whatever its onset. It is the one status that is a
        // price rather than an effect - the amulets charge it for what they just did - so it
        // reads as a footnote to the lines above it rather than as one of them. This is the
        // only override of the keys below, and it is deliberate: without it the healing
        // amulet's instant petrify cost sorted above the shield it buys.
        int byPetrify = IsPrice(a).CompareTo(IsPrice(b));
        if (byPetrify != 0)
        {
            return byPetrify;
        }

        int byOnset = a.Onset.CompareTo(b.Onset);
        if (byOnset != 0)
        {
            return byOnset;
        }

        int byStatus = Rank(a.Status).CompareTo(Rank(b.Status));
        if (byStatus != 0)
        {
            return byStatus;
        }

        // Removals first. This only ever decides between two lines about the same status,
        // where it is the difference between "it wipes your drowsiness, then makes you
        // drowsy" and a pair of lines that contradict each other.
        int byDirection = Direction(a.Amount).CompareTo(Direction(b.Amount));
        return byDirection != 0 ? byDirection : a.Source.CompareTo(b.Source);
    }

    private static int Direction(float amount) => amount < 0f ? 0 : 1;

    /// <summary>
    /// Whether a line is what the item costs rather than what it does. Only petrify, and
    /// only ever sorted after everything else.
    /// </summary>
    private static int IsPrice(EffectLine line) =>
        line.Status == STATUSTYPE.Petrify.ToString() ? 1 : 0;
}
