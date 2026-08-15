using BepInEx.Configuration;

namespace VeeItemInfo;

internal static class PluginConfig
{
    private const string Section = "VeeItemInfo";

    internal static ConfigEntry<float> FontSize = null!;
    internal static ConfigEntry<float> OutlineWidth = null!;
    internal static ConfigEntry<float> LineSpacing = null!;
    internal static ConfigEntry<float> SizeDeltaX = null!;
    internal static ConfigEntry<float> ForceUpdateTime = null!;

    internal static void Bind(ConfigFile config)
    {
        FontSize = config.Bind(Section, "Font Size", 20f,
            "Customize the Font Size for description text.");
        OutlineWidth = config.Bind(Section, "Outline Width", 0.08f,
            "Customize the Outline Width for item description text.");
        LineSpacing = config.Bind(Section, "Line Spacing", -35f,
            "Customize the Line Spacing for item description text.");
        SizeDeltaX = config.Bind(Section, "Size Delta X", 550f,
            "Customize the horizontal length of the container for the mod. Increasing moves text left, decreasing moves text right.");
        ForceUpdateTime = config.Bind(Section, "Force Update Time", 1f,
            "Customize the time in seconds until the mod forces an update for the item.");
    }

    /// <summary>
    /// Some values (scorpion sting damage, rope remaining) change continuously with no
    /// hook to catch them, so they are only shown when the poll is frequent enough to
    /// keep them honest.
    /// </summary>
    internal static bool LiveValuesTrustworthy => ForceUpdateTime.Value <= 1f;
}
