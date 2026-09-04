"""Rebuild Tarkovy Assets/quests.json from SPT quests (complete per-map catalog)
merged with existing marker coordinates from the previous Sayser dump.
"""
from __future__ import annotations

import json
import re
import unicodedata
from pathlib import Path

ROOT = Path(r"E:\Projects\Tarkovy")
SPT = ROOT / "tools" / "_spt"
OLD = ROOT / "src" / "Tarkovy" / "Assets" / "quests.json"
OUT = OLD

LOCATION_TO_MAP = {
    "56f40101d2720b2a4d8b45d6": "customs",  # bigmap
    "55f2d3fd4bdc2d5f408b4567": "factory",
    "59fc81d786f774390775787e": "factory",
    "5704e3c2d2720bac5b8b4567": "woods",
    "5704e554d2720bac5b8b456e": "shoreline",
    "5714dbc024597771384a510d": "interchange",
    "5704e5fad2720bc05b8b4567": "reserve",
    "5704e4dad2720bb55b8b4567": "lighthouse",
    "5714dc692459777137212e12": "streets-of-tarkov",
    "5b0fc42d86f7744a585f9105": "the-lab",
    "6733700029c367a3d40b02af": "the-labyrinth",
    "653e6760052c01c1c805532f": "ground-zero",  # sandbox
    "65b8d6f5cdde2479cb2a3125": "ground-zero",  # sandbox_high
    "5704e5a4d2720bb45b8b4567": "terminal",
}

TRADER_IDS = {
    "54cb50c76803fa8b248b4571": "Prapor",
    "54cb57776803fa99248b456e": "Therapist",
    "579dc571d53a0658a154fbec": "Fence",
    "579dc571d53f634f238b456a": "Fence",
    "58330581ace78e27b8b10cee": "Skier",
    "5935c25fb3acc3127c3d8cd9": "Peacekeeper",
    "5a7c2eca46aef81a7ca2145d": "Mechanic",
    "5ac3b934156ae10c4430e83c": "Ragman",
    "5c0647fdd443bc2504c2d371": "Jaeger",
    "638f541a29ffd1183d187f57": "Lightkeeper",
    "638f541a29ffd1183d0c9141": "Lightkeeper",
    "6617beeaa9cfa777ca915b7c": "Ref",
    "661422d877667532fd605963": "Ref",
    "656f0f98d80a697f855d34b1": "BTR Driver",
}


def slugify(name: str) -> str:
    s = unicodedata.normalize("NFKD", name)
    s = "".join(c for c in s if not unicodedata.combining(c))
    s = s.lower()
    s = s.replace("&", " and ")
    s = re.sub(r"[^a-z0-9]+", "-", s)
    return s.strip("-")


def norm_name(name: str) -> str:
    s = name.lower().strip()
    s = re.sub(r"\s*\[.*?\]\s*", " ", s)
    s = re.sub(r"\s+", " ", s)
    return s.strip()


def main() -> None:
    quests_spt = json.loads((SPT / "quests.json").read_text(encoding="utf-8"))
    en = json.loads((SPT / "en.json").read_text(encoding="utf-8"))
    po = json.loads((SPT / "po.json").read_text(encoding="utf-8"))
    old = json.loads(OLD.read_text(encoding="utf-8")) if OLD.exists() else {}

    # Index old objectives by normalized EN name and by slug
    old_by_name: dict[str, dict] = {}
    old_by_slug: dict[str, dict] = {}
    for map_id, lst in old.items():
        for q in lst:
            old_by_name[norm_name(q.get("name") or "")] = q
            old_by_slug[(q.get("slug") or "").lower()] = q

    out: dict[str, list] = {mid: [] for mid in set(LOCATION_TO_MAP.values()) | set(old.keys())}
    # ensure known maps exist even if empty
    for mid in [
        "customs", "factory", "woods", "shoreline", "interchange", "reserve",
        "lighthouse", "streets-of-tarkov", "the-lab", "the-labyrinth",
        "ground-zero", "terminal",
    ]:
        out.setdefault(mid, [])

    seen: dict[str, set[str]] = {k: set() for k in out}

    for qid, q in quests_spt.items():
        loc = q.get("location")
        map_id = LOCATION_TO_MAP.get(loc)
        if not map_id:
            continue
        name = en.get(f"{qid} name") or q.get("QuestName") or qid
        name_pt = po.get(f"{qid} name") or ""
        trader = TRADER_IDS.get(q.get("traderId") or "", "Unknown")
        slug = slugify(name)
        key = slug or qid

        if key in seen[map_id]:
            continue
        seen[map_id].add(key)

        prev = old_by_slug.get(slug) or old_by_name.get(norm_name(name))
        objectives = []
        if prev and isinstance(prev.get("objectives"), list):
            objectives = prev["objectives"]

        entry = {
            "slug": slug,
            "name": name,
            "namePt": name_pt,
            "trader": trader,
            "traderPt": trader,  # nicknames usually unchanged in PT
            "objectives": objectives,
        }
        out[map_id].append(entry)

    # Live-game / wipe quests missing from SPT templates (manual catalog).
    # Markers optional; list completeness matters for the UI.
    MANUAL = [
        {
            "mapId": "interchange",
            "slug": "fuel-crisis",
            "name": "Fuel Crisis",
            "namePt": "Crise de Combustível",
            "trader": "Ragman",
        },
        {
            # Live PT name; official EN not in SPT dump yet.
            "mapId": "interchange",
            "slug": "desbravador",
            "name": "Desbravador",
            "namePt": "Desbravador",
            "trader": "Ragman",
        },
    ]

    for m in MANUAL:
        mid = m["mapId"]
        out.setdefault(mid, [])
        seen.setdefault(mid, set())
        slug = m["slug"]
        if slug in seen[mid]:
            continue
        # skip if EN name already present
        if any(norm_name(q["name"]) == norm_name(m["name"]) or q.get("namePt") == m["namePt"] for q in out[mid]):
            continue
        seen[mid].add(slug)
        prev = old_by_slug.get(slug) or old_by_name.get(norm_name(m["name"]))
        out[mid].append({
            "slug": slug,
            "name": m["name"],
            "namePt": m["namePt"],
            "trader": m["trader"],
            "traderPt": m["trader"],
            "objectives": (prev or {}).get("objectives") or [],
        })
        print("manual+", m["namePt"], "on", mid)

    # Prefer Blood of War markers as Fuel Crisis waypoints (same map / fuel tanks).
    bow = next((q for q in out.get("interchange", []) if q.get("slug") == "the-blood-of-war-part-1"), None)
    fuel = next((q for q in out.get("interchange", []) if q.get("slug") == "fuel-crisis"), None)
    if bow and fuel and bow.get("objectives") and not fuel.get("objectives"):
        fuel["objectives"] = [
            {**o, "id": f"fuel-crisis-{i}", "description": o.get("description") or "MS2000 fuel tank"}
            for i, o in enumerate(bow["objectives"][:2])
        ]

    # Sort
    for mid, lst in out.items():
        lst.sort(key=lambda x: (x.get("name") or "").lower())

    # Stats
    print("=== counts ===")
    for mid in sorted(out):
        with_obj = sum(1 for q in out[mid] if q.get("objectives"))
        print(f"{mid}: {len(out[mid])} quests ({with_obj} with markers)")

    # Validate interchange highlights
    ic = out.get("interchange", [])
    ic_names = {q["name"] for q in ic}
    ic_pt = {q.get("namePt") for q in ic}
    for need in ["Make ULTRA Great Again", "Big Sale", "Long Line", "Sales Night", "The Stylish One", "Fuel Crisis"]:
        print("has", need, need in ic_names)
    print("has PT Crise", "Crise de Combustível" in ic_pt)
    print("has PT Desbravador", "Desbravador" in ic_pt)
    print("has PT ULTRA", "Tornar o ULTRA bom novamente" in ic_pt)

    # Drop empty terminal if we want parity with maps.json - keep it
    OUT.write_text(json.dumps(out, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("wrote", OUT)

    appdata = Path.home() / "AppData/Roaming/Tarkovy/assets/quests.json"
    if appdata.parent.exists():
        appdata.write_text(OUT.read_text(encoding="utf-8"), encoding="utf-8")
        print("updated appdata")


if __name__ == "__main__":
    main()
