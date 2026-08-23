# Third-party artwork shipped with this mod

Everything in this folder is **artwork from the game PEAK**, not original work by this
project's authors, and it is **not covered by the repository's MIT licence**. The MIT licence
in `LICENSE` applies to the source code only.

## numbness.png

- **What it is:** the numbness status icon from PEAK, cropped from a screenshot and
  background-keyed to transparency. No redrawing; it is the game's own art.
- **Copyright:** © Aggro Crab and Landfall Games. All rights reserved by them.
- **Why it is here:** the mod's overlay identifies statuses using the game's own icons, so a
  player recognises them instantly. Every other icon is scraped from the running game at
  runtime and nothing is stored — but numbness is not a `CharacterAfflictions.STATUSTYPE`
  and has no `BarAffliction`, so there is nothing in the scene to scrape. Shipping this one
  file is the only way to show it.
- **How it is used:** compiled into the mod assembly as an embedded resource, drawn only
  inside PEAK, only to label the effect it depicts. It is not redistributed as artwork, sold,
  or presented as this project's own.

## If you are the rights holder

Open an issue and this file will be removed and the feature dropped. No argument.

## Note for anyone reusing this repository

Cloning this repo does **not** give you a licence to PEAK's artwork. If you fork and publish,
the same reasoning has to hold for you: identification of in-game content, inside the game,
with attribution. Do not move these files into a context where they are the product.
