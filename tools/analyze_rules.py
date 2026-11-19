#!/usr/bin/env python3
"""Классификация контрольных соотношений ЦБ РФ (УОД ПУРЦБ 6.1.0.10).

Разбирает SQL-файлы valueAssertion_*, извлекает код/критичность/сообщение
 и классифицирует шаблон проверки для последующего перевода в rules.json.
"""
import re
import json
from pathlib import Path
from collections import Counter

ROOT = Path("/home/z/my-project/research/cbr/Rules_DDL/formulas/tab")
OUT = Path("/home/z/my-project/research/cbr/rules_catalog.json")

CATALOG = []

date_cmp = re.compile(r"\(\s*'&par:(\w+)&'::date\s*-\s*(\w+)::date\s*\)\s*(>=|<=|>|<|=)\s*(\d+)")
date_cmp2 = re.compile(r"\(\s*(\w+)::date\s*-\s*'&par:(\w+)&'::date\s*\)\s*(>=|<=|>|<|=)\s*(\d+)")
col_date_cmp = re.compile(r"\(\s*(\w+)::date\s*-\s*(\w+)::date\s*\)\s*(>=|<=|>|<|=)\s*(\d+)")
result_expr = re.compile(r"CASE WHEN\s+(.*?)\s+END AS RESULT", re.S)
notnull = re.compile(r"originalValue_(\w+)\s+is not null", re.I)

for sql_file in sorted(ROOT.rglob("*.SQL")):
    text = sql_file.read_text(encoding="utf-8", errors="replace")
    table = sql_file.parent.name
    code_m = re.search(r"'(valueAssertion_[\w]+)'\s+AS FORMULACODE", text)
    sev_m = re.search(r"'(\w+)'\s+AS FORMULASEVERITY", text)
    msg_m = re.search(r"'((?:[^']|'')+)'\s+AS FORMULAUNSATISFIEDMESSAGE", text)
    if not code_m:
        continue
    code = code_m.group(1)
    severity = sev_m.group(1) if sev_m else ""
    message = msg_m.group(1).replace("''", "'") if msg_m else ""

    res_m = result_expr.search(text)
    res_expr = " ".join(res_m.group(1).split()) if res_m else ""

    # Классификация выражения RESULT
    if re.fullmatch(r"originalValue_\w+\s+is not null", res_expr, re.I):
        kind = "presence"
    elif re.search(r"is not null", res_expr, re.I):
        kind = "presence_multi"
    elif re.search(r"[<>]|!=|=", res_expr):
        kind = "comparison"
    else:
        kind = "other"

    # Предусловия: сравнения дат
    dates = []
    for m in date_cmp.finditer(text):
        dates.append({"left": f"$par:{m.group(1)}", "right": m.group(2), "op": m.group(3), "days": int(m.group(4))})
    for m in date_cmp2.finditer(text):
        dates.append({"left": m.group(1), "right": f"$par:{m.group(2)}", "op": m.group(3), "days": int(m.group(4))})
    for m in col_date_cmp.finditer(text):
        if f"$par:" in m.group(1) or f"$par:" in m.group(2):
            continue
        dates.append({"left": m.group(1), "right": m.group(2), "op": m.group(3), "days": int(m.group(4))})

    # OR-предусловие (какие колонки дают "применимость" правила)
    where_m = re.search(r"WHERE\s*\(\s*(.*?)\s*\)\s+AND", text, re.S)

    CATALOG.append({
        "table": table,
        "code": code,
        "severity": severity,
        "message": message,
        "resultKind": kind,
        "resultExpr": res_expr[:400],
        "dateConditions": dates,
        "file": str(sql_file.relative_to(ROOT)),
    })

print("Всего формул:", len(CATALOG))
print("По типам RESULT:", Counter(c["resultKind"] for c in CATALOG))
print("С датными условиями:", sum(1 for c in CATALOG if c["dateConditions"]))
print("По таблицам:", Counter(c["table"] for c in CATALOG))
OUT.write_text(json.dumps(CATALOG, ensure_ascii=False, indent=1), encoding="utf-8")
print("Каталог сохранён:", OUT)

# Примеры каждого класса
for kindname in ("presence", "presence_multi", "comparison", "other"):
    ex = [c for c in CATALOG if c["resultKind"] == kindname][:2]
    print(f"\n--- {kindname} ---")
    for e in ex:
        print(e["table"], e["code"])
        print("  expr:", e["resultExpr"][:200])
        print("  msg:", e["message"][:150])
        print("  dates:", e["dateConditions"][:3])
