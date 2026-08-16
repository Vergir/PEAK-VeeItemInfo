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

    internal static ConfigEntry<float> Width = null!;
    internal static ConfigEntry<float> OffsetX = null!;
    internal static ConfigEntry<float> OffsetY = null!;

    internal static ConfigEntry<float> ForceUpdateTime = null!;
    internal static ConfigEntry<bool> DebugLogging = null!;

    internal static void Bind(ConfigFile config)
    {
        // Every numeric setting declares a range on purpose. Without one, config editors
        // such as ConfigurationManager fall back to a text box that only commits on Enter,
        // which reads as "changing the value does nothing". A range gets you a live slider.
        FontSize = Bind(config, Appearance, "Font Size", 20f, 8f, 72f,
            "Font size for the description text.");
        OutlineWidth = Bind(config, Appearance, "Outline Width", 0f, 0f, 1f,
            "Thickness of the outline around the text. 0 disables it.");
        LineSpacing = Bind(config, Appearance, "Line Spacing", -35f, -100f, 50f,
            "Spacing between lines. Negative values tighten it up.");
        // Icon size and alignment are constants in StatusIcons, expressed as fractions of
        // the font size. That makes Font Size the single knob for how big everything is.

        // Offsets are measured from the top-centre of whichever inventory slot holds the
        // item being described, so the overlay follows the selected slot and holds at any
        // resolution.
        Width = Bind(config, Position, "Width", 200f, 50f, 2000f,
            "Width of the text box. Text wraps at this width.");
        OffsetX = Bind(config, Position, "Offset X", 0f, -1500f, 1500f,
            "Horizontal offset from the active slot. Negative moves left.");
        OffsetY = Bind(config, Position, "Offset Y", 50f, -1500f, 1500f,
            "Height of the text's bottom edge above the active slot. Lines grow upward from here.");

        ForceUpdateTime = Bind(config, Behaviour, "Force Update Time", 1f, 0.1f, 10f,
            "Seconds between forced refreshes for values that no game event reports.");
        DebugLogging = config.Bind(Behaviour, "Debug Logging", false,
            "Log overlay placement numbers to the BepInEx console. For diagnosing position problems.");

        // Re-apply on change so the overlay can be styled and positioned while the game
        // is running, instead of a rebuild-and-relaunch for every nudge.
        config.SettingChanged += (_, _) =>
        {
            Overlay.ApplyStyle();
            ItemInfoController.MarkDirty();
        };
    }

    private static ConfigEntry<float> Bind(ConfigFile config, string section, string key, float value, float min, float max, string description) =>
        config.Bind(section, key, value, new ConfigDescription(description, new AcceptableValueRange<float>(min, max)));

    /// <summary>
    /// Some values (scorpion sting damage, rope remaining) change continuously with no
    /// hook to catch them, so they are only shown when the poll is frequent enough to
    /// keep them honest.
    /// </summary>
    internal static bool LiveValuesTrustworthy => ForceUpdateTime.Value <= 1f;
}
