using TMPro;
using UnityEngine;

namespace VeeItemInfo;

/// <summary>
/// Owns the TextMeshPro object the description is drawn into.
///
/// The overlay is parented straight to the HUD canvas and positioned against its
/// bottom-right corner. An earlier version parented into ItemPromptLayout, but that is a
/// layout group: it owned the position, so the only way to move the text was to stretch
/// the box and let the group push it around. Anchoring directly makes the offsets mean
/// what they say.
/// </summary>
internal static class Overlay
{
    private static GUIManager? guiManager;
    private static TextMeshProUGUI? textMesh;
    private static RectTransform? rect;

    /// <summary>
    /// Builds the overlay if it doesn't exist yet, and reports whether it's usable.
    /// Normally the GUIManager.Start hook gets here first; this covers the case where
    /// the HUD is torn down and rebuilt (returning to the menu and starting a new run),
    /// which leaves the old references Unity-null.
    /// </summary>
    internal static bool EnsureCreated()
    {
        if (textMesh == null || guiManager == null)
        {
            Create();
        }

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

        Transform? hud = guiManagerGameObj.transform.Find("Canvas_HUD");
        if (hud == null)
        {
            Plugin.Log.LogWarning("Could not find Canvas_HUD - overlay not created.");
            return;
        }

        GameObject overlayGameObj = new GameObject("VeeItemInfo");
        overlayGameObj.transform.SetParent(hud, worldPositionStays: false);
        textMesh = overlayGameObj.AddComponent<TextMeshProUGUI>();
        rect = overlayGameObj.GetComponent<RectTransform>();

        textMesh.font = guiManager.heroDayText.font;
        textMesh.text = "";
        textMesh.raycastTarget = false;

        ApplyStyle();
    }

    /// <summary>
    /// Pushes the current config values onto the overlay. Safe to call at any time,
    /// including before the overlay exists.
    /// </summary>
    internal static void ApplyStyle()
    {
        if (textMesh == null || rect == null)
        {
            return;
        }

        // Pin all three to the bottom-right corner so the offsets are measured from
        // there and stay put across resolutions and aspect ratios.
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        // Height 0 lets TMP grow the box upward to fit however many lines there are.
        rect.sizeDelta = new Vector2(PluginConfig.Width.Value, 0f);
        rect.anchoredPosition = new Vector2(PluginConfig.OffsetX.Value, PluginConfig.OffsetY.Value);

        textMesh.fontSize = PluginConfig.FontSize.Value;
        textMesh.lineSpacing = PluginConfig.LineSpacing.Value;
        textMesh.outlineWidth = PluginConfig.OutlineWidth.Value;
        textMesh.alignment = PluginConfig.RightAlign.Value
            ? TextAlignmentOptions.BottomRight
            : TextAlignmentOptions.BottomLeft;
    }

    internal static void SetText(string text)
    {
        if (textMesh != null)
        {
            textMesh.text = text;
        }
    }

    internal static void SetVisible(bool visible)
    {
        if (textMesh != null && textMesh.gameObject.activeSelf != visible)
        {
            textMesh.gameObject.SetActive(visible);
        }
    }
}
