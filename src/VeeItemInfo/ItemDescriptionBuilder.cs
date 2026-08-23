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

        // Effect lines are collected rather than added straight to the layout so instant
        // changes read before timed ones. Energy Drink takes 100 Drowsy off you and hands 25
        // back when its boost ends; printed the other way round the overlay says the opposite
        // of what happens.
        //
        // Actions flagged OnConsumed only fire when the item is eaten or drunk, which means
        // an item with no Action_Consume never runs them at all. Cooking an amulet adds an
        // Action_GiveExtraStamina through ItemCooking.ChangeStatsCooked regardless, so a
        // cooked Scout's Ambition was advertising +10 stamina it can never hand out.
        bool consumable = itemGameObj.GetComponent<Action_Consume>() != null;

        List<string> primary = new();
        List<string> instant = new();
        List<string> timed = new();

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
                    Collect(primary, EffectFormatter.Effect(effect.restorationAmount * -1f, "Hunger"));
                }
            }
            else if (itemComponents[i].GetType() == typeof(Action_GiveExtraStamina))
            {
                Action_GiveExtraStamina effect = (Action_GiveExtraStamina)itemComponents[i];
                if (consumable || !effect.OnConsumed)
                {
                    Collect(primary, EffectFormatter.Effect(effect.amount, "Extra Stamina"));
                }
            }
            else if (itemComponents[i].GetType() == typeof(Action_InflictPoison))
            {
                // The delay used to be spelled out as "AFTER 10s,". Dropped: the "/ 8s"
                // suffix already says this is spread over time, and the lead-in was the
                // only English on an otherwise symbolic line.
                Action_InflictPoison effect = (Action_InflictPoison)itemComponents[i];
                Collect(timed,
                    EffectFormatter.EffectOverTime(effect.poisonPerSecond, 1f, effect.inflictionTime, "Poison"));
            }
            else if (itemComponents[i].GetType() == typeof(Action_AddOrRemoveThorns))
            {
                Action_AddOrRemoveThorns effect = (Action_AddOrRemoveThorns)itemComponents[i];
                // UpdateWeight sets Thorns to 0.025 per *increment* returned by
                // GetTotalThornStatusIncrements, and in-game testing on Prickleberry shows a
                // thorn is worth two of those - 2 thorns read as 10, not 5. So 0.05 per thorn.
                Collect(instant, EffectFormatter.Effect(effect.thornCount * 0.05f, "Thorns"));
            }
            else if (itemComponents[i].GetType() == typeof(Action_ModifyStatus))
            {
                Action_ModifyStatus effect = (Action_ModifyStatus)itemComponents[i];
                if (consumable || !effect.OnConsumed)
                {
                    Collect(instant, EffectFormatter.Effect(effect.changeAmount, effect.statusType.ToString()));

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
                        Collect(instant, EffectFormatter.Effect(effect.changeAmount, "Spores"));
                    }
                }
            }
            else if (itemComponents[i].GetType() == typeof(Action_ApplyAffliction))
            {
                Action_ApplyAffliction effect = (Action_ApplyAffliction)itemComponents[i];
                Collect(timed, EffectFormatter.Affliction(effect.affliction));
            }
            else if (itemComponents[i].GetType() == typeof(Action_Numb))
            {
                // Mandrake. Numbness hides your stamina bar, which is the whole reason to
                // cook one first - and the only icon in the mod that had to be shipped
                // rather than scraped, because numbness is not a STATUSTYPE.
                Action_Numb effect = (Action_Numb)itemComponents[i];
                Collect(timed, EffectFormatter.Colored(EffectFormatter.Seconds(effect.numbAmount), "Numb"));
            }
            else if (itemComponents[i].GetType() == typeof(Action_Die))
            {
                // Cursed Skull. Nothing else in the game does this, and no number describes
                // it - "the worst thing" is the whole message, so it leads in the Custom
                // section above everything the item gives everyone else.
                layout.Add(Block.Custom, EffectColors.Negative + "???</color>");
            }
            else if (itemComponents[i].GetType() == typeof(Action_RandomMushroomEffect))
            {
                Collect(instant, DescribeMushroom((Action_RandomMushroomEffect)itemComponents[i]));
            }
            else if (itemComponents[i].GetType() == typeof(Action_ClearAllStatus))
            {
                Action_ClearAllStatus effect = (Action_ClearAllStatus)itemComponents[i];
                Collect(instant,
                    EffectFormatter.ClearedStatuses(effect.excludeCurse, effect.otherExclusions));
            }
            else if (itemComponents[i].GetType() == typeof(Action_ApplyMassAffliction))
            {
                // The "NEARBY PLAYERS WILL RECEIVE:" header is gone. Nothing replaces it -
                // the effects speak for themselves, and a header was a whole line of English
                // for a distinction no item ever needs stated twice.
                Action_ApplyMassAffliction effect = (Action_ApplyMassAffliction)itemComponents[i];
                Collect(timed, EffectFormatter.Affliction(effect.affliction));
                for (int j = 0; j < effect.extraAfflictions.Length; j++)
                {
                    Collect(timed, EffectFormatter.Affliction(effect.extraAfflictions[j]));
                }
            }
            else if (itemComponents[i].GetType() == typeof(Action_RaycastDart))
            {
                Action_RaycastDart effect = (Action_RaycastDart)itemComponents[i];
                for (int j = 0; j < effect.afflictionsOnHit.Length; j++)
                {
                    Collect(timed, EffectFormatter.Affliction(effect.afflictionsOnHit[j]));
                }
            }
            else if (itemComponents[i].GetType() == typeof(Lantern))
            {
                Collect(timed, DescribeLantern(itemGameObj));
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
                Collect(instant, DescribeHealingShroom((ShelfShroom)itemComponents[i]));
            }
            else if (itemComponents[i].GetType() == typeof(Action_MoraleBoost))
            {
                Action_MoraleBoost effect = (Action_MoraleBoost)itemComponents[i];
                Collect(instant, EffectFormatter.Effect(effect.baselineStaminaBoost, "Extra Stamina"));
            }
            else if (itemComponents[i].GetType() == typeof(Dynamite))
            {
                Dynamite effect = (Dynamite)itemComponents[i];
                Collect(instant, EffectFormatter.Effect(
                    effect.explosionPrefab.GetComponent<AOE>().statusAmount, "Injury"));
            }
            else if (itemComponents[i].GetType() == typeof(Action_Spawn))
            {
                Action_Spawn effect = (Action_Spawn)itemComponents[i];
                if (effect.objectToSpawn.name.Equals("VFX_Sunscreen"))
                {
                    RemoveAfterSeconds duration = effect.objectToSpawn.transform.Find("AOE")
                        .GetComponent<RemoveAfterSeconds>();
                    Collect(timed,
                        EffectColors.Neutral + EffectFormatter.Seconds(duration.seconds) + "</color>");
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
                Collect(timed, EffectFormatter.Colored("50-105", "Poison")
                    + EffectColors.Neutral + " / " + EffectFormatter.Seconds(effect.totalPoisonTime) + "</color>");
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
                Collect(instant,
                    EffectFormatter.SharedBudget(-effect.healingAffliction.maxHealing, HealAllStatuses));

                if (effect.invincibilityAffliction != null)
                {
                    Collect(timed, EffectColors.Neutral
                        + EffectFormatter.Seconds(effect.invincibilityAffliction.totalTime) + "</color> "
                        + EffectColors.Get("Shield") + StatusIcons.Tag("Shield") + "</color>");
                }

                // Petrify scales with how much healing was actually possible, clamped to
                // this range, so a range is the honest thing to show.
                Collect(instant, EffectColors.Get("Petrify") + "+"
                    + EffectFormatter.Scaled(effect.minPetrify) + "-" + EffectFormatter.Scaled(effect.maxPetrify)
                    + " " + StatusIcons.Tag("Petrify") + "</color>");
            }
            else if (itemComponents[i].GetType() == typeof(Peak.Action_CloneSelectedItem))
            {
                Peak.Action_CloneSelectedItem effect = (Peak.Action_CloneSelectedItem)itemComponents[i];
                string generic = StatusIcons.Tag("Item");

                layout.Add(Block.Custom, generic + EffectFormatter.Arrow + generic + generic);
                // AddPetrify takes whole points on the 0-100 scale, unlike almost everything
                // else here, so these are already display units. The two values are discrete
                // - plain items versus mystical ones - so a slash, not a range.
                Collect(instant, EffectColors.Get("Petrify") + "+"
                    + EffectFormatter.Num(effect.petrify) + "/" + EffectFormatter.Num(effect.petrifyMystical)
                    + " " + StatusIcons.Tag("Petrify") + "</color>");
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
                Collect(timed, EffectFormatter.Affliction(superJump.affliction));
                // AddStatus takes a 0-1 fraction, same scale as every other status.
                Collect(instant, EffectFormatter.Effect(superJump.petrifyPerUse, "Petrify"));
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
        }

        EmitEffects(layout, primary, instant, timed);
        return layout.Render();
    }

    /// <summary>Appends a line if it has anything in it. Keeps the branches free of guards.</summary>
    private static void Collect(List<string> into, string? line)
    {
        if (!string.IsNullOrEmpty(line))
        {
            into.Add(line!.Trim('\n'));
        }
    }

    /// <summary>
    /// Writes the collected effect lines into the layout in reading order: what the item
    /// does to your bars first, then anything that unfolds over time.
    ///
    /// Ordering is not cosmetic. Energy Drink strips 100 Drowsy the moment you drink it and
    /// hands 25 back when the boost runs out; printed the other way round the overlay says
    /// the opposite of what happens.
    /// </summary>
    private static void EmitEffects(DescriptionLayout layout, List<string> primary, List<string> instant,
        List<string> timed)
    {
        foreach (string line in primary)
        {
            layout.Add(Block.Effects, line);
        }

        foreach (string line in instant)
        {
            layout.Add(Block.Effects, line);
        }

        foreach (string line in timed)
        {
            layout.Add(Block.Effects, line);
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
    private static string DescribeLantern(GameObject itemGameObj)
    {
        string path = itemGameObj.name.Equals("Lantern_Faerie(Clone)")
            ? "FaerieLantern/Light/Heat"
            : itemGameObj.name.Equals("Lantern(Clone)") ? "GasLantern/Light/Heat" : null!;

        if (path == null)
        {
            return "";
        }

        Transform? heat = itemGameObj.transform.Find(path);
        StatusField? effect = heat != null ? heat.GetComponent<StatusField>() : null;
        if (effect == null)
        {
            return "";
        }

        // One line per status. Grouping by shared rate was tried and dropped for consistency
        // with every other effect in the overlay - the Faerie Lantern is taller for it, but a
        // reader no longer has to learn a second way of reading a line.
        List<string> lines = new()
        {
            EffectFormatter.PerSecond(effect.statusAmountPerSecond, effect.statusType.ToString()),
        };

        foreach (StatusField.StatusFieldStatus status in effect.additionalStatuses)
        {
            lines.Add(EffectFormatter.PerSecond(status.statusAmountPerSecond, status.statusType.ToString()));
        }

        lines.RemoveAll(string.IsNullOrEmpty);
        return string.Join("\n", lines);
    }

    private static void Collect(Dictionary<float, List<string>> byRate, float rate, string status)
    {
        if (rate == 0f)
        {
            return;
        }

        if (!byRate.TryGetValue(rate, out List<string>? statuses))
        {
            statuses = new List<string>();
            byRate[rate] = statuses;
        }

        statuses.Add(status);
    }

    /// <summary>
    /// Remedy Fungus. There is no eat-it effect at all - the only way to use it is to throw
    /// it, and the explosion is what heals - so every figure here is the thrown one.
    /// </summary>
    private static string DescribeHealingShroom(ShelfShroom effect)
    {
        if (!effect.instantiateOnBreak.name.Equals("HealingPuffShroomSpawn"))
        {
            return "";
        }

        GameObject healing = effect.instantiateOnBreak.transform.Find("VFX_SporeHealingExplo").gameObject;
        AOE healingAOE = healing.GetComponent<AOE>();
        GameObject poison = healing.transform.Find("VFX_SporePoisonExplo").gameObject;
        AOE[] poisonAOEs = poison.GetComponents<AOE>();
        TimeEvent timeEvent = poison.GetComponent<TimeEvent>();
        RemoveAfterSeconds duration = poison.GetComponent<RemoveAfterSeconds>();

        // Values below were adjusted by hand - they calculate strangely, and may still be
        // wrong. See BACKLOG 7.
        List<string> lines = new()
        {
            EffectFormatter.Effect(Mathf.Round(healingAOE.statusAmount * 0.9f * 40f) / 40f,
                healingAOE.statusType.ToString()),
        };

        foreach (AOE poisonAOE in poisonAOEs)
        {
            lines.Add(EffectFormatter.EffectOverTime(
                Mathf.Round(poisonAOE.statusAmount * (1f / timeEvent.rate) * 40f) / 40f,
                1f, duration.seconds, poisonAOE.statusType.ToString()));
        }

        return string.Join("", lines);
    }
}
