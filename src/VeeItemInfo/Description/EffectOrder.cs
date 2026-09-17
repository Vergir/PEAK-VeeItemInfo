using System.Collections.Generic;

using STATUSTYPE = CharacterAfflictions.STATUSTYPE;

namespace VeeItemInfo;

/// <summary>
/// When a change reaches the player. An effect that lands when a timer runs out is not a
/// third value: it is written onto the line that starts the timer, with an arrow.
/// </summary>
internal enum Onset
{
    Instant,
    OverTime,
}

/// <summary>One rendered line, with the keys <see cref="EffectOrder"/> places it by.</summary>
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

    internal string Text { get; }

    internal Onset Onset { get; }

    /// <summary>Keyed as <see cref="EffectColors"/> keys; empty for a line about no one status.</summary>
    internal string Status { get; }

    /// <summary>Only the sign is read, to tell a removal from an addition.</summary>
    internal float Amount { get; }

    /// <summary>Index of the component that produced the line.</summary>
    internal int Source { get; }

    /// <summary>A clear-all line, which can be dropped when something else removes the same status.</summary>
    internal bool Clears { get; }

    internal EffectLine WithSource(int source) => new(Text, Onset, Status, Amount, source, Clears);
}

/// <summary>
/// The one place that decides what order effect lines read in: petrify last, then
/// <see cref="Onset"/>, then status by the curated order below, then the component the line
/// came from. The reasons are in docs/design.md, "Ordering effects".
/// </summary>
internal static class EffectOrder
{
    /// <summary>
    /// Names come from <see cref="STATUSTYPE"/> rather than being spelled out, so a member
    /// the game renames breaks the build instead of silently sorting last.
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

    /// <summary>Anything unranked, including the empty key, trails the rest rather than throwing.</summary>
    internal static int Rank(string status) =>
        Ranks.TryGetValue(status, out int rank) ? rank : Order.Length;

    internal static void Sort(List<EffectLine> lines)
    {
        DropRedundantClears(lines);

        // Original position is the last tiebreak: one component can add several lines, and
        // List.Sort is unstable.
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

        MergeRepeats(lines);
    }

    /// <summary>
    /// Adds up neighbouring lines that say the same thing about one status, so a cooked Sports
    /// Drink reads "+45" rather than "+30" above "+15". Run after the sort, where two such
    /// lines are already adjacent.
    /// </summary>
    private static void MergeRepeats(List<EffectLine> lines)
    {
        for (int i = lines.Count - 2; i >= 0; i--)
        {
            if (!CanMerge(lines[i], lines[i + 1]))
            {
                continue;
            }

            EffectLine first = lines[i];
            float total = first.Amount + lines[i + 1].Amount;
            lines[i] = new EffectLine(EffectFormatter.Token(total, first.Status),
                first.Onset, first.Status, total, first.Source);
            lines.RemoveAt(i + 1);
        }
    }

    /// <summary>
    /// Same status, same moment, same direction, and nothing in either line but the figure. The
    /// exclusions carry the reasoning; docs/design.md, "Ordering effects", has the why.
    /// </summary>
    private static bool CanMerge(EffectLine a, EffectLine b) =>
        a.Status.Length > 0
        && a.Status == b.Status
        && a.Onset == b.Onset
        // Opposite signs are the Book of Bones' +50 then -25, where a status clamped at either
        // end makes the net depend on where you started. Only a like-signed pair is safe.
        && (a.Amount > 0f) == (b.Amount > 0f)
        // Two clear-alls are two full bars removed once, not twice.
        && !a.Clears
        && !b.Clears
        && IsPlainToken(a)
        && IsPlainToken(b);

    /// <summary>
    /// Whether the whole line is its figure and icon, and so can be rebuilt from a new total.
    /// Asked of the rendered text rather than tracked as a flag, so a line that grows a
    /// duration, a reach or an arrow stops folding without anyone remembering to say so.
    /// </summary>
    private static bool IsPlainToken(EffectLine line) =>
        line.Amount != 0f && line.Text == EffectFormatter.Token(line.Amount, line.Status);

    /// <summary>
    /// Drops a clear-all's line for any status something else already removes at the same
    /// onset. Only a clear, and only against a removal: ordinary removals stack.
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

    private static string Key(EffectLine line) => line.Onset + "|" + line.Status;

    private static int Compare(EffectLine a, EffectLine b)
    {
        // Petrify last, whatever its onset: a price rather than an effect.
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
        // them, which is the order their components sit on the item.
        return a.Source.CompareTo(b.Source);
    }

    private static int IsPrice(EffectLine line) =>
        line.Status == STATUSTYPE.Petrify.ToString() ? 1 : 0;
}
