using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;

namespace VeeItemInfo;

/// <summary>
/// The game's own status icons, scraped off the HUD and packed into one TMP sprite atlas.
/// Everything degrades to plain text if it fails. See docs/internals_infra.md, "Icons".
/// </summary>
internal static class StatusIcons
{
    private static readonly Dictionary<string, string> Tags = new();
    private static readonly List<TMP_SpriteGlyph> Glyphs = new();
    private static readonly List<float> Aspects = new();
    private static TMP_SpriteAsset? spriteAsset;
    private static Texture2D? atlas;

    /// <summary>Textures this class created; scraped ones belong to the game and are never destroyed.</summary>
    private static readonly List<Texture2D> OwnedSources = new();

    // Building is a scene scan, so retries are spaced and capped.
    private const int MaxAttempts = 15;
    private const float RetryInterval = 2f;

    /// <summary>Middle of an upper-case glyph, in em above the baseline; dialled in against the HUD font.</summary>
    private const float CapCentre = 0.31f;

    /// <summary>Icon height as a fraction of the font size, so Font Size stays the one knob.</summary>
    private const float IconScale = 0.85f;

    /// <summary>Copied height in pixels; height, not the long side, since the sources are far from square.</summary>
    private const int IconPixelHeight = 128;

    /// <summary>Sharpness is baked into the atlas, so a change has to repack.</summary>
    internal static float BuiltForSharpness { get; private set; }

    internal static bool MatchesSettings =>
        Mathf.Approximately(BuiltForSharpness, PluginConfig.IconSharpness.Value);

    private static int attempts;
    private static float nextAttempt;

    internal static bool Available => Tags.Count > 0;

    /// <summary>A scene load can destroy the Unity objects while the mapping still looks populated.</summary>
    internal static bool IsValid => Available && spriteAsset != null && atlas != null;

    internal static void Invalidate()
    {
        Reset();
        attempts = 0;
        nextAttempt = 0f;
    }

    internal static TMP_SpriteAsset? SpriteAsset => spriteAsset;

    /// <summary>The sprite tag, or the upper-case name when no icon could be found.</summary>
    internal static string Tag(string status) =>
        Tags.TryGetValue(status, out string? tag) ? tag : status.ToUpper();

    /// <summary>Anything listing statuses needs this: the <see cref="Tag"/> fallback is English.</summary>
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

            // The petrify bar reports its afflictionType as Injury.
            string name = bar.isPetrify ? "Petrify" : bar.afflictionType.ToString();

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

        // The colour key keeps its space (it is what EffectColors is asked for); the sprite
        // key cannot. Icon first, bar second, so the bar's reading wins.
        SampleIndicatorColour(staminaBar?.extraStaminaIcon, "Extra Stamina");
        SampleIndicatorColour(staminaBar?.extraBarStamina, "Extra Stamina");

        // The invincibility marker, and the campfire shown while you cannot get hungry.
        AddBarIndicator(icons, seen, staminaBar?.shield, "Shield");
        AddBarIndicator(icons, seen, staminaBar?.campfire, "Cook");

        // A stand-in for "some item".
        AddItemIcon(icons, seen, "Item", FirstItemWith(item => item.itemTags.HasFlag(Item.ItemTags.BingBong)));

        AddItemIcon(icons, seen, "RopeCannon", FirstItemWith(item =>
            item.GetComponent<RopeShooter>() != null && item.GetComponent<Antigrav>() == null));
        AddItemIcon(icons, seen, "RopeCannonAnti", FirstItemWith(item =>
            item.GetComponent<RopeShooter>() != null && item.GetComponent<Antigrav>() != null));
        AddItemIcon(icons, seen, "RopeSpool", FirstItemWith(item =>
            item.GetComponent<RopeSpool>() is RopeSpool spool && !spool.isAntiRope));
        AddItemIcon(icons, seen, "RopeSpoolAnti", FirstItemWith(item =>
            item.GetComponent<RopeSpool>() is RopeSpool spool && spool.isAntiRope));

        AddItemIcon(icons, seen, "Float", FirstItemWith(item =>
            item.GetComponent<Balloon>() is Balloon balloon && !balloon.isBunch));
        AddItemIcon(icons, seen, "FloatBunch", FirstItemWith(item =>
            item.GetComponent<Balloon>() is Balloon balloon && balloon.isBunch));

        // Last, so a status keeps its key if an item ever shares the name.
        AddTransformationIcons(icons, seen);

        // The one shipped icon: numbness has nothing in the scene to scrape. See assets/NOTICE.md.
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
    /// The fill is the colour two of the bar's Images share; not the brightest, since Curse's
    /// backing outshines its fill. The bar's own icon would pair with anything and is skipped.
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

    /// <summary>Equality with room for the float round trip.</summary>
    private static bool Agree(Color a, Color b)
    {
        const float Tolerance = 1f / 255f;
        return Mathf.Abs(a.r - b.r) <= Tolerance
            && Mathf.Abs(a.g - b.g) <= Tolerance
            && Mathf.Abs(a.b - b.b) <= Tolerance;
    }

    /// <summary>For the markers with no BarAffliction, and so no agreeing pair.</summary>
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

    /// <summary>The indicator objects are usually inactive, hence includeInactive.</summary>
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

        // A white silhouette tinted by its Image, so the tint is the HUD's colour for it.
        EffectColors.Sample(name, image!.color);

        if (seen.Add(name))
        {
            icons.Add(IconSource.FromSprite(name, sprite));
        }
    }

    /// <summary>A sub-rect of a texture (a Sprite) or a whole texture (an item icon).</summary>
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

    /// <summary>HideAndDontSave, so a scene load cannot unload it out from under the atlas.</summary>
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

    /// <summary>The icon of every item that another item turns into, asked of the prefabs.</summary>
    private static void AddTransformationIcons(List<IconSource> icons, HashSet<string> seen)
    {
        foreach (Item item in AllItems())
        {
            // A prefab is never active, so includeInactive is mandatory.
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

    /// <summary>A sprite name cannot contain a space.</summary>
    private static string KeyFor(Item item) => item.name.Replace(" ", "");

    /// <summary>Falls back to the generic item glyph, never to the item's name, which is English.</summary>
    internal static string ItemTag(Item item) =>
        Tags.TryGetValue(KeyFor(item), out string? tag) ? tag : Tag("Item");

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

    /// <summary>Under a key of our own, for an item icon that stands for something else.</summary>
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

    /// <summary>Every item in the database that carries UI data.</summary>
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
    /// Steepens the alpha ramp around its midpoint. The game's icons are soft because the
    /// artwork is soft, so this, not resolution, is what sharpens them. Runs after the
    /// downscale, which softens the edge again.
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
    /// Tint a white silhouette; leave artwork with its own colours alone, since tinting
    /// darkens it. Decided from the texture rather than a list of names.
    /// </summary>
    private static bool ShouldTint(Texture2D texture)
    {
        const float SaturationThreshold = 0.2f;
        const float ColouredPixelShare = 0.15f;

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

        // Source textures are not CPU-readable, so each is blitted into a readable copy.
        Texture2D[] copies = new Texture2D[icons.Count];
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
            // TMP treats a version-less asset as legacy and walks spriteInfoList, which is
            // null on a runtime instance.
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

                // The size comes from the copy, not the UV rect: rounding UVs back to texels
                // can land a pixel out, and a glyph rect one pixel wrong rescales the sprite.
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
                Aspects.Add((float)w / h);
                Tags[name] = $"<sprite name=\"{name}\" tint={(tintable[i] ? 1 : 0)}>";
            }

            asset.UpdateLookupTables();
            spriteAsset = asset;

            ApplyMetrics();
            BuiltForSharpness = PluginConfig.IconSharpness.Value;

            // Runtime-generated assets belong to no scene; a scene load would unload them.
            atlas.hideFlags = HideFlags.HideAndDontSave;
            asset.hideFlags = HideFlags.HideAndDontSave;
            asset.material.hideFlags = HideFlags.HideAndDontSave;

            Alias("Hot", "Heat");
            Alias("Drowsy", "Sleepy");
            Alias("ExtraStamina", "Extra Stamina");

            // An icon rendering muddy is almost always the tint decision going the wrong way.
            List<string> ownColours = new();
            for (int i = 0; i < icons.Count; i++)
            {
                if (!tintable[i])
                {
                    ownColours.Add(icons[i].Name);
                }
            }

            // Source sizes before the copy shrank them.
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

    private static void ApplyMetrics()
    {
        for (int i = 0; i < Glyphs.Count; i++)
        {
            float height = IconScale;
            float width = Aspects[i] * IconScale;

            // The bearing follows the height so the icon stays centred on the cap height at
            // any size; a fixed bearing drifts toward the baseline as the glyph shrinks.
            Glyphs[i].metrics = new GlyphMetrics(width, height, 0f, CapCentre + (height * 0.5f), width);
        }
    }

    private static void Alias(string from, string to)
    {
        if (Tags.TryGetValue(from, out string? tag))
        {
            Tags[to] = tag;
        }
    }

    /// <summary>A readable copy, cropped to <paramref name="region"/> and shrunk to <paramref name="pixelHeight"/>.</summary>
    private static Texture2D MakeReadable(Texture source, Rect region, int pixelHeight)
    {
        int width = Mathf.Max(1, Mathf.RoundToInt(region.width));
        int height = Mathf.Max(1, Mathf.RoundToInt(region.height));

        // Never upscale.
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

            // Halve repeatedly: a bilinear tap reads four texels, so a larger single-step
            // reduction skips source pixels and aliases thin lines.
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

    /// <summary>Releases the source, so the caller only ever holds the newest step.</summary>
    private static RenderTexture Blit(RenderTexture source, int width, int height)
    {
        RenderTexture destination = Temporary(width, height);
        Graphics.Blit(source, destination);
        RenderTexture.ReleaseTemporary(source);
        return destination;
    }

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

        // Indexed in step with Glyphs.
        Aspects.Clear();

        // Both are only as fresh as this build.
        EffectColors.ClearSamples();
        EffectFormatter.ForgetClearable();
    }
}
