using UnityEngine;

namespace VeeItemInfo;

/// <summary>
/// Builds the overlay text for an item by scanning every component on it.
///
/// The goal is a display that needs no translation: numbers, signs and the game's own
/// status icons instead of sentences. English prose that carried no information has been
/// removed. What remains is either wrapped around a real number (rope lengths, blast
/// damage, charge thresholds) or labels who an effect applies to, and both are still
/// waiting to be reworked into symbols.
///
/// Note the component chain matches with GetType() == typeof(T), which is exact - a
/// subclass will not match. That is the most likely reason for an item silently losing its
/// description after a game update. Amulets are the exception and use 'is', because they
/// all derive from AmuletBase.
/// </summary>
internal static class ItemDescriptionBuilder
{
    internal static string Build(Item item)
    {
        GameObject itemGameObj = item.gameObject;
        Component[] itemComponents = itemGameObj.GetComponents(typeof(Component));
        bool isConsumable = false;
        string body = "";
        string prefixStatus = "";
        string suffixUses = "";
        string suffixAfflictions = "";

        float weight = Ascents.itemWeightModifier > 0
            ? (item.carryWeight + Ascents.itemWeightModifier) * 2.5f
            : item.carryWeight * 2.5f;
        // Weight is a property of the item, not a change to your status, so it carries no
        // sign - just the number and the icon.
        string suffixWeight = EffectFormatter.Plain(weight, "Weight");

        for (int i = 0; i < itemComponents.Length; i++)
        {
            if (itemComponents[i].GetType() == typeof(ItemUseFeedback))
            {
                ItemUseFeedback itemUseFeedback = (ItemUseFeedback)itemComponents[i];
                if (itemUseFeedback.useAnimation.Equals("Eat") || itemUseFeedback.useAnimation.Equals("Drink") || itemUseFeedback.useAnimation.Equals("Heal"))
                {
                    isConsumable = true;
                }
            }
            else if (itemComponents[i].GetType() == typeof(Action_Consume))
            {
                isConsumable = true;
            }
            else if (itemComponents[i].GetType() == typeof(Action_RestoreHunger))
            {
                Action_RestoreHunger effect = (Action_RestoreHunger)itemComponents[i];
                prefixStatus += EffectFormatter.Effect(effect.restorationAmount * -1f, "Hunger");
            }
            else if (itemComponents[i].GetType() == typeof(Action_GiveExtraStamina))
            {
                Action_GiveExtraStamina effect = (Action_GiveExtraStamina)itemComponents[i];
                prefixStatus += EffectFormatter.Effect(effect.amount, "Extra Stamina");
            }
            else if (itemComponents[i].GetType() == typeof(Action_InflictPoison))
            {
                Action_InflictPoison effect = (Action_InflictPoison)itemComponents[i];
                prefixStatus += "AFTER " + effect.delay.ToString() + "s, "
                    + EffectFormatter.EffectOverTime(effect.poisonPerSecond, 1f, effect.inflictionTime, "Poison");
            }
            else if (itemComponents[i].GetType() == typeof(Action_AddOrRemoveThorns))
            {
                Action_AddOrRemoveThorns effect = (Action_AddOrRemoveThorns)itemComponents[i];
                // UpdateWeight sets Thorns to 0.025 per *increment* returned by
                // GetTotalThornStatusIncrements, and in-game testing on Prickleberry shows a
                // thorn is worth two of those - 2 thorns read as 10, not 5. So 0.05 per thorn.
                prefixStatus += EffectFormatter.Effect(effect.thornCount * 0.05f, "Thorns");
            }
            else if (itemComponents[i].GetType() == typeof(Action_ModifyStatus))
            {
                Action_ModifyStatus effect = (Action_ModifyStatus)itemComponents[i];
                prefixStatus += EffectFormatter.Effect(effect.changeAmount, effect.statusType.ToString());
            }
            else if (itemComponents[i].GetType() == typeof(Action_ApplyMassAffliction))
            {
                Action_ApplyMassAffliction effect = (Action_ApplyMassAffliction)itemComponents[i];
                suffixAfflictions += EffectColors.Neutral + "NEARBY PLAYERS WILL RECEIVE:</color>\n";
                suffixAfflictions += EffectFormatter.Affliction(effect.affliction);
                if (effect.extraAfflictions.Length > 0)
                {
                    for (int j = 0; j < effect.extraAfflictions.Length; j++)
                    {
                        suffixAfflictions = TrimTrailingNewline(suffixAfflictions);
                        suffixAfflictions += ",\n" + EffectFormatter.Affliction(effect.extraAfflictions[j]);
                    }
                }
            }
            else if (itemComponents[i].GetType() == typeof(Action_ApplyAffliction))
            {
                Action_ApplyAffliction effect = (Action_ApplyAffliction)itemComponents[i];
                suffixAfflictions += EffectFormatter.Affliction(effect.affliction);
            }
            else if (itemComponents[i].GetType() == typeof(Action_ClearAllStatus))
            {
                Action_ClearAllStatus effect = (Action_ClearAllStatus)itemComponents[i];
                body += EffectColors.Positive + "CLEAR ALL STATUS</color>";
                if (effect.excludeCurse)
                {
                    body += " EXCEPT " + EffectColors.Get("Curse") + "CURSE</color>";
                }
                if (effect.otherExclusions.Count > 0)
                {
                    foreach (CharacterAfflictions.STATUSTYPE exclusion in effect.otherExclusions)
                    {
                        body += ", " + EffectColors.Get(exclusion.ToString()) + exclusion.ToString().ToUpper() + "</color>";
                    }
                }
                body = body.Replace(", <#E13542>CRAB</color>", "") + "\n";
            }
            else if (itemComponents[i].GetType() == typeof(Action_ReduceUses))
            {
                OptionableIntItemData uses = (OptionableIntItemData)item.data.data[DataEntryKey.ItemUses];
                if (uses.HasData && uses.Value > 1)
                {
                    suffixUses += "   " + uses.Value + " USES";
                }
            }
            else if (itemComponents[i].GetType() == typeof(Lantern))
            {
                Lantern lantern = (Lantern)itemComponents[i];
                // A torch burns for itself; everything else warms whoever is nearby.
                if (!itemGameObj.name.Equals("Torch(Clone)"))
                {
                    suffixAfflictions += EffectColors.Neutral + "WHEN LIT, NEARBY PLAYERS RECEIVE:</color>\n";
                }

                if (itemGameObj.name.Equals("Lantern_Faerie(Clone)"))
                {
                    StatusField effect = itemGameObj.transform.Find("FaerieLantern/Light/Heat").GetComponent<StatusField>();
                    suffixAfflictions += EffectFormatter.EffectOverTime(effect.statusAmountPerSecond, 1f, lantern.startingFuel, effect.statusType.ToString());
                    foreach (StatusField.StatusFieldStatus status in effect.additionalStatuses)
                    {
                        suffixAfflictions = TrimTrailingNewline(suffixAfflictions);
                        suffixAfflictions += ",\n" + EffectFormatter.EffectOverTime(status.statusAmountPerSecond, 1f, lantern.startingFuel, status.statusType.ToString());
                    }
                }
                else if (itemGameObj.name.Equals("Lantern(Clone)"))
                {
                    StatusField effect = itemGameObj.transform.Find("GasLantern/Light/Heat").GetComponent<StatusField>();
                    suffixAfflictions += EffectFormatter.EffectOverTime(effect.statusAmountPerSecond, 1f, lantern.startingFuel, effect.statusType.ToString());
                }
            }
            else if (itemComponents[i].GetType() == typeof(Action_RaycastDart))
            {
                Action_RaycastDart effect = (Action_RaycastDart)itemComponents[i];
                isConsumable = true;
                suffixAfflictions += EffectColors.Neutral + "SHOOT A DART THAT WILL APPLY:</color>\n";
                for (int j = 0; j < effect.afflictionsOnHit.Length; j++)
                {
                    suffixAfflictions += EffectFormatter.Affliction(effect.afflictionsOnHit[j]);
                    suffixAfflictions = TrimTrailingNewline(suffixAfflictions);
                    suffixAfflictions += ",\n";
                }
                if (suffixAfflictions.EndsWith('\n'))
                {
                    suffixAfflictions = suffixAfflictions.Remove(suffixAfflictions.Length - 2);
                }
                suffixAfflictions += "\n";
            }
            else if (itemComponents[i].GetType() == typeof(Constructable))
            {
                Constructable effect = (Constructable)itemComponents[i];
                if (effect.constructedPrefab.name.Equals("PortableStovetop_Placed"))
                {
                    body += "PLACE A " + EffectColors.Get("Injury") + "COOKING</color> STOVE FOR "
                        + effect.constructedPrefab.GetComponent<Campfire>().burnsFor.ToString() + "s\n";
                }
            }
            else if (itemComponents[i].GetType() == typeof(RopeSpool))
            {
                RopeSpool effect = (RopeSpool)itemComponents[i];
                body += effect.isAntiRope ? "PLACE A ROPE THAT FLOATS UP\n" : "PLACE A ROPE\n";
                body += "FROM " + (effect.minSegments / 4f).ToString("F2").Replace(".0", "") + "m LONG, UP TO "
                    + EffectFormatter.Num(Rope.MaxSegments / 4f) + "m LONG\n";
                // Rope has no character distinction for Detach_Rpc(), so remaining length
                // rides the timed poll and is hidden when that poll is too slow to trust.
                if (PluginConfig.LiveValuesTrustworthy)
                {
                    suffixUses += "   " + (effect.RopeFuel / 4f).ToString("F2").Replace(".00", "") + "m LEFT";
                }
            }
            else if (itemComponents[i].GetType() == typeof(RopeShooter))
            {
                RopeShooter effect = (RopeShooter)itemComponents[i];
                body += "SHOOT A ROPE ANCHOR WHICH PLACES\nA ROPE THAT ";
                body += effect.ropeAnchorWithRopePref.name.Equals("RopeAnchorForRopeShooterAnti") ? "FLOATS UP " : "DROPS DOWN ";
                body += EffectFormatter.Num(effect.maxLength / 4f) + "m\n";
            }
            else if (itemComponents[i].GetType() == typeof(VineShooter))
            {
                VineShooter effect = (VineShooter)itemComponents[i];
                body += "SHOOT A CHAIN THAT CONNECTS FROM\nYOUR POSITION TO WHERE YOU SHOOT\nUP TO "
                    + EffectFormatter.Num(effect.maxLength / (5f / 3f)) + "m AWAY\n";
            }
            else if (itemComponents[i].GetType() == typeof(ShelfShroom))
            {
                ShelfShroom effect = (ShelfShroom)itemComponents[i];
                if (effect.instantiateOnBreak.name.Equals("HealingPuffShroomSpawn"))
                {
                    GameObject effect1 = effect.instantiateOnBreak.transform.Find("VFX_SporeHealingExplo").gameObject;
                    AOE effect1AOE = effect1.GetComponent<AOE>();
                    GameObject effect2 = effect1.transform.Find("VFX_SporePoisonExplo").gameObject;
                    AOE effect2AOE = effect2.GetComponent<AOE>();
                    AOE[] effect2AOEs = effect2.GetComponents<AOE>();
                    TimeEvent effect2TimeEvent = effect2.GetComponent<TimeEvent>();
                    RemoveAfterSeconds effect2RemoveAfterSeconds = effect2.GetComponent<RemoveAfterSeconds>();
                    body += EffectColors.Get("Hunger") + "THROW</color> TO RELEASE GAS THAT WILL:\n";
                    // Values below were adjusted by hand - they calculate strangely, and may still be wrong.
                    body += EffectFormatter.Effect(Mathf.Round(effect1AOE.statusAmount * 0.9f * 40f) / 40f, effect1AOE.statusType.ToString());
                    body += EffectFormatter.EffectOverTime(Mathf.Round(effect2AOE.statusAmount * (1f / effect2TimeEvent.rate) * 40f) / 40f, 1f, effect2RemoveAfterSeconds.seconds, effect2AOE.statusType.ToString());
                    if (effect2AOEs.Length > 1)
                    {
                        // Not handled dynamically: there are 2 poison removal AOEs but one
                        // doesn't seem to work, probably down to the time event rate.
                        body += EffectFormatter.EffectOverTime(Mathf.Round(effect2AOEs[1].statusAmount * (1f / effect2TimeEvent.rate) * 40f) / 40f, 1f, effect2RemoveAfterSeconds.seconds + 1f, effect2AOEs[1].statusType.ToString());
                    }
                }
            }
            else if (itemComponents[i].GetType() == typeof(Action_SpawnGuidebookPage))
            {
                isConsumable = true;
            }
            else if (itemComponents[i].GetType() == typeof(Action_MoraleBoost))
            {
                Action_MoraleBoost effect = (Action_MoraleBoost)itemComponents[i];
                if (effect.boostRadius < 0)
                {
                    body += EffectColors.Positive + "GAIN</color> " + EffectColors.Get("Extra Stamina")
                        + EffectFormatter.Scaled(effect.baselineStaminaBoost) + " EXTRA STAMINA</color>\n";
                }
                else if (effect.boostRadius > 0)
                {
                    body += EffectColors.Neutral + "NEARBY PLAYERS</color>" + EffectColors.Positive + " GAIN</color> "
                        + EffectColors.Get("Extra Stamina") + EffectFormatter.Scaled(effect.baselineStaminaBoost) + " EXTRA STAMINA</color>\n";
                }
            }
            else if (itemComponents[i].GetType() == typeof(MagicBean))
            {
                MagicBean effect = (MagicBean)itemComponents[i];
                body += EffectColors.Get("Hunger") + "THROW</color> TO PLANT A VINE THAT GROWS\nPERPENDICULAR TO TERRAIN UP TO\n"
                    + EffectFormatter.Num(effect.plantPrefab.maxLength / 2f) + "m OR UNTIL IT HITS SOMETHING\n";
            }
            else if (itemComponents[i].GetType() == typeof(Action_WarpToBiome))
            {
                Action_WarpToBiome effect = (Action_WarpToBiome)itemComponents[i];
                body += "WARP TO " + effect.segmentToWarpTo.ToString().ToUpper() + "\n";
            }
            else if (itemComponents[i].GetType() == typeof(Dynamite))
            {
                Dynamite effect = (Dynamite)itemComponents[i];
                body += EffectColors.Get("Injury") + "EXPLODES</color> FOR UP TO " + EffectColors.Get("Injury")
                    + EffectFormatter.Scaled(effect.explosionPrefab.GetComponent<AOE>().statusAmount) + " INJURY</color>\n"
                    + EffectColors.Neutral + "ADDITIONAL DAMAGE TAKEN IF HELD</color>\n";
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
                body += EffectFormatter.Colored("2.5", "Poison") + " + "
                    + EffectFormatter.Colored("50-105", "Poison") + " / " + EffectFormatter.Num(effect.totalPoisonTime) + "s\n";
            }
            else if (itemComponents[i].GetType() == typeof(Action_Spawn))
            {
                Action_Spawn effect = (Action_Spawn)itemComponents[i];
                if (effect.objectToSpawn.name.Equals("VFX_Sunscreen"))
                {
                    AOE effectAOE = effect.objectToSpawn.transform.Find("AOE").GetComponent<AOE>();
                    RemoveAfterSeconds effectTime = effect.objectToSpawn.transform.Find("AOE").GetComponent<RemoveAfterSeconds>();
                    body += EffectColors.Neutral + "SPRAY A " + EffectFormatter.Num(effectTime.seconds) + "s MIST THAT APPLIES:</color>\n"
                        + EffectFormatter.Affliction(effectAOE.affliction);
                }
            }
            else if (itemComponents[i].GetType() == typeof(CactusBall))
            {
                CactusBall effect = (CactusBall)itemComponents[i];
                body += EffectColors.Get("Thorns") + "STICKS</color> TO YOUR BODY\n\nCAN " + EffectColors.Get("Hunger")
                    + "THROW</color> BY USING\nAT LEAST " + EffectFormatter.Scaled(effect.throwChargeRequirement) + "% POWER\n";
            }
            // Amulets are matched with 'is' rather than an exact type check: they all derive
            // from AmuletBase and each applies petrify through a different path.
            else if (itemComponents[i] is Peak.Action_SuperJumpAmulet superJump)
            {
                // AddStatus takes a 0-1 fraction, same scale as every other status.
                prefixStatus += EffectFormatter.Effect(superJump.petrifyPerUse, "Petrify");
            }
            // InfiniteStamAmulet and HealingAmulet are deliberately not handled yet. Their
            // petrify costs live in nested ItemPocketBehavior instances that tick while the
            // amulet sits in a pocket, which is a different thing from the cost of using it
            // - reporting the tick as if it were the use cost would be worse than silence.
        }

        if (prefixStatus.Length > 0 && isConsumable)
        {
            body = prefixStatus + "\n" + body;
        }
        if (suffixAfflictions.Length > 0)
        {
            body += "\n" + suffixAfflictions;
        }
        if (suffixUses.Length > 0)
        {
            body += "\n" + suffixUses.Trim();
        }
        body += "\n" + suffixWeight;

        return body.Replace("\n\n\n", "\n\n");
    }

    /// <summary>Drops a single trailing newline so a "," can be appended to join list entries.</summary>
    private static string TrimTrailingNewline(string text) =>
        text.EndsWith('\n') ? text.Remove(text.Length - 1) : text;
}
