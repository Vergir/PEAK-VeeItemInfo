using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using UnityEngine;

namespace VeeItemInfo;

/// <summary>
/// Writes every item in the database to one file: its components with the fields that drive
/// the description, and what <see cref="ItemDescriptionBuilder.Build"/> returns for it.
///
/// One file answers what would otherwise be a round trip per item - which affliction a dart
/// carries, whether any prefab sets a flag, which items show a blank Effects section. It runs
/// on the database prefabs rather than live items, which is also the shape the preview page
/// will need, so this doubles as the proof that Build works on a prefab at all.
/// </summary>
internal static class ItemDump
{
    private const string FileName = "VeeItemInfo-items.txt";

    private static bool written;

    /// <summary>
    /// Writes the dump the first time this is called after debug logging was switched on.
    /// Called from the held-item refresh, because holding an item is the one moment the
    /// database is certain to be loaded - a switch-on in the menu screen would find nothing.
    /// </summary>
    internal static void WriteOnce()
    {
        if (written)
        {
            return;
        }

        written = true;
        Write();
    }

    /// <summary>Arms the next <see cref="WriteOnce"/>, for a switch-off or a hot reload.</summary>
    internal static void Forget() => written = false;

    internal static void Write()
    {
        string path = Path.Combine(Paths.BepInExRootPath, FileName);

        // Sorted by name, so two dumps from two builds diff cleanly.
        SortedDictionary<string, Item> items = new(StringComparer.Ordinal);
        foreach (ItemDatabase database in Resources.FindObjectsOfTypeAll<ItemDatabase>())
        {
            if (database.itemLookup == null)
            {
                continue;
            }

            foreach (KeyValuePair<ushort, Item> entry in database.itemLookup)
            {
                if (entry.Value != null)
                {
                    items[$"{entry.Value.gameObject.name} #{entry.Key}"] = entry.Value;
                }
            }
        }

        StringBuilder text = new();
        List<PreviewPage.Row> rows = new();
        int failed = 0;
        foreach (KeyValuePair<string, Item> entry in items)
        {
            text.Append("==== ").Append(entry.Key).Append(" ====\n");

            // Each item on its own, so one prefab that throws costs one entry rather than
            // the whole file.
            try
            {
                text.Append(ItemDebug.Components(entry.Value)).Append('\n');
                string built = ItemDescriptionBuilder.Build(entry.Value);
                text.Append("[plain]\n").Append(Plain(built)).Append('\n');
                text.Append("[raw]\n").Append(built).Append('\n');
                rows.Add(new PreviewPage.Row(entry.Value, built, null));
            }
            catch (Exception e)
            {
                failed++;
                text.Append("[failed] ").Append(e.GetType().Name).Append(": ").Append(e.Message)
                    .Append('\n').Append(e.StackTrace).Append('\n');
                rows.Add(new PreviewPage.Row(entry.Value, null, e.GetType().Name + ": " + e.Message));
            }

            text.Append('\n');
        }

        File.WriteAllText(path, text.ToString());

        // The same rows as a page, beside the text: the showcase is this dump rendered.
        string pagePath = Path.Combine(Paths.BepInExRootPath, "VeeItemInfo-showcase.html");
        File.WriteAllText(pagePath, PreviewPage.Render(rows));

        Plugin.Log.LogInfo($"[dump] {items.Count} items, {failed} failed -> {path} and {pagePath}");
    }

    /// <summary>
    /// The rich text with its colour tags stripped and each sprite reduced to its name in
    /// braces, so a description reads as "+20 {Hunger}" rather than a wall of markup.
    /// </summary>
    private static string Plain(string richText)
    {
        string sprites = Regex.Replace(richText, "<sprite[^>]*name=\"([^\"]+)\"[^>]*>", "{$1}");
        return Regex.Replace(sprites, "<[^>]+>", "");
    }
}
