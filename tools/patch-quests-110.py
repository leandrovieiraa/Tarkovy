"""Patch bundled quests.json with EFT 1.1.0 / KORD BREACH tasks missing from the SPT dump.

Run: python tools/patch-quests-110.py
"""
from __future__ import annotations

import json
import re
import unicodedata
from copy import deepcopy
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
QUESTS = ROOT / "src" / "Tarkovy" / "Assets" / "quests.json"

# Clone map markers from an existing quest when objectives are identical / reused in 1.1.0.
OBJECTIVES_FROM: dict[str, str] = {
    "small-things-big-help": "the-blood-of-war-part-3",
}

# In-game 1.1.0 titles that replaced older SPT dump names (keep slug for tracking).
RENAMES: list[dict] = [
    {
        "slug": "database-part-2",
        "name": "A Big Loss",
        "namePt": "Uma Grande Perda",
    },
]


def slugify(name: str) -> str:
    s = unicodedata.normalize("NFKD", name)
    s = "".join(c for c in s if not unicodedata.combining(c))
    s = s.lower().replace("&", " and ")
    s = re.sub(r"[^a-z0-9]+", "-", s)
    return s.strip("-")


def norm_name(name: str) -> str:
    s = name.lower().strip()
    s = re.sub(r"\s*\[.*?\]\s*", " ", s)
    s = re.sub(r"\s+", " ", s)
    return s.strip()


def clone_objectives(data: dict, source_slug: str, target_slug: str) -> list:
    for quests in data.values():
        for q in quests:
            if q.get("slug") == source_slug and q.get("objectives"):
                out = []
                for i, o in enumerate(q["objectives"]):
                    item = deepcopy(o)
                    item["id"] = f"{target_slug}-{i}"
                    out.append(item)
                return out
    return []


# maps: one or more map ids; objectivesFrom: optional slug to copy markers
PATCH: list[dict] = [
    # --- Ragman / Woods (user report) ---
    {
        "maps": ["woods"],
        "slug": "small-things-big-help",
        "name": "Small Things, Big Help",
        "namePt": "Coisas Pequenas, Grande Ajuda",
        "trader": "Ragman",
        "objectivesFrom": "the-blood-of-war-part-3",
    },
    # --- KORD BREACH seasonal (list-only on primary map) ---
    {"maps": ["shoreline"], "slug": "uninvited-guests-part-1", "name": "Uninvited Guests - Part 1", "namePt": "Convidados Indesejados - Parte 1", "trader": "Prapor"},
    {"maps": ["woods", "streets-of-tarkov"], "slug": "uninvited-guests-part-2", "name": "Uninvited Guests - Part 2", "namePt": "Convidados Indesejados - Parte 2", "trader": "BTR Driver"},
    {"maps": ["ground-zero"], "slug": "unanswered-calls", "name": "Unanswered Calls", "namePt": "Chamadas Sem Resposta", "trader": "Therapist"},
    {"maps": ["shoreline"], "slug": "cast-the-net", "name": "Cast the Net", "namePt": "Lançar a Rede", "trader": "Prapor"},
    {"maps": ["shoreline"], "slug": "reverse-gear", "name": "Reverse Gear", "namePt": "Marcha à Ré", "trader": "Prapor"},
    {"maps": ["shoreline"], "slug": "know-your-enemy", "name": "Know Your Enemy", "namePt": "Conheça Seu Inimigo", "trader": "Prapor"},
    {"maps": ["shoreline"], "slug": "sheep-in-wolfs-clothing", "name": "Sheep in Wolf's Clothing", "namePt": "Lobo em Pele de Cordeiro", "trader": "Fence"},
    {"maps": ["shoreline"], "slug": "stay-clear-of-blast-zone", "name": "Stay Clear of Blast Zone", "namePt": "Fique Longe da Zona de Explosão", "trader": "Jaeger"},
    {"maps": ["streets-of-tarkov"], "slug": "final-stretch", "name": "Final Stretch", "namePt": "Reta Final", "trader": "Fence"},
    {"maps": ["streets-of-tarkov"], "slug": "consequences-of-our-decisions", "name": "Consequences of Our Decisions", "namePt": "Consequências das Nossas Decisões", "trader": "Mechanic"},
    {"maps": ["the-lab"], "slug": "desperate-assault", "name": "Desperate Assault", "namePt": "Assalto Desesperado", "trader": "Mechanic"},
    {"maps": ["customs", "woods"], "slug": "break-the-chain", "name": "Break the Chain", "namePt": "Quebrar a Corrente", "trader": "Mechanic"},
    {"maps": ["customs"], "slug": "hot-zone", "name": "Hot Zone", "namePt": "Zona Quente", "trader": "Ragman"},
    {"maps": ["lighthouse"], "slug": "harley-forever", "name": "Harley Forever", "namePt": "Harley Para Sempre", "trader": "Ragman"},
    {"maps": ["lighthouse"], "slug": "communication-difficulties", "name": "Communication Difficulties", "namePt": "Dificuldades de Comunicação", "trader": "Ragman"},
    {"maps": ["streets-of-tarkov"], "slug": "out-of-time", "name": "Out of Time", "namePt": "Sem Tempo", "trader": "Ragman"},
    {"maps": ["customs"], "slug": "riding-the-wave", "name": "Riding the Wave", "namePt": "Surfando a Onda", "trader": "Ragman"},
    {"maps": ["ground-zero"], "slug": "the-tarkov-butcher", "name": "The Tarkov Butcher", "namePt": "O Açougueiro de Tarkov", "trader": "Therapist"},
    {"maps": ["shoreline"], "slug": "forced-alliance", "name": "Forced Alliance", "namePt": "Aliança Forçada", "trader": "Mechanic"},
    {"maps": ["streets-of-tarkov"], "slug": "school-guard", "name": "School Guard", "namePt": "Guarda da Escola", "trader": "Prapor"},
    {"maps": ["streets-of-tarkov"], "slug": "secret-message", "name": "Secret Message", "namePt": "Mensagem Secreta", "trader": "Peacekeeper"},
    {"maps": ["streets-of-tarkov"], "slug": "pets-wont-need-it-part-2", "name": "Pets Won't Need It - Part 2", "namePt": "Animais Não Precisam - Parte 2", "trader": "Therapist"},
    {"maps": ["streets-of-tarkov"], "slug": "glory-to-cpsu", "name": "Glory to CPSU", "namePt": "Glória ao CPSU", "trader": "Prapor"},
    {"maps": ["streets-of-tarkov"], "slug": "house-arrest", "name": "House Arrest", "namePt": "Prisão Domiciliar", "trader": "Skier"},
    {"maps": ["woods"], "slug": "hiking", "name": "Hiking", "namePt": "Trilha", "trader": "Peacekeeper"},
    {"maps": ["reserve"], "slug": "demonstration-model", "name": "Demonstration Model", "namePt": "Modelo de Demonstração", "trader": "Peacekeeper"},
    {"maps": ["the-lab"], "slug": "the-huntsman-path-control", "name": "The Huntsman Path - Control", "namePt": "Caminho do Caçador - Controle", "trader": "Jaeger"},
    {"maps": ["woods"], "slug": "inevitable-response", "name": "Inevitable Response", "namePt": "Resposta Inevitável", "trader": "BTR Driver"},
    {"maps": ["shoreline"], "slug": "natural-exchange", "name": "Natural Exchange", "namePt": "Troca Natural", "trader": "BTR Driver"},
    {"maps": ["customs"], "slug": "digital-puzzle", "name": "Digital Puzzle", "namePt": "Quebra-Cabeça Digital", "trader": "Mechanic"},
    {"maps": ["customs"], "slug": "forbidden-knowledge", "name": "Forbidden Knowledge", "namePt": "Conhecimento Proibido", "trader": "Fence"},
    {"maps": ["customs"], "slug": "key-to-understanding", "name": "Key to Understanding", "namePt": "Chave para Entender", "trader": "Fence"},
    {"maps": ["customs"], "slug": "whats-in-the-bag", "name": "What's in the bag?", "namePt": "O que tem na bolsa?", "trader": "Fence"},
    {"maps": ["customs"], "slug": "the-survivalist-path-zatoichi", "name": "The Survivalist Path - Zatoichi", "namePt": "Caminho do Sobrevivente - Zatoichi", "trader": "Jaeger"},
    {"maps": ["customs"], "slug": "fall-ailment", "name": "Fall Ailment", "namePt": "Mal de Outono", "trader": "Therapist"},
    {"maps": ["customs"], "slug": "sanitary-standards-part-2", "name": "Sanitary Standards - Part 2", "namePt": "Padrões Sanitários - Parte 2", "trader": "Therapist"},
    {"maps": ["customs"], "slug": "building-foundations", "name": "Building Foundations", "namePt": "Construindo Fundações", "trader": "BTR Driver"},
    {"maps": ["customs"], "slug": "timeout", "name": "Timeout", "namePt": "Tempo Esgotado", "trader": "Mechanic"},
]


def apply_renames(data: dict) -> int:
    n = 0
    by_slug = {r["slug"]: r for r in RENAMES}
    for quests in data.values():
        for q in quests:
            spec = by_slug.get(q.get("slug") or "")
            if not spec:
                continue
            changed = False
            if spec.get("name") and q.get("name") != spec["name"]:
                q["name"] = spec["name"]
                changed = True
            if spec.get("namePt") and q.get("namePt") != spec["namePt"]:
                q["namePt"] = spec["namePt"]
                changed = True
            if changed:
                n += 1
                print(f"rename {spec['slug']} -> {spec['namePt'] or spec['name']}")
    return n


def merge_patch(data: dict) -> int:
    added = 0
    for entry in PATCH:
        slug = entry.get("slug") or slugify(entry["name"])
        objectives_from = entry.get("objectivesFrom") or OBJECTIVES_FROM.get(slug)
        objectives: list = []
        if objectives_from:
            objectives = clone_objectives(data, objectives_from, slug)

        for map_id in entry["maps"]:
            data.setdefault(map_id, [])
            names = {norm_name(q.get("name") or "") for q in data[map_id]}
            slugs = {q.get("slug", "").lower() for q in data[map_id]}
            if slug in slugs or norm_name(entry["name"]) in names:
                continue
            if entry.get("namePt") and any(q.get("namePt") == entry["namePt"] for q in data[map_id]):
                continue

            data[map_id].append({
                "slug": slug,
                "name": entry["name"],
                "namePt": entry.get("namePt") or "",
                "trader": entry["trader"],
                "traderPt": entry["trader"],
                "objectives": deepcopy(objectives),
            })
            added += 1
            print(f"+ {entry['name']} ({map_id})")

    for lst in data.values():
        lst.sort(key=lambda x: (x.get("name") or "").lower())

    return added


def main() -> None:
    data = json.loads(QUESTS.read_text(encoding="utf-8"))
    before = sum(len(v) for v in data.values())
    renamed = apply_renames(data)
    added = merge_patch(data)
    after = sum(len(v) for v in data.values())
    QUESTS.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"\n=== {renamed} renamed, {added} entries added ({before} -> {after} total) ===")
    print("wrote", QUESTS)

    appdata = Path.home() / "AppData/Roaming/Tarkovy/assets/quests.json"
    if appdata.parent.exists():
        appdata.write_text(QUESTS.read_text(encoding="utf-8"), encoding="utf-8")
        print("updated", appdata)


if __name__ == "__main__":
    main()
