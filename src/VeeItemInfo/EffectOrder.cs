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
/// 3. The component the line came from, which for two changes to one status is the order the
///    game applies them - ItemAction.Subscribe appends to a delegate and OnEnable runs down
///    the component list. It also makes the order total, so nothing shuffles frame to frame.
///
/// **Component order is a key only here, and only because here it is the arithmetic.** It is
/// arbitrary as a general rule - the Cactus keeps its CactusBall behind a Rigidbody - and it
/// used to decide everything inside a bucket, which made the ordering unreviewable because no
/// rule was being followed for anyone to disagree with. Between two changes to the *same
/// status at the same moment* it is not decoration: the game runs them in that sequence, and
/// with a status clamped at zero the sequence changes the answer.
///
/// That replaced a fourth key, "removals before additions", which guessed at the same thing.
/// The Book of Bones carries Curse +50 and Curse -25 in that order and nets +25 from any
/// starting point; sorting the removal first said -25 then +50, which reads as +50 to anyone
/// with no curse. The rule had been written for Napberry - wipe your drowsiness, then make you
/// drowsy - and there the clear-all sits before the drowsy addition anyway, so the game's own
/// order gives the same answer. Redundant where it was right, wrong where it was not.
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

        // Where each line started is the last tiebreak, because Source is not the total key
        // it was taken for: one component can add several lines, and List.Sort is unstable,
        // so two lines from the same branch are free to swap between frames. Dynamite is the
        // case that made it visible - a held-injury line and a blast line, both instant, both
        // Injury, both additions, both from the Dynamite component.
        List<(EffectLine Line, int Index)> indexed = new(lines.Count);
        for (int i = 0; i < lines.Count; i++)
        {
            indexed.Add((lines[i], i));
        }

        indexed.Sort((a, b) =>
        {
            int ordered = Compare(a.Line, b.Line);
            return ordered != 0 ? ordered : a.Index.CompareTo(b.Index);
        });

        for (int i = 0; i < lines.Count; i++)
        {
            lines[i] = indexed[i].Line;
        }
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

        // Two changes to the same status at the same moment read in the order the game applies
        // them, which is the order their components sit on the item: ItemAction.Subscribe
        // appends to a delegate, and OnEnable runs down the component list.
        //
        // This used to be "removals first", which is a guess at something readable. The Book
        // of Bones carries Curse +50 and Curse -25 in that order and nets +25 from any
        // starting point; sorting the removal first said -25 then +50, which reads as +50 to
        // anyone with no curse, because a removal at zero does nothing. The rule was written
        // for Napberry - wipe your drowsiness, then make you drowsy - and there the clear-all
        // sits before the drowsy addition anyway, so the game's own order already gives the
        // same answer. It was redundant where it was right and wrong where it was not.
        return a.Source.CompareTo(b.Source);
    }

    /// <summary>
    /// Whether a line is what the item costs rather than what it does. Only petrify, and
    /// only ever sorted after everything else.
    /// </summary>
    private static int IsPrice(EffectLine line) =>
        line.Status == STATUSTYPE.Petrify.ToString() ? 1 : 0;
}
