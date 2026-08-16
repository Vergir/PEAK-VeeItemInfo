using System.Collections.Generic;
using UnityEngine;

namespace VeeItemInfo;

/// <summary>
/// Builds the overlay text for an item by scanning every component on it.
///
/// Each branch decides only *what* it has to say and *which block* it belongs in;
/// <see cref="DescriptionLayout"/> owns the ordering and the spacing. Deciding the block is
/// a question of scope, not of effect type: if a line reads on its own it is Status, and if
/// a reader would ask "to whom?" or "when?" it is Others.
///
/// The goal is a display that needs no translation: numbers, signs and the game's own
/// status icons instead of sentences. The English that remains is either wrapped around a
/// real number or names who an effect applies to, and both still want symbol treatment.
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

        float weight = Ascents.itemWeightModifier > 0
            ? (item.carryWeight + Ascents.itemWeightModifier) * 2.5f
            : item.carryWeight * 2.5f;
        // Weight is a property of the item, not a change to your status, so it carries no
        // sign - just the number and the icon.
        layout.Add(Block.Weight, EffectFormatter.Plain(weight, "Weight"));

        for (int i = 0; i < itemComponents.Length; i++)
        {
            if (itemComponents[i].GetType() == typeof(Action_RestoreHunger))
            {
                Action_RestoreHunger effect = (Action_RestoreHunger)itemComponents[i];
                layout.Add(Block.Status, EffectFormatter.Effect(effect.restorationAmount * -1f, "Hunger"));
            }
            else if (itemComponents[i].GetType() == typeof(Action_GiveExtraStamina))
            {
                Action_GiveExtraStamina effect = (Action_GiveExtraStamina)itemComponents[i];
                layout.Add(Block.Status, EffectFormatter.Effect(effect.amount, "Extra Stamina"));
            }
            else if (itemComponents[i].GetType() == typeof(Action_InflictPoison))
            {
                Action_InflictPoison effect = (Action_InflictPoison)itemComponents[i];
                layout.Add(Block.Status, "AFTER " + effect.delay.ToString() + "s, "
                    + EffectFormatter.EffectOverTime(effect.poisonPerSecond, 1f, effect.inflictionTime, "Poison"));
            }
            else if (itemComponents[i].GetType() == typeof(Action_AddOrRemoveThorns))
            {
                Action_AddOrRemoveThorns effect = (Action_AddOrRemoveThorns)itemComponents[i];
                // UpdateWeight sets Thorns to 0.025 per *increment* returned by
                // GetTotalThornStatusIncrements, and in-game testing on Prickleberry shows a
                // thorn is worth two of those - 2 thorns read as 10, not 5. So 0.05 per thorn.
                layout.Add(Block.Status, EffectFormatter.Effect(effect.thornCount * 0.05f, "Thorns"));
            }
            else if (itemComponents[i].GetType() == typeof(Action_ModifyStatus))
            {
                Action_ModifyStatus effect = (Action_ModifyStatus)itemComponents[i];
                layout.Add(Block.Status, EffectFormatter.Effect(effect.changeAmount, effect.statusType.ToString()));
            }
            else if (itemComponents[i].GetType() == typeof(Action_ApplyAffliction))
            {
                Action_ApplyAffliction effect = (Action_ApplyAffliction)itemComponents[i];
                layout.Add(Block.Status, EffectFormatter.Affliction(effect.affliction));
            }
            else if (itemComponents[i].GetType() == typeof(Action_ClearAllStatus))
            {
                layout.Add(Block.Status, DescribeClearAllStatus((Action_ClearAllStatus)itemComponents[i]));
            }
            else if (itemComponents[i].GetType() == typeof(Action_ApplyMassAffliction))
            {
                Action_ApplyMassAffliction effect = (Action_ApplyMassAffliction)itemComponents[i];
                List<string> afflictions = new() { EffectFormatter.Affliction(effect.affliction) };
                for (int j = 0; j < effect.extraAfflictions.Length; j++)
                {
                    afflictions.Add(EffectFormatter.Affliction(effect.extraAfflictions[j]));
                }

                layout.Add(Block.Others, EffectColors.Neutral + "NEARBY PLAYERS WILL RECEIVE:</color>");
                layout.Add(Block.Others, DescriptionLayout.JoinList(afflictions));
            }
            else if (itemComponents[i].GetType() == typeof(Action_RaycastDart))
            {
                Action_RaycastDart effect = (Action_RaycastDart)itemComponents[i];
                List<string> afflictions = new();
                for (int j = 0; j < effect.afflictionsOnHit.Length; j++)
                {
                    afflictions.Add(EffectFormatter.Affliction(effect.afflictionsOnHit[j]));
                }

                layout.Add(Block.Others, EffectColors.Neutral + "SHOOT A DART THAT WILL APPLY:</color>");
                layout.Add(Block.Others, DescriptionLayout.JoinList(afflictions));
            }
            else if (itemComponents[i].GetType() == typeof(Lantern))
            {
                layout.Add(Block.Others, DescribeLantern((Lantern)itemComponents[i], itemGameObj));
            }
            else if (itemComponents[i].GetType() == typeof(Action_ReduceUses))
            {
                OptionableIntItemData uses = (OptionableIntItemData)item.data.data[DataEntryKey.ItemUses];
                if (uses.HasData && uses.Value > 1)
                {
                    layout.Add(Block.State, uses.Value + " USES");
                }
            }
            else if (itemComponents[i].GetType() == typeof(Constructable))
            {
                Constructable effect = (Constructable)itemComponents[i];
                if (effect.constructedPrefab.name.Equals("PortableStovetop_Placed"))
                {
                    layout.Add(Block.Note, "PLACE A " + EffectColors.Get("Injury") + "COOKING</color> STOVE FOR "
                        + effect.constructedPrefab.GetComponent<Campfire>().burnsFor.ToString() + "s");
                }
            }
            else if (itemComponents[i].GetType() == typeof(RopeSpool))
            {
                RopeSpool effect = (RopeSpool)itemComponents[i];
                layout.Add(Block.Note, (effect.isAntiRope ? "PLACE A ROPE THAT FLOATS UP\n" : "PLACE A ROPE\n")
                    + "FROM " + (effect.minSegments / 4f).ToString("F2").Replace(".0", "") + "m LONG, UP TO "
                    + EffectFormatter.Num(Rope.MaxSegments / 4f) + "m LONG");

                // Rope has no character distinction for Detach_Rpc(), so remaining length
                // rides the timed poll and is hidden when that poll is too slow to trust.
                if (PluginConfig.LiveValuesTrustworthy)
                {
                    layout.Add(Block.State, (effect.RopeFuel / 4f).ToString("F2").Replace(".00", "") + "m LEFT");
                }
            }
            else if (itemComponents[i].GetType() == typeof(RopeShooter))
            {
                RopeShooter effect = (RopeShooter)itemComponents[i];
                layout.Add(Block.Note, "SHOOT A ROPE ANCHOR WHICH PLACES\nA ROPE THAT "
                    + (effect.ropeAnchorWithRopePref.name.Equals("RopeAnchorForRopeShooterAnti") ? "FLOATS UP " : "DROPS DOWN ")
                    + EffectFormatter.Num(effect.maxLength / 4f) + "m");
            }
            else if (itemComponents[i].GetType() == typeof(VineShooter))
            {
                VineShooter effect = (VineShooter)itemComponents[i];
                layout.Add(Block.Note, "SHOOT A CHAIN THAT CONNECTS FROM\nYOUR POSITION TO WHERE YOU SHOOT\nUP TO "
                    + EffectFormatter.Num(effect.maxLength / (5f / 3f)) + "m AWAY");
            }
            else if (itemComponents[i].GetType() == typeof(MagicBean))
            {
                MagicBean effect = (MagicBean)itemComponents[i];
                layout.Add(Block.Note, EffectColors.Get("Hunger") + "THROW</color> TO PLANT A VINE THAT GROWS\nPERPENDICULAR TO TERRAIN UP TO\n"
                    + EffectFormatter.Num(effect.plantPrefab.maxLength / 2f) + "m OR UNTIL IT HITS SOMETHING");
            }
            else if (itemComponents[i].GetType() == typeof(Action_WarpToBiome))
            {
                Action_WarpToBiome effect = (Action_WarpToBiome)itemComponents[i];
                layout.Add(Block.Note, "WARP TO " + effect.segmentToWarpTo.ToString().ToUpper());
            }
            else if (itemComponents[i].GetType() == typeof(CactusBall))
            {
                CactusBall effect = (CactusBall)itemComponents[i];
                layout.Add(Block.Note, EffectColors.Get("Thorns") + "STICKS</color> TO YOUR BODY\nCAN " + EffectColors.Get("Hunger")
                    + "THROW</color> BY USING\nAT LEAST " + EffectFormatter.Scaled(effect.throwChargeRequirement) + "% POWER");
            }
            else if (itemComponents[i].GetType() == typeof(ShelfShroom))
            {
                layout.Add(Block.Others, DescribeHealingShroom((ShelfShroom)itemComponents[i]));
            }
            else if (itemComponents[i].GetType() == typeof(Action_MoraleBoost))
            {
                Action_MoraleBoost effect = (Action_MoraleBoost)itemComponents[i];
                string boost = EffectColors.Positive + "GAIN</color> " + EffectColors.Get("Extra Stamina")
                    + EffectFormatter.Scaled(effect.baselineStaminaBoost) + " EXTRA STAMINA</color>";

                // A negative radius means it boosts only the user, which is the one case
                // that belongs in Status rather than Others.
                if (effect.boostRadius < 0)
                {
                    layout.Add(Block.Status, boost);
                }
                else if (effect.boostRadius > 0)
                {
                    layout.Add(Block.Others, EffectColors.Neutral + "NEARBY PLAYERS</color> " + boost);
                }
            }
            else if (itemComponents[i].GetType() == typeof(Dynamite))
            {
                Dynamite effect = (Dynamite)itemComponents[i];
                layout.Add(Block.Others, EffectColors.Get("Injury") + "EXPLODES</color> FOR UP TO " + EffectColors.Get("Injury")
                    + EffectFormatter.Scaled(effect.explosionPrefab.GetComponent<AOE>().statusAmount) + " INJURY</color>\n"
                    + EffectColors.Neutral + "ADDITIONAL DAMAGE TAKEN IF HELD</color>");
            }
            else if (itemComponents[i].GetType() == typeof(Action_Spawn))
            {
                Action_Spawn effect = (Action_Spawn)itemComponents[i];
                if (effect.objectToSpawn.name.Equals("VFX_Sunscreen"))
                {
                    AOE effectAOE = effect.objectToSpawn.transform.Find("AOE").GetComponent<AOE>();
                    RemoveAfterSeconds effectTime = effect.objectToSpawn.transform.Find("AOE").GetComponent<RemoveAfterSeconds>();
                    layout.Add(Block.Others, EffectColors.Neutral + "SPRAY A " + EffectFormatter.Num(effectTime.seconds) + "s MIST THAT APPLIES:</color>\n"
                        + EffectFormatter.Affliction(effectAOE.affliction));
                }
            }
            else if (itemComponents[i].GetType() == typeof(Scorpion))
            {
                // Hiding the poison info when dead was tried and reverted: mob state does not
                // update immediately on equip, which produced a visual bug.
                Scorpion effect = (Scorpion)itemComponents[i];

                // Scorpion.InflictAttack (verified against 2.1.a) does two things: an instant
                // AddStatus(Poison, 0.025), then a poison-over-time affliction totalling
                // max(0.5, (1 - statusSum) + 0.05). statusSum runs 0..1, so the over-time part
                // spans 50 at full status to 105 at none - more damage the healthier you are.
                layout.Add(Block.Status, EffectFormatter.Colored("2.5", "Poison") + " + "
                    + EffectFormatter.Colored("50-105", "Poison") + " / " + EffectFormatter.Num(effect.totalPoisonTime) + "s");
            }
            else if (itemComponents[i].GetType() == typeof(Peak.Action_HealingGem))
            {
                Peak.Action_HealingGem effect = (Peak.Action_HealingGem)itemComponents[i];

                // Heals a shared budget across six statuses at once, so the amount is white
                // rather than any one status colour, and the icons say which are eligible.
                layout.Add(Block.Status, EffectColors.White + "-"
                    + EffectFormatter.Scaled(effect.healingAffliction.maxHealing) + "</color> "
                    + EffectFormatter.IconList(HealAllStatuses));

                if (effect.invincibilityAffliction != null)
                {
                    layout.Add(Block.Status, EffectFormatter.Num(effect.invincibilityAffliction.totalTime)
                        + "s " + EffectColors.Get("Shield") + StatusIcons.Tag("Shield") + "</color>");
                }

                // Petrify scales with how much healing was actually possible, clamped to
                // this range, so a range is the honest thing to show.
                layout.Add(Block.Status, EffectColors.Get("Petrify") + "+"
                    + EffectFormatter.Scaled(effect.minPetrify) + "-" + EffectFormatter.Scaled(effect.maxPetrify)
                    + "</color> " + StatusIcons.Tag("Petrify"));
            }
            else if (itemComponents[i].GetType() == typeof(Peak.Action_CloneSelectedItem))
            {
                Peak.Action_CloneSelectedItem effect = (Peak.Action_CloneSelectedItem)itemComponents[i];
                string generic = StatusIcons.Tag("Item");

                layout.Add(Block.Note, generic + EffectFormatter.Arrow + generic + generic);
                // AddPetrify takes whole points on the 0-100 scale, unlike almost everything
                // else here, so these are already display units. The two values are discrete
                // - plain items versus mystical ones - so a slash, not a range.
                layout.Add(Block.Status, EffectColors.Get("Petrify") + "+"
                    + EffectFormatter.Num(effect.petrify) + "/" + EffectFormatter.Num(effect.petrifyMystical)
                    + "</color> " + StatusIcons.Tag("Petrify"));
            }
            // Amulets are matched with 'is' rather than an exact type check: they all derive
            // from AmuletBase and each applies petrify through a different path.
            else if (itemComponents[i] is Peak.Action_SuperJumpAmulet superJump)
            {
                // Action_SuperJumpAmulet derives from Action_ApplyAffliction and its
                // RunAction calls base.RunAction() before charging petrify, so it carries a
                // real affliction as well as a cost. The Action_ApplyAffliction branch above
                // matches on exact type and so never sees a subclass - this is the only
                // place that affliction is read, and without it the amulets showed their
                // price and nothing they bought.
                layout.Add(Block.Status, EffectFormatter.Affliction(superJump.affliction));
                // AddStatus takes a 0-1 fraction, same scale as every other status.
                layout.Add(Block.Status, EffectFormatter.Effect(superJump.petrifyPerUse, "Petrify"));
            }
            // InfiniteStamAmulet and HealingAmulet are deliberately not handled yet. Their
            // petrify costs live in nested ItemPocketBehavior instances that tick while the
            // amulet sits in a pocket, which is a different thing from the cost of using it
            // - reporting the tick as if it were the use cost would be worse than silence.
        }

        return layout.Render();
    }

    private static string DescribeClearAllStatus(Action_ClearAllStatus effect)
    {
        string result = EffectColors.Positive + "CLEAR ALL STATUS</color>";
        if (effect.excludeCurse)
        {
            result += " EXCEPT " + EffectColors.Get("Curse") + "CURSE</color>";
        }

        foreach (CharacterAfflictions.STATUSTYPE exclusion in effect.otherExclusions)
        {
            result += ", " + EffectColors.Get(exclusion.ToString()) + exclusion.ToString().ToUpper() + "</color>";
        }

        // Crab is listed as an exclusion on everything but is not a status a player can see,
        // so it is dropped rather than cluttering the line.
        return result.Replace(", " + EffectColors.Get("Crab") + "CRAB</color>", "");
    }

    private static string DescribeLantern(Lantern lantern, GameObject itemGameObj)
    {
        string statuses = "";

        if (itemGameObj.name.Equals("Lantern_Faerie(Clone)"))
        {
            StatusField effect = itemGameObj.transform.Find("FaerieLantern/Light/Heat").GetComponent<StatusField>();
            List<string> parts = new()
            {
                EffectFormatter.EffectOverTime(effect.statusAmountPerSecond, 1f, lantern.startingFuel, effect.statusType.ToString()),
            };

            foreach (StatusField.StatusFieldStatus status in effect.additionalStatuses)
            {
                parts.Add(EffectFormatter.EffectOverTime(status.statusAmountPerSecond, 1f, lantern.startingFuel, status.statusType.ToString()));
            }

            statuses = DescriptionLayout.JoinList(parts);
        }
        else if (itemGameObj.name.Equals("Lantern(Clone)"))
        {
            StatusField effect = itemGameObj.transform.Find("GasLantern/Light/Heat").GetComponent<StatusField>();
            statuses = EffectFormatter.EffectOverTime(effect.statusAmountPerSecond, 1f, lantern.startingFuel, effect.statusType.ToString());
        }

        if (statuses.Trim('\n').Length == 0)
        {
            return "";
        }

        // A torch burns for itself; everything else warms whoever is nearby.
        string header = itemGameObj.name.Equals("Torch(Clone)")
            ? ""
            : EffectColors.Neutral + "WHEN LIT, NEARBY PLAYERS RECEIVE:</color>\n";

        return header + statuses;
    }

    private static string DescribeHealingShroom(ShelfShroom effect)
    {
        if (!effect.instantiateOnBreak.name.Equals("HealingPuffShroomSpawn"))
        {
            return "";
        }

        GameObject healing = effect.instantiateOnBreak.transform.Find("VFX_SporeHealingExplo").gameObject;
        AOE healingAOE = healing.GetComponent<AOE>();
        GameObject poison = healing.transform.Find("VFX_SporePoisonExplo").gameObject;
        AOE poisonAOE = poison.GetComponent<AOE>();
        AOE[] poisonAOEs = poison.GetComponents<AOE>();
        TimeEvent timeEvent = poison.GetComponent<TimeEvent>();
        RemoveAfterSeconds duration = poison.GetComponent<RemoveAfterSeconds>();

        // Values below were adjusted by hand - they calculate strangely, and may still be wrong.
        string result = EffectColors.Get("Hunger") + "THROW</color> TO RELEASE GAS THAT WILL:\n"
            + EffectFormatter.Effect(Mathf.Round(healingAOE.statusAmount * 0.9f * 40f) / 40f, healingAOE.statusType.ToString())
            + EffectFormatter.EffectOverTime(Mathf.Round(poisonAOE.statusAmount * (1f / timeEvent.rate) * 40f) / 40f, 1f, duration.seconds, poisonAOE.statusType.ToString());

        if (poisonAOEs.Length > 1)
        {
            // Not handled dynamically: there are 2 poison removal AOEs but one doesn't seem
            // to work, probably down to the time event rate.
            result += EffectFormatter.EffectOverTime(Mathf.Round(poisonAOEs[1].statusAmount * (1f / timeEvent.rate) * 40f) / 40f, 1f, duration.seconds + 1f, poisonAOEs[1].statusType.ToString());
        }

        return result;
    }
}
