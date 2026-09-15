using BepInEx.Configuration;

namespace VeeItemInfo;

/// <summary>
/// Read by BepInEx ConfigurationManager through reflection, matched by class name, so there
/// is no assembly to reference. Only the fields the mod uses are declared.
/// </summary>
internal sealed class ConfigurationManagerAttributes
{
    /// <summary>Position within a section, highest first; without it the menu sorts alphabetically.</summary>
    public int? Order;

    public bool? IsAdvanced;
}

internal static class PluginConfig
{
    private const string Appearance = "Appearance";
    private const string Sections = "Sections";
    private const string Formatting = "Formatting";
    private const string ItemInfo = "Item Info";
    private const string Advanced = "Advanced";

    internal static ConfigEntry<float> FontSize = null!;
    internal static ConfigEntry<float> OutlineWidth = null!;
    internal static ConfigEntry<float> LineSpacing = null!;

    internal static ConfigEntry<float> IconSharpness = null!;

    internal static ConfigEntry<float> Width = null!;
    internal static ConfigEntry<float> OffsetX = null!;
    internal static ConfigEntry<float> OffsetY = null!;

    internal static ConfigEntry<bool> ShowCustom = null!;
    internal static ConfigEntry<bool> ShowEffects = null!;
    internal static ConfigEntry<bool> ShowCooking = null!;
    internal static ConfigEntry<bool> ShowWeight = null!;

    internal static ConfigEntry<int> StatusScale = null!;
    internal static ConfigEntry<bool> UnityMetres = null!;
    internal static ConfigEntry<bool> ShroomberryHint = null!;
    internal static ConfigEntry<bool> PurpleSpoiler = null!;
    internal static ConfigEntry<bool> EnergySpoiler = null!;
    internal static ConfigEntry<bool> SectionSpacing = null!;
    internal static ConfigEntry<bool> HidePoisonTwins = null!;
    internal static ConfigEntry<bool> RealRopeLength = null!;
    internal static ConfigEntry<bool> PeriodicRefresh = null!;
    internal static ConfigEntry<bool> DebugLogging = null!;

    internal static void Bind(ConfigFile config)
    {
        // Every numeric setting declares a range: without one ConfigurationManager renders a
        // text box that only commits on Enter, which reads as "changing the value does
        // nothing". Order runs downward within each section.
        FontSize = Bind(config, Appearance, "Font Size", 20f, 8f, 72f, 80,
            "Font size for the description text.");
        OutlineWidth = Bind(config, Appearance, "Outline Width", 0f, 0f, 1f, 70,
            "Thickness of the outline around the text. 0 disables it.");
        LineSpacing = Bind(config, Appearance, "Line Spacing", -35f, -100f, 50f, 60,
            "Spacing between lines. Negative values tighten it up.");
        IconSharpness = Bind(config, Appearance, "Icon Sharpness", 4f, 1f, 8f, 50,
            "How crisp the status icons are. The game draws them with very soft edges, which "
            + "reads as blurry at overlay size. 1 leaves them exactly as the game has them.");
        SectionSpacing = Bind(config, Appearance, "Add Space Between Sections", true, 40,
            "A blank line between the item-specific facts, the effects and the cooking/weight.");

        Width = Bind(config, Appearance, "Width", 200f, 50f, 2000f, 30,
            "Width of the text box. Text wraps at this width.");
        OffsetX = Bind(config, Appearance, "Offset X", 0f, -1500f, 1500f, 20,
            "Horizontal offset from the active slot. Negative moves left.");
        OffsetY = Bind(config, Appearance, "Offset Y", 50f, -1500f, 1500f, 10,
            "Height of the text's bottom edge above the active slot. Lines grow upward from here.");

        // In the order the overlay draws them, top to bottom.
        ShowCustom = Bind(config, Sections, "Show Item-specific data", true, 40,
            "Facts that are not status changes: the reach of a Rope Cannon, how long a stovetop burns.");
        ShowEffects = Bind(config, Sections, "Show Effects", true, 30,
            "Status changes, like -20 Hunger.");
        ShowCooking = Bind(config, Sections, "Show Cooking Hint", true, 20,
            "Whether cooking the item helps or ruins it.");
        ShowWeight = Bind(config, Sections, "Show Weight", true, 10,
            "The item's weight.");

        StatusScale = Bind(config, Formatting, "Status Scale", 100, 1, 1000, 20,
            "What a full status bar counts as. At 100 a full bar of hunger reads as 100 and a "
            + "Granola Bar restores 15; at 40 they read 40 and 6.");
        UnityMetres = Bind(config, Formatting, "Show Unity Meters", false, 10,
            "Show distances in Unity units instead of the PEAK's metres "
            + "(1 Unity Meter is 1.6 PEAK Meter).");

        ShroomberryHint = Bind(config, ItemInfo, "Shroomberry Effect Hint", true, 30,
            "Colour a Shroomberry's ???? green or red by whether its effect is good or bad. "
            + "Off shows plain question marks.\n\n"
            + "Red and yellow berries are always good and green and blue always bad, which anyone "
            + "can remember after a run or two - so this spoils nothing, and it is on by default.");
        PurpleSpoiler = Bind(config, ItemInfo, "Spoil Purple Shroomberry", false, 20,
            "The purple Shroomberry can roll either way, so its ???? is half green, half red. On, "
            + "the overlay reads the roll and colours it fully. Does nothing with the effect hint off.\n\n"
            + "The roll is different on every map, so seeing it before you have eaten one shows "
            + "information the game hides - which is why it is off by default.");
        EnergySpoiler = Bind(config, ItemInfo, "Spoil Shroomberry Energy Increase", false, 10,
            "Show the stamina a Shroomberry actually gives instead of the 0-15 range.\n\n"
            + "The amount is rolled per map like the effect, so this too shows information the "
            + "game hides - which is why it is off by default.");

        // No apostrophe: BepInEx rejects ' in a key, and a throw here stops the plugin loading.
        HidePoisonTwins = Bind(config, ItemInfo, "Do Not Distinguish Poisonous Mushrooms", false, 40,
            "Some mushrooms have a poisonous twin that looks slightly different and carries the same "
            + "name. Off, the poisonous one shows its poison and the safe one shows none. On, both show "
            + "the poison as a coin flip - 0/+20 - so the overlay no longer tells them apart.\n\n"
            + "Nothing about the run is spoiled either way; this is for players who enjoy telling "
            + "the twins apart by eye.");

        RealRopeLength = Bind(config, ItemInfo, "Show Real Rope Length", false, 50,
            "By default Rope Cannon shows rope length using the same units as in-game UI for Rope Spool (7.5m). "
            + "This option makes Rope Cannon show its real rope length: "
            + "this is consistent with all other distances in the game but breaks parity with Rope Spool");

        PeriodicRefresh = Bind(config, Advanced, "Periodic Refreshes", true, 30,
            "Re-check held item once a second. Allows seeing item effects when watching other "
            + "players and fixes some weird glitches. Has a negligible performance cost", advanced: true);
        DebugLogging = Bind(config, Advanced, "Debug Logging", false, 20,
            "Keep off unless you're diagnosing problems. On, the held item's components and values "
            + "go to BepInEx/LogOutput.log, and the next item you hold also writes every item in "
            + "the game, with what the overlay says for it, to BepInEx/VeeItemInfo-items.txt. "
            + "Switch it off and on again to write that file again.", advanced: true);

        config.SettingChanged += (_, _) =>
        {
            // Switching debug off arms the next switch-on to dump the database again.
            if (!DebugLogging.Value)
            {
                ItemDump.Forget();
            }

            Overlay.ApplyStyle();
            ItemInfoController.MarkDirty();
        };
    }

    private static ConfigEntry<float> Bind(ConfigFile config, string section, string key,
        float value, float min, float max, int order, string description) =>
        config.Bind(section, key, value, new ConfigDescription(description,
            new AcceptableValueRange<float>(min, max), Attributes(order)));

    private static ConfigEntry<int> Bind(ConfigFile config, string section, string key,
        int value, int min, int max, int order, string description) =>
        config.Bind(section, key, value, new ConfigDescription(description,
            new AcceptableValueRange<int>(min, max), Attributes(order)));

    private static ConfigEntry<bool> Bind(ConfigFile config, string section, string key,
        bool value, int order, string description, bool advanced = false) =>
        config.Bind(section, key, value, new ConfigDescription(description, null,
            Attributes(order, advanced)));

    private static ConfigurationManagerAttributes Attributes(int order, bool advanced = false) =>
        new() { Order = order, IsAdvanced = advanced ? true : null };

    internal static bool ShowBlock(Block block) => block switch
    {
        Block.Custom => ShowCustom.Value,
        Block.Effects => ShowEffects.Value,
        Block.Cooking => ShowCooking.Value,
        Block.Weight => ShowWeight.Value,
        _ => true,
    };
}
