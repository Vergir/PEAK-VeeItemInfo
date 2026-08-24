using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace VeeItemInfo;

/// <summary>
/// Dumps what an item is actually made of, so an unrecognised item can be identified
/// without decompiling first. Spawn the item, hold it with Debug Logging on, and the log
/// names every component the description chain could have matched.
/// </summary>
internal static class ItemDebug
{
    private static string lastLogged = "";

    /// <summary>
    /// Logs the held item once per item, not once per refresh - the poll would otherwise
    /// repeat the same block every second.
    /// </summary>
    internal static void LogItem(Item item)
    {
        GameObject itemGameObj = item.gameObject;
        if (itemGameObj.name == lastLogged)
        {
            return;
        }

        lastLogged = itemGameObj.name;

        StringBuilder report = new();
        report.Append($"[item] {itemGameObj.name} carryWeight={item.carryWeight} tags={item.itemTags}");

        Texture2D? icon = item.UIData?.GetIcon();
        report.Append($" icon={(icon == null ? "<none>" : icon.name)}");

        foreach (Component component in itemGameObj.GetComponents(typeof(Component)))
        {
            if (component == null)
            {
                continue;
            }

            // Transform and the renderer/collider furniture are on everything and say
            // nothing about what the item does.
            System.Type type = component.GetType();
            if (type == typeof(Transform) || type == typeof(RectTransform))
            {
                continue;
            }

            report.Append("\n[item]   ").Append(type.FullName);

            // Duplicated components with different values are the hardest bug to see from a
            // type list alone - two Action_GiveExtraStamina on one item look identical here
            // but produce two lines in the overlay. Printing the value each one carries, and
            // whether it is switched on, makes that obvious without a decompile.
            if (component is Behaviour behaviour && !behaviour.enabled)
            {
                report.Append(" [DISABLED]");
            }

            report.Append(Values(component));
        }

        Plugin.Log.LogInfo(report.ToString());
    }

    private static string lastAudited = "";

    /// <summary>
    /// Reports anything in a finished description that would render in the overlay's base
    /// colour rather than a chosen one.
    ///
    /// The rule this enforces is that **every visible thing sits inside a colour tag**. It
    /// used not to be checkable by eye: a missed tag rendered pure white, which reads as
    /// "bright" rather than "wrong", and on a `tint=1` sprite it looked like a slightly
    /// crisper icon. Two lines had been leaking for as long as they had existed - the item
    /// duplication arrow and the low-gravity balloon.
    ///
    /// Reads the string that is actually handed to TextMeshPro, so it cannot be fooled by
    /// how the line was assembled, and it catches leaks nobody thought to look for.
    /// Whitespace between tags is fine and expected; it is invisible in any colour.
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

    /// <summary>The fields worth seeing, for the components that drive the description.</summary>
    private static string Values(Component component) => component switch
    {
        Action_RestoreHunger a => $" restorationAmount={a.restorationAmount} onConsumed={a.OnConsumed}",
        Action_GiveExtraStamina a => $" amount={a.amount} onConsumed={a.OnConsumed}",
        Action_ModifyStatus a => $" {a.statusType}={a.changeAmount} onConsumed={a.OnConsumed}",
        Action_InflictPoison a => $" perSecond={a.poisonPerSecond} time={a.inflictionTime} delay={a.delay}",
        Action_AddOrRemoveThorns a => $" thornCount={a.thornCount}",
        Action_ApplyAffliction a => $" affliction={a.affliction?.GetAfflictionType()}",
        Action_RandomMushroomEffect a => $" mushroomTypeIndex={a.mushroomTypeIndex}",
        Action_ClearAllStatus a => $" excludeCurse={a.excludeCurse}"
            + $" otherExclusions=[{string.Join(", ", a.otherExclusions)}]",
        StickyItemComponent a => $" thorns={a.addThornsToStuckPlayer} weight={a.addWeightToStuckPlayer}"
            + $" throwCharge={a.throwChargeRequirement}",
        ShelfShroom a => BreaksInto(a),
        Action_Spawn a => Spawns(a),
        RopeShooter a => $" shootRange={a.maxLength}u ropeSegments={a.length}"
            + $" ropeMetres={Rope.GetLengthInMeters(a.length)}",
        RopeSpool a => $" fuel={a.RopeFuel} startFuel={a.ropeStartFuel}"
            + $" metres={Rope.GetLengthInMeters(a.RopeFuel)} anti={a.isAntiRope}",
        Peak.RitualDaggerFeedBehavior a => $" bonusStamina={a.bonusStamina}"
            + $" infiniteStaminaTime={a.infiniteStaminaTime}",
        ItemCooking c => $" canBeCooked={c.canBeCooked} wreck={c.wreckWhenCooked} preCooked={c.preCooked}"
            + $" behaviours={c.additionalCookingBehaviors.Length} explosionPrefab={(c.explosionPrefab == null ? "<none>" : c.explosionPrefab.name)}",
        _ => "",
    };

    /// <summary>
    /// What this run dealt the berry in hand, and the whole table behind it.
    ///
    /// A Shroomberry's effect and its stamina are both decided once at level generation and
    /// held in MushroomManager, not rolled when you eat one. Printing the slot this berry
    /// reads plus the full table is what makes a claim like "no stamina arrives" checkable:
    /// a berry dealt 0 and a berry that is broken look identical from the bar alone.
    /// </summary>
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

    /// <summary>
    /// What an Action_Spawn puts into the world.
    ///
    /// Sunscreen carries nothing but Action_ReduceUses and Action_Spawn - the protection, its
    /// duration and the cloud's lifetime are all on the thing it sprays - so a component list
    /// of the item alone explains none of it.
    /// </summary>
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

    /// <summary>
    /// The subtree a breakable item turns into, with the numbers on it.
    ///
    /// A component list alone cannot explain a Remedy Fungus, because everything it does
    /// lives on the prefab it leaves behind rather than on the item you are holding - and
    /// that subtree is reached by four hardcoded child names, any of which goes null the day
    /// the game renames one. Printing the real tree is how those names get corrected.
    /// </summary>
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

    /// <summary>How far down a break-prefab to walk before the particle systems take over.</summary>
    private const int MaxPrefabDepth = 4;

    /// <summary>
    /// Walks a prefab subtree, naming each child, **every** component on it, and the figures
    /// on the ones that describe an effect.
    ///
    /// Listing every component is the point. The first version of this printed only AOE,
    /// TimeEvent and RemoveAfterSeconds, which hid the very thing it was written to find: a
    /// Remedy Fungus heals over time from a lingering field, and the child holding it looked
    /// empty because a StatusField is none of those three.
    /// </summary>
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

    /// <summary>The fields worth seeing on a component inside a spawned prefab.</summary>
    private static string PrefabValues(Component component) => component switch
    {
        AOE a => $" {a.statusType}={a.statusAmount} range={a.range} minFactor={a.minFactor}"
            + (a.hasAffliction && a.affliction != null
                ? $" affliction={a.affliction.GetAfflictionType()} totalTime={a.affliction.totalTime}"
                : "")
            + $" factorPow={a.factorPow} ignoreFactor={a.ignoreFactor}"
            + (a.addtlStatus != null && a.addtlStatus.Length > 0
                ? $" addtl=[{string.Join(", ", a.addtlStatus)}]"
                    + $" overrides=[{string.Join(", ", a.addlStatusAmountOverrides ?? new List<float>())}]"
                : ""),
        Peak.StatusFieldBase f => $" {f.statusType}={f.statusAmountPerSecond}/s"
            + $" onEntry={f.statusAmountOnEntry} delay={f.delayBeforeStatusOverTime}"
            + $" addtl=[{DescribeAdditional(f)}]"
            + (f is StatusField s ? $" radius={s.radius} tickBased={s.tickBased}"
                + $" timeBetweenTicks={s.timeBetweenTicks}" : ""),
        TimeEvent t => $" rate={t.rate} repeating={t.repeating}",
        RemoveAfterSeconds r => $" seconds={r.seconds}",
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
