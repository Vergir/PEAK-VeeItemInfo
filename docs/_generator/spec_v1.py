# -*- coding: utf-8 -*-
"""v1 - the target display, as decided in review.

Global rules first, then per-item overrides. Anything still undecided carries a note rather
than being silently invented. Numbers marked ? are prefab fields to be read from the game.
"""
from gen_spec import (C, NEUTRAL, WHITE, POS, NEG, ALL_STATUS, ic, icons, tok, num, col)
from spec_special import Q, clr

GREEN = "#7CE07C"   # readable positive, unlike the pale #DDFFDD
RED = "#FF7B7B"

# Glyphs whose presence in PEAK's HUD font is unverified. Every use is flagged.
INF = "&#8734;"      # U+221E - the only glyph whose font support is still unverified
ARROW = "&rarr;"


def glyph(ch):
    return '<b class="glyph">%s</b>' % ch


def unk(n=3):
    return "?" * n


def all_but(*drop):
    return [s for s in ALL_STATUS if s not in drop]


def line(amount, statuses, colour=WHITE, suffix=""):
    """One amount covering a run of statuses - the compact multi-status form."""
    sign = "+" if amount > 0 else "-"
    return clr(sign + num(abs(amount)), colour) + " " + icons(statuses) + suffix


def per_sec(amount, status, total=None):
    """Over-time effects are stated per second; an optional total duration follows an arrow."""
    out = tok(amount, status) + clr(" / s", NEUTRAL)
    if total is not None:
        out += clr(" " + ARROW + " ", NEUTRAL) + clr(total, NEUTRAL)
    return out


def reach(value):
    """How far a thing reaches. Just the number and a lowercase m - the arrow glyph was
    dropped in review as unnecessary, and units stay lowercase like the s on durations."""
    return clr(value + "m", NEUTRAL)


def secs(value, status):
    """A duration coloured to match the status it grants, e.g. Mandrake's numbness."""
    return clr(value, col(status)) + " " + ic(status)


# --- the cooking line -------------------------------------------------------
def cook_plain(polarity, strength=1):
    if polarity > 0:
        return clr("+" * strength, GREEN) + " " + ic("Cook")
    if polarity < 0:
        return clr("-", RED) + " " + ic("Cook")
    return None


def cook_explodes(radius=None, injury=None):
    """Cooking makes it go off over an area.

    No blast icon: the dynamite glyph was dropped because it reads as danger, and most of
    these explosions are helpful (Antidote, Cure-All and Faerie Lantern all heal everyone in
    range). A helpful explosion is campfire, arrow, radius. A harmful one adds the injury it
    deals, which is the honest way to mark it - the damage says "dangerous", not the icon.
    """
    out = ic("Cook") + clr(" " + ARROW + " ", NEUTRAL) + clr((radius or Q) + "m", NEUTRAL)
    if injury is not None:
        out += " " + tok(injury, "Injury")
    return out


def cook_polarity(it):
    """+1 cooking improves it, -1 cooking ruins or destroys it, 0 nothing meaningful."""
    ck = it["cooking"].lower().rstrip(".")
    if not ck:
        return 1 if "Food" in it["types"] else 0
    if ck in ("cannot be cooked", "no effect"):
        return 0
    if any(k in ck for k in ("incinerat", "pops", "explod", "wreck")):
        return -1
    if ck.startswith(("removes", "disables", "adds")):
        return 1
    return 0


# =============================================================================
# Per-item overrides.
#   note/status/others/state - replace that block outright
#   cook - explicit cooking line; False omits it entirely
#   notes - bullets shown in the right-hand column
# =============================================================================
V1 = {}


def v1(name, **kw):
    V1[name] = kw


def addnote(name, *texts):
    V1.setdefault(name, {}).setdefault("notes", []).extend(texts)


# --- shroomberries: the random-effect family --------------------------------
# No sign - the colour alone carries the direction, and four marks read as "a whole effect".
GOOD = clr("????", GREEN)
BAD = clr("????", RED)
EITHER = clr("??", GREEN) + clr("??", RED)

for berry, roll in (("Red Shroomberry", GOOD), ("Yellow Shroomberry", GOOD),
                    ("Green Shroomberry", BAD), ("Blue Shroomberry", BAD),
                    ("Purple Shroomberry", EITHER)):
    v1(berry, status=[tok(-10, "Hunger"), tok(5, "Spores"), roll])

# --- food -------------------------------------------------------------------
v1("Mandrake",
   status=[tok(-10, "Hunger"), secs("60s", "Numb")],
   cook=cook_plain(1, 3),
   notes=["Three plusses on the cooking line: cooking a Mandrake does much more than the "
          "usual hunger/stamina bump.",
          "TO VERIFY IN GAME: the 60s numbness duration, against the prefab."])
v1("Napberry",
   status=[tok(-100, "Hunger"), line(-100, all_but("Hunger", "Drowsy")), tok(100, "Drowsy")],
   notes=["Drowsy is kept out of the cleared run because the berry applies it straight after."])
v1("Big Lollipop",
   status=[tok(-5, "Hunger"), clr("8s", NEUTRAL) + " " + glyph(INF) + ic("Extra Stamina")],
   notes=["-100 Drowsy removed - you confirmed it does not do that.",
          "GLYPH CHECK: the infinity mark must render in PEAK's HUD font, or it shows as a box.",
          "TO VERIFY IN GAME: the 8s duration."])
v1("Energy Drink",
   status=[tok(-30, "Heat"),
           tok(-50, "Drowsy"),
           clr("8s", NEUTRAL) + " " + icons(["Extra Stamina"] * 3, sep="")
           + clr(" " + ARROW + " ", NEUTRAL) + tok(25, "Drowsy")],
   notes=["Three stamina icons stand for the speed boost - no infinity mark, because stamina "
          "is not actually infinite here.",
          "TO VERIFY IN GAME: the 8s duration and the trailing +25 Drowsy."])
v1("Fortified Milk",
   status=[tok(-5, "Hunger"), tok(-50, "Heat"), clr("15s", NEUTRAL) + " " + ic("Shield")],
   notes=["15s of invulnerability, extended by 10s more if cooked first - which is what the "
          "cooking plus stands for."])
v1("Scout Cookies", state=[])
v1("Sports Drink",
   notes=["NOTED FOR LATER: one of the few items that does not get worse when overcooked "
          "several times. Recorded in BACKLOG."])
v1("Cooked Bird", cook=False, notes=["Cannot be cooked any further."])
v1("Coconut",
   note=[clr("2", NEUTRAL) + " " + ic("HalfCoconut")],
   notes=["Breaking it yields two Half-Coconuts."])

# --- medicine ---------------------------------------------------------------
v1("Cure-All",
   status=[tok(-20, "Hunger"),
           line(-35, ["Poison", "Heat", "Cold", "Drowsy", "Injury", "Spores", "Thorns"]),
           tok(-5, "Curse")],
   state=[], cook=cook_explodes("4.8"),
   notes=["Curse keeps its own line because it is 5, not 35."])
v1("First Aid Kit", status=[line(-100, ["Poison", "Injury", "Spores"])], state=[])
v1("Antidote",
   status=[tok(-35, "Poison"), tok(-35, "Spores"), tok(-10, "Heat")],
   state=[], cook=cook_explodes("4.8"),
   notes=["Poison and Spores split back onto their own lines, as asked."])
v1("Heat Pack",
   status=[per_sec(-6, "Cold", "360s")],
   cook=cook_explodes("4.8", 20),
   notes=["4.8m confirmed by the user against the wiki table's 3m - user testing wins."])
v1("Pandora's Lunchbox",
   status=[line(-100, ALL_STATUS), clr("+" + unk(1), WHITE) + " " + icons(ALL_STATUS)],
   state=[], cook=cook_explodes("4.8"),
   notes=["Cooking applies the whole effect to everyone caught in the explosion."])
v1("Remedy Fungus",
   status=[], others=[tok(-30, "Poison"), tok(-30, "Spores"), tok(-45, "Injury")],
   notes=["Corrected: there is no eat-it effect at all. The only way to use it is to throw it, "
          "and the explosion is what heals - so the numbers belong in one line in Others.",
          "TO VERIFY IN GAME: the values themselves are still jkqt's hand-fudged ones - BACKLOG 7."])
v1("Sunscreen",
   status=[], others=[clr("90s", NEUTRAL)],
   state=[], cook=cook_explodes("3"),
   notes=["Heat and shield icons dropped - the duration alone, as asked."])
v1("Ritual Dagger",
   status=[tok(-100, "Hunger"), tok(100, "Extra Stamina"),
           line(-100, ["Poison", "Heat", "Cold", "Drowsy", "Injury", "Spores"])],
   notes=["All six cures collapsed onto one line."])

# --- lanterns ---------------------------------------------------------------
v1("Faerie Lantern",
   others=[line(-5, ["Cold", "Drowsy"], suffix=clr(" / s", NEUTRAL)),
           line(-2.5, ["Heat", "Poison", "Spores", "Injury"], suffix=clr(" / s", NEUTRAL))],
   cook=cook_explodes("4.8"))
v1("Lantern", others=[per_sec(-5, "Cold")], cook=cook_explodes("4.8", 20),
   notes=["TO VERIFY IN GAME: 150 Cold over Lantern.startingFuel seconds - read the real rate."])

# --- climbing and traversal: reach, no prose --------------------------------
for nm, hint in (("Rope Spool", "12.5 m total, up to 10 m per placement"),
                 ("Anti-Rope Spool", "12.5 m total, up to 10 m per placement"),
                 ("Rope Cannon", "RopeShooter.maxLength"),
                 ("Anti-Rope Cannon", "RopeShooter.maxLength"),
                 ("Chain Launcher", "VineShooter.maxLength"),
                 ("Magic Bean", "MagicBean.plantPrefab.maxLength, about 10 m")):
    v1(nm, note=[reach(Q)], state=[],
       notes=["Reach only, no prose. Value from %s." % hint])
v1("Anti-Zooka",
   note=[reach("16")], cook=cook_explodes("32"),
   notes=["16 m diameter in use; cooking detonates it into a 32 m sphere.",
          "TO VERIFY IN GAME: both diameters."])
v1("Rescue Claw", state=[], note=[reach(Q)], cook=cook_plain(-1),
   notes=["TO VERIFY IN GAME: the claw's reach - I could not find it in the assembly, it is a "
          "prefab field.",
          "Cooking makes it unusable."])
v1("Scout Cannon", status=[], note=[], cook=cook_explodes(Q, 50),
   notes=["It only inflicts injury when cooked, so the cooking line carries the whole item."])

# --- amulets ----------------------------------------------------------------
v1("Scout's Ambition",
   status=[clr("8s", NEUTRAL) + " " + glyph(INF) + ic("Extra Stamina"),
           clr("+" + Q, C["Petrify"]) + " " + ic("Petrify")],
   notes=["Same infinite-stamina form as Big Lollipop.",
          "TO VERIFY IN GAME: duration and petrify cost."])
v1("Scout's Initiative",
   status=[clr("+" + Q, C["Petrify"]) + " " + ic("Petrify")],
   notes=["Corrected: it does not grant run or climb speed - it makes you jump and float. "
          "No symbol for that yet, so nothing is claimed."])

# --- mystical ---------------------------------------------------------------
v1("Ancient Idol", note=[glyph(INF) + ic("Shield")],
   notes=["Invincibility for as long as it is held."])
v1("Flare", cook=cook_explodes("4.8", 20))
v1("Portable Stove", note=[clr("60s", NEUTRAL) + " " + ic("Cook")],
   cook=cook_explodes("3", 20))
v1("Cursed Skull",
   note=[clr(unk(), WHITE)], status=[],
   others=[tok(50, "Extra Stamina"), line(-100, all_but())],
   cook=cook_explodes(Q, 52.5),
   notes=["The ??? leads, because the first thing to know is that something drastic happens "
          "to you - it kills you. It sits in its own block above the two lines about everyone "
          "else. Confirmed layout: question marks, blank line, effects on others, blank line, "
          "cooking and weight.",
          "Cooking shows the explosion only. The auto-activation that precedes it is left out "
          "deliberately - too tangled to state in symbols."])
v1("Scoutmaster's Bugle",
   others=[tok(100, "Extra Stamina"), clr(unk(), WHITE)],
   notes=["The ??? sits under the stamina: it summons the Scoutmaster and marks you."])
v1("The Book of Bones", cook=False, notes=["No bonus from cooking."])

# --- signals ----------------------------------------------------------------
v1("Bugle", cook=False, notes=["Cooking only changes its pitch - nothing worth a label."])
v1("Bugle of Friendship",
   others=[glyph(INF) + ic("Extra Stamina"), reach(Q)],
   notes=["Infinite stamina, same form as Big Lollipop, plus how far it carries.",
          "TO VERIFY IN GAME: the radius."])
v1("Scroll", cook=cook_plain(1), notes=["Cooking it has a positive effect."])
v1("The Book of Bones", cook=False,
   notes=["No effect from cooking - this is the 'red book' from review."])

# --- creatures and hazards --------------------------------------------------
v1("Frog", cook=cook_plain(1), notes=["Can be cooked, and becomes good."])
v1("Scorpion",
   status=[clr("50-105", C["Poison"]) + " " + ic("Poison") + clr(" / " + Q + "s", NEUTRAL)],
   cook=False,
   notes=["Simplified - the separate 2.5 instant hit is folded in.",
          "Cooking line dropped: a Scorpion in your hands is already the cooked form."])
v1("Beehive", note=[clr("4", NEUTRAL) + " " + ic("Honeycomb")],
   notes=["Breaking it yields four Honeycombs."])
v1("Blowgun",
   status=[], others=[line(-100, all_but()), tok(120, "Drowsy")],
   state=[])
v1("Dynamite", others=[tok(52.5, "Injury")], cook=cook_explodes(Q, 52.5),
   notes=["Cooking sets it off where it stands - the same explosion it makes anyway."])
v1("Cactus", status=[tok(20, "Thorns")], note=[])
v1("Warp Fungus", note=[],
   notes=["Corrected: it does not warp you to a biome. You throw it and it teleports you to "
          "wherever it lands. Nothing numeric to show, so weight only - confirmed fine."])
