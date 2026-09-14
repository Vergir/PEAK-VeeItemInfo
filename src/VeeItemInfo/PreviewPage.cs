using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace VeeItemInfo;

/// <summary>
/// The showcase: one HTML page of cards, each the overlay as it draws in game with the
/// item's picture and name under it. Generated from the real
/// <see cref="ItemDescriptionBuilder.Build"/>, so it can never disagree with the overlay - and
/// so it is not a specification. No artwork lives in the repository: every icon is hotlinked
/// from peak.wiki.gg, and a tinted sprite is recoloured with an SVG filter because a browser
/// refuses a cross-origin image as a CSS mask. See docs/internals_infra.md, "The showcase".
/// </summary>
internal static class PreviewPage
{
    internal sealed class Row
    {
        internal Row(Item item, string? built, string? error)
        {
            Item = item;
            Built = built;
            Error = error;
        }

        internal Item Item { get; }

        /// <summary>The overlay's rich text, or null where Build threw.</summary>
        internal string? Built { get; }

        internal string? Error { get; }
    }

    private const string Thumb = "https://peak.wiki.gg/images/thumb/{0}.png/64px-{0}.png";
    private const string Wiki = "https://peak.wiki.gg/wiki/";

    /// <summary>
    /// Wiki file names for every sprite the overlay can emit. Statuses first - the wiki keeps
    /// them under Status_*, with two names that differ from the game's (Extra Stamina is
    /// "Bonus stamina" there, Shield is "Invincibility") - then the item icons the overlay
    /// borrows for the rope pair, the balloons and the generic item glyph.
    ///
    /// Whether a sprite is tinted is not decided here: the sprite tag carries tint=0 or 1,
    /// set by StatusIcons.ShouldTint from the texture, and the page follows the tag.
    /// </summary>
    private static readonly Dictionary<string, string> Files = new()
    {
        { "Hunger", "Status_Hunger" },
        { "ExtraStamina", "Status_Bonus_stamina" },
        { "Injury", "Status_Injury" },
        { "Poison", "Status_Poison" },
        { "Cold", "Status_Cold" },
        { "Hot", "Status_Heat" },
        { "Heat", "Status_Heat" },
        { "Drowsy", "Status_Drowsy" },
        { "Spores", "Status_Spores" },
        { "Thorns", "Status_Thorns" },
        { "Weight", "Status_Weight" },
        { "Curse", "Status_Curse" },
        { "Petrify", "Status_Petrify" },
        { "Shield", "Status_Invincibility" },
        { "Numb", "Status_Numb" },
        { "Web", "Status_Web" },
        { "Arrow", "Status_Arrow" },
        { "FlyTrap", "Status_FlyTrap" },
        { "Item", "Bing_Bong" },
        { "RopeCannon", "Rope_Cannon" },
        { "RopeCannonAnti", "Anti-Rope_Cannon" },
        { "RopeSpool", "Rope_Spool" },
        { "RopeSpoolAnti", "Anti-Rope_Spool" },
        { "Float", "Balloon" },
        { "FloatBunch", "Balloon_Bunch" },
    };

    /// <summary>
    /// The wiki has no campfire status icon - the mod scrapes the real one off the stamina
    /// bar at runtime - and a stovetop item icon standing in for it read as an item rather
    /// than as "fire". An emoji says it and costs no request.
    /// </summary>
    private const string CookEmoji = "🔥";

    /// <summary>
    /// The three things the overlay's rich text is made of: a colour open, a colour close,
    /// and a sprite. Everything between them is plain text.
    /// </summary>
    private static readonly Regex Tag = new(
        "<#([0-9A-Fa-f]{6})>|</color>|<sprite name=\"([^\"]+)\" tint=(\\d)>", RegexOptions.Compiled);

    /// <summary>The overlay's own text colour, for a sprite outside any colour tag.</summary>
    private const string BaseColour = "F2ECDE";

    internal static string Render(List<Row> rows)
    {
        // One card per distinct (name, overlay): fifteen Torn Pages and twenty chess pieces
        // say the same thing, and the card notes how many prefabs it stands for.
        List<Card> cards = Collapse(rows);
        cards.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        HashSet<string> tints = new(StringComparer.OrdinalIgnoreCase);
        StringBuilder body = new();
        foreach (Card card in cards)
        {
            string file = WikiFile(card.Name);
            body.Append("<div class=card>")
                .Append("<div class=ov>");
            if (card.Built != null)
            {
                body.Append(ToHtml(card.Built, tints));
            }
            else
            {
                body.Append("<span class=failed>Build threw: ").Append(Escape(card.Error ?? "")).Append("</span>");
            }

            body.Append("</div>")
                .Append("<img class=item src=\"").Append(string.Format(Thumb, file))
                .Append("\" alt=\"\" loading=lazy onerror=\"this.remove()\">")
                .Append("<div class=name><a href=\"").Append(Wiki).Append(file)
                .Append("\" target=_blank rel=noopener>").Append(Escape(card.Name)).Append("</a></div>")
                .Append("<div class=internal>").Append(Escape(card.Prefabs)).Append("</div>")
                .Append("</div>\n");
        }

        StringBuilder page = new();
        page.Append("<!doctype html><html lang=en><head><meta charset=utf-8>\n")
            .Append("<meta name=viewport content='width=device-width,initial-scale=1'>\n")
            .Append("<title>VeeItemInfo &ndash; Showcase</title>\n")
            .Append("<style>").Append(Css).Append("</style></head><body>\n");

        page.Append("<header><h1>VeeItemInfo</h1>\n")
            .Append("<p class=sub>What the overlay shows for every item in PEAK, as it draws in game. ")
            .Append("Generated by the mod itself from the same code that draws the overlay.</p>\n")
            .Append("<p class=meta>PEAK ").Append(Escape(Application.version))
            .Append(" &middot; VeeItemInfo ").Append(Escape(Plugin.Version))
            .Append(" &middot; ").Append(cards.Count).Append(" items</p>\n")
            .Append("</header>\n");

        // One SVG filter per colour a tinted icon appeared in. feFlood paints the colour,
        // feComposite keeps it only where the icon's alpha is - the game's tint=1, without
        // ever reading the cross-origin pixels back.
        page.Append("<svg width=0 height=0 style='position:absolute' aria-hidden=true><defs>\n");
        foreach (string tint in tints)
        {
            page.Append("<filter id=\"t").Append(tint).Append("\" color-interpolation-filters=\"sRGB\">")
                .Append("<feFlood flood-color=\"#").Append(tint).Append("\"/>")
                .Append("<feComposite in2=\"SourceAlpha\" operator=\"in\"/></filter>\n");
        }

        page.Append("</defs></svg>\n")
            .Append("<main>\n").Append(body).Append("</main>\n")
            .Append("</body></html>\n");

        return page.ToString();
    }

    private sealed class Card
    {
        internal Card(string name, string? built, string? error)
        {
            Name = name;
            Built = built;
            Error = error;
        }

        internal string Name { get; }
        internal string? Built { get; }
        internal string? Error { get; }
        internal List<string> PrefabNames { get; } = new();

        /// <summary>The prefab name, or the first with a count where several collapsed.</summary>
        internal string Prefabs => PrefabNames.Count == 1
            ? PrefabNames[0]
            : $"{PrefabNames[0]} ×{PrefabNames.Count}";
    }

    private static List<Card> Collapse(List<Row> rows)
    {
        Dictionary<string, Card> byKey = new(StringComparer.Ordinal);
        List<Card> cards = new();
        foreach (Row row in rows)
        {
            string name = DisplayName(row.Item);
            string key = name + "\n" + (row.Built ?? "!" + row.Error);
            if (!byKey.TryGetValue(key, out Card? card))
            {
                card = new Card(name, row.Built, row.Error);
                byKey[key] = card;
                cards.Add(card);
            }

            card.PrefabNames.Add(row.Item.gameObject.name);
        }

        return cards;
    }

    /// <summary>
    /// The overlay's TMP rich text as HTML. Colour tags become spans, sprites become wiki
    /// icons, newlines become breaks and everything else is escaped text. Every colour a
    /// tinted sprite sits in is added to <paramref name="tints"/> so the page can define
    /// its filter.
    /// </summary>
    internal static string ToHtml(string richText, HashSet<string> tints)
    {
        StringBuilder html = new();
        Stack<string> colours = new();
        int last = 0;
        foreach (Match match in Tag.Matches(richText))
        {
            AppendText(html, richText.Substring(last, match.Index - last));
            last = match.Index + match.Length;

            if (match.Groups[1].Success)
            {
                string colour = match.Groups[1].Value.ToUpperInvariant();
                colours.Push(colour);
                html.Append("<span style=\"color:#").Append(colour).Append("\">");
            }
            else if (match.Groups[2].Success)
            {
                string colour = colours.Count > 0 ? colours.Peek() : BaseColour;
                html.Append(Icon(match.Groups[2].Value, match.Groups[3].Value == "1", colour, tints));
            }
            else
            {
                if (colours.Count > 0)
                {
                    colours.Pop();
                }

                html.Append("</span>");
            }
        }

        AppendText(html, richText.Substring(last));
        return html.ToString();
    }

    private static void AppendText(StringBuilder html, string text)
    {
        html.Append(Escape(text).Replace("\n", "<br>"));
    }

    /// <summary>
    /// One sprite. A tinted one is the wiki's white silhouette recoloured by the filter for
    /// the colour it sits in, exactly as the game's tint=1 sprite takes the text colour; an
    /// untinted one is the wiki's image as it is. A key with no wiki file is shown as its
    /// name, so a gap in the table above is visible rather than blank.
    /// </summary>
    private static string Icon(string name, bool tinted, string colour, HashSet<string> tints)
    {
        if (name == "Cook")
        {
            return "<span class=emoji title=Cook>" + CookEmoji + "</span>";
        }

        if (!Files.TryGetValue(name, out string? file))
        {
            return "<span class=fallback>" + Escape(name) + "</span>";
        }

        string url = string.Format(Thumb, file);
        string tag = "<img class=si src=\"" + url + "\" alt=\"" + Escape(name) + "\" loading=lazy";
        if (tinted)
        {
            tints.Add(colour);
            tag += " style=\"filter:url(#t" + colour + ")\"";
        }

        return tag + ">";
    }

    /// <summary>
    /// The name a player knows the item by, in the case the wiki files it under.
    ///
    /// GetName is the game's localisation and always has the right words, but in the HUD's
    /// upper case - "GRANOLA BAR" - and wiki file names are case-sensitive. UIData.itemName
    /// was tried first and is worse: it is the localisation key's source and is a key for
    /// some items ("AMULET_CLONE", "VOIDLAUNCHER") and lower case for others. So the
    /// localised name is title-cased, with the small words the wiki keeps lower - "Bugle of
    /// Friendship", "The Book of Bones" - left alone.
    /// </summary>
    private static string DisplayName(Item item)
    {
        string? name = null;
        try
        {
            name = item.GetName();
        }
        catch (Exception)
        {
            // Localisation not up; fall through to the raw name.
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = item.UIData != null && !string.IsNullOrWhiteSpace(item.UIData.itemName)
                ? item.UIData.itemName
                : item.gameObject.name;
        }

        // Two amulets are spelled with a typographic apostrophe in the game; the wiki uses a
        // straight one everywhere, and TextInfo would capitalise the letter after a curly one.
        string cased = TitleCase(name!.Replace('’', '\''));
        return WikiAliases.TryGetValue(cased, out string? alias) ? alias : cased;
    }

    /// <summary>
    /// The few real items the wiki files under a different name from the game's own. Props,
    /// guidebook pages and unlocalised leftovers are not here - they have no page to find.
    /// </summary>
    private static readonly Dictionary<string, string> WikiAliases = new(StringComparer.Ordinal)
    {
        { "Coconut Half", "Half-Coconut" },
        { "Bird", "Cooked Bird" },
    };

    private static readonly HashSet<string> SmallWords = new(StringComparer.Ordinal)
    {
        "of", "the", "and", "a", "an", "in", "on", "to",
    };

    /// <summary>
    /// "SCOUT'S INITIATIVE" to "Scout's Initiative", "BUGLE OF FRIENDSHIP" to "Bugle of
    /// Friendship". TextInfo handles apostrophes and hyphens; the small-word rule is ours,
    /// and never applies to the first word.
    /// </summary>
    private static string TitleCase(string upper)
    {
        string[] words = System.Globalization.CultureInfo.InvariantCulture.TextInfo
            .ToTitleCase(upper.ToLowerInvariant()).Split(' ');
        for (int i = 1; i < words.Length; i++)
        {
            string lower = words[i].ToLowerInvariant();
            if (SmallWords.Contains(lower))
            {
                words[i] = lower;
            }
        }

        return string.Join(" ", words);
    }

    /// <summary>Display name to wiki file and page name: underscores for spaces, apostrophes escaped.</summary>
    private static string WikiFile(string displayName) =>
        displayName.Replace(' ', '_').Replace("'", "%27");

    private static string Escape(string text) => WebUtility.HtmlEncode(text);

    private const string Css = @"
:root{--bg:#14181d;--panel:#1b2027;--panel2:#20262e;--line:#2c343e;--ink:#dfe6ee;--dim:#8d9aa8;--accent:#BFEC1B;--overlay:#0d1014}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--ink);font:15px/1.55 'Segoe UI',system-ui,sans-serif}
a{color:var(--ink);text-decoration:none}
a:hover{color:var(--accent)}
header{padding:26px 32px 16px;border-bottom:1px solid var(--line);background:var(--panel);position:sticky;top:0;z-index:2}
h1{margin:0 0 4px;font-size:26px}
.sub{color:var(--dim);max-width:90ch;margin:0 0 4px}
.meta{color:var(--dim);font-size:12.5px;margin:0}
main{display:grid;grid-template-columns:repeat(auto-fill,minmax(230px,1fr));gap:16px;padding:24px 32px 80px}
.card{display:flex;flex-direction:column;align-items:center;justify-content:flex-end;background:var(--panel);border:1px solid var(--line);border-radius:10px;padding:14px 12px 12px}
.card:hover{border-color:#3b4652}
.ov{width:100%;text-align:center;background:var(--overlay);border:1px solid var(--line);border-radius:6px;padding:9px 10px;font-family:Consolas,ui-monospace,monospace;font-size:13px;line-height:1.7;letter-spacing:.02em;white-space:nowrap;overflow-x:auto;margin-bottom:12px}
img.item{width:64px;height:64px;margin:2px 0 8px}
.name{font-weight:600;text-align:center;line-height:1.25}
.internal{color:var(--dim);font-size:11px;margin-top:3px;text-align:center}
.failed{color:#ff8fa3;font-size:12px;white-space:normal}
img.si{height:1.1em;width:auto;vertical-align:-.2em;margin:0 .05em}
.emoji{font-size:1.05em;line-height:1}
.fallback{color:#ff8fa3;font-weight:600;font-size:.85em;letter-spacing:.06em}
";
}
