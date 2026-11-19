#!/usr/bin/env python3
"""Транслятор контрольных соотношений ЦБ РФ (УОД ПУРЦБ 6.1.0.10) в rules.json.

Разбирает SQL-файлы valueAssertion_* / existenceAssertion_* и переводит
регулярные шаблоны в DSL движка бизнес-правил. Нетранслируемые формулы
попадают в rules_reference.json (справочный каталог).
"""
import json
import re
from pathlib import Path
from collections import Counter

SRC = Path("/home/z/my-project/research/cbr/Rules_DDL/formulas/tab")
OUT_DIR = Path("/home/z/my-project/research/cbr/ruleset")
OUT_DIR.mkdir(parents=True, exist_ok=True)

rules = []
reference = []
stats = Counter()

# --- регулярные выражения ---
RE_CODE = re.compile(r"'([\w]+_[\w]+)'\s+AS FORMULACODE")
RE_SEV = re.compile(r"'(\w+)'\s+AS FORMULASEVERITY")
RE_MSG = re.compile(r"'((?:[^']|'')+)'\s+AS FORMULAUNSATISFIEDMESSAGE")
# RESULT-выражение: ... THEN 1 ELSE 0 END AS RESULT (якорь отсекает CTE CASE WHEN)
RE_RESULT = re.compile(r"CASE WHEN\s+(.*?)\s+THEN\s+1\s+ELSE\s+0\s+END\s+AS\s+RESULT", re.S)
# псевдоним -> columnId:  COALESCE( col, 'x') AS Alias   |   col AS originalValue_Alias
RE_ALIAS_COALESCE = re.compile(r"COALESCE\(\s*(purcb_dic_\w+?)\s*,\s*'[^']*'\s*\)\s+AS\s+(\w+)")
RE_ALIAS_ORIG = re.compile(r"(purcb_dic_\w+?)\s+AS\s+originalValue_(\w+)")
RE_ALIAS_PLAIN = re.compile(r"(purcb_dic_\w+?)\s+AS\s+(\w+)\s*$", re.M)
# bindAsSequense_All: список колонок-фактов
RE_BINDALL = re.compile(
    r"CASE WHEN\s+((?:purcb_dic_\w+?\s+IS NOT NULL\s+OR\s+)+purcb_dic_\w+?\s+IS NOT NULL)\s+THEN", re.S | re.I)
RE_DIM = re.compile(r"\b(dim_int_\w+)\b")

DATE_RE = re.compile(
    r"\(\s*'&par:(\w+)&'::date\s*-\s*(\w+)::date\s*\)+\s*(>=|<=|>|<|=)\s*(-?\d+)\s*\)")
DATE_RE_REV = re.compile(
    r"\(\s*(\w+)::date\s*-\s*'&par:(\w+)&'::date\s*\)+\s*(>=|<=|>|<|=)\s*(-?\d+)\s*\)")


def parse_final_where(text: str) -> str:
    i = text.rfind("RR WHERE RR.RESULT")
    seg = text[:i] if i > 0 else text
    m = re.search(r"periodAspect\s*WHERE(.*)$", seg, re.S)
    return m.group(1) if m else ""


def split_top_level_or(expr: str):
    """Разбивает 'A or B or C' верхнего уровня (без скобок)."""
    parts, depth, cur = [], 0, ""
    for ch in expr:
        if ch == "(":
            depth += 1
        elif ch == ")":
            depth -= 1
        if depth == 0 and re.fullmatch(r"\s*", cur + ch) is None and False:
            pass
        cur += ch
    # простая разбивка по ' or ' вне скобок
    parts, depth, cur = [], 0, ""
    token = ""
    for ch in expr:
        if ch == "(":
            depth += 1
        elif ch == ")":
            depth -= 1
        if depth == 0:
            token += ch
        else:
            token += ch
    # используем regex-подход: найти все операнды
    ops = re.split(r"\s+or\s+", expr, flags=re.I)
    return [o.strip() for o in ops]


def main():
    for sql_file in sorted(SRC.rglob("*.SQL")):
        text = sql_file.read_text(encoding="utf-8", errors="replace")
        table = sql_file.parent.name

        code_m = RE_CODE.search(text)
        if not code_m:
            continue
        code = code_m.group(1)
        severity = (RE_SEV.search(text) or [None, "WARNING"])[1]
        msg_m = RE_MSG.search(text)
        message = msg_m.group(1).replace("''", "'") if msg_m else ""

        # таблицы, участвующие в формуле (кросс-табличные -> справочно)
        used_tables = set(re.findall(r"FCT_CSVSTAGE_20260403_20260430_(sr_\w+)", text))

        # карта псевдонимов
        aliases = {}
        for m in RE_ALIAS_COALESCE.finditer(text):
            aliases[m.group(2)] = m.group(1)
        for m in RE_ALIAS_ORIG.finditer(text):
            aliases[m.group(2)] = m.group(1)

        def col_of(alias_or_orig):
            a = alias_or_orig
            if a.startswith("originalValue_"):
                a = a[len("originalValue_"):]
            return aliases.get(a)

        res_m = RE_RESULT.search(text)
        result_expr = " ".join(res_m.group(1).split()) if res_m else ""
        where_raw = parse_final_where(text)

        entry = {
            "id": code,
            "table": table,
            "severity": "warning" if severity.upper() == "WARNING" else "error",
            "severityCbr": severity.upper(),
            "message": message,
        }
        ref = dict(entry)
        ref["resultExpr"] = result_expr
        ref["where"] = " ".join(where_raw.split())[:500]
        ref["crossTable"] = len(used_tables) > 1
        ref["usedTables"] = sorted(used_tables)
        reference.append(ref)

        # --- кросс-табличные формулы: только справочно ---
        if len(used_tables) > 1:
            stats["cross_table"] += 1
            continue

        # --- разбор WHERE ---
        any_filled = []
        all_filled = []
        value_in = []
        value_not_in = []
        value_matches = []
        date_rules = []

        # 1) OR-список originalValue_X IS NOT NULL
        m_or = re.search(r"\(\s*((?:originalValue_\w+\s+IS\s+NOT\s+NULL\s+OR\s+)+originalValue_\w+\s+IS\s+NOT\s+NULL)\s*\)", where_raw, re.I)
        if m_or:
            for alias in re.findall(r"originalValue_(\w+)", m_or.group(1)):
                c = col_of("originalValue_" + alias)
                if c and c not in any_filled:
                    any_filled.append(c)
        # 2) отдельные AND originalValue_X is not null
        rest = where_raw
        if m_or:
            rest = where_raw.replace(m_or.group(1), " ")
        for m in re.finditer(r"(?:AND\s+)?originalValue_(\w+)\s+is\s+not\s+null", rest, re.I):
            c = col_of("originalValue_" + m.group(1))
            if c and c not in all_filled and c not in any_filled:
                all_filled.append(c)
        # 2b) предусловие originalValue_All -> anyFilled по всем фактам строки
        if re.search(r"originalValue_All\s+is\s+not\s+null", where_raw, re.I):
            m_bind = RE_BINDALL.search(text)
            if m_bind and not any_filled:
                any_filled = re.findall(r"purcb_dic_\w+?(?=\s+IS\s+NOT\s+NULL)", m_bind.group(1), re.I)
        # 3) bindAsSequense_All -> anyFilled по всем фактам
        if not any_filled and not all_filled:
            m_bind = RE_BINDALL.search(text)
            if m_bind:
                any_filled = re.findall(r"purcb_dic_\w+?(?=\s+IS\s+NOT\s+NULL)", m_bind.group(1), re.I)
        # 4) условия на даты
        for m in DATE_RE.finditer(where_raw):
            par, alias, op, days = m.group(1), m.group(2), m.group(3), int(m.group(4))
            c = col_of(alias)
            if c and days == 0:
                # ( reportDate - col ) >= 0  =>  reportDate >= col
                date_rules.append({"left": "$reportDate", "right": c, "op": op})
            elif c:
                date_rules.append({"left": "$reportDate", "right": c, "op": op, "days": days})
        for m in DATE_RE_REV.finditer(where_raw):
            alias, par, op, days = m.group(1), m.group(2), m.group(3), int(m.group(4))
            c = col_of(alias)
            if c and days == 0:
                # ( col - reportDate ) >= 0  =>  col >= reportDate
                date_rules.append({"left": c, "right": "$reportDate", "op": op})
            elif c:
                date_rules.append({"left": c, "right": "$reportDate", "op": op, "days": days})
        # 5) условия на значения: Alias = 'v' or ... ; Alias != 'v' ; regexp_match(Alias,'...')
        rest2 = re.sub(r"originalValue_\w+\s+is\s+not\s+null", " ", where_raw, flags=re.I)
        rest2 = re.sub(DATE_RE.pattern, " ", rest2)
        rest2 = re.sub(DATE_RE_REV.pattern, " ", rest2)
        for m in re.finditer(r"(\w+)\s*!=\s*'([^']*)'", rest2):
            c = col_of(m.group(1))
            if c:
                value_not_in.append({"column": c, "values": [m.group(2)]})
        eq_groups = {}
        for m in re.finditer(r"(\w+)\s*=\s*'([^']*)'", rest2):
            c = col_of(m.group(1))
            if c:
                eq_groups.setdefault(c, set()).add(m.group(2))
        for c, vals in eq_groups.items():
            value_in.append({"column": c, "values": sorted(vals)})
        # 5b) regexp-условия: regexp_match( Alias , 'pat') IS NOT NULL
        for m in re.finditer(r"regexp_match\(\s*(\w+)\s*,\s*'([^']*)'\s*\)\s+IS\s+NOT\s+NULL", where_raw, re.I):
            c = col_of(m.group(1))
            if c:
                value_matches.append({"column": c, "pattern": m.group(2)})

        when = {}
        if any_filled:
            when["anyFilled"] = any_filled
        if all_filled:
            when["allFilled"] = all_filled
        if value_in:
            when["valueIn"] = value_in
        if value_not_in:
            when["valueNotIn"] = value_not_in
        if value_matches:
            when["valueMatches"] = value_matches
        if date_rules:
            when["dateRules"] = date_rules

        # --- разбор RESULT ---
        # A) presence: originalValue_X is not null [OR ...] | сырые колонки: col IS NOT NULL OR ...
        presence = re.findall(r"originalValue_(\w+)\s+is\s+not\s+null", result_expr, re.I)
        raw_presence = re.findall(r"(purcb_dic_\w+?)\s+IS\s+NOT\s+NULL", result_expr, re.I)
        # B) forbidden: (originalValue_X) is null
        forbidden = re.findall(r"\(\s*originalValue_(\w+)\s*\)\s+is\s+null", result_expr, re.I)
        # C) значения: Alias = 'v' / Alias != 'v' / regexp_match(Alias,'pat')
        allowed = [(a, v) for a, v in re.findall(r"(\w+)\s*=\s*'([^']*)'", result_expr) if a != "UUID"]
        disallowed = re.findall(r"(\w+)\s*!=\s*'([^']*)'", result_expr)
        res_regexp = re.findall(r"regexp_match\(\s*(\w+)\s*,\s*'([^']*)'\s*\)\s+IS\s+NOT\s+NULL", result_expr, re.I)

        def cols_from_presence():
            cols = [col_of("originalValue_" + p) for p in presence]
            cols = [c for c in cols if c]
            cols += [c for c in raw_presence if c and c not in cols]
            return cols

        rule = None
        if forbidden and len(forbidden) == 1 and not presence and not raw_presence:
            c = col_of("originalValue_" + forbidden[0])
            if c:
                rule = dict(entry, check="forbiddenIf", column=c, when=when)
        elif res_regexp and len(res_regexp) == 1 and not presence and not raw_presence and not allowed and not disallowed:
            c = col_of(res_regexp[0][0])
            if c:
                rule = dict(entry, check="matchesPatternIf", column=c,
                            pattern=res_regexp[0][1], when=when)
        elif (presence or raw_presence) and not allowed and not disallowed:
            cols = cols_from_presence()
            if cols:
                if code.startswith("existenceAssertion"):
                    rule = dict(entry, check="required", column=cols[0],
                                **({"when": when} if when else {}))
                elif len(cols) == 1:
                    rule = dict(entry, check="requiredIf", column=cols[0], when=when)
                else:
                    rule = dict(entry, check="requiredAnyOf", columns=cols, when=when)
        elif (presence or raw_presence) and (allowed or disallowed):
            # комбинированный тест: колонка заполнена И (не)равна значению
            cols = cols_from_presence()
            val_cols = {}
            for alias, val in allowed:
                c = col_of(alias)
                if c:
                    val_cols.setdefault(c, set()).add(val)
            ne_cols = {}
            for alias, val in disallowed:
                c = col_of(alias)
                if c:
                    ne_cols.setdefault(c, set()).add(val)
            if len(cols) == 1:
                c = cols[0]
                if c in ne_cols and not (c in val_cols):
                    rule = dict(entry, check="requiredNotIn", column=c,
                                values=sorted(ne_cols[c]), when=when)
                elif c in val_cols and not (c in ne_cols):
                    rule = dict(entry, check="requiredIn", column=c,
                                values=sorted(val_cols[c]), when=when)
                else:
                    stats["mixed_result"] += 1
            else:
                stats["mixed_result"] += 1
        elif allowed and not presence and not raw_presence and not forbidden:
            cols = {}
            for alias, val in allowed:
                c = col_of(alias)
                if c:
                    cols.setdefault(c, set()).add(val)
            if len(cols) == 1:
                c, vals = next(iter(cols.items()))
                rule = dict(entry, check="allowedValuesIf", column=c,
                            values=sorted(vals), when=when)
            elif cols and not disallowed:
                # конъюнкция значений нескольких колонок: все условия должны выполняться
                conds = [{"column": c, "values": sorted(v)} for c, v in cols.items()]
                rule = dict(entry, check="valueConjunction",
                            conditions=conds, when=when)
            elif cols:
                stats["multi_col_result"] += 1
        elif disallowed and not presence and not raw_presence and not forbidden:
            cols = {}
            for alias, val in disallowed:
                c = col_of(alias)
                if c:
                    cols.setdefault(c, set()).add(val)
            if len(cols) == 1:
                c, vals = next(iter(cols.items()))
                rule = dict(entry, check="forbiddenValuesIf", column=c,
                            values=sorted(vals), when=when)
        if rule:
            rules.append(rule)
            stats[rule["check"]] += 1
        else:
            stats["untranslated"] += 1

    # сохранение
    out = {
        "ruleSet": "Контрольные соотношения ЦБ РФ — УОД ПУРЦБ",
        "taxonomyVersion": "6.1.0.10",
        "taxonomyIdentifier": "20260403_20260430",
        "source": "ЦБ РФ, апрель 2026 (April_2026_m.zip), переведены автоматически",
        "rules": rules,
    }
    (OUT_DIR / "rules.json").write_text(json.dumps(out, ensure_ascii=False, indent=1), encoding="utf-8")
    (OUT_DIR / "rules_reference.json").write_text(
        json.dumps({"formulas": reference}, ensure_ascii=False, indent=1), encoding="utf-8")

    total = len(reference)
    print(f"Формул всего: {total}")
    print(f"Переведено в правила: {len(rules)}")
    print(f"По типам: {dict(stats)}")
    print(f"Справочно (кросс-табличные и др.): {total - len(rules)}")
    # примеры
    for k in ("required", "requiredIf", "requiredAnyOf", "forbiddenIf", "allowedValuesIf"):
        ex = [r for r in rules if r["check"] == k][:1]
        for e in ex:
            print(f"\n--- {k} ---")
            print(json.dumps(e, ensure_ascii=False)[:600])


if __name__ == "__main__":
    main()
