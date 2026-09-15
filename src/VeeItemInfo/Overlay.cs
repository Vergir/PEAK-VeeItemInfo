using TMPro;
using UnityEngine;

namespace VeeItemInfo;

/// <summary>
/// Owns the TextMeshPro object the description is drawn into, and keeps it above the
/// inventory slot holding the item. See docs/internals_infra.md, "Overlay placement".
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
    /// Covers the HUD being torn down and rebuilt (a new run), which leaves the references
    /// Unity-null after the GUIManager.Start hook has already fired.
    /// </summary>
    internal static bool EnsureCreated()
    {
        if (textMesh != null && guiManager != null)
        {
            return true;
        }

        // Create() is a scene lookup, so a failed attempt must not repeat every frame.
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
        guiManager = GUIManager.instance;
        if (guiManager == null)
        {
            return;
        }

        hudRect = guiManager.hudCanvas == null
            ? null
            : guiManager.hudCanvas.transform as RectTransform;
        if (hudRect == null)
        {
            Plugin.Log.LogWarning("GUIManager has no HUD canvas - overlay not created.");
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

    /// <summary>Safe to call before the overlay exists, so config changes can be applied live.</summary>
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

    /// <summary>Centres the overlay above the slot holding <paramref name="item"/>; runs every frame.</summary>
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
    /// The top-centre of the slot showing the tracked item, in HUD-local space. The temporary
    /// slot (a pickup with a full inventory) is checked first.
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

    private static RectTransform? MatchSlot(InventoryItemUI? slot, ItemInstanceData data)
    {
        if (slot == null || !slot.gameObject.activeInHierarchy || slot.rectTransform == null)
        {
            return null;
        }

        return ReferenceEquals(slot._itemData, data) ? slot.rectTransform : null;
    }

    /// <summary>Attaches the sprite asset once the status bar exists, which may be after the HUD.</summary>
    internal static void EnsureIcons()
    {
        if (textMesh == null)
        {
            return;
        }

        // A HUD rebuild can destroy the atlas while the mapping still looks populated.
        if (StatusIcons.Available && !StatusIcons.IsValid)
        {
            StatusIcons.Invalidate();
        }

        // Sharpness is baked into the atlas, so moving that slider has to repack it.
        if (StatusIcons.Available && !StatusIcons.MatchesSettings)
        {
            StatusIcons.Invalidate();
        }

        StatusIcons.EnsureBuilt();

        // A HUD rebuild also gives a fresh text mesh with no sprite asset assigned.
        if (StatusIcons.SpriteAsset != null && textMesh.spriteAsset != StatusIcons.SpriteAsset)
        {
            textMesh.spriteAsset = StatusIcons.SpriteAsset;

            // Rebuild the text, not just re-point it: a description assembled before the atlas
            // existed holds the word HUNGER, not a sprite tag.
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

    /// <summary>Expensive; only when the text or the styling changes, never per frame.</summary>
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

    /// <summary>For hot reloading: the GameObject sits on the game's canvas and would outlive the assembly.</summary>
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
}
