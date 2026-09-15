using System;
using UnityEngine;

namespace VeeItemInfo;

/// <summary>
/// The single place update logic lives. Every Harmony patch routes here and does no work of
/// its own.
/// </summary>
internal static class ItemInfoController
{
    /// <summary>Not a config value: the setting is whether periodic re-checks happen at all.</summary>
    private const float RefreshInterval = 1f;

    private static bool dirty = true;
    private static float lastKnownSinceItemAttach;

    internal static void MarkDirty()
    {
        dirty = true;
    }

    internal static void MarkDirtyIfObserved(Character? character)
    {
        if (character != null && Character.ReferenceEquals(Character.observedCharacter, character))
        {
            dirty = true;
        }
    }

    /// <summary>Once per frame: rebuilds the overlay when flagged stale, and positions it.</summary>
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
                // A change detector as much as a timer: sinceItemAttach is reset to zero on
                // every attach, so this also fires at once when a watched player's item
                // changes hands. Re-render next frame, so a slow build cannot land in the
                // same frame as the check that asked for it.
                dirty = true;
                lastKnownSinceItemAttach = observed.data.sinceItemAttach;
            }

            // After the text is built, so the box is sized to the current content.
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
        // Before Build, so an item that makes Build throw still gets dumped.
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
