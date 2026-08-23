# -*- coding: utf-8 -*-
"""Items the mod handles through a dedicated branch.

These reproduce each branch's own output, prose and all. "?" marks a number that lives in
a prefab field the wiki does not publish - it renders correctly in game, we simply cannot
print its value here without an in-game dump.
"""
from gen_spec import C, NEUTRAL, WHITE, POS, NEG, ic, tok, txt

Q = '<b class="unk">?</b>'


def prose(s, c=NEUTRAL):
    return '<span class="prose" style="color:%s">%s</span>' % (c, s)


def clr(s, c):
    return '<span style="color:%s">%s</span>' % (c, s)


SPECIAL = {}


def sp(name, **kw):
    SPECIAL[name] = kw


# --- climbing and traversal: the mod's prose problem, concentrated -----------
sp("Rope Spool",
   note=[prose("PLACE A ROPE<br>FROM %s m LONG, UP TO %s m LONG" % (Q, Q))],
   state=[prose("%s m LEFT" % Q) + txt("  (hidden unless Force Update Time <= 1)", "#7a8894")],
   gaps=["Pure prose: PLACE A ROPE / FROM / UP TO / LONG / LEFT all need translating.",
         "Remaining length is poll-gated and normally hidden - BACKLOG 4."])
sp("Anti-Rope Spool",
   note=[prose("PLACE A ROPE THAT FLOATS UP<br>FROM %s m LONG, UP TO %s m LONG" % (Q, Q))],
   state=[prose("%s m LEFT" % Q)],
   gaps=["Pure prose.",
         "Weight is -2.5 and renders as a bare '-2.5' - nothing distinguishes 'lighter' from 'costs weight'."])
sp("Rope Cannon",
   note=[prose("SHOOT A ROPE ANCHOR WHICH PLACES<br>A ROPE THAT DROPS DOWN %s m" % Q)],
   gaps=["Pure prose."])
sp("Anti-Rope Cannon",
   note=[prose("SHOOT A ROPE ANCHOR WHICH PLACES<br>A ROPE THAT FLOATS UP %s m" % Q)],
   gaps=["Pure prose."])
sp("Chain Launcher",
   note=[prose("SHOOT A CHAIN THAT CONNECTS FROM<br>YOUR POSITION TO WHERE YOU SHOOT<br>UP TO %s m AWAY" % Q)],
   gaps=["Three lines of pure prose - the worst offender in the mod."])
sp("Magic Bean",
   note=[prose("%s TO PLANT A VINE THAT GROWS<br>PERPENDICULAR TO TERRAIN UP TO<br>%s m OR UNTIL IT HITS SOMETHING"
               % (clr("THROW", C["Hunger"]), Q))],
   gaps=["Pure prose.",
         "THROW is coloured Hunger-orange, which implies a hunger effect it does not have."])
sp("Warp Fungus",
   note=[prose("WARP TO %s" % Q)],
   gaps=["Prose, plus a biome name that is itself untranslated English."])
sp("Portable Stove",
   note=[prose("PLACE A %s STOVE FOR 60s" % clr("COOKING", C["Injury"]))],
   gaps=["Prose.", "COOKING is drawn in the Injury colour, so it reads as damage."])
sp("Scout Cannon",
   status=[tok(50, "Injury")],
   gaps=["Shows the 50 Injury but nothing about the cannon itself - that it launches you is the point."])
sp("Checkpoint Flag",
   gaps=["Weight only. The single-use respawn that also restores your statuses has no component the mod reads."])
sp("Rescue Claw", state=[prose("3 USES")], gaps=["Grapple behaviour not shown at all."])
sp("Balloon", gaps=["Weight (-5) only. -18% gravity for 2 minutes is not shown.",
                    "A bare '-5' does not distinguish 'makes you lighter' from 'costs you weight'."])
sp("Balloon Bunch", gaps=["Weight (-15) only. -54% gravity is not shown."])
sp("Glider", gaps=["Weight only."])
sp("Jetpack", gaps=["Weight only."])
sp("Parasol", gaps=["Weight only. Mesa sun protection and slow-fall are both invisible to the mod."])
sp("Bounce Fungus", gaps=["Weight only. Creates a bouncy platform when thrown - not shown."])
sp("Cloud Fungus", gaps=["Weight only. Creates a platform - not shown."])
sp("Shelf Fungus", gaps=["Weight only. Creates a platform - not shown.",
                         "Note the class name ShelfShroom is used by Remedy Fungus, not this item."])
sp("Piton", gaps=["Weight only. Rest point that regenerates stamina - not shown."])
sp("Anti-Zooka", gaps=["Wiki row carries no effect data. Needs an in-game [item] dump."])

# --- explosives and hazards --------------------------------------------------
sp("Dynamite",
   status=[],
   others=[prose("%s FOR UP TO %s<br>%s"
                 % (clr("EXPLODES", C["Injury"]), clr("52.5 INJURY", C["Injury"]),
                    clr("ADDITIONAL DAMAGE TAKEN IF HELD", NEUTRAL)))],
   gaps=["Prose wrapped round a number.",
         "Spells INJURY in words when the injury icon is already available."])
sp("Cactus",
   note=[prose("%s TO YOUR BODY<br>CAN %s BY USING<br>AT LEAST %s%% POWER"
               % (clr("STICKS", C["Thorns"]), clr("THROW", C["Hunger"]), Q))],
   status=[tok(20, "Thorns")],
   gaps=["Pure prose."])
sp("Blowgun",
   status=[],
   others=[prose("SHOOT A DART THAT WILL APPLY:"),
           prose("%s EXCEPT %s," % (clr("CLEAR ALL STATUS", POS), clr("CURSE", C["Curse"]))),
           tok(120, "Drowsy")],
   state=[prose("3 USES")],
   gaps=["Header is prose; CLEAR ALL STATUS is prose.",
         "CURSE is drawn in #1B0043 - near-black on a dark overlay, effectively invisible."])
sp("Beehive", gaps=["Weight only. Breaking it yields 4x Honeycomb and angers bees - neither is shown."])
sp("Snowball", gaps=["Weight only."])

# --- light and fire ----------------------------------------------------------
sp("Lantern",
   status=[],
   others=[prose("WHEN LIT, NEARBY PLAYERS RECEIVE:"), tok(-150, "Cold") + clr(" / %s s" % Q, NEUTRAL)],
   gaps=["Header is prose."])
sp("Faerie Lantern",
   status=[],
   others=[prose("WHEN LIT, NEARBY PLAYERS RECEIVE:"),
           tok(-75, "Heat") + txt(" / 30s") + ",",
           tok(-150, "Cold") + txt(" / 30s") + ",",
           tok(-75, "Poison") + txt(" / 30s") + ",",
           tok(-75, "Spores") + txt(" / 30s") + ",",
           tok(-150, "Drowsy") + txt(" / 30s") + ",",
           tok(-75, "Injury") + txt(" / 30s")],
   gaps=["Header is prose.",
         "Seven lines plus weight - the tallest overlay in the game. Worth compressing to a shared '/30s'."])
sp("Torch",
   gaps=["Shows weight only. The Lantern branch matches 'Torch(Clone)' purely to suppress the "
         "header, and no StatusField path is read, so it emits nothing."])
sp("Candlestick", gaps=["Weight only."])
sp("Flare", gaps=["Weight only. The smoke column and helicopter summon at the PEAK are not shown."])

# --- medicine ----------------------------------------------------------------
sp("Remedy Fungus",
   status=[tok(-30, "Poison"), tok(-45, "Injury"), tok(-30, "Spores")],
   others=[prose("%s TO RELEASE GAS THAT WILL:" % clr("THROW", C["Hunger"])),
           tok(-17.5, "Injury"),
           tok(-27.5, "Poison") + txt(" / 11s")],
   gaps=["Header is prose.",
         "The area numbers still carry jkqt's 'incorrect?' comment and are hand-fudged - BACKLOG 7.",
         "Only one of the two poison AOEs is rendered; the second is skipped by hand."])
sp("Sunscreen",
   others=[prose("SPRAY A 90s MIST THAT APPLIES:"),
           prose("PREVENT %s IN MESA&rsquo;S SUN FOR 90s" % clr("HEAT", C["Heat"]))],
   state=[prose("3 USES")],
   gaps=["Two lines of prose, one naming a biome."])
sp("Pandora's Lunchbox",
   status=[prose("%s, THEN RANDOMIZE<br>%s, %s, %s,<br>%s, %s, %s, %s"
                 % (clr("CLEAR ALL STATUS", POS), clr("HUNGER", C["Hunger"]),
                    clr("EXTRA STAMINA", C["Extra Stamina"]), clr("INJURY", C["Injury"]),
                    clr("POISON", C["Poison"]), clr("COLD", C["Cold"]),
                    clr("HEAT", C["Heat"]), clr("DROWSY", C["Drowsy"])))],
   state=[prose("3 USES")],
   gaps=["Three lines of prose naming seven statuses that all already have icons."])

# --- food with afflictions ---------------------------------------------------
sp("Napberry",
   status=[tok(-100, "Hunger"), tok(100, "Drowsy"),
           prose("%s EXCEPT %s" % (clr("CLEAR ALL STATUS", POS), clr("CURSE", C["Curse"])))],
   gaps=["CLEAR ALL STATUS is prose.", "CURSE is invisible on the dark overlay."])
sp("Energy Drink",
   status=[tok(-30, "Heat"), tok(25, "Drowsy"),
           prose("%s %s s OF %s OR<br>%s %s s OF %s<br>AFTERWARDS, %s %s"
                 % (clr("GAIN", POS), Q, clr("%s%% BONUS RUN SPEED" % Q, C["Extra Stamina"]),
                    clr("GAIN", POS), Q, clr("%s%% BONUS CLIMB SPEED" % Q, C["Extra Stamina"]),
                    clr("GAIN", NEG), clr("%s DROWSY" % Q, C["Drowsy"])))],
   gaps=["Three lines of prose out of EffectFormatter.Affliction - BACKLOG 3.",
         "The run-versus-climb distinction has no icon and cannot survive translation."])
sp("Big Lollipop",
   status=[tok(-5, "Hunger"),
           prose("%s %s s OF %s OR<br>%s %s s OF %s"
                 % (clr("GAIN", POS), Q, clr("INFINITE RUN STAMINA", C["Extra Stamina"]),
                    clr("GAIN", POS), Q, clr("INFINITE CLIMB STAMINA", C["Extra Stamina"])))],
   gaps=["Prose. Needs the shared infinite-stamina design - BACKLOG 1b."])
sp("Mandrake",
   gaps=["Hiding your stamina bar for 60s when eaten raw is not shown - and it is the entire reason to cook it first.",
         "The scream that drowses nearby players is not shown."])
sp("Scorpion",
   status=["%s + %s / %s s" % (clr("2.5 %s" % ic("Poison"), C["Poison"]),
                               clr("50-105 %s" % ic("Poison"), C["Poison"]), Q)],
   gaps=["The 50-105 range varies with your current total status - correct, but nothing explains it.",
         "Moves continuously and rides the 1s poll, so it can be a second stale - BACKLOG 5."])

# --- social / mass effects ---------------------------------------------------
sp("Cursed Skull",
   others=[prose("NEARBY PLAYERS WILL RECEIVE:"),
           tok(50, "Extra Stamina") + ",",
           prose("%s EXCEPT %s" % (clr("CLEAR ALL STATUS", POS), clr("CURSE", C["Curse"])))],
   gaps=["Header is prose.",
         "Nothing states that using it kills you. That is the most important fact about the item."])
sp("Bugle of Friendship",
   others=[prose("%s %s %s" % (clr("NEARBY PLAYERS", NEUTRAL), clr("GAIN", POS),
                               clr("%s EXTRA STAMINA" % Q, C["Extra Stamina"])))],
   gaps=["Prose.",
         "Wiki describes infinite stamina for up to 7.5s, which is not what Action_MoraleBoost models - reconcile against the prefab."])
sp("Scoutmaster's Bugle",
   status=[],
   others=[prose("%s %s %s" % (clr("NEARBY PLAYERS", NEUTRAL), clr("GAIN", POS),
                               clr("100 EXTRA STAMINA", C["Extra Stamina"])))],
   gaps=["Prose.",
         "Nothing says it summons the Scoutmaster and marks you as his target - a fatal omission, literally."])

# --- amulets -----------------------------------------------------------------
sp("Scout's Tenacity",
   status=[clr("-60 ", WHITE) + (" %s " % clr("/", WHITE)).join(
               ic(s) for s in ["Injury", "Spores", "Poison", "Cold", "Hot", "Drowsy"]),
           "%s s %s" % (Q, clr(ic("Shield"), C["Shield"])),
           clr("+%s-%s %s" % (Q, Q, ic("Petrify")), C["Petrify"])],
   gaps=["-60 is a budget shared across all six statuses, not 60 off each - nothing conveys that.",
         "Shield has no scraped icon, so it renders as the literal text SHIELD - BACKLOG 1.",
         "Petrify has neither colour nor icon: grey text reading PETRIFY."])
sp("Scout's Generosity",
   note=['<b class="fallback">ITEM</b> &rarr; <b class="fallback">ITEM</b> <b class="fallback">ITEM</b>'],
   status=[clr("+%s/%s %s" % (Q, Q, ic("Petrify")), C["Petrify"])],
   gaps=["Uses BingBong's icon as a stand-in for 'an item'; falls back to the text ITEM if the lookup fails - BACKLOG 1.4.",
         "The arrow is U+2192 and it is unverified that PEAK's HUD font has the glyph - BACKLOG 1.3."])
sp("Scout's Ambition",
   status=[prose("%s %s s OF %s OR<br>%s %s s OF %s"
                 % (clr("GAIN", POS), Q, clr("INFINITE RUN STAMINA", C["Extra Stamina"]),
                    clr("GAIN", POS), Q, clr("INFINITE CLIMB STAMINA", C["Extra Stamina"]))),
           clr("+%s %s" % (Q, ic("Petrify")), C["Petrify"])],
   gaps=["Prose. Shares the infinite-stamina problem with Big Lollipop - BACKLOG 1b.",
         "Whole branch is unverified in game - BACKLOG 1.1."])
sp("Scout's Initiative",
   status=[prose("%s %s s OF %s OR<br>%s %s s OF %s"
                 % (clr("GAIN", POS), Q, clr("%s%% BONUS RUN SPEED" % Q, C["Extra Stamina"]),
                    clr("GAIN", POS), Q, clr("%s%% BONUS CLIMB SPEED" % Q, C["Extra Stamina"]))),
           clr("+%s %s" % (Q, ic("Petrify")), C["Petrify"])],
   gaps=["Prose.", "Unverified in game - BACKLOG 1.1."])
sp("Scout's Honor",
   gaps=["Wiki row carries weight and nothing else. Component unknown - needs an in-game [item] dump."])
sp("Strange Gem",
   gaps=["Wiki row carries no effect data. Component unknown - needs an in-game [item] dump."])

# --- mystical ----------------------------------------------------------------
sp("Ancient Idol",
   gaps=["Weight (40 - the heaviest item in the game) and nothing else.",
         "Invincibility while held and the bonus-stamina freeze are invisible to the mod: neither is an Action_ component."])
sp("Scout Effigy", gaps=["Weight only. Resurrecting a dead scout is not shown."])
sp("The Book of Bones",
   status=[tok(25, "Curse")],
   gaps=["The one line it prints is in #1B0043 - unreadable on the dark overlay."])
sp("Ritual Dagger",
   gaps=["Wiki lists -100 across six statuses plus -100 hunger and +100 stamina. "
         "Verify whether this is a shared budget like Tenacity's heal-all, in which case the display is misleading."])

# --- storage, navigation, junk ----------------------------------------------
sp("Backpack", gaps=["Weight only. Four extra slots is the entire point of the item and is not shown."])
sp("Fanny Pack", gaps=["Weight only. Slot count not shown."])
sp("Binoculars", gaps=["Weight only - correct, it has no status effect."])
sp("Compass", gaps=["Weight only - correct."])
sp("Pirate's Compass", gaps=["Weight only. Points to unopened luggage; arguably worth a note."])
sp("Guidebook", gaps=["Weight only - correct."])
sp("Passport", gaps=["Weight only. Negative weight (-2.5)."])
sp("Bugle", gaps=["Weight only - correct.",
                  "Cooking modulates its pitch, which the missing cooking indicator would cover."])
sp("Conch", gaps=["Weight only."])
sp("Bing Bong", gaps=["Weight only, which is right - but cooking changes its voice and that is not shown either."])
sp("Scroll", gaps=["Weight only."])
sp("Frog", gaps=["Weight only."])
sp("Cooked Bird", gaps=["Wiki lists no hunger value, which looks like a wiki gap rather than a mod gap - verify in game."])
sp("Tick", gaps=["Consuming it does not inflict poison, unlike every other creature - worth surfacing."])
