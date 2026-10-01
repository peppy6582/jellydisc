import json, sys, copy, os
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
from jsonschema import Draft202012Validator, FormatChecker

def load(p): return json.load(open(p))
ms, bs = load(os.path.join(ROOT,"schema","menu.schema.json")), load(os.path.join(ROOT,"schema","binding.schema.json"))
Draft202012Validator.check_schema(ms); Draft202012Validator.check_schema(bs)
mv = Draft202012Validator(ms, format_checker=FormatChecker())
bv = Draft202012Validator(bs, format_checker=FormatChecker())

def semantic(m):
    """Cross-reference checks JSON Schema can't express."""
    errs, menus, extras = [], m["menus"], m["extras"]
    if m["root"] not in menus: errs.append(f"root '{m['root']}' not in menus")
    for mk, menu in menus.items():
        for i, e in enumerate(menu["entries"]):
            a = e["action"]
            if a == "playExtra" and e["extra"] not in extras: errs.append(f"{mk}[{i}] unknown extra {e['extra']}")
            if a == "playSequence":
                errs += [f"{mk}[{i}] unknown extra {x}" for x in e["extras"] if x not in extras]
            if a == "submenu" and e["menu"] not in menus: errs.append(f"{mk}[{i}] unknown menu {e['menu']}")
    for mk, menu in menus.items():
        placed = [("position" in e) for e in menu["entries"]]
        flow = menu.get("layout", {}).get("flow") or m.get("layout", {}).get("flow")
        if flow and any(placed):
            errs.append(f"menu '{mk}' uses flow layout, so its entries must not have positions")
        elif any(placed) and not all(placed):
            errs.append(f"menu '{mk}' mixes positioned and unpositioned entries (position all or none)")
    for mk, menu in menus.items():
        fl = menu.get("layout", {}).get("flow") or m.get("layout", {}).get("flow")
        if fl:
            actions = [e["action"] for e in menu["entries"]]
            pinned = ("back" in actions) + ("home" in actions)
            need = max(4, pinned + 3)
            if fl["columns"] * fl["rows"] < need:
                errs.append(f"menu '{mk}' needs a flow grid of at least {need} cells for its pinned Back/Home plus paging buttons")
    for where, lay in [("document", m.get("layout"))] + [(k, mn.get("layout")) for k, mn in menus.items()]:
        fl = (lay or {}).get("flow")
        if fl and fl["columns"] * fl["rows"] < 4:
            errs.append(f"{where} flow needs at least 4 cells (columns x rows) to fit paging buttons")
    seen, stack = set(), [m["root"]]
    while stack:
        k = stack.pop()
        if k in seen or k not in menus: continue
        seen.add(k)
        stack += [e["menu"] for e in menus[k]["entries"] if e["action"] == "submenu"]
    errs += [f"menu '{k}' unreachable from root" for k in menus if k not in seen]
    return errs

def check(validator, doc, sem=False):
    errs = [e.message for e in validator.iter_errors(doc)]
    if sem and not errs: errs = semantic(doc)
    return errs

m, b = load(os.path.join(ROOT,"examples","example.menu.json")), load(os.path.join(ROOT,"examples","example.binding.json"))
def mut(f):
    d = copy.deepcopy(m); f(d); return d
neg = {
  "label with HTML": mut(lambda d: d["menus"]["main"]["entries"][0].update(label="<img onerror=x>")),
  "filename sneaks into extra": mut(lambda d: d["extras"]["trailer"].update(file="t.mkv")),
  "no provider IDs": mut(lambda d: d["match"].update(providerIds={})),
  "tmdb bg without path": mut(lambda d: d.update(background={"source":"tmdb"})),
  "playExtra missing key": mut(lambda d: d["menus"]["main"]["entries"].append({"action":"playExtra","label":"x"})),
  "wrong field for action": mut(lambda d: d["menus"]["main"]["entries"].append({"action":"back","label":"x","menu":"main"})),
  "dangling extra ref": mut(lambda d: d["menus"]["features"]["entries"][0].update(extra="nope")),
  "orphan menu": mut(lambda d: d["menus"].update(orphan={"title":"O","entries":[{"action":"back","label":"Back"}]})),
  "season w/o number": mut(lambda d: d["match"].update(itemType="Season")),
  "position out of range": mut(lambda d: d["menus"]["main"]["entries"][0].update(position={"x":120,"y":10})),
  "position missing y": mut(lambda d: d["menus"]["main"]["entries"][0].update(position={"x":10})),
  "bad anchor": mut(lambda d: d["menus"]["main"]["entries"][0].update(position={"x":1,"y":1,"anchor":"middle"})),
  "unknown button style": mut(lambda d: d["menus"]["main"]["entries"][0].update(style="neon")),
  "image is a file path": mut(lambda d: d["menus"]["main"]["entries"][0].update(image="/mnt/user/art/play.png")),
  "image is http not https": mut(lambda d: d["menus"]["main"]["entries"][0].update(image="http://example.com/a.png")),
  "image url with quote/paren": mut(lambda d: d["menus"]["main"]["entries"][0].update(image='https://example.com/a.png");x:url(')),
  "svg data uri": mut(lambda d: d["menus"]["main"]["entries"][0].update(image="data:image/svg+xml;base64,PHN2Zz48L3N2Zz4=")),
  "mixed positioned/unpositioned": mut(lambda d: d["menus"]["features"]["entries"][0].update(position={"x":10,"y":10})),
  "unknown font": mut(lambda d: d["theme"].update(font="Comic Sans MS")),
  "font stack injection": mut(lambda d: d["theme"].update(font="x;background:url(//evil)")),
  "bad text colour": mut(lambda d: d["theme"].update(textColor="red")),
  "fontSize too big": mut(lambda d: d["theme"].update(fontSize=50)),
  "unknown layer type": mut(lambda d: d["menus"]["main"].update(layout={"layers":[{"type":"script","position":{"x":0,"y":0}}]})),
  "layer without position": mut(lambda d: d["menus"]["main"].update(layout={"layers":[{"type":"panel","fill":"#000000"}]})),
  "panel with image field": mut(lambda d: d["menus"]["main"].update(layout={"layers":[{"type":"panel","position":{"x":0,"y":0},"image":"https://e.com/a.png"}]})),
  "image layer w/ path": mut(lambda d: d["menus"]["main"].update(layout={"layers":[{"type":"image","position":{"x":0,"y":0},"image":"/etc/passwd"}]})),
  "image bg without image": mut(lambda d: d.update(background={"source":"image"})),
  "asset path traversal": mut(lambda d: d["menus"]["main"]["entries"][0].update(image="asset:../../etc/passwd")),
  "asset absolute path": mut(lambda d: d["menus"]["main"]["entries"][0].update(image="asset:/etc/passwd")),
  "asset hidden file": mut(lambda d: d["menus"]["main"]["entries"][0].update(image="asset:.secret/x.png")),
  "asset too deep": mut(lambda d: d["menus"]["main"]["entries"][0].update(image="asset:a/b/c/d/e/f.png")),
  "layer unknown fit": mut(lambda d: d["menus"]["main"].update(layout={"layers":[{"type":"image","position":{"x":0,"y":0},"image":"asset:a/b.webp","fit":"stretchy"}]})),
  "flow with positioned entries": mut(lambda d: (d["menus"]["features"].update(layout={"flow":{"region":{"x":50,"y":50,"w":50,"h":10},"columns":3,"rows":1}}), d["menus"]["features"]["entries"][0].update(position={"x":1,"y":1}))),
  "flow too small": mut(lambda d: d["menus"]["features"].update(layout={"flow":{"region":{"x":50,"y":50,"w":50,"h":10},"columns":3,"rows":1}})),
  "flow region without size": mut(lambda d: d["menus"]["features"].update(layout={"flow":{"region":{"x":50,"y":50},"columns":4,"rows":1}})),
  "flow zero columns": mut(lambda d: d["menus"]["features"].update(layout={"flow":{"region":{"x":50,"y":50,"w":50,"h":10},"columns":0,"rows":5}})),
  "flow html label": mut(lambda d: d["menus"]["features"].update(layout={"flow":{"region":{"x":50,"y":50,"w":50,"h":10},"columns":4,"rows":2,"moreLabel":"<b>More"}})),
  "flow unknown field": mut(lambda d: d["menus"]["features"].update(layout={"flow":{"region":{"x":50,"y":50,"w":50,"h":10},"columns":4,"rows":2,"perPage":3}})),
  "trailerIndex negative": mut(lambda d: d.update(background={"source":"trailer","trailerIndex":-1})),
  "trailerIndex too big": mut(lambda d: d.update(background={"source":"trailer","trailerIndex":99})),
  "poster is a file path": mut(lambda d: d.update(background={"source":"trailer","poster":"/mnt/art/p.png"})),
  "muted not boolean": mut(lambda d: d.update(background={"source":"trailer","muted":"yes"})),
  "home with a menu target": mut(lambda d: d["menus"]["features"]["entries"].append({"action":"home","label":"x","menu":"main"})),
  "back+home need 5 cells": mut(lambda d: (d["menus"]["features"].update(layout={"flow":{"region":{"x":50,"y":50,"w":50,"h":10},"columns":4,"rows":1}}), d["menus"]["features"]["entries"].append({"action":"home","label":"Home"}))),
  "bad uuid": mut(lambda d: d.update(menuId="not-a-uuid")),
}
ok = True
for k, v in {"example menu": check(mv, m, True), "example binding": check(bv, b)}.items():
    print(("PASS " if not v else "FAIL ") + k, v or ""); ok &= not v
for k, d in neg.items():
    e = check(mv, d, True); print(("PASS " if e else "FAIL ") + "rejects " + k); ok &= bool(e)
# Every shipped example menu must pass the schema AND the semantic rules, and every
# asset: image it references must actually be bundled (except art we can't redistribute).
import glob, re
ALLOW_MISSING_ASSETS = {"thor-ragnarok.menu.json"}
ids = {}
def asset_refs(node):
    if isinstance(node, dict):
        for v in node.values(): yield from asset_refs(v)
    elif isinstance(node, list):
        for v in node: yield from asset_refs(v)
    elif isinstance(node, str) and node.startswith("asset:"):
        yield node[6:]
for path in sorted(glob.glob(os.path.join(ROOT, "examples", "*.menu.json"))):
    name = os.path.basename(path); doc = load(path)
    errs = check(mv, doc, True)
    missing = [] if name in ALLOW_MISSING_ASSETS else [a for a in asset_refs(doc) if not os.path.isfile(os.path.join(ROOT, "examples", "assets", a))]
    if missing: errs += [f"asset not bundled: {a}" for a in missing]
    if doc["menuId"] in ids: errs.append(f"menuId duplicates {ids[doc['menuId']]}")
    ids[doc["menuId"]] = name
    print(("PASS " if not errs else "FAIL ") + "example " + name, errs or ""); ok &= not errs
bneg = copy.deepcopy(b); bneg["bindings"]["trailer"] = {"status":"matched"}
e = check(bv, bneg); print(("PASS " if e else "FAIL ") + "rejects matched binding w/o itemId"); ok &= bool(e)
sys.exit(0 if ok else 1)
