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
    /// The "GAIN"/"REMOVE" prefix and its colour. Extra Stamina is the one effect where
    /// gaining is good news, so its polarity is inverted against everything else.
    /// </summary>
    private static string GainOrRemove(float amount, string effect)
    {
        bool isGain = amount > 0f;
        bool beneficial = effect.Equals("Extra Stamina") ? isGain : !isGain;
        return (beneficial ? EffectColors.Positive : EffectColors.Negative)
            + (isGain ? "GAIN" : "REMOVE") + "</color> ";
    }

    /// <summary>An instant status change, e.g. "GAIN 25 HUNGER".</summary>
    internal static string Effect(float amount, string effect)
    {
        if (amount == 0f)
        {
            return "";
        }

        return GainOrRemove(amount, effect)
            + EffectColors.Get(effect) + Scaled(Mathf.Abs(amount)) + " " + effect.ToUpper() + "</color>\n";
    }

    /// <summary>A status change spread over time, e.g. "GAIN 40 POISON OVER 8s".</summary>
    internal static string EffectOverTime(float amountPerSecond, float rate, float time, string effect)
    {
        if (amountPerSecond == 0f || time == 0f)
        {
            return "";
        }

        float total = Mathf.Abs(amountPerSecond) * time * (1f / rate);
        return GainOrRemove(amountPerSecond, effect)
            + EffectColors.Get(effect) + Scaled(total) + " " + effect.ToUpper() + "</color> OVER " + time.ToString() + "s\n";
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
            string status = effect.statusType.ToString();
            result += GainOrRemove(effect.statusAmount, status)
                + EffectColors.Get(status) + Scaled(Mathf.Abs(effect.statusAmount)) + " " + status.ToUpper() + "</color>\n";
        }
        else if (affliction.GetAfflictionType() is PeakAffliction.AfflictionType.DrowsyOverTime)
        {
            Affliction_AdjustDrowsyOverTime effect = (Affliction_AdjustDrowsyOverTime)affliction; // 1.6.a
            result += (effect.statusPerSecond > 0 ? EffectColors.Negative + "GAIN</color> " : EffectColors.Positive + "REMOVE</color> ")
                + EffectColors.Get("Drowsy")
                + Num(Mathf.Round((Mathf.Abs(effect.statusPerSecond) * effect.totalTime * 100f) * 0.4f) / 0.4f)
                + " DROWSY</color> OVER " + Num(effect.totalTime) + "s\n";
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
