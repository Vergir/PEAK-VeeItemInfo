using System.Collections.Generic;
using System.Text;

namespace VeeItemInfo;

/// <summary>
/// Which section of the overlay a line belongs to, in reading order. What goes where is in
/// docs/design.md, "Four sections".
/// </summary>
internal enum Block
{
    /// <summary>Facts that are not the result of using the item, including what it grants while held.</summary>
    Custom,

    /// <summary>Every status change, whether it lands on you or on everyone nearby.</summary>
    Effects,

    Cooking,

    Weight,
}

/// <summary>
/// Collects description lines into sections and renders them in a fixed order. Owns the
/// separators: callers never embed newlines or pad lines.
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

    /// <summary>Ignores empty text and any section the player has switched off.</summary>
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
        string gap = PluginConfig.SectionSpacing.Value ? "\n\n" : "\n";

        foreach (Block block in Order)
        {
            if (!entries.TryGetValue(block, out List<string>? lines) || lines.Count == 0)
            {
                continue;
            }

            if (!first)
            {
                // Weight belongs with the cooking hint, not apart from it.
                result.Append(block == Block.Weight && previous == Block.Cooking ? "\n" : gap);
            }

            result.Append(string.Join("\n", lines));
            previous = block;
            first = false;
        }

        return result.ToString();
    }

    /// <summary>Joins entries with a trailing comma, dropping empty ones.</summary>
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
