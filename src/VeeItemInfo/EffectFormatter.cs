using System.Collections.Generic;
using System.Text;
using Peak.Afflictions;
using UnityEngine;

// There is a second, unrelated Affliction type in the global namespace. Alias the one we
// mean so it can never be resolved against that by accident.
using PeakAffliction = Peak.Afflictions.Affliction;

namespace VeeItemInfo;

/// <summary>
/// Turns raw game numbers into the coloured, upper-case lines shown in the overlay.
/// </summary>
internal static class EffectFormatter
{
    /// <summary>One decimal place, with a pointless trailing ".0" trimmed off.</summary>
    internal static string Num(float value) => value.ToString("F1").Replace(".0", "");

    /// <summary>Status amounts are stored as 0-1 fractions but displayed on a 0-100 scale.</summary>
    internal static string Scaled(float value) => Num(value * 100f);

    /// <summary>
    /// A signed, coloured amount followed by the status icon - "+30 &lt;flame&gt;".
    ///
    /// The sign states which way the status moves, so no wording is needed and the line
    /// reads the same in any language. Colour comes from the status itself rather than
    /// from whether the change is good for you: the icon already carries that meaning,
    /// and tying colour to the status keeps it consistent with the game's own bars.
    /// </summary>
    internal static string Token(float amount, string effect) =>
        Colored((amount > 0f ? "+" : "-") + Scaled(Mathf.Abs(amount)), effect);

    /// <summary>An unsigned amount and its icon, for values that have no direction.</summary>
    internal static string Plain(float amount, string effect) =>
        Colored(Num(amount), effect);

    /// <summary>
    /// A value and its status icon in the status colour. The icon sits inside the colour
    /// span on purpose: its tag carries tint=1, so it takes that colour too and the icon
    /// always matches the number beside it.
    /// </summary>
    internal static string Colored(string value, string effect) =>
        EffectColors.Get(effect) + value + " " + StatusIcons.Tag(effect) + "</color>";

    /// <summary>Separates the two halves of a transformation, as in "item -> item item".</summary>
    internal const string Arrow = " → ";

    /// <summary>
    /// Infinite rather than a number. Font support for U+221E in PEAK's HUD font is not
    /// verified - if it renders as a box, this is the single place to change.
    /// </summary>
    internal const string Infinity = "∞";

    /// <summary>
    /// Every status a player can carry that an item can clear, in the order they read best.
    /// Curse is not in the list: almost everything excludes it, so it is handled separately.
    /// </summary>
    internal static readonly string[] AllStatuses =
    {
        "Hunger", "Injury", "Poison", "Spores", "Cold", "Hot", "Drowsy", "Thorns",
    };

    /// <summary>
    /// A distance. No space before the unit, matching how durations are written: a number
    /// and its unit are one token, so "4.8m" and "8s" read the same way.
    /// </summary>
    internal static string Metres(float value) => Num(value) + "m";

    /// <summary>A duration, same no-space rule as <see cref="Metres"/>.</summary>
    internal static string Seconds(float value) => Num(value) + "s";

    /// <summary>
    /// "8s ∞ &lt;stamina&gt;". The infinity mark is spaced away from the icon: flush it read
    /// as one glyph rather than as a quantity standing in for a number.
    /// </summary>
    internal static string InfiniteStamina(float seconds) =>
        EffectColors.Neutral + Seconds(seconds) + "</color> "
        + EffectColors.Get("Extra Stamina") + Infinity + " " + StatusIcons.Tag("Extra Stamina") + "</color>";

    /// <summary>The signed amount in the neutral cream, for figures owned by no one status.</summary>
    private static string Figure(float amount) =>
        EffectColors.White + (amount > 0f ? "+" : "-") + Scaled(Mathf.Abs(amount)) + "</color>";

    /// <summary>
    /// One amount applying to each of several statuses - "-35 &lt;poison&gt; &lt;spores&gt;",
    /// meaning 35 off poison <i>and</i> 35 off spores. Space separated, because that is what
    /// almost every multi-status item in the game does: Pandora's Lunchbox and Cure-All clear
    /// the full amount from every status they touch.
    ///
    /// White, because a figure belonging to a whole set belongs to none of them in
    /// particular. This is what keeps Cure-All to three lines instead of nine.
    /// </summary>
    internal static string MultiStatus(float amount, params string[] statuses)
    {
        if (amount == 0f || statuses.Length == 0)
        {
            return "";
        }

        return Figure(amount) + " " + IconRun(statuses);
    }

    /// <summary>
    /// One amount <i>shared across</i> several statuses - "-60 &lt;injury&gt;/&lt;poison&gt;",
    /// meaning 60 points of relief split between them, not 60 off each.
    ///
    /// Slashes are the whole difference from <see cref="MultiStatus"/>, and they are
    /// deliberately reserved for this one meaning. Only the healing amulet works this way in
    /// PEAK 2.1.a; using slashes anywhere else would blur the distinction that makes them
    /// worth having.
    /// </summary>
    internal static string SharedBudget(float amount, params string[] statuses)
    {
        if (amount == 0f || statuses.Length == 0)
        {
            return "";
        }

        return Figure(amount) + " " + IconRun(statuses, EffectColors.White + "/</color>");
    }

    /// <summary>
    /// How many icons fit on one line before it starts to read as a wall. Past four the eye
    /// stops counting them and the overlay wraps at an arbitrary point instead of a chosen
    /// one, so the break is made here rather than left to the text box.
    /// </summary>
    private const int IconsPerLine = 4;

    /// <summary>
    /// Status icons in a row, each tinted its own colour, space separated and wrapped onto a
    /// new line every <see cref="IconsPerLine"/>.
    /// </summary>
    internal static string IconRun(string[] statuses, string separator = " ")
    {
        StringBuilder result = new();
        for (int i = 0; i < statuses.Length; i++)
        {
            if (i > 0)
            {
                result.Append(i % IconsPerLine == 0 ? "\n" : separator);
            }

            result.Append(EffectColors.Get(statuses[i])).Append(StatusIcons.Tag(statuses[i])).Append("</color>");
        }

        return result.ToString();
    }

    /// <summary>
    /// A rate rather than a total - "-5 &lt;cold&gt; / s". Over-time effects are stated per
    /// second so the figure means the same thing on every item, whatever its duration, and
    /// so a nearly-spent lantern reads the same as a full one.
    /// </summary>
    internal static string PerSecond(float amountPerSecond, string effect)
    {
        if (amountPerSecond == 0f)
        {
            return "";
        }

        return Token(amountPerSecond, effect) + EffectColors.Neutral + " /s</color>";
    }

    /// <summary>
    /// "Clear all status" as one line: the amount, then the icons it actually clears.
    /// Dropping the excluded ones from the run says which are spared without naming them.
    /// </summary>
    internal static string ClearedStatuses(bool excludeCurse, IEnumerable<CharacterAfflictions.STATUSTYPE>? exclusions)
    {
        List<string> cleared = new();
        foreach (string status in AllStatuses)
        {
            bool skipped = false;
            if (exclusions != null)
            {
                foreach (CharacterAfflictions.STATUSTYPE exclusion in exclusions)
                {
                    if (exclusion.ToString() == status)
                    {
                        skipped = true;
                        break;
                    }
                }
            }

            if (!skipped)
            {
                cleared.Add(status);
            }
        }

        if (!excludeCurse)
        {
            cleared.Add("Curse");
        }

        if (cleared.Count == 0)
        {
            return "";
        }

        // One line per status. Collapsing them into a shared "-100 <eight icons>" line read
        // as a single pooled effect, which is what SharedBudget means - and clearing all
        // status is emphatically not that. Every status loses its full 100.
        StringBuilder lines = new();
        foreach (string status in cleared)
        {
            if (lines.Length > 0)
            {
                lines.Append('\n');
            }

            lines.Append(Token(-1f, status));
        }

        return lines.ToString();
    }

    /// <summary>
    /// A run of status icons, each in its own colour, slash-separated. For effects that hit
    /// a whole set of statuses rather than one.
    /// </summary>
    internal static string IconList(string[] statuses)
    {
        string[] tokens = new string[statuses.Length];
        for (int i = 0; i < statuses.Length; i++)
        {
            tokens[i] = EffectColors.Get(statuses[i]) + StatusIcons.Tag(statuses[i]) + "</color>";
        }

        return string.Join(EffectColors.White + "/</color>", tokens);
    }

    /// <summary>An instant status change, e.g. "+25 &lt;food&gt;".</summary>
    internal static string Effect(float amount, string effect)
    {
        if (amount == 0f)
        {
            return "";
        }

        return Token(amount, effect) + "\n";
    }

    /// <summary>A status change spread over time, e.g. "+40 &lt;skull&gt; 8s".</summary>
    internal static string EffectOverTime(float amountPerSecond, float rate, float time, string effect)
    {
        if (amountPerSecond == 0f || time == 0f)
        {
            return "";
        }

        float total = amountPerSecond * time * (1f / rate);
        return Token(total, effect) + " / " + Num(time) + "s\n";
    }

    /// <summary>
    /// Describes an affliction. Dispatches on <see cref="Affliction.AfflictionType"/>;
    /// an unrecognised type returns an empty string rather than guessing.
    /// </summary>
    internal static string Affliction(PeakAffliction affliction)
    {
        string result = "";

        if (affliction.GetAfflictionType() is PeakAffliction.AfflictionType.FasterBoi)
        {
            // Three stamina icons for "you move faster", deliberately not the infinity mark:
            // the stamina is not infinite here, only the movement is quicker, and reusing
            // the infinity mark would say the wrong thing. The run and climb windows differ
            // by climbDelay; the shorter one is the honest figure to show.
            Affliction_FasterBoi effect = (Affliction_FasterBoi)affliction;
            result += EffectColors.Neutral + Seconds(effect.totalTime) + "</color> "
                + IconRun(new[] { "Extra Stamina", "Extra Stamina", "Extra Stamina" });

            if (effect.drowsyOnEnd > 0f)
            {
                result += EffectColors.Neutral + Arrow + "</color>" + Token(effect.drowsyOnEnd, "Drowsy");
            }

            result += "\n";
        }
        else if (affliction.GetAfflictionType() is PeakAffliction.AfflictionType.ClearAllStatus)
        {
            Affliction_ClearAllStatus effect = (Affliction_ClearAllStatus)affliction;
            result += ClearedStatuses(effect.excludeCurse, null) + "\n";
        }
        else if (affliction.GetAfflictionType() is PeakAffliction.AfflictionType.AddBonusStamina)
        {
            // Was "GAIN 100 EXTRA STAMINA" - the last piece of prose left on a common path.
            Affliction_AddBonusStamina effect = (Affliction_AddBonusStamina)affliction;
            result += Token(effect.staminaAmount, "Extra Stamina") + "\n";
        }
        else if (affliction.GetAfflictionType() is PeakAffliction.AfflictionType.InfiniteStamina)
        {
            // A duration, the infinity mark, and the stamina icon - flush, because the mark
            // qualifies the icon rather than standing on its own. Where climbDelay grants a
            // longer running window than a climbing one, the shorter figure is shown: it is
            // the one you can rely on whatever you are doing.
            Affliction_InfiniteStamina effect = (Affliction_InfiniteStamina)affliction;
            result += InfiniteStamina(effect.totalTime) + "\n";

            if (effect.drowsyAffliction != null)
            {
                result += Affliction(effect.drowsyAffliction);
            }
        }
        else if (affliction.GetAfflictionType() is PeakAffliction.AfflictionType.AdjustStatus)
        {
            Affliction_AdjustStatus effect = (Affliction_AdjustStatus)affliction;
            result += Token(effect.statusAmount, effect.statusType.ToString()) + "\n";
        }
        else if (affliction.GetAfflictionType() is PeakAffliction.AfflictionType.DrowsyOverTime)
        {
            // Affliction_AdjustDrowsyOverTime.UpdateEffect applies statusPerSecond * deltaTime
            // every frame, so the total is exactly statusPerSecond * totalTime (verified
            // against 2.1.a). The original rounded to multiples of 2.5 for no reason, which
            // could be off by up to 1.25.
            Affliction_AdjustDrowsyOverTime effect = (Affliction_AdjustDrowsyOverTime)affliction;
            result += EffectOverTime(effect.statusPerSecond, 1f, effect.totalTime, "Drowsy");
        }
        else if (affliction.GetAfflictionType() is PeakAffliction.AfflictionType.ColdOverTime)
        {
            // Heat Pack, and the last prose in the mod - it used to read
            // "GAIN/REMOVE {n} COLD OVER {n}s". The sign says which way the status moves,
            // the icon says what moves, and the arrow says how long it keeps moving.
            //
            // A rate and a duration rather than a total, unlike the drowsy branch above.
            // UpdateEffect applies statusPerSecond * deltaTime every frame against a scale
            // that stops at 100, so a Heat Pack's rate across its full time multiplies out
            // to many times that maximum - a total here is arithmetic nobody can feel. The
            // rate is what you get; the duration is how long you keep getting it.
            Affliction_AdjustColdOverTime effect = (Affliction_AdjustColdOverTime)affliction;
            string rate = PerSecond(effect.statusPerSecond, "Cold");
            if (rate.Length > 0)
            {
                result += rate + EffectColors.Neutral + Arrow
                    + Seconds(effect.totalTime) + "</color>\n";
            }
        }
        else if (affliction.GetAfflictionType() is PeakAffliction.AfflictionType.Chaos)
        {
            // Cleared, then an unknown amount handed straight back. Showing the pair on one
            // line per status is what makes the randomisation legible - two separate blocks
            // read as two unrelated effects rather than one shuffle.
            foreach (string status in AllStatuses)
            {
                result += Token(-1f, status);

                // Everything is cleared, but Thorns is the one status the randomiser cannot
                // hand back, so it gets no "-> +?" tail.
                if (status != "Thorns")
                {
                    result += EffectColors.Neutral + Arrow + "</color>"
                        + EffectColors.Get(status) + "+?" + " " + StatusIcons.Tag(status) + "</color>";
                }

                result += "\n";
            }
        }
        else if (affliction.GetAfflictionType() is PeakAffliction.AfflictionType.Invincibility)
        {
            // Fortified Milk and the healing amulet both grant this. It was going unreported
            // entirely - there was no branch for it, so the shield line simply never appeared.
            result += EffectColors.Neutral + Seconds(affliction.totalTime) + "</color> "
                + EffectColors.Get("Shield") + StatusIcons.Tag("Shield") + "</color>\n";
        }
        else if (affliction.GetAfflictionType() is PeakAffliction.AfflictionType.RadiateInfiniteStam)
        {
            // Scout's Ambition. Infinite stamina for everyone standing close enough, so the
            // radius is as much the point as the duration.
            Affliction_RadiateInfiniteStam effect = (Affliction_RadiateInfiniteStam)affliction;
            result += InfiniteStamina(effect.totalTime)
                + EffectColors.Neutral + " " + Metres(effect.radius) + "</color>\n";
        }
        else if (affliction.GetAfflictionType() is PeakAffliction.AfflictionType.MassSuperJump)
        {
            // Scout's Initiative. It does not grant speed - it launches everyone nearby and
            // drops their gravity, so the balloon is the right symbol for what you feel.
            Affliction_MassSuperJump effect = (Affliction_MassSuperJump)affliction;
            result += EffectColors.Neutral + Seconds(effect.lowGravTime) + "</color> "
                + StatusIcons.Tag("Float")
                + EffectColors.Neutral + " " + Metres(effect.radius) + "</color>\n";
        }
        else if (affliction.GetAfflictionType() is PeakAffliction.AfflictionType.Sunscreen)
        {
            // Just how long it lasts. Naming the biome it protects you in was the only
            // English left on this line, and the item's own icon already says what it is.
            Affliction_Sunscreen effect = (Affliction_Sunscreen)affliction;
            result += EffectColors.Neutral + Seconds(effect.totalTime) + "</color>\n";
        }

        return result;
    }
}
