using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Peak.Afflictions;
using UnityEngine;

// A second, unrelated Affliction type exists in the global namespace.
using PeakAffliction = Peak.Afflictions.Affliction;

namespace VeeItemInfo;

/// <summary>
/// Turns raw game numbers into the coloured lines shown in the overlay. The only place a
/// line's form is chosen; what each form means is in docs/design.md, "Reading a line".
/// </summary>
internal static class EffectFormatter
{
    /// <summary>Invariant culture: a comma decimal would break "2,5 / 8s" into two figures.</summary>
    internal static string Num(float value) => Fixed(value, 1);

    private static string Fixed(float value, int decimals)
    {
        string text = value.ToString("F" + decimals, CultureInfo.InvariantCulture);
        return text.Contains('.') ? text.TrimEnd('0').TrimEnd('.') : text;
    }

    /// <summary>A 0-1 status fraction on the configured scale, with enough decimals to show one step.</summary>
    internal static string Scaled(float value) => Fixed(value * StatusScale, ScaleDecimals);

    private static float StatusScale => PluginConfig.StatusScale.Value;

    /// <summary>Fewest decimals that show one status step exactly, capped at three.</summary>
    private static int ScaleDecimals
    {
        get
        {
            float step = GameValues.StatusStep * StatusScale;
            for (int decimals = 0; decimals < 3; decimals++)
            {
                float shifted = step * Mathf.Pow(10f, decimals);
                if (Mathf.Abs(shifted - Mathf.Round(shifted)) < 0.001f)
                {
                    return decimals;
                }
            }

            return 3;
        }
    }

    /// <summary>
    /// Petrify is floored to whole points on the game's 100-point scale before it is stored,
    /// so 0.075 is 7, not 7.5; the display scale applies after the floor.
    /// </summary>
    internal static string WholePoints(float fraction) =>
        Scaled(Mathf.Floor(fraction * 100f) / 100f);

    /// <summary>"+30 &lt;flame&gt;".</summary>
    internal static string Token(float amount, string effect) =>
        Colored((amount > 0f ? "+" : "-") + Scaled(Mathf.Abs(amount)), effect);

    /// <summary>"+ 0/20 &lt;poison&gt; / 8s" - a change that may not land at all.</summary>
    internal static string ConditionalOverTime(float total, float seconds, string effect)
    {
        if (total == 0f || seconds <= 0f)
        {
            return "";
        }

        return Colored((total > 0f ? "+" : "-") + " 0/" + Scaled(Mathf.Abs(total)), effect)
            + EffectColors.Neutral + " / " + Seconds(seconds) + "</color>";
    }

    /// <summary>An unsigned amount and its icon; weight is a 0-1 status fraction like the rest.</summary>
    internal static string Plain(float amount, string effect) =>
        Colored(Scaled(amount), effect);

    /// <summary>The icon sits inside the colour span: its tag carries tint=1, so it takes the colour too.</summary>
    internal static string Colored(string value, string effect) =>
        EffectColors.Get(effect) + value + " " + StatusIcons.Tag(effect) + "</color>";

    internal const string Arrow = " → ";

    /// <summary>If U+221E ever renders as a box in the HUD font, this is the one place to change.</summary>
    internal const string Infinity = "∞";

    /// <summary>
    /// The statuses a clear-all lists: everything <c>StatusIsCurable</c> allows, minus
    /// <see cref="TooRareToList"/>, minus any status with no icon. Curse and Petrify are
    /// added by callers. Cached per icon build, since icons decide what can be rendered.
    /// </summary>
    internal static IReadOnlyList<string> Clearable => clearable ??= ReadClearable();

    private static string[]? clearable;

    internal static void ForgetClearable() => clearable = null;

    /// <summary>
    /// Curable, but from hazards met rarely, so not worth a line on every clear-all. Thorns
    /// stays named even though the game refuses it anyway, because the reason is ours.
    /// </summary>
    private static readonly CharacterAfflictions.STATUSTYPE[] TooRareToList =
    {
        CharacterAfflictions.STATUSTYPE.Web,
        CharacterAfflictions.STATUSTYPE.FlyTrap,
        CharacterAfflictions.STATUSTYPE.Thorns,
    };

    private static string[] ReadClearable()
    {
        CharacterAfflictions? afflictions = Character.observedCharacter == null
            ? null
            : Character.observedCharacter.refs.afflictions;

        List<string> curable = new();
        foreach (CharacterAfflictions.STATUSTYPE status in
            Enum.GetValues(typeof(CharacterAfflictions.STATUSTYPE)))
        {
            // Curse and Petrify are asked for as excluded; callers add them.
            if (afflictions != null && !afflictions.StatusIsCurable(status, false, false))
            {
                continue;
            }

            if (Array.IndexOf(TooRareToList, status) >= 0)
            {
                continue;
            }

            string name = status.ToString();
            if (StatusIcons.HasIcon(name))
            {
                curable.Add(name);
            }
        }

        return curable.ToArray();
    }

    /// <summary>A distance already in metres. Almost everything in the game is Unity units; prefer <see cref="PeakMetres"/>.</summary>
    internal static string Metres(float value) => Num(value) + "m";

    /// <summary>The fallback is only for a call that lands before the static field is touched.</summary>
    private static float UnityUnitsToMetres =>
        CharacterStats.unitsToMeters > 0f ? CharacterStats.unitsToMeters : 1.6f;

    /// <summary>A distance in Unity units, shown in the metres the altitude readout uses.</summary>
    internal static string PeakMetres(float unityUnits) =>
        Metres(PluginConfig.UnityMetres.Value ? unityUnits : unityUnits * UnityUnitsToMetres);

    internal static string Seconds(float value) => Num(value) + "s";

    /// <summary>"8s ∞ &lt;stamina&gt;". The mark is spaced from the icon so it does not read as one glyph.</summary>
    internal static string InfiniteStamina(float seconds) =>
        EffectColors.Neutral + Seconds(seconds) + "</color> "
        + EffectColors.Get("Extra Stamina") + Infinity + " " + StatusIcons.Tag("Extra Stamina") + "</color>";

    /// <summary>A signed amount in the cream, for figures owned by no one status.</summary>
    private static string Figure(float amount) =>
        EffectColors.White + (amount > 0f ? "+" : "-") + Scaled(Mathf.Abs(amount)) + "</color>";

    /// <summary>"-60 &lt;injury&gt;/&lt;poison&gt;" - one amount shared across several statuses.</summary>
    internal static string SharedBudget(float amount, params string[] statuses)
    {
        if (amount == 0f || statuses.Length == 0)
        {
            return "";
        }

        return Figure(amount) + " " + IconRun(statuses, EffectColors.White + "/</color>");
    }

    /// <summary>The break is made here rather than left to the text box.</summary>
    private const int IconsPerLine = 3;

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

    /// <summary>"-5 &lt;cold&gt; /s" - for an effect with no duration to state.</summary>
    internal static string PerSecond(float amountPerSecond, string effect)
    {
        if (amountPerSecond == 0f)
        {
            return "";
        }

        return Token(amountPerSecond, effect) + EffectColors.Neutral + " /s</color>";
    }

    /// <summary>One "-100 &lt;x&gt;" line per status a clear-all removes.</summary>
    internal static List<EffectLine> ClearedStatuses(bool excludeCurse,
        IEnumerable<CharacterAfflictions.STATUSTYPE>? exclusions)
    {
        List<string> cleared = new();
        foreach (string status in Clearable)
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
            cleared.Add(CharacterAfflictions.STATUSTYPE.Curse.ToString());
        }

        List<EffectLine> lines = new();
        foreach (string status in cleared)
        {
            lines.Add(new EffectLine(Token(-1f, status), Onset.Instant, status, -1f, clears: true));
        }

        return lines;
    }

    internal static string Effect(float amount, string effect)
    {
        if (amount == 0f)
        {
            return "";
        }

        return Token(amount, effect) + "\n";
    }

    /// <summary>
    /// "-35 &lt;injury&gt; / 15s", or "-6 &lt;cold&gt; /s → 360s" where the total runs past a
    /// full bar. The only place this choice is made.
    /// </summary>
    internal static string OverTime(float total, float seconds, string effect)
    {
        if (total == 0f || seconds <= 0f)
        {
            return "";
        }

        if (Mathf.Abs(total) > 1f)
        {
            return PerSecond(total / seconds, effect)
                + EffectColors.Neutral + Arrow + Seconds(seconds) + "</color>";
        }

        return Token(total, effect) + EffectColors.Neutral + " / " + Seconds(seconds) + "</color>";
    }

    /// <summary>An unrecognised type returns nothing rather than guessing.</summary>
    internal static List<EffectLine> Affliction(PeakAffliction? affliction)
    {
        List<EffectLine> lines = new();
        if (affliction != null
            && AfflictionHandlers.TryGetValue(affliction.GetAfflictionType(),
                out Action<PeakAffliction, List<EffectLine>>? describe))
        {
            describe(affliction, lines);
        }

        return lines;
    }

    /// <summary>
    /// Types with no entry are described by the component carrying them (HealAll,
    /// BingBongShield, PoisonOverTime), or reach a player only through the Shroomberry roll,
    /// which "????" withholds (LowGravity, Blind, Numb).
    /// </summary>
    private static readonly Dictionary<PeakAffliction.AfflictionType, Action<PeakAffliction, List<EffectLine>>>
        AfflictionHandlers = new()
    {
        { PeakAffliction.AfflictionType.FasterBoi, DescribeFasterBoi },
        { PeakAffliction.AfflictionType.ClearAllStatus, DescribeClearAll },
        { PeakAffliction.AfflictionType.AddBonusStamina, DescribeAddBonusStamina },
        { PeakAffliction.AfflictionType.InfiniteStamina, DescribeInfiniteStamina },
        { PeakAffliction.AfflictionType.AdjustStatus, DescribeAdjustStatus },
        { PeakAffliction.AfflictionType.DrowsyOverTime, DescribeDrowsyOverTime },
        { PeakAffliction.AfflictionType.ColdOverTime, DescribeColdOverTime },
        { PeakAffliction.AfflictionType.Chaos, DescribeChaos },
        { PeakAffliction.AfflictionType.Invincibility, DescribeInvincibility },
        { PeakAffliction.AfflictionType.RadiateInfiniteStam, DescribeRadiateInfiniteStam },
        { PeakAffliction.AfflictionType.MassSuperJump, DescribeMassSuperJump },
        { PeakAffliction.AfflictionType.Sunscreen, DescribeSunscreen },
    };

    private static void DescribeFasterBoi(PeakAffliction affliction, List<EffectLine> lines)
    {
        // Three stamina icons mean "faster"; the stamina is not infinite here.
        Affliction_FasterBoi effect = (Affliction_FasterBoi)affliction;
        string text = EffectColors.Neutral + Seconds(effect.totalTime) + "</color> "
            + IconRun(new[] { "Extra Stamina", "Extra Stamina", "Extra Stamina" });

        // An effect that lands when the timer runs out is welded on, never a line of its own.
        if (effect.drowsyOnEnd > 0f)
        {
            text += EffectColors.Neutral + Arrow + "</color>" + Token(effect.drowsyOnEnd, "Drowsy");
        }

        lines.Add(new EffectLine(text, Onset.OverTime, "Extra Stamina", 1f));
    }

    private static void DescribeClearAll(PeakAffliction affliction, List<EffectLine> lines)
    {
        Affliction_ClearAllStatus effect = (Affliction_ClearAllStatus)affliction;
        lines.AddRange(ClearedStatuses(effect.excludeCurse, null));
    }

    private static void DescribeAddBonusStamina(PeakAffliction affliction, List<EffectLine> lines)
    {
        Affliction_AddBonusStamina effect = (Affliction_AddBonusStamina)affliction;
        lines.Add(new EffectLine(Token(effect.staminaAmount, "Extra Stamina"),
            Onset.Instant, "Extra Stamina", effect.staminaAmount));
    }

    private static void DescribeInfiniteStamina(PeakAffliction affliction, List<EffectLine> lines)
    {
        // totalTime is the climbing window, the shorter of the two; the one you can rely on.
        Affliction_InfiniteStamina effect = (Affliction_InfiniteStamina)affliction;
        lines.Add(new EffectLine(InfiniteStamina(effect.totalTime),
            Onset.OverTime, "Extra Stamina", 1f));

        if (effect.drowsyAffliction != null)
        {
            lines.AddRange(Affliction(effect.drowsyAffliction));
        }
    }

    private static void DescribeAdjustStatus(PeakAffliction affliction, List<EffectLine> lines)
    {
        Affliction_AdjustStatus effect = (Affliction_AdjustStatus)affliction;
        lines.Add(new EffectLine(Token(effect.statusAmount, effect.statusType.ToString()),
            Onset.Instant, effect.statusType.ToString(), effect.statusAmount));
    }

    private static void DescribeDrowsyOverTime(PeakAffliction affliction, List<EffectLine> lines)
    {
        // The total is exactly rate x time.
        Affliction_AdjustDrowsyOverTime effect = (Affliction_AdjustDrowsyOverTime)affliction;
        string drowsy = OverTime(effect.statusPerSecond * effect.totalTime, effect.totalTime, "Drowsy");
        if (drowsy.Length > 0)
        {
            lines.Add(new EffectLine(drowsy, Onset.OverTime, "Drowsy", effect.statusPerSecond));
        }
    }

    private static void DescribeColdOverTime(PeakAffliction affliction, List<EffectLine> lines)
    {
        // Heat Pack. The total runs past a full bar; OverTime states the rate instead.
        Affliction_AdjustColdOverTime effect = (Affliction_AdjustColdOverTime)affliction;
        string cold = OverTime(effect.statusPerSecond * effect.totalTime, effect.totalTime, "Cold");
        if (cold.Length > 0)
        {
            lines.Add(new EffectLine(cold, Onset.OverTime, "Cold", effect.statusPerSecond));
        }
    }

    private static void DescribeChaos(PeakAffliction affliction, List<EffectLine> lines)
    {
        // Cleared, then an unknown amount handed back; Curse is in both halves.
        foreach (string status in Clearable)
        {
            lines.Add(new EffectLine(Reshuffled(status), Onset.Instant, status, -1f));
        }

        string curse = CharacterAfflictions.STATUSTYPE.Curse.ToString();
        lines.Add(new EffectLine(Reshuffled(curse), Onset.Instant, curse, -1f));
    }

    private static void DescribeInvincibility(PeakAffliction affliction, List<EffectLine> lines)
    {
        lines.Add(new EffectLine(
            EffectColors.Neutral + Seconds(affliction.totalTime) + "</color> "
            + EffectColors.Get("Shield") + StatusIcons.Tag("Shield") + "</color>",
            Onset.OverTime, "Shield", 1f));
    }

    private static void DescribeRadiateInfiniteStam(PeakAffliction affliction, List<EffectLine> lines)
    {
        Affliction_RadiateInfiniteStam effect = (Affliction_RadiateInfiniteStam)affliction;
        lines.Add(new EffectLine(
            InfiniteStamina(effect.totalTime)
            + EffectColors.Neutral + " " + PeakMetres(effect.radius) + "</color>",
            Onset.OverTime, "Extra Stamina", 1f));
    }

    private static void DescribeMassSuperJump(PeakAffliction affliction, List<EffectLine> lines)
    {
        // lowGravAmount is a balloon count; three or more is drawn as the bunch.
        Affliction_MassSuperJump effect = (Affliction_MassSuperJump)affliction;
        string icon = effect.lowGravAmount >= 3 && StatusIcons.HasIcon("FloatBunch") ? "FloatBunch" : "Float";
        lines.Add(new EffectLine(
            EffectColors.Neutral + Seconds(effect.lowGravTime) + "</color> "
            + EffectColors.Get("Float") + StatusIcons.Tag(icon) + "</color>"
            + EffectColors.Neutral + " " + PeakMetres(effect.radius) + "</color>",
            Onset.OverTime, "Float", 1f));
    }

    private static void DescribeSunscreen(PeakAffliction affliction, List<EffectLine> lines)
    {
        // A bare duration; no icon says "sun protection" without misleading.
        Affliction_Sunscreen effect = (Affliction_Sunscreen)affliction;
        lines.Add(new EffectLine(EffectColors.Neutral + Seconds(effect.totalTime) + "</color>",
            Onset.OverTime));
    }

    /// <summary>"-100 &lt;x&gt; → +? &lt;x&gt;".</summary>
    private static string Reshuffled(string status) =>
        Token(-1f, status) + EffectColors.Neutral + Arrow + "</color>"
        + EffectColors.Get(status) + "+? " + StatusIcons.Tag(status) + "</color>";

}
