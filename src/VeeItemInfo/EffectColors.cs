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

        // Colour-picked from the game's own status icons rather than guessed. Shield and
        // Weight were sampled too and confirmed the existing values.
        { "Spores", "<#A55B63>" },

        // The cooking hint. Not a status - it is the campfire icon's own orange.
        { "Cook", "<#E8722A>" },

        // Numbness has no status bar to borrow a colour from, so this is sampled from the
        // icon itself - the pale stems and spots rather than the darker caps, which reads
        // against the overlay where the cap colour would not.
        { "Numb", "<#D3AC9B>" },

        // Present in STATUSTYPE as of PEAK 2.1.a but never given a colour by the
        // original mod. Listed explicitly so the gap is visible rather than silent.
        // None of these appears on any item in the game, so none has been sampled yet.
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
