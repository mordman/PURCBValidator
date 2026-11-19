#!/usr/bin/env python3
"""Экстрактор справочников из таксономии ЦБ РФ УОД ПУРЦБ 6.1.0.10.

Извлекает:
  taxonomy_labels.json  - русские метки концептов/членов/доменов/таблиц
  taxonomy_enums.json   - домены перечислений (enum / enum2)
  taxonomy_facets.json  - фасеты XSD (pattern, totalDigits, ...)
  taxonomy_tables.json  - названия таблиц и их колонок (concept -> columnId)
Результат кладётся в /home/z/my-project/research/cbr/taxonomy_dict/
"""
import json
import re
import xml.etree.ElementTree as ET
from pathlib import Path

TAX = Path("/home/z/my-project/research/cbr/Taxonomy/final_6_1_0_10/www.cbr.ru/xbrl")
OUT = Path("/home/z/my-project/research/cbr/taxonomy_dict")
OUT.mkdir(parents=True, exist_ok=True)

XLINK = "http://www.w3.org/1999/xlink"
LINK = "http://www.xbrl.org/2003/linkbase"
XSD = "http://www.w3.org/2001/XMLSchema"
ROLE_LABEL = "http://www.xbrl.org/2003/role/label"

# --- Префиксы по файлам (targetNamespace) ---
TARGET_NS = {}
def load_target_ns(xsd_path: Path):
    root = ET.parse(xsd_path).getroot()
    tns = root.get("targetNamespace", "")
    TARGET_NS[str(xsd_path.resolve())] = tns
    return tns

load_target_ns(TAX / "udr/dom/mem-int.xsd")
load_target_ns(TAX / "nso/purcb/dic/purcb-dic.xsd")
load_target_ns(TAX / "udr/dim/dim-int.xsd")

KNOWN_FILES = {  # файл -> qname префикс для значений enum1
    "mem-int.xsd": "mem-int",
    "purcb-dic.xsd": "purcb-dic",
    "dim-int.xsd": "dim-int",
}

def href_to_qname(href: str):
    """'../../../udr/dom/mem-int.xsd#mem-int_X' -> ('mem-int:X', file, localname)"""
    m = re.match(r"^(?:.*/)?([^/]+?\.xsd)#(.+)$", href)
    if not m:
        return None, None, None
    fname, elem_id = m.group(1), m.group(2)
    localname = elem_id
    # id у purcb-dic: purcb-dic_X -> X; у mem-int: mem-int_X -> X
    for pref in ("purcb-dic_", "mem-int_", "dim-int_"):
        if elem_id.startswith(pref):
            localname = elem_id[len(pref):]
            break
    prefix = KNOWN_FILES.get(fname)
    qname = f"{prefix}:{localname}" if prefix else localname
    return qname, fname, elem_id

def parse_label_linkbase(path: Path):
    """Возвращает {qname: label} для role=label, lang=ru (или первый)."""
    tree = ET.parse(path)
    root = tree.getroot()
    labels = {}
    label_text = {}
    for el in root.iter(f"{{{LINK}}}label"):
        if el.get(f"{{{XLINK}}}role") == ROLE_LABEL:
            label_text[el.get(f"{{{XLINK}}}label")] = (el.text or "").strip()
    for arc in root.iter(f"{{{LINK}}}labelArc"):
        to = arc.get(f"{{{XLINK}}}to")
        frm = arc.get(f"{{{XLINK}}}from")
        if to in label_text and frm:
            labels[frm] = label_text[to]
    # loc: label -> qname
    result = {}
    for loc in root.iter(f"{{{LINK}}}loc"):
        lbl = loc.get(f"{{{XLINK}}}label")
        href = loc.get(f"{{{XLINK}}}href", "")
        qname, _, _ = href_to_qname(href.split("#")[0] and href)
        if qname and lbl in labels:
            result[qname] = labels[lbl]
    return result

print("Извлечение меток...")
labels = {}
# Метки концептов purcb-dic + членов mem-int + доменов dim-int
for lb in [TAX / "nso/purcb/dic/purcb-dic-label.xml",
           TAX / "udr/dom/mem-int-label.xml",
           TAX / "udr/dim/dim-int-label.xml"]:
    if lb.exists():
        got = parse_label_linkbase(lb)
        print(f"  {lb.name}: {len(got)} меток")
        labels.update(got)

# Названия таблиц из сообщений контрольных соотношений ЦБ ("Раздел N. ...")
table_titles = {}
rules_cat = Path("/home/z/my-project/research/cbr/rules_catalog.json")
if rules_cat.exists():
    for r in json.loads(rules_cat.read_text(encoding="utf-8")):
        t = r.get("table", "")
        msg = r.get("message", "")
        if t and msg and t not in table_titles:
            # первый сегмент до первой точки длиной 5-120 символов
            seg = msg.split(". ")[0]
            if 5 <= len(seg) <= 120:
                table_titles[t] = seg
# дополнение: сообщения existenceAssertion для sved_purcb
svd_dir = Path("/home/z/my-project/research/cbr/Rules_DDL/formulas/tab/sr_sved_purcb")
if svd_dir.exists() and "sr_sved_purcb" not in table_titles:
    for f in svd_dir.glob("*.SQL"):
        m = re.search(r"'([^']{10,200})'\s+AS FORMULAUNSATISFIEDMESSAGE", f.read_text(encoding="utf-8", errors="replace"))
        if m:
            table_titles["sr_sved_purcb"] = m.group(1).split(". ")[0][:120]
            break
print("  таблиц:", len(table_titles))

# --- Концепты из purcb-dic.xsd: типы, фасеты, enum-домены ---
print("Извлечение концептов и фасетов...")
concepts = {}
enum_link = {}  # concept -> domain QName
facets = {}
xsd_root = ET.parse(TAX / "nso/purcb/dic/purcb-dic.xsd").getroot()
ENUM_NS = "http://xbrl.org/2014/extensible-enumerations"
for el in xsd_root.iter(f"{{{XSD}}}element"):
    cid = el.get("id") or ""
    name = el.get("name") or ""
    if not cid.startswith("purcb-dic_"):
        continue
    info = {
        "name": name,
        "qname": f"purcb-dic:{name}",
        "type": el.get("type", ""),
        "periodType": el.get(f"{{{LINK}}}periodType") or el.get("xbrli:periodType", ""),
        "label": labels.get(f"purcb-dic:{name}", ""),
    }
    dom = el.get(f"{{{ENUM_NS}}}domain")
    if dom:
        enum_link[f"purcb-dic:{name}"] = dom
        info["enumDomain"] = dom
        info["enumLinkrole"] = el.get(f"{{{ENUM_NS}}}linkrole", "")
    # фасеты в сложном типе
    restr = el.find(f".//{{{XSD}}}restriction")
    if restr is not None:
        fdict = {}
        for f in restr:
            tag = f.tag.split("}")[1]
            val = f.get("value")
            if val is not None:
                fdict[tag] = val
        if fdict:
            facets[f"purcb-dic:{name}"] = fdict
            info["facets"] = fdict
    concepts[f"purcb-dic:{name}"] = info
print(f"  концептов: {len(concepts)}, enum-концептов: {len(enum_link)}, с фасетами: {len(facets)}")

# --- Домены перечислений из purcb-dic-definition.xml ---
print("Извлечение доменов перечислений...")
def parse_definition_domains(path: Path):
    root = ET.parse(path).getroot()
    locs = {}
    for loc in root.iter(f"{{{LINK}}}loc"):
        lbl = loc.get(f"{{{XLINK}}}label")
        href = loc.get(f"{{{XLINK}}}href", "")
        qname, _, _ = href_to_qname(href)
        locs[lbl] = (qname, href)
    arcs = []
    for arc in root.iter(f"{{{LINK}}}definitionArc"):
        arcs.append((arc.get(f"{{{XLINK}}}from"), arc.get(f"{{{XLINK}}}to"),
                     arc.get(f"{{{XLINK}}}arcrole", "")))
    return locs, arcs

enums = {}
if (TAX / "nso/purcb/dic/purcb-dic-definition.xml").exists():
    locs, arcs = parse_definition_domains(TAX / "nso/purcb/dic/purcb-dic-definition.xml")
    # domain-member: from = домен (header), to = член
    dom_members = {}
    for frm, to, ar in arcs:
        if ar.endswith("domain-member") and frm in locs and to in locs:
            dom_qname = locs[frm][0]
            member_qname, member_href = locs[to]
            dom_members.setdefault(dom_qname, []).append((member_qname, member_href))
    # сопоставить концепты с их доменами
    for concept_qname, dom in enum_link.items():
        members = dom_members.get(dom, [])
        mlist = []
        for mq, mhref in members:
            # значение в CSV: enum1 -> "mem-int:X"; enum2 -> полный URI
            is_enum2 = concept_qname.endswith("2")
            if is_enum2:
                fname = re.match(r"^(?:.*/)?([^/]+?\.xsd)#", mhref)
                tns = TARGET_NS.get(str((TAX / "udr/dom" / ("mem-int.xsd" if "mem-int" in mhref else "nso/purcb/dic/purcb-dic.xsd")).resolve()), "")
                # для enum2 значение - URI#LocalName
                local = mq.split(":")[-1]
                ns = "http://www.cbr.ru/xbrl/udr/dom/mem-int" if "mem-int" in mhref else "http://www.cbr.ru/xbrl/nso/purcb/dic"
                mlist.append({"value": f"{ns}#{local}", "label": labels.get(mq, "")})
            else:
                mlist.append({"value": mq, "label": labels.get(mq, "")})
        enums[concept_qname] = {
            "domain": dom,
            "members": mlist,
        }
print(f"  доменов: {len(enums)}; всего членов: {sum(len(v['members']) for v in enums.values())}")

# --- Типизированные домены (dim-int) ---
print("Извлечение типизированных доменов...")
typed_domains = {}
dim_root = ET.parse(TAX / "udr/dim/dim-int.xsd").getroot()
for el in dim_root.iter(f"{{{XSD}}}element"):
    name = el.get("name", "")
    if name:
        typed_domains[f"dim-int:{name}"] = {
            "label": labels.get(f"dim-int:{name}", ""),
            "typed": el.get("xbrldi:typed", "") == "true" or "Typed" in name,
        }

# --- Таблицы: колонки из mapping-схем (для человекочитаемых имён) ---
print("Формирование справочника таблиц...")
mapping_schema_dir = Path("/home/z/my-project/research/cbr/Schema/www.cbr.ru/xbrl_csv/20260403/20260430/tab/nso/purcb")
tables_info = {}
for td in sorted(mapping_schema_dir.glob("sr_*.def.json")):
    tname = td.stem.replace(".def", "")
    tables_info[tname] = {"title": table_titles.get(tname, ""), "schemaFile": td.name}

result = {
    "labels": labels,
    "concepts": concepts,
    "enums": enums,
    "facets": facets,
    "typedDomains": typed_domains,
    "tables": tables_info,
    "tableTitles": table_titles,
}
(OUT / "taxonomy_dict.json").write_text(json.dumps(result, ensure_ascii=False, indent=1), encoding="utf-8")
print(f"Сохранено: {OUT / 'taxonomy_dict.json'}")

# Статистика и примеры
print("\nПримеры меток:")
for k in list(labels)[:5]:
    print(f"  {k} = {labels[k][:70]}")
print("\nПримеры enum-доменов:")
for k in list(enums)[:5]:
    print(f"  {k}: домен {enums[k]['domain']}, членов {len(enums[k]['members'])}, пример: {enums[k]['members'][:2]}")
print("\nПримеры фасетов:")
for k in list(facets)[:6]:
    print(f"  {k}: {facets[k]}")
print("\nТаблицы:")
for t, v in list(tables_info.items()):
    print(f"  {t}: {v['title'][:80]}")
