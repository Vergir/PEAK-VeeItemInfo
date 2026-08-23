# -*- coding: utf-8 -*-
"""Emits docs/ITEM-SPEC.html."""
import os, html as H
from urllib.parse import quote
from spec_build import *  # noqa
from spec_build import rows, stats, overlay, gt_cell, items, ORDER_V1
from gen_spec import CATS, C, NEUTRAL, WHITE, POS, NEG, ic, wiki_icon, OUT

CSS = """
:root{
  --bg:#14181d; --panel:#1b2027; --panel2:#20262e; --line:#2c343e;
  --ink:#dfe6ee; --dim:#8d9aa8; --accent:#BFEC1B; --overlay:#0d1014;
}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--ink);
  font:15px/1.55 "Segoe UI",system-ui,sans-serif;}
a{color:var(--accent)}
header{padding:34px 32px 22px;border-bottom:1px solid var(--line);background:var(--panel)}
h1{margin:0 0 6px;font-size:27px;letter-spacing:-.01em}
.sub{color:var(--dim);max-width:80ch}
.wrap{padding:0 32px 80px}
section{margin-top:44px;scroll-margin-top:64px}
h2{font-size:20px;margin:0 0 4px;padding-bottom:7px;border-bottom:2px solid var(--accent);
   display:inline-block}
.cat-desc{color:var(--dim);margin:8px 0 16px;max-width:95ch}
nav{position:sticky;top:0;z-index:20;background:rgba(20,24,29,.96);
  backdrop-filter:blur(8px);border-bottom:1px solid var(--line);
  padding:9px 32px;display:flex;flex-wrap:wrap;gap:6px}
nav a{font-size:12.5px;text-decoration:none;color:var(--dim);border:1px solid var(--line);
  border-radius:20px;padding:3px 11px;white-space:nowrap}
nav a:hover{color:var(--accent);border-color:var(--accent)}
table.items{width:100%;border-collapse:collapse;table-layout:fixed}
table.items th{text-align:left;font-size:11.5px;letter-spacing:.09em;text-transform:uppercase;
  color:var(--dim);font-weight:600;padding:7px 10px;border-bottom:1px solid var(--line)}
table.items td{padding:13px 10px;border-bottom:1px solid var(--line);vertical-align:top;font-size:13.5px}
table.items tr:hover td{background:var(--panel2)}
col.c-icon{width:60px} col.c-name{width:11%} col.c-gt{width:20%}
col.c-v0{width:22%} col.c-v1{width:22%} col.c-gap{width:21%}
img.item{width:52px;height:52px;image-rendering:auto}
img.si{height:1.05em;width:auto;vertical-align:-.18em;margin:0 .05em}
.si.tint{display:inline-block;height:1.05em;width:1.05em;vertical-align:-.18em;margin:0 .05em;
  -webkit-mask-size:contain;mask-size:contain;-webkit-mask-repeat:no-repeat;mask-repeat:no-repeat;
  -webkit-mask-position:center;mask-position:center}
.name{font-weight:600}
.name a{text-decoration:none}
.types{color:var(--dim);font-size:11.5px;margin-top:3px}
.chips{display:flex;flex-wrap:wrap;gap:5px;margin-bottom:7px}
.chip{border:1px solid;border-radius:5px;padding:1px 6px;font-size:12px;
  background:rgba(255,255,255,.03);white-space:nowrap}
ul.gt{margin:0;padding-left:16px;color:var(--dim);font-size:12.5px}
ul.gt li{margin:2px 0}
ul.gaps{margin:0;padding-left:16px;font-size:12.5px}
ul.gaps li{margin:3px 0;color:#f0c9a0}
/* No text-transform: units are authored lowercase (8s, 2.5 m) and words uppercase, and
   forcing the whole box to uppercase was misrepresenting both. */
.ov{background:var(--overlay);border:1px solid var(--line);border-radius:6px;padding:9px 11px;
  font-family:"Consolas",ui-monospace,monospace;font-size:12.5px;line-height:1.65;
  letter-spacing:.02em}
.gap{height:.85em}
.prose{font-style:normal}
.empty{color:#5d6874;font-style:italic;text-transform:none}
.unk{color:#ff8fa3;font-weight:700}
.fallback{color:#ff8fa3;font-weight:600;font-size:.85em;letter-spacing:.06em}
table.ck{border-collapse:collapse;font-size:12px;margin-top:5px}
table.ck th{color:var(--dim);font-weight:500;text-align:right;padding:1px 7px 1px 0;
  white-space:nowrap;border:0;font-size:12px;text-transform:none;letter-spacing:0}
table.ck td{padding:1px 0;border:0;font-size:12px}
.v1cell .ov{border-color:#3f5a2e;background:#0e1410}
th.h1{color:var(--accent)}
ul.gaps li.q{color:#9fd0ff}
.qmark{color:#9fd0ff}
img.si.big{height:1.25em}
.glyph{color:#ffd479;font-weight:400}
.stats{display:flex;flex-wrap:wrap;gap:10px;margin-top:18px}
.stat{background:var(--panel2);border:1px solid var(--line);border-radius:8px;padding:10px 15px}
.stat b{display:block;font-size:23px;color:var(--accent);line-height:1.15}
.stat span{font-size:12px;color:var(--dim)}
.box{background:var(--panel);border:1px solid var(--line);border-left:3px solid var(--accent);
  border-radius:0 8px 8px 0;padding:15px 19px;margin:22px 0;max-width:105ch}
.box h3{margin:0 0 8px;font-size:14.5px;letter-spacing:.04em;text-transform:uppercase;color:var(--accent)}
.box ol,.box ul{margin:6px 0;padding-left:20px}
.box li{margin:4px 0}
.legend{display:flex;flex-wrap:wrap;gap:7px;margin-top:9px}
.legend span{font-size:12px;border:1px solid var(--line);border-radius:5px;padding:2px 8px;
  font-family:Consolas,monospace}
code{background:var(--panel2);padding:1px 5px;border-radius:4px;font-size:12.5px}
.blocks{border-collapse:collapse;margin-top:8px;font-size:13px}
.blocks td,.blocks th{border:1px solid var(--line);padding:5px 10px;text-align:left}
.blocks th{color:var(--dim);font-weight:600;font-size:11.5px;text-transform:uppercase}
"""

def esc(s):
    return H.escape(s)


parts = []
A = parts.append
A("<!doctype html><html lang=en><head><meta charset=utf-8>")
A("<meta name=viewport content='width=device-width,initial-scale=1'>")
A("<title>VeeItemInfo &ndash; Item Display Spec v0</title>")
A("<style>%s</style></head><body>" % CSS)

A("<header><h1>VeeItemInfo &ndash; Item Display Spec</h1>")
A("</header>")

A("<nav>")
for cid, title, _ in CATS:
    A("<a href='#%s'>%s <span style='opacity:.6'>%d</span></a>" % (cid, title, len(rows[cid])))
A("</nav>")

A("<div class=wrap>")

# ---- category tables --------------------------------------------------------
for cid, title, desc in CATS:
    A("<section id=%s><h2>%s</h2>" % (cid, title))
    A("<table class=items><colgroup><col class=c-icon><col class=c-name><col class=c-gt>"
      "<col class=c-v0><col class=c-v1><col class=c-gap></colgroup>")
    A("<tr><th></th><th>Item</th><th>Ground truth (wiki)</th>"
      "<th>jkqt&rsquo;s version</th><th class=h1>v1 &mdash; target</th>"
      "<th>Notes &amp; open questions</th></tr>")
    for it, b, g, b1, n1 in rows[cid]:
        base = it["icon"].replace("64px-", "").replace("_l1b6.webp", "")
        icon = ('<img class=item src="%s" alt="" loading="lazy">' % wiki_icon(base)) if base else ""
        wikiname = it["name"].replace(" ", "_").replace("'", "%27")
        A("<tr><td>%s</td>" % icon)
        A("<td><div class=name><a href='https://peak.wiki.gg/wiki/%s' target=_blank>%s</a></div>"
          "<div class=types>%s</div></td>" % (wikiname, esc(it["name"]), esc(it["types"])))
        A("<td>%s</td>" % gt_cell(it))
        A("<td>%s</td>" % overlay(b))
        A("<td class=v1cell>%s</td>" % overlay(b1, ORDER_V1))
        bullets = ["<li class=q>%s</li>" % esc(x) for x in n1] +                   ["<li>%s</li>" % esc(x) for x in g]
        A("<td>%s</td>" % ("<ul class=gaps>%s</ul>" % "".join(bullets)
                           if bullets else "<span class=empty>&mdash;</span>"))
        A("</tr>")
    A("</table></section>")

A("</div></body></html>")

os.makedirs(os.path.dirname(OUT), exist_ok=True)
open(OUT, "w", encoding="utf-8").write("\n".join(parts))
print("wrote", OUT)
print("items %(total)d | weight-only %(weight_only)d | prose %(prose)d | cookable %(cookable)d" % stats)
for cid, title, _ in CATS:
    print("  %-28s %d" % (title.replace("&amp;", "&"), len(rows[cid])))
