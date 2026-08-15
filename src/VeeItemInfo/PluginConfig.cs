using BepInEx.Configuration;

namespace VeeItemInfo;

internal static class PluginConfig
{
    private const string Appearance = "Appearance";
    private const string Position = "Position";
    private const string Behaviour = "Behaviour";

    internal static ConfigEntry<float> FontSize = null!;
    internal static ConfigEntry<float> OutlineWidth = null!;
    internal static ConfigEntry<float> LineSpacing = null!;
    internal static ConfigEntry<bool> RightAlign = null!;

    internal static ConfigEntry<float> Width = null!;
    internal static ConfigEntry<float> OffsetX = null!;
    internal static ConfigEntry<float> OffsetY = null!;

    internal static ConfigEntry<float> ForceUpdateTime = null!;

    internal static void Bind(ConfigFile config)
    {
        FontSize = config.Bind(Appearance, "Font Size", 28f,
            "Font size for the description text.");
        OutlineWidth = config.Bind(Appearance, "Outline Width", 0f,
            "Thickness of the outline around the text. 0 disables it.");
        LineSpacing = config.Bind(Appearance, "Line Spacing", -35f,
            "Spacing between lines. Negative values tighten it up.");
        RightAlign = config.Bind(Appearance, "Right Align", true,
            "Align text to the right edge of its box. Turn off for left-aligned text.");

        Width = config.Bind(Position, "Width", 550f,
            "Width of the text box. Text wraps at this width.");
        OffsetX = config.Bind(Position, "Offset X", -40f,
            "Horizontal offset from the bottom-right corner of the HUD. Negative moves left.");
        OffsetY = config.Bind(Position, "Offset Y", 210f,
            "Vertical offset from the bottom-right corner of the HUD. Positive moves up.");

        ForceUpdateTime = config.Bind(Behaviour, "Force Update Time", 1f,
            "Seconds between forced refreshes for values that no game event reports.");

        // Re-apply on change so the overlay can be positioned and styled while the game
        // is running, instead of a rebuild-and-relaunch for every nudge.
        config.SettingChanged += (_, _) =>
        {
            Overlay.ApplyStyle();
            ItemInfoController.MarkDirty();
        };
    }

    /// <summary>
    /// Some values (scorpion sting damage, rope remaining) change continuously with no
    /// hook to catch them, so they are only shown when the poll is frequent enough to
    /// keep them honest.
    /// </summary>
    internal static bool LiveValuesTrustworthy => ForceUpdateTime.Value <= 1f;
}
