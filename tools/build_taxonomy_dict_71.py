#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Сборка taxonomy_dict.json для таксономии 7.1 (ПУРЦБ 10-РД, набор 2025-12-30/20251230).

Источники — официальная публикация Банка России (https://www.cbr.ru):
  * «Материалы по спецификации XBRL-CSV» — xcsv_draft_7.1_20251230.zip
    (draft-схемы служебного файла; задают область концептов точки входа
    ep_nso_purcb_m_q_y_10rd_reestr_0420417);
  * «Финальная таксономия XBRL Банка России (версия 7.1)» — final_7_1.zip
    (XSD-словари purcb-dic/nfo-dic/mem-int/dim-int, линки меток и определений).

Перед запуском распакуйте архивы:
  final_7_1.zip        -> <источники>/final_7_1/...
  xcsv_draft_7.1.zip   -> <источники>/schemas/<набор>/...

Выход: src/PurcbValidator.Core/Resources/7.1/taxonomy_dict.json в том же
формате, что и словарь УОД 6.1.0.10 (labels/concepts/enums/facets/
typedDomains/tables/tableTitles).

Запуск из корня репозитория:
  python3 tools/build_taxonomy_dict_71.py --sources /путь/к/распакованным/архивам
"""
import argparse
import json
import os
import re
import xml.etree.ElementTree as ET

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
XLINK = '{http://www.w3.org/1999/xlink}'

# файл -> префикс пространства имён
XSD_MAP = {
    'purcb-dic.xsd': 'purcb-dic',
    'nfo-dic.xsd': 'nfo-dic',
    'mem-int.xsd': 'mem-int',
    'dim-int.xsd': 'dim-int',
}
LABEL_MAP = {
    'purcb-dic-label.xml': ('purcb-dic.xsd', 'purcb-dic'),
    'nfo-dic-label.xml': ('nfo-dic.xsd', 'nfo-dic'),
    'mem-int-label.xml': ('mem-int.xsd', 'mem-int'),
    'dim-int-label.xml': ('dim-int.xsd', 'dim-int'),
}


def local(name):
    return name.rsplit('}', 1)[-1]


def find_unique(root_dir, rel_name):
    for dirpath, _dirs, files in os.walk(root_dir):
        if rel_name in files:
            return os.path.join(dirpath, rel_name)
    raise FileNotFoundError(rel_name)


def parse_xsd(path, prefix):
    """{id элемента: qname, тип, домен перечисления, период, абстрактность}."""
    elements = {}
    for el in ET.parse(path).getroot().iter():
        if local(el.tag) != 'element' or el.get('name') is None or el.get('id') is None:
            continue
        rec = {'qname': f'{prefix}:{el.get("name")}', 'name': el.get('name'), 'type': '',
               'enumDomain': '', 'periodType': '', 'abstract': (el.get('abstract') or '').lower() == 'true',
               'label': ''}
        for k, v in el.attrib.items():
            lk = local(k)
            if lk == 'type' and ':' not in k:
                rec['type'] = v
            elif lk == 'domain':
                rec['enumDomain'] = v
            elif lk == 'periodType':
                rec['periodType'] = v
        elements[el.get('id')] = rec
    return elements


def parse_labels(path):
    """{id элемента: (текст метки, роль)} по link:loc/label/labelArc."""
    root = ET.parse(path).getroot()
    loc_id, label_text, out = {}, {}, {}
    for el in root.iter():
        lt = local(el.tag)
        if lt == 'loc':
            href = el.get(f'{XLINK}href') or ''
            if '#' in href:
                loc_id[el.get(f'{XLINK}label') or ''] = href.split('#', 1)[1]
        elif lt == 'label':
            label_text[el.get(f'{XLINK}label') or ''] = ((el.text or '').strip(), el.get(f'{XLINK}role') or 'standard')
        elif lt == 'labelArc':
            src, dst = loc_id.get(el.get(f'{XLINK}from') or ''), label_text.get(el.get(f'{XLINK}to') or '')
            if src and dst:
                prev = out.get(src)
                if prev is None or (prev[1] != 'standard' and dst[1] == 'standard'):
                    out[src] = dst
    return out


def parse_domains(path, by_id):
    """{qname домена: [qname членов]} по definitionArc domain-member."""
    domains = {}
    for link in ET.parse(path).getroot().iter():
        if local(link.tag) != 'definitionLink':
            continue
        loc = {}
        for el in link:
            lt = local(el.tag)
            if lt == 'loc':
                href = el.get(f'{XLINK}href') or ''
                if '#' in href and href.split('#', 1)[1] in by_id:
                    loc[el.get(f'{XLINK}label') or ''] = by_id[href.split('#', 1)[1]]['qname']
            elif lt == 'definitionArc' and (el.get(f'{XLINK}arcrole') or '').endswith('domain-member'):
                frm, to = loc.get(el.get(f'{XLINK}from') or ''), loc.get(el.get(f'{XLINK}to') or '')
                if frm and to:
                    domains.setdefault(frm, []).append(to)
    return domains


def draft_concepts(draft_root):
    """Концепты, оси и типизированные домены из draft-схем 7.1."""
    concepts, dims, typed = set(), set(), set()
    for dirpath, _dirs, files in os.walk(draft_root):
        for fn in files:
            if not fn.endswith('.json') or fn in ('ep_matching_list.json', 'xbrl-csv.json'):
                continue
            try:
                d = json.load(open(os.path.join(dirpath, fn), encoding='utf-8'))
            except Exception:
                continue
            for name, ddef in d.get('definitions', {}).items():
                if name.startswith('conceptName_'):
                    try:
                        concepts.add(ddef['properties']['xbrl:concept']['const'])
                    except Exception:
                        concepts.add(name[len('conceptName_'):].replace('_', ':', 1))
            for m in re.finditer(r'"dim-int:([A-Za-z0-9_]+)"', json.dumps(d.get('definitions', {}))):
                qn = 'dim-int:' + m.group(1)
                dims.add(qn)
                if m.group(1).endswith('TypedName'):
                    typed.add(qn)
    return concepts, dims, typed


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--sources', required=True,
                    help='каталог с распакованными архивами ЦБ (final_7_1/, schemas/)')
    ap.add_argument('--out', default=os.path.join(
        REPO, 'src/PurcbValidator.Core/Resources/7.1/taxonomy_dict.json'))
    args = ap.parse_args()

    by_id = {}
    for xsd, prefix in XSD_MAP.items():
        by_id.update(parse_xsd(find_unique(args.sources, xsd), prefix))
    print(f'xsd: элементов {len(by_id)}')

    labels_by_id = {}
    for lab in LABEL_MAP:
        labels_by_id.update(parse_labels(find_unique(args.sources, lab)))
    for eid, (text, _role) in labels_by_id.items():
        if eid in by_id:
            by_id[eid]['label'] = text
    print(f'linkbase: меток {len(labels_by_id)}')

    domain_members = {}
    for defs in ('purcb-dic-definition.xml', 'nfo-dic-definition.xml'):
        domain_members.update(parse_domains(find_unique(args.sources, defs), by_id))
    print(f'linkbase: доменов с членами {len(domain_members)}')

    # draft-схемы: схема точки входа 10-РД ( *.def.ep.json в наборе 2025-12-30 )
    draft_root = None
    for dirpath, _dirs, files in os.walk(args.sources):
        if 'ep_nso_purcb_m_q_y_10rd_reestr_0420417.def.ep.json' in files:
            draft_root = dirpath
            break
    if draft_root is None:
        raise SystemExit('не найдена draft-схема точки входа 10-РД (ep_nso_purcb_m_q_y_10rd_reestr_0420417.def.ep.json)')
    concepts, dims, typed = draft_concepts(draft_root)
    print(f'draft-схемы: концептов {len(concepts)}, осей {len(dims)}')

    by_qname = {r['qname']: r for r in by_id.values()}
    labels, out_concepts, enums = {}, {}, {}
    seen_members = set()
    for qn in sorted(concepts):
        rec = by_qname.get(qn)
        if rec is None:
            continue
        labels[qn] = rec['label']
        if not rec['abstract']:
            out_concepts[qn] = {'name': rec['name'], 'qname': qn, 'type': rec['type'],
                                 'periodType': rec['periodType'], 'label': rec['label']}
        if rec['enumDomain']:
            domain = rec['enumDomain']
            drec = by_qname.get(domain)
            if drec:
                labels.setdefault(domain, drec['label'])
                out_concepts.setdefault(domain, {'name': drec['name'], 'qname': domain,
                                                 'type': drec['type'], 'periodType': drec['periodType'],
                                                 'label': drec['label']})
            members = []
            for member_qn in domain_members.get(domain, []):
                mrec = by_qname.get(member_qn)
                members.append({'value': member_qn, 'label': mrec['label'] if mrec else ''})
                seen_members.add(member_qn)
            enums[qn] = {'domain': domain, 'members': members}
    for member_qn in seen_members:
        mrec = by_qname.get(member_qn)
        if mrec and mrec['label']:
            labels[member_qn] = mrec['label']

    typed_domains = {}
    for qn in sorted(dims):
        rec = by_qname.get(qn)
        if rec is None:
            continue
        if rec['label']:
            labels[qn] = rec['label']
        if qn in typed:
            typed_domains[qn] = {'label': rec['label'], 'typed': True}

    tables = {k: {'title': k, 'schemaFile': f'{k}_types.def_ep_nso_purcb_m_q_y_10rd_reestr_0420417.def.ep.json'}
              for k in ('SR_0420417', 'SR_0420459', 'SR_INFO', 'SR_Sved_ObOtchOrg_OtvLicza')}
    tables['sr_soprovod'] = {
        'title': 'sr_soprovod',
        'schemaFile': 'sr_soprovod_types.def_ep_nso_purcb_m_q_y_10rd_reestr_0420417.def.ep.json'}

    result = {
        'labels': dict(sorted(labels.items())),
        'concepts': dict(sorted(out_concepts.items())),
        'enums': dict(sorted(enums.items())),
        'facets': {},
        'typedDomains': dict(sorted(typed_domains.items())),
        'tables': tables,
        'tableTitles': {k: v['title'] for k, v in tables.items()},
    }
    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    with open(args.out, 'w', encoding='utf-8') as f:
        json.dump(result, f, ensure_ascii=False, indent=1)
    total_members = sum(len(e['members']) for e in enums.values())
    print(f'ГОТОВО: {args.out}')
    print(f"  концептов {len(out_concepts)}, меток {len(labels)}, "
          f"перечислений {len(enums)} ({total_members} членов), осей {len(typed_domains)}")


if __name__ == '__main__':
    main()
