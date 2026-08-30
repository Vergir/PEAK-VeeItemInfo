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
    /// What a run with no colour tag around it renders in.
    ///
    /// TextMeshPro defaults to pure white, so every tag the overlay forgot showed up as
    /// `#FFFFFF` - brighter than anything the palette contains, and brighter still on a
    /// `tint=1` sprite, which multiplies by it. Setting the component's own colour makes a
    /// missed tag degrade to the cream instead of shouting.
    ///
    /// This is a safety net and not a licence: an untagged run is still a bug, and
    /// <see cref="ItemDebug.LogUntagged"/> reports every one of them with debug logging on.
    /// Matching <see cref="White"/> is deliberate - a leak now looks like the thing it should
    /// most likely have been.
    /// </summary>
    internal static readonly Color Base = new Color32(0xF2, 0xEC, 0xDE, 0xFF);

    /// <summary>
    /// For a value that applies to several statuses at once and so belongs to none. Matches
    /// the cream the game uses for item names, rather than pure white - a figure with no
    /// status of its own should look like it belongs to the HUD, not shout over it.
    /// </summary>
    internal const string White = "<#F2ECDE>";
    /// <summary>
    /// Saturated rather than pastel. These carry the whole meaning of a cooking hint or a
    /// random-effect marker, with no number beside them to lean on, so they have to hold
    /// their own against a bright HUD.
    /// </summary>
    internal const string Positive = "<#5FD35F>";
    internal const string Negative = "<#F55C5C>";

    /// <summary>
    /// Status colours, matching the game's own bars.
    ///
    /// Every status with a bar is now **sampled from that bar** rather than guessed. Each
    /// BarAffliction carries three Images - a dark backing on
    /// `procedural_ui_image_default_sprite`, and the bright fill on `DitherStripes` and
    /// `UI_Blur_Outlne_Thick`, which always agree. The bright pair is the status colour.
    ///
    /// The scrape that read them was a throwaway: it printed every bar's three Images once a
    /// second, which is how this table was checked, and it was deleted once the table matched.
    /// Bring it back the same way if a game update moves the palette - a loop over
    /// `FindObjectsByType&lt;BarAffliction&gt;` reading `GetComponentsInChildren&lt;Image&gt;`.
    ///
    /// Eleven of them already matched exactly, which says the hand-picking was careful.
    /// Petrify was missing outright; Spores had been taken off the icon instead of the bar
    /// and was a point out in every channel.
    ///
    /// The keys the mod invents - Extra Stamina, Shield, Numb, Cook, Item, Float - have no
    /// bar to scrape and stay hand-picked.
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
        // Nearly black, and deliberately kept that way. It is the game's own curse colour,
        // it is legible enough against the overlay in play, and it reads as cursed - which
        // no brighter substitute would. Do not "fix" this.
        { "Curse", "<#1B0043>" },
        { "Weight", "<#A65A1C>" },
        { "Thorns", "<#768E00>" },
        { "Shield", "<#D48E00>" },

        // Sampled from the bar itself rather than the icon, which put this one point out
        // in every channel. See the note above the table.
        { "Spores", "<#A65C63>" },

        // The cooking hint. Not a status - it is the campfire icon's own orange.
        { "Cook", "<#E8722A>" },

        // Numbness has no status bar to borrow a colour from, so this is sampled from the
        // icon itself - the pale stems and spots rather than the darker caps, which reads
        // against the overlay where the cap colour would not.
        { "Numb", "<#D3AC9B>" },

        // Petrify had no entry at all, so Get fell through to Neutral and every petrify
        // figure came out plain grey beside a correctly blue icon - the icon is scraped, the
        // colour was not. Muted blue-grey, not the brighter periwinkle it looks like on a
        // screenshot.
        { "Petrify", "<#858CAB>" },

        // No item in 2.1.a inflicts any of these, so none reaches the overlay. Filled in
        // anyway now that the bars are readable: a real colour costs nothing and stops the
        // day one of them appears from being the day somebody discovers it renders grey.
        { "Web", "<#E6E6E7>" },
        { "Arrow", "<#D1A072>" },
        { "FlyTrap", "<#187B32>" },
    };

    /// <summary>
    /// Colours read off the running game, which win over the table above.
    ///
    /// The table is a sampling somebody took once and pasted in, so a patch that repainted a
    /// bar would leave it saying the old thing forever - exactly the failure this mod keeps
    /// hitting with numbers. <see cref="StatusIcons"/> already walks every BarAffliction to
    /// scrape icons, so the colour rides along on that same walk and costs nothing.
    ///
    /// The table stays as the fallback rather than being deleted: the walk happens once the
    /// HUD exists, and a description built before then still needs an answer.
    /// </summary>
    private static readonly Dictionary<string, string> Sampled = new();

    /// <summary>
    /// Records a colour read off the game's own UI.
    ///
    /// Pure white and pure black are refused. A tinted silhouette left at white means the
    /// artwork carries its own colour and nothing was chosen here, which is not a palette
    /// entry - taking it would turn the shield marker from gold into the default. Same for a
    /// fully black Image, which is a backing rather than a fill.
    /// </summary>
    internal static void Sample(string effect, Color color)
    {
        // Only *pure* white is refused, not merely pale. Web is #E6E6E7 - a real status colour
        // at 0.90 - so a threshold with any slack in it would silently start discarding a
        // reading the moment the game brightened that bar.
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
