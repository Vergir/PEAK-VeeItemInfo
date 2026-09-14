using System.Collections.Generic;
using UnityEngine;

namespace VeeItemInfo;

/// <summary>
/// TextMeshPro colour tags keyed by status/effect name.
/// Keys come from two places: <see cref="CharacterAfflictions.STATUSTYPE"/> member names,
/// and a few literal names the mod passes in itself (e.g. "Extra Stamina", "Shield").
/// </summary>
internal static class EffectColors
{
    /// <summary>Used for plain descriptive text, and as the fallback for unknown statuses.</summary>
    internal const string Neutral = "<#CCCCCC>";

    /// <summary>
    /// What a run with no colour tag around it renders in. TextMeshPro's default is pure
    /// white, which made every forgotten tag shout; the cream makes a leak look like what it
    /// most likely should have been. A safety net, not a licence - an untagged run is still a
    /// bug, and <see cref="ItemDebug.LogUntagged"/> reports every one.
    /// </summary>
    internal static readonly Color Base = new Color32(0xF2, 0xEC, 0xDE, 0xFF);

    /// <summary>
    /// For a value that belongs to no one status. The cream the game uses for item names,
    /// not pure white.
    /// </summary>
    internal const string White = "<#F2ECDE>";

    /// <summary>
    /// Saturated rather than pastel: these carry the whole meaning of a cooking hint or a
    /// random-effect marker with no number beside them to lean on.
    /// </summary>
    internal const string Positive = "<#5FD35F>";
    internal const string Negative = "<#F55C5C>";

    /// <summary>
    /// Status colours, matching the game's own bars.
    ///
    /// Every status with a bar is **sampled from that bar** on each atlas build - see
    /// <see cref="Sampled"/> - and this table is the fallback for a description built before
    /// the HUD exists. It was checked against the bars once by hand: eleven matched exactly,
    /// Petrify was missing outright, and Spores had been taken off the icon instead of the
    /// bar and was a point out in every channel.
    ///
    /// The keys the mod invents - Numb, Item, Float - have no bar to sample and stay
    /// hand-picked. Shield, Cook and Extra Stamina turned out to have one after all.
    /// </summary>
    private static readonly Dictionary<string, string> Colors = new()
    {
        { "Hunger", "<#FFBD16>" },
        { "Extra Stamina", "<#BFEC1B>" },
        { "Injury", "<#FF5300>" },
        { "Crab", "<#E13542>" },
        { "Poison", "<#A139FF>" },
        { "Cold", "<#00BCFF>" },
        { "Heat", "<#C80918>" },
        { "Hot", "<#C80918>" },
        { "Sleepy", "<#FF5CA4>" },
        { "Drowsy", "<#FF5CA4>" },
        // Nearly black, and deliberately kept that way: it is the game's own curse colour and
        // it reads as cursed. Settled - do not "fix" this.
        { "Curse", "<#1B0043>" },
        { "Weight", "<#A65A1C>" },
        { "Thorns", "<#768E00>" },
        { "Shield", "<#D48E00>" },
        { "Spores", "<#A65C63>" },

        // The cooking hint. Not a status - it is the campfire icon's own orange.
        { "Cook", "<#E8722A>" },

        // Numbness has no bar; taken from the icon's pale stems rather than its dark caps,
        // which would not read against the overlay.
        { "Numb", "<#D3AC9B>" },

        // Muted blue-grey, not the brighter periwinkle it looks like on a screenshot.
        { "Petrify", "<#858CAB>" },

        // No item inflicts these, but a real colour costs nothing and grey would be a bug.
        { "Web", "<#E6E6E7>" },
        { "Arrow", "<#D1A072>" },
        { "FlyTrap", "<#187B32>" },
    };

    /// <summary>
    /// Colours read off the running game, which win over the table above. They ride along on
    /// the <see cref="StatusIcons"/> walk that scrapes the bars; the table stays as the
    /// fallback for a description built before the HUD exists. See docs/internals_infra.md,
    /// "Colours".
    /// </summary>
    private static readonly Dictionary<string, string> Sampled = new();

    /// <summary>
    /// Records a colour read off the game's own UI. Pure white is refused - an untinted Image
    /// means the artwork carries its own colour and nothing was chosen - and so is pure
    /// black, which is a backing rather than a fill.
    /// </summary>
    internal static void Sample(string effect, Color color)
    {
        // Only *pure* white, not merely pale: Web is #E6E6E7, a real status colour at 0.90.
        float max = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
        float min = Mathf.Min(color.r, Mathf.Min(color.g, color.b));
        if (max < 0.02f || min > 0.99f)
        {
            return;
        }

        Sampled[effect] = "<#" + ColorUtility.ToHtmlStringRGB(color) + ">";
    }

    /// <summary>
    /// What was sampled and how it compares to the table, as one line. Only the differences
    /// are named: a long list means the sampling is picking the wrong Image, and a short one
    /// means the game moved a colour and the overlay followed it.
    /// </summary>
    internal static string SampleReport()
    {
        List<string> moved = new();
        foreach (KeyValuePair<string, string> entry in Sampled)
        {
            if (!Colors.TryGetValue(entry.Key, out string? had) || had != entry.Value)
            {
                moved.Add($"{entry.Key} {had ?? "(none)"}->{entry.Value}");
            }
        }

        return $"{Sampled.Count} colours read from the game"
            + (moved.Count == 0 ? ", all matching the table." : $", {moved.Count} differing: {string.Join(", ", moved)}.");
    }

    /// <summary>Drops the sampled palette, for a hot reload or a rebuilt HUD.</summary>
    internal static void ClearSamples() => Sampled.Clear();

    /// <summary>
    /// Never throws. An unmapped status returns <see cref="Neutral"/> - a missing key
    /// used to bubble a KeyNotFoundException out of the whole build and blank the overlay.
    /// </summary>
    internal static string Get(string effect)
    {
        if (Sampled.TryGetValue(effect, out string? live))
        {
            return live;
        }

        return Colors.TryGetValue(effect, out string? color) ? color : Neutral;
    }
}
