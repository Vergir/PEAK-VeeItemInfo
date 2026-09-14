using System;
using UnityEngine;

namespace VeeItemInfo;

/// <summary>
/// The single place update logic lives. Every Harmony patch routes here and does no
/// work of its own, so hooks can be added, swapped or dropped without touching behaviour.
/// </summary>
internal static class ItemInfoController
{
    /// <summary>
    /// How long between periodic re-checks. Not a config value: the setting is whether they
    /// happen at all, because the thing being waited for - somebody else swapping the item
    /// in their hands - is not something a player tunes in fractions of a second.
    /// </summary>
    private const float RefreshInterval = 1f;

    private static bool dirty = true;
    private static float lastKnownSinceItemAttach;

    /// <summary>Flags the overlay as stale, rebuilding it on the next tick.</summary>
    internal static void MarkDirty()
    {
        dirty = true;
    }

    /// <summary>Flags the overlay as stale, but only for the character we're watching.</summary>
    internal static void MarkDirtyIfObserved(Character? character)
    {
        if (character != null && Character.ReferenceEquals(Character.observedCharacter, character))
        {
            dirty = true;
        }
    }

    /// <summary>
    /// Called once per frame. Rebuilds the overlay when something flagged it stale, and
    /// otherwise falls back to a timed poll for values that no hook covers.
    /// </summary>
    internal static void Tick()
    {
        try
        {
            if (!Overlay.EnsureCreated())
            {
                return;
            }

            Character observed = Character.observedCharacter;
            Item item = observed?.data?.currentItem!;
            if (observed == null || item == null)
            {
                Overlay.SetVisible(false);
                return;
            }

            Overlay.EnsureIcons();

            if (dirty)
            {
                dirty = false;
                Refresh(item);
            }
            else if (PluginConfig.PeriodicRefresh.Value
                && Mathf.Abs(observed.data.sinceItemAttach - lastKnownSinceItemAttach) >= RefreshInterval)
            {
                // This reads as a timer and is really a change detector: sinceItemAttach counts
                // up in real time *and* is reset to zero on every attach, so one comparison
                // fires on a schedule and fires at once when a watched player's item changes
                // hands. Nothing the overlay prints changes while you hold an item, so there
                // is nothing else to poll for. Re-render next frame rather than here, so a slow
                // build cannot land in the same frame as the check that asked for it.
                dirty = true;
                lastKnownSinceItemAttach = observed.data.sinceItemAttach;
            }

            // Positioned after the text is built, so the box is sized to the current
            // content. Follows the slot holding this item, so the overlay moves with the
            // selection and holds through resolution and aspect ratio changes.
            Overlay.UpdatePosition(item);
            Overlay.SetVisible(true);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError(e.Message + e.StackTrace);
        }
    }

    private static void Refresh(Item item)
    {
        // The item dump comes first. It is the tool for working out why an item shows
        // nothing, and an item that makes Build throw is exactly that case - logging it
        // afterwards meant the one item you most needed to see never got dumped at all.
        if (PluginConfig.DebugLogging.Value)
        {
            ItemDebug.LogItem(item);
            ItemDump.WriteOnce();
        }

        string description = ItemDescriptionBuilder.Build(item);
        Overlay.SetText(description);

        if (PluginConfig.DebugLogging.Value)
        {
            ItemDebug.LogUntagged(description);
        }
    }
}
