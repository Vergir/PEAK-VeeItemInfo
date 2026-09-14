using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;

namespace VeeItemInfo;

/// <summary>
/// Makes the game's own status icons usable inside our TextMeshPro text: scraped off the
/// HUD, packed into one atlas (a sprite asset draws from exactly one texture, and chained
/// fallbacks do not resolve), and exposed as a TMP_SpriteAsset. Nothing here may assume a
/// size or alignment it has not read - the source rects are neither square nor
/// whole-numbered. Everything degrades to plain text: if any of this fails, <see cref="Tag"/>
/// returns the status name and the overlay stays readable. The pipeline and its traps are
/// in docs/internals_infra.md, "Icons".
/// </summary>
internal static class StatusIcons
{
    private static readonly Dictionary<string, string> Tags = new();
    private static readonly List<TMP_SpriteGlyph> Glyphs = new();
    private static readonly List<float> Aspects = new();
    private static TMP_SpriteAsset? spriteAsset;
    private static Texture2D? atlas;

    /// <summary>
    /// Source textures this class created rather than borrowed. Scraped icons belong to the
    /// game and must never be destroyed; anything loaded from an embedded resource is ours
    /// and leaks one texture per atlas rebuild if it is not cleaned up. Rebuilds happen on
    /// every hot reload and on every HUD rebuild, so that adds up.
    /// </summary>
    private static readonly List<Texture2D> OwnedSources = new();

    // Building involves a full scene scan, so a failed attempt must never be retried on
    // the next frame. Retries are spaced out and capped: the status bar may genuinely not
    // exist yet on the first few tries, but if it is never going to work we stop paying
    // for it rather than scanning the scene forever.
    private const int MaxAttempts = 15;
    private const float RetryInterval = 2f;

    /// <summary>
    /// Roughly the middle of an upper-case glyph, in em above the baseline. PEAK's HUD font
    /// is all caps, so this is what an icon should line up with. Dialled in against the
    /// game rather than derived - the font's own metrics put it slightly high.
    /// </summary>
    private const float CapCentre = 0.31f;

    /// <summary>
    /// Icon height as a fraction of the font size. Being a ratio rather than an absolute
    /// size is what lets Font Size stay the only knob: icons scale with the text and keep
    /// their alignment for free.
    /// </summary>
    private const float IconScale = 0.85f;

    /// <summary>
    /// How many pixels tall each icon is copied at. Height, not the long side, because a
    /// glyph's height is what <see cref="ApplyMetrics"/> pins to the font and the sources are
    /// nothing like square. 128 covers the whole Font Size range on a HiDPI display with room
    /// to spare. A constant on purpose: deriving it from the live font size was tried and
    /// bought nothing but a scene scan hanging off a config slider.
    /// </summary>
    private const int IconPixelHeight = 128;

    /// <summary>
    /// The sharpness the live atlas was baked with. Baked in rather than applied at render
    /// time, so moving the setting has to repack - the same reason a size change does.
    /// </summary>
    internal static float BuiltForSharpness { get; private set; }

    /// <summary>Whether the live atlas still matches the settings that produced it.</summary>
    internal static bool MatchesSettings =>
        Mathf.Approximately(BuiltForSharpness, PluginConfig.IconSharpness.Value);

    private static int attempts;
    private static float nextAttempt;

    internal static bool Available => Tags.Count > 0;

    /// <summary>
    /// True when the icons are built and their Unity objects are still alive. A scene load
    /// can destroy them out from under us while the mapping still looks populated.
    /// </summary>
    internal static bool IsValid => Available && spriteAsset != null && atlas != null;

    /// <summary>Throws away a dead build so the next EnsureBuilt starts fresh.</summary>
    internal static void Invalidate()
    {
        Reset();
        attempts = 0;
        nextAttempt = 0f;
    }

    internal static TMP_SpriteAsset? SpriteAsset => spriteAsset;

    /// <summary>
    /// A TMP rich-text tag for the status, or the plain upper-case name when no icon could
    /// be found. tint=1 makes the sprite take the surrounding text colour, so an icon
    /// matches the number in front of it.
    /// </summary>
    internal static string Tag(string status) =>
        Tags.TryGetValue(status, out string? tag) ? tag : status.ToUpper();

    /// <summary>
    /// Whether a status has a real icon, as opposed to the capitalised name
    /// <see cref="Tag"/> falls back to. Anything assembling a list of statuses to show needs
    /// this, because the fallback is English and the overlay has none.
    /// </summary>
    internal static bool HasIcon(string status) => Tags.ContainsKey(status);

    internal static void EnsureBuilt()
    {
        if (Available || attempts >= MaxAttempts || Time.unscaledTime < nextAttempt)
        {
            return;
        }

        nextAttempt = Time.unscaledTime + RetryInterval;
        attempts++;

        try
        {
            Build();
        }
        catch (Exception e)
        {
            Reset();
            Plugin.Log.LogWarning($"Status icon build attempt {attempts}/{MaxAttempts} failed: {e.Message}\n{e.StackTrace}");

            if (attempts >= MaxAttempts)
            {
                Plugin.Log.LogWarning("Giving up on status icons; text labels will be used for the rest of the session.");
            }
        }
    }

    private static void Build()
    {
        BarAffliction[] bars = UnityEngine.Object.FindObjectsByType<BarAffliction>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (bars.Length == 0)
        {
            return;
        }

        List<IconSource> icons = new();
        HashSet<string> seen = new();

        foreach (BarAffliction bar in bars)
        {
            Sprite? sprite = bar.icon?.sprite;
            if (sprite == null || sprite.texture == null)
            {
                continue;
            }

            // The petrify bar reports its afflictionType as Injury while carrying the
            // petrify icon. Without this, Injury gets the wrong picture.
            string name = bar.isPetrify ? "Petrify" : bar.afflictionType.ToString();

            // The colour rides along on the walk we are already doing.
            SampleBarColour(bar, name);

            if (seen.Add(name))
            {
                icons.Add(IconSource.FromSprite(name, sprite));
            }
        }

        // Extra Stamina has no BarAffliction - its icon lives on the stamina bar itself.
        StaminaBar? staminaBar = UnityEngine.Object.FindFirstObjectByType<StaminaBar>(FindObjectsInactive.Include);
        Sprite? staminaIcon = staminaBar?.extraStaminaIcon?.sprite;
        if (staminaIcon != null && seen.Add("ExtraStamina"))
        {
            icons.Add(IconSource.FromSprite("ExtraStamina", staminaIcon));
        }

        // Its colour comes from the short second stamina stripe; the icon beside it is tinted
        // to match and is sampled first so the bar's reading wins. Keyed *with* the space,
        // because that is what EffectColors is asked for - the sprite key has none only
        // because a sprite name cannot contain one.
        SampleIndicatorColour(staminaBar?.extraStaminaIcon, "Extra Stamina");
        SampleIndicatorColour(staminaBar?.extraBarStamina, "Extra Stamina");

        // Two more indicators hang off the stamina bar as plain GameObjects: the invincibility
        // marker, and the campfire shown while you cannot get hungry - the cooking hint's fire.
        AddBarIndicator(icons, seen, staminaBar?.shield, "Shield");
        AddBarIndicator(icons, seen, staminaBar?.campfire, "Cook");

        // A stand-in for "some item": the game's own mascot, found by its tag rather than name.
        AddItemIcon(icons, seen, "Item", FirstItemWith(item => item.itemTags.HasFlag(Item.ItemTags.BingBong)));

        // The Rope Cannon's two distances are told apart by its own icon and the spool's, with
        // a separate pair for floating rope. Asked of the components, not of four item names.
        AddItemIcon(icons, seen, "RopeCannon", FirstItemWith(item =>
            item.GetComponent<RopeShooter>() != null && item.GetComponent<Antigrav>() == null));
        AddItemIcon(icons, seen, "RopeCannonAnti", FirstItemWith(item =>
            item.GetComponent<RopeShooter>() != null && item.GetComponent<Antigrav>() != null));
        AddItemIcon(icons, seen, "RopeSpool", FirstItemWith(item =>
            item.GetComponent<RopeSpool>() is RopeSpool spool && !spool.isAntiRope));
        AddItemIcon(icons, seen, "RopeSpoolAnti", FirstItemWith(item =>
            item.GetComponent<RopeSpool>() is RopeSpool spool && spool.isAntiRope));

        // "You float": a balloon is the game's own picture of low gravity, and the bunch its
        // picture of three balloons' worth or more.
        AddItemIcon(icons, seen, "Float", FirstItemWith(item =>
            item.GetComponent<Balloon>() is Balloon balloon && !balloon.isBunch));
        AddItemIcon(icons, seen, "FloatBunch", FirstItemWith(item =>
            item.GetComponent<Balloon>() is Balloon balloon && balloon.isBunch));

        // Every item that another item turns into. Added last, so a status keeps its key if
        // an item ever shares the name.
        AddTransformationIcons(icons, seen);

        // The one icon that has to be shipped. Numbness is not a STATUSTYPE and has no
        // BarAffliction, so there is nothing in the scene to scrape - see assets/NOTICE.md
        // for why this file is here and what its licensing is.
        Texture2D? numb = LoadEmbedded("VeeItemInfo.numbness.png");
        if (numb != null && seen.Add("Numb"))
        {
            OwnedSources.Add(numb);
            icons.Add(IconSource.FromTexture("Numb", numb));
        }

        if (icons.Count == 0)
        {
            return;
        }

        BuildAtlas(icons);
    }

    /// <summary>
    /// Reads a status's colour off its own bar: the fill is the colour two of its Images
    /// share. Not brightest-wins (Curse's backing outshines its fill) and not sprite names (a
    /// lookup that goes quiet after a UI reshuffle). Finding no pair samples nothing, leaving
    /// the table in place - a wrong colour is worse than an old one. The bar's own icon is
    /// skipped because a tinted white silhouette would pair with anything.
    /// </summary>
    private static void SampleBarColour(BarAffliction bar, string name)
    {
        UnityEngine.UI.Image[] images = bar.GetComponentsInChildren<UnityEngine.UI.Image>(true);

        for (int i = 0; i < images.Length; i++)
        {
            if (images[i] == null || images[i] == bar.icon)
            {
                continue;
            }

            for (int j = i + 1; j < images.Length; j++)
            {
                if (images[j] == null || images[j] == bar.icon)
                {
                    continue;
                }

                if (Agree(images[i].color, images[j].color))
                {
                    EffectColors.Sample(name, images[i].color);
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Whether two Images are showing the same colour. Set from the same source, so this is
    /// really an equality test with room for the float round trip.
    /// </summary>
    private static bool Agree(Color a, Color b)
    {
        const float Tolerance = 1f / 255f;
        return Mathf.Abs(a.r - b.r) <= Tolerance
            && Mathf.Abs(a.g - b.g) <= Tolerance
            && Mathf.Abs(a.b - b.b) <= Tolerance;
    }

    /// <summary>
    /// Reads a colour off a single tinted Image somewhere under <paramref name="host"/>, for
    /// the markers that have no BarAffliction and so no agreeing pair to look for.
    /// </summary>
    private static void SampleIndicatorColour(Component? host, string name)
    {
        UnityEngine.UI.Image? image = host == null
            ? null
            : host.GetComponentInChildren<UnityEngine.UI.Image>(includeInactive: true);
        if (image != null)
        {
            EffectColors.Sample(name, image.color);
        }
    }

    /// <summary>
    /// Pulls the sprite out of one of the stamina bar's loose indicator objects. The Image
    /// may sit on the object itself or on a child, and the object is usually inactive -
    /// GetComponentInChildren needs includeInactive for that.
    /// </summary>
    private static void AddBarIndicator(List<IconSource> icons, HashSet<string> seen, GameObject? host, string name)
    {
        if (host == null || seen.Contains(name))
        {
            return;
        }

        UnityEngine.UI.Image? image = host.GetComponentInChildren<UnityEngine.UI.Image>(includeInactive: true);
        Sprite? sprite = image?.sprite;
        if (sprite == null || sprite.texture == null)
        {
            return;
        }

        // Shield and Cook are white silhouettes tinted by their Image, so that tint is the
        // HUD's colour for them. Sample refuses a white Image, so an untinted marker keeps
        // the hand-picked entry.
        EffectColors.Sample(name, image!.color);

        if (seen.Add(name))
        {
            icons.Add(IconSource.FromSprite(name, sprite));
        }
    }

    /// <summary>
    /// An icon to pack, whatever it came from. Status icons arrive as Sprites carrying a
    /// sub-rect of a texture; item icons arrive as bare Texture2Ds where the whole texture
    /// is the icon.
    /// </summary>
    private readonly struct IconSource
    {
        private IconSource(string name, Texture texture, Rect region)
        {
            Name = name;
            Texture = texture;
            Region = region;
        }

        internal string Name { get; }

        internal Texture Texture { get; }

        internal Rect Region { get; }

        internal static IconSource FromSprite(string name, Sprite sprite) =>
            new(name, sprite.texture, sprite.textureRect);

        internal static IconSource FromTexture(string name, Texture2D texture) =>
            new(name, texture, new Rect(0f, 0f, texture.width, texture.height));
    }

    /// <summary>
    /// Loads a PNG compiled into this assembly. The only shipped artwork is the numbness
    /// icon; everything else comes from the running game and is stored nowhere.
    ///
    /// The texture is created readable and flagged HideAndDontSave, like the scraped ones,
    /// so a scene load cannot unload it out from under the atlas.
    /// </summary>
    private static Texture2D? LoadEmbedded(string resourceName)
    {
        using System.IO.Stream? stream = typeof(StatusIcons).Assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            Plugin.Log.LogWarning($"Embedded resource '{resourceName}' not found; its icon will fall back to text.");
            return null;
        }

        byte[] bytes = new byte[stream.Length];
        int read = 0;
        while (read < bytes.Length)
        {
            int got = stream.Read(bytes, read, bytes.Length - read);
            if (got <= 0)
            {
                break;
            }

            read += got;
        }

        Texture2D texture = new(2, 2, TextureFormat.RGBA32, mipChain: false)
        {
            hideFlags = HideFlags.HideAndDontSave,
        };

        if (!texture.LoadImage(bytes))
        {
            UnityEngine.Object.Destroy(texture);
            return null;
        }

        return texture;
    }

    /// <summary>
    /// Packs the icon of every item that another item turns into - a Berrynana's peel
    /// (<c>Action_ConsumeAndSpawn.itemToSpawn</c>), a cooked Frog's legs
    /// (<c>CookingBehavior_ReplaceItem.replaceWithItem</c>). Collected by asking the prefabs,
    /// not by listing names, which would go stale silently.
    /// </summary>
    private static void AddTransformationIcons(List<IconSource> icons, HashSet<string> seen)
    {
        foreach (Item item in AllItems())
        {
            // A prefab is never active, so includeInactive is mandatory here.
            foreach (Action_ConsumeAndSpawn spawn in item.GetComponentsInChildren<Action_ConsumeAndSpawn>(true))
            {
                AddItemIcon(icons, seen, spawn.itemToSpawn);
            }

            foreach (ItemCooking cooking in item.GetComponentsInChildren<ItemCooking>(true))
            {
                foreach (AdditionalCookingBehavior behaviour in cooking.additionalCookingBehaviors
                    ?? Array.Empty<AdditionalCookingBehavior>())
                {
                    if (behaviour is CookingBehavior_ReplaceItem replace)
                    {
                        AddItemIcon(icons, seen, replace.replaceWithItem);
                    }
                }
            }
        }
    }

    /// <summary>
    /// The sprite key an item's own icon is registered under. Spaces come out because a
    /// sprite name containing one breaks the rich text tag, exactly as Extra Stamina is
    /// registered as ExtraStamina.
    /// </summary>
    private static string KeyFor(Item item) => item.name.Replace(" ", "");

    /// <summary>
    /// A tag for an item's own icon, falling back to the generic item glyph - never to the
    /// item's name, which is a prefab name: English and internal.
    /// </summary>
    internal static string ItemTag(Item item) =>
        Tags.TryGetValue(KeyFor(item), out string? tag) ? tag : Tag("Item");

    /// <summary>Registers one item's own icon under its own key.</summary>
    private static void AddItemIcon(List<IconSource> icons, HashSet<string> seen, Item? item)
    {
        if (item == null || item.UIData == null)
        {
            return;
        }

        Texture2D? icon = item.UIData.GetIcon();
        string key = KeyFor(item);
        if (icon != null && seen.Add(key))
        {
            icons.Add(IconSource.FromTexture(key, icon));
        }
    }

    /// <summary>
    /// Registers one item's icon under a key of our own, for the handful used as symbols for
    /// something other than themselves - the rope pair labelling two distances, the balloon
    /// standing for low gravity, BingBong standing for "some item".
    /// </summary>
    private static void AddItemIcon(List<IconSource> icons, HashSet<string> seen,
        string key, Item? item)
    {
        if (item == null)
        {
            return;
        }

        Texture2D? icon = item.UIData.GetIcon();
        if (icon != null && seen.Add(key))
        {
            icons.Add(IconSource.FromTexture(key, icon));
        }
    }

    /// <summary>
    /// The first item in the database that answers to <paramref name="matches"/>, or null.
    /// Asks what an item *is* rather than what it is called; a name goes stale without failing.
    /// </summary>
    private static Item? FirstItemWith(Func<Item, bool> matches)
    {
        foreach (Item entry in AllItems())
        {
            if (matches(entry))
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>
    /// Every item the game knows about, skipping any that carries no UI data to read an icon
    /// from.
    ///
    /// Resources rather than a singleton accessor: the database is a loaded ScriptableObject
    /// either way, and this needs no guess at the accessor's shape.
    /// </summary>
    internal static IEnumerable<Item> AllItems()
    {
        foreach (ItemDatabase database in Resources.FindObjectsOfTypeAll<ItemDatabase>())
        {
            if (database.itemLookup == null)
            {
                continue;
            }

            foreach (Item entry in database.itemLookup.Values)
            {
                if (entry != null && entry.UIData != null)
                {
                    yield return entry;
                }
            }
        }
    }

    /// <summary>
    /// Steepens an icon's alpha ramp around its midpoint, tightening the edge. The game's
    /// icons are soft because the artwork is soft - a 1:1 copy is exactly as soft as the
    /// packed one - so this, not resolution, is what sharpens them. Runs after the downscale
    /// because the downscale softens the edge again. See docs/internals_game.md, "The HUD".
    /// </summary>
    private static void Sharpen(Texture2D copy)
    {
        float strength = PluginConfig.IconSharpness.Value;
        if (strength <= 1f)
        {
            return;
        }

        Color32[] pixels = copy.GetPixels32();
        for (int i = 0; i < pixels.Length; i++)
        {
            float alpha = Mathf.Clamp01(((pixels[i].a / 255f) - 0.5f) * strength + 0.5f);
            pixels[i].a = (byte)Mathf.RoundToInt(alpha * 255f);
        }

        copy.SetPixels32(pixels);
        copy.Apply();
    }

    /// <summary>
    /// Whether an icon should take the colour of the text beside it. <c>tint=1</c> multiplies
    /// the sprite by the text colour: right for a white silhouette, wrong for artwork with its
    /// own colours, which it darkens. Decided by looking at the texture rather than by a list
    /// of names, which would rot the first time the game recoloured an icon.
    /// </summary>
    private static bool ShouldTint(Texture2D texture)
    {
        const float SaturationThreshold = 0.2f;
        const float ColouredPixelShare = 0.15f;

        // A coarse grid is plenty: this only has to tell a flat silhouette from artwork, and
        // it runs once per icon at build time rather than per frame.
        int step = Mathf.Max(1, Mathf.Min(texture.width, texture.height) / 24);
        int opaque = 0;
        int coloured = 0;

        for (int y = 0; y < texture.height; y += step)
        {
            for (int x = 0; x < texture.width; x += step)
            {
                Color pixel = texture.GetPixel(x, y);
                if (pixel.a < 0.5f)
                {
                    continue;
                }

                opaque++;
                float max = Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b));
                float min = Mathf.Min(pixel.r, Mathf.Min(pixel.g, pixel.b));
                if (max > 0f && (max - min) / max > SaturationThreshold)
                {
                    coloured++;
                }
            }
        }

        // No opaque pixels at all means nothing to judge; tinting is the safer default.
        return opaque == 0 || (float)coloured / opaque < ColouredPixelShare;
    }

    private static void BuildAtlas(List<IconSource> icons)
    {
        Shader shader = Shader.Find("TextMeshPro/Sprite");
        if (shader == null)
        {
            throw new InvalidOperationException("Shader 'TextMeshPro/Sprite' not found.");
        }

        // Source textures are GPU-side and usually not CPU-readable, so blit each one into
        // a readable copy before packing.
        Texture2D[] copies = new Texture2D[icons.Count];

        // Whether each icon's own art is already coloured, decided by looking at it rather
        // than by keeping a list that would rot. See ShouldTint for why it matters.
        bool[] tintable = new bool[icons.Count];

        try
        {
            for (int i = 0; i < icons.Count; i++)
            {
                copies[i] = MakeReadable(icons[i].Texture, icons[i].Region, IconPixelHeight);
                tintable[i] = ShouldTint(copies[i]);
                Sharpen(copies[i]);
            }

            atlas = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            Rect[] uvs = atlas.PackTextures(copies, 2, 4096);

            TMP_SpriteAsset asset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
            asset.name = "VeeItemInfo Status Icons";
            asset.spriteSheet = atlas;
            asset.fallbackSpriteAssets ??= new List<TMP_SpriteAsset>();
            // TMP treats a version-less asset as legacy and runs an upgrade pass that walks
            // spriteInfoList, which is null on a runtime-created instance. Give it an empty
            // list and let the upgrade run once before we populate the real tables.
            asset.spriteInfoList = new List<TMP_Sprite>();
            asset.faceInfo = new FaceInfo
            {
                pointSize = 1,
                scale = 1f,
                lineHeight = 1f,
                ascentLine = 1f,
                baseline = 0f,
                descentLine = 0f,
            };
            asset.material = new Material(shader);
            asset.material.SetTexture(ShaderUtilities.ID_MainTex, atlas);
            asset.UpdateLookupTables();

            if (asset.spriteCharacterTable == null || asset.spriteGlyphTable == null)
            {
                throw new InvalidOperationException("TMP sprite tables were not initialised.");
            }

            for (int i = 0; i < icons.Count; i++)
            {
                string name = icons[i].Name;
                Rect uv = uvs[i];

                // The packed cell is the glyph, because each copy was cropped to its own rect.
                // Its size is taken from the copy, not the packer's UV rect: rounding UVs back
                // to texels can land a pixel out, and a glyph rect one pixel wrong rescales
                // the sprite rather than cropping it, softening the whole icon.
                int x = Mathf.RoundToInt(uv.x * atlas.width);
                int y = Mathf.RoundToInt(uv.y * atlas.height);
                int w = copies[i].width;
                int h = copies[i].height;

                int packedWidth = Mathf.RoundToInt(uv.width * atlas.width);
                if (Mathf.Abs(packedWidth - w) > 1)
                {
                    Plugin.Log.LogWarning(
                        $"Atlas packing scaled '{name}' from {w}px to {packedWidth}px wide; "
                        + "its glyph will be sampled from the packed size instead.");
                    w = Mathf.Max(1, packedWidth);
                    h = Mathf.Max(1, Mathf.RoundToInt(uv.height * atlas.height));
                }

                TMP_SpriteGlyph glyph = new TMP_SpriteGlyph
                {
                    index = (uint)i,
                    glyphRect = new GlyphRect(x, y, w, h),
                    scale = 1f,
                    atlasIndex = 0,
                };

                asset.spriteGlyphTable.Add(glyph);
                asset.spriteCharacterTable.Add(new TMP_SpriteCharacter(0u, asset, glyph) { name = name });
                Glyphs.Add(glyph);
                // Normalise against height so every icon renders at the same visual size,
                // whatever its source resolution. ApplyScale turns this into real metrics.
                Aspects.Add((float)w / h);
                Tags[name] = $"<sprite name=\"{name}\" tint={(tintable[i] ? 1 : 0)}>";
            }

            asset.UpdateLookupTables();
            spriteAsset = asset;

            ApplyMetrics();
            BuiltForSharpness = PluginConfig.IconSharpness.Value;

            // Runtime-generated assets belong to no scene, so a scene load would otherwise
            // unload them and leave the text pointing at freed sprites - which TMP draws as
            // its missing-sprite placeholder.
            atlas.hideFlags = HideFlags.HideAndDontSave;
            asset.hideFlags = HideFlags.HideAndDontSave;
            asset.material.hideFlags = HideFlags.HideAndDontSave;

            // Our own aliases for statuses the game names differently, and for the one
            // key that has a space in it - a space would break the rich text tag.
            Alias("Hot", "Heat");
            Alias("Drowsy", "Sleepy");
            Alias("ExtraStamina", "Extra Stamina");

            // Name the untinted ones: an icon rendering muddy is almost always this decision
            // going the wrong way, and it is invisible without being told.
            List<string> ownColours = new();
            for (int i = 0; i < icons.Count; i++)
            {
                if (!tintable[i])
                {
                    ownColours.Add(icons[i].Name);
                }
            }

            // What each icon was *before* the copy shrank it. An icon whose source is already
            // at or under IconPixelHeight looks the way it looks because of its artwork, and no
            // amount of repacking will help it.
            SortedDictionary<string, List<string>> sources = new();
            for (int i = 0; i < icons.Count; i++)
            {
                Rect region = icons[i].Region;
                string size = $"{Mathf.RoundToInt(region.width)}x{Mathf.RoundToInt(region.height)}";
                if (!sources.TryGetValue(size, out List<string>? named))
                {
                    sources[size] = named = new List<string>();
                }

                named.Add(icons[i].Name);
            }

            List<string> summary = new();
            foreach (KeyValuePair<string, List<string>> group in sources)
            {
                // The big groups are the norm and naming thirty icons helps nobody; the small
                // ones are the outliers worth seeing.
                summary.Add(group.Value.Count > 4
                    ? $"{group.Key} x{group.Value.Count}"
                    : $"{group.Key} ({string.Join(", ", group.Value)})");
            }

            Plugin.Log.LogInfo($"Status icons: {icons.Count} packed into a {atlas.width}x{atlas.height} atlas"
                + $". Untinted: {(ownColours.Count == 0 ? "none" : string.Join(", ", ownColours))}."
                + $" Colours: {EffectColors.SampleReport()}");
        }
        finally
        {
            foreach (Texture2D copy in copies)
            {
                if (copy != null)
                {
                    UnityEngine.Object.Destroy(copy);
                }
            }

            // The packed atlas is the only copy that needs to survive, so our own source
            // textures go too. Borrowed ones are not in this list and are left alone.
            foreach (Texture2D owned in OwnedSources)
            {
                if (owned != null)
                {
                    UnityEngine.Object.Destroy(owned);
                }
            }

            OwnedSources.Clear();
        }
    }

    /// <summary>
    /// Resizes the icons relative to the text. Glyph scale is read during text layout, so
    /// this takes effect on the next rebuild without repacking the atlas - cheap enough to
    /// drive from a config slider.
    /// </summary>
    private static void ApplyMetrics()
    {
        for (int i = 0; i < Glyphs.Count; i++)
        {
            float height = IconScale;
            float width = Aspects[i] * IconScale;

            // Vertical placement is the bearing: how far the glyph's top sits above the
            // baseline. Deriving it from the icon's own height keeps the icon centred on
            // the cap height at any size, which sizing alone does not - a fixed bearing
            // gets dragged toward the baseline as the glyph shrinks.
            Glyphs[i].metrics = new GlyphMetrics(width, height, 0f, CapCentre + (height * 0.5f), width);
        }
    }

    private static void Alias(string from, string to)
    {
        // The alias reuses the same tag, which still points at the original sprite name.
        if (Tags.TryGetValue(from, out string? tag))
        {
            Tags[to] = tag;
        }
    }

    /// <summary>
    /// Copies one icon through the GPU so its pixels can be read back, cropped to
    /// <paramref name="region"/> and shrunk to <paramref name="pixelHeight"/> pixels tall.
    /// Both ride along on the blit the readback already needed.
    /// </summary>
    private static Texture2D MakeReadable(Texture source, Rect region, int pixelHeight)
    {
        int width = Mathf.Max(1, Mathf.RoundToInt(region.width));
        int height = Mathf.Max(1, Mathf.RoundToInt(region.height));

        // Never upscale. An icon whose art is already smaller than the height it is drawn at
        // gains nothing from more texels and would only cost atlas space - the campfire and
        // shield markers are about 60 pixels tall and hit this at any large Font Size.
        float shrink = Mathf.Min(1f, pixelHeight / (float)height);
        int copyWidth = Mathf.Max(1, Mathf.RoundToInt(width * shrink));
        int copyHeight = Mathf.Max(1, Mathf.RoundToInt(height * shrink));

        RenderTexture previous = RenderTexture.active;
        RenderTexture step = Temporary(width, height);

        try
        {
            Graphics.Blit(source, step,
                new Vector2(region.width / source.width, region.height / source.height),
                new Vector2(region.x / source.width, region.y / source.height));

            // Halve repeatedly rather than dropping to the target in one blit. A bilinear
            // tap reads four texels, so a 4x reduction taken in a single step would miss
            // fifteen source pixels in every sixteen and alias the thin lines the status
            // silhouettes are mostly made of.
            while (step.width >= copyWidth * 2 && step.height >= copyHeight * 2)
            {
                step = Blit(step, step.width / 2, step.height / 2);
            }

            if (step.width != copyWidth || step.height != copyHeight)
            {
                step = Blit(step, copyWidth, copyHeight);
            }

            RenderTexture.active = step;

            Texture2D readable = new Texture2D(copyWidth, copyHeight, TextureFormat.RGBA32, mipChain: false);
            readable.ReadPixels(new Rect(0f, 0f, copyWidth, copyHeight), 0, 0);
            readable.Apply();
            return readable;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(step);
        }
    }

    private static RenderTexture Temporary(int width, int height) =>
        RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);

    /// <summary>
    /// Draws one temporary render texture into a smaller one and releases the original, so
    /// the caller only ever holds the newest step of the chain.
    /// </summary>
    private static RenderTexture Blit(RenderTexture source, int width, int height)
    {
        RenderTexture destination = Temporary(width, height);
        Graphics.Blit(source, destination);
        RenderTexture.ReleaseTemporary(source);
        return destination;
    }

    /// <summary>
    /// Destroys the generated atlas and sprite asset and clears the mapping. Needed for hot
    /// reloading, and after a failed build so a retry starts clean.
    /// </summary>
    internal static void Reset()
    {
        if (spriteAsset != null)
        {
            if (spriteAsset.material != null)
            {
                UnityEngine.Object.Destroy(spriteAsset.material);
            }

            UnityEngine.Object.Destroy(spriteAsset);
        }

        if (atlas != null)
        {
            UnityEngine.Object.Destroy(atlas);
        }

        spriteAsset = null;
        atlas = null;
        BuiltForSharpness = 0f;
        Tags.Clear();
        Glyphs.Clear();

        // Aspects is indexed in step with Glyphs; leaving it behind made the next build read
        // the previous one's shapes.
        Aspects.Clear();

        // The palette is read on the same walk that builds the icons, so it is only ever as
        // fresh as this build. Dropping it here keeps a hot reload or a rebuilt HUD from
        // carrying colours forward from a scene that no longer exists.
        EffectColors.ClearSamples();

        // Which statuses can be listed depends on which have icons, so that answer is only as
        // good as this build too.
        EffectFormatter.ForgetClearable();
    }
}
