# How the mod is built

The machinery: how a description is assembled, how the icons and colours are borrowed from
the game, how the overlay is placed, and what the diagnostics are for. The facts about PEAK it
relies on are in [internals_game.md](internals_game.md); what the overlay chooses to say is in
[design.md](design.md).

---

## The update path

**Harmony patches only signal.** Every patch is a Postfix that sets a flag or forwards to
`ItemInfoController`; none contains update logic, and one function owns the update path. The
mod observes and must not alter gameplay. Targets are named with `nameof`, which works for
private Unity messages like `Update` because `Assembly-CSharp` is publicized at build time, so
a method the game renames breaks the build instead of silently never firing. Each patch type
gets its own `Harmony` instance in `Plugin.ApplyPatches`, so a target that still fails at
runtime costs one hook rather than the plugin.

The hooks: `GUIManager.Start` creates the overlay; `CharacterItems.Update` is the frame tick;
`CharacterItems.Equip`, `ItemCooking.FinishCooking` and `Action_ReduceUses.ReduceUsesRPC` mark
the overlay dirty when they concern the observed character.

**Rebuilt on events, not sampled.** Nothing the overlay prints changes while you hold the item:
every figure comes from a field on a component or a prefab. Two things still need catching
that no game event reports:

- *Somebody else swapping what they hold while you watch them.* Their `Equip` does reach us,
  but nothing else about them does. The `sinceItemAttach` comparison in `Tick` reads as a
  timer and is really a change detector: the value counts up in real time *and* is reset to
  zero on every attach, so one comparison fires on a schedule and fires at once when the item
  changes hands. `Periodic Refreshes` in config turns it off. The interval is one second and
  not configurable - the setting is whether the re-check happens at all.
- *A description assembled before the icon atlas existed.* `StatusIcons.Tag` falls back to
  the status name in capitals, and assigning the sprite asset later cannot repair a string
  that is already built. `Overlay.EnsureIcons` marks the overlay dirty when it attaches a new
  asset, so the rebuild is an event rather than a race the timer wins.

A re-render is requested for the next frame rather than done in the check, so a slow build
cannot land in the same frame as the check that asked for it. The overlay is positioned after
the text is built, so the box is sized to the current content.

**Refresh order.** The held-item dump runs *before* `Build`, because an item that makes `Build`
throw is exactly the one you need dumped. Then `Build`, `SetText`, and the untagged-colour
audit.

**Hot reload contract.** `Plugin.OnDestroy` must undo everything `Awake` did, or each
AutoReload stacks another copy: unpatch every stored `Harmony`, destroy the overlay
GameObject (it is parented to the game's canvas and would outlive the assembly), reset
`StatusIcons`, and forget the dump's once-flag and the builder's twin table. New static state
or new Unity objects need their cleanup added there.

**Performance.** The overlay runs off `CharacterItems.Update`, so anything it touches runs
every frame. Never retry a failed expensive operation on the next frame -
`StatusIcons.EnsureBuilt` spaces and caps its retries, `Overlay.EnsureCreated` throttles to
once a second. `ForceMeshUpdate` and text measurement run only when the text or style changes,
`SetText` early-returns on unchanged text, the handler lookup caches its misses, and debug
logging is gated behind one config check.

---

## Building a description

**`Build` walks every component on the item** and skips three kinds: a null entry (a missing
script serializes as one, and Stone ships one), a disabled `Behaviour` (cooking switches
actions off rather than removing them), and an `ItemAction` flagged `OnConsumed` on an item
that is never consumed (cooking adds such actions to anything). One test each, here, rather
than in whichever handlers happen to need it.

`CookingHint.IsConsumed` answers that last one, and it asks which components consume the item
rather than whether it can be eaten: reading a Scroll as un-consumable hid a real +10 stamina.
The paths are in [internals_game.md](internals_game.md), "Cooking".

**Components are dispatched by a type lookup that walks to the base**, never by a chain of
tests. `ItemDescriptionBuilder.Handlers` maps a component type to the method that describes
it; `HandlerFor` tries the component's own type, then its base, and so on, caching the answer
including misses. The most derived entry wins, source order means nothing, and a subclass with
no entry inherits its base's description. A chain of `GetType() == typeof(T)` tests would
lose every subclass silently (`Action_SuperJumpAmulet` under `Action_ApplyAffliction`,
`CactusBall` under `StickyItemComponent`, `ScoutEffigy` under `Constructable`); a chain of
`is` tests would make correctness depend on which branch sat higher in the file, since a
Scout's Ambition is both an `Action_SuperJumpAmulet` and an `Action_ApplyAffliction`. Adding
an item is one entry in `Handlers` plus one method. `ItemCooking` is deliberately absent from
the table: the cooking hint is decided in `Build` after every handler has run, from the first
`ItemCooking` only. `EffectFormatter.AfflictionHandlers` is the same shape for afflictions.

**A handler says what it means, never where its line goes.** Every handler receives a `Parts`:
the layout, the effect-line list, the item, whether it is consumed, and the index of the
component being described. Custom, Cooking and Weight lines go straight to the layout. Effect
lines are collected as `EffectLine`s carrying the finished text plus the keys that place them
- `Onset` (instant or over time), the status, the signed amount, the source component index,
and whether the line is a clear-all - and `EffectOrder.Sort` places them once all handlers
have run. `Collect` drops anything empty so the branches stay free of guards.

**`EffectOrder`** applies, in turn: petrify last whatever else is true; onset; status by the
curated rank in `EffectOrder.Order` (names taken from `STATUSTYPE` so a renamed member breaks
the build rather than sorting last); source component index; and finally the line's original
position, because one component can add several lines and `List.Sort` is unstable -
Dynamite's held-injury and blast lines are identical on every other key. Before sorting,
`DropRedundantClears` removes a clear-all line for any status something else already removes
at the same onset. After sorting, `MergeRepeats` walks the neighbours it has just made
adjacent and adds up any like-signed pair whose whole text is `Token(amount, status)` -
re-rendered from the new total, so `EffectFormatter` still chooses the form. `IsPlainToken`
compares the line's rendered text rather than reading a flag set when it was built: a form
that later grows a duration or a reach stops folding on its own, with nothing to keep in
sync. The reasons for each key, and for each thing the fold refuses, are in
[design.md](design.md).

**`DescriptionLayout`** owns sections and separators. Handlers call `Add(Block, text)`; the
layout ignores empty text and any section the player has switched off, trims surrounding
blank lines, and renders the four blocks in order with a blank line between sections (or none,
by config), except that Weight sits flush under Cooking. Callers never embed newlines or
re-read what was added.

**`EffectFormatter`** is the only place the *form* of a line is chosen: signed token, shared
budget, conditional pair, per-second rate, or total-and-duration versus rate-and-duration
(`OverTime` picks by whether the total exceeds a full bar). It scales fractions by the
configured status scale with the fewest decimals that show one status step exactly, floors
petrify to whole points first, converts Unity units to metres through
`CharacterStats.unitsToMeters` unless the raw-units setting is on, and formats every number
with the invariant culture so a comma decimal on a Russian locale cannot break `2,5 / 8s` into
two figures. `Clearable`, the statuses a clear-all lists, is derived by asking
`StatusIsCurable` for every `STATUSTYPE`, minus the mod's two deliberate omissions, minus any
status with no icon; it is cached and dropped whenever the icon atlas is rebuilt, because
which statuses can be rendered depends on that build.

**Two aliases and one assumption.** `Peak.Afflictions.Affliction` is used through the
`PeakAffliction` alias, because a second unrelated `Affliction` type in the global namespace
wins otherwise. `Affliction_HealAll.statusesToHeal` is read rather than typed out. The
database dump sets `AssumeHuman` so prefabs are described for a human holder.

**Twins.** A mushroom and its poisonous twin share a display name. `ItemDescriptionBuilder`
builds a name-to-items table from the database once and keeps it until a hot reload; a held
item is a clone of its prefab and is not its own twin.

**`GameValues`** reads the game's `const` fields out of the loaded assembly's metadata, since
naming a `const` inlines it. Each reading names the member through `nameof`, passes the
compiled value as the fallback, rejects a non-positive reading, and warns once by name if it
had to fall back.

**Never throw on the build path.** This is a read-only overlay, and an exception in `Build`
blanks it. `EffectColors.Get` returns the neutral colour for an unknown key,
`StatusIcons.Tag` degrades to text, `GameValues` falls back, prefab walks are guarded at every
hop, and the database dump wraps each item so one throwing prefab costs one entry. A number
that is one patch stale still describes the item; no overlay describes nothing. What must not
happen is a *silent* fallback, which is why the ones that can log do so once.

**`Blast`** is the arithmetic for what an AOE delivers to the person who set it off: the
point-blank factor from the AOE's own `range`, `factorPow`, `minFactor` and `ignoreFactor`,
floored to whole steps by magnitude, times the number of firings. `Reachable` says whether a
radius measured from the character's centre can be entered on foot at all. Not for Petrify,
which is whole points rather than a fraction.

**`CookingHint`** answers "should this go on the fire again?" from the *next* stage: canBeCooked
and the cooking maximum, wreck-on-cook, then the behaviours judged by kind (skipping any not
yet reached or already spent), then `ignoreDefaultCookBehavior`, then the stat ladder.

---

## Colours

**Every visible thing sits inside a colour tag** - text and sprites both, because a `tint=1`
sprite multiplies by the surrounding colour, so an untagged icon is as wrong as an untagged
number. A leak is not catchable by eye: TextMeshPro renders an untagged run pure white, which
reads as "bright" rather than "wrong". Two things guard it. `Overlay` sets `textMesh.color` to
`EffectColors.Base`, the HUD's cream, so a missed tag degrades instead of shouting - a safety
net, not a licence. And `ItemDebug.LogUntagged` parses the finished string every time it
changes and warns `[color]` with the offending run, so a leak is a log line rather than a
thing somebody has to notice.

**The palette is read off the game, and the table is the fallback.** `StatusIcons` already
walks every `BarAffliction` to scrape icons, so `EffectColors.Sample` rides along on that walk
and costs nothing; `Get` prefers a sampled colour. The table `EffectColors.Colors` stays
because a description built before the HUD exists still needs an answer, and because a
repaint should move the overlay rather than leave a pasted sampling saying the old thing.

- **The bright fill is picked as the pair of Images that agree**, not by sprite name (a
  name-keyed lookup goes quiet after a UI reshuffle) and not by brightness (Curse's backing
  outshines its fill). The bar's own icon is skipped: it is a white silhouette tinted by its
  Image and would pair with anything. Finding no pair samples nothing, leaving the table in
  place - a wrong colour is worse than an old one.
- **`Sample` refuses pure white and pure black.** An untinted Image means the artwork carries
  its own colour and nothing was chosen; taking it would turn the shield marker from gold into
  the default. A black Image is a backing. Only *pure* white is refused: Web is `#E6E6E7`, a
  real status colour at 0.90, so a threshold with any slack would silently discard a reading
  the moment the game brightened that bar.
- Shield, Cook and Extra Stamina are sampled from their indicator Images. Extra Stamina is
  keyed **with the space**, because that is what `EffectColors` is asked for; the sprite is
  registered without one only because a sprite name cannot contain a space, and those are two
  different tables. The icon is sampled first and the bar fill second so the fill wins.
- The atlas log line names every sampled colour that differs from the table. **A long list
  means the sampling has latched onto the wrong Image**; a short one means the game moved a
  colour and the overlay followed it. Samples are cleared on every `Reset`, so a hot reload or
  a rebuilt HUD never carries colours forward from a scene that no longer exists.

---

## Icons

Status icons are scraped from the game at runtime, packed into one atlas, and exposed as a
`TMP_SpriteAsset`, so a description can say `+30 <flame>` instead of `GAIN 30 HOT`. Nothing is
shipped or attributed except the numbness icon, which has nothing in the scene to scrape;
`assets/NOTICE.md` records its provenance, and a second shipped asset would need the same
paperwork.

**Sources**, in the order they are added so a status keeps its key if an item ever shares the
name: every `BarAffliction` (keyed off `isPetrify` for the petrify bar), the stamina bar's
extra-stamina icon, the shield and campfire indicator objects (`Shield`, `Cook`), item icons
under keys of the mod's own - `Item` (the BingBong-tagged mascot, for "some item"),
`RopeCannon`/`RopeCannonAnti` (a `RopeShooter` without or with `Antigrav`),
`RopeSpool`/`RopeSpoolAnti` (`isAntiRope`), `Float`/`FloatBunch` (a `Balloon` by `isBunch`) -
then every item that another item turns into, then the embedded numbness PNG.

The transformation icons are collected by asking the prefabs: `Action_ConsumeAndSpawn.itemToSpawn`
and `CookingBehavior_ReplaceItem.replaceWithItem`. A line saying "this becomes that" has to
show *that*, and which item it is comes off the field. Packing all items instead would be
about 16 MB to serve a handful of lines. `ItemTag` renders one and falls back to the generic
glyph, **never** to the name: a prefab name is English and internal, and `Tag`'s upper-case
fallback is only right for a status. Item keys have their spaces removed, as `Extra Stamina`
is registered as `ExtraStamina`, because a space breaks the rich-text tag.

**Pipeline**, per icon:

1. `MakeReadable` blits the source through the GPU (source textures are not CPU-readable),
   cropped to the sprite's own rect and shrunk to `IconPixelHeight` (128) pixels tall, both
   riding on the blit the readback already needed. **Height, not the long side**: the sources
   are nothing like square, and every glyph is pinned to a height of `IconScale` em with its
   width following its own aspect, so capping the long side would give a wide icon fewer
   vertical pixels than a tall one. It shrinks in halving steps: one bilinear tap reads four
   texels, so a 4x reduction in a single blit would alias every thin line in a silhouette. It
   never upscales. 128 covers the whole font-size range with room to spare; the atlas lands at
   1024x512. Packing at the stored size would cost a 4096x4096 atlas - 64 MB for twenty-five
   glyphs.
2. `ShouldTint` samples the copy on a coarse grid and decides whether the icon is a flat
   silhouette (tint it) or artwork with its own colours (do not). Decided by looking at the
   texture, never by a list of names, which would rot the first time the game recoloured an
   icon.
3. `Sharpen` steepens the alpha ramp around its midpoint, driven by `Icon Sharpness`. It runs
   *after* the downscale because the downscale softens the edge again. Applied to every icon:
   a feathered boundary is an artefact of the authoring size whatever is inside it. The
   sharpness is baked in, so moving the slider repacks the atlas (`MatchesSettings`).
4. `Texture2D.PackTextures` into one atlas; a `TMP_SpriteAsset` with a `TextMeshPro/Sprite`
   material. TMP treats a version-less asset as legacy and runs an upgrade pass over
   `spriteInfoList`, which is null on a runtime instance - it is given an empty list first.
5. Each glyph's rect **is** the packed cell, because the copy was cropped to the icon. Its
   size is taken from the copy rather than from the packer's UV rect: rounding `uv.width x
   atlas.width` back to whole texels can land a pixel out, and a glyph rect one pixel wrong
   does not crop the sprite, it rescales it, so every texel is sampled off-grid and the icon
   softens - one pixel in 28 is nearly four percent. If the packer had to shrink a cell to
   fit, that is warned rather than absorbed.
6. `ApplyMetrics` sets every glyph to `IconScale` (0.85) em tall with its own aspect, and a
   bearing of `CapCentre` (0.31 em, dialled in against the HUD's all-caps font) plus half the
   height, so the icon stays centred on the cap height at any size - a fixed bearing gets
   dragged toward the baseline as the glyph shrinks. Both are fractions of the font size, so
   `Font Size` stays the single knob.
7. Aliases: `Heat` for `Hot`, `Sleepy` for `Drowsy`, `Extra Stamina` for `ExtraStamina`.
8. Atlas, asset and material are flagged `HideAndDontSave`: runtime-generated assets belong to
   no scene, and a scene load would otherwise unload them and leave the text pointing at freed
   sprites, which TMP draws as `?`. Readable copies and the mod's own source textures are
   destroyed afterwards; borrowed ones are never touched.

One log line per build: how many icons, the atlas size, which icons were left untinted (an
icon rendering muddy is almost always that decision going the wrong way), the source sizes
grouped so the outliers stand out, and the palette report.

**Lifecycle.** `EnsureBuilt` spaces attempts two seconds apart and gives up after fifteen,
since the status bar may genuinely not exist yet on the first few tries. `IsValid` checks the
Unity objects are still alive, not just that the mapping is populated, because a scene load
can destroy them underneath it. A HUD rebuild (dying, a new run) creates a **new**
`TextMeshProUGUI` with no sprite asset assigned, so `EnsureIcons` re-assigns it rather than
checking whether icons exist, and marks the text dirty because a string built before the atlas
contains `HUNGER` rather than a sprite tag. `Reset` also clears the aspect list, which is
indexed in step with the glyphs.

**One atlas, not one asset per icon.** Each icon is on its own texture, and one sprite asset
per texture chained as fallbacks does not resolve - TMP renders `?`. If icons ever look wrong
again, two throwaway diagnostics settle it fastest: write the packed atlas to a PNG beside the
log with `Texture2D.EncodeToPNG`, and write a single icon's source texture out whole, then
compare them side by side.

---

## Overlay placement

The overlay is a `TextMeshProUGUI` parented to `GUIManager.hudCanvas`, found through
`GUIManager.instance` - both public fields, so a rename breaks the build. The font is the
HUD's own; `raycastTarget` is off so it never intercepts clicks; overflow is allowed so a long
description spills rather than clips.

**Each frame it measures the inventory slot holding the current item and centres itself above
it.** Anchors are the HUD's bottom-left so `anchoredPosition` is a plain HUD-space
coordinate whatever pivot the canvas uses; the pivot is the text's bottom-centre so it grows
upward. The temporary slot is checked first, then the numbered ones; a slot matches when it
is active and holds this exact `ItemInstanceData`. The offsets from config are applied, and
the result is clamped on screen so a misplaced overlay never becomes an invisible one. The box
is sized to the measured text height, which is what makes `Offset Y` a true bottom edge;
measuring only happens when text or style changes.

Four other placements do not work: parenting into `ItemPromptLayout` hands position to the
game's layout group, leaving box width as the only way to move sideways;
`LayoutElement.ignoreLayout` to escape that stops the overlay rendering entirely; offsets from
a screen corner drift because the HUD reflows with aspect ratio; and `ItemPromptLayout` spans
the full canvas height, so its corners are the screen edges.

`EnsureCreated` covers the HUD being torn down and rebuilt, which leaves the old references
Unity-null; it retries at most once a second because `Create` is a scene lookup.

---

## Configuration

`ConfigurationManagerAttributes` is read by BepInEx ConfigurationManager through reflection -
it matches the class by name, so only the fields used are declared. `Order` runs downward
within each section so the F1 menu shows settings in binding order rather than alphabetically.
`IsAdvanced` hides a setting behind the menu's advanced toggle.

**Every numeric setting declares a range.** Without one, ConfigurationManager renders a text
box that only commits on Enter, which reads as "changing the value does nothing", and an empty
text box commits `Font Size = 0`, which makes the overlay invisible. The `Bind` helpers
enforce it.

**No apostrophe, quote, bracket, backslash or `=` in a config key.** BepInEx throws from
`Bind`, and `Bind` runs in `Awake`, so the whole plugin fails to load: nothing patched, nothing
in F1, one `ArgumentException` in the console. It is "Do Not", not "Don't".

**Renaming a config key or moving it to another section resets it.** BepInEx reads the `.cfg`
by section and key, so the old value becomes an orphaned line and the entry comes back at its
default.

`SettingChanged` re-applies the style and marks the overlay dirty, so everything can be tuned
live from F1. Switching Debug Logging off arms the next switch-on to write the database dump
again. Icon size and alignment are constants expressed as fractions of the font size, so
`Font Size` is the one knob for how big everything is. Offsets are measured from the top-centre
of the slot holding the item.

---

## Diagnostics

`Advanced -> Debug Logging` (off by default) turns on three things:

| Tag | What | When |
|---|---|---|
| `[item]` | The held item's name, tags, icon and every component on it, with the values of the fields that drive the description and a `[DISABLED]` marker. Prefabs a component spawns, builds or breaks into are walked four levels deep, every component named; effect-carrying components (AOE, StatusField, StatusEmitter) are listed at any depth with `[INACTIVE]` where their object is off; cooking behaviours are printed with what each would do; a Shroomberry prints its slot and the whole dealt table. | Once per item name - switch items and back for a fresh block. |
| `[color]` | Any run in the finished description that sits outside a colour tag. | Once per distinct description; silent when there is nothing wrong. |
| `[dump]` | Every item in the database with its components and what `Build` returns for it, to `BepInEx/VeeItemInfo-items.txt`, sorted by name so two dumps diff cleanly and each item wrapped so one throwing prefab costs one entry; and the same rows rendered as `BepInEx/VeeItemInfo-showcase.html`. | Once per switch-on, the next time an item is held - the one moment the database is certain to be loaded. |

Not gated: one line per atlas build (icon count, atlas size, untinted icons, source sizes,
palette report), a warning when a game constant could not be read, a warning when the packer
scaled a cell, a warning when the HUD has no canvas, and an error per patch that failed to
apply.

The rules behind them:

- **Log state when it changes, never once per refresh.** `Refresh` runs on every equip and on
  the periodic re-check, so anything logged unconditionally arrives about once a second and
  buries the lines that report an actual event.
- **A log line nobody will read is not a safety net.** Warning on an unrecognised affliction
  type or a component with no handler would make a gap feel covered without anybody learning
  of it, because nobody opens the log unless they are already debugging. Diagnostics are for
  an investigation somebody is running now. A field that drives a description belongs in
  `[item]`, not in a warning.
- **A diagnostic that has done its job is deleted.** Git history has it if the question
  comes back.
- **The dump is the regression check.** After any display change, diff two dumps. It also
  writes the showcase, which is copied to `docs/showcase.html` by hand and committed with the
  change. It runs on database prefabs rather than live items, which is also the proof that
  `Build` works on a prefab at all; `RopeSpool.RopeFuel` needs a live instance and is printed
  as `<prefab>` there.
- `[item]` prints the value each component carries because two `Action_GiveExtraStamina` on
  one item look identical in a type list, and lists *every* component in a prefab walk because
  a filtered list hides exactly the component you did not think to filter for.

Inspecting the game outside the mod: `tools/Dump-GameTypes.ps1` lists types and members from
the game assembly (`-TypePattern` is the fastest way to find a candidate when the name is
unknown), and `ilspycmd -t <TypeName>` decompiles a single type, which is the workhorse.

---

## The showcase

`docs/showcase.html` is `PreviewPage` rendering the dump's rows: a card grid, each card the
overlay as it draws in game with the item's wiki picture and name under it, variants with
identical text collapsed into one card with a count. **Generated from `Build` itself, so it can
never disagree with the code** - and equally it is not a specification and cannot catch a bug
on its own.

- **No artwork in the repository.** Every icon is hotlinked from peak.wiki.gg, whose file names
  are the display name with underscores. Statuses live under `Status_*`, with two names that
  differ from the game's (`Bonus_stamina`, `Invincibility`); the campfire has no wiki icon and
  is an emoji.
- **Tinted icons cannot be CSS masks.** A browser refuses a cross-origin image as `mask-image`
  unless the host opts in, and the icon vanishes with it. They are `<img>`s recoloured by an
  SVG filter (`feFlood` clipped to `SourceAlpha`), one per colour used; filters never read
  pixels back and are allowed on any image. Whether a sprite is tinted follows the `tint=`
  attribute the mod itself wrote.
- **No script.** The page is static HTML and CSS.
- **Display names.** `Item.GetName()` has the right words in the HUD's upper case;
  `UIData.itemName` is a raw key for some items. The page title-cases `GetName`, keeps the
  wiki's small words lower (`Bugle of Friendship`), straightens the typographic apostrophes,
  and aliases the two real items the wiki files elsewhere (`Half-Coconut`, `Cooked Bird`).
  Props and unlocalised leftovers have no page; their image hides itself.
- A sprite key with no wiki file is shown as its name, so a gap in the table is visible.

Regenerating it: Debug Logging on, hold an item, copy `BepInEx/VeeItemInfo-showcase.html` over
`docs/showcase.html`. Where it renders for readers is an open publishing decision (GitHub
Pages, or a raw-HTML previewer).

---

## Build and packaging

`dotnet build -c Release` builds and deploys the DLL into the local mod profile;
`-target:PackTS` also produces the Thunderstore zip through `tcli`. The package version comes
from `<Version>` in the csproj, not from `thunderstore.toml`, and `manifest.json` is generated
at pack time - never hand-written. `Assembly-CSharp` is publicized by
`BepInEx.AssemblyPublicizer.MSBuild`, which is what makes private game members readable and
`nameof` usable on private patch targets. The plugin attribute is generated by
`Hamunii.BepInEx.AutoPlugin`.
