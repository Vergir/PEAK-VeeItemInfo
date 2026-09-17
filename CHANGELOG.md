# Changelog

## 2.0.0

Revived as VeeItemInfo, a fork of jkqt's ItemInfoDisplay 1.0.8, rebuilt to be more visual
and updated to the latest patch

- The overlay is symbols rather than sentences, and it sits above the inventory slot
  holding the item instead of in a corner of the screen.
- Four sections - item-specific facts, effects, cooking, weight - each hideable.
- Status icons are scraped from the running HUD and tinted with the status bars' own
  colours, so the overlay matches what the bars show.
- A cooking hint saying whether one more turn on the fire helps the item or ruins it.
- Every figure is read out of the game rather than typed in, so items the original never
  described are covered and a patch that changes a value does not make the mod lie.
- Configurable status scale, distance units, Shroomberry spoilers and placement.

---

Changelog below is from the original ItemInfoDisplay by jkqt.

## 1.0.8 (for PEAK v.1.23.b)
- Added estimated poison info for Scorpion's sting.
- Minor change to descriptions for Cactus Ball, Scout Cannon, & Ancient Idol.

## 1.0.7 (for PEAK v.1.23.a)
- Fixed mod breaking from removal of scorpion's poison var.

## 1.0.6 (for PEAK v.1.20.a)
- Added thorns to description for Prickleberry.

## 1.0.5 (for PEAK v.1.20.a)
- Added description for Sunscreen.
- Added description for Dynamite.
- Added description for Scorpion.

## 1.0.4 (for PEAK v.1.20.a)
- Rebuilt for THE MESA update.

## 1.0.3 (for PEAK v.1.10.b)
- Added poison removal for Remedy Fungus.
- Added line spacing config variable.

## 1.0.2 (for PEAK v.1.7.a)
- Added bonk description for Coconut.
- Added warning for Anti-Rope Spool & Anti-Rope Cannon.
- Added maximum length to Chain Launcher's description.
- Fixed incorrect length for Rope Cannons' descriptions.
- Fixed incorrect value of Drowsy status change for Lollipop.

## 1.0.1 (for PEAK v.1.6.b)
- Check if ascent item weight modifier is > 0 and add it to the item weight.