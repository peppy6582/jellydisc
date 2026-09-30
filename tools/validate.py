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
  "bad uuid": mut(lambda d: d.update(menuId="not-a-uuid")),
}
ok = True
for k, v in {"example menu": check(mv, m, True), "example binding": check(bv, b)}.items():
    print(("PASS " if not v else "FAIL ") + k, v or ""); ok &= not v
for k, d in neg.items():
    e = check(mv, d, True); print(("PASS " if e else "FAIL ") + "rejects " + k); ok &= bool(e)
bneg = copy.deepcopy(b); bneg["bindings"]["trailer"] = {"status":"matched"}
e = check(bv, bneg); print(("PASS " if e else "FAIL ") + "rejects matched binding w/o itemId"); ok &= bool(e)
sys.exit(0 if ok else 1)
