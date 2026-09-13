using System;
using System.Collections.Generic;
using UnityEngine;

// There is a second, unrelated Affliction type in the global namespace. Alias the one we
// mean so it can never be resolved against that by accident.
using PeakAffliction = Peak.Afflictions.Affliction;

namespace VeeItemInfo;

/// <summary>
/// Builds the overlay text for an item by scanning every component on it.
///
/// Each branch decides only *what* it has to say and *which section* it belongs in;
/// <see cref="DescriptionLayout"/> owns the ordering and the spacing. Four sections: Custom
/// for facts that are not status changes, Effects for every status change whether it lands
/// on you or on everyone nearby, Cooking for the campfire hint, Weight last.
///
/// The goal is a display that needs no translation, and as of the v1 pass that is nearly
/// true: numbers, signs and the game's own icons, with no English left in the common paths.
/// Where a fact has no symbol yet it is omitted rather than described in words.
///
/// Note the component chain matches with GetType() == typeof(T), which is exact - a
/// subclass will not match. That is the most likely reason for an item silently losing its
/// description after a game update. Amulets are the exception and use 'is', because they
/// all derive from AmuletBase.
/// </summary>
internal static class ItemDescriptionBuilder
{
    /// <summary>
    /// What Affliction_HealAll treats, in its own order - read from the affliction rather
    /// than typed out. maxHealing is a budget shared across all of them rather than an
    /// allowance for each, and the order matters because the first one ranks the line.
    ///
    /// <c>statusesToHeal</c> is private on the game's side, which the publicizer settles;
    /// naming it means a patch that adds a seventh status, drops one, or reorders them moves
    /// the overlay with it, and a patch that renames the field breaks this build rather than
    /// leaving six hardcoded names quietly describing the wrong item.
    /// </summary>
    private static readonly string[] HealAllStatuses = ReadHealAllStatuses();

    private static string[] ReadHealAllStatuses()
    {
        CharacterAfflictions.STATUSTYPE[] healed = Peak.Afflictions.Affliction_HealAll.statusesToHeal;
        string[] names = new string[healed.Length];
        for (int i = 0; i < healed.Length; i++)
        {
            names[i] = healed[i].ToString();
        }

        return names;
    }

    internal static string Build(Item item)
    {
        GameObject itemGameObj = item.gameObject;
        Component[] itemComponents = itemGameObj.GetComponents(typeof(Component));
        DescriptionLayout layout = new();

        // Effect lines are collected rather than added straight to the layout, because
        // where a line belongs is not something a branch can know. Each states what it means
        // - when it lands, which status it moves, which way - and EffectOrder places it.
        //
        // Actions flagged OnConsumed only fire when the item is eaten or drunk, which means
        // an item with no Action_Consume never runs them at all. Cooking an amulet adds an
        // Action_GiveExtraStamina through ItemCooking.ChangeStatsCooked regardless, so a
        // cooked Scout's Ambition was advertising +10 stamina it can never hand out.
        bool consumable = CookingHint.IsConsumable(itemGameObj);

        List<EffectLine> effects = new();

        // Item.CarryWeight, not the serialized carryWeight field: the property is what
        // UpdateWeight adds up, and it applies the ascent modifier itself. This used to
        // reimplement it as "carryWeight + itemWeightModifier when that is positive", which
        // got the lighter-items setting wrong - a modifier of -1 makes CarryWeight zero
        // outright rather than one less, so weightless items were still showing their weight.
        //
        // UpdateWeight then does SetStatus(Weight, STATUS_INCREMENT * total), so weight is a
        // status fraction like any other and the display scaling belongs to the formatter.
        // The 2.5 that used to be here was that same arithmetic already carried out.
        float weight = item.CarryWeight * GameValues.StatusStep;
        // Weight is a property of the item, not a change to your status, so it carries no
        // sign - just the number and the icon.
        layout.Add(Block.Weight, EffectFormatter.Plain(weight, "Weight"));

        // Each component is looked up rather than tested against in turn - see Handlers.
        Parts parts = new(layout, effects, item, consumable);

        for (int i = 0; i < itemComponents.Length; i++)
        {
            // Cooking switches actions off rather than removing them -
            // CookingBehavior_DisableScripts sets enabled=false on the components it ruins.
            // Reading a disabled component is how a cooked poisonous berry kept advertising
            // poison it no longer inflicts.
            // A missing script serializes as a null entry, and Stone ships one. Reading its
            // type threw, which blanked the whole overlay rather than one line.
            if (itemComponents[i] == null)
            {
                continue;
            }

            if (itemComponents[i] is Behaviour behaviour && !behaviour.enabled)
            {
                continue;
            }

            // An action flagged OnConsumed fires only when the item is eaten, so on an item
            // nobody can eat it never fires. Cooking adds such actions to anything - a cooked
            // amulet was advertising stamina it can never hand out. One test here, rather
            // than in whichever three handlers happened to need it.
            if (!consumable && itemComponents[i] is ItemAction action && action.OnConsumed)
            {
                continue;
            }

            Action<Component, Parts>? describe = HandlerFor(itemComponents[i].GetType());
            if (describe != null)
            {
                parts.Source = i;
                describe(itemComponents[i], parts);
            }
        }

        // The safe half of a mushroom pair, told not to give itself away: it shows the poison
        // its twin carries as a coin flip, exactly as the twin does. Zero on the roll is the
        // truth about this one.
        if (PluginConfig.HidePoisonTwins.Value && itemGameObj.GetComponent<Action_InflictPoison>() == null)
        {
            Action_InflictPoison? twin = PoisonousTwin(item);
            if (twin != null)
            {
                Collect(effects, itemComponents.Length,
                    EffectFormatter.ConditionalOverTime(twin.poisonPerSecond * twin.inflictionTime,
                        twin.inflictionTime, "Poison"),
                    Onset.OverTime, "Poison", twin.poisonPerSecond);
            }
        }

        EmitEffects(layout, effects);

        // The cooking hint is decided here rather than from the component loop, and from
        // the one ItemCooking the game itself uses: Item.Awake does GetOrAddComponent, which
        // is GetComponent - the first - so every live item has one and a prefab without one
        // (most foods) behaves as the default. The Infinite Rescue Claw carries two, a
        // cannot-be-cooked one first and a wreck-on-cook one second; the game reads the first
        // and the item cannot be cooked, and describing the second promised a wreck that
        // never happens. A disabled one is a cooked-off script and says nothing, like any
        // other disabled component.
        ItemCooking? cooking = itemGameObj.GetComponent<ItemCooking>();
        if (cooking == null || cooking.enabled)
        {
            layout.Add(Block.Cooking, CookingHint.Describe(itemGameObj, cooking));
        }

        return layout.Render();
    }

    /// <summary>
    /// Which method describes which component.
    ///
    /// This replaced a chain of thirty-one `if / else if` tests that each asked
    /// `GetType() == typeof(T)`. That test is exact, so a **subclass matched nothing at
    /// all** and the component was described by no one - silently, with no error and no log
    /// line, just a shorter overlay. It had already cost real data twice:
    /// `Action_SuperJumpAmulet` derives from `Action_ApplyAffliction` and its affliction was
    /// read by nobody, and `CactusBall` derives from `StickyItemComponent`. `ScoutEffigy` and
    /// `CheckpointConstructable` derive from `Constructable` and are the cases still standing.
    ///
    /// Swapping the tests for `is` would have been worse, not better: a Scout's Ambition is
    /// both an `Action_SuperJumpAmulet` and an `Action_ApplyAffliction`, so whichever branch
    /// sat higher in the file would have won. Correctness would have depended on the order
    /// thirty-one branches happened to be written in, which nothing enforces and nobody can
    /// see.
    ///
    /// Looking the type up and walking to its base settles both. The most derived entry wins
    /// because it is found first, source order means nothing, and a subclass with no entry of
    /// its own inherits its base's description rather than vanishing.
    ///
    /// Deliberately absent, each for a reason worth keeping:
    ///
    /// <list type="bullet">
    /// <item><c>Action_ReduceUses</c> - the "{n} USES" label was dropped outright.</item>
    /// <item><c>Action_WarpToBiome</c> - it does not warp you to a biome at all; it teleports
    /// you to wherever the thrown fungus lands, so the old "WARP TO &lt;SEGMENT&gt;" was wrong
    /// as well as wordy, and there is no symbol for the real behaviour yet.</item>
    /// <item><c>CactusBall</c> - the throw-charge threshold has no agreed symbol. The thorns
    /// it inflicts come through <c>StickyItemComponent</c>, which it derives from - and which
    /// it reaches by the base walk described above.</item>
    /// <item><c>Action_SacrificeFriend</c> - it kills whoever the dagger is *fed to*, never
    /// the holder. The dagger carries no <c>Action_Consume</c>, so there is no way to use it
    /// on yourself; the only path to RunAction is <c>RitualDaggerFeedBehavior</c> calling
    /// ConsumeDelayed once the item has changed hands. The "???" mark means "the worst thing
    /// happens to you", so it said the wrong thing here, and there is no symbol yet for a
    /// death that lands on someone else.</item>
    /// <item><c>Action_BecomeSkeleton</c> - the Book of Bones, and it does not add or remove
    /// anything: <c>RunAction</c> is one line flipping <c>data.isSkeleton</c>. Being a skeleton
    /// has no icon and is not a <c>STATUSTYPE</c>, so there is nothing to draw and a word is
    /// not an option. **It is still felt in the lines around it**: the item's <c>Curse +50</c>
    /// carries <c>ifSkeleton</c>, and this component runs first, so the flag reads as "when you
    /// turn into one" rather than "while you are one". That is why the curse pair renders
    /// <c>0/+50</c> and <c>-25</c> - net +25 going in, -25 coming back.</item>
    /// <item><c>Action_ConsumeAndSpawn</c> - the four Berrynanas and nothing else in 2.1.a:
    /// eating one leaves you holding its own coloured peel. Judged not worth a line.
    /// **Not a gap** - the peel icons are packed regardless by
    /// <c>StatusIcons.AddTransformationIcons</c>, which asks the components rather than
    /// naming items, so what is decided here is what to *say*, not what is reachable. The
    /// other half of that pair, <c>CookingBehavior_ReplaceItem</c> turning a cooked Frog into
    /// FrogLegs, *is* wanted and belongs to the cooking hint.</item>
    /// </list>
    /// </summary>
    private static readonly Dictionary<Type, Action<Component, Parts>> Handlers = new()
    {
        { typeof(Action_RestoreHunger), DescribeRestoreHunger },
        { typeof(Action_GiveExtraStamina), DescribeGiveExtraStamina },
        { typeof(Action_InflictPoison), DescribeInflictPoison },
        { typeof(Action_AddOrRemoveThorns), DescribeAddOrRemoveThorns },
        { typeof(Action_ModifyStatus), DescribeModifyStatus },
        { typeof(Action_ApplyAffliction), DescribeApplyAffliction },
        { typeof(Action_Numb), DescribeNumb },
        { typeof(Action_Die), DescribeDie },
        { typeof(Peak.RitualDaggerFeedBehavior), DescribeRitualDaggerFeedBehavior },
        { typeof(Action_RandomMushroomEffect), DescribeRandomMushroomEffect },
        { typeof(Action_ClearAllStatus), DescribeClearAllStatus },
        { typeof(Action_ApplyMassAffliction), DescribeApplyMassAffliction },
        { typeof(Action_RaycastDart), DescribeRaycastDart },
        { typeof(Lantern), DescribeLanternItem },
        // A lit candle keeps a StatusField switched on exactly as a lantern does - it
        // removes drowsiness within its reach - so it reads through the same walk.
        { typeof(Candle), DescribeLanternItem },
        { typeof(Constructable), DescribeConstructable },
        { typeof(RopeShooter), DescribeRopeShooter },
        { typeof(VineShooter), DescribeVineShooter },
        { typeof(MagicBean), DescribeMagicBean },
        { typeof(ShelfShroom), DescribeShelfShroom },
        { typeof(Breakable), DescribeBreakable },
        { typeof(Action_MoraleBoost), DescribeMoraleBoost },
        { typeof(Dynamite), DescribeDynamite },
        { typeof(Action_Spawn), DescribeSpawn },
        { typeof(Scorpion), DescribeScorpion },
        { typeof(StickyItemComponent), DescribeStickyItemComponent },
        { typeof(BingBongShieldWhileHolding), DescribeBingBongShieldWhileHolding },
        { typeof(Peak.Action_HealingGem), DescribeHealingGem },
        { typeof(Peak.Action_CloneSelectedItem), DescribeCloneSelectedItem },
        { typeof(Peak.Action_SuperJumpAmulet), DescribeSuperJumpAmulet },
        // ItemCooking is deliberately absent: the cooking hint is decided in Build after
        // every handler has run, because it depends on what they described.
    };

    /// <summary>
    /// Cached answers from <see cref="Handlers"/>, including the misses. Build runs on every
    /// equip and on the poll, and most components on an item - the rigidbody, the photon
    /// view, the particles - will never have a handler; walking their base chain to find that
    /// out again each time is work with a known answer.
    /// </summary>
    private static readonly Dictionary<Type, Action<Component, Parts>?> Resolved = new();

    /// <summary>
    /// The handler for a component's own type, or the nearest one above it. Null where
    /// nothing in the chain is described, which is the common case.
    /// </summary>
    private static Action<Component, Parts>? HandlerFor(Type type)
    {
        if (Resolved.TryGetValue(type, out Action<Component, Parts>? cached))
        {
            return cached;
        }

        Action<Component, Parts>? found = null;
        for (Type? current = type; current != null; current = current.BaseType)
        {
            if (Handlers.TryGetValue(current, out Action<Component, Parts>? handler))
            {
                found = handler;
                break;
            }
        }

        Resolved[type] = found;
        return found;
    }

    /// <summary>
    /// The state one description is assembled into, handed to every handler so none of them
    /// has to know where its line ends up.
    /// </summary>
    private sealed class Parts
    {
        internal Parts(DescriptionLayout layout, List<EffectLine> effects, Item entity,
            bool consumable)
        {
            Layout = layout;
            Effects = effects;
            Entity = entity;
            Item = entity.gameObject;
            Consumable = consumable;
        }

        /// <summary>The item component itself, for the branch that looks up its twin.</summary>
        internal Item Entity { get; }

        internal DescriptionLayout Layout { get; }

        internal List<EffectLine> Effects { get; }

        /// <summary>The item itself, for the handful of branches that read its children.</summary>
        internal GameObject Item { get; }

        /// <summary>
        /// Whether the item can be eaten at all. An action flagged OnConsumed never runs on an
        /// item with no Action_Consume, and cooking adds an Action_GiveExtraStamina to
        /// anything - a cooked Scout's Ambition was advertising stamina it can never hand out.
        /// </summary>
        internal bool Consumable { get; }

        /// <summary>
        /// Index of the component being described, so <see cref="EffectOrder"/> has a stable
        /// last tiebreak.
        /// </summary>
        internal int Source { get; set; }
    }

    /// <summary>
    /// Keeps a finished line, with the keys <see cref="EffectOrder"/> sorts it by. Anything
    /// empty is dropped here so the branches stay free of guards.
    /// </summary>
    private static void Collect(List<EffectLine> into, int source, string? text, Onset onset,
        string status = "", float amount = 0f)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        string trimmed = text!.Trim('\n');
        if (trimmed.Length > 0)
        {
            into.Add(new EffectLine(trimmed, onset, status, amount, source));
        }
    }

    /// <summary>
    /// Keeps lines that already carry their own keys - anything routed through
    /// <see cref="EffectFormatter.Affliction"/> - stamping the component they came from so
    /// the final tiebreak has something to hold on to.
    /// </summary>
    private static void Collect(List<EffectLine> into, int source, List<EffectLine> lines)
    {
        foreach (EffectLine line in lines)
        {
            into.Add(line.WithSource(source));
        }
    }

    /// <summary>
    /// Sorts the collected lines and writes them into the Effects section.
    ///
    /// Every ordering decision the overlay makes lives in <see cref="EffectOrder"/>; this
    /// only carries the result across. What used to be here was three lists drained in
    /// sequence, where the position of a line inside each was whatever order its component
    /// happened to sit on the prefab.
    /// </summary>
    private static void EmitEffects(DescriptionLayout layout, List<EffectLine> effects)
    {
        EffectOrder.Sort(effects);

        foreach (EffectLine line in effects)
        {
            layout.Add(Block.Effects, line.Text);
        }
    }

    /// <summary>
    /// A Shroomberry: what this berry is worth in stamina, and four question marks for the
    /// effect, coloured by whether this run rolled a good one or a bad one for it.
    ///
    /// A berry colour's valence <i>is</i> stable across runs, even though the effects
    /// themselves are shuffled every time. GenerateEffectList deals the slots in order and
    /// spends its quotas first: the first minGoodEffects slots are drawn from GoodEffects and
    /// the next minBadEffects from BadEffects, only then falling back to a free choice. So a
    /// berry whose mushroomTypeIndex sits in the guaranteed-good range is always good, one in
    /// the guaranteed-bad range is always bad, and one past both quotas is a genuine coin
    /// flip. That is exactly the red/yellow-good, green/blue-bad, purple-either pattern
    /// players report.
    ///
    /// Reading it live gets that right without hardcoding a colour per berry, and keeps
    /// working if the quotas ever change. Falls back to neutral when MushroomManager is not
    /// up yet, which is honest: unknown rather than guessed.
    /// </summary>
    /// <summary>
    /// The most stamina a Shroomberry can carry, as a 0-1 fraction: 15 display units.
    ///
    /// **Hardcoded, and it cannot be otherwise.** MushroomManager.GenerateEffectList deals
    /// each slot a <c>Random.Range(0, 4)</c> and RunAction multiplies by <c>0.05f</c>; both
    /// are literals in the game's own code with nothing exposing them at runtime. Only the
    /// dealt values survive, in <c>mushroomStamAmt</c>, and the highest of those in any one
    /// run is a sample rather than the bound.
    ///
    /// **0-15, not 0-20.** The wiki says 20, which is what reading <c>Range(0, 4)</c> as
    /// inclusive gives you. Unity's integer overload is max-exclusive, so the draws are 0, 1,
    /// 2 and 3. Verified in the IL rather than the decompiler's C#:
    /// <c>ldc.i4.0; ldc.i4.4; call int32 UnityEngine.Random::Range(int32, int32)</c>.
    /// A 4 appearing in the <c>stamAmts</c> table the item dump prints would disprove it.
    /// </summary>
    private const float MushroomStaminaPerRoll = 0.05f;
    private const float MaxMushroomStamina = 3 * MushroomStaminaPerRoll;

    private static List<EffectLine> DescribeMushroom(Action_RandomMushroomEffect effect)
    {
        const string Marks = "????";

        List<EffectLine> lines = new();
        MushroomManager? manager = MushroomManager.instance;
        if (manager == null || manager.mushroomEffects == null || manager.mushroomEffects.Length == 0)
        {
            lines.Add(new EffectLine(EffectColors.White + Marks + "</color>", Onset.Instant));
            return lines;
        }

        int index = effect.mushroomTypeIndex % manager.mushroomEffects.Length;

        // The span, not this berry's draw. mushroomStamAmt[index] is dealt once at level
        // generation and would tell us exactly what this berry carries - and not printing it
        // is the point. A Shroomberry is a gamble, and the overlay gives away no more of it
        // than the four question marks below already do.
        //
        // Confirmed in game: the stamina does arrive. It was hidden on a report that it did
        // not, which a roll of 0 - a quarter of berries - looks exactly like.
        //
        // Spoil Energy Increase in config prints the draw instead, read the way RunAction
        // reads it: the dealt integer times the same 0.05 the span is built from. A draw of
        // zero leaves no line, which is the truth about that berry.
        if (PluginConfig.EnergySpoiler.Value && manager.mushroomStamAmt != null
            && index < manager.mushroomStamAmt.Length)
        {
            float stamina = manager.mushroomStamAmt[index] * MushroomStaminaPerRoll;
            Collect(lines, 0, EffectFormatter.Effect(stamina, "Extra Stamina"),
                Onset.Instant, "Extra Stamina", stamina);
        }
        else
        {
            lines.Add(new EffectLine(
                EffectFormatter.Colored("+0-" + EffectFormatter.Scaled(MaxMushroomStamina), "Extra Stamina"),
                Onset.Instant, "Extra Stamina", MaxMushroomStamina));
        }

        // GenerateEffectList fills the slots in order and spends its quotas first: the first
        // minGoodEffects slots are drawn from GoodEffects, the next minBadEffects from
        // BadEffects, and only then does it choose freely. So a berry's slot decides whether
        // its valence is guaranteed or a coin flip, and that holds across every run even
        // though the effects themselves are reshuffled each time.
        //
        // No status of its own, so the marker trails the stamina rather than claiming a place
        // among the ranked lines.
        string marker;
        if (!PluginConfig.ShroomberryHint.Value)
        {
            marker = EffectColors.White + Marks + "</color>";
        }
        else if (index < manager.minGoodEffects)
        {
            marker = EffectColors.Positive + Marks + "</color>";
        }
        else if (index < manager.minGoodEffects + manager.minBadEffects)
        {
            marker = EffectColors.Negative + Marks + "</color>";
        }
        else if (PluginConfig.PurpleSpoiler.Value)
        {
            // The roll itself, read the way RunAction reads it, and judged by the game's own
            // list of which effect ids are the good half.
            int rolled = manager.mushroomEffects[index];
            bool good = Array.IndexOf(Action_RandomMushroomEffect.GoodEffects, rolled) >= 0;
            marker = (good ? EffectColors.Positive : EffectColors.Negative) + Marks + "</color>";
        }
        else
        {
            // Past both quotas the roll is genuinely free, so the marker says "either" -
            // half green, half red - rather than committing to this run's outcome.
            marker = EffectColors.Positive + "??</color>" + EffectColors.Negative + "??</color>";
        }

        lines.Add(new EffectLine(marker, Onset.Instant));
        return lines;
    }

    private static void DescribeRestoreHunger(Component component, Parts parts)
    {
        Action_RestoreHunger effect = (Action_RestoreHunger)component;
        Collect(parts.Effects, parts.Source, EffectFormatter.Effect(effect.restorationAmount * -1f, "Hunger"),
            Onset.Instant, "Hunger", effect.restorationAmount * -1f);
    }
    private static void DescribeGiveExtraStamina(Component component, Parts parts)
    {
        Action_GiveExtraStamina effect = (Action_GiveExtraStamina)component;
        Collect(parts.Effects, parts.Source, EffectFormatter.Effect(effect.amount, "Extra Stamina"),
            Onset.Instant, "Extra Stamina", effect.amount);
    }
    private static void DescribeInflictPoison(Component component, Parts parts)
    {
        // The delay used to be spelled out as "AFTER 10s,". Dropped: the "/ 8s"
        // suffix already says this is spread over time, and the lead-in was the
        // only English on an otherwise symbolic line.
        Action_InflictPoison effect = (Action_InflictPoison)component;
        float total = effect.poisonPerSecond * effect.inflictionTime;

        // A poisonous mushroom whose safe twin shares its name, with the overlay told not to
        // tell them apart: the poison becomes a coin flip on both. See PoisonousTwin.
        string text = PluginConfig.HidePoisonTwins.Value && HasSafeTwin(parts.Entity)
            ? EffectFormatter.ConditionalOverTime(total, effect.inflictionTime, "Poison")
            : EffectFormatter.OverTime(total, effect.inflictionTime, "Poison");

        Collect(parts.Effects, parts.Source, text, Onset.OverTime, "Poison", effect.poisonPerSecond);
    }

    /// <summary>
    /// Every other item the game gives the same name as this one. The game itself is what
    /// pairs a mushroom with its poisonous twin - Bugle Shroom is the name of both Mushroom
    /// Lace and Mushroom Lace Poison - so the name is the game's own key here rather than a
    /// proxy for behaviour. Built once from the item database and kept until a hot reload.
    /// </summary>
    private static IEnumerable<Item> Twins(Item item)
    {
        twinsByName ??= BuildTwins();
        string name = SafeName(item);
        if (!twinsByName.TryGetValue(name, out List<Item>? twins))
        {
            yield break;
        }

        foreach (Item twin in twins)
        {
            // A held item is a clone of its prefab; the prefab is not its own twin.
            if (twin.gameObject.name != item.gameObject.name.Replace("(Clone)", ""))
            {
                yield return twin;
            }
        }
    }

    private static Dictionary<string, List<Item>>? twinsByName;

    /// <summary>Drops the twin table, for a hot reload.</summary>
    internal static void Forget() => twinsByName = null;

    private static Dictionary<string, List<Item>> BuildTwins()
    {
        Dictionary<string, List<Item>> byName = new(StringComparer.Ordinal);
        foreach (Item entry in StatusIcons.AllItems())
        {
            string name = SafeName(entry);
            if (!byName.TryGetValue(name, out List<Item>? list))
            {
                list = new List<Item>();
                byName[name] = list;
            }

            list.Add(entry);
        }

        return byName;
    }

    private static string SafeName(Item item)
    {
        try
        {
            return item.GetName();
        }
        catch (Exception)
        {
            return item.gameObject.name;
        }
    }

    /// <summary>A twin that carries no poison, for the poisonous one to hedge against.</summary>
    private static bool HasSafeTwin(Item item)
    {
        foreach (Item twin in Twins(item))
        {
            if (twin.GetComponent<Action_InflictPoison>() == null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The poison a twin carries, for the safe one to hedge with.</summary>
    private static Action_InflictPoison? PoisonousTwin(Item item)
    {
        foreach (Item twin in Twins(item))
        {
            Action_InflictPoison? poison = twin.GetComponent<Action_InflictPoison>();
            if (poison != null)
            {
                return poison;
            }
        }

        return null;
    }
    private static void DescribeAddOrRemoveThorns(Component component, Parts parts)
    {
        Action_AddOrRemoveThorns effect = (Action_AddOrRemoveThorns)component;
        float thorns = effect.thornCount * ThornStatus();
        Collect(parts.Effects, parts.Source, EffectFormatter.Effect(thorns, "Thorns"),
            Onset.Instant, "Thorns", thorns);
    }

    /// <summary>
    /// What one thorn is worth, as a status fraction.
    ///
    /// UpdateWeight sets Thorns to one <see cref="GameValues.StatusStep"/> per *increment*, and a thorn
    /// is not one increment: <c>GetTotalThornStatusIncrements</c> adds up
    /// <c>ThornOnMe.GetThornDamage()</c>, which is the thorn's own <c>thornDamage</c> scaled
    /// by <c>Ascents.etcDamageMultiplier</c>. In 2.1.a at ascent zero that is two increments,
    /// which is why testing Prickleberry read 10 for two thorns rather than 5.
    ///
    /// This used to be a hardcoded 0.05 - that measurement, written down. Reading it also
    /// picks up the ascent multiplier, which the constant never could: thorns are worth more
    /// on the higher ascents and the overlay was quietly saying otherwise.
    ///
    /// The thorns belong to the character, not the item - a pool of ThornOnMe objects under
    /// the ragdoll, enabled as they are stuck in - so this needs somebody to read them off.
    /// With no character the fallback is the two increments 2.1.a ships, which is the old
    /// constant expressed as what it always meant.
    ///
    /// <b>Zero is a real answer here.</b> Two custom-run switches take thorns away, and both
    /// were being reported as a full Prickleberry:
    /// <list type="bullet">
    /// <item><c>Hazard_Thorns</c> off makes <c>CharacterAfflictions.AddThorn</c> return before
    /// it does anything, so no thorn is stuck in at all.</item>
    /// <item><c>EtcDamage</c> at zero makes <c>Ascents.etcDamageMultiplier</c> zero, so a thorn
    /// is stuck in and worth nothing.</item>
    /// </list>
    /// Returning zero drops the line: <see cref="EffectFormatter.Effect"/> renders an empty
    /// string for it and Collect discards that. Which is right - an effect that cannot happen
    /// should not be described.
    /// </summary>
    private static float ThornStatus()
    {
        const int MeasuredIncrementsPerThorn = 2;

        // AddThorn's own first test. With the hazard off it returns before stuffing anything
        // in, so the action is inert however many thorns it asks for.
        if (RunSettings.GetValue(RunSettings.SETTINGTYPE.Hazard_Thorns) == 0)
        {
            return 0f;
        }

        Character observed = Character.observedCharacter;
        if (observed != null && observed.refs != null && observed.refs.afflictions != null
            && observed.refs.afflictions.physicalThorns != null)
        {
            foreach (ThornOnMe thorn in observed.refs.afflictions.physicalThorns)
            {
                // Arrows share the pool and carry their own damage, so the first entry is not
                // necessarily a thorn. The damage itself is not tested - a thorn worth zero
                // increments is the EtcDamage switch turned off, which is an answer rather
                // than a reason to reach for the fallback.
                if (thorn != null && thorn.isThorn)
                {
                    return thorn.GetThornDamage() * GameValues.StatusStep;
                }
            }
        }

        return MeasuredIncrementsPerThorn * GameValues.StatusStep;
    }
    private static void DescribeModifyStatus(Component component, Parts parts)
    {
        Action_ModifyStatus effect = (Action_ModifyStatus)component;
        string status = effect.statusType.ToString();
        Collect(parts.Effects, parts.Source, Change(effect, status, parts),
            Onset.Instant, status, effect.changeAmount);

        // CharacterAfflictions.SubtractStatus takes the same amount off Spores
        // whenever Poison is reduced deliberately:
        //
        //   if (statusType == Poison && !decreasedNaturally && character.IsLocal)
        //       SubtractStatus(Spores, amount);
        //
        // So every poison cure is silently a spores cure of equal size. It is
        // one-way - adding poison adds no spores - and it does not apply to the
        // passive per-second decay. First Aid Kit, Antidote and Medicinal Root
        // all cure spores through this and nothing else; the wiki was right and
        // the components alone do not show it.
        if (effect.statusType == CharacterAfflictions.STATUSTYPE.Poison && effect.changeAmount < 0f)
        {
            string spores = CharacterAfflictions.STATUSTYPE.Spores.ToString();
            Collect(parts.Effects, parts.Source, Change(effect, spores, parts),
                Onset.Instant, spores, effect.changeAmount);
        }
    }

    /// <summary>
    /// One status change, or nothing where its gate is shut.
    ///
    /// <c>ifSkeleton</c> makes RunAction return before doing anything unless the character is
    /// a skeleton. Rather than hedge the line as "0/+50", the overlay asks the observed
    /// character the same question the action will, and shows the change only when it would
    /// land. The coupling above rides along: a gated poison cure is a gated spores cure.
    ///
    /// The Book of Bones is the one twist: its <c>Action_BecomeSkeleton</c> runs first, so
    /// there the gate opens for a <i>human</i> - you are a skeleton by the time the curse is
    /// checked - and shuts for a skeleton, who has just been turned back. So the gate applies
    /// when the character's state and the item's toggle disagree. Fortified Milk's skeleton-
    /// only injury cure has no toggle and simply waits for a skeleton to hold it.
    /// </summary>
    private static string Change(Action_ModifyStatus effect, string status, Parts parts) =>
        effect.ifSkeleton && !SkeletonGateOpen(parts.Item)
            ? ""
            : EffectFormatter.Effect(effect.changeAmount, status);

    private static bool SkeletonGateOpen(GameObject item)
    {
        bool toggles = item.GetComponent<Action_BecomeSkeleton>() != null;
        return IsSkeleton() != toggles;
    }

    /// <summary>
    /// Whether the character being described is a skeleton right now. The database dump has
    /// no one holding the item and assumes a human, so the showcase is the ordinary case.
    /// </summary>
    private static bool IsSkeleton()
    {
        if (AssumeHuman)
        {
            return false;
        }

        Character observed = Character.observedCharacter;
        return observed != null && observed.data != null && observed.data.isSkeleton;
    }

    /// <summary>Set by the dump while it runs, so prefabs are described for a human holder.</summary>
    internal static bool AssumeHuman { get; set; }

    private static void DescribeApplyAffliction(Component component, Parts parts)
    {
        Action_ApplyAffliction effect = (Action_ApplyAffliction)component;
        CollectAfflictions(parts, effect.affliction, effect.extraAfflictions);
    }

    /// <summary>
    /// The main affliction and the extras applied straight after it. Every
    /// Action_ApplyAffliction carries both, and only the mass variant used to read the
    /// second - in 2.1.a only the Cursed Skull fills it, but a base handler dropping what a
    /// derived one keeps is the kind of gap that goes quiet rather than failing.
    /// </summary>
    private static void CollectAfflictions(Parts parts, PeakAffliction? affliction,
        PeakAffliction[]? extras)
    {
        Collect(parts.Effects, parts.Source, EffectFormatter.Affliction(affliction));
        if (extras == null)
        {
            return;
        }

        foreach (PeakAffliction extra in extras)
        {
            Collect(parts.Effects, parts.Source, EffectFormatter.Affliction(extra));
        }
    }
    private static void DescribeNumb(Component component, Parts parts)
    {
        // Mandrake. Numbness hides your stamina bar, which is the whole reason to
        // cook one first - and the only icon in the mod that had to be shipped
        // rather than scraped, because numbness is not a STATUSTYPE.
        Action_Numb effect = (Action_Numb)component;
        Collect(parts.Effects, parts.Source, EffectFormatter.Colored(EffectFormatter.Seconds(effect.numbAmount), "Numb"),
            Onset.OverTime, "Numb", 1f);
    }
    private static void DescribeDie(Component component, Parts parts)
    {
        // Cursed Skull. Nothing else in the game does this, and no number describes
        // it - "the worst thing" is the whole message, so it leads in the Custom
        // section above everything the item gives everyone else.
        parts.Layout.Add(Block.Custom, EffectColors.Negative + "???</color>");
    }
    private static void DescribeRitualDaggerFeedBehavior(Component component, Parts parts)
    {
        // The other half of the Ritual Dagger, and the reason the wiki lists effects
        // the item does not carry: RPC_RitualDaggerBuff runs on every client and
        // skips only the character who was fed the dagger, so everybody else in the
        // lobby - the feeder included - is healed and handed stamina.
        //
        // This is not reachable as an ItemAction. IExtraFeedBehavior is its own
        // hook, called when one player feeds an item to another, and
        // RitualDaggerFeedBehavior is the only thing in 2.1.a that implements it.
        Peak.RitualDaggerFeedBehavior effect = (Peak.RitualDaggerFeedBehavior)component;

        // ClearAllStatus() with no arguments, so curse and petrify are spared.
        Collect(parts.Effects, parts.Source, EffectFormatter.ClearedStatuses(true, null));

        // AddExtraStamina takes the same 0-1 fraction as a status.
        Collect(parts.Effects, parts.Source, EffectFormatter.Effect(effect.bonusStamina, "Extra Stamina"),
            Onset.Instant, "Extra Stamina", effect.bonusStamina);

        if (effect.infiniteStaminaTime > 0f)
        {
            Collect(parts.Effects, parts.Source, EffectFormatter.InfiniteStamina(effect.infiniteStaminaTime),
                Onset.OverTime, "Extra Stamina", 1f);
        }
    }
    private static void DescribeRandomMushroomEffect(Component component, Parts parts)
    {
        Collect(parts.Effects, parts.Source, DescribeMushroom((Action_RandomMushroomEffect)component));
    }
    private static void DescribeClearAllStatus(Component component, Parts parts)
    {
        Action_ClearAllStatus effect = (Action_ClearAllStatus)component;
        Collect(parts.Effects, parts.Source,
            EffectFormatter.ClearedStatuses(effect.excludeCurse, effect.otherExclusions));
    }
    private static void DescribeApplyMassAffliction(Component component, Parts parts)
    {
        // The "NEARBY PLAYERS WILL RECEIVE:" header is gone. Nothing replaces it -
        // the effects speak for themselves, and a header was a whole line of English
        // for a distinction no item ever needs stated twice.
        //
        // ignoreCaster - the Cursed Skull and the Magic Bugle - hands the effect to everyone
        // nearby except you. Not marked: the skull's ??? already says it is unusual, and the
        // bugle's line below is about the crowd by construction.
        Action_ApplyMassAffliction effect = (Action_ApplyMassAffliction)component;

        // A Magic Bugle re-fires this every tenth of a second for as long as it is tooted,
        // so the affliction's own half-second is not a duration anybody experiences: the
        // stamina is infinite while the horn sounds. The reach is the point and is shown;
        // the skull's 900 units means "everyone" and is not.
        MagicBugle? bugle = parts.Item.GetComponent<MagicBugle>();
        if (bugle != null && bugle.massAffliction == effect)
        {
            Collect(parts.Effects, parts.Source,
                EffectFormatter.Colored(EffectFormatter.Infinity, "Extra Stamina")
                + EffectColors.Neutral + " " + EffectFormatter.PeakMetres(effect.radius) + "</color>",
                Onset.OverTime, "Extra Stamina", 1f);
            return;
        }

        CollectAfflictions(parts, effect.affliction, effect.extraAfflictions);
    }
    private static void DescribeRaycastDart(Component component, Parts parts)
    {
        Action_RaycastDart effect = (Action_RaycastDart)component;
        for (int j = 0; j < effect.afflictionsOnHit.Length; j++)
        {
            Collect(parts.Effects, parts.Source, EffectFormatter.Affliction(effect.afflictionsOnHit[j]));
        }
    }
    private static void DescribeLanternItem(Component component, Parts parts)
    {
        Collect(parts.Effects, parts.Source, DescribeLantern(parts.Item));
    }
    private static void DescribeConstructable(Component component, Parts parts)
    {
        // Whether it builds something you can cook on, asked of the thing itself rather
        // than of its name. This used to require the prefab be called
        // "PortableStovetop_Placed", which said nothing a Campfire component does not, and
        // would have gone quiet the day the prefab was renamed.
        //
        // Constructable has two subclasses - ScoutEffigy and CheckpointConstructable - which
        // the base walk now brings here. Neither builds a campfire, so neither says anything,
        // and that is the right answer rather than a lucky one.
        Constructable effect = (Constructable)component;
        Campfire? campfire = effect.constructedPrefab == null
            ? null
            : effect.constructedPrefab.GetComponent<Campfire>();
        if (campfire != null)
        {
            parts.Layout.Add(Block.Custom,
                EffectColors.Neutral + EffectFormatter.Seconds(campfire.burnsFor) + "</color> "
                + EffectColors.Get("Cook") + StatusIcons.Tag("Cook") + "</color>");
        }

        // What standing by the built thing does - a stovetop is a campfire, and a campfire
        // warms you, which the cook icon alone does not say.
        Collect(parts.Effects, parts.Source, DescribeEmitters(effect.constructedPrefab));
    }
    private static void DescribeRopeShooter(Component component, Parts parts)
    {
        // Two different distances, and the old single line conflated them. How far
        // the cannon shoots is a raycast in Unity units; how much rope that leaves is
        // a segment count. They were both being read off maxLength, which was only
        // ever right by coincidence - maxLength is 30 units and length is 30
        // segments, so dividing the wrong field by four still landed on 7.5.
        RopeShooter effect = (RopeShooter)component;

        // The anti-rope cannon shares this component with the ordinary one and has no
        // flag of its own; what marks it is Antigrav, which makes the item float
        // where it lies. A plain rope cannon has no reason to carry that, and the
        // alternative was reading Rope.antigrav two prefabs deep through
        // ropeAnchorWithRopePref.
        bool anti = parts.Item.GetComponent<Antigrav>() != null;

        parts.Layout.Add(Block.Custom, EffectFormatter.Colored(
            EffectFormatter.PeakMetres(effect.maxLength),
            anti ? "RopeCannonAnti" : "RopeCannon"));
        // Two answers here, and the game gives the smaller one.
        //
        // Rope.GetLengthInMeters is segments * 0.25, which is what the spool's own display
        // shows - a Rope Cannon's rope and a spool rope of the same 30 segments hang side by
        // side at the same length, and the game calls that 7.5m. Those are not the metres the
        // altitude readout uses; they are almost exactly the rope's length in *units*, which
        // is how the wiki came to publish 7.5m for it.
        //
        // The real span has to account for scale. Rope joins its segments with
        // connectedAnchor = (0, -spacing, 0), and a joint anchor is measured in the connected
        // body's *local* space, so the segment prefab's Y scale shrinks every gap: 0.75
        // spacing against a 0.35 scale is 0.2625 a segment, and 30 of them span 7.875 units.
        // In real metres that is 12.6, not the 22.5 units spacing alone suggests.
        //
        // Matching the game is the default, so the overlay and the spool never disagree in
        // front of a player. The truth is a switch away.
        float segment = RopeSegmentLength(effect);
        string ropeLength = PluginConfig.RealRopeLength.Value && segment > 0f
            ? EffectFormatter.PeakMetres(effect.length * segment)
            : EffectFormatter.Metres(Rope.GetLengthInMeters(effect.length));

        parts.Layout.Add(Block.Custom, EffectFormatter.Colored(
            ropeLength, anti ? "RopeSpoolAnti" : "RopeSpool"));
    }
    private static void DescribeVineShooter(Component component, Parts parts)
    {
        // Chain Launcher. maxLength is the raycast it fires along, in Unity units like every
        // other range - the old "/ (5f / 3f)" was a fit made before CharacterStats.unitsToMeters
        // was found, and it reported 30m for a chain that lands about 48 units away and that
        // the wiki puts at 80.
        VineShooter effect = (VineShooter)component;
        parts.Layout.Add(Block.Custom, ReachInUnits(effect.maxLength));
    }
    private static void DescribeMagicBean(Component component, Parts parts)
    {
        // maxLength is written into the vine's localScale.y as it grows, so it reads like a
        // scale rather than a distance - and scaling the stalk mesh by it gives 94 units,
        // which is nonsense. The mesh's long axis is Z, not Y, so its bounds are not the
        // height of the thing being scaled; something in the renderer compensates.
        //
        // Taken as plain Unity units it gives 32m, and a height-tracking mod reads 28m of
        // gain on a vine that grew skewed - so the real length is at least that. 32 is where
        // it should land. That also matches how every other range in the game is authored,
        // and it retires the last of the invented divisors: this used to be "/ 2f".
        MagicBean effect = (MagicBean)component;
        if (effect.plantPrefab != null)
        {
            parts.Layout.Add(Block.Custom, ReachInUnits(effect.plantPrefab.maxLength));
        }
    }
    private static void DescribeShelfShroom(Component component, Parts parts)
    {
        // Remedy Fungus. There is no eat-it effect at all - the only way to use it is to
        // throw it, and the explosion is what heals - so every figure here is the thrown one.
        // The 2.1.a spawn holds one healing blast and two AOEs re-firing every half second
        // for as long as it lives; no radius, because the reach is not what a player acts on.
        DescribeBlasts(((ShelfShroom)component).instantiateOnBreak, parts, showRange: false);
    }
    private static void DescribeBreakable(Component component, Parts parts)
    {
        // What a thrown item does when it shatters. Breakable spawns two kinds of thing:
        // items (a Coconut's halves, a nest's egg), which are the player's to discover and
        // are not named, and non-item prefabs, which is where an effect lives - a Snowball's
        // impact is an AOE of cold. Only the second kind is walked.
        //
        // And only where shattering is the item's use. An Antidote shatters into its cloud
        // too, but an Antidote is for drinking - it has uses to spend - and listing the
        // cloud beside the drink read as the same cure twice at two strengths. An item with
        // no consume action and no uses has nothing else to do but be thrown.
        Breakable breakable = (Breakable)component;
        if (breakable.instantiateNonItemOnBreak == null || parts.Consumable
            || parts.Item.GetComponent<Action_ReduceUses>() != null)
        {
            return;
        }

        foreach (GameObject prefab in breakable.instantiateNonItemOnBreak)
        {
            DescribeBlasts(prefab, parts, showRange: false);
        }
    }
    private static void DescribeMoraleBoost(Component component, Parts parts)
    {
        Action_MoraleBoost effect = (Action_MoraleBoost)component;
        Collect(parts.Effects, parts.Source, EffectFormatter.Effect(effect.baselineStaminaBoost, "Extra Stamina"),
            Onset.Instant, "Extra Stamina", effect.baselineStaminaBoost);
    }
    /// <summary>
    /// What still holding a stick of dynamite costs when the fuse runs out, as a status
    /// fraction. <c>Dynamite.Update</c>, before the explosion is even spawned:
    ///
    /// <code>
    ///   if (Character.localCharacter.data.currentItem == item)
    ///       AddStatus(STATUSTYPE.Injury, 0.25f);
    /// </code>
    ///
    /// <b>Hardcoded, and it has to be.</b> The 0.25 is a literal in a method body with
    /// nothing exposing it at runtime - the one number this file gained back during the
    /// provenance pass rather than lost.
    /// </summary>
    private const float HeldDynamiteInjury = 0.25f;

    private static void DescribeDynamite(Component component, Parts parts)
    {
        Dynamite effect = (Dynamite)component;

        // Two separate things happen and they used to be one line reading the blast's raw
        // figure. A stick going off in the hand measures 52.5: a flat 25 for holding it, and
        // 27.5 from the blast. Splitting them is what made that arithmetic come out.
        //
        // This one first, and with no radius after it, because it is not an area effect at
        // all - it lands on whoever is holding the thing, wherever they are standing.
        Collect(parts.Effects, parts.Source,
            EffectFormatter.Effect(HeldDynamiteInjury, "Injury"),
            Onset.Instant, "Injury", HeldDynamiteInjury);

        // The reach trails the amount, the same shape Scout's Initiative uses for its own
        // area effect. It is also what tells this line apart from the one above at a glance.
        DescribeBlasts(effect.explosionPrefab, parts, showRange: true);
    }
    private static void DescribeSpawn(Component component, Parts parts)
    {
        // Everything sunscreen does lives on the thing it sprays: the bottle carries only
        // Action_ReduceUses and this. The spawned prefab is walked for what it holds rather
        // than reached through the names it used to be - "VFX_Sunscreen" with an "AOE" child,
        // where Transform.Find returned null straight into a GetComponent the day either
        // moved.
        //
        // An AOE flagged hasAffliction hands its affliction to whoever it catches, which is
        // where the protection and its duration actually are.
        //
        // The cloud's own lifetime is deliberately not shown. It was tried - a RemoveAfterSeconds
        // on the spawned prefab, four seconds, beside the ninety the protection lasts - and two
        // durations on one item read as a puzzle rather than as information. How long the spray
        // hangs in the air is not something a player acts on; how long they are covered is.
        DescribeBlasts(((Action_Spawn)component).objectToSpawn, parts, showRange: false);
    }

    /// <summary>
    /// Everything the AOEs in a spawned prefab do: each status they move, as one hit or as a
    /// rate where the blast repeats, and any affliction they hand out.
    ///
    /// One reader for dynamite, Remedy Fungus and Sunscreen. There were three, and each read
    /// a different part of an AOE - the first took one blast's amount and called it Injury
    /// without looking, the second read statuses but never the affliction, the third the
    /// affliction but never the statuses. Any prefab change on the unread side went unreported.
    ///
    /// GetComponentsInChildren with includeInactive, and guarded: this used to be a bare
    /// GetComponent on the prefab root, which would have thrown straight through Build and
    /// blanked the whole overlay the day the AOE moved down a level.
    /// </summary>
    private static void DescribeBlasts(GameObject? prefab, Parts parts, bool showRange)
    {
        if (prefab == null)
        {
            return;
        }

        foreach (AOE aoe in prefab.GetComponentsInChildren<AOE>(true))
        {
            // A repeating TimeEvent on the same object turns a one-off burst into a field you
            // stand in. Remedy Fungus is both: one blast that heals as it goes off, and two
            // AOEs re-firing every half second for as long as the spawn lives.
            TimeEvent? repeat = aoe.GetComponent<TimeEvent>();
            bool ticking = repeat != null && repeat.repeating && repeat.rate > 0f;
            float seconds = ticking ? Lifetime(aoe.transform) : 0f;
            string reach = showRange
                ? EffectColors.Neutral + " " + EffectFormatter.PeakMetres(aoe.range) + "</color>"
                : "";

            List<EffectLine> lines = new();
            AddBlast(lines, aoe, aoe.statusType, aoe.statusAmount, repeat, seconds, reach);

            for (int j = 0; aoe.addtlStatus != null && j < aoe.addtlStatus.Length; j++)
            {
                // Each additional status uses its own override where one is given, and the
                // main amount otherwise - the same fallback Explode does.
                float amount = aoe.addlStatusAmountOverrides != null
                    && j < aoe.addlStatusAmountOverrides.Count
                        ? aoe.addlStatusAmountOverrides[j]
                        : aoe.statusAmount;
                AddBlast(lines, aoe, aoe.addtlStatus[j], amount, repeat, seconds, reach);
            }

            // An AOE flagged hasAffliction hands its affliction to whoever it catches - for
            // Sunscreen that is where the protection and its duration actually are.
            if (aoe.hasAffliction && aoe.affliction != null)
            {
                lines.AddRange(EffectFormatter.Affliction(aoe.affliction));
            }

            Collect(parts.Effects, parts.Source, lines);
        }
    }
    private static void DescribeScorpion(Component component, Parts parts)
    {
        // Hiding the poison info when dead was tried and reverted: mob state does not
        // update immediately on equip, which produced a visual bug.
        Scorpion effect = (Scorpion)component;

        // Scorpion.InflictAttack (verified against 2.1.a) does an instant
        // AddStatus(Poison, 0.025) then a poison-over-time totalling
        // max(0.5, (1 - statusSum) + 0.05). statusSum runs 0..1, so the over-time
        // part spans 50 at full status to 105 at none - more damage the healthier
        // you are. The instant 2.5 is folded in rather than shown separately.
        //
        // Both bounds are literals inside the method body with nothing exposing them, so
        // they are literals here too - as fractions, so the status scale still applies.
        const float MinPoison = 0.5f;
        const float MaxPoison = 1.05f;
        Collect(parts.Effects, parts.Source, EffectFormatter.Colored(
                EffectFormatter.Scaled(MinPoison) + "-" + EffectFormatter.Scaled(MaxPoison), "Poison")
            + EffectColors.Neutral + " / " + EffectFormatter.Seconds(effect.totalPoisonTime) + "</color>",
            Onset.OverTime, "Poison", 1f);
    }
    // 'is' rather than an exact match: CactusBall derives from StickyItemComponent
    // and is the only item carrying one in 2.1.a, so an exact check would read the
    // base class and describe nothing at all. Same trap that lost
    // Action_SuperJumpAmulet's affliction.
    private static void DescribeStickyItemComponent(Component component, Parts parts)
    {
        StickyItemComponent sticky = (StickyItemComponent)component;
        // Cactus. addThornsToStuckPlayer is charged to whoever the cactus is stuck
        // to - and CharacterData.currentItem's setter makes the item in your hand
        // your currentStickyItem, so UpdateWeight charges you for it while you are
        // merely holding it, not only after someone throws it at you. The number is
        // the same on both readings, so one line says both.
        //
        // Thorn *increments*, not thorns: UpdateWeight does
        // SetStatus(Thorns, 0.025 * increments) and this field is added straight to
        // that count, unlike Action_AddOrRemoveThorns where one thorn is worth two
        // increments. Custom rather than Effects for the same reason as the idol
        // below - a cactus has no use-action for the line to be mistaken for.
        //
        // addWeightToStuckPlayer rides the same path and is deliberately unread: it
        // would print a second Weight figure that the Weight section does not know
        // about, and no item in 2.1.a is known to set it.
        parts.Layout.Add(Block.Custom,
            EffectFormatter.Effect(sticky.addThornsToStuckPlayer * GameValues.StatusStep, "Thorns"));
    }
    private static void DescribeBingBongShieldWhileHolding(Component component, Parts parts)
    {
        // Ancient Idol - the one item in 2.1.a that does its work while merely held
        // rather than when used. The component re-applies a two-second
        // Affliction_BingBongShield every 1.5 seconds for as long as the idol is your
        // current item, so neither number means anything on its own: the shield never
        // lapses, and infinity is the honest amount of it.
        //
        // Coloured like any other figure with an icon beside it, which is what Big
        // Lollipop already does with the same mark. Neutral is for a duration standing
        // next to a figure, as on the healing amulet below; here the mark is the figure.
        //
        // Custom rather than Effects, even though the amulet's shield is an effect.
        // Effects answers "what happens when you use this", and the idol is never used;
        // filing it there would promise a shield on some action that does not exist.
        // Nothing marks it as a held effect - the idol has no use-action to confuse it
        // with.
        parts.Layout.Add(Block.Custom, EffectFormatter.Colored(EffectFormatter.Infinity, "Shield"));
    }
    private static void DescribeHealingGem(Component component, Parts parts)
    {
        Peak.Action_HealingGem effect = (Peak.Action_HealingGem)component;

        // Heals a shared budget across six statuses at once, so the amount is white
        // rather than any one status colour, and the icons say which are eligible.
        // Slashes, not spaces: maxHealing is one pool spread across all six, not
        // an allowance for each. This is the only item in 2.1.a that works this way,
        // which is exactly why the slash form is reserved for it.
        // Ranked by the first status of its run, so the budget leads the amulet's
        // three lines rather than sorting after the petrify it costs.
        Collect(parts.Effects, parts.Source,
            EffectFormatter.SharedBudget(-effect.healingAffliction.maxHealing, HealAllStatuses),
            Onset.Instant, HealAllStatuses[0], -1f);

        if (effect.invincibilityAffliction != null)
        {
            Collect(parts.Effects, parts.Source, EffectColors.Neutral
                + EffectFormatter.Seconds(effect.invincibilityAffliction.totalTime) + "</color> "
                + EffectColors.Get("Shield") + StatusIcons.Tag("Shield") + "</color>",
                Onset.OverTime, "Shield", 1f);
        }

        // Petrify scales with how much healing was actually possible, clamped to
        // this range, so a range is the honest thing to show.
        Collect(parts.Effects, parts.Source, EffectColors.Get("Petrify") + "+"
            + EffectFormatter.WholePoints(effect.minPetrify)
            + "-" + EffectFormatter.WholePoints(effect.maxPetrify)
            + " " + StatusIcons.Tag("Petrify") + "</color>",
            Onset.Instant, "Petrify", 1f);
    }
    private static void DescribeCloneSelectedItem(Component component, Parts parts)
    {
        Peak.Action_CloneSelectedItem effect = (Peak.Action_CloneSelectedItem)component;
        // Every part of this line was untagged, so all four glyphs rendered in
        // TMP's default. The arrow is neutral like every other arrow; the item
        // glyphs take the cream, which is what a figure belonging to no status wears.
        string generic = EffectColors.White + StatusIcons.Tag("Item") + "</color>";

        parts.Layout.Add(Block.Custom, generic
            + EffectColors.Neutral + EffectFormatter.Arrow + "</color>"
            + generic + generic);
        // AddPetrify takes whole points on the game's 0-100 scale, unlike almost everything
        // else here, so they go through WholePoints as fractions rather than being printed
        // raw - that keeps them on the configured status scale. The two values are discrete
        // - plain items versus mystical ones - so a slash, not a range.
        Collect(parts.Effects, parts.Source, EffectColors.Get("Petrify") + "+"
            + EffectFormatter.WholePoints(effect.petrify / 100f) + "/"
            + EffectFormatter.WholePoints(effect.petrifyMystical / 100f)
            + " " + StatusIcons.Tag("Petrify") + "</color>",
            Onset.Instant, "Petrify", 1f);
    }
    // Amulets are matched with 'is' rather than an exact type check: they all derive
    // from AmuletBase and each applies petrify through a different path.
    private static void DescribeSuperJumpAmulet(Component component, Parts parts)
    {
        Peak.Action_SuperJumpAmulet superJump = (Peak.Action_SuperJumpAmulet)component;
        // Action_SuperJumpAmulet derives from Action_ApplyAffliction and its
        // RunAction calls base.RunAction() before charging petrify, so it carries a
        // real affliction as well as a cost. The Action_ApplyAffliction branch above
        // matches on exact type and so never sees a subclass - this is the only
        // place that affliction is read.
        CollectAfflictions(parts, superJump.affliction, superJump.extraAfflictions);
        // AddStatus takes a 0-1 fraction like every other status, but petrify is
        // floored to whole points on the way in - this read +7.5 where the game gives
        // you 7.
        if (superJump.petrifyPerUse != 0f)
        {
            Collect(parts.Effects, parts.Source, EffectFormatter.Colored(
                    "+" + EffectFormatter.WholePoints(superJump.petrifyPerUse), "Petrify"),
                Onset.Instant, "Petrify", superJump.petrifyPerUse);
        }
    }

    /// <summary>
    /// How far apart two segments of the rope this cannon fires sit, in Unity units, or zero
    /// if the prefab chain cannot be walked.
    ///
    /// A ConfigurableJoint pins a point on its own body to a point on the connected one, so
    /// the gap between their centres is **both** offsets added, not either alone:
    ///
    ///   <c>anchor.y</c>          0.5  - set on the segment prefab
    ///   <c>Rope.spacing</c>      0.75 - written as connectedAnchor = (0, -spacing, 0)
    ///
    /// and both are local-space offsets, so the segment prefab's Y scale applies to each.
    /// (0.5 + 0.75) x 0.35 is 0.4375 a segment, which puts a 30-segment rope at 13.1 units
    /// and 21 metres. Measuring from the anchor straight down gives 12 to 14 units, and
    /// climbing one with a height-tracking mod reads about 20 metres.
    ///
    /// Reading either offset on its own is what made three earlier attempts wrong - spacing
    /// alone says 22.5 units, connectedAnchor scaled alone says 7.9, and the truth is the sum.
    /// The joint is Locked on every axis with a zero linear limit, so none of this stretches:
    /// the rope really is rigid and the figure really is derivable.
    ///
    /// Guarded at every step: these are prefab references, and a missing one is a null the day
    /// the game reorganises them.
    /// </summary>
    private static float RopeSegmentLength(RopeShooter shooter)
    {
        RopeAnchorWithRope? anchorPrefab = shooter.ropeAnchorWithRopePref == null
            ? null
            : shooter.ropeAnchorWithRopePref.GetComponent<RopeAnchorWithRope>();
        Rope? rope = anchorPrefab == null || anchorPrefab.ropePrefab == null
            ? null
            : anchorPrefab.ropePrefab.GetComponent<Rope>();
        if (rope == null || rope.ropeSegmentPrefab == null)
        {
            return 0f;
        }

        ConfigurableJoint? joint = rope.ropeSegmentPrefab.GetComponent<ConfigurableJoint>();
        float anchor = joint == null ? 0f : Mathf.Abs(joint.anchor.y);
        float scale = rope.ropeSegmentPrefab.transform.localScale.y;
        return (anchor + rope.spacing) * scale;
    }

    /// <summary>A distance in metres, in the neutral colour. No unit space: "12.5m".</summary>
    /// <summary>A distance held in Unity units, in the neutral colour.</summary>
    private static string ReachInUnits(float unityUnits) =>
        EffectColors.Neutral + EffectFormatter.PeakMetres(unityUnits) + "</color>";

    /// <summary>A distance already in the metres the game shows, in the neutral colour.</summary>
    private static string Reach(float metres) =>
        EffectColors.Neutral + EffectFormatter.Metres(metres) + "</color>";

    /// <summary>
    /// A lit lantern warms whoever is near it. Stated per second rather than as a total over
    /// the fuel, so the figure means the same thing on a full lantern and a nearly-spent one.
    ///
    /// The field is found by walking the lantern for one, not by knowing that a Faerie
    /// Lantern is called "Lantern_Faerie(Clone)" and keeps its heat at
    /// "FaerieLantern/Light/Heat". That was four hardcoded names for two items, each of which
    /// would have gone silent on a rename, and it could not describe a third lantern at all.
    /// </summary>
    private static List<EffectLine> DescribeLantern(GameObject itemGameObj)
    {
        List<EffectLine> lines = new();

        StatusField? effect = itemGameObj.GetComponentInChildren<StatusField>(true);
        if (effect == null)
        {
            return lines;
        }

        // Every status in the field moves at the *main* rate. StatusFieldStatus carries a
        // statusAmountPerSecond of its own and the game never reads it -
        // StatusFieldBase.IncreaseStatus passes the same amt to every additional status:
        //
        //   AdjustStatus(statusType, amt);
        //   foreach (var additional in additionalStatuses)
        //       AdjustStatus(additional.statusType, amt);
        //
        // So a per-status figure was reporting an inspector field with no effect on play.
        // (tickBased changes nothing either: it applies statusAmountPerSecond * timeBetweenTicks
        // once per tick, which is the same rate.)
        Dictionary<CharacterAfflictions.STATUSTYPE, float> rates = new();
        Accumulate(rates, effect.statusType, effect.statusAmountPerSecond);
        foreach (StatusField.StatusFieldStatus status in effect.additionalStatuses)
        {
            Accumulate(rates, status.statusType, effect.statusAmountPerSecond);
        }

        // IncreaseStatus goes through AdjustStatus, which sends anything negative to
        // SubtractStatus - so a lantern that cures poison cures spores at the same rate, for
        // free and without a component saying so. Same coupling the Action_ModifyStatus
        // branch honours; the Faerie Lantern was the case where it was being missed.
        if (rates.TryGetValue(CharacterAfflictions.STATUSTYPE.Poison, out float poison)
            && CuresSporesToo(CharacterAfflictions.STATUSTYPE.Poison, poison))
        {
            Accumulate(rates, CharacterAfflictions.STATUSTYPE.Spores, poison);
        }

        // One line per status. Grouping by shared rate was tried and dropped for consistency
        // with every other effect in the overlay - the Faerie Lantern is taller for it, but a
        // reader no longer has to learn a second way of reading a line.
        foreach (KeyValuePair<CharacterAfflictions.STATUSTYPE, float> rate in rates)
        {
            AddPerSecond(lines, rate.Value, rate.Key);
        }

        return lines;
    }

    /// <summary>
    /// True when taking this status down also takes Spores down by the same amount.
    /// CharacterAfflictions.SubtractStatus does it for every deliberate poison cure:
    ///
    ///   if (statusType == Poison &amp;&amp; !decreasedNaturally &amp;&amp; character.IsLocal)
    ///       SubtractStatus(Spores, amount);
    ///
    /// One-way, and not applied to the passive decay. Stated once here because three
    /// different shapes of line need it and each was working it out for itself.
    /// </summary>
    private static bool CuresSporesToo(CharacterAfflictions.STATUSTYPE statusType, float amount) =>
        statusType == CharacterAfflictions.STATUSTYPE.Poison && amount < 0f;

    /// <summary>
    /// Adds to a status's running total rather than replacing it, because the game applies
    /// each entry separately - a field naming the same status twice moves it twice as fast.
    /// </summary>
    private static void Accumulate(Dictionary<CharacterAfflictions.STATUSTYPE, float> rates,
        CharacterAfflictions.STATUSTYPE statusType, float amount)
    {
        rates[statusType] = rates.TryGetValue(statusType, out float running) ? running + amount : amount;
    }

    /// <summary>One warmed-or-chilled status from a lantern's field, if it moves at all.</summary>
    private static void AddPerSecond(List<EffectLine> lines, float perSecond,
        CharacterAfflictions.STATUSTYPE statusType)
    {
        string status = statusType.ToString();
        string text = EffectFormatter.PerSecond(perSecond, status);
        if (text.Length > 0)
        {
            lines.Add(new EffectLine(text, Onset.OverTime, status, perSecond));
        }
    }

    /// <summary>
    /// What a built thing does to whoever stands near it, per second - a stovetop's warmth.
    ///
    /// <c>StatusEmitter.Update</c> hands <c>amount x tickTime</c> to Add/SubtractStatus every
    /// <c>tickTime</c>, so <c>amount</c> is a per-second rate - but a banked one, paid out in
    /// whole steps per tick, and <see cref="TickedRate"/> turns that back into the rate a
    /// player would measure. The inner fade scales an <i>added</i> status down towards
    /// <c>minAmount</c> by distance; a removed one is never faded, and the campfire removes.
    /// Same shape as a lantern's field, and the same poison-to-spores coupling.
    /// </summary>
    private static List<EffectLine> DescribeEmitters(GameObject? prefab)
    {
        List<EffectLine> lines = new();
        if (prefab == null)
        {
            return lines;
        }

        Dictionary<CharacterAfflictions.STATUSTYPE, float> rates = new();
        foreach (StatusEmitter emitter in prefab.GetComponentsInChildren<StatusEmitter>(true))
        {
            // The walk includes inactive objects because a prefab's ancestors are switched on
            // by the thing that builds it - the stovetop's EnableWhenLit by Campfire. An
            // emitter whose *own* object is off is a different matter: the stovetop's
            // HealRadius ships disabled and nothing turns it on, and the stove does not heal.
            if (!emitter.gameObject.activeSelf)
            {
                continue;
            }

            // A radius your chest cannot get inside on foot is an emitter nobody meets: the
            // stovetop's HotRadius is half a unit around the flame itself.
            if (!Blast.Reachable(emitter.radius + emitter.outerFade))
            {
                continue;
            }

            Accumulate(rates, emitter.statusType,
                TickedRate(emitter.amount * emitter.tickTime, emitter.tickTime));
        }

        if (rates.TryGetValue(CharacterAfflictions.STATUSTYPE.Poison, out float poison)
            && CuresSporesToo(CharacterAfflictions.STATUSTYPE.Poison, poison))
        {
            Accumulate(rates, CharacterAfflictions.STATUSTYPE.Spores, poison);
        }

        foreach (KeyValuePair<CharacterAfflictions.STATUSTYPE, float> rate in rates)
        {
            AddPerSecond(lines, rate.Value, rate.Key);
        }

        return lines;
    }

    /// <summary>
    /// The rate a repeating amount actually moves a status at, once the game's banking has
    /// had its say: whole steps per tick where the amount covers one, else one step every few
    /// ticks. See <see cref="TickedTotal"/> for why dividing the raw amount overstates it.
    /// </summary>
    private static float TickedRate(float perTickAmount, float period)
    {
        float payout = Payout(perTickAmount, period, out float every);
        if (payout == 0f)
        {
            return 0f;
        }

        float rate = payout / every;
        return perTickAmount < 0f ? -rate : rate;
    }

    /// <summary>
    /// What one payout of a banked repeating amount is worth and how often it lands: the
    /// floored amount every period where the amount covers a step, else a single step every
    /// however many periods it takes to reach one. Zero payout where nothing ever lands.
    /// </summary>
    private static float Payout(float perTickAmount, float period, out float every)
    {
        every = period;
        float perTick = Mathf.Abs(perTickAmount);
        if (perTick == 0f || period <= 0f)
        {
            return 0f;
        }

        if (perTick >= GameValues.StatusStep)
        {
            // Big enough to pay out every tick, still losing whatever does not fill a step.
            return Mathf.Floor(perTick / GameValues.StatusStep) * GameValues.StatusStep;
        }

        // Too small to pay out alone, so it takes several ticks to reach one step.
        every = Mathf.Ceil(GameValues.StatusStep / perTick) * period;
        return GameValues.StatusStep;
    }

    /// <summary>
    /// How long a spawned effect lasts, from the nearest RemoveAfterSeconds at or above it.
    /// Zero where nothing sets a lifetime, which reads as "no duration to state".
    ///
    /// **includeInactive matters here.** Everything walked in this file is a prefab asset,
    /// never a live object, so nothing in it is active in any hierarchy - and the no-argument
    /// GetComponentInParent skips inactive objects and returns null every time. That silently
    /// made every repeating blast durationless, which OverTime renders as no line at all:
    /// Remedy Fungus showed its instant heal and nothing else.
    /// </summary>
    private static float Lifetime(Transform spawned)
    {
        RemoveAfterSeconds? removeAfter = spawned.GetComponentInParent<RemoveAfterSeconds>(true);
        return removeAfter != null ? removeAfter.seconds : 0f;
    }

    /// <summary>
    /// One status an explosion moves - as a single hit, or as a rate where the blast repeats -
    /// plus the spores that come free with a poison cure. AOE.Explode applies its amounts
    /// through AdjustStatus, so that coupling reaches here exactly as it reaches a lantern.
    /// </summary>
    private static void AddBlast(List<EffectLine> lines, AOE aoe,
        CharacterAfflictions.STATUSTYPE statusType, float amount, TimeEvent? repeat, float seconds,
        string suffix)
    {
        string status = statusType.ToString();

        if (repeat == null || !repeat.repeating || repeat.rate <= 0f)
        {
            // Token rather than Effect, so the reach can trail the figure on the same line.
            float delivered = Blast.Delivered(aoe, amount);
            if (delivered != 0f)
            {
                Collect(lines, 0, EffectFormatter.Token(delivered, status) + suffix,
                    Onset.Instant, status, amount);
            }
        }
        else
        {
            float total = TickedTotal(amount, repeat.rate, seconds);
            Collect(lines, 0, EffectFormatter.OverTime(total, seconds, status),
                Onset.OverTime, status, total);
        }

        if (CuresSporesToo(statusType, amount))
        {
            AddBlast(lines, aoe, CharacterAfflictions.STATUSTYPE.Spores, amount, repeat, seconds, suffix);
        }
    }

    /// <summary>
    /// What a repeating blast is worth over its whole life - which is neither its amount
    /// divided by its period nor that multiplied by its duration, because a status does not
    /// move by whatever it is handed and the last payout never lands.
    ///
    /// SubtractStatus banks each amount and pays out only in whole 0.025 steps, **throwing
    /// the remainder away** each time it pays:
    ///
    ///   currentDecrementalStatuses[t] += amount;
    ///   if (acc >= 0.025) { currentStatuses[t] -= floor(acc / 0.025) * 0.025; acc = 0; }
    ///
    /// Remedy Fungus hands over 0.015 every half second. That is under the step, so nothing
    /// lands on the first tick and 0.025 comes off on the second - one step per second, 2.5
    /// display units, against a raw figure of 3. Dividing would overstate it by a fifth, and
    /// the discarded remainder is why.
    ///
    /// Then the payouts are counted rather than multiplied out. They land at one step, two
    /// steps, and so on, while RemoveAfterSeconds destroys the spawn at the duration itself -
    /// so a payout falling exactly on that boundary never happens. A 15-second fungus field
    /// pays 14 times, which is what in-game testing found: it heals as though it ran for 14
    /// seconds.
    ///
    /// This also makes the blast's distance factor irrelevant here: anything between 0.0125
    /// and 0.025 a tick lands on the same one step per second, so the empirical 0.9 in
    /// <see cref="Blast.Delivered"/>'s point-blank factor is not needed for the ticking half
    /// and is not applied to it.
    /// </summary>
    private static float TickedTotal(float amount, float period, float seconds)
    {
        float payout = Payout(amount, period, out float every);
        if (payout == 0f || seconds <= 0f)
        {
            return 0f;
        }

        float payouts = Mathf.Max(0f, Mathf.Ceil(seconds / every) - 1f);
        float total = payouts * payout;
        return amount < 0f ? -total : total;
    }
}
