using BepInEx.Configuration;

namespace VeeItemInfo;

/// <summary>
/// Read by BepInEx ConfigurationManager through reflection - it matches this class by name
/// and reads the fields it knows, so there is no assembly to reference. Only the fields the
/// mod uses are declared.
/// </summary>
internal sealed class ConfigurationManagerAttributes
{
    /// <summary>
    /// Position within a section, highest first. Without it the F1 menu sorts every section
    /// alphabetically, which put Show Cooking Hint above Show Custom - the opposite of the
    /// order the overlay draws them in.
    /// </summary>
    public int? Order;

    /// <summary>Hidden behind the menu's "advanced" toggle.</summary>
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

    /// <summary>
    /// How hard the contrast curve on an icon's alpha is - 1 leaves it alone.
    ///
    /// The game's icons are authored with very wide feathered edges: a status icon is pure
    /// white in RGB with the entire shape carried in alpha, and nearly as many of its pixels
    /// are part-transparent as are solid. That reads as a soft, muddy glyph at the size the
    /// overlay draws them, and it is in the artwork rather than in anything the mod does - a
    /// copy of the game's own texture at 1:1 is exactly as soft.
    /// </summary>
    internal static ConfigEntry<float> IconSharpness = null!;

    internal static ConfigEntry<float> Width = null!;
    internal static ConfigEntry<float> OffsetX = null!;
    internal static ConfigEntry<float> OffsetY = null!;

    // One toggle per section of the overlay, so a player who only cares about weight can
    // have just that. Defaults are all on - the mod's whole purpose is the information.
    internal static ConfigEntry<bool> ShowCustom = null!;
    internal static ConfigEntry<bool> ShowEffects = null!;
    internal static ConfigEntry<bool> ShowCooking = null!;
    internal static ConfigEntry<bool> ShowWeight = null!;

    /// <summary>
    /// What a full status bar reads as. Every status is a 0-1 fraction in the game;
    /// <see cref="EffectFormatter.Scaled"/> multiplies by this, and petrify and weight ride
    /// the same scale because they land on the same bars.
    /// </summary>
    internal static ConfigEntry<int> StatusScale = null!;

    /// <summary>
    /// Distances in raw Unity units rather than the metres the altitude readout shows.
    /// The Rope Cannon's rope length only follows this when Show Real Rope Length is on -
    /// otherwise it matches the spool's own display, whatever the unit.
    /// </summary>
    internal static ConfigEntry<bool> UnityMetres = null!;

    /// <summary>
    /// Colour a Shroomberry's ???? by whether its slot is good or bad. A colour's valence is
    /// fixed across runs, which is what makes this a hint rather than a spoiler.
    /// </summary>
    internal static ConfigEntry<bool> ShroomberryHint = null!;

    /// <summary>
    /// A berry past both quotas is a genuine coin flip and shows half green, half red. This
    /// reads the actual roll and commits to one colour. Nothing without the hint.
    /// </summary>
    internal static ConfigEntry<bool> PurpleSpoiler = null!;

    /// <summary>This map's dealt stamina for the berry, instead of the 0-15 span.</summary>
    internal static ConfigEntry<bool> EnergySpoiler = null!;

    /// <summary>A blank line between sections, or every line flush.</summary>
    internal static ConfigEntry<bool> SectionSpacing = null!;

    /// <summary>
    /// Show a mushroom and its poisonous twin - the game gives both the same name - with the
    /// same "0/+20 poison" line, so the overlay stops telling them apart by their effects.
    /// </summary>
    internal static ConfigEntry<bool> HidePoisonTwins = null!;

    internal static ConfigEntry<bool> RealRopeLength = null!;
    internal static ConfigEntry<bool> PeriodicRefresh = null!;

    /// <summary>
    /// The held-item log, and - once per switch-on, the next time an item is held - the
    /// whole-database dump. One switch: nobody turns on diagnostics without wanting both.
    /// </summary>
    internal static ConfigEntry<bool> DebugLogging = null!;

    internal static void Bind(ConfigFile config)
    {
        // Every numeric setting declares a range on purpose. Without one, config editors
        // such as ConfigurationManager fall back to a text box that only commits on Enter,
        // which reads as "changing the value does nothing". A range gets you a live slider.
        //
        // Order runs downward within each section, so the menu shows settings in the order
        // they are bound here.
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
        // Icon size and alignment are constants in StatusIcons, expressed as fractions of
        // the font size. That makes Font Size the single knob for how big everything is.

        // Offsets are measured from the top-centre of whichever inventory slot holds the
        // item being described, so the overlay follows the selected slot and holds at any
        // resolution.
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

        // No apostrophe: BepInEx rejects ' in a key, and a throw here takes the whole plugin
        // down with it - Awake never finishes, so nothing loads and nothing appears in F1.
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

        // Re-apply on change so the overlay can be styled and positioned while the game
        // is running, instead of a rebuild-and-relaunch for every nudge.
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
