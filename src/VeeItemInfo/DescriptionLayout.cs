using System.Collections.Generic;
using System.Text;

namespace VeeItemInfo;

/// <summary>
/// Which section of the overlay a line belongs to. Four, in reading order: what the item is,
/// what it does, what cooking does to it, what it costs to carry.
///
/// This replaced a five-block model (Note, Status, Others, State, Weight). State only ever
/// held the uses label, which was dropped; Status and Others merged because across all 130
/// items in 2.1.a only one had content in both, and there the self-facing part was really a
/// Custom note rather than a number. Every section can be hidden independently from config.
/// </summary>
internal enum Block
{
    /// <summary>
    /// Item-specific facts that are not the result of using the item: how far a rope
    /// reaches, how many pieces something breaks into, how long an effect lasts, or a bare
    /// "???" where the item does something we deliberately do not spell out.
    ///
    /// Also what an item costs or grants while merely held - the Ancient Idol's shield, the
    /// Cactus's thorns. Those are status changes, but they belong here rather than in
    /// Effects: Effects answers "what happens when you use this", and neither item is ever
    /// used.
    /// </summary>
    Custom,

    /// <summary>
    /// Every status change, whether it lands on you or on everyone nearby. The distinction
    /// is carried by the item, not by the layout - no item in the game states both.
    /// </summary>
    Effects,

    /// <summary>What cooking does to the item.</summary>
    Cooking,

    /// <summary>Always last, so the eye learns where to find it.</summary>
    Weight,
}

/// <summary>
/// Collects description lines into sections and renders them in a fixed order.
///
/// Owning the separators here is the point: callers never embed "\n", pad with spaces to
/// share a line, or leave doubled blank lines for a final Replace to clean up.
///
/// Sections are separated by a blank line, with one deliberate exception: Weight sits flush
/// under Cooking. They are the two lines about the item rather than about its effects, and
/// reading them as one group is what stops the overlay looking like it has a stray trailing
/// line.
/// </summary>
internal sealed class DescriptionLayout
{
    private static readonly Block[] Order =
    {
        Block.Custom,
        Block.Effects,
        Block.Cooking,
        Block.Weight,
    };

    private readonly Dictionary<Block, List<string>> entries = new();

    /// <summary>
    /// Adds an entry, ignoring anything empty and anything the player has switched off.
    /// Entries may be multi-line; surrounding blank lines are stripped so the layout
    /// controls spacing rather than the caller.
    /// </summary>
    internal void Add(Block block, string? text)
    {
        if (string.IsNullOrEmpty(text) || !PluginConfig.ShowBlock(block))
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

    internal string Render()
    {
        StringBuilder result = new();
        Block previous = Block.Custom;
        bool first = true;

        foreach (Block block in Order)
        {
            if (!entries.TryGetValue(block, out List<string>? lines) || lines.Count == 0)
            {
                continue;
            }

            if (!first)
            {
                // Weight belongs with the cooking hint, not apart from it.
                result.Append(block == Block.Weight && previous == Block.Cooking ? "\n" : "\n\n");
            }

            result.Append(string.Join("\n", lines));
            previous = block;
            first = false;
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
