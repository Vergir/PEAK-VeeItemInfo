using BepInEx.Configuration;

namespace VeeItemInfo;

internal static class PluginConfig
{
    private const string Appearance = "Appearance";
    private const string Position = "Position";
    private const string Behaviour = "Behaviour";
    private const string Sections = "Sections";

    internal static ConfigEntry<float> FontSize = null!;
    internal static ConfigEntry<float> OutlineWidth = null!;
    internal static ConfigEntry<float> LineSpacing = null!;

    internal static ConfigEntry<float> Width = null!;
    internal static ConfigEntry<float> OffsetX = null!;
    internal static ConfigEntry<float> OffsetY = null!;

    internal static ConfigEntry<bool> PeriodicRefresh = null!;
    internal static ConfigEntry<bool> RealRopeLength = null!;
    internal static ConfigEntry<bool> DebugLogging = null!;

    // One toggle per section of the overlay, so a player who only cares about weight can
    // have just that. Defaults are all on - the mod's whole purpose is the information.
    internal static ConfigEntry<bool> ShowCustom = null!;
    internal static ConfigEntry<bool> ShowEffects = null!;
    internal static ConfigEntry<bool> ShowCooking = null!;
    internal static ConfigEntry<bool> ShowWeight = null!;

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

        ShowCustom = config.Bind(Sections, "Show Custom", true,
            "Item-specific facts that are not status changes, like length of Rope Cannon rope");
        ShowEffects = config.Bind(Sections, "Show Effects", true,
            "Status changes, like -20 Hunger");
        ShowCooking = config.Bind(Sections, "Show Cooking Hint", true,
            "Whether cooking the item helps or ruins it.");
        ShowWeight = config.Bind(Sections, "Show Weight", true,
            "The item's weight.");

        PeriodicRefresh = config.Bind(Behaviour, "Periodic Refreshes", true,
            "Re-check held item once a second. Allows seeing item effects when watching other "
            + "players and fixes some weird glitches. Has a negligible performance cost");
        RealRopeLength = config.Bind(Behaviour, "Show Real Rope Length", false,
            "By default Rope Cannon shows rope length using the same units as in-game UI for Rope Spool (7.5m). "
            + "This option makes Rope Cannon show its real rope length (21m): "
            + "this is consistent with all other distances in the game but breaks parity with Rope Spool");
        DebugLogging = config.Bind(Behaviour, "Debug Logging", false,
            "Debug logging. Keep off unless you're diagnosing problems.");

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
    /// Whether a section is switched on. Called for every line added, so it stays a plain
    /// switch over already-bound entries rather than a dictionary lookup.
    /// </summary>
    internal static bool ShowBlock(Block block) => block switch
    {
        Block.Custom => ShowCustom.Value,
        Block.Effects => ShowEffects.Value,
        Block.Cooking => ShowCooking.Value,
        Block.Weight => ShowWeight.Value,
        _ => true,
    };
}
