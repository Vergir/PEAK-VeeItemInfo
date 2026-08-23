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
        ItemCooking c => $" canBeCooked={c.canBeCooked} wreck={c.wreckWhenCooked} preCooked={c.preCooked}"
            + $" behaviours={c.additionalCookingBehaviors.Length} explosionPrefab={(c.explosionPrefab == null ? "<none>" : c.explosionPrefab.name)}",
        _ => "",
    };

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
