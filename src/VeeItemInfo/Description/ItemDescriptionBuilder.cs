using System;
using System.Collections.Generic;
using UnityEngine;

// A second, unrelated Affliction type exists in the global namespace.
using PeakAffliction = Peak.Afflictions.Affliction;

namespace VeeItemInfo;

/// <summary>
/// Builds the overlay text for an item from its components. Each handler says what it means;
/// <see cref="DescriptionLayout"/> and <see cref="EffectOrder"/> decide where it goes. See
/// docs/internals_infra.md, "Building a description".
/// </summary>
internal static class ItemDescriptionBuilder
{
    /// <summary>Read from the game's own list; the first one ranks the healing line.</summary>
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

        bool consumed = CookingHint.IsConsumed(itemGameObj);

        List<EffectLine> effects = new();

        // The CarryWeight property, never the field plus the modifier: it returns 0 for a -1
        // modifier, not one less.
        float weight = item.CarryWeight * GameValues.StatusStep;
        layout.Add(Block.Weight, EffectFormatter.Plain(weight, "Weight"));

        Parts parts = new(layout, effects, item, consumed);

        for (int i = 0; i < itemComponents.Length; i++)
        {
            // A missing script serializes as a null entry (Stone ships one), and cooking
            // disables actions rather than removing them.
            if (itemComponents[i] == null)
            {
                continue;
            }

            if (itemComponents[i] is Behaviour behaviour && !behaviour.enabled)
            {
                continue;
            }

            // An OnConsumed action never fires on an item nobody can eat, and cooking adds
            // such actions to anything.
            if (!consumed && itemComponents[i] is ItemAction action && action.OnConsumed)
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

        // The safe half of a mushroom pair shows its twin's poison as the same coin flip.
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

        // The first ItemCooking only, which is the one the game uses; a disabled one says nothing.
        ItemCooking? cooking = itemGameObj.GetComponent<ItemCooking>();
        if (cooking == null || cooking.enabled)
        {
            layout.Add(Block.Cooking, CookingHint.Describe(itemGameObj, cooking));
        }

        return layout.Render();
    }

    /// <summary>
    /// Looked up by type with a walk to the base: the most derived entry wins and a subclass
    /// with no entry inherits its base's. Never a chain of type tests. Components absent on
    /// purpose are listed in docs/design.md, "What is deliberately not shown".
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
        { typeof(Candle), DescribeLanternItem },
        { typeof(Constructable), DescribeConstructable },
        { typeof(RopeShooter), DescribeRopeShooter },
        { typeof(VineShooter), DescribeVineShooter },
        { typeof(Peak.Action_RaycastSpawnSomething), DescribeRaycastSpawnSomething },
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
        // ItemCooking is absent on purpose: the cooking hint is decided in Build after every
        // handler has run.
    };

    /// <summary>Cached lookups, including misses, which are the common case.</summary>
    private static readonly Dictionary<Type, Action<Component, Parts>?> Resolved = new();

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

    /// <summary>The state one description is assembled into, handed to every handler.</summary>
    private sealed class Parts
    {
        internal Parts(DescriptionLayout layout, List<EffectLine> effects, Item entity,
            bool consumed)
        {
            Layout = layout;
            Effects = effects;
            Entity = entity;
            Item = entity.gameObject;
            Consumed = consumed;
        }

        internal Item Entity { get; }

        internal DescriptionLayout Layout { get; }

        internal List<EffectLine> Effects { get; }

        internal GameObject Item { get; }

        internal bool Consumed { get; }

        /// <summary>Index of the component being described, for <see cref="EffectOrder"/>.</summary>
        internal int Source { get; set; }
    }

    /// <summary>Anything empty is dropped here so the handlers stay free of guards.</summary>
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

    private static void Collect(List<EffectLine> into, int source, List<EffectLine> lines)
    {
        foreach (EffectLine line in lines)
        {
            into.Add(line.WithSource(source));
        }
    }

    private static void EmitEffects(DescriptionLayout layout, List<EffectLine> effects)
    {
        EffectOrder.Sort(effects);

        foreach (EffectLine line in effects)
        {
            layout.Add(Block.Effects, line.Text);
        }
    }

    /// <summary>
    /// Literals in the game's code with nothing exposing them; the roll is Random.Range(0, 4),
    /// max-exclusive. See docs/internals_game.md, "Numbers nothing exposes".
    /// </summary>
    private const float MushroomStaminaPerRoll = 0.05f;
    private const float MaxMushroomStamina = 3 * MushroomStaminaPerRoll;

    /// <summary>The stamina span and "????" coloured by the slot's valence. See docs/internals_game.md, "Shroomberries".</summary>
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

        // The span unless spoiling; a dealt zero leaves no line.
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
        // guaranteed bad, the rest a coin flip.
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
            int rolled = manager.mushroomEffects[index];
            bool good = Array.IndexOf(Action_RandomMushroomEffect.GoodEffects, rolled) >= 0;
            marker = (good ? EffectColors.Positive : EffectColors.Negative) + Marks + "</color>";
        }
        else
        {
            // Half green, half red: "either".
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
        Action_InflictPoison effect = (Action_InflictPoison)component;
        float total = effect.poisonPerSecond * effect.inflictionTime;

        // A poisonous mushroom with a safe twin, when told not to tell them apart.
        string text = PluginConfig.HidePoisonTwins.Value && HasSafeTwin(parts.Entity)
            ? EffectFormatter.ConditionalOverTime(total, effect.inflictionTime, "Poison")
            : EffectFormatter.OverTime(total, effect.inflictionTime, "Poison");

        Collect(parts.Effects, parts.Source, text, Onset.OverTime, "Poison", effect.poisonPerSecond);
    }

    /// <summary>
    /// Every other item with the same display name. The game itself pairs a mushroom with its
    /// poisonous twin by name, so the name is the game's own key here.
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
    /// What one thorn is worth, read off a <c>ThornOnMe</c> on the observed character since
    /// thorns belong to the character. Zero is a real answer and drops the line. See
    /// docs/internals_game.md, "Status arithmetic".
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
                // Arrows share the pool. A thorn worth zero is the EtcDamage switch, not a
                // reason to fall back.
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
    /// Nothing where the <c>ifSkeleton</c> gate is shut for the observed character. The gate
    /// is open when the character's state and the item's skeleton toggle disagree, because
    /// the toggle runs first. See docs/internals_game.md, "Actions and hooks".
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

    private static bool IsSkeleton()
    {
        if (AssumeHuman)
        {
            return false;
        }

        Character observed = Character.observedCharacter;
        return observed != null && observed.data != null && observed.data.isSkeleton;
    }

    /// <summary>Set by the database dump, so prefabs are described for a human holder.</summary>
    internal static bool AssumeHuman { get; set; }

    private static void DescribeApplyAffliction(Component component, Parts parts)
    {
        Action_ApplyAffliction effect = (Action_ApplyAffliction)component;
        CollectAfflictions(parts, effect.affliction, effect.extraAfflictions);
    }

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
        Action_Numb effect = (Action_Numb)component;
        Collect(parts.Effects, parts.Source, EffectFormatter.Colored(EffectFormatter.Seconds(effect.numbAmount), "Numb"),
            Onset.OverTime, "Numb", 1f);
    }
    private static void DescribeDie(Component component, Parts parts)
    {
        // Cursed Skull: "the worst thing" is the whole message, so it leads in Custom.
        parts.Layout.Add(Block.Custom, EffectColors.Negative + "???</color>");
    }
    private static void DescribeRitualDaggerFeedBehavior(Component component, Parts parts)
    {
        // Fires when the dagger is fed to another player; buffs everybody except the one fed.
        Peak.RitualDaggerFeedBehavior effect = (Peak.RitualDaggerFeedBehavior)component;

        // ClearAllStatus() with no arguments, so curse and petrify are spared.
        Collect(parts.Effects, parts.Source, EffectFormatter.ClearedStatuses(true, null));

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
        Action_ApplyMassAffliction effect = (Action_ApplyMassAffliction)component;

        // A Magic Bugle re-fires this for as long as it is tooted, so the affliction's own
        // duration is not one anybody experiences; the reach is what matters.
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
        // Whether it builds something you can cook on, asked of the built prefab.
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

        Collect(parts.Effects, parts.Source, DescribeEmitters(effect.constructedPrefab));
    }
    private static void DescribeRopeShooter(Component component, Parts parts)
    {
        RopeShooter effect = (RopeShooter)component;

        // The anti-rope cannon has no flag of its own; Antigrav is what marks it.
        bool anti = parts.Item.GetComponent<Antigrav>() != null;

        parts.Layout.Add(Block.Custom, EffectFormatter.Colored(
            EffectFormatter.PeakMetres(effect.maxLength),
            anti ? "RopeCannonAnti" : "RopeCannon"));

        // Rope.GetLengthInMeters is the spool's own figure and not metres; it is the default
        // so the two never disagree in front of a player. See docs/internals_game.md, "Rope geometry".
        float segment = RopeSegmentLength(effect);
        string ropeLength = PluginConfig.RealRopeLength.Value && segment > 0f
            ? EffectFormatter.PeakMetres(effect.length * segment)
            : EffectFormatter.Metres(Rope.GetLengthInMeters(effect.length));

        parts.Layout.Add(Block.Custom, EffectFormatter.Colored(
            ropeLength, anti ? "RopeSpoolAnti" : "RopeSpool"));
    }
    private static void DescribeVineShooter(Component component, Parts parts)
    {
        VineShooter effect = (VineShooter)component;
        parts.Layout.Add(Block.Custom, ReachInUnits(effect.maxLength));
    }
    private static void DescribeRaycastSpawnSomething(Component component, Parts parts)
    {
        // The Anti-Zooka fires an antigravity bubble; the reach worth stating is how big the
        // bubble is, not how far the shot carries. Nothing else spawns this way, and a prefab
        // that is not a bubble says nothing rather than guessing at a size.
        Peak.Action_RaycastSpawnSomething effect = (Peak.Action_RaycastSpawnSomething)component;
        if (effect.prefabToSpawn == null)
        {
            return;
        }

        float radius = Blast.AntiSphereRadius(effect.prefabToSpawn);
        if (radius > 0f)
        {
            parts.Layout.Add(Block.Custom, ReachInUnits(radius));
        }
    }
    private static void DescribeMagicBean(Component component, Parts parts)
    {
        // maxLength is a scale in the game's code, yet plain units match what is measured in
        // game. See docs/internals_game.md, "Chain Launcher and Magic Bean".
        MagicBean effect = (MagicBean)component;
        if (effect.plantPrefab != null)
        {
            parts.Layout.Add(Block.Custom, ReachInUnits(effect.plantPrefab.maxLength));
        }
    }
    private static void DescribeShelfShroom(Component component, Parts parts)
    {
        // Remedy Fungus: the spawn is what heals. No radius; the reach is not what a player acts on.
        DescribeBlasts(((ShelfShroom)component).instantiateOnBreak, parts, showRange: false);
    }
    private static void DescribeBreakable(Component component, Parts parts)
    {
        // Only the non-item spawns, and only where throwing is the item's use - an Antidote
        // shatters into its cloud too, but an Antidote is for drinking.
        Breakable breakable = (Breakable)component;
        if (breakable.instantiateNonItemOnBreak == null || parts.Consumed
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
    /// <summary>A literal in <c>Dynamite.Update</c> with nothing exposing it.</summary>
    private const float HeldDynamiteInjury = 0.25f;

    private static void DescribeDynamite(Component component, Parts parts)
    {
        Dynamite effect = (Dynamite)component;

        // The flat cost for holding it comes first and carries no radius: not an area effect.
        Collect(parts.Effects, parts.Source,
            EffectFormatter.Effect(HeldDynamiteInjury, "Injury"),
            Onset.Instant, "Injury", HeldDynamiteInjury);

        DescribeBlasts(effect.explosionPrefab, parts, showRange: true);
    }
    private static void DescribeSpawn(Component component, Parts parts)
    {
        // Sunscreen: everything it does lives on the sprayed prefab. The cloud's own lifetime
        // is not shown; two durations on one item read as a puzzle.
        DescribeBlasts(((Action_Spawn)component).objectToSpawn, parts, showRange: false);
    }

    /// <summary>Every status an AOE moves, as one hit or a rate where it repeats, plus any affliction it hands out.</summary>
    private static void DescribeBlasts(GameObject? prefab, Parts parts, bool showRange)
    {
        if (prefab == null)
        {
            return;
        }

        foreach (AOE aoe in prefab.GetComponentsInChildren<AOE>(true))
        {
            // A repeating TimeEvent on the same object turns a burst into a field you stand in.
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
                // The same fallback Explode uses.
                float amount = aoe.addlStatusAmountOverrides != null
                    && j < aoe.addlStatusAmountOverrides.Count
                        ? aoe.addlStatusAmountOverrides[j]
                        : aoe.statusAmount;
                AddBlast(lines, aoe, aoe.addtlStatus[j], amount, repeat, seconds, reach);
            }

            if (aoe.hasAffliction && aoe.affliction != null)
            {
                lines.AddRange(EffectFormatter.Affliction(aoe.affliction));
            }

            Collect(parts.Effects, parts.Source, lines);
        }
    }
    private static void DescribeScorpion(Component component, Parts parts)
    {
        // Shown even when the scorpion is dead: mob state does not update on equip.
        Scorpion effect = (Scorpion)component;

        // Literals in InflictAttack with nothing exposing them; see docs/internals_game.md,
        // "Numbers nothing exposes".
        const float MinPoison = 0.5f;
        const float MaxPoison = 1.05f;
        Collect(parts.Effects, parts.Source, EffectFormatter.Colored(
                EffectFormatter.Scaled(MinPoison) + "-" + EffectFormatter.Scaled(MaxPoison), "Poison")
            + EffectColors.Neutral + " / " + EffectFormatter.Seconds(effect.totalPoisonTime) + "</color>",
            Onset.OverTime, "Poison", 1f);
    }
    private static void DescribeStickyItemComponent(Component component, Parts parts)
    {
        StickyItemComponent sticky = (StickyItemComponent)component;
        // Cactus. Thorn *increments*, unlike Action_AddOrRemoveThorns; charged to the holder
        // as well as to whoever it is thrown at. Custom, because it has no use-action.
        parts.Layout.Add(Block.Custom,
            EffectFormatter.Effect(sticky.addThornsToStuckPlayer * GameValues.StatusStep, "Thorns"));
    }
    private static void DescribeBingBongShieldWhileHolding(Component component, Parts parts)
    {
        // Ancient Idol. Re-applied faster than it lapses while held, so infinity is the
        // amount. Custom, because the idol is never used.
        parts.Layout.Add(Block.Custom, EffectFormatter.Colored(EffectFormatter.Infinity, "Shield"));
    }
    private static void DescribeHealingGem(Component component, Parts parts)
    {
        Peak.Action_HealingGem effect = (Peak.Action_HealingGem)component;

        // One pool across every status it treats. Ranked by the first, so the budget leads.
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

        // Petrify scales with how much healing was possible, clamped to this range.
        Collect(parts.Effects, parts.Source, EffectColors.Get("Petrify") + "+"
            + EffectFormatter.WholePoints(effect.minPetrify)
            + "-" + EffectFormatter.WholePoints(effect.maxPetrify)
            + " " + StatusIcons.Tag("Petrify") + "</color>",
            Onset.Instant, "Petrify", 1f);
    }
    private static void DescribeCloneSelectedItem(Component component, Parts parts)
    {
        Peak.Action_CloneSelectedItem effect = (Peak.Action_CloneSelectedItem)component;
        string generic = EffectColors.White + StatusIcons.Tag("Item") + "</color>";

        parts.Layout.Add(Block.Custom, generic
            + EffectColors.Neutral + EffectFormatter.Arrow + "</color>"
            + generic + generic);
        // These two are whole points already; two discrete values (plain versus mystical).
        Collect(parts.Effects, parts.Source, EffectColors.Get("Petrify") + "+"
            + EffectFormatter.WholePoints(effect.petrify / 100f) + "/"
            + EffectFormatter.WholePoints(effect.petrifyMystical / 100f)
            + " " + StatusIcons.Tag("Petrify") + "</color>",
            Onset.Instant, "Petrify", 1f);
    }
    private static void DescribeSuperJumpAmulet(Component component, Parts parts)
    {
        Peak.Action_SuperJumpAmulet superJump = (Peak.Action_SuperJumpAmulet)component;
        // Its RunAction calls the Action_ApplyAffliction base before charging petrify, and
        // this entry shadows the base one, so the affliction is read here too.
        CollectAfflictions(parts, superJump.affliction, superJump.extraAfflictions);
        if (superJump.petrifyPerUse != 0f)
        {
            Collect(parts.Effects, parts.Source, EffectFormatter.Colored(
                    "+" + EffectFormatter.WholePoints(superJump.petrifyPerUse), "Petrify"),
                Onset.Instant, "Petrify", superJump.petrifyPerUse);
        }
    }

    /// <summary>
    /// The gap between two rope segments in Unity units, or zero if the prefab chain cannot
    /// be walked: both joint offsets added, scaled by the segment. See
    /// docs/internals_game.md, "Rope geometry".
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

    private static string ReachInUnits(float unityUnits) =>
        EffectColors.Neutral + EffectFormatter.PeakMetres(unityUnits) + "</color>";

    /// <summary>Per second rather than a total over the fuel, so a nearly-spent lantern reads the same as a full one.</summary>
    private static List<EffectLine> DescribeLantern(GameObject itemGameObj)
    {
        List<EffectLine> lines = new();

        StatusField? effect = itemGameObj.GetComponentInChildren<StatusField>(true);
        if (effect == null)
        {
            return lines;
        }

        // Every status in the field moves at the *main* rate; the game never reads the
        // additional statuses' own per-second fields.
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

        foreach (KeyValuePair<CharacterAfflictions.STATUSTYPE, float> rate in rates)
        {
            AddPerSecond(lines, rate.Value, rate.Key);
        }

        return lines;
    }

    /// <summary>
    /// Every deliberate poison cure takes the same amount off Spores. See
    /// docs/internals_game.md, "Status arithmetic".
    /// </summary>
    private static bool CuresSporesToo(CharacterAfflictions.STATUSTYPE statusType, float amount) =>
        statusType == CharacterAfflictions.STATUSTYPE.Poison && amount < 0f;

    /// <summary>A field naming the same status twice moves it twice as fast.</summary>
    private static void Accumulate(Dictionary<CharacterAfflictions.STATUSTYPE, float> rates,
        CharacterAfflictions.STATUSTYPE statusType, float amount)
    {
        rates[statusType] = rates.TryGetValue(statusType, out float running) ? running + amount : amount;
    }

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

    /// <summary>What a built thing does to whoever stands near it, per second - a stovetop's warmth.</summary>
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
            // Ancestors are switched on by the thing that builds the prefab; an emitter whose
            // *own* object is off never runs.
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

    /// <summary>The rate a banked repeating amount actually moves a status at.</summary>
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
    /// floored amount every period, or one step every however many periods reach one.
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
            return Mathf.Floor(perTick / GameValues.StatusStep) * GameValues.StatusStep;
        }

        every = Mathf.Ceil(GameValues.StatusStep / perTick) * period;
        return GameValues.StatusStep;
    }

    /// <summary>From the nearest RemoveAfterSeconds at or above; zero where nothing sets one.</summary>
    private static float Lifetime(Transform spawned)
    {
        RemoveAfterSeconds? removeAfter = spawned.GetComponentInParent<RemoveAfterSeconds>(true);
        return removeAfter != null ? removeAfter.seconds : 0f;
    }

    /// <summary>One status an explosion moves, plus the spores that come free with a poison cure.</summary>
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
    /// What a repeating blast is worth over its life: payouts counted, since the one on the
    /// final boundary never lands. No distance factor, since any factor between 0.5 and 1
    /// lands a sub-step tick on the same schedule. See docs/internals_game.md, "Status arithmetic".
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
