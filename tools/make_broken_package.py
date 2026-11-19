#!/usr/bin/env python3
"""Генерирует samples/broken_package (копия эталона ЦБ с внесёнными ошибками)
и samples/broken_json (mapping.json с синтаксической ошибкой) для негативных тестов.
"""
import json
import shutil
from pathlib import Path

SRC = Path("/home/z/my-project/PurcbValidator/samples/example_package")
DST = Path("/home/z/my-project/PurcbValidator/samples/broken_package")
DST_JSON = Path("/home/z/my-project/PurcbValidator/samples/broken_json")

if DST.exists():
    shutil.rmtree(DST)
if DST_JSON.exists():
    shutil.rmtree(DST_JSON)
shutil.copytree(SRC, DST)
DST_JSON.mkdir(parents=True)

# ------------------------------------------------------- mapping.json: правки
mpath = DST / "mapping.json"
mapping = json.loads(mpath.read_text(encoding="utf-8"))

for t in mapping["tables"]:
    # 1) sr_R1: заявлено 3 строки, фактически 5 -> CSV-020
    if t["uri"] == "sr_R1.csv":
        t["csvRowsCount"] = 3
    # 6) sr_R4: дублируем строку данных и согласовно поднимаем счётчик
    if t["uri"] == "sr_R4.csv":
        t["csvRowsCount"] = 6

mpath.write_text(json.dumps(mapping, ensure_ascii=False, indent=1), encoding="utf-8")

# ------------------------------------------------------- sr_R2.csv: колонка
p = DST / "sr_R2.csv"
lines = p.read_text(encoding="utf-8").split("\n")
head = lines[0]
# переименуем первую колонку заголовка -> CSV-011 (лишняя) + CSV-010 (пропавшая)
head_cols = head.split("|")
head_cols[0] = '"neizvestnaya_kolonka"'
lines[0] = "|".join(head_cols)
p.write_text("\n".join(lines), encoding="utf-8")

# ------------------------------------------------------- sr_R3.csv: тип даты
p = DST / "sr_R3.csv"
text = p.read_text(encoding="utf-8")
# в первой строке данных заменим первую встреченную ISO-дату на мусор -> CSV-060
import re
m = re.search(r'"\d{4}-\d{2}-\d{2}"', text)
if m:
    text = text[:m.start()] + '"31.12.2025-НЕ-ДАТА"' + text[m.end():]
    p.write_text(text, encoding="utf-8")

# ------------------------------------------------------- sr_R4.csv: дубль строки
p = DST / "sr_R4.csv"
lines = [l for l in p.read_text(encoding="utf-8").split("\n") if l != ""]
# строка 0 — заголовок; дублируем первую строку данных
lines.append(lines[1])
p.write_text("\n".join(lines) + "\n", encoding="utf-8")

# ------------------------------------------------------- sr_R1.csv: паттерн Actvts
p = DST / "sr_R1.csv"
lines = [l for l in p.read_text(encoding="utf-8").split("\n") if l != ""]
head_cols = lines[0].split("|")
idx = next(i for i, c in enumerate(head_cols) if "purcb_dic_Actvts_dimGrp" in c)
vals = lines[1].split("|")
vals[idx] = '"777"'
lines[1] = "|".join(vals)
p.write_text("\n".join(lines) + "\n", encoding="utf-8")

# ------------------------------------- sr_sved_purcb.csv: enum + флаг присутствия
p = DST / "sr_sved_purcb.csv"
lines = [l for l in p.read_text(encoding="utf-8").split("\n") if l != ""]
head_cols = lines[0].split("|")
r1i = next(i for i, c in enumerate(head_cols) if "purcb_dic_R1Enumerator_dimGrp" in c)
r2i = next(i for i, c in enumerate(head_cols) if "purcb_dic_R2Enumerator_dimGrp" in c)
vals = lines[1].split("|")
# 4) неизвестный член перечисления -> TAX-020
vals[r1i] = '"mem-int:NeizvestnyjMember"'
# 7) «данные отсутствуют» при наличии строк в sr_R2.csv -> BUILTIN-PRESENCE
vals[r2i] = '"mem-int:DannyeOtsutstvuyutMember"'
lines[1] = "|".join(vals)
p.write_text("\n".join(lines) + "\n", encoding="utf-8")

# ------------------------------------------------------- broken_json
(DST_JSON / "mapping.json").write_text(
    '{ "@context": "www.cbr.ru/xbrl_csv2", "header": { ', encoding="utf-8")

print("Готово:")
print(" ", DST)
print(" ", DST_JSON / "mapping.json")
