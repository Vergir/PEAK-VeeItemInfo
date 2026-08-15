# Changelog

## 2.0.0
- Revived as VeeItemInfo, a fork of jkqt's ItemInfoDisplay 1.0.8.
- New mod GUID (`com.github.vergir.VeeItemInfo`) and config section (`VeeItemInfo`) - existing ItemInfoDisplay settings will not carry over.
- Fixed the overlay going completely blank on items carrying a status added since PEAK 1.x
  (Spores, Web, Arrow, Petrify, FlyTrap). These had no colour assigned, and the resulting
  error aborted the whole description. Unknown statuses now fall back to a neutral colour.
- A game update that renames one patched method no longer stops the whole mod from loading;
  only the affected hook is lost.
- The overlay is now created when the HUD initialises rather than by searching for it every frame.
- Internal: split the single 861-line Plugin.cs into focused files, and moved all update
  logic behind one entry point so the Harmony patches only signal, never do work.

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