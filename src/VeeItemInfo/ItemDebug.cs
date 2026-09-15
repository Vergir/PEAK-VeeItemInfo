using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace VeeItemInfo;

/// <summary>
/// The "[item]" and "[color]" diagnostics. See docs/internals_infra.md, "Diagnostics".
/// </summary>
internal static class ItemDebug
{
    private static string lastLogged = "";

    /// <summary>Once per item name, not once per refresh.</summary>
    internal static void LogItem(Item item)
    {
        if (item.gameObject.name == lastLogged)
        {
            return;
        }

        lastLogged = item.gameObject.name;
        Plugin.Log.LogInfo(Components(item));
    }

    /// <summary>Shared by the held-item log and the database dump.</summary>
    internal static string Components(Item item)
    {
        GameObject itemGameObj = item.gameObject;
        StringBuilder report = new();
        report.Append($"[item] {itemGameObj.name} name=\"{item.UIData?.itemName}\""
            + $" carryWeight={item.carryWeight} tags={item.itemTags}");

        Texture2D? icon = item.UIData?.GetIcon();
        report.Append($" icon={(icon == null ? "<none>" : icon.name)}");

        foreach (Component component in itemGameObj.GetComponents(typeof(Component)))
        {
            if (component == null)
            {
                continue;
            }

            System.Type type = component.GetType();
            if (type == typeof(Transform) || type == typeof(RectTransform))
            {
                continue;
            }

            report.Append("\n[item]   ").Append(type.FullName);

            if (component is Behaviour behaviour && !behaviour.enabled)
            {
                report.Append(" [DISABLED]");
            }

            report.Append(Values(component));
        }

        return report.ToString();
    }

    private static string lastAudited = "";

    /// <summary>
    /// Warns about any visible run outside a colour tag. Reads the string handed to
    /// TextMeshPro, so it cannot be fooled by how a line was assembled.
    /// </summary>
    internal static void LogUntagged(string description)
    {
        if (description == lastAudited)
        {
            return;
        }

        lastAudited = description;

        List<string> leaks = new();
        int depth = 0;
        int i = 0;

        while (i < description.Length)
        {
            if (description[i] == '<')
            {
                int close = description.IndexOf('>', i);
                if (close < 0)
                {
                    leaks.Add("unterminated tag");
                    break;
                }

                string tag = description.Substring(i + 1, close - i - 1);
                if (tag.StartsWith("#") || tag.StartsWith("color="))
                {
                    depth++;
                }
                else if (tag == "/color")
                {
                    if (depth > 0)
                    {
                        depth--;
                    }
                    else
                    {
                        leaks.Add("</color> closing nothing");
                    }
                }
                else if (tag.StartsWith("sprite") && depth == 0)
                {
                    leaks.Add("<" + tag + ">");
                }

                i = close + 1;
                continue;
            }

            if (depth == 0 && !char.IsWhiteSpace(description[i]))
            {
                int start = i;
                while (i < description.Length && description[i] != '<')
                {
                    i++;
                }

                leaks.Add("\"" + description.Substring(start, i - start).Trim() + "\"");
                continue;
            }

            i++;
        }

        if (depth != 0)
        {
            leaks.Add($"{depth} colour tag(s) left open");
        }

        if (leaks.Count > 0)
        {
            Plugin.Log.LogWarning("[color] untagged, renders in the base colour: "
                + string.Join(", ", leaks));
        }
    }

    /// <summary>The fields that drive the description.</summary>
    private static string Values(Component component) => component switch
    {
        Action_RestoreHunger a => $" restorationAmount={a.restorationAmount} onConsumed={a.OnConsumed}",
        Action_GiveExtraStamina a => $" amount={a.amount} onConsumed={a.OnConsumed}",
        Action_ModifyStatus a => $" {a.statusType}={a.changeAmount} onConsumed={a.OnConsumed}"
            + $" ifSkeleton={a.ifSkeleton}",
        Action_InflictPoison a => $" perSecond={a.poisonPerSecond} time={a.inflictionTime} delay={a.delay}",
        Action_AddOrRemoveThorns a => $" thornCount={a.thornCount}",
        // Derived types first: a pattern switch takes the first match, and both of these
        // are Action_ApplyAffliction too.
        Peak.Action_SuperJumpAmulet a => $" petrifyPerUse={a.petrifyPerUse}"
            + $" affliction={Affliction(a.affliction)} extra=[{Afflictions(a.extraAfflictions)}]",
        Action_ApplyMassAffliction a => $" radius={a.radius} ignoreCaster={a.ignoreCaster}"
            + $" affliction={Affliction(a.affliction)} extra=[{Afflictions(a.extraAfflictions)}]",
        Action_ApplyAffliction a => $" affliction={Affliction(a.affliction)}"
            + $" extra=[{Afflictions(a.extraAfflictions)}]",
        Action_RaycastDart a => $" maxDistance={a.maxDistance} onHit=[{Afflictions(a.afflictionsOnHit)}]",
        Action_MoraleBoost a => $" radius={a.boostRadius} baseline={a.baselineStaminaBoost}"
            + $" perScout={a.staminaBoostPerAdditionalScout}",
        Action_Numb a => $" numbAmount={a.numbAmount}",
        Action_RandomMushroomEffect a => $" mushroomTypeIndex={a.mushroomTypeIndex}",
        Action_ClearAllStatus a => $" excludeCurse={a.excludeCurse}"
            + $" otherExclusions=[{string.Join(", ", a.otherExclusions)}]",
        StickyItemComponent a => $" thorns={a.addThornsToStuckPlayer} weight={a.addWeightToStuckPlayer}"
            + $" throwCharge={a.throwChargeRequirement}",
        ShelfShroom a => BreaksInto(a),
        Breakable a => $" onCollision={a.breakOnCollision} minVelocity={a.minBreakVelocity}"
            + $" items=[{string.Join(", ", (a.instantiateOnBreak ?? new List<GameObject>()).ConvertAll(g => g == null ? "<null>" : g.name))}]"
            + string.Concat((a.instantiateNonItemOnBreak ?? new List<GameObject>()).ConvertAll(g => Prefab(" spawns", g))),
        VineShooter a => $" maxLength={a.maxLength}u -> {a.maxLength * CharacterStats.unitsToMeters}m",
        MagicBean a => a.plantPrefab == null ? " plantPrefab=<none>" : $" maxLength={a.plantPrefab.maxLength}u",
        Action_Spawn a => Spawns(a),
        RopeShooter a => $" shootRange={a.maxLength}u ropeSegments={a.length}"
            + $" segmentLength={ItemDescriptionBuilder.RopeSegmentLength(a)}u"
            + $" achievementMetres={Rope.GetLengthInMeters(a.length)}",
        // RopeFuel needs a live instance; on a prefab (no scene) it throws.
        RopeSpool a => (a.gameObject.scene.IsValid()
                ? $" fuel={a.RopeFuel} metres={Rope.GetLengthInMeters(a.RopeFuel)}"
                : " fuel=<prefab>")
            + $" startFuel={a.ropeStartFuel} anti={a.isAntiRope}",
        MagicBugle a => $" totalTootTime={a.totalTootTime} initialTootCost={a.initialTootCost}",
        Peak.RitualDaggerFeedBehavior a => $" bonusStamina={a.bonusStamina}"
            + $" infiniteStaminaTime={a.infiniteStaminaTime}",
        ItemCooking c => $" canBeCooked={c.canBeCooked} wreck={c.wreckWhenCooked} preCooked={c.preCooked}"
            + $" ignoreDefault={c.ignoreDefaultCookBehavior} ignorePoison={c.ignoreDefaultPoisonBehavior}"
            + Prefab(" explosionPrefab", c.explosionPrefab)
            + Behaviours(c),
        Scorpion a => $" totalPoisonTime={a.totalPoisonTime}",
        Dynamite a => $" fuse={a.startingFuseTime} lightFuseRadius={a.lightFuseRadius}"
            + Prefab(" explodes", a.explosionPrefab),
        Constructable a => $" maxConstructDistance={a.maxConstructDistance}"
            + Prefab(" builds", a.constructedPrefab),
        Lantern a => $" startingFuel={a.startingFuel}" + Field(a.gameObject),
        Candle a => $" startingFuel={a.startingFuel}" + Field(a.gameObject),
        Peak.Action_HealingGem a => $" heal={Affliction(a.healingAffliction)}"
            + $" shield={Affliction(a.invincibilityAffliction)}"
            + $" ratio={a.healingToPetrifyRatio} petrify={a.minPetrify}-{a.maxPetrify}",
        Peak.Action_CloneSelectedItem a => $" petrify={a.petrify} mystical={a.petrifyMystical} range={a.range}",
        Peak.AmuletBase a => $" amuletIndex={a.amuletIndex} startActive={a.startActive}",
        _ => "",
    };

    private static string Behaviours(ItemCooking cooking)
    {
        if (cooking.additionalCookingBehaviors == null || cooking.additionalCookingBehaviors.Length == 0)
        {
            return "";
        }

        StringBuilder text = new();
        foreach (AdditionalCookingBehavior behaviour in cooking.additionalCookingBehaviors)
        {
            if (behaviour == null)
            {
                text.Append("\n[item]     <null behaviour>");
                continue;
            }

            text.Append("\n[item]     ").Append(behaviour.GetType().Name)
                .Append($" at={behaviour.cookedAmountToTrigger} once={behaviour.onlyOnce}");
            text.Append(behaviour switch
            {
                CookingBehavior_DisableScripts b => $" disables=[{Names(b.scriptsToDisable)}]",
                CookingBehavior_EnableScripts b => $" enables=[{Names(b.scriptsToEnable)}]",
                CookingBehavior_RunActions b => $" runs=[{Names(b.actionsToRun)}]",
                CookingBehavior_ReplaceItem b => $" replaceWith={(b.replaceWithItem == null ? "<none>" : b.replaceWithItem.name)} cookNew={b.cookNewItem}",
                CookingBehavior_ChangeAfflictionTime b => $" change={b.change} on={(b.action == null ? "<none>" : Affliction(b.action.affliction))}",
                CookingBehavior_AddPoisonOnUse b => $" onUse={b.onUse} onConsume={b.onConsume}",
                CookingBehavior_AdjustStatusInstantly b => $" {b.statusType}={b.amount}",
                CookingBehavior_Explode b => $" dontRunIfOutOfFuel={b.dontRunIfOutOfFuel}",
                CookingBehavior_MessUpAudio b => $" pitch-={b.pitchReductionPerCooking} volume-={b.volumeReductionPerCooking} max={b.max}",
                CookingBehavior_ModifyBugleWobble b => $" wobble+={b.changePerCooking} max={b.maxCooking}",
                CookingBehavior_ModifyAudioSourcePitch b => $" pitch+={b.changePerCooking}",
                CookingBehavior_EnableDisableObjects b => $" enable={b.objectsToEnable?.Count ?? 0} disable={b.objectsToDisable?.Count ?? 0}",
                CookingBehavior_ModifyEtcStats b => $" canPocket={b.canPocket} canBackpack={b.canBackpack}",
                _ => "",
            });
        }

        return text.ToString();
    }

    private static string Names(Component[]? components)
    {
        if (components == null)
        {
            return "";
        }

        List<string> names = new();
        foreach (Component component in components)
        {
            names.Add(component == null ? "<null>" : component.GetType().Name);
        }

        return string.Join(", ", names);
    }

    private static string Affliction(Peak.Afflictions.Affliction? affliction)
    {
        if (affliction == null)
        {
            return "<none>";
        }

        string detail = affliction switch
        {
            Peak.Afflictions.Affliction_FasterBoi a => $" drowsyOnEnd={a.drowsyOnEnd}",
            Peak.Afflictions.Affliction_InfiniteStamina a => $" climbDelay={a.climbDelay}"
                + $" drowsy={Affliction(a.drowsyAffliction)}",
            Peak.Afflictions.Affliction_AddBonusStamina a => $" stamina={a.staminaAmount}",
            Peak.Afflictions.Affliction_AdjustStatus a => $" {a.statusType}={a.statusAmount}",
            Peak.Afflictions.Affliction_AdjustDrowsyOverTime a => $" perSecond={a.statusPerSecond}",
            Peak.Afflictions.Affliction_AdjustColdOverTime a => $" perSecond={a.statusPerSecond}",
            Peak.Afflictions.Affliction_AdjustStatusOverTime a => $" perSecond={a.statusPerSecond}",
            Peak.Afflictions.Affliction_PoisonOverTime a => $" perSecond={a.statusPerSecond} delay={a.delayBeforeEffect}",
            Peak.Afflictions.Affliction_ClearAllStatus a => $" excludeCurse={a.excludeCurse}",
            Peak.Afflictions.Affliction_HealAll a => $" maxHealing={a.maxHealing}",
            Peak.Afflictions.Affliction_RadiateInfiniteStam a => $" radius={a.radius}",
            Peak.Afflictions.Affliction_MassSuperJump a => $" radius={a.radius}"
                + $" lowGrav={a.lowGravAmount} lowGravTime={a.lowGravTime}",
            Peak.Afflictions.Affliction_LowGravity a => $" lowGrav={a.lowGravAmount}",
            _ => "",
        };

        return $"{affliction.GetAfflictionType()}(totalTime={affliction.totalTime}{detail})";
    }

    private static string Afflictions(Peak.Afflictions.Affliction[]? afflictions)
    {
        if (afflictions == null)
        {
            return "";
        }

        List<string> parts = new();
        foreach (Peak.Afflictions.Affliction affliction in afflictions)
        {
            parts.Add(Affliction(affliction));
        }

        return string.Join(", ", parts);
    }

    private static string Prefab(string label, GameObject? prefab)
    {
        if (prefab == null)
        {
            return $"{label}=<none>";
        }

        StringBuilder tree = new($"{label}={prefab.name}");
        foreach (Component component in prefab.GetComponents(typeof(Component)))
        {
            if (component != null && !(component is Transform))
            {
                tree.Append(" | ").Append(component.GetType().Name).Append(PrefabValues(component));
            }
        }

        Describe(tree, prefab.transform, MaxPrefabDepth);
        DeepEffects(tree, prefab);
        return tree.ToString();
    }

    /// <summary>Effect components at any depth, since the tree stops at four levels and the handlers do not.</summary>
    private static void DeepEffects(StringBuilder tree, GameObject prefab)
    {
        foreach (Component component in prefab.GetComponentsInChildren<Component>(true))
        {
            if (component is not (AOE or Peak.StatusFieldBase or StatusEmitter))
            {
                continue;
            }

            string path = component.transform.name;
            bool active = component.gameObject.activeSelf;
            for (Transform t = component.transform.parent; t != null && t != prefab.transform; t = t.parent)
            {
                path = t.name + "/" + path;
                active &= t.gameObject.activeSelf;
            }

            tree.Append("\n[item]     effect ").Append(path).Append(active ? "" : " [INACTIVE]")
                .Append(" | ").Append(component.GetType().Name).Append(PrefabValues(component));
        }
    }

    /// <summary>Found the way DescribeLantern finds it, so the dump shows what that reader sees.</summary>
    private static string Field(GameObject item)
    {
        StatusField? field = item.GetComponentInChildren<StatusField>(true);
        return field == null ? " field=<none>" : $" field={field.name}" + PrefabValues(field);
    }

    /// <summary>This map's dealt slot for the berry, and the whole table behind it.</summary>
    private static string Rolls(Action_RandomMushroomEffect effect)
    {
        MushroomManager? manager = MushroomManager.instance;
        if (manager == null || manager.mushroomEffects == null || manager.mushroomEffects.Length == 0)
        {
            return " <no MushroomManager>";
        }

        int index = effect.mushroomTypeIndex % manager.mushroomEffects.Length;
        string stam = manager.mushroomStamAmt != null && index < manager.mushroomStamAmt.Length
            ? (manager.mushroomStamAmt[index] * 5).ToString()
            : "?";

        return $" slot={index} effect={manager.mushroomEffects[index]} stamina={stam}"
            + $" minGood={manager.minGoodEffects} minBad={manager.minBadEffects}"
            + $" effects=[{string.Join(", ", manager.mushroomEffects)}]"
            + $" stamAmts=[{string.Join(", ", manager.mushroomStamAmt ?? new int[0])}]";
    }

    private static string Spawns(Action_Spawn action)
    {
        if (action.objectToSpawn == null)
        {
            return " spawns=<none>";
        }

        StringBuilder tree = new($" spawns={action.objectToSpawn.name}");
        foreach (Component component in action.objectToSpawn.GetComponents(typeof(Component)))
        {
            if (component != null && !(component is Transform))
            {
                tree.Append(" | ").Append(component.GetType().Name).Append(PrefabValues(component));
            }
        }

        Describe(tree, action.objectToSpawn.transform, MaxPrefabDepth);
        return tree.ToString();
    }

    private static string BreaksInto(ShelfShroom shroom)
    {
        if (shroom.instantiateOnBreak == null)
        {
            return " breaksInto=<none>";
        }

        StringBuilder tree = new($" breaksInto={shroom.instantiateOnBreak.name}");
        Describe(tree, shroom.instantiateOnBreak.transform, MaxPrefabDepth);
        return tree.ToString();
    }

    /// <summary>Below this the particle systems take over.</summary>
    private const int MaxPrefabDepth = 4;

    /// <summary>Every component on every child, not a filtered list: a filter hides what you did not expect.</summary>
    private static void Describe(StringBuilder tree, Transform parent, int depth)
    {
        if (depth == 0)
        {
            return;
        }

        foreach (Transform child in parent)
        {
            tree.Append("\n[item]     ").Append(new string(' ', (MaxPrefabDepth - depth) * 2))
                .Append(child.name);

            foreach (Component component in child.GetComponents(typeof(Component)))
            {
                if (component == null || component is Transform)
                {
                    continue;
                }

                tree.Append(" | ").Append(component.GetType().Name).Append(PrefabValues(component));
            }

            Describe(tree, child, depth - 1);
        }
    }

    private static string PrefabValues(Component component) => component switch
    {
        AOE a => $" {a.statusType}={a.statusAmount} range={a.range} minFactor={a.minFactor}"
            + (a.hasAffliction && a.affliction != null
                ? $" affliction={a.affliction.GetAfflictionType()} totalTime={a.affliction.totalTime}"
                : "")
            + $" factorPow={a.factorPow} ignoreFactor={a.ignoreFactor}"
            + $" auto={a.auto} onEnable={a.onEnable}"
            + (string.IsNullOrEmpty(a.illegalStatus) ? "" : $" illegalStatus={a.illegalStatus}")
            + (a.cooksItems ? " cooksItems" : "")
            + (a.addtlStatus != null && a.addtlStatus.Length > 0
                ? $" addtl=[{string.Join(", ", a.addtlStatus)}]"
                    + $" overrides=[{string.Join(", ", a.addlStatusAmountOverrides ?? new List<float>())}]"
                : ""),
        Peak.StatusFieldBase f => $" {f.statusType}={f.statusAmountPerSecond}/s"
            + $" onEntry={f.statusAmountOnEntry} delay={f.delayBeforeStatusOverTime}"
            + $" addtl=[{DescribeAdditional(f)}]"
            + (f is StatusField s ? $" radius={s.radius} tickBased={s.tickBased}"
                + $" timeBetweenTicks={s.timeBetweenTicks}" : ""),
        StatusEmitter e => $" {e.statusType}={e.amount}/s radius={e.radius} outerFade={e.outerFade}"
            + $" innerFade={e.innerFade} minAmount={e.minAmount} tick={e.tickTime}",
        TimeEvent t => $" rate={t.rate} repeating={t.repeating}",
        RemoveAfterSeconds r => $" seconds={r.seconds}",
        Campfire c => $" burnsFor={c.burnsFor} moraleRadius={c.moraleBoostRadius}",
        _ => "",
    };

    private static string DescribeAdditional(Peak.StatusFieldBase field)
    {
        if (field.additionalStatuses == null)
        {
            return "";
        }

        List<string> parts = new();
        foreach (Peak.StatusFieldBase.StatusFieldStatus status in field.additionalStatuses)
        {
            parts.Add($"{status.statusType}={status.statusAmountPerSecond}/s");
        }

        return string.Join(", ", parts);
    }
}
