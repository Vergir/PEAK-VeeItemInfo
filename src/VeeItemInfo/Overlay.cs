using TMPro;
using UnityEngine;

namespace VeeItemInfo;

/// <summary>
/// Owns the TextMeshPro object bolted onto the HUD's item prompt layout.
/// </summary>
internal static class Overlay
{
    private static GUIManager? guiManager;
    private static TextMeshProUGUI? textMesh;

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

        Transform? promptLayout = guiManagerGameObj.transform.Find("Canvas_HUD/Prompts/ItemPromptLayout");
        if (promptLayout == null)
        {
            Plugin.Log.LogWarning("Could not find Canvas_HUD/Prompts/ItemPromptLayout - overlay not created.");
            return;
        }

        GameObject overlayGameObj = new GameObject("VeeItemInfo");
        overlayGameObj.transform.SetParent(promptLayout);
        textMesh = overlayGameObj.AddComponent<TextMeshProUGUI>();

        // Y is 0, otherwise it moves the other item prompts.
        overlayGameObj.GetComponent<RectTransform>().sizeDelta = new Vector2(PluginConfig.SizeDeltaX.Value, 0f);
        textMesh.font = guiManager.heroDayText.font;
        textMesh.fontSize = PluginConfig.FontSize.Value;
        textMesh.alignment = TextAlignmentOptions.BottomLeft;
        textMesh.lineSpacing = PluginConfig.LineSpacing.Value;
        textMesh.outlineWidth = PluginConfig.OutlineWidth.Value;
        textMesh.text = "";
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
