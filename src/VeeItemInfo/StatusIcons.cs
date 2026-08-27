using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;

namespace VeeItemInfo;

/// <summary>
/// Makes the game's own status icons usable inside our TextMeshPro text.
///
/// PEAK draws the icons above the stamina bar with BarAffliction, which pairs an Image
/// with the STATUSTYPE it represents. We scrape those and assemble a TMP_SpriteAsset, so
/// descriptions can say "+30 &lt;flame&gt;" instead of "GAIN 30 HOT". Borrowing the game's
/// art means the icons always match the game's visual language, and there is nothing to
/// ship or attribute.
///
/// Each icon lives on its own 512x512 texture, and a sprite asset draws from exactly one
/// texture. Chaining one asset per icon as fallbacks does not resolve reliably - TMP just
/// renders its missing-sprite placeholder - so everything is packed into a single atlas
/// first and the whole set becomes one asset.
///
/// Everything degrades to plain text: if any of this fails, <see cref="Tag"/> returns the
/// status name and the overlay stays readable.
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
    /// The longest side an icon is copied at, in pixels.
    ///
    /// Derived rather than picked. An icon draws at <see cref="IconScale"/> x Font Size, and
    /// Font Size is capped at 72 by its config range, so 61 units is the largest the overlay
    /// can ever ask for. Round that up to 64 and double it for a HUD canvas scaled up on a
    /// high-DPI display.
    ///
    /// The game's own icons are 512x512, which is thirty times what a default 20pt line
    /// needs, and packing them at that size cost a 4096x4096 atlas - 64 MB of texture for
    /// twenty-five glyphs. Nothing about that was visible on screen.
    /// </summary>
    private const int MaxIconPixels = 128;

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

        // Two more indicators hang off the stamina bar as plain GameObjects rather than
        // BarAfflictions, so they need fetching by hand. 'shield' is the invincibility
        // marker; 'campfire' is what the game shows while you cannot get hungry, and its
        // artwork is the campfire we want for the cooking hint.
        AddBarIndicator(icons, seen, staminaBar?.shield, "Shield");
        AddBarIndicator(icons, seen, staminaBar?.campfire, "Cook");

        // A stand-in for "some item", used where a description needs to talk about an item
        // without naming one. BingBong is the game's own mascot and reads as generic.
        Texture2D? genericItem = FindItemIcon("BingBong");
        if (genericItem != null && seen.Add("Item"))
        {
            icons.Add(IconSource.FromTexture("Item", genericItem));
        }

        // The Rope Cannon describes two distances that would otherwise be a pair of bare
        // numbers: how far it shoots, and how much rope that leaves behind. Its own icon and
        // the spool's tell them apart without a word, and the anti-rope pair gets its own set
        // so a floating rope never advertises itself with an ordinary one.
        //
        // Exact names, not substrings: "RopeShooter" is a prefix of "RopeShooterAnti", so a
        // contains-match would hand whichever the database iterated first to both.
        AddItemIcon(icons, seen, "RopeCannon", "RopeShooter");
        AddItemIcon(icons, seen, "RopeCannonAnti", "RopeShooterAnti");
        AddItemIcon(icons, seen, "RopeSpool", "RopeSpool");
        AddItemIcon(icons, seen, "RopeSpoolAnti", "Anti-Rope Spool");

        // "You float." Scout's Initiative drops your gravity rather than granting speed, and
        // the balloon bunch is the game's own picture of that - no status icon exists for it.
        Texture2D? floaty = FindItemIcon("BalloonBunch") ?? FindItemIcon("Balloon");
        if (floaty != null && seen.Add("Float"))
        {
            icons.Add(IconSource.FromTexture("Float", floaty));
        }

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
    /// Packs the icon of every item that another item turns into.
    ///
    /// Two fields in 2.1.a name a different item: <c>Action_ConsumeAndSpawn.itemToSpawn</c>,
    /// which is how each Berrynana hands you its own coloured peel, and
    /// <c>CookingBehavior_ReplaceItem.replaceWithItem</c>, which is how a cooked Frog becomes
    /// FrogLegs. A line saying "this becomes that" has to show <i>that</i>, and which item it
    /// is comes off the field rather than from anything we choose here.
    ///
    /// So the set is collected by asking the prefabs, not by listing the five names 2.1.a
    /// happens to have. Naming them would go stale silently, and it would also be a
    /// judgement about which transformations matter that these two fields already make.
    /// </summary>
    private static void AddTransformationIcons(List<IconSource> icons, HashSet<string> seen)
    {
        foreach (Item item in AllItems())
        {
            // A prefab lives in the asset database rather than a scene, so nothing on one is
            // active in any hierarchy. includeInactive is mandatory, not a precaution - the
            // no-argument overload returns nothing at all.
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
    /// A tag for an item's own icon, falling back to the generic item glyph rather than to
    /// the item's name.
    ///
    /// <see cref="Tag"/>'s fallback is right for a status, whose name is a word a player
    /// reads on the HUD. An item's is a prefab name - "Berrynana Peel Pink Variant" - which
    /// is both English and internal. The generic glyph already means "some item", which is
    /// the honest thing to say when the real one could not be packed.
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
        string key, string itemName)
    {
        Texture2D? icon = FindItemIconExact(itemName);
        if (icon != null && seen.Add(key))
        {
            icons.Add(IconSource.FromTexture(key, icon));
        }
    }

    /// <summary>
    /// An item's icon by its exact database name. <see cref="FindItemIcon"/> matches on a
    /// substring, which is fine for a one-off like BingBong and wrong wherever one item's
    /// name is a prefix of another's.
    /// </summary>
    private static Texture2D? FindItemIconExact(string itemName)
    {
        foreach (Item entry in AllItems())
        {
            if (entry.name.Equals(itemName, StringComparison.OrdinalIgnoreCase))
            {
                return entry.UIData.GetIcon();
            }
        }

        return null;
    }

    private static Texture2D? FindItemIcon(string nameContains)
    {
        foreach (Item entry in AllItems())
        {
            if (entry.name.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return entry.UIData.GetIcon();
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
    private static IEnumerable<Item> AllItems()
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
    /// Whether an icon should take the colour of the text beside it.
    ///
    /// TMP's <c>tint=1</c> multiplies the sprite by the surrounding text colour. That is
    /// exactly right for the game's status icons, which are white silhouettes coloured by
    /// their UI Image at runtime - multiplying white by the status colour reproduces what the
    /// HUD shows. It is exactly wrong for art that already carries its own colours: the
    /// numbness mushrooms, and the item icons pulled from ItemDatabase. Multiplying those by
    /// anything darkens them, which is how tinting a mushroom by its own pale stem colour
    /// turned the whole icon muddy.
    ///
    /// Decided by looking at the texture rather than by keeping a list of names, because a
    /// list would silently rot the first time the game recolours an icon.
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
                copies[i] = MakeReadable(icons[i].Texture, icons[i].Region);
                tintable[i] = ShouldTint(copies[i]);
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

                // The packed cell is the glyph. MakeReadable cropped each copy to its own
                // sprite rect, so there is no sub-rect left to map, and PackTextures shrinking
                // a texture to make it fit no longer needs accounting for either.
                int x = Mathf.RoundToInt(uv.x * atlas.width);
                int y = Mathf.RoundToInt(uv.y * atlas.height);
                int w = Mathf.Max(1, Mathf.RoundToInt(uv.width * atlas.width));
                int h = Mathf.Max(1, Mathf.RoundToInt(uv.height * atlas.height));

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

            Plugin.Log.LogInfo($"Status icons ready: {icons.Count} packed into a {atlas.width}x{atlas.height} atlas. "
                + $"Drawn in their own colours (untinted): {(ownColours.Count == 0 ? "none" : string.Join(", ", ownColours))}.");
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
    /// <paramref name="region"/> and shrunk to <see cref="MaxIconPixels"/> on its long side.
    ///
    /// Both of those ride along on the blit the readback already needed, so neither costs a
    /// pass. The crop is what lets <see cref="BuildAtlas"/> treat a packed cell as the glyph
    /// rect outright: the copy is the icon and nothing else, so there is no sub-rect left to
    /// map through the packer's own scaling.
    /// </summary>
    private static Texture2D MakeReadable(Texture source, Rect region)
    {
        int width = Mathf.Max(1, Mathf.RoundToInt(region.width));
        int height = Mathf.Max(1, Mathf.RoundToInt(region.height));

        float shrink = Mathf.Min(1f, MaxIconPixels / (float)Mathf.Max(width, height));
        int targetWidth = Mathf.Max(1, Mathf.RoundToInt(width * shrink));
        int targetHeight = Mathf.Max(1, Mathf.RoundToInt(height * shrink));

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
            while (step.width >= targetWidth * 2 && step.height >= targetHeight * 2)
            {
                step = Blit(step, step.width / 2, step.height / 2);
            }

            if (step.width != targetWidth || step.height != targetHeight)
            {
                step = Blit(step, targetWidth, targetHeight);
            }

            RenderTexture.active = step;

            Texture2D readable = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, mipChain: false);
            readable.ReadPixels(new Rect(0f, 0f, targetWidth, targetHeight), 0, 0);
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
        Tags.Clear();
        Glyphs.Clear();
    }

    private static string lastLogged = "";

    /// <summary>
    /// The icon state, logged when it changes rather than every time it is asked for.
    ///
    /// This is a snapshot of something that settles once and then holds, and it was being
    /// written every refresh - a line a second saying the same thing, alongside three others
    /// doing the same. Four state dumps at one hertz drowned the lines that report an actual
    /// event, which is the whole reason to keep a log.
    /// </summary>
    internal static void LogDiagnostics()
    {
        string state = $"[icons] mapped={Tags.Count} "
            + $"atlas={(atlas == null ? "none" : $"{atlas.width}x{atlas.height}")} "
            + $"glyphs={spriteAsset?.spriteCharacterTable?.Count ?? -1} attempts={attempts}";

        if (state != lastLogged)
        {
            lastLogged = state;
            Plugin.Log.LogInfo(state);
        }
    }
}
