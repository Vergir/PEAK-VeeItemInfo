using System;
using UnityEngine;

namespace VeeItemInfo;

/// <summary>
/// The single place update logic lives. Every Harmony patch routes here and does no
/// work of its own, so hooks can be added, swapped or dropped without touching behaviour.
/// </summary>
internal static class ItemInfoController
{
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
            else if (Mathf.Abs(observed.data.sinceItemAttach - lastKnownSinceItemAttach) >= PluginConfig.ForceUpdateTime.Value)
            {
                // Re-render on the next frame rather than here, so a slow build can't
                // land in the same frame as the poll that asked for it.
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
        Overlay.SetText(ItemDescriptionBuilder.Build(item));

        if (PluginConfig.DebugLogging.Value)
        {
            ItemDebug.LogItem(item);
            Overlay.LogDiagnostics();
            StatusIcons.LogDiagnostics();
        }
    }
}
