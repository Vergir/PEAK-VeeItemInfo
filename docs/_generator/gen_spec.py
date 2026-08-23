# -*- coding: utf-8 -*-
"""Generates docs/ITEM-SPEC.html from the wiki extract plus the mod's own formatting rules.

The v0 column is *derived*: it applies the rules in ItemDescriptionBuilder / EffectFormatter
to the wiki's ground-truth numbers. It is not a screenshot of the running mod. Anything the
derivation could not settle is flagged in the Gaps column rather than guessed.
"""
import json, re, os, html as H, collections

SCRATCH = os.path.dirname(os.path.abspath(__file__))
REPO = r"C:\Users\vergir\home\code\csharp\peak\PEAK-VeeItemInfo"
OUT = os.path.join(REPO, "docs", "ITEM-SPEC.html")

# ---- colours, straight out of EffectColors.cs -------------------------------
C = {
 "Hunger": "#FFBD16", "Extra Stamina": "#BFEC1B", "Injury": "#FF5300", "Crab": "#E13542",
 "Poison": "#A139FF", "Cold": "#00BCFF", "Heat": "#C80918", "Hot": "#C80918",
 "Sleepy": "#FF5CA4", "Drowsy": "#FF5CA4", "Curse": "#1B0043", "Weight": "#A65A1C",
 "Thorns": "#768E00", "Shield": "#D48E00",
 "Spores": "#A55B63", "Web": "#CCCCCC", "Arrow": "#CCCCCC",
 "Petrify": "#CCCCCC", "FlyTrap": "#CCCCCC", "Numb": "#C08A8A", "Cook": "#E8722A",
}
NEUTRAL, WHITE, POS, NEG = "#CCCCCC", "#FFFFFF", "#DDFFDD", "#FFCCCC"
UNCOLOURED = {"Web", "Arrow", "Petrify", "FlyTrap"}

# Icons are hotlinked from peak.wiki.gg rather than copied into this repository, so no
# PEAK artwork lives here and docs/ can be published.
#
# Two kinds, matching the mod's own rule (StatusIcons.ShouldTint): the wiki's status icons
# are white silhouettes and get tinted to their status colour via a CSS mask, exactly as the
# mod's tint=1 sprite tags do. Anything whose art already carries its own colours is drawn
# as a plain image and left alone.
WIKI_THUMB = "https://peak.wiki.gg/images/thumb/%s.png/64px-%s.png"

ICON = {
 "Hunger": "Status_Hunger", "Extra Stamina": "Status_Bonus_stamina", "Injury": "Status_Injury",
 "Poison": "Status_Poison", "Cold": "Status_Cold", "Heat": "Status_Heat", "Hot": "Status_Heat",
 "Drowsy": "Status_Drowsy", "Sleepy": "Status_Drowsy", "Spores": "Status_Spores",
 "Thorns": "Status_Thorns", "Weight": "Status_Weight", "Curse": "Status_Curse",
 "Petrify": "Status_Petrify", "Shield": "Status_Invincibility",

 # Already coloured on the wiki, so never tinted.
 "Numb": "Status_Numb",
 # The wiki has no campfire status icon. The stove stands in here; the mod itself scrapes
 # the real campfire off StaminaBar at runtime.
 "Cook": "Portable_Stove",
 "Dynamite": "Dynamite", "Honeycomb": "Honeycomb", "HalfCoconut": "Half-Coconut",
}

# Icons whose own art is coloured. Everything else is a silhouette and gets tinted.
FULL_COLOUR = {"Numb", "Cook", "Dynamite", "Honeycomb", "HalfCoconut"}
STATUSES = ["Hunger", "Extra Stamina", "Injury", "Poison", "Cold", "Heat", "Hot", "Drowsy",
            "Spores", "Thorns", "Weight", "Curse", "Petrify", "Shield", "Numb"]

# What "clear all status" actually clears, for the compact one-line form.
ALL_STATUS = ["Hunger", "Injury", "Poison", "Spores", "Cold", "Heat", "Drowsy", "Thorns"]


def col(s):
    return C.get(s, NEUTRAL)


def wiki_icon(file_name):
    """The wiki's 64px thumbnail for a file, apostrophes escaped for a URL."""
    safe = file_name.replace("'", "%27")
    return WIKI_THUMB % (safe, safe)


def ic(s):
    """Inline status icon, or a text fallback where the game has no icon at all."""
    f = ICON.get(s)
    if not f:
        return '<b class="fallback">%s</b>' % s.upper()

    url = wiki_icon(f)
    if s in FULL_COLOUR:
        return '<img class="si" src="%s" alt="%s" loading="lazy">' % (url, s)

    # A CSS mask over a solid background reproduces the mod's tint=1 exactly: the silhouette
    # decides the shape, the status colour fills it.
    return ('<span class="si tint" style="-webkit-mask-image:url(%s);mask-image:url(%s);'
            'background-color:%s" title="%s"></span>' % (url, url, col(s), s))


def icons(names, sep=" "):
    """A run of icons, space-separated - the compact 'all statuses' form."""
    return sep.join(ic(n) for n in names)


def num(v):
    return re.sub(r"\.0$", "", "%.1f" % v)


def tok(amount, status):
    """EffectFormatter.Token - signed, coloured, icon inside the colour span."""
    sign = "+" if amount > 0 else "-"
    return '<span style="color:%s">%s%s %s</span>' % (col(status), sign, num(abs(amount)), ic(status))


def plain(amount, status):
    return '<span style="color:%s">%s %s</span>' % (col(status), num(amount), ic(status))


def txt(s, c=NEUTRAL):
    return '<span style="color:%s">%s</span>' % (c, H.escape(s))


# ---- categories -------------------------------------------------------------
CATS = [
 ("natural",   "Natural Food",
  "Berries, mushrooms and anything foraged. Cooking rewrites their numbers, so this is where a cooking indicator matters most."),
 ("packaged",  "Packaged Food",
  "Manufactured food from the crash site and campfires. Fixed effects: hunger plus at most one status."),
 ("medicine",  "Medicine &amp; Cures",
  "Items whose whole purpose is removing a status. These are the mod at its best - pure numbers, no prose."),
 ("traversal", "Climbing &amp; Traversal",
  "Ropes, anchors, platforms and anything that moves you. Almost entirely English prose today, and the largest single obstacle to the no-translation goal."),
 ("amulets",   "Amulets",
  "The Scout&rsquo;s amulets and the Strange Gem. Petrify-cost items, implemented recently and still unverified in game."),
 ("mystical",  "Other Mystical Items",
  "Mystical items that are not amulets."),
 ("light",     "Light &amp; Fire",
  "Sources of light and heat. Lantern and Faerie Lantern project status fields onto nearby players, which is why they render in the Others block."),
 ("signals",   "Signals &amp; Navigation",
  "Instruments, compasses and readables. Almost none carry a status effect, so the mod has almost nothing to say about them."),
 ("weapons",   "Explosives &amp; Hazards",
  "Things that hurt somebody."),
 ("storage",   "Storage",
  "Containers. Their value is slot count, which the mod cannot currently see."),
 ("creatures", "Creatures",
  "Live and cooked animals."),
 ("junk",      "Toys &amp; Junk",
  "No mechanical effect. The mod shows weight and nothing else - correct, but worth confirming that really is all there is."),
]

FORCE = {}


def _f(cat, *names):
    for n in names:
        FORCE[n] = cat


_f("amulets", "Scout's Ambition", "Scout's Tenacity", "Scout's Generosity",
   "Scout's Initiative", "Scout's Honor", "Strange Gem")
_f("medicine", "Aloe Vera", "Antidote", "Bandages", "First Aid Kit", "Heat Pack",
   "Remedy Fungus", "Sunscreen", "Cure-All", "Pandora's Lunchbox", "Ritual Dagger")
_f("traversal", "Balloon", "Balloon Bunch", "Glider", "Jetpack", "Parasol", "Rescue Claw",
   "Warp Fungus", "Scout Cannon", "Checkpoint Flag", "Anti-Zooka")
_f("mystical", "Ancient Idol", "Cursed Skull", "Scout Effigy", "The Book of Bones",
   "Scoutmaster's Bugle", "Faerie Lantern")
_f("light", "Lantern", "Torch", "Candlestick", "Flare")
_f("signals", "Bugle", "Bugle of Friendship", "Conch", "Compass", "Pirate's Compass",
   "Binoculars", "Guidebook", "Passport", "Bing Bong", "Scroll")
_f("weapons", "Dynamite", "Blowgun", "Cactus", "Snowball", "Beehive")
_f("storage", "Backpack", "Fanny Pack")
_f("creatures", "Frog", "Scorpion", "Tick", "Cooked Bird")


def categorise(it):
    if it["name"] in FORCE:
        return FORCE[it["name"]]
    t = it["types"]
    if "Berries" in t or "Mushrooms" in t or "Natural food" in t:
        return "natural"
    if "Packaged food" in t:
        return "packaged"
    if "Enemies" in t:
        return "creatures"
    if "Deployables" in t:
        return "traversal"
    if "Misc" in t:
        return "junk"
    if "Mystical" in t:
        return "mystical"
    if "Consumables" in t:
        return "medicine"
    if "Equipment" in t:
        return "signals"
    if "Food" in t:
        return "natural"
    return "junk"


# ---- parsing the wiki notes into status changes ------------------------------
PAIR = re.compile(r"\{([A-Za-z ]+)\}\s*([\d.]+)")
DELAY = re.compile(r"After ([\d.]+) seconds?, inflicts \{Poison\} ([\d.]+) Poison over ([\d.]+) seconds?")
USES = re.compile(r"Can be used (\d+) times?")


def fnum(s):
    try:
        return float(s)
    except (TypeError, ValueError):
        return None


def parse_notes(notes):
    """-> (status changes, delayed poison, uses, sentences we could not turn into numbers)"""
    changes, delayed, uses, left = [], None, None, []
    for sent in [s.strip() for s in notes.split("|") if s.strip()]:
        m = DELAY.search(sent)
        if m:
            delayed = (fnum(m.group(1)), fnum(m.group(2)), fnum(m.group(3)))
            continue
        m = USES.search(sent)
        if m:
            uses = int(m.group(1))
            continue
        low = sent.lower()
        if low.startswith(("removes", "heals", "cleanses", "prevents consumption")):
            sign = -1
        elif low.startswith(("inflicts", "adds", "applies", "grants", "gives", "user and")):
            sign = +1
        else:
            left.append(sent)
            continue
        found = False
        for st, amt in PAIR.findall(sent):
            st = st.strip()
            if st == "Bonus stamina":
                st = "Extra Stamina"
            if st not in STATUSES:
                continue
            v = fnum(amt)
            if v is None:
                continue
            changes.append((st, sign * v))
            found = True
        if not found:
            left.append(sent)
    return changes, delayed, uses, left


# ---- cooking ----------------------------------------------------------------
def cooked_stages(hunger, stamina):
    """ItemCooking.ChangeStatsCooked applied cumulatively. Returns display units."""
    if hunger is None and stamina is None:
        return None
    rest = abs(hunger) / 100.0 if hunger is not None else None
    stam = (stamina or 0.0) / 100.0
    out = {}
    for total in range(1, 6):
        if rest is not None:
            if total < 2:
                rest = rest * 2
            elif total > 2:
                rest = max(rest - 0.05, 0.0)
        if total < 2:
            stam = max(0.1, stam * 1.5)
        elif total > 2:
            stam = 0.0
        poison = 0.1 * (total - 3) if total >= 4 else 0.0
        out[total] = (None if rest is None else -rest * 100, stam * 100, poison * 100)
    return out
