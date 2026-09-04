"""Merge live tarkov.dev tasks into Assets/quests.json.

Adds missing per-map quests and fills empty marker lists from zone coordinates.
Keeps existing names, slugs, and hand-placed markers.

Run: python tools/merge-quests-from-tarkovdev.py
"""
from __future__ import annotations

import json
import re
import unicodedata
import urllib.request
from pathlib import Path
from urllib.parse import unquote

ROOT = Path(__file__).resolve().parents[1]
QUESTS = ROOT / "src" / "Tarkovy" / "Assets" / "quests.json"
APPDATA = Path.home() / "AppData" / "Roaming" / "Tarkovy" / "assets" / "quests.json"

UA = {"User-Agent": "Tarkovy/0.1.49", "Accept": "application/json"}

LOCATION_TO_MAP = {
    "56f40101d2720b2a4d8b45d6": "customs",
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
    "653e6760052c01c1c805532f": "ground-zero",
    "65b8d6f5cdde2479cb2a3125": "ground-zero",
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

ITEM_TYPES = {
    "findItem", "findQuestItem", "giveItem", "giveQuestItem",
    "plantItem", "sellItem", "buildWeapon", "mark",
}


def fetch(url: str):
    req = urllib.request.Request(url, headers=UA)
    with urllib.request.urlopen(req, timeout=90) as r:
        return json.loads(r.read().decode("utf-8"))


def slugify(name: str) -> str:
    s = unicodedata.normalize("NFKD", name)
    s = "".join(c for c in s if not unicodedata.combining(c))
    s = s.lower().replace("&", " and ")
    s = re.sub(r"[^a-z0-9]+", "-", s)
    return s.strip("-")


def norm_name(name: str) -> str:
    s = (name or "").lower().strip()
    s = re.sub(r"\s*\[.*?\]\s*", " ", s)
    s = re.sub(r"\s+", " ", s)
    return s.strip()


def wiki_title(url: str) -> str:
    if not url:
        return ""
    last = unquote(url.rstrip("/").rsplit("/", 1)[-1])
    last = last.replace("_", " ")
    last = re.sub(r"\s+", " ", last).strip()
    return last


def loc_text(table: dict, key: str) -> str:
    if not key:
        return ""
    v = table.get(key) or table.get(key + " name") or table.get(key + " description") or ""
    if not isinstance(v, str):
        return ""
    v = v.strip()
    if not v or v == key or v.endswith(" name") or v.endswith(" description"):
        return ""
    return v


def round_coord(n: float) -> float:
    return float(f"{n:.6g}")


def task_maps(task: dict) -> set[str]:
    """Primary task map plus maps that have visit/mark zones.

    Do not copy a quest onto every map listed on a kill objective (Debut
    is Customs-only even if scav kills are tagged on several maps).
    """
    maps: set[str] = set()
    loc = task.get("map")
    if isinstance(loc, str) and loc in LOCATION_TO_MAP:
        maps.add(LOCATION_TO_MAP[loc])
    for obj in task.get("objectives") or []:
        for zone in obj.get("zones") or []:
            zmap = zone.get("map")
            if zmap in LOCATION_TO_MAP:
                maps.add(LOCATION_TO_MAP[zmap])
    if maps:
        maps.discard("")
        return maps
    for obj in task.get("objectives") or []:
        for mid in obj.get("maps") or []:
            if mid in LOCATION_TO_MAP:
                maps.add(LOCATION_TO_MAP[mid])
    maps.discard("")
    return maps


def objectives_for_map(task: dict, map_id: str, slug: str, en: dict, pt: dict) -> list[dict]:
    out: list[dict] = []
    seen: set[tuple] = set()
    idx = 0
    for obj in task.get("objectives") or []:
        desc = loc_text(en, obj.get("id") or "") or loc_text(en, obj.get("description") or "")
        desc_pt = loc_text(pt, obj.get("id") or "") or loc_text(pt, obj.get("description") or "")
        cat = "item" if (obj.get("type") or "") in ITEM_TYPES else "objective"
        zones = obj.get("zones") or []
        placed = False
        for zone in zones:
            zmap = LOCATION_TO_MAP.get(zone.get("map") or "")
            if zmap != map_id:
                continue
            pos = zone.get("position") or {}
            try:
                x, y, z = float(pos["x"]), float(pos["y"]), float(pos["z"])
            except (KeyError, TypeError, ValueError):
                continue
            key = (round(x, 1), round(y, 1), round(z, 1), desc)
            if key in seen:
                continue
            seen.add(key)
            out.append({
                "id": f"{slug}-{idx}",
                "description": desc or "Objective",
                "descriptionPt": desc_pt or desc or "",
                "category": cat,
                "x": round_coord(x),
                "y": round_coord(y),
                "z": round_coord(z),
            })
            idx += 1
            placed = True
        if placed:
            continue
        # map-listed objective without a zone: skip pin (quest still listed)
    return out


def find_existing(quests: list, slug: str, name: str, name_pt: str):
    n = norm_name(name)
    npt = (name_pt or "").strip()
    for q in quests:
        if (q.get("slug") or "").lower() == slug.lower():
            return q
        if n and norm_name(q.get("name") or "") == n:
            return q
        if npt and (q.get("namePt") or "").strip() == npt:
            return q
    return None


def main() -> None:
    print("Fetching tarkov.dev tasks…")
    bundle = fetch("https://json.tarkov.dev/regular/tasks")
    en = fetch("https://json.tarkov.dev/regular/tasks_en")["data"]
    pt = fetch("https://json.tarkov.dev/regular/tasks_pt")["data"]
    tasks = bundle["data"]["tasks"]
    print(f"  {len(tasks)} live tasks")

    data = json.loads(QUESTS.read_text(encoding="utf-8"))
    unknown: dict[str, int] = {}
    added = 0
    filled = 0
    skipped_nomap = 0

    for task in tasks.values():
        tid = task.get("id") or ""
        name = loc_text(en, tid) or loc_text(en, tid + " name") or wiki_title(task.get("wikiLink") or "")
        if not name:
            name = (task.get("normalizedName") or "").replace("-", " ").title()
        if not name or name.lower() in {"unknown", "???"}:
            continue
        name_pt = loc_text(pt, tid) or loc_text(pt, tid + " name") or ""
        slug = task.get("normalizedName") or slugify(name)
        trader = TRADER_IDS.get(task.get("trader") or "", "Unknown")
        maps = task_maps(task)
        if not maps:
            skipped_nomap += 1
            continue
        loc = task.get("map")
        if isinstance(loc, str) and loc and loc not in LOCATION_TO_MAP:
            unknown[loc] = unknown.get(loc, 0) + 1

        for map_id in sorted(maps):
            data.setdefault(map_id, [])
            existing = find_existing(data[map_id], slug, name, name_pt)
            pins = objectives_for_map(task, map_id, slug if not existing else existing.get("slug") or slug, en, pt)
            if existing:
                if not existing.get("objectives") and pins:
                    existing["objectives"] = pins
                    filled += 1
                if name_pt and not (existing.get("namePt") or "").strip():
                    existing["namePt"] = name_pt
                if trader != "Unknown" and (existing.get("trader") or "Unknown") == "Unknown":
                    existing["trader"] = trader
                    existing["traderPt"] = trader
                continue
            data[map_id].append({
                "slug": slug,
                "name": name,
                "namePt": name_pt,
                "trader": trader,
                "traderPt": trader,
                "objectives": pins,
            })
            added += 1

    for mid, lst in data.items():
        lst.sort(key=lambda q: (q.get("name") or "").lower())

    QUESTS.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    if APPDATA.exists():
        APPDATA.write_text(QUESTS.read_text(encoding="utf-8"), encoding="utf-8")

    print(f"added {added} quest-map rows")
    print(f"filled markers on {filled} existing list-only quests")
    print(f"skipped (no map) {skipped_nomap}")
    if unknown:
        print("unknown location ids", unknown)
    print("counts:")
    total = 0
    with_pin = 0
    for mid in sorted(data):
        n = len(data[mid])
        p = sum(1 for q in data[mid] if q.get("objectives"))
        total += n
        with_pin += p
        print(f"  {mid:22} {n:4}  ({p} with pins)")
    print(f"  {'TOTAL':22} {total:4}  ({with_pin} with pins)")
    print("wrote", QUESTS)


if __name__ == "__main__":
    main()
