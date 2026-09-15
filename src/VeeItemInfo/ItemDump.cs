using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using UnityEngine;

namespace VeeItemInfo;

/// <summary>
/// Writes every item in the database to one file - its components and what
/// <see cref="ItemDescriptionBuilder.Build"/> returns for it - and the showcase beside it.
/// </summary>
internal static class ItemDump
{
    private const string FileName = "VeeItemInfo-items.txt";

    private static bool written;

    /// <summary>
    /// Called from the held-item refresh, because holding an item is the one moment the
    /// database is certain to be loaded.
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

        // The showcase should read as the ordinary case, whoever happened to run the dump.
        ItemDescriptionBuilder.AssumeHuman = true;
        try
        {
            Dump(items, text, rows, ref failed);
        }
        finally
        {
            ItemDescriptionBuilder.AssumeHuman = false;
        }

        File.WriteAllText(path, text.ToString());

        string pagePath = Path.Combine(Paths.BepInExRootPath, "VeeItemInfo-showcase.html");
        File.WriteAllText(pagePath, PreviewPage.Render(rows));

        Plugin.Log.LogInfo($"[dump] {items.Count} items, {failed} failed -> {path} and {pagePath}");
    }

    private static void Dump(SortedDictionary<string, Item> items, StringBuilder text,
        List<PreviewPage.Row> rows, ref int failed)
    {
        foreach (KeyValuePair<string, Item> entry in items)
        {
            text.Append("==== ").Append(entry.Key).Append(" ====\n");

            // One prefab that throws costs one entry rather than the whole file.
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
    }

    /// <summary>Colour tags stripped and each sprite reduced to "{Name}".</summary>
    private static string Plain(string richText)
    {
        string sprites = Regex.Replace(richText, "<sprite[^>]*name=\"([^\"]+)\"[^>]*>", "{$1}");
        return Regex.Replace(sprites, "<[^>]+>", "");
    }
}
