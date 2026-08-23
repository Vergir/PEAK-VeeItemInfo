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
        ItemCooking c => $" canBeCooked={c.canBeCooked} wreck={c.wreckWhenCooked} preCooked={c.preCooked}"
            + $" behaviours={c.additionalCookingBehaviors.Length} explosionPrefab={(c.explosionPrefab == null ? "<none>" : c.explosionPrefab.name)}",
        _ => "",
    };
}
