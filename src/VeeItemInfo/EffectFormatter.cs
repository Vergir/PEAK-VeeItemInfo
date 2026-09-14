using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// <summary>
    /// A duration or distance: one decimal place, trailing zeros trimmed. Invariant culture,
    /// because a comma decimal on a Russian locale would break "2,5 / 8s" into two figures.
    /// </summary>
    internal static string Num(float value) => Fixed(value, 1);

    private static string Fixed(float value, int decimals)
    {
        string text = value.ToString("F" + decimals, CultureInfo.InvariantCulture);
        return text.Contains('.') ? text.TrimEnd('0').TrimEnd('.') : text;
    }

    /// <summary>
    /// A status amount. Stored as a 0-1 fraction; shown on whatever scale Status Scale in
    /// config says a full bar is - 100 by default.
    ///
    /// The decimals follow the scale so the game's step is never rounded away: the 0.025
    /// step is 2.5 at 100, 0.25 at 10, 0.025 at 1, and 5 at 200 with no decimals at all.
    /// </summary>
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
    /// Petrify, in the whole points the game actually gives you, then on the status scale.
    /// The game floors petrify to an int on its 100-point scale before storing it, so a
    /// `petrifyPerUse` of 0.075 is 7, not 7.5; the display scale applies after the floor.
    /// </summary>
    internal static string WholePoints(float fraction) =>
        Scaled(Mathf.Floor(fraction * 100f) / 100f);

    /// <summary>
    /// A signed, coloured amount followed by the status icon - "+30 &lt;flame&gt;". The sign
    /// says which way the status moves; the colour is the status's own, not good-or-bad.
    /// </summary>
    internal static string Token(float amount, string effect) =>
        Colored((amount > 0f ? "+" : "-") + Scaled(Mathf.Abs(amount)), effect);

    /// <summary>
    /// A change over time that may not land at all - "+ 0/20 &lt;poison&gt; / 8s". The sign
    /// leads on its own, then the two outcomes as a slash-pair, then the duration as
    /// <see cref="OverTime"/> writes it. Used for a mushroom that may or may not be the
    /// poisonous one, when the overlay has been told not to tell them apart.
    /// </summary>
    internal static string ConditionalOverTime(float total, float seconds, string effect)
    {
        if (total == 0f || seconds <= 0f)
        {
            return "";
        }

        return Colored((total > 0f ? "+" : "-") + " 0/" + Scaled(Mathf.Abs(total)), effect)
            + EffectColors.Neutral + " / " + Seconds(seconds) + "</color>";
    }

    /// <summary>
    /// An unsigned amount and its icon, for values that have no direction. Takes a 0-1
    /// fraction like every other formatter here - weight is a status the game sets through
    /// SetStatus, so it is on the same scale as the rest and scales the same way.
    /// </summary>
    internal static string Plain(float amount, string effect) =>
        Colored(Scaled(amount), effect);

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
    /// The statuses a clear-all actually removes, in display order. Asked of the game -
    /// <c>StatusIsCurable</c> for every <c>STATUSTYPE</c>, which is the question
    /// <c>ClearAllStatus</c> itself asks - rather than listed, so a status becoming curable is
    /// followed. Two omissions are ours: a status with no icon (its fallback is English) and
    /// <see cref="TooRareToList"/>. Curse and Petrify are added by callers whose own flag says
    /// so. Recomputed whenever the icons are rebuilt, since that decides which entries can be
    /// rendered. Which items clear, and how the two clear-alls differ: docs/internals_game.md,
    /// "Clearing all status".
    /// </summary>
    internal static IReadOnlyList<string> Clearable => clearable ??= ReadClearable();

    private static string[]? clearable;

    /// <summary>Drops the cached list, for a rebuilt icon atlas or a hot reload.</summary>
    internal static void ForgetClearable() => clearable = null;

    /// <summary>
    /// Statuses a clear-all really does remove and the overlay still does not name: they come
    /// from hazards a player meets rarely, so the line would describe something the reader is
    /// not carrying. An editorial choice, stated here rather than smuggled in as a filter;
    /// Thorns stays named even though the game refuses it anyway, because the reason is ours.
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
            // Curse and Petrify are the two the game defers to its caller, and both are
            // handled by the caller here too - so they are asked for as excluded.
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

    /// <summary>
    /// A distance already in PEAK metres. No space before the unit, matching how durations
    /// are written: a number and its unit are one token, so "4.8m" and "8s" read the same way.
    ///
    /// Prefer <see cref="PeakMetres"/> - almost every figure in the game is a Unity unit and
    /// has to be converted first. This overload is for the few places holding a distance that
    /// is already in the player's units.
    /// </summary>
    internal static string Metres(float value) => Num(value) + "m";

    /// <summary>
    /// How many metres a Unity unit is, read from the game: <c>CharacterStats.unitsToMeters</c>
    /// is what turns hip height into the altitude readout. The fallback is only for a call
    /// that lands before the static field is touched.
    /// </summary>
    private static float UnityUnitsToMetres =>
        CharacterStats.unitsToMeters > 0f ? CharacterStats.unitsToMeters : 1.6f;

    /// <summary>
    /// A distance held in Unity units, shown in the metres a player reads; every radius, range
    /// and raycast in the game is in units. Show Unity Meters skips the conversion. The Rope
    /// Cannon's rope length reaches here only when Show Real Rope Length is on.
    /// </summary>
    internal static string PeakMetres(float unityUnits) =>
        Metres(PluginConfig.UnityMetres.Value ? unityUnits : unityUnits * UnityUnitsToMetres);

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
    /// One amount <i>shared across</i> several statuses - "-60 &lt;injury&gt;/&lt;poison&gt;",
    /// 60 points of relief split between them, not 60 off each. A slash between icons is
    /// reserved for this meaning; see docs/design.md, "Reading a line".
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
    /// How many icons fit on one line before it reads as a wall; the break is made here rather
    /// than left to the text box. Three rather than four because Tenacity's six statuses
    /// split evenly that way.
    /// </summary>
    private const int IconsPerLine = 3;

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
    /// "Clear all status", one keyed line per status actually removed - never a pooled
    /// "-100 &lt;seven icons&gt;", which would read as a <see cref="SharedBudget"/>. Leaving the
    /// excluded ones out of the run says which are spared without naming them.
    /// </summary>
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
            // A clear lands the instant the action runs, whichever of the two components
            // delivered it, and -1 is a full bar however much of it you were carrying.
            lines.Add(new EffectLine(Token(-1f, status), Onset.Instant, status, -1f, clears: true));
        }

        return lines;
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

    /// <summary>
    /// A status change spread over time: total and duration ("-35 &lt;injury&gt; / 15s") by
    /// default, or rate and duration ("-6 &lt;cold&gt; /s → 360s") where the total runs past a
    /// full bar and so stops meaning anything. **This is the only place that choice is
    /// made**; picked branch by branch, two items side by side stated the same kind of effect
    /// differently. <see cref="PerSecond"/> alone is for the case with no duration at all.
    /// See docs/design.md, "Reading a line".
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

        // The suffix sits inside a colour tag on purpose; bare, it rendered in TMP's white.
        return Token(total, effect) + EffectColors.Neutral + " / " + Seconds(seconds) + "</color>";
    }

    /// <summary>
    /// Describes an affliction as keyed lines, by looking its type up in
    /// <see cref="AfflictionHandlers"/>. An unrecognised type returns nothing rather than
    /// guessing. Every handler states its own <see cref="Onset"/>: an affliction is not
    /// "timed" just because it arrived as an Affliction object.
    /// </summary>
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
    /// Which method describes which affliction type - the same shape as
    /// <c>ItemDescriptionBuilder.Handlers</c>, for the same reason. Types with no entry are
    /// either described by the component carrying them (HealAll, BingBongShield,
    /// PoisonOverTime), withheld on purpose because they only reach a player through the
    /// Shroomberry roll (LowGravity, Blind, Numb), or dead code and mob-only afflictions - see
    /// docs/design.md, "What is deliberately not shown".
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
        // Three stamina icons for "you move faster", not the infinity mark: the stamina is not
        // infinite here. The run and climb windows differ by climbDelay; the shorter is shown.
        Affliction_FasterBoi effect = (Affliction_FasterBoi)affliction;
        string text = EffectColors.Neutral + Seconds(effect.totalTime) + "</color> "
            + IconRun(new[] { "Extra Stamina", "Extra Stamina", "Extra Stamina" });

        // The drowsiness handed back when the boost ends is welded on with an arrow: an effect
        // that lands when a timer runs out is never a line of its own.
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
        // Where climbDelay grants a longer running window than a climbing one, the shorter
        // figure is shown: it is the one you can rely on whatever you are doing.
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
        // UpdateEffect applies statusPerSecond * deltaTime every frame, so the total is
        // exactly rate x time - not rounded to 2.5s, as it once was.
        Affliction_AdjustDrowsyOverTime effect = (Affliction_AdjustDrowsyOverTime)affliction;
        string drowsy = OverTime(effect.statusPerSecond * effect.totalTime, effect.totalTime, "Drowsy");
        if (drowsy.Length > 0)
        {
            lines.Add(new EffectLine(drowsy, Onset.OverTime, "Drowsy", effect.statusPerSecond));
        }
    }

    private static void DescribeColdOverTime(PeakAffliction affliction, List<EffectLine> lines)
    {
        // Heat Pack. The total is rate x time, which here runs past a full bar; OverTime
        // sees that and states the rate instead - the branch does not choose.
        Affliction_AdjustColdOverTime effect = (Affliction_AdjustColdOverTime)affliction;
        string cold = OverTime(effect.statusPerSecond * effect.totalTime, effect.totalTime, "Cold");
        if (cold.Length > 0)
        {
            lines.Add(new EffectLine(cold, Onset.OverTime, "Cold", effect.statusPerSecond));
        }
    }

    private static void DescribeChaos(PeakAffliction affliction, List<EffectLine> lines)
    {
        // Cleared, then an unknown amount handed straight back - one line per status, so the
        // randomisation reads as one shuffle rather than two unrelated effects. OnApplied
        // clears with excludeCurse false and refills Curse too, so Curse is in both halves.
        foreach (string status in Clearable)
        {
            lines.Add(new EffectLine(Reshuffled(status), Onset.Instant, status, -1f));
        }

        string curse = CharacterAfflictions.STATUSTYPE.Curse.ToString();
        lines.Add(new EffectLine(Reshuffled(curse), Onset.Instant, curse, -1f));
    }

    private static void DescribeInvincibility(PeakAffliction affliction, List<EffectLine> lines)
    {
        // Fortified Milk and the healing amulet both grant this.
        lines.Add(new EffectLine(
            EffectColors.Neutral + Seconds(affliction.totalTime) + "</color> "
            + EffectColors.Get("Shield") + StatusIcons.Tag("Shield") + "</color>",
            Onset.OverTime, "Shield", 1f));
    }

    private static void DescribeRadiateInfiniteStam(PeakAffliction affliction, List<EffectLine> lines)
    {
        // Scout's Ambition. Infinite stamina for everyone standing close enough, so the
        // radius is as much the point as the duration.
        Affliction_RadiateInfiniteStam effect = (Affliction_RadiateInfiniteStam)affliction;
        lines.Add(new EffectLine(
            InfiniteStamina(effect.totalTime)
            + EffectColors.Neutral + " " + PeakMetres(effect.radius) + "</color>",
            Onset.OverTime, "Extra Stamina", 1f));
    }

    private static void DescribeMassSuperJump(PeakAffliction affliction, List<EffectLine> lines)
    {
        // Scout's Initiative drops gravity rather than granting speed, so the balloon is the
        // symbol. lowGravAmount is a balloon count; three or more is drawn as the bunch.
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
        // Just how long it lasts. No icon of its own: two candidates were tried and dropped
        // (docs/design.md, "Reading a line").
        Affliction_Sunscreen effect = (Affliction_Sunscreen)affliction;
        lines.Add(new EffectLine(EffectColors.Neutral + Seconds(effect.totalTime) + "</color>",
            Onset.OverTime));
    }

    /// <summary>
    /// One status wiped and an unknown amount handed straight back, as a chaos berry does.
    /// </summary>
    private static string Reshuffled(string status) =>
        Token(-1f, status) + EffectColors.Neutral + Arrow + "</color>"
        + EffectColors.Get(status) + "+? " + StatusIcons.Tag(status) + "</color>";

}
