# -*- coding: utf-8 -*-
"""Derives the v0 render for every item and writes docs/ITEM-SPEC.html."""
import json, os, re, html as H, collections
from gen_spec import (C, NEUTRAL, WHITE, POS, NEG, UNCOLOURED, ICON, STATUSES, CATS,
                      col, ic, num, tok, plain, txt, categorise, parse_notes,
                      cooked_stages, fnum, SCRATCH, OUT)
from spec_special import SPECIAL, Q, prose, clr
from spec_v1 import V1, cook_polarity, cook_plain, GREEN, RED

items = json.load(open(os.path.join(SCRATCH, "wiki_items.json"), encoding="utf-8"))

# Wiki column order is not the prefab component order the mod actually iterates, so within
# the Status block we present a stable canonical order and say so in the preamble.
CANON = {"Hunger": 0, "Extra Stamina": 1, "Injury": 2, "Poison": 3, "Spores": 4,
         "Cold": 5, "Heat": 6, "Hot": 6, "Drowsy": 7, "Thorns": 8, "Curse": 9}


def derive(it):
    """What ItemDescriptionBuilder would emit, given the wiki's numbers."""
    b = {"note": [], "status": [], "others": [], "state": [], "weight": []}
    spec = SPECIAL.get(it["name"], {})

    w = fnum(it["weight"])
    b["weight"] = [plain(w, "Weight")] if w is not None else []

    changes, delayed, uses, left = parse_notes(it["notes"])

    seen = set()
    ordered = []
    for st, amt in changes:
        if st in seen:
            continue
        seen.add(st)
        ordered.append((st, amt))

    h, s, p = fnum(it["hunger"]), fnum(it["stamina"]), fnum(it["poison"])
    if h is not None and h != 0 and "Hunger" not in seen:
        ordered.append(("Hunger", h)); seen.add("Hunger")
    if s is not None and s != 0 and "Extra Stamina" not in seen:
        ordered.append(("Extra Stamina", s)); seen.add("Extra Stamina")
    if p is not None and p != 0 and "Poison" not in seen and not delayed:
        ordered.append(("Poison", p)); seen.add("Poison")

    ordered.sort(key=lambda x: CANON.get(x[0], 50))
    b["status"] = [tok(a, st) for st, a in ordered if a != 0]

    if delayed:
        d, amt, dur = delayed
        b["status"].append(prose("AFTER %ss," % num(d)) + " " + tok(amt, "Poison") + txt(" / %ss" % num(dur)))
    if uses:
        b["state"].append(prose("%d USES" % uses))

    for k in ("note", "status", "others", "state"):
        if k in spec:
            b[k] = spec[k]
    return b, left


def derive_v1(it, b0):
    """v1 global rules, then per-item overrides.

    Global, as agreed in review: no 'AFTER Ns' prefix, no USES label, no trailing commas,
    and a cooking +/- line directly above weight.
    """
    b = {k: list(v) for k, v in b0.items()}
    ov = V1.get(it["name"], {})

    b["status"] = [re.sub(r'<span class="prose"[^>]*>AFTER [^<]*</span>\s*', "", x)
                   for x in b["status"]]
    b["state"] = []                                   # uses labels dropped everywhere
    b["others"] = [x.rstrip(",") for x in b["others"]]
    b["status"] = [x.rstrip(",") for x in b["status"]]

    for k in ("note", "status", "others", "state"):
        if k in ov:
            b[k] = list(ov[k])

    if "cook" in ov:
        cl = ov["cook"] or None          # False omits the line entirely
    else:
        cl = cook_plain(cook_polarity(it))
    # Cooking sits directly above weight, in the same block, so nothing separates them.
    b["weight"] = ([cl] if cl else []) + b["weight"]
    return b, ov.get("notes", [])


ORDER = ["note", "status", "others", "state", "weight"]
ORDER_V1 = ["note", "status", "others", "state", "weight"]


def overlay(b, order=None):
    """DescriptionLayout.Render - blocks in order, blank line between them."""
    chunks = []
    for k in (order or ORDER):
        if b[k]:
            chunks.append("<br>".join(b[k]))
    if not chunks:
        return '<span class="empty">(nothing)</span>'
    return '<div class="ov">' + '<div class="gap"></div>'.join(chunks) + '</div>'


def gt_cell(it):
    """Ground truth from the wiki."""
    chips = []
    for label, key, st in (("", "weight", "Weight"), ("", "hunger", "Hunger"),
                           ("", "stamina", "Extra Stamina"), ("", "poison", "Poison")):
        v = fnum(it[key])
        if v is None:
            continue
        chips.append('<span class="chip" style="border-color:%s">%s&nbsp;%s</span>'
                     % (col(st), ic(st), num(v)))
    out = '<div class="chips">%s</div>' % "".join(chips)
    notes = [n.strip() for n in it["notes"].split("|") if n.strip()]
    if notes:
        out += "<ul class=gt>" + "".join("<li>%s</li>" % fmt_note(n) for n in notes) + "</ul>"
    return out


def fmt_note(n):
    """Turn the wiki's {Status} placeholders back into inline icons."""
    def rep(m):
        st = m.group(1).strip()
        st = "Extra Stamina" if st == "Bonus stamina" else st
        return ic(st) + " " if st in ICON else ""
    return re.sub(r"\{([A-Za-z0-9' -]*)\}\s*", rep, H.escape(n)).replace("&#x27;", "'")


def cook_cell(it):
    h, s = fnum(it["hunger"]), fnum(it["stamina"])
    ck = it["cooking"]
    parts = []
    hard = ck.lower().rstrip(".")
    if hard in ("cannot be cooked",):
        return '<span class="empty">cannot be cooked</span>'
    if hard in ("immediately incinerated", "pops", "immediately explodes", "explodes", "no effect"):
        parts.append('<div class="ck-hard">%s</div>' % H.escape(ck))
    elif ck:
        parts.append('<div class="ck-hard">%s</div>' % fmt_note(ck))

    st = cooked_stages(h, s)
    if st and (h is not None or s is not None):
        rows = []
        for stage, label in ((1, "&times;1&ndash;2"), (3, "&times;3"), (4, "&times;4"), (5, "&times;5")):
            hh, ss, pp = st[stage]
            cells = []
            if hh is not None and abs(hh) > 0.01:
                cells.append(tok(hh, "Hunger"))
            if ss > 0.01:
                cells.append(tok(ss, "Extra Stamina"))
            if pp > 0.01:
                cells.append(tok(pp, "Poison"))
            if not cells:
                cells = ['<span class="empty">nothing</span>']
            rows.append('<tr><th>%s</th><td>%s</td></tr>' % (label, " ".join(cells)))
        parts.append('<table class="ck">%s</table>' % "".join(rows))
    if not parts:
        return '<span class="empty">&mdash;</span>'
    return "".join(parts)


# ---- gaps -------------------------------------------------------------------
def gaps_for(it, b, left):
    g = list(SPECIAL.get(it["name"], {}).get("gaps", []))
    body = " ".join(b["note"] + b["status"] + b["others"] + b["state"])

    only_weight = not any(b[k] for k in ("note", "status", "others", "state"))
    if only_weight and not g:
        g.append("Shows weight only.")

    if 'class="prose"' in body and not any("prose" in x.lower() for x in g):
        g.append("Contains untranslatable English prose.")
    for n in left:
        if len(n) > 3 and not n.lower().startswith(("source", "found", "part of", "can be used for",
                                                    "does not spawn", "cannot spawn", "much rarer", "eating it will",
                                                    "produces a", "required for")):
            g.append("Not shown: %s" % n.rstrip("."))
    return g


# ---- build ------------------------------------------------------------------
rows = collections.defaultdict(list)
stats = collections.Counter()
for it in items:
    b, left = derive(it)
    g = gaps_for(it, b, left)
    b1, notes1 = derive_v1(it, b)
    cat = categorise(it)
    rows[cat].append((it, b, g, b1, notes1))
    stats["total"] += 1
    if not any(b[k] for k in ("note", "status", "others", "state")):
        stats["weight_only"] += 1
    if 'class="prose"' in " ".join(b["note"] + b["status"] + b["others"] + b["state"]):
        stats["prose"] += 1
    if any("No cooking indicator" in x for x in g):
        stats["cookable"] += 1
    if notes1:
        stats["open"] += len(notes1)
    if it["name"] in V1:
        stats["v1"] += 1

for cat in rows:
    rows[cat].sort(key=lambda r: r[0]["name"])
