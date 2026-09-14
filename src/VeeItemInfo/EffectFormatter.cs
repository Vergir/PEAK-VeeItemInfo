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
    ///
    /// It is the one status that is not continuous. `CharacterData.petrifyAmount` is an
    /// `int`, and every route into it - `AddStatus`, `SetStatus`, `SubtractStatus` - runs
    /// `Mathf.FloorToInt(amount * 100f)` before calling `AddPetrify`. So a `petrifyPerUse` of
    /// `0.075` is **7**, and <see cref="Scaled"/> reporting 7.5 overstated every amulet whose
    /// cost was not a whole percent.
    ///
    /// Floor, not round, because that is what the game does: 7.9 points is still 7. The
    /// floor is to whole points of the game's own 100-point scale; the display scale applies
    /// after, so at a scale of 10 those 7 points read 0.7 - petrify lands on the same bar as
    /// everything else and has to read on the same scale.
    /// </summary>
    internal static string WholePoints(float fraction) =>
        Scaled(Mathf.Floor(fraction * 100f) / 100f);

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
    /// The statuses a clear-all actually removes, in display order.
    ///
    /// Not every status, and not the display order's own list - the two were one array
    /// until this split, and it was quietly lying. **Thorns was in the old list**, so every
    /// clear-all item in the game promised to strip 100 thorns it has never once removed.
    ///
    /// <b>Who consults this</b>, since a derived list makes that unanswerable from the source:
    /// <c>Action_ClearAllStatus</c>, which in 2.1.a is on <b>Napberry</b> and the <b>Book of
    /// Bones</b> and nothing else; <c>RitualDaggerFeedBehavior</c>, which clears whoever it is
    /// fed to; and the <c>Chaos</c> affliction below. A walk of all 194 item prefabs settled
    /// the first of those - an older note here named Cure-All, Pandora's Lunchbox and the
    /// Blowgun dart as clear-all items and all three were wrong. Cure-All in particular carries
    /// <c>Action_AddOrRemoveThorns</c> with a count of <c>-5</c>, so it really does take thorns
    /// off, which is presumably how it got mistaken for one.
    ///
    /// Napberry's own <c>otherExclusions</c> is <c>[Crab]</c>, which <c>StatusIsCurable</c>
    /// already refuses - a prefab restating a rule the game enforces anyway, and a reason to
    /// ask the game rather than trust the list beside it.
    ///
    /// <b>Asked of the game rather than listed.</b> <c>ClearAllStatus</c> keeps no list: it
    /// walks every <c>STATUSTYPE</c> and asks <c>StatusIsCurable</c>, which refuses Crab,
    /// Weight, Thorns and Arrow outright and defers Curse and Petrify to its callers. Asking
    /// the same question means a patch that makes a status curable, or stops one being so, is
    /// followed rather than described from memory. The four names that used to be written here
    /// are now nobody's opinion.
    ///
    /// Two judgements are left, and both are ours rather than the game's. A status with no
    /// icon is skipped, because <see cref="StatusIcons.Tag"/> falls back to the name in
    /// capitals and listing it would put English back on the line. And
    /// <see cref="TooRareToList"/> is left out on purpose - see there.
    ///
    /// Curse is not here because almost every clear-all excludes it; callers add it when
    /// their own flag says it is included. Petrify likewise, and it sorts last anyway.
    ///
    /// Recomputed whenever the icons are rebuilt, since that is what decides which entries
    /// can be rendered at all.
    /// </summary>
    internal static IReadOnlyList<string> Clearable => clearable ??= ReadClearable();

    private static string[]? clearable;

    /// <summary>Drops the cached list, for a rebuilt icon atlas or a hot reload.</summary>
    internal static void ForgetClearable() => clearable = null;

    /// <summary>
    /// Statuses a clear-all really does remove, and the overlay still does not name.
    ///
    /// Web, FlyTrap and Thorns come from hazards a player meets rarely, so on almost every
    /// clear-all item they are two or three lines describing something the reader is not
    /// carrying and will not be. The list is an editorial choice about what is worth a line,
    /// which is why it is stated here rather than smuggled in as a filter.
    ///
    /// Thorns is already refused by <c>StatusIsCurable</c>, so today this changes nothing for
    /// it. It stays named because the reason to leave it out is ours and would outlive a patch
    /// that made thorns curable.
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
    /// How many metres a Unity unit is, read from the game rather than assumed.
    ///
    /// <c>CharacterStats.unitsToMeters</c> is what turns your hip height into the altitude the
    /// end screen shows, so it is the game's own answer and not a convention borrowed from the
    /// wiki. It is 1.6 in 2.1.a, which is what the amulet radii were checked against, and
    /// reading it means a rebalance moves the whole overlay with it.
    ///
    /// The fallback is only for a call that lands before the field is touched; a static field
    /// with an initialiser is set by the type initialiser, so in practice it is always there.
    /// </summary>
    private static float UnityUnitsToMetres =>
        CharacterStats.unitsToMeters > 0f ? CharacterStats.unitsToMeters : 1.6f;

    /// <summary>
    /// A distance held in Unity units, shown in the metres a player reads. Radii, ranges and
    /// raycast lengths in the game are all Unity units, and printing one with an "m" after it
    /// understates the distance by well over a third.
    ///
    /// Show Unity Meters in config skips the conversion, for players who think in the units
    /// every other measuring mod reports. The Rope Cannon's rope length reaches here only
    /// when Show Real Rope Length is on; otherwise it prints the spool's own figure through
    /// <see cref="Metres"/>, which this setting leaves alone.
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
    /// meaning 60 points of relief split between them, not 60 off each.
    ///
    /// Slashes are deliberately reserved for this one meaning. Only the healing amulet works this way in
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
    /// How many icons fit on one line before it starts to read as a wall. Past this the eye
    /// stops counting them and the overlay wraps at an arbitrary point instead of a chosen
    /// one, so the break is made here rather than left to the text box.
    ///
    /// Three rather than four: Scout's Tenacity heals six statuses, and four broke it into
    /// an uneven 4 and 2 where three gives two even rows.
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
    /// "Clear all status", one keyed line per status actually removed. Leaving the excluded
    /// ones out of the run says which are spared without naming them.
    ///
    /// Collapsing them into a shared "-100 &lt;seven icons&gt;" line was tried and dropped: it
    /// reads as a single pooled effect, which is what <see cref="SharedBudget"/> means, and
    /// clearing all status is emphatically not that. Every status loses its full 100.
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
    /// A status change spread over time, in whichever of the two forms tells the truth.
    /// **This is the only place that choice is made.** It used to be picked branch by
    /// branch, which is how Heat Pack ended up stating a total twenty times the scale
    /// maximum while the item beside it stated a rate.
    ///
    /// **Total and duration** - "-35 &lt;injury&gt; / 15s" - is the default, and the form
    /// worth having: it answers "what does this do to me", and a duration you can wait out.
    ///
    /// **Rate and duration** - "-6 &lt;cold&gt; /s → 360s" - where the total runs past a full
    /// bar and so describes arithmetic rather than anything a player can feel. A Heat Pack's
    /// 6 a second across 360 seconds multiplies out to 2160 on a scale that stops at 100;
    /// the rate is what you get and the duration is how long you keep getting it.
    ///
    /// **Rate alone** is <see cref="PerSecond"/>, called directly by the one case with no
    /// duration to state at all: a lantern warms you until its fuel runs out, and stating a
    /// total would make a nearly-spent lantern read differently from a full one.
    ///
    /// The threshold is a full bar, because that is the point past which a figure stops
    /// meaning anything - you cannot be more than 100 cold.
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

        // The suffix sits inside a colour tag on purpose. Left bare it rendered in TMP's
        // default pure white, brighter than the figure in front of it - the known case in
        // the white-leak audit.
        return Token(total, effect) + EffectColors.Neutral + " / " + Seconds(seconds) + "</color>";
    }

    /// <summary>
    /// Describes an affliction as keyed lines, by looking its type up in
    /// <see cref="AfflictionHandlers"/>. An unrecognised type returns nothing rather than
    /// guessing.
    ///
    /// Every handler states its own <see cref="Onset"/>, and that is the point of the split:
    /// afflictions used to be posted wholesale into the "timed" bucket, so
    /// Affliction_ClearAllStatus - which fires in OnApplied and is as instant as anything in
    /// the game - sorted below every instant line, while the identical Action_ClearAllStatus
    /// sorted above them.
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
    /// <c>ItemDescriptionBuilder.Handlers</c>, for the same reason. This was a chain of
    /// twelve <c>else if</c> tests, and a type with no branch fell off the end into an empty
    /// list with nothing to say so. As a table, what is and is not described is one list to
    /// read rather than a chain to count.
    ///
    /// Types with no entry, each checked against the 2.1.a item database:
    /// <list type="bullet">
    /// <item><c>HealAll</c>, <c>BingBongShield</c>, <c>PoisonOverTime</c> - described by the
    /// component carrying them (<c>Action_HealingGem</c>, <c>BingBongShieldWhileHolding</c>,
    /// <c>Action_InflictPoison</c> and the Scorpion).</item>
    /// <item><c>LowGravity</c>, <c>Blind</c>, <c>Numb</c> - reach a player only from the
    /// Shroomberry roll, which <c>????</c> withholds on purpose. Mandrake's numbness comes
    /// through <c>Action_Numb</c>.</item>
    /// <item><c>Glowing</c> - Mushroom Glow, which is not in the live game.</item>
    /// <item><c>ClimbingChalk</c>, <c>Exhausted</c>, <c>AdjustStatusOverTime</c> - read by
    /// nothing in the game.</item>
    /// <item><c>ZombieBite</c>, <c>PreventPoisonHealing</c>, <c>NoHunger</c> - mobs and
    /// campfires, never an item.</item>
    /// </list>
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
        // Three stamina icons for "you move faster", deliberately not the infinity mark:
        // the stamina is not infinite here, only the movement is quicker, and reusing
        // the infinity mark would say the wrong thing. The run and climb windows differ
        // by climbDelay; the shorter one is the honest figure to show.
        Affliction_FasterBoi effect = (Affliction_FasterBoi)affliction;
        string text = EffectColors.Neutral + Seconds(effect.totalTime) + "</color> "
            + IconRun(new[] { "Extra Stamina", "Extra Stamina", "Extra Stamina" });

        // The drowsiness Energy Drink hands back when its boost ends belongs on this
        // line rather than on one of its own. It is a single statement - faster now,
        // sleepy afterwards - and the arrow is the word "afterwards". An effect that
        // lands when a timer runs out is never a line of its own.
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
        // Was "GAIN 100 EXTRA STAMINA" - the last piece of prose left on a common path.
        Affliction_AddBonusStamina effect = (Affliction_AddBonusStamina)affliction;
        lines.Add(new EffectLine(Token(effect.staminaAmount, "Extra Stamina"),
            Onset.Instant, "Extra Stamina", effect.staminaAmount));
    }

    private static void DescribeInfiniteStamina(PeakAffliction affliction, List<EffectLine> lines)
    {
        // A duration, the infinity mark, and the stamina icon - flush, because the mark
        // qualifies the icon rather than standing on its own. Where climbDelay grants a
        // longer running window than a climbing one, the shorter figure is shown: it is
        // the one you can rely on whatever you are doing.
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
        // Affliction_AdjustDrowsyOverTime.UpdateEffect applies statusPerSecond * deltaTime
        // every frame, so the total is exactly statusPerSecond * totalTime (verified
        // against 2.1.a). The original rounded to multiples of 2.5 for no reason, which
        // could be off by up to 1.25.
        Affliction_AdjustDrowsyOverTime effect = (Affliction_AdjustDrowsyOverTime)affliction;
        string drowsy = OverTime(effect.statusPerSecond * effect.totalTime, effect.totalTime, "Drowsy");
        if (drowsy.Length > 0)
        {
            lines.Add(new EffectLine(drowsy, Onset.OverTime, "Drowsy", effect.statusPerSecond));
        }
    }

    private static void DescribeColdOverTime(PeakAffliction affliction, List<EffectLine> lines)
    {
        // Heat Pack, and the last prose in the mod - it used to read
        // "GAIN/REMOVE {n} COLD OVER {n}s". The sign says which way the status moves,
        // the icon says what moves, and the duration says how long it keeps moving.
        //
        // UpdateEffect applies statusPerSecond * deltaTime every frame, so the total is
        // the rate times the time - which here is 2160 on a scale that stops at 100.
        // OverTime sees that and states the rate instead; the branch does not choose.
        Affliction_AdjustColdOverTime effect = (Affliction_AdjustColdOverTime)affliction;
        string cold = OverTime(effect.statusPerSecond * effect.totalTime, effect.totalTime, "Cold");
        if (cold.Length > 0)
        {
            lines.Add(new EffectLine(cold, Onset.OverTime, "Cold", effect.statusPerSecond));
        }
    }

    private static void DescribeChaos(PeakAffliction affliction, List<EffectLine> lines)
    {
        // Cleared, then an unknown amount handed straight back. Showing the pair on one
        // line per status is what makes the randomisation legible - two separate blocks
        // read as two unrelated effects rather than one shuffle.
        //
        // OnApplied calls ClearAllStatus(excludeCurse: false) and then redistributes over
        // its own list of eight: Cold, Hot, Poison, Drowsy, Injury, Hunger, Spores and
        // Curse. So Curse is both cleared and refillable, and Thorns is in neither half.
        // Thorns was in this loop until the clearable set was split out, carrying a
        // hand-written exception that said the randomiser could not hand thorns back;
        // the exception is gone because the status never belonged here at all.
        foreach (string status in Clearable)
        {
            lines.Add(new EffectLine(Reshuffled(status), Onset.Instant, status, -1f));
        }

        string curse = CharacterAfflictions.STATUSTYPE.Curse.ToString();
        lines.Add(new EffectLine(Reshuffled(curse), Onset.Instant, curse, -1f));
    }

    private static void DescribeInvincibility(PeakAffliction affliction, List<EffectLine> lines)
    {
        // Fortified Milk and the healing amulet both grant this. It was going unreported
        // entirely - there was no branch for it, so the shield line simply never appeared.
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
        // Scout's Initiative. It does not grant speed - it launches everyone nearby and
        // drops their gravity, so the balloon is the right symbol for what you feel.
        //
        // lowGravAmount feeds the same float-and-jump formula as the number of balloons you
        // are holding, so it is a balloon count. Three or more is drawn as the bunch - the
        // game's own second picture of the same lift, and what the Initiative hands out.
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
        // Just how long it lasts. Naming the biome it protects you in was the only
        // English this line ever had, and the item's own icon on the line above already
        // says what it is.
        //
        // No icon of its own, and two candidates were tried and dropped. Heat behind a
        // shield overpromises: AddSunHeat is skipped for anyone wearing sunscreen, so
        // this is immunity to the *sun*, and a campfire will still cook you. The parasol
        // - the other half of that same check - is accurate but reads as a different
        // item rather than as this one's duration.
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
