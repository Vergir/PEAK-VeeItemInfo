using UnityEngine;

namespace VeeItemInfo;

/// <summary>
/// Builds the overlay text for an item by scanning every component on it.
///
/// Three dispatch layers feed the result:
///   1. GameObject name, for flavour text on items with no distinguishing component.
///   2. Component type, which is where nearly all the real information comes from.
///   3. Affliction type, handled over in <see cref="EffectFormatter.Affliction"/>.
///
/// Note the component chain matches with GetType() == typeof(T), which is exact - a
/// subclass will not match. That is deliberate for now, but it is also the most likely
/// reason for an item silently losing its description after a game update.
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
        string suffixWeight;
        string suffixUses = "";
        string suffixCooked = "";
        string suffixAfflictions = "";

        float weight = Ascents.itemWeightModifier > 0
            ? (item.carryWeight + Ascents.itemWeightModifier) * 2.5f
            : item.carryWeight * 2.5f;
        suffixWeight = EffectColors.Get("Weight") + EffectFormatter.Num(weight) + " WEIGHT</color>";

        // Layer 1: items identified only by name.
        if (itemGameObj.name.Equals("Bugle(Clone)"))
        {
            body += "MAKE SOME NOISE\n";
        }
        else if (itemGameObj.name.Equals("Pirate Compass(Clone)"))
        {
            body += EffectColors.Get("Injury") + "POINTS</color> TO THE NEAREST LUGGAGE\n";
        }
        else if (itemGameObj.name.Equals("Compass(Clone)"))
        {
            body += EffectColors.Get("Injury") + "POINTS</color> NORTH TO THE PEAK\n";
        }
        else if (itemGameObj.name.Equals("Shell Big(Clone)"))
        {
            body += "TRY " + EffectColors.Get("Hunger") + "THROWING</color> AT A COCONUT\n";
        }

        // Layer 2: everything the item's components can tell us.
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
                prefixStatus += EffectFormatter.Effect(effect.thornCount * 0.05f, "Thorns"); // TODO: Search for thorns amount per applied thorn
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
            else if (itemComponents[i].GetType() == typeof(Action_ConsumeAndSpawn))
            {
                Action_ConsumeAndSpawn effect = (Action_ConsumeAndSpawn)itemComponents[i];
                if (effect.itemToSpawn.ToString().Contains("Peel"))
                {
                    body += EffectColors.Neutral + "GAIN A PEEL WHEN EATEN</color>\n";
                }
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
                if (itemGameObj.name.Equals("Torch(Clone)"))
                {
                    body += "CAN BE LIT\n";
                }
                else
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
            else if (itemComponents[i].GetType() == typeof(MagicBugle))
            {
                body += "WHILE PLAYING THE BUGLE,";
            }
            else if (itemComponents[i].GetType() == typeof(ClimbingSpikeComponent))
            {
                body += "PLACE A PITON YOU CAN GRAB\nTO " + EffectColors.Get("Extra Stamina") + "REGENERATE STAMINA</color>\n";
            }
            else if (itemComponents[i].GetType() == typeof(Action_Flare))
            {
                body += "CAN BE LIT\n";
            }
            else if (itemComponents[i].GetType() == typeof(Backpack))
            {
                body += "DROP TO PLACE ITEMS INSIDE\n";
            }
            else if (itemComponents[i].GetType() == typeof(BananaPeel))
            {
                body += EffectColors.Get("Hunger") + "SLIP</color> WHEN STEPPED ON\n";
            }
            else if (itemComponents[i].GetType() == typeof(Constructable))
            {
                Constructable effect = (Constructable)itemComponents[i];
                if (effect.constructedPrefab.name.Equals("PortableStovetop_Placed"))
                {
                    body += "PLACE A " + EffectColors.Get("Injury") + "COOKING</color> STOVE FOR "
                        + effect.constructedPrefab.GetComponent<Campfire>().burnsFor.ToString() + "s\n";
                }
                else
                {
                    body += "CAN BE PLACED\n";
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
            else if (itemComponents[i].GetType() == typeof(Antigrav))
            {
                Antigrav effect = (Antigrav)itemComponents[i];
                if (effect.intensity != 0f)
                {
                    suffixAfflictions += EffectColors.Get("Injury") + "WARNING:</color> " + EffectColors.Neutral + "FLIES AWAY IF DROPPED</color>\n";
                }
            }
            else if (itemComponents[i].GetType() == typeof(Action_Balloon))
            {
                suffixAfflictions += "CAN ATTACH TO CHARACTER\n";
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
                else if (effect.instantiateOnBreak.name.Equals("ShelfShroomSpawn"))
                {
                    body += EffectColors.Get("Hunger") + "THROW</color> TO DEPLOY A PLATFORM\n";
                }
                else if (effect.instantiateOnBreak.name.Equals("BounceShroomSpawn"))
                {
                    body += EffectColors.Get("Hunger") + "THROW</color> TO DEPLOY A BOUNCE PAD\n";
                }
            }
            else if (itemComponents[i].GetType() == typeof(ScoutEffigy))
            {
                body += EffectColors.Get("Extra Stamina") + "REVIVE</color> A DEAD PLAYER\n";
            }
            else if (itemComponents[i].GetType() == typeof(Action_Die))
            {
                body += "YOU " + EffectColors.Get("Curse") + "DIE</color> WHEN USED\n";
            }
            else if (itemComponents[i].GetType() == typeof(Action_SpawnGuidebookPage))
            {
                isConsumable = true;
                body += "CAN BE OPENED\n";
            }
            else if (itemComponents[i].GetType() == typeof(Action_Guidebook))
            {
                body += "CAN BE READ\n";
            }
            else if (itemComponents[i].GetType() == typeof(Action_CallScoutmaster))
            {
                body += EffectColors.Get("Injury") + "BREAKS RULE 0 WHEN USED</color>\n";
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
            else if (itemComponents[i].GetType() == typeof(Breakable))
            {
                body += EffectColors.Get("Hunger") + "THROW</color> TO CRACK OPEN\n";
            }
            else if (itemComponents[i].GetType() == typeof(Bonkable))
            {
                body += EffectColors.Get("Hunger") + "THROW</color> AT HEAD TO " + EffectColors.Get("Injury") + "BONK</color>\n";
            }
            else if (itemComponents[i].GetType() == typeof(MagicBean))
            {
                MagicBean effect = (MagicBean)itemComponents[i];
                body += EffectColors.Get("Hunger") + "THROW</color> TO PLANT A VINE THAT GROWS\nPERPENDICULAR TO TERRAIN UP TO\n"
                    + EffectFormatter.Num(effect.plantPrefab.maxLength / 2f) + "m OR UNTIL IT HITS SOMETHING\n";
            }
            else if (itemComponents[i].GetType() == typeof(BingBong))
            {
                body += "MASCOT OF BINGBONG AIRWAYS\n";
            }
            else if (itemComponents[i].GetType() == typeof(Action_Passport))
            {
                body += "OPEN TO CUSTOMIZE CHARACTER\n";
            }
            else if (itemComponents[i].GetType() == typeof(Actions_Binoculars))
            {
                body += "USE TO LOOK FURTHER\n";
            }
            else if (itemComponents[i].GetType() == typeof(Action_WarpToRandomPlayer))
            {
                body += "WARP TO RANDOM PLAYER\n";
            }
            else if (itemComponents[i].GetType() == typeof(Action_WarpToBiome))
            {
                Action_WarpToBiome effect = (Action_WarpToBiome)itemComponents[i];
                body += "WARP TO " + effect.segmentToWarpTo.ToString().ToUpper() + "\n";
            }
            else if (itemComponents[i].GetType() == typeof(Parasol))
            {
                body += "OPEN TO SLOW YOUR DESCENT\n";
            }
            else if (itemComponents[i].GetType() == typeof(Frisbee))
            {
                body += EffectColors.Get("Hunger") + "THROW</color> IT\n";
            }
            else if (itemComponents[i].GetType() == typeof(Action_ConstructableScoutCannonScroll))
            {
                body += "\n" + EffectColors.Neutral + "WHEN PLACED, LIGHT FUSE TO:</color>\nLAUNCH SCOUTS IN BARREL\n";
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
                string sting = "IF ALIVE, " + EffectColors.Get("Poison") + "STINGS</color> YOU\n" + EffectColors.Get("Curse")
                    + "DIES</color> WHEN " + EffectColors.Get("Heat") + "COOKED</color>\n\n" + EffectColors.Neutral + "NEXT STING WILL DEAL:</color>\n";

                if (PluginConfig.LiveValuesTrustworthy)
                {
                    // v.1.23.a BASED ON Scorpion.InflictAttack - there is no variable for the
                    // poison amount, it is computed at sting time from the victim's health.
                    float effectPoison = Mathf.Max(0.5f, 1f - item.holderCharacter.refs.afflictions.statusSum + 0.05f) * 100f;
                    body += sting + EffectColors.Get("Poison") + EffectFormatter.Num(effectPoison) + " POISON</color> OVER "
                        + EffectFormatter.Num(effect.totalPoisonTime) + "s\n" + EffectColors.Neutral + "(MORE DAMAGE IF HEALTHY)</color>\n";
                }
                else
                {
                    body += sting + "AT LEAST " + EffectColors.Get("Poison") + "50 POISON</color> OVER "
                        + EffectFormatter.Num(effect.totalPoisonTime) + "s\nAT MOST " + EffectColors.Get("Poison") + "105 POISON</color> OVER "
                        + EffectFormatter.Num(effect.totalPoisonTime) + "s\n" + EffectColors.Neutral + "(MORE DAMAGE IF HEALTHY)</color>\n";
                }
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
            else if (itemComponents[i].GetType() == typeof(BingBongShieldWhileHolding))
            {
                body += EffectColors.Neutral + "WHILE EQUIPPED, GRANTS:</color>\n" + EffectColors.Get("Shield") + "SHIELD</color> (INVINCIBILITY)\n";
            }
            else if (itemComponents[i].GetType() == typeof(ItemCooking))
            {
                suffixCooked += DescribeCooking((ItemCooking)itemComponents[i]);
            }
        }

        if (prefixStatus.Length > 0 && isConsumable)
        {
            body = prefixStatus + "\n" + body;
        }
        if (suffixAfflictions.Length > 0)
        {
            body += "\n" + suffixAfflictions;
        }
        body += "\n" + suffixWeight + suffixUses + suffixCooked;

        return body.Replace("\n\n\n", "\n\n");
    }

    /// <summary>
    /// Cooking state, colour-coded by how close the item is to being ruined.
    /// </summary>
    private static string DescribeCooking(ItemCooking itemCooking)
    {
        if (itemCooking.wreckWhenCooked)
        {
            return itemCooking.timesCookedLocal >= 1
                ? "\n" + EffectColors.Get("Curse") + "BROKEN FROM COOKING</color>"
                : "\n" + EffectColors.Get("Curse") + "BREAKS IF COOKED</color>";
        }

        if (itemCooking.timesCookedLocal >= ItemCooking.COOKING_MAX)
        {
            return "   " + EffectColors.Get("Curse") + itemCooking.timesCookedLocal.ToString() + "x COOKED\nCANNOT BE COOKED</color>";
        }

        return itemCooking.timesCookedLocal switch
        {
            0 => "\n" + EffectColors.Get("Extra Stamina") + "CAN BE COOKED</color>",
            1 => "   " + EffectColors.Get("Extra Stamina") + "1x COOKED</color>\n" + EffectColors.Get("Hunger") + "CAN BE COOKED</color>",
            2 => "   " + EffectColors.Get("Hunger") + "2x COOKED</color>\n" + EffectColors.Get("Injury") + "CAN BE COOKED</color>",
            3 => "   " + EffectColors.Get("Injury") + "3x COOKED</color>\n" + EffectColors.Get("Poison") + "CAN BE COOKED</color>",
            _ => "   " + EffectColors.Get("Poison") + itemCooking.timesCookedLocal.ToString() + "x COOKED\nCAN BE COOKED</color>",
        };
    }

    /// <summary>Drops a single trailing newline so a "," can be appended to join list entries.</summary>
    private static string TrimTrailingNewline(string text) =>
        text.EndsWith('\n') ? text.Remove(text.Length - 1) : text;
}
