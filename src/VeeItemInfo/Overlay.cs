using TMPro;
using UnityEngine;

namespace VeeItemInfo;

/// <summary>
/// Owns the TextMeshPro object the description is drawn into, and keeps it sitting above
/// whichever inventory slot holds the item being described.
///
/// Placement went through a few wrong turns worth recording, so they don't get retried:
///
///   - Parenting into ItemPromptLayout makes the game's layout group own our position,
///     leaving box width as the only way to move the text sideways.
///   - Setting LayoutElement.ignoreLayout to escape that stops the overlay rendering.
///   - Offsets from a screen corner drift, because the HUD reflows with aspect ratio.
///   - ItemPromptLayout is a full-height container, so its corners sit at the screen edges
///     rather than around the prompts you can actually see.
///
/// What works: parent to Canvas_HUD, which neither positions nor clips us, and each frame
/// measure the slot we want to sit above. Rendering is safe, tracking is exact.
/// </summary>
internal static class Overlay
{
    private static GUIManager? guiManager;
    private static TextMeshProUGUI? textMesh;
    private static RectTransform? rect;
    private static RectTransform? hudRect;

    private static Item? trackedItem;
    private static string lastText = "";
    private static float cachedHeight;
    private static float nextCreateAttempt;

    private static readonly Vector3[] CornerBuffer = new Vector3[4];

    /// <summary>
    /// Builds the overlay if it doesn't exist yet, and reports whether it's usable.
    /// Normally the GUIManager.Start hook gets here first; this covers the case where
    /// the HUD is torn down and rebuilt (returning to the menu and starting a new run),
    /// which leaves the old references Unity-null.
    /// </summary>
    internal static bool EnsureCreated()
    {
        if (textMesh != null && guiManager != null)
        {
            return true;
        }

        // Create() searches the scene by name, so a failed attempt must not repeat on the
        // next frame - that turns a missing HUD into a per-frame scene scan.
        if (Time.unscaledTime < nextCreateAttempt)
        {
            return false;
        }

        nextCreateAttempt = Time.unscaledTime + 1f;
        Create();
        return textMesh != null;
    }

    internal static void Create()
    {
        GameObject? guiManagerGameObj = GameObject.Find("GAME/GUIManager");
        if (guiManagerGameObj == null)
        {
            return;
        }

        guiManager = guiManagerGameObj.GetComponent<GUIManager>();
        if (guiManager == null)
        {
            return;
        }

        hudRect = guiManagerGameObj.transform.Find("Canvas_HUD") as RectTransform;
        if (hudRect == null)
        {
            Plugin.Log.LogWarning("Could not find Canvas_HUD - overlay not created.");
            return;
        }

        GameObject overlayGameObj = new GameObject("VeeItemInfo");
        overlayGameObj.transform.SetParent(hudRect, worldPositionStays: false);
        textMesh = overlayGameObj.AddComponent<TextMeshProUGUI>();
        rect = overlayGameObj.GetComponent<RectTransform>();

        textMesh.font = guiManager.heroDayText.font;
        textMesh.text = "";
        // Never intercept clicks - the HUD sits over the game world.
        textMesh.raycastTarget = false;
        // Let long descriptions spill past the box rather than being clipped away.
        textMesh.overflowMode = TextOverflowModes.Overflow;

        ApplyStyle();
    }

    /// <summary>
    /// Pushes the current config values onto the overlay. Safe to call at any time,
    /// including before the overlay exists, so config changes can be applied live.
    /// </summary>
    internal static void ApplyStyle()
    {
        if (textMesh == null || rect == null)
        {
            return;
        }

        // Anchor to the HUD's bottom-left so anchoredPosition is a plain coordinate in
        // HUD space, whatever pivot the canvas itself happens to use. Our own pivot is the
        // bottom-centre of the text, so it sits centred over a slot and grows upward.
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0f);

        // Never leave this at TMP's default of pure white - see EffectColors.Base.
        textMesh.color = EffectColors.Base;
        textMesh.fontSize = PluginConfig.FontSize.Value;
        textMesh.lineSpacing = PluginConfig.LineSpacing.Value;
        textMesh.outlineWidth = PluginConfig.OutlineWidth.Value;
        textMesh.alignment = TextAlignmentOptions.Bottom;

        Remeasure();
        UpdatePosition();
    }

    /// <summary>
    /// Centres the overlay above the slot holding <paramref name="item"/>. Cheap enough to
    /// run every frame, which keeps it correct as the selected slot changes and through
    /// resolution and aspect ratio changes.
    /// </summary>
    internal static void UpdatePosition(Item? item = null)
    {
        if (item != null)
        {
            trackedItem = item;
        }

        if (rect == null || textMesh == null || hudRect == null)
        {
            return;
        }

        Rect hud = hudRect.rect;
        Vector2 target = TryGetSlotTopCentre(out Vector2 slotTop)
            ? slotTop
            : new Vector2(hud.center.x, hud.yMin);

        // Re-base onto the bottom-left anchor set in ApplyStyle, then apply the offsets.
        target -= hud.min;
        target += new Vector2(PluginConfig.OffsetX.Value, PluginConfig.OffsetY.Value);

        // Keep the block on screen no matter what the measurement produced. Our pivot is
        // the bottom-centre, so the text spans x +/- half its width, and y upward.
        float halfWidth = Mathf.Min(rect.sizeDelta.x * 0.5f, hud.width * 0.5f);
        target.x = Mathf.Clamp(target.x, halfWidth, hud.width - halfWidth);
        target.y = Mathf.Clamp(target.y, 0f, Mathf.Max(0f, hud.height - cachedHeight));

        rect.anchoredPosition = target;
    }

    /// <summary>
    /// Finds the top-centre of the inventory slot showing the tracked item, in HUD-local
    /// space. The temporary slot is checked first: when the inventory is full and you pick
    /// something up, it appears there, to the left of the numbered slots.
    /// </summary>
    private static bool TryGetSlotTopCentre(out Vector2 topCentre)
    {
        topCentre = default;
        if (guiManager == null || hudRect == null || trackedItem == null)
        {
            return false;
        }

        ItemInstanceData data = trackedItem.data;
        if (data == null)
        {
            return false;
        }

        RectTransform? slot = MatchSlot(guiManager.temporaryItem, data);
        if (slot == null && guiManager.items != null)
        {
            for (int i = 0; i < guiManager.items.Length && slot == null; i++)
            {
                slot = MatchSlot(guiManager.items[i], data);
            }
        }

        if (slot == null)
        {
            return false;
        }

        // Corners are 0 = bottom-left, 1 = top-left, 2 = top-right, 3 = bottom-right.
        slot.GetWorldCorners(CornerBuffer);
        Vector2 topLeft = hudRect.InverseTransformPoint(CornerBuffer[1]);
        Vector2 topRight = hudRect.InverseTransformPoint(CornerBuffer[2]);
        topCentre = new Vector2((topLeft.x + topRight.x) * 0.5f, Mathf.Max(topLeft.y, topRight.y));
        return true;
    }

    /// <summary>Returns the slot's rect if it is on screen and holding this exact item.</summary>
    private static RectTransform? MatchSlot(InventoryItemUI? slot, ItemInstanceData data)
    {
        if (slot == null || !slot.gameObject.activeInHierarchy || slot.rectTransform == null)
        {
            return null;
        }

        return ReferenceEquals(slot._itemData, data) ? slot.rectTransform : null;
    }

    /// <summary>
    /// Attaches the status icon sprite asset once the status bar exists. The bar is not
    /// necessarily built when the HUD is, so this keeps trying until it succeeds.
    /// </summary>
    internal static void EnsureIcons()
    {
        if (textMesh == null)
        {
            return;
        }

        // Dying or starting a new run rebuilds the HUD, which gives us a fresh text mesh
        // and can destroy the generated atlas. Both cases have to be caught: the mapping
        // can still look populated while its Unity objects are gone, and a new text mesh
        // has no sprite asset assigned even when the old one is perfectly alive. Either
        // way TMP falls back to its own sprite set and every icon renders as a "?".
        if (StatusIcons.Available && !StatusIcons.IsValid)
        {
            StatusIcons.Invalidate();
        }

        // Icons are packed at the size they are drawn, and that size follows Font Size and
        // the canvas scale - so a settings change or a resolution change makes the current
        // atlas the wrong resolution rather than merely stale. Comparing the target against
        // what was built catches both without either needing an event of its own; the target
        // is quantised to a power of two, so it holds still across a slider drag instead of
        // repacking on every frame of it.
        if (StatusIcons.Available && !StatusIcons.MatchesSettings)
        {
            StatusIcons.Invalidate();
        }

        StatusIcons.EnsureBuilt();

        if (StatusIcons.SpriteAsset != null && textMesh.spriteAsset != StatusIcons.SpriteAsset)
        {
            textMesh.spriteAsset = StatusIcons.SpriteAsset;

            // The text has to be built again, not just re-pointed at the new asset.
            // StatusIcons.Tag falls back to the status name in capitals when the atlas is not
            // ready, so a description assembled before this point contains the literal word
            // HUNGER rather than a sprite tag - and assigning a sprite asset cannot go back
            // and change a string that has already been built. Until this existed, the only
            // thing that repaired it was the periodic re-check happening to come round.
            ItemInfoController.MarkDirty();
            // Icons change the line metrics, so the cached height is now stale.
            lastText = "";
            ItemInfoController.MarkDirty();
        }
    }

    internal static void SetText(string text)
    {
        if (textMesh == null || text == lastText)
        {
            return;
        }

        lastText = text;
        textMesh.text = text;
        Remeasure();
    }

    /// <summary>
    /// Recomputes the box height from the current text. Rebuilding the mesh is expensive,
    /// so this runs only when the text or the styling actually changes - never per frame.
    /// The measured height is what makes Offset Y a true bottom edge.
    /// </summary>
    private static void Remeasure()
    {
        if (textMesh == null || rect == null)
        {
            return;
        }

        textMesh.ForceMeshUpdate();
        cachedHeight = textMesh.preferredHeight;
        rect.sizeDelta = new Vector2(PluginConfig.Width.Value, cachedHeight);
    }

    internal static void SetVisible(bool visible)
    {
        if (textMesh != null && textMesh.gameObject.activeSelf != visible)
        {
            textMesh.gameObject.SetActive(visible);
        }
    }

    /// <summary>
    /// Tears the overlay out of the HUD and forgets everything. Needed for hot reloading:
    /// the GameObject is parented to the game's own canvas, so it outlives our assembly
    /// unless we remove it ourselves.
    /// </summary>
    internal static void Destroy()
    {
        if (textMesh != null)
        {
            UnityEngine.Object.Destroy(textMesh.gameObject);
        }

        textMesh = null;
        rect = null;
        hudRect = null;
        guiManager = null;
        trackedItem = null;
        lastText = "";
        cachedHeight = 0f;
        nextCreateAttempt = 0f;
    }

    /// <summary>Dumps the numbers behind the current placement, for diagnosing position bugs.</summary>
    private static string lastLogged = "";

    /// <summary>
    /// Where the overlay ended up, logged when it moves rather than every refresh.
    ///
    /// The slot it follows drifts by fractions as the HUD animates, so the figures are
    /// rounded before they are compared - otherwise "has this changed" is true almost every
    /// time and the throttle does nothing.
    /// </summary>
    internal static void LogDiagnostics()
    {
        if (rect == null || hudRect == null || textMesh == null)
        {
            Log("[pos] overlay not created yet");
            return;
        }

        string slot = TryGetSlotTopCentre(out Vector2 top)
            ? $"({Mathf.Round(top.x)}, {Mathf.Round(top.y)})"
            : "<no matching slot>";

        Log($"[pos] hud={hudRect.rect} slotTopCentre={slot} "
            + $"anchoredPos=({Mathf.Round(rect.anchoredPosition.x)}, {Mathf.Round(rect.anchoredPosition.y)}) "
            + $"textHeight={textMesh.preferredHeight:F0} "
            + $"visible={textMesh.gameObject.activeSelf} chars={textMesh.text.Length}");
    }

    private static void Log(string state)
    {
        if (state != lastLogged)
        {
            lastLogged = state;
            Plugin.Log.LogInfo(state);
        }
    }
}
