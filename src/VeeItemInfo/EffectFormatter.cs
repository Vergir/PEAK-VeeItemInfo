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
            Affliction_FasterBoi effect = (Affliction_FasterBoi)affliction;
            result += EffectColors.Positive + "GAIN</color> " + Num(effect.totalTime + effect.climbDelay) + "s OF "
                + EffectColors.Get("Extra Stamina") + Num(Mathf.Round(effect.moveSpeedMod * 100f)) + "% BONUS RUN SPEED</color> OR\n"
                + EffectColors.Positive + "GAIN</color> " + Num(effect.totalTime) + "s OF " + EffectColors.Get("Extra Stamina")
                + Num(Mathf.Round(effect.climbSpeedMod * 100f)) + "% BONUS CLIMB SPEED</color>\nAFTERWARDS, " + EffectColors.Negative
                + "GAIN</color> " + EffectColors.Get("Drowsy") + Scaled(effect.drowsyOnEnd) + " DROWSY</color>\n";
        }
        else if (affliction.GetAfflictionType() is PeakAffliction.AfflictionType.ClearAllStatus)
        {
            Affliction_ClearAllStatus effect = (Affliction_ClearAllStatus)affliction;
            result += EffectColors.Positive + "CLEAR ALL STATUS</color>";
            if (effect.excludeCurse)
            {
                result += " EXCEPT " + EffectColors.Get("Curse") + "CURSE</color>";
            }
            result += "\n";
        }
        else if (affliction.GetAfflictionType() is PeakAffliction.AfflictionType.AddBonusStamina)
        {
            Affliction_AddBonusStamina effect = (Affliction_AddBonusStamina)affliction;
            result += EffectColors.Positive + "GAIN</color> " + EffectColors.Get("Extra Stamina")
                + Scaled(effect.staminaAmount) + " EXTRA STAMINA</color>\n";
        }
        else if (affliction.GetAfflictionType() is PeakAffliction.AfflictionType.InfiniteStamina)
        {
            Affliction_InfiniteStamina effect = (Affliction_InfiniteStamina)affliction;
            if (effect.climbDelay > 0)
            {
                result += EffectColors.Positive + "GAIN</color> " + Num(effect.totalTime + effect.climbDelay) + "s OF "
                    + EffectColors.Get("Extra Stamina") + "INFINITE RUN STAMINA</color> OR\n" + EffectColors.Positive + "GAIN</color> "
                    + Num(effect.totalTime) + "s OF " + EffectColors.Get("Extra Stamina") + "INFINITE CLIMB STAMINA</color>\n";
            }
            else
            {
                result += EffectColors.Positive + "GAIN</color> " + Num(effect.totalTime) + "s OF "
                    + EffectColors.Get("Extra Stamina") + "INFINITE STAMINA\n";
            }
            if (effect.drowsyAffliction != null)
            {
                result += "AFTERWARDS, " + Affliction(effect.drowsyAffliction);
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
            Affliction_AdjustColdOverTime effect = (Affliction_AdjustColdOverTime)affliction; // 1.6.a
            result += (effect.statusPerSecond > 0 ? EffectColors.Negative + "GAIN</color> " : EffectColors.Positive + "REMOVE</color> ")
                + EffectColors.Get("Cold") + Scaled(Mathf.Abs(effect.statusPerSecond) * effect.totalTime)
                + " COLD</color> OVER " + Num(effect.totalTime) + "s\n";
        }
        else if (affliction.GetAfflictionType() is PeakAffliction.AfflictionType.Chaos)
        {
            result += EffectColors.Positive + "CLEAR ALL STATUS</color>, THEN RANDOMIZE\n" + EffectColors.Get("Hunger") + "HUNGER</color>, "
                + EffectColors.Get("Extra Stamina") + "EXTRA STAMINA</color>, " + EffectColors.Get("Injury") + "INJURY</color>,\n"
                + EffectColors.Get("Poison") + "POISON</color>, " + EffectColors.Get("Cold") + "COLD</color>, "
                + EffectColors.Get("Hot") + "HEAT</color>, " + EffectColors.Get("Drowsy") + "DROWSY</color>\n";
        }
        else if (affliction.GetAfflictionType() is PeakAffliction.AfflictionType.Sunscreen)
        {
            Affliction_Sunscreen effect = (Affliction_Sunscreen)affliction;
            result += "PREVENT " + EffectColors.Get("Heat") + "HEAT</color> IN MESA'S SUN FOR " + Num(effect.totalTime) + "s\n";
        }

        return result;
    }
}
