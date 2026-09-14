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
/// The display needs no translation: numbers, signs and the game's own icons, with no
/// English on any rendered line. Where a fact has no symbol it is omitted rather than
/// described in words.
///
/// Components are dispatched through <see cref="Handlers"/>, a type lookup that walks to the
/// base class - see there for why it is neither a chain of exact type tests nor of 'is'.
/// </summary>
internal static class ItemDescriptionBuilder
{
    /// <summary>
    /// What Affliction_HealAll treats, in its own order - read from the (publicized) field
    /// rather than typed out, so a patch that changes the set moves the overlay with it. The
    /// order matters because the first one ranks the line.
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

        // Effect lines are collected rather than added straight to the layout, because where
        // a line belongs is not something a branch can know; EffectOrder places them.
        bool consumable = CookingHint.IsConsumable(itemGameObj);

        List<EffectLine> effects = new();

        // Item.CarryWeight, the property, never the serialized field plus the modifier: the
        // property is what UpdateWeight adds up and it returns 0 for a -1 modifier. Weight is
        // then a status fraction like any other, and unsigned - it is not a change.
        float weight = item.CarryWeight * GameValues.StatusStep;
        layout.Add(Block.Weight, EffectFormatter.Plain(weight, "Weight"));

        // Each component is looked up rather than tested against in turn - see Handlers.
        Parts parts = new(layout, effects, item, consumable);

        for (int i = 0; i < itemComponents.Length; i++)
        {
            // A missing script serializes as a null entry, and Stone ships one. Cooking
            // switches actions off rather than removing them, so a disabled one is read as
            // absent - a cooked poisonous berry no longer inflicts its poison.
            if (itemComponents[i] == null)
            {
                continue;
            }

            if (itemComponents[i] is Behaviour behaviour && !behaviour.enabled)
            {
                continue;
            }

            // An OnConsumed action never fires on an item nobody can eat, and cooking adds
            // such actions to anything. One test here rather than in three handlers.
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

        // The cooking hint is decided after every handler has run, from the first ItemCooking
        // only - the one the game itself uses (the Infinite Rescue Claw carries two). A
        // disabled one is a cooked-off script and says nothing.
        ItemCooking? cooking = itemGameObj.GetComponent<ItemCooking>();
        if (cooking == null || cooking.enabled)
        {
            layout.Add(Block.Cooking, CookingHint.Describe(itemGameObj, cooking));
        }

        return layout.Render();
    }

    /// <summary>
    /// Which method describes which component. Looked up by type with a walk to the base
    /// (<see cref="HandlerFor"/>): the most derived entry wins, source order means nothing,
    /// and a subclass with no entry inherits its base's description. Not a chain of exact
    /// type tests, which lose every subclass silently, and not <c>is</c> tests, whose answer
    /// would depend on file order - see docs/internals_infra.md, "Building a description".
    ///
    /// Deliberately absent - Action_ReduceUses, Action_WarpToBiome, CactusBall,
    /// Action_SacrificeFriend, Action_BecomeSkeleton, Action_ConsumeAndSpawn - each for a
    /// reason given in docs/design.md, "What is deliberately not shown". Adding an item is
    /// one entry here plus one method; do not reintroduce a type test in Build.
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

        /// <summary>Whether the item can be eaten at all.</summary>
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
    /// Sorts the collected lines and writes them into the Effects section. Every ordering
    /// decision lives in <see cref="EffectOrder"/>; this only carries the result across.
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
    /// The most stamina a Shroomberry can carry, as a 0-1 fraction: 15 display units.
    /// Hardcoded, and it cannot be otherwise - the roll's range and the multiplier are both
    /// literals in the game's code. 0-15, not the wiki's 0-20: <c>Random.Range(0, 4)</c> is
    /// max-exclusive. See docs/internals_game.md, "Numbers nothing exposes".
    /// </summary>
    private const float MushroomStaminaPerRoll = 0.05f;
    private const float MaxMushroomStamina = 3 * MushroomStaminaPerRoll;

    /// <summary>
    /// A Shroomberry: what this berry is worth in stamina, and four question marks for the
    /// effect, coloured by whether its slot is guaranteed good, guaranteed bad, or a coin
    /// flip. A slot's valence is stable across runs because GenerateEffectList spends its
    /// quotas on the first slots in order. Neutral when MushroomManager is not up yet.
    /// </summary>
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

        // The span, not this berry's draw - a Shroomberry is a gamble, and the overlay gives
        // away no more than the question marks do. The spoiler setting prints the draw
        // instead, read the way RunAction reads it; a draw of zero leaves no line.
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

        // The first minGoodEffects slots are guaranteed good, the next minBadEffects
        // guaranteed bad, the rest a coin flip. No status of its own, so the marker trails
        // the stamina rather than claiming a place among the ranked lines.
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
        // The delay is not stated; the "/ 8s" suffix already says this is spread over time.
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
    /// Every other item the game gives the same name as this one - the game itself pairs a
    /// mushroom with its poisonous twin by name, so here the name is the game's own key
    /// rather than a proxy for behaviour. Built once and kept until a hot reload.
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
    /// What one thorn is worth, as a status fraction: the thorn's own damage times the ascent
    /// multiplier, read off a <c>ThornOnMe</c> on the observed character because the thorns
    /// belong to the character, not the item. Two increments at ascent zero, which is the
    /// fallback when nobody is there to read. Zero is a real answer - two custom-run switches
    /// make thorns inert - and returning it drops the line, since an effect that cannot
    /// happen should not be described. See docs/internals_game.md, "Status arithmetic".
    /// </summary>
    private static float ThornStatus()
    {
        const int MeasuredIncrementsPerThorn = 2;

        // AddThorn's own first test: with the hazard off nothing is ever stuck in.
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
                // Arrows share the pool. A thorn worth zero is the EtcDamage switch turned
                // off - an answer, not a reason to reach for the fallback.
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

        // Every deliberate poison cure is a spores cure of equal size - see CuresSporesToo.
        if (effect.statusType == CharacterAfflictions.STATUSTYPE.Poison && effect.changeAmount < 0f)
        {
            string spores = CharacterAfflictions.STATUSTYPE.Spores.ToString();
            Collect(parts.Effects, parts.Source, Change(effect, spores, parts),
                Onset.Instant, spores, effect.changeAmount);
        }
    }

    /// <summary>
    /// One status change, or nothing where its <c>ifSkeleton</c> gate is shut. The overlay
    /// asks the observed character the same question the action will, rather than hedging
    /// the line as "0/+50". The gate is open when the character's state and the item's
    /// skeleton toggle disagree, because the Book of Bones toggles you *first*. See
    /// docs/internals_game.md, "Actions and hooks".
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
    /// Action_ApplyAffliction carries both; only the Cursed Skull fills the extras, but a base
    /// handler dropping what a derived one keeps is the kind of gap that goes quiet.
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
        // Mandrake. Numbness is not a STATUSTYPE, hence the one shipped icon.
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
        // The feed hook: fires when one player feeds the dagger to another, and buffs
        // everybody except the one who was fed. Not reachable as an ItemAction.
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
        // No header and no ignoreCaster marker: the effects speak for themselves.
        Action_ApplyMassAffliction effect = (Action_ApplyMassAffliction)component;

        // A Magic Bugle re-fires this every tenth of a second for as long as it is tooted, so
        // the affliction's own half-second is no duration anybody experiences. The reach is
        // shown; the skull's 900 units means "everyone" and is not.
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
        // Whether it builds something you can cook on, asked of the built thing rather than
        // its name. The two Constructable subclasses reach here too and build no campfire.
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
        // Two distances: how far the cannon shoots is a raycast in units, how much rope that
        // leaves is a segment count.
        RopeShooter effect = (RopeShooter)component;

        // The anti-rope cannon has no flag of its own; Antigrav is what marks it.
        bool anti = parts.Item.GetComponent<Antigrav>() != null;

        parts.Layout.Add(Block.Custom, EffectFormatter.Colored(
            EffectFormatter.PeakMetres(effect.maxLength),
            anti ? "RopeCannonAnti" : "RopeCannon"));

        // Rope.GetLengthInMeters is what the spool's own UI shows and is not metres; matching
        // it is the default so the two never disagree in front of a player. The derived span
        // is a switch away. See docs/internals_game.md, "Rope geometry".
        float segment = RopeSegmentLength(effect);
        string ropeLength = PluginConfig.RealRopeLength.Value && segment > 0f
            ? EffectFormatter.PeakMetres(effect.length * segment)
            : EffectFormatter.Metres(Rope.GetLengthInMeters(effect.length));

        parts.Layout.Add(Block.Custom, EffectFormatter.Colored(
            ropeLength, anti ? "RopeSpoolAnti" : "RopeSpool"));
    }
    private static void DescribeVineShooter(Component component, Parts parts)
    {
        // Chain Launcher. maxLength is the raycast it fires along, in Unity units.
        VineShooter effect = (VineShooter)component;
        parts.Layout.Add(Block.Custom, ReachInUnits(effect.maxLength));
    }
    private static void DescribeMagicBean(Component component, Parts parts)
    {
        // maxLength is written into a localScale, but taken as plain units it matches what a
        // height-tracking mod measures on the grown vine. See docs/internals_game.md.
        MagicBean effect = (MagicBean)component;
        if (effect.plantPrefab != null)
        {
            parts.Layout.Add(Block.Custom, ReachInUnits(effect.plantPrefab.maxLength));
        }
    }
    private static void DescribeShelfShroom(Component component, Parts parts)
    {
        // Remedy Fungus: the only way to use it is to throw it, and the spawn is what heals.
        // No radius, because the reach is not what a player acts on.
        DescribeBlasts(((ShelfShroom)component).instantiateOnBreak, parts, showRange: false);
    }
    private static void DescribeBreakable(Component component, Parts parts)
    {
        // What a thrown item does when it shatters: only the non-item spawns, where an effect
        // lives, and only where throwing is the item's use - an Antidote shatters into its
        // cloud too, but an Antidote is for drinking.
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
    /// What still holding a stick of dynamite costs when the fuse runs out. Hardcoded, and it
    /// has to be: a literal in <c>Dynamite.Update</c> with nothing exposing it. See
    /// docs/internals_game.md, "Numbers nothing exposes".
    /// </summary>
    private const float HeldDynamiteInjury = 0.25f;

    private static void DescribeDynamite(Component component, Parts parts)
    {
        Dynamite effect = (Dynamite)component;

        // Two separate things happen: a flat cost for holding it, then the blast. This one
        // first and with no radius, because it is not an area effect at all.
        Collect(parts.Effects, parts.Source,
            EffectFormatter.Effect(HeldDynamiteInjury, "Injury"),
            Onset.Instant, "Injury", HeldDynamiteInjury);

        // The reach trails the amount, which also tells this line from the one above.
        DescribeBlasts(effect.explosionPrefab, parts, showRange: true);
    }
    private static void DescribeSpawn(Component component, Parts parts)
    {
        // Everything Sunscreen does lives on the thing it sprays, walked for what it holds
        // rather than reached by name. The cloud's own lifetime is deliberately not shown:
        // two durations on one item read as a puzzle.
        DescribeBlasts(((Action_Spawn)component).objectToSpawn, parts, showRange: false);
    }

    /// <summary>
    /// Everything the AOEs in a spawned prefab do: each status they move, as one hit or as a
    /// rate where the blast repeats, and any affliction they hand out. One reader for every
    /// item that spawns a blast, so no prefab change goes unread on one side.
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
        // Hiding the poison when the scorpion is dead was tried and reverted: mob state does
        // not update immediately on equip.
        Scorpion effect = (Scorpion)component;

        // InflictAttack's poison-over-time is max(0.5, (1 - statusSum) + 0.05) - more damage
        // the healthier you are - with an instant 0.025 folded in. Both bounds are literals in
        // the method body; see docs/internals_game.md, "Numbers nothing exposes".
        const float MinPoison = 0.5f;
        const float MaxPoison = 1.05f;
        Collect(parts.Effects, parts.Source, EffectFormatter.Colored(
                EffectFormatter.Scaled(MinPoison) + "-" + EffectFormatter.Scaled(MaxPoison), "Poison")
            + EffectColors.Neutral + " / " + EffectFormatter.Seconds(effect.totalPoisonTime) + "</color>",
            Onset.OverTime, "Poison", 1f);
    }
    // Reached by the Cactus's CactusBall through the base walk in HandlerFor - the only
    // StickyItemComponent on any item.
    private static void DescribeStickyItemComponent(Component component, Parts parts)
    {
        StickyItemComponent sticky = (StickyItemComponent)component;
        // Cactus. Charged to whoever it is stuck to, which includes its holder, so one line
        // says both. Thorn *increments*, not thorns - added straight to the count, unlike
        // Action_AddOrRemoveThorns. Custom, because a cactus has no use-action to be mistaken
        // for. addWeightToStuckPlayer is deliberately unread; no item sets it.
        parts.Layout.Add(Block.Custom,
            EffectFormatter.Effect(sticky.addThornsToStuckPlayer * GameValues.StatusStep, "Thorns"));
    }
    private static void DescribeBingBongShieldWhileHolding(Component component, Parts parts)
    {
        // Ancient Idol. The shield is re-applied faster than it lapses for as long as the idol
        // is held, so infinity is the honest amount. Custom, because the idol is never used;
        // coloured, because here the mark is the figure rather than a duration beside one.
        parts.Layout.Add(Block.Custom, EffectFormatter.Colored(EffectFormatter.Infinity, "Shield"));
    }
    private static void DescribeHealingGem(Component component, Parts parts)
    {
        Peak.Action_HealingGem effect = (Peak.Action_HealingGem)component;

        // One pool spread across every status it treats, hence the shared-budget form.
        // Ranked by the first status of its run, so the budget leads the amulet's lines.
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
        // The arrow is neutral like every arrow; the item glyphs wear the cream of a figure
        // belonging to no status.
        string generic = EffectColors.White + StatusIcons.Tag("Item") + "</color>";

        parts.Layout.Add(Block.Custom, generic
            + EffectColors.Neutral + EffectFormatter.Arrow + "</color>"
            + generic + generic);
        // Whole points on the game's 0-100 scale, so through WholePoints as fractions to stay
        // on the configured scale. Two discrete values (plain versus mystical), so a slash.
        Collect(parts.Effects, parts.Source, EffectColors.Get("Petrify") + "+"
            + EffectFormatter.WholePoints(effect.petrify / 100f) + "/"
            + EffectFormatter.WholePoints(effect.petrifyMystical / 100f)
            + " " + StatusIcons.Tag("Petrify") + "</color>",
            Onset.Instant, "Petrify", 1f);
    }
    private static void DescribeSuperJumpAmulet(Component component, Parts parts)
    {
        Peak.Action_SuperJumpAmulet superJump = (Peak.Action_SuperJumpAmulet)component;
        // Derives from Action_ApplyAffliction and its RunAction calls base.RunAction() before
        // charging petrify, so it carries a real affliction as well as a cost. This entry wins
        // over the base one in Handlers, so the affliction has to be read here too.
        CollectAfflictions(parts, superJump.affliction, superJump.extraAfflictions);
        // Petrify is floored to whole points on the way in: 0.075 is 7, not 7.5.
        if (superJump.petrifyPerUse != 0f)
        {
            Collect(parts.Effects, parts.Source, EffectFormatter.Colored(
                    "+" + EffectFormatter.WholePoints(superJump.petrifyPerUse), "Petrify"),
                Onset.Instant, "Petrify", superJump.petrifyPerUse);
        }
    }

    /// <summary>
    /// How far apart two segments of the rope this cannon fires sit, in Unity units, or zero
    /// if the prefab chain cannot be walked. A joint pins a point on its own body to a point
    /// on the connected one, so the gap is <b>both</b> offsets added - <c>anchor.y</c> plus
    /// <c>Rope.spacing</c> - and both are local-space, so the segment's scale applies. Reading
    /// either alone was wrong. See docs/internals_game.md, "Rope geometry".
    /// </summary>
    internal static float RopeSegmentLength(RopeShooter shooter)
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

    /// <summary>A distance held in Unity units, in the neutral colour.</summary>
    private static string ReachInUnits(float unityUnits) =>
        EffectColors.Neutral + EffectFormatter.PeakMetres(unityUnits) + "</color>";

    /// <summary>
    /// A lit lantern warms whoever is near it. Stated per second rather than as a total over
    /// the fuel, so a full lantern and a nearly-spent one read the same. The field is found
    /// by walking for one, not by two prefab names and two child paths.
    /// </summary>
    private static List<EffectLine> DescribeLantern(GameObject itemGameObj)
    {
        List<EffectLine> lines = new();

        StatusField? effect = itemGameObj.GetComponentInChildren<StatusField>(true);
        if (effect == null)
        {
            return lines;
        }

        // Every status in the field moves at the *main* rate: the additional statuses' own
        // per-second fields are serialized and never read by the game.
        Dictionary<CharacterAfflictions.STATUSTYPE, float> rates = new();
        Accumulate(rates, effect.statusType, effect.statusAmountPerSecond);
        foreach (StatusField.StatusFieldStatus status in effect.additionalStatuses)
        {
            Accumulate(rates, status.statusType, effect.statusAmountPerSecond);
        }

        if (rates.TryGetValue(CharacterAfflictions.STATUSTYPE.Poison, out float poison)
            && CuresSporesToo(CharacterAfflictions.STATUSTYPE.Poison, poison))
        {
            Accumulate(rates, CharacterAfflictions.STATUSTYPE.Spores, poison);
        }

        // One line per status; grouping by shared rate was tried and dropped.
        foreach (KeyValuePair<CharacterAfflictions.STATUSTYPE, float> rate in rates)
        {
            AddPerSecond(lines, rate.Value, rate.Key);
        }

        return lines;
    }

    /// <summary>
    /// True when taking this status down also takes Spores down by the same amount, which
    /// <c>SubtractStatus</c> does for every deliberate poison cure. One-way, and not applied
    /// to the passive decay. Stated once because three shapes of line need it. See
    /// docs/internals_game.md, "Status arithmetic".
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
    /// An emitter's <c>amount</c> is a per-second rate, but a banked one paid out in whole
    /// steps per tick, so it goes through <see cref="TickedRate"/>. Same shape as a lantern's
    /// field, and the same poison-to-spores coupling.
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
            // Ancestors are switched on by the thing that builds the prefab, so the walk
            // includes inactive objects; an emitter whose *own* object is off never runs
            // (the stovetop's HealRadius).
            if (!emitter.gameObject.activeSelf)
            {
                continue;
            }

            // A radius your chest cannot get inside on foot is an emitter nobody meets.
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
    /// Zero where nothing sets a lifetime. includeInactive is mandatory: everything walked
    /// here is a prefab asset, and nothing on one is active.
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
    /// What a repeating blast is worth over its whole life. Neither amount over period nor
    /// that times duration: the game banks each amount, pays out whole steps and throws the
    /// remainder away, and the payout on the final boundary never lands (a 15 second field
    /// pays 14 times). The distance factor is not applied here, because any factor between
    /// 0.5 and 1 lands a sub-step tick on the same step schedule. See docs/internals_game.md,
    /// "Status arithmetic".
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
