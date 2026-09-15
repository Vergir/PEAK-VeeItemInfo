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
    /// <summary>Plain descriptive text, and the fallback for unknown statuses.</summary>
    internal const string Neutral = "<#CCCCCC>";

    /// <summary>
    /// What a run with no colour tag renders in: the HUD's cream, not TextMeshPro's white. A
    /// safety net - an untagged run is still a bug, and <see cref="ItemDebug.LogUntagged"/>
    /// reports it.
    /// </summary>
    internal static readonly Color Base = new Color32(0xF2, 0xEC, 0xDE, 0xFF);

    /// <summary>For a value that belongs to no one status: the cream the game uses for item names.</summary>
    internal const string White = "<#F2ECDE>";

    /// <summary>Saturated, because these carry their whole meaning with no number beside them.</summary>
    internal const string Positive = "<#5FD35F>";
    internal const string Negative = "<#F55C5C>";

    /// <summary>
    /// The game's own bar colours, as a fallback for a description built before the HUD
    /// exists; see <see cref="Sampled"/>. Numb, Item and Float have no bar and stay hand-picked.
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
        // Nearly black on purpose: the game's own curse colour. Settled - do not "fix" this.
        { "Curse", "<#1B0043>" },
        { "Weight", "<#A65A1C>" },
        { "Thorns", "<#768E00>" },
        { "Shield", "<#D48E00>" },
        { "Spores", "<#A65C63>" },
        { "Cook", "<#E8722A>" },
        // The icon's pale stems, not its dark caps.
        { "Numb", "<#D3AC9B>" },
        { "Petrify", "<#858CAB>" },
        { "Web", "<#E6E6E7>" },
        { "Arrow", "<#D1A072>" },
        { "FlyTrap", "<#187B32>" },
    };

    /// <summary>
    /// Colours read off the bars on each atlas build, which win over the table. See
    /// docs/internals_infra.md, "Colours".
    /// </summary>
    private static readonly Dictionary<string, string> Sampled = new();

    /// <summary>
    /// Pure white is refused (an untinted Image carries its own colour) and so is pure black
    /// (a backing, not a fill). Only *pure* white: Web is a real status colour at 0.90.
    /// </summary>
    internal static void Sample(string effect, Color color)
    {
        float max = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
        float min = Mathf.Min(color.r, Mathf.Min(color.g, color.b));
        if (max < 0.02f || min > 0.99f)
        {
            return;
        }

        Sampled[effect] = "<#" + ColorUtility.ToHtmlStringRGB(color) + ">";
    }

    /// <summary>
    /// The sampled colours that differ from the table. A long list means the sampling picked
    /// the wrong Image; a short one means the game moved a colour.
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

    internal static void ClearSamples() => Sampled.Clear();

    /// <summary>Never throws: an unmapped status renders neutral rather than blanking the overlay.</summary>
    internal static string Get(string effect)
    {
        if (Sampled.TryGetValue(effect, out string? live))
        {
            return live;
        }

        return Colors.TryGetValue(effect, out string? color) ? color : Neutral;
    }
}
