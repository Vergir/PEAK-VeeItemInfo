using System.Collections.Generic;
using System.Text;

namespace VeeItemInfo;

/// <summary>
/// Where a line belongs in the description. The reading order is deliberate:
/// what the item is, what it does to you, what it does to everyone else, its own
/// condition, then its cost.
/// </summary>
internal enum Block
{
    /// <summary>What the item is or does, where that isn't a status change.</summary>
    Note,

    /// <summary>Numeric changes to your own status bars. Needs no qualifier to be read.</summary>
    Status,

    /// <summary>
    /// Effects that need a scope or a condition to make sense - nearby players, on hit,
    /// over an area. The same number means something different here than in Status, which
    /// is exactly why they are separated.
    /// </summary>
    Others,

    /// <summary>The item's own condition: uses remaining, and eventually cooking state.</summary>
    State,

    /// <summary>Always the last line, so the eye learns where to find it.</summary>
    Weight,
}

/// <summary>
/// Collects description lines into blocks and renders them in a fixed order.
///
/// This replaces six ad-hoc string accumulators that each branch appended to by hand.
/// Owning the separators here is the point: callers no longer embed "\n", pad with spaces
/// to share a line, or leave doubled blank lines for a final Replace to clean up.
/// </summary>
internal sealed class DescriptionLayout
{
    private static readonly Block[] Order =
    {
        Block.Note,
        Block.Status,
        Block.Others,
        Block.State,
        Block.Weight,
    };

    private readonly Dictionary<Block, List<string>> entries = new();

    /// <summary>
    /// Adds an entry, ignoring anything empty. Entries may be multi-line; surrounding
    /// blank lines are stripped so the layout controls spacing rather than the caller.
    /// </summary>
    internal void Add(Block block, string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        string trimmed = text!.Trim('\n');
        if (trimmed.Length == 0)
        {
            return;
        }

        if (!entries.TryGetValue(block, out List<string>? lines))
        {
            lines = new List<string>();
            entries[block] = lines;
        }

        lines.Add(trimmed);
    }

    /// <summary>Blocks are separated by a blank line, entries within a block by a newline.</summary>
    internal string Render()
    {
        StringBuilder result = new();

        foreach (Block block in Order)
        {
            if (!entries.TryGetValue(block, out List<string>? lines) || lines.Count == 0)
            {
                continue;
            }

            if (result.Length > 0)
            {
                result.Append("\n\n");
            }

            result.Append(string.Join("\n", lines));
        }

        return result.ToString();
    }

    /// <summary>
    /// Joins entries with a trailing comma, the way a list of afflictions reads. Empty
    /// entries are dropped rather than leaving a dangling comma.
    /// </summary>
    internal static string JoinList(List<string> parts)
    {
        List<string> populated = new();
        foreach (string part in parts)
        {
            string trimmed = part.Trim('\n');
            if (trimmed.Length > 0)
            {
                populated.Add(trimmed);
            }
        }

        return string.Join(",\n", populated);
    }
}
