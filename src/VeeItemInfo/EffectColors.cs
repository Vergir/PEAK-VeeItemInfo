using System.Collections.Generic;

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
    internal const string Positive = "<#DDFFDD>";
    internal const string Negative = "<#FFCCCC>";

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
        { "Curse", "<#1B0043>" },
        { "Weight", "<#A65A1C>" },
        { "Thorns", "<#768E00>" },
        { "Shield", "<#D48E00>" },

        // Present in STATUSTYPE as of PEAK 2.1.a but never given a colour by the
        // original mod. Listed explicitly so the gap is visible rather than silent.
        // TODO: replace with the game's own status UI colours.
        { "Spores", Neutral },
        { "Web", Neutral },
        { "Arrow", Neutral },
        { "Petrify", Neutral },
        { "FlyTrap", Neutral },
    };

    /// <summary>
    /// Never throws. An unmapped status returns <see cref="Neutral"/> - a missing key
    /// used to bubble a KeyNotFoundException out of the whole build and blank the overlay.
    /// </summary>
    internal static string Get(string effect) =>
        Colors.TryGetValue(effect, out string? color) ? color : Neutral;
}
