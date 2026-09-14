using System.Collections.Generic;

using STATUSTYPE = CharacterAfflictions.STATUSTYPE;

namespace VeeItemInfo;

/// <summary>
/// When a change reaches the player - the first ordering key, and a property of the effect
/// rather than a bucket a branch chooses. An effect that lands when a timer runs out is not a
/// third value: it is written onto the line that starts the timer, with an arrow.
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
/// The one place that decides what order effect lines read in: petrify last, then
/// <see cref="Onset"/>, then status by the curated order below, then the component the line
/// came from - which for two changes to one status is the order the game applies them. The
/// reasons for each key, and the fourth key that was dropped, are in docs/design.md,
/// "Ordering effects".
/// </summary>
internal static class EffectOrder
{
    /// <summary>
    /// Status order, curated for reading. The names come from <see cref="STATUSTYPE"/> rather
    /// than being spelled out, so a member the game renames or drops breaks the build here
    /// instead of silently sorting last. The mod's own keys - Shield, Numb, Float - have no
    /// STATUSTYPE and are named as the rest of the mod names them.
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
    /// Drops a clear-all's line for any status something else already removes at the same
    /// onset (Napberry: -100 hunger, and a clear-all). Only a clear is ever dropped, and only
    /// against a removal: two ordinary removals genuinely stack, and a clear against an
    /// addition is kept on purpose.
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
        // Petrify last, whatever its onset: it is a price rather than an effect, and the only
        // override of the keys below.
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
        // them, which is the order their components sit on the item. Not "removals first" -
        // that was a guess at this, and the Book of Bones proved it wrong.
        return a.Source.CompareTo(b.Source);
    }

    /// <summary>
    /// Whether a line is what the item costs rather than what it does. Only petrify, and
    /// only ever sorted after everything else.
    /// </summary>
    private static int IsPrice(EffectLine line) =>
        line.Status == STATUSTYPE.Petrify.ToString() ? 1 : 0;
}
