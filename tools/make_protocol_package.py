#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Сборка samples/protocol_package — анонимизированное воспроизведение
пакета, отклонённого приёмкой ЦБ (протокол fControlLogicalCsv2/3/5/6).

Источник — реальный пакет с дефектами конвертера (R5/R6/R10/R11).
Скрипт переносит только СТРУКТУРУ (mapping.json с колонками таблиц);
строки данных синтезируются заново и персональных данных не содержат.
Воспроизводимые дефекты (по протоколу приёмки ЦБ):

  * sr_R10: purcb_dic_Scrt_NmnlPrc = ".5" — число без целой части
    (fControlLogicalCsv2, 28 срабатываний в оригинале);
  * sr_R6:  purcb_dic_R_DcmDt = текст в колонке даты (fControlLogicalCsv2);
  * sr_R6:  purcb_dic_AO_Tm = "-" — маркер отсутствия не проходит маску
    времени (fControlLogicalCsv5);
  * sr_R6:  purcb_dic_Asst_CrncEnumerator = сырой код валюты вместо члена
    перечисления (fControlLogicalCsv6);
  * sr_R6:  пустые открытые оси dim_int_A_PrtflCdTaxis/A_SctnTaxis
    (fControlLogicalCsv3).

Запуск из корня репозитория:
  python3 tools/make_protocol_package.py --source /путь/к/распакованному/пакету
"""
import argparse
import csv
import io
import json
import os
import zipfile

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

DANNYE_PEREDANY = 'mem-int:DannyePeredanyMember'
DANNYE_OTSUT = 'mem-int:DannyeOtsutstvuyutMember'


def read_rows(path):
    with open(path, encoding='utf-8', newline='') as f:
        return list(csv.reader(f, delimiter='|', quotechar='"'))


def write_rows(path, rows):
    with open(path, 'w', encoding='utf-8', newline='') as f:
        w = csv.writer(f, delimiter='|', quotechar='"', lineterminator='\n',
                       quoting=csv.QUOTE_MINIMAL)
        w.writerows(rows)


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--source", required=True,
                    help='каталог реального пакета (с mapping.json и CSV)')
    ap.add_argument('--out', default=os.path.join(REPO, 'samples', 'protocol_package'))
    args = ap.parse_args()

    src = args.source
    if os.path.isfile(src) and src.lower().endswith('.zip'):
        # читаем прямо из архива
        zf = zipfile.ZipFile(src)
        mapping = json.loads(zf.read('mapping.json').decode('utf-8'))
        csv_files = {n: list(csv.reader(io.StringIO(zf.read(n).decode('utf-8')),
                                        delimiter='|', quotechar='"'))
                     for n in zf.namelist() if n.endswith('.csv')}
        src = None
    else:
        mapping = json.load(open(os.path.join(src, 'mapping.json'), encoding='utf-8'))
        csv_files = {n: read_rows(os.path.join(src, n))
                     for n in os.listdir(src) if n.endswith('.csv')}

    os.makedirs(args.out, exist_ok=True)
    out_rows = {}

    for t in mapping['tables']:
        uri = t['uri']
        if uri not in csv_files:
            raise SystemExit(f'в источнике нет {uri}')
        hdr = csv_files[uri][0]
        axes = [c for c in hdr if c.startswith('dim_int_')]
        n = 1 if uri in ('sr_R5.csv', 'sr_R11.csv', 'sr_sved_purcb.csv') else 3
        rows = []
        for r in range(1, n + 1):
            row = [''] * len(hdr)
            for i, c in enumerate(hdr):
                # оси: уникальные нейтральные значения, чтобы не дублировать
                # комбинации осей (иначе BUILTIN-UNIQUE добавит лишний шум)
                if c in axes:
                    row[i] = f'ось-{r}-{axes.index(c)}'
            rows.append(row)

        if uri == 'sr_R6.csv':
            def setv(col, val):
                row_idx = hdr.index(col)
                for r in rows:
                    r[row_idx] = val
            # дефект по протоколу: две открытые оси пусты во всех строках
            setv('dim_int_A_PrtflCdTaxis', '')
            setv('dim_int_A_SctnTaxis', '')
            # дефект по протоколу: маркер «-» не проходит маску времени
            setv('purcb_dic_AO_Tm_dimGrp_1_periodGrp_1', '-')
            # дефект по протоколу: текст в колонке даты
            setv('purcb_dic_R_DcmDt_dimGrp_1_periodGrp_1', 'Клиринговый отчет')
            # дефект по протоколу: сырой код вместо члена перечисления
            setv('purcb_dic_Asst_CrncEnumerator_dimGrp_1_periodGrp_1', '6098')

        if uri == 'sr_R10.csv':
            col = hdr.index('purcb_dic_Scrt_NmnlPrc_dimGrp_1_periodGrp_1')
            # дефект по протоколу: числа без целой части
            for r, v in zip(rows, ('.5', '.025', '.000450595')):
                r[col] = v

        if uri == 'sr_sved_purcb.csv':
            row = rows[0]
            filled = {'purcb_dic_Inf_PO_dimGrp_1_periodGrp_1': 'ПО отчитывающейся организации (тест)',
                      'purcb_dic_OtvZaPrInf_ElPoch_dimGrp_1_periodGrp_1': 'test@example.com',
                      'purcb_dic_OtvZaPrInf_FIO_dimGrp_1_periodGrp_1': 'Тестов Тест Тестович',
                      'purcb_dic_OtvZaPrInf_Tlf_dimGrp_1_periodGrp_1': '+7 (000) 000-00-00',
                      'purcb_dic_PSMP_INN_dimGrp_1_periodGrp_1': '1111111111',
                      'purcb_dic_PSMP_Nm_dimGrp_1_periodGrp_1': 'Тестовая организация',
                      'purcb_dic_PeriodStart_Dt_dimGrp_1_periodGrp_1': '2026-11-01',
                      'purcb_dic_PeriodEnd_Dt_dimGrp_1_periodGrp_1': '2026-11-30',
                      'purcb_dic_Rqst_Dt_dimGrp_1_periodGrp_1': '2026-11-19',
                      'purcb_dic_Rspns_Dt_dimGrp_1_periodGrp_1': '2026-11-19',
                      'purcb_dic_Rqst_YrsEnumerator2_dimGrp_1_periodGrp_1':
                          'http://www.cbr.ru/xbrl/udr/dom/mem-int#Y2025Member'}
            for i, c in enumerate(hdr):
                if c in filled:
                    row[i] = filled[c]
                elif 'Enumerator' in c:
                    # разделы, вошедшие в пакет, — «данные переданы»,
                    # отсутствующие разделы — «данные отсутствуют»
                    present = any(x in c for x in ('R5', 'R6', 'R10', 'R11'))
                    row[i] = DANNYE_PEREDANY if present else DANNYE_OTSUT

        out_rows[uri] = [hdr] + rows
        t['csvRowsCount'] = len(rows)

    # идентификатор организации заменяется на тестовый
    mapping.setdefault('header', {})['identifier'] = '1111111111111'

    with open(os.path.join(args.out, 'mapping.json'), 'w', encoding='utf-8') as f:
        json.dump(mapping, f, ensure_ascii=False, indent=1)
    for uri, rows in out_rows.items():
        write_rows(os.path.join(args.out, uri), rows)

    print(f'ГОТОВО: {args.out}')
    for uri, rows in out_rows.items():
        print(f'  {uri}: {len(rows) - 1} строк')


if __name__ == '__main__':
    main()
