using System.Collections.Generic;
using UnityEngine;

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
    /// What Affliction_HealAll treats, in its own order. maxHealing is a budget shared
    /// across all six rather than an allowance for each.
    /// </summary>
    private static readonly string[] HealAllStatuses =
    {
        "Injury", "Spores", "Poison", "Cold", "Hot", "Drowsy",
    };

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
        bool consumable = itemGameObj.GetComponent<Action_Consume>() != null;

        List<EffectLine> effects = new();

        float weight = Ascents.itemWeightModifier > 0
            ? (item.carryWeight + Ascents.itemWeightModifier) * 2.5f
            : item.carryWeight * 2.5f;
        // Weight is a property of the item, not a change to your status, so it carries no
        // sign - just the number and the icon.
        layout.Add(Block.Weight, EffectFormatter.Plain(weight, "Weight"));

        for (int i = 0; i < itemComponents.Length; i++)
        {
            // Cooking switches actions off rather than removing them -
            // CookingBehavior_DisableScripts sets enabled=false on the components it ruins.
            // Reading a disabled component is how a cooked poisonous berry kept advertising
            // poison it no longer inflicts.
            if (itemComponents[i] is Behaviour behaviour && !behaviour.enabled)
            {
                continue;
            }

            if (itemComponents[i].GetType() == typeof(Action_RestoreHunger))
            {
                Action_RestoreHunger effect = (Action_RestoreHunger)itemComponents[i];
                if (consumable || !effect.OnConsumed)
                {
                    Collect(effects, i, EffectFormatter.Effect(effect.restorationAmount * -1f, "Hunger"),
                        Onset.Instant, "Hunger", effect.restorationAmount * -1f);
                }
            }
            else if (itemComponents[i].GetType() == typeof(Action_GiveExtraStamina))
            {
                Action_GiveExtraStamina effect = (Action_GiveExtraStamina)itemComponents[i];
                if (consumable || !effect.OnConsumed)
                {
                    Collect(effects, i, EffectFormatter.Effect(effect.amount, "Extra Stamina"),
                        Onset.Instant, "Extra Stamina", effect.amount);
                }
            }
            else if (itemComponents[i].GetType() == typeof(Action_InflictPoison))
            {
                // The delay used to be spelled out as "AFTER 10s,". Dropped: the "/ 8s"
                // suffix already says this is spread over time, and the lead-in was the
                // only English on an otherwise symbolic line.
                Action_InflictPoison effect = (Action_InflictPoison)itemComponents[i];
                Collect(effects, i,
                    EffectFormatter.OverTime(effect.poisonPerSecond * effect.inflictionTime,
                        effect.inflictionTime, "Poison"),
                    Onset.OverTime, "Poison", effect.poisonPerSecond);
            }
            else if (itemComponents[i].GetType() == typeof(Action_AddOrRemoveThorns))
            {
                Action_AddOrRemoveThorns effect = (Action_AddOrRemoveThorns)itemComponents[i];
                // UpdateWeight sets Thorns to 0.025 per *increment* returned by
                // GetTotalThornStatusIncrements, and in-game testing on Prickleberry shows a
                // thorn is worth two of those - 2 thorns read as 10, not 5. So 0.05 per thorn.
                Collect(effects, i, EffectFormatter.Effect(effect.thornCount * 0.05f, "Thorns"),
                    Onset.Instant, "Thorns", effect.thornCount * 0.05f);
            }
            else if (itemComponents[i].GetType() == typeof(Action_ModifyStatus))
            {
                Action_ModifyStatus effect = (Action_ModifyStatus)itemComponents[i];
                if (consumable || !effect.OnConsumed)
                {
                    Collect(effects, i,
                        EffectFormatter.Effect(effect.changeAmount, effect.statusType.ToString()),
                        Onset.Instant, effect.statusType.ToString(), effect.changeAmount);

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
                        Collect(effects, i, EffectFormatter.Effect(effect.changeAmount, "Spores"),
                            Onset.Instant, "Spores", effect.changeAmount);
                    }
                }
            }
            else if (itemComponents[i].GetType() == typeof(Action_ApplyAffliction))
            {
                Action_ApplyAffliction effect = (Action_ApplyAffliction)itemComponents[i];
                Collect(effects, i, EffectFormatter.Affliction(effect.affliction));
            }
            else if (itemComponents[i].GetType() == typeof(Action_Numb))
            {
                // Mandrake. Numbness hides your stamina bar, which is the whole reason to
                // cook one first - and the only icon in the mod that had to be shipped
                // rather than scraped, because numbness is not a STATUSTYPE.
                Action_Numb effect = (Action_Numb)itemComponents[i];
                Collect(effects, i, EffectFormatter.Colored(EffectFormatter.Seconds(effect.numbAmount), "Numb"),
                    Onset.OverTime, "Numb", 1f);
            }
            else if (itemComponents[i].GetType() == typeof(Action_Die))
            {
                // Cursed Skull. Nothing else in the game does this, and no number describes
                // it - "the worst thing" is the whole message, so it leads in the Custom
                // section above everything the item gives everyone else.
                layout.Add(Block.Custom, EffectColors.Negative + "???</color>");
            }
            else if (itemComponents[i].GetType() == typeof(Peak.RitualDaggerFeedBehavior))
            {
                // The other half of the Ritual Dagger, and the reason the wiki lists effects
                // the item does not carry: RPC_RitualDaggerBuff runs on every client and
                // skips only the character who was fed the dagger, so everybody else in the
                // lobby - the feeder included - is healed and handed stamina.
                //
                // This is not reachable as an ItemAction. IExtraFeedBehavior is its own
                // hook, called when one player feeds an item to another, and
                // RitualDaggerFeedBehavior is the only thing in 2.1.a that implements it.
                Peak.RitualDaggerFeedBehavior effect = (Peak.RitualDaggerFeedBehavior)itemComponents[i];

                // ClearAllStatus() with no arguments, so curse and petrify are spared.
                Collect(effects, i, EffectFormatter.ClearedStatuses(true, null));

                // AddExtraStamina takes the same 0-1 fraction as a status.
                Collect(effects, i, EffectFormatter.Effect(effect.bonusStamina, "Extra Stamina"),
                    Onset.Instant, "Extra Stamina", effect.bonusStamina);

                if (effect.infiniteStaminaTime > 0f)
                {
                    Collect(effects, i, EffectFormatter.InfiniteStamina(effect.infiniteStaminaTime),
                        Onset.OverTime, "Extra Stamina", 1f);
                }
            }
            else if (itemComponents[i].GetType() == typeof(Action_RandomMushroomEffect))
            {
                // No status of its own - four question marks standing in for whatever the
                // berry rolls - so it trails the instant lines rather than claiming a place
                // among them.
                Collect(effects, i, DescribeMushroom((Action_RandomMushroomEffect)itemComponents[i]),
                    Onset.Instant);
            }
            else if (itemComponents[i].GetType() == typeof(Action_ClearAllStatus))
            {
                Action_ClearAllStatus effect = (Action_ClearAllStatus)itemComponents[i];
                Collect(effects, i,
                    EffectFormatter.ClearedStatuses(effect.excludeCurse, effect.otherExclusions));
            }
            else if (itemComponents[i].GetType() == typeof(Action_ApplyMassAffliction))
            {
                // The "NEARBY PLAYERS WILL RECEIVE:" header is gone. Nothing replaces it -
                // the effects speak for themselves, and a header was a whole line of English
                // for a distinction no item ever needs stated twice.
                Action_ApplyMassAffliction effect = (Action_ApplyMassAffliction)itemComponents[i];
                Collect(effects, i, EffectFormatter.Affliction(effect.affliction));
                for (int j = 0; j < effect.extraAfflictions.Length; j++)
                {
                    Collect(effects, i, EffectFormatter.Affliction(effect.extraAfflictions[j]));
                }
            }
            else if (itemComponents[i].GetType() == typeof(Action_RaycastDart))
            {
                Action_RaycastDart effect = (Action_RaycastDart)itemComponents[i];
                for (int j = 0; j < effect.afflictionsOnHit.Length; j++)
                {
                    Collect(effects, i, EffectFormatter.Affliction(effect.afflictionsOnHit[j]));
                }
            }
            else if (itemComponents[i].GetType() == typeof(Lantern))
            {
                Collect(effects, i, DescribeLantern(itemGameObj));
            }
            else if (itemComponents[i].GetType() == typeof(Constructable))
            {
                Constructable effect = (Constructable)itemComponents[i];
                if (effect.constructedPrefab.name.Equals("PortableStovetop_Placed"))
                {
                    Campfire campfire = effect.constructedPrefab.GetComponent<Campfire>();
                    layout.Add(Block.Custom, EffectColors.Neutral + EffectFormatter.Seconds(campfire.burnsFor)
                        + "</color> " + EffectColors.Get("Cook") + StatusIcons.Tag("Cook") + "</color>");
                }
            }
            else if (itemComponents[i].GetType() == typeof(RopeSpool))
            {
                // Only what is left on the spool. Printing the maximum too gave two bare
                // numbers in the same format with nothing to tell them apart, and the
                // maximum is the same on every spool anyway - it says nothing about the one
                // in your hand.
                //
                // Rope has no character distinction for Detach_Rpc(), so this rides the
                // timed poll and is hidden when that poll is too slow to trust.
                RopeSpool effect = (RopeSpool)itemComponents[i];
                if (PluginConfig.LiveValuesTrustworthy)
                {
                    layout.Add(Block.Custom, Reach(effect.RopeFuel / 4f));
                }
            }
            else if (itemComponents[i].GetType() == typeof(RopeShooter))
            {
                RopeShooter effect = (RopeShooter)itemComponents[i];
                layout.Add(Block.Custom, Reach(effect.maxLength / 4f));
            }
            else if (itemComponents[i].GetType() == typeof(VineShooter))
            {
                VineShooter effect = (VineShooter)itemComponents[i];
                layout.Add(Block.Custom, Reach(effect.maxLength / (5f / 3f)));
            }
            else if (itemComponents[i].GetType() == typeof(MagicBean))
            {
                MagicBean effect = (MagicBean)itemComponents[i];
                layout.Add(Block.Custom, Reach(effect.plantPrefab.maxLength / 2f));
            }
            else if (itemComponents[i].GetType() == typeof(ShelfShroom))
            {
                Collect(effects, i, DescribeHealingShroom((ShelfShroom)itemComponents[i]));
            }
            else if (itemComponents[i].GetType() == typeof(Action_MoraleBoost))
            {
                Action_MoraleBoost effect = (Action_MoraleBoost)itemComponents[i];
                Collect(effects, i, EffectFormatter.Effect(effect.baselineStaminaBoost, "Extra Stamina"),
                    Onset.Instant, "Extra Stamina", effect.baselineStaminaBoost);
            }
            else if (itemComponents[i].GetType() == typeof(Dynamite))
            {
                Dynamite effect = (Dynamite)itemComponents[i];
                float injury = effect.explosionPrefab.GetComponent<AOE>().statusAmount;
                Collect(effects, i, EffectFormatter.Effect(injury, "Injury"),
                    Onset.Instant, "Injury", injury);
            }
            else if (itemComponents[i].GetType() == typeof(Action_Spawn))
            {
                Action_Spawn effect = (Action_Spawn)itemComponents[i];
                if (effect.objectToSpawn.name.Equals("VFX_Sunscreen"))
                {
                    RemoveAfterSeconds duration = effect.objectToSpawn.transform.Find("AOE")
                        .GetComponent<RemoveAfterSeconds>();
                    Collect(effects, i,
                        EffectColors.Neutral + EffectFormatter.Seconds(duration.seconds) + "</color>",
                        Onset.OverTime);
                }
            }
            else if (itemComponents[i].GetType() == typeof(Scorpion))
            {
                // Hiding the poison info when dead was tried and reverted: mob state does not
                // update immediately on equip, which produced a visual bug.
                Scorpion effect = (Scorpion)itemComponents[i];

                // Scorpion.InflictAttack (verified against 2.1.a) does an instant
                // AddStatus(Poison, 0.025) then a poison-over-time totalling
                // max(0.5, (1 - statusSum) + 0.05). statusSum runs 0..1, so the over-time
                // part spans 50 at full status to 105 at none - more damage the healthier
                // you are. The instant 2.5 is folded in rather than shown separately.
                Collect(effects, i, EffectFormatter.Colored("50-105", "Poison")
                    + EffectColors.Neutral + " / " + EffectFormatter.Seconds(effect.totalPoisonTime) + "</color>",
                    Onset.OverTime, "Poison", 1f);
            }
            // 'is' rather than an exact match: CactusBall derives from StickyItemComponent
            // and is the only item carrying one in 2.1.a, so an exact check would read the
            // base class and describe nothing at all. Same trap that lost
            // Action_SuperJumpAmulet's affliction.
            else if (itemComponents[i] is StickyItemComponent sticky)
            {
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
                layout.Add(Block.Custom,
                    EffectFormatter.Effect(sticky.addThornsToStuckPlayer * 0.025f, "Thorns"));
            }
            else if (itemComponents[i].GetType() == typeof(BingBongShieldWhileHolding))
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
                layout.Add(Block.Custom, EffectFormatter.Colored(EffectFormatter.Infinity, "Shield"));
            }
            else if (itemComponents[i].GetType() == typeof(Peak.Action_HealingGem))
            {
                Peak.Action_HealingGem effect = (Peak.Action_HealingGem)itemComponents[i];

                // Heals a shared budget across six statuses at once, so the amount is white
                // rather than any one status colour, and the icons say which are eligible.
                // Slashes, not spaces: maxHealing is one pool spread across all six, not
                // an allowance for each. This is the only item in 2.1.a that works this way,
                // which is exactly why the slash form is reserved for it.
                // Ranked by the first status of its run, so the budget leads the amulet's
                // three lines rather than sorting after the petrify it costs.
                Collect(effects, i,
                    EffectFormatter.SharedBudget(-effect.healingAffliction.maxHealing, HealAllStatuses),
                    Onset.Instant, HealAllStatuses[0], -1f);

                if (effect.invincibilityAffliction != null)
                {
                    Collect(effects, i, EffectColors.Neutral
                        + EffectFormatter.Seconds(effect.invincibilityAffliction.totalTime) + "</color> "
                        + EffectColors.Get("Shield") + StatusIcons.Tag("Shield") + "</color>",
                        Onset.OverTime, "Shield", 1f);
                }

                // Petrify scales with how much healing was actually possible, clamped to
                // this range, so a range is the honest thing to show.
                Collect(effects, i, EffectColors.Get("Petrify") + "+"
                    + EffectFormatter.Scaled(effect.minPetrify) + "-" + EffectFormatter.Scaled(effect.maxPetrify)
                    + " " + StatusIcons.Tag("Petrify") + "</color>",
                    Onset.Instant, "Petrify", 1f);
            }
            else if (itemComponents[i].GetType() == typeof(Peak.Action_CloneSelectedItem))
            {
                Peak.Action_CloneSelectedItem effect = (Peak.Action_CloneSelectedItem)itemComponents[i];
                string generic = StatusIcons.Tag("Item");

                layout.Add(Block.Custom, generic + EffectFormatter.Arrow + generic + generic);
                // AddPetrify takes whole points on the 0-100 scale, unlike almost everything
                // else here, so these are already display units. The two values are discrete
                // - plain items versus mystical ones - so a slash, not a range.
                Collect(effects, i, EffectColors.Get("Petrify") + "+"
                    + EffectFormatter.Num(effect.petrify) + "/" + EffectFormatter.Num(effect.petrifyMystical)
                    + " " + StatusIcons.Tag("Petrify") + "</color>",
                    Onset.Instant, "Petrify", 1f);
            }
            // Amulets are matched with 'is' rather than an exact type check: they all derive
            // from AmuletBase and each applies petrify through a different path.
            else if (itemComponents[i] is Peak.Action_SuperJumpAmulet superJump)
            {
                // Action_SuperJumpAmulet derives from Action_ApplyAffliction and its
                // RunAction calls base.RunAction() before charging petrify, so it carries a
                // real affliction as well as a cost. The Action_ApplyAffliction branch above
                // matches on exact type and so never sees a subclass - this is the only
                // place that affliction is read.
                Collect(effects, i, EffectFormatter.Affliction(superJump.affliction));
                // AddStatus takes a 0-1 fraction, same scale as every other status.
                Collect(effects, i, EffectFormatter.Effect(superJump.petrifyPerUse, "Petrify"),
                    Onset.Instant, "Petrify", superJump.petrifyPerUse);
            }
            // 'is' rather than an exact match: ItemCooking declares UpdateCookedBehavior and
            // CookVisually virtual, so the game clearly anticipates subclasses even though
            // 2.1.a ships none. An exact check would silently drop the hint the day one appears.
            else if (itemComponents[i] is ItemCooking cooking)
            {
                layout.Add(Block.Cooking, CookingHint.Describe(cooking));
            }

            // Deliberately not handled any more, each for a reason worth keeping:
            //   Action_ReduceUses   - the "{n} USES" label was dropped outright.
            //   Action_WarpToBiome  - it does not warp you to a biome at all; it teleports
            //                         you to wherever the thrown fungus lands, so the old
            //                         "WARP TO <SEGMENT>" was wrong as well as wordy, and
            //                         there is no symbol for the real behaviour yet.
            //   CactusBall          - the throw-charge threshold has no agreed symbol; the
            //                         thorns it inflicts still come through Action_AddOrRemoveThorns.
            //   Action_SacrificeFriend - it kills whoever the dagger is *fed to*, never the
            //                         holder. The dagger carries no Action_Consume, so there
            //                         is no way to use it on yourself; the only path to
            //                         RunAction is RitualDaggerFeedBehavior calling
            //                         ConsumeDelayed once the item has changed hands. The
            //                         "???" mark means "the worst thing happens to you", so
            //                         it said the wrong thing here. There is no symbol yet
            //                         for a death that lands on someone else.
        }

        EmitEffects(layout, effects);
        return layout.Render();
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
    /// A Shroomberry. Four question marks for "something happens", coloured by whether this
    /// run rolled a good effect or a bad one for this berry.
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
    private static string DescribeMushroom(Action_RandomMushroomEffect effect)
    {
        const string Marks = "????";

        MushroomManager? manager = MushroomManager.instance;
        if (manager == null || manager.mushroomEffects == null || manager.mushroomEffects.Length == 0)
        {
            return EffectColors.White + Marks + "</color>";
        }

        int index = effect.mushroomTypeIndex % manager.mushroomEffects.Length;

        // GenerateEffectList fills the slots in order and spends its quotas first: the first
        // minGoodEffects slots are drawn from GoodEffects, the next minBadEffects from
        // BadEffects, and only then does it choose freely. So a berry's slot decides whether
        // its valence is guaranteed or a coin flip, and that holds across every run even
        // though the effects themselves are reshuffled each time.
        // RunAction also calls AddExtraStamina with mushroomStamAmt * 0.05, but in-game
        // testing says no stamina actually arrives - green and blue berries were advertising
        // a gain they do not give. Not shown until that is understood; the roll marker is the
        // honest part of this branch.
        string result;
        if (index < manager.minGoodEffects)
        {
            result = EffectColors.Positive + Marks + "</color>";
        }
        else if (index < manager.minGoodEffects + manager.minBadEffects)
        {
            result = EffectColors.Negative + Marks + "</color>";
        }
        else
        {
            // Past both quotas the roll is genuinely free, so the marker says "either" -
            // half green, half red - rather than committing to this run's outcome.
            result = EffectColors.Positive + "??</color>" + EffectColors.Negative + "??</color>";
        }

        return result;
    }

    /// <summary>A distance in metres, in the neutral colour. No unit space: "12.5m".</summary>
    private static string Reach(float metres) =>
        EffectColors.Neutral + EffectFormatter.Metres(metres) + "</color>";

    /// <summary>
    /// A lit lantern warms whoever is near it. Stated per second rather than as a total over
    /// the fuel, so the figure means the same thing on a full lantern and a nearly-spent one.
    /// </summary>
    private static List<EffectLine> DescribeLantern(GameObject itemGameObj)
    {
        List<EffectLine> lines = new();

        string path = itemGameObj.name.Equals("Lantern_Faerie(Clone)")
            ? "FaerieLantern/Light/Heat"
            : itemGameObj.name.Equals("Lantern(Clone)") ? "GasLantern/Light/Heat" : null!;

        if (path == null)
        {
            return lines;
        }

        Transform? heat = itemGameObj.transform.Find(path);
        StatusField? effect = heat != null ? heat.GetComponent<StatusField>() : null;
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
    /// Remedy Fungus. There is no eat-it effect at all - the only way to use it is to throw
    /// it, and the explosion is what heals - so every figure here is the thrown one.
    ///
    /// Every AOE in the prefab it breaks into, found by walking for the component rather than
    /// by name. The old walk reached through four hardcoded child names and threw when one of
    /// them went missing, which is exactly what happened: in 2.1.a the spawn holds a single
    /// healing AOE and the poison child the code went looking for is gone.
    /// </summary>
    private static List<EffectLine> DescribeHealingShroom(ShelfShroom effect)
    {
        List<EffectLine> lines = new();
        if (effect.instantiateOnBreak == null)
        {
            return lines;
        }

        foreach (AOE aoe in effect.instantiateOnBreak.GetComponentsInChildren<AOE>(true))
        {
            // A repeating TimeEvent on the same object turns a one-off burst into a field you
            // stand in. Remedy Fungus is both: one blast that heals as it goes off, and two
            // AOEs re-firing every half second for as long as the spawn lives.
            TimeEvent? repeat = aoe.GetComponent<TimeEvent>();
            bool ticking = repeat != null && repeat.repeating && repeat.rate > 0f;
            float seconds = ticking ? Lifetime(aoe.transform) : 0f;

            AddBlast(lines, aoe, aoe.statusType, aoe.statusAmount, repeat, seconds);

            for (int j = 0; aoe.addtlStatus != null && j < aoe.addtlStatus.Length; j++)
            {
                // Each additional status uses its own override where one is given, and the
                // main amount otherwise - the same fallback Explode does.
                float amount = aoe.addlStatusAmountOverrides != null
                    && j < aoe.addlStatusAmountOverrides.Count
                        ? aoe.addlStatusAmountOverrides[j]
                        : aoe.statusAmount;
                AddBlast(lines, aoe, aoe.addtlStatus[j], amount, repeat, seconds);
            }
        }

        return lines;
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
        CharacterAfflictions.STATUSTYPE statusType, float amount, TimeEvent? repeat, float seconds)
    {
        string status = statusType.ToString();

        if (repeat == null || !repeat.repeating || repeat.rate <= 0f)
        {
            Collect(lines, 0, EffectFormatter.Effect(Standing(aoe, amount), status),
                Onset.Instant, status, amount);
        }
        else
        {
            float total = TickedTotal(amount, repeat.rate, seconds);
            Collect(lines, 0, EffectFormatter.OverTime(total, seconds, status),
                Onset.OverTime, status, total);
        }

        if (CuresSporesToo(statusType, amount))
        {
            AddBlast(lines, aoe, CharacterAfflictions.STATUSTYPE.Spores, amount, repeat, seconds);
        }
    }

    /// <summary>
    /// The smallest change the status bars can actually record. CharacterAfflictions holds a
    /// running total per status and only spends it in whole units of this.
    /// </summary>
    private const float StatusStep = 0.025f;

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
    /// <see cref="Standing"/> is not needed for the ticking half and is not applied to it.
    /// </summary>
    private static float TickedTotal(float amount, float period, float seconds)
    {
        float perTick = Mathf.Abs(amount);
        if (perTick == 0f || period <= 0f || seconds <= 0f)
        {
            return 0f;
        }

        float payout;
        float every;
        if (perTick >= StatusStep)
        {
            // Big enough to pay out every tick, still losing whatever does not fill a step.
            payout = Mathf.Floor(perTick / StatusStep) * StatusStep;
            every = period;
        }
        else
        {
            // Too small to pay out alone, so it takes several ticks to reach one step.
            payout = StatusStep;
            every = Mathf.Ceil(StatusStep / perTick) * period;
        }

        float payouts = Mathf.Max(0f, Mathf.Ceil(seconds / every) - 1f);
        float total = payouts * payout;
        return amount < 0f ? -total : total;
    }

    /// <summary>
    /// What an explosion actually gives the person who set it off, rather than what its
    /// statusAmount says.
    ///
    /// AOE.Explode scales every amount by <c>GetFactor(dist) = (1 - dist/range)^factorPow</c>,
    /// and <c>dist</c> is measured to <c>character.Center</c> - your chest, not your feet. So
    /// the factor never reaches 1 no matter where you stand, and the full figure is a number
    /// nobody can ever be given. Standing on the blast leaves roughly a tenth of the range
    /// between you and it.
    ///
    /// The rounding is the game's own: <c>CharacterAfflictions.RoundStatus</c> snaps statuses
    /// to multiples of 1/40, which is 2.5 display units.
    ///
    /// This is jkqt's original formula, restored. It was removed as an unexplained haircut
    /// and put back when in-game testing confirmed the figure it produces - Remedy Fungus
    /// heals 17.5, not the 20 its AOE advertises. **The 0.9 is empirical**: it is a stand-in
    /// for a geometry the overlay cannot measure, and it is the one number in this file that
    /// no field in the game backs up.
    /// </summary>
    private static float Standing(AOE aoe, float amount) =>
        aoe.ignoreFactor ? amount : Mathf.Round(amount * 0.9f * 40f) / 40f;

}
