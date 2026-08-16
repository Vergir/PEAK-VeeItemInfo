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
        }

        Plugin.Log.LogInfo(report.ToString());
    }
}
