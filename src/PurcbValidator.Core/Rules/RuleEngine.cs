using System.Globalization;
using System.Text.RegularExpressions;
using PurcbValidator.Core.Csv;
using PurcbValidator.Core.Models;
using PurcbValidator.Core.Taxonomy;

namespace PurcbValidator.Core.Rules;

/// <summary>Статистика выполнения набора правил.</summary>
public sealed class RuleEngineStats
{
    public int RulesTotal { get; set; }
    public int RulesExecuted { get; set; }
    public int RulesSkippedUnknownColumn { get; set; }
    public int Violations { get; set; }
}

/// <summary>
/// Слой 4 — движок бизнес-правил. Выполняет правила rules.json
/// (переведённые контрольные соотношения ЦБ РФ) и встроенные структурные
/// проверки пакета (уникальность комбинаций осей, согласованность
/// сопроводительной информации с составом таблиц).
/// </summary>
public sealed class RuleEngine
{
    private const string Peredany = "mem-int:DannyePeredanyMember";
    private const string Otsutstvuyut = "mem-int:DannyeOtsutstvuyutMember";

    private readonly TaxonomyDictionary _taxonomy;

    public RuleEngine(TaxonomyDictionary taxonomy)
    {
        _taxonomy = taxonomy;
    }

    public RuleEngineStats Execute(
        MappingDocument mapping,
        IReadOnlyDictionary<string, CsvTableData> tables,
        IEnumerable<BusinessRule> rules,
        List<ValidationIssue> issues)
    {
        var stats = new RuleEngineStats();
        var ruleList = rules.ToList();
        stats.RulesTotal = ruleList.Count;

        var reportDate = TryParseDate(mapping.Header?.ReportDate);

        foreach (var rule in ruleList)
        {
            if (!tables.TryGetValue(rule.Table, out var table))
            {
                // таблица отсутствует в пакете — замечание уже выдано на слое CSV
                continue;
            }

            var cols = RuleColumns(rule);
            if (cols.Any(c => table.ColumnIndex(c) < 0))
            {
                stats.RulesSkippedUnknownColumn++;
                issues.Add(new ValidationIssue
                {
                    Layer = ValidationLayer.BusinessRules,
                    Severity = IssueSeverity.Info,
                    Code = "RULE-SKIP",
                    File = table.Uri,
                    Table = rule.Table,
                    Message = $"Правило {rule.Id} пропущено: в таблице нет колонок, на которые оно ссылается",
                });
                continue;
            }

            stats.RulesExecuted++;

            for (var r = 0; r < table.Rows.Count; r++)
            {
                var row = table.Rows[r];
                if (!IsApplicable(rule.When, table, row, reportDate)) continue;

                var violation = CheckRule(rule, table, row, out var badColumn, out var actual);
                if (violation is null) continue;

                stats.Violations++;
                issues.Add(new ValidationIssue
                {
                    Layer = ValidationLayer.BusinessRules,
                    Severity = rule.Severity == "error" ? IssueSeverity.Error : IssueSeverity.Warning,
                    Code = "RULE-" + rule.Id,
                    File = table.Uri,
                    Table = rule.Table,
                    Row = r + 1,
                    Column = badColumn,
                    ColumnLabel = badColumn is null ? null : _taxonomy.Label(badColumn),
                    Value = actual is null ? null : (actual.Length <= 120 ? actual : actual[..120] + "…"),
                    Message = rule.Message,
                });
            }
        }

        BuiltInChecks(mapping, tables, issues);
        return stats;
    }

    // ------------------------------------------------------------------

    private static List<string> RuleColumns(BusinessRule rule)
    {
        var cols = new List<string>();
        if (rule.Column is not null) cols.Add(rule.Column);
        cols.AddRange(rule.Columns ?? new List<string>());
        cols.AddRange((rule.Conditions ?? new List<ValueCondition>()).Select(c => c.Column));
        cols.AddRange(WhenColumns(rule.When));
        return cols.Distinct().ToList();
    }

    private static IEnumerable<string> WhenColumns(RuleWhen? when)
    {
        if (when is null) yield break;
        foreach (var c in when.AnyFilled ?? new List<string>()) yield return c;
        foreach (var c in when.AllFilled ?? new List<string>()) yield return c;
        foreach (var c in when.ValueIn ?? new List<ValueCondition>()) yield return c.Column;
        foreach (var c in when.ValueNotIn ?? new List<ValueCondition>()) yield return c.Column;
        foreach (var c in when.ValueMatches ?? new List<PatternCondition>()) yield return c.Column;
        foreach (var d in when.DateRules ?? new List<DateRule>())
        {
            if (!d.Left.StartsWith('$')) yield return d.Left;
            if (!d.Right.StartsWith('$')) yield return d.Right;
        }
    }

    /// <summary>Проверяет условия применимости правила к строке.</summary>
    private static bool IsApplicable(RuleWhen? when, CsvTableData table, CsvRow row, DateOnly? reportDate)
    {
        if (when is null) return true;

        if (when.AnyFilled is { Count: > 0 } &&
            !when.AnyFilled.Any(c => IsFilled(table, row, c)))
            return false;

        if (when.AllFilled is { Count: > 0 } &&
            !when.AllFilled.All(c => IsFilled(table, row, c)))
            return false;

        foreach (var cond in when.ValueIn ?? new List<ValueCondition>())
        {
            var v = CellValue(table, row, cond.Column);
            if (v is null || !cond.Values.Contains(v)) return false;
        }

        foreach (var cond in when.ValueNotIn ?? new List<ValueCondition>())
        {
            var v = CellValue(table, row, cond.Column);
            // по семантике SQL: X != 'v' при NULL даёт NULL (условие не выполняется)
            if (v is null || cond.Values.Contains(v)) return false;
        }

        foreach (var cond in when.ValueMatches ?? new List<PatternCondition>())
        {
            var v = CellValue(table, row, cond.Column);
            if (v is null || !Regex.IsMatch(v, cond.Pattern)) return false;
        }

        foreach (var d in when.DateRules ?? new List<DateRule>())
        {
            var left = d.Left == "$reportDate" ? reportDate : TryParseDate(CellValue(table, row, d.Left));
            var right = d.Right == "$reportDate" ? reportDate : TryParseDate(CellValue(table, row, d.Right));
            if (left is null || right is null) return false;
            var shiftDays = (int)(d.Days ?? 0);
            var shifted = right.Value.AddDays(shiftDays);
            var ok = d.Op switch
            {
                "ge" => left >= shifted,
                "gt" => left > shifted,
                "le" => left <= shifted,
                "lt" => left < shifted,
                "eq" => left == shifted,
                _ => false,
            };
            if (!ok) return false;
        }

        return true;
    }

    /// <summary>Возвращает текст нарушения или null. Семантика повторяет SQL-формулы ЦБ.</summary>
    private static string? CheckRule(
        BusinessRule rule, CsvTableData table, CsvRow row, out string? badColumn, out string? actual)
    {
        badColumn = rule.Column;
        actual = rule.Column is null ? null : CellValue(table, row, rule.Column);

        switch (rule.Check)
        {
            case "required":
            case "requiredIf":
                if (string.IsNullOrEmpty(actual))
                    return "не заполнено обязательное поле";
                return null;

            case "requiredAnyOf":
                badColumn = (rule.Columns ?? new()).FirstOrDefault(c => IsFilled(table, row, c))
                            ?? rule.Columns?.FirstOrDefault();
                if (rule.Columns is { Count: > 0 } && !rule.Columns.Any(c => IsFilled(table, row, c)))
                {
                    actual = rule.Columns.Select(c => CellValue(table, row, c)).FirstOrDefault(v => v is not null);
                    return "должно быть заполнено хотя бы одно из полей группы";
                }
                return null;

            case "requiredIn":
            // допустимые значения: по SQL пустое значение тоже является нарушением
            case "allowedValuesIf":
                if (string.IsNullOrEmpty(actual)) return "не заполнено обязательное поле";
                if (rule.Values is { } vals && !vals.Contains(actual))
                    return $"значение должно быть одним из: {string.Join(", ", vals)}";
                return null;

            case "requiredNotIn":
            // запрещённые значения: по SQL пустое значение тоже является нарушением
            case "forbiddenValuesIf":
                if (string.IsNullOrEmpty(actual)) return "не заполнено обязательное поле";
                if (rule.Values is { } nvals && nvals.Contains(actual))
                    return $"значение не должно быть одним из: {string.Join(", ", nvals)}";
                return null;

            case "forbiddenIf":
                if (!string.IsNullOrEmpty(actual))
                    return "поле не должно заполняться при выполнении условий";
                return null;

            case "matchesPatternIf":
                if (string.IsNullOrEmpty(actual)) return "не заполнено обязательное поле";
                if (rule.Pattern is { } p && !Regex.IsMatch(actual, p))
                    return $"значение должно соответствовать маске «{p}»";
                return null;

            case "valueConjunction":
                foreach (var cond in rule.Conditions ?? new List<ValueCondition>())
                {
                    var v = CellValue(table, row, cond.Column);
                    badColumn = cond.Column;
                    actual = v;
                    if (v is null)
                        return "не заполнено поле, входящее в условие";
                    if (!cond.Values.Contains(v))
                        return $"значение должно быть одним из: {string.Join(", ", cond.Values)}";
                }
                badColumn = rule.Conditions?.FirstOrDefault()?.Column;
                return null;

            default:
                return null;
        }
    }

    // ------------------------------------------------------------------

    /// <summary>Встроенные структурные проверки пакета.</summary>
    private void BuiltInChecks(
        MappingDocument mapping,
        IReadOnlyDictionary<string, CsvTableData> tables,
        List<ValidationIssue> issues)
    {
        // 1) уникальность комбинаций осей в таблицах с typedFiltering
        foreach (var t in mapping.Tables)
        {
            if (t.TypedFiltering != true) continue;
            if (!tables.TryGetValue(t.TableName, out var table)) continue;
            var dimIndexes = Enumerable.Range(0, table.HeaderColumns.Count)
                .Where(i => table.HeaderColumns[i].StartsWith("dim_int_"))
                .ToList();
            if (dimIndexes.Count == 0) continue;

            var seen = new Dictionary<string, int>();
            for (var r = 0; r < table.Rows.Count; r++)
            {
                var key = string.Join("\u0001", dimIndexes.Select(i => table.Rows[r].Fields[i]));
                if (seen.TryGetValue(key, out var first))
                {
                    issues.Add(new ValidationIssue
                    {
                        Layer = ValidationLayer.BusinessRules,
                        Severity = IssueSeverity.Error,
                        Code = "BUILTIN-UNIQUE",
                        File = table.Uri,
                        Table = table.TableName,
                        Row = r + 1,
                        Message = $"Повторяющаяся комбинация значений осей (dim_int_*): дублирует строку {first + 1}; каждая комбинация осей должна встречаться один раз",
                    });
                }
                else
                {
                    seen[key] = r;
                }
            }
        }

        // 2) согласованность сопроводительной информации с составом пакета
        if (tables.TryGetValue("sr_sved_purcb", out var sved))
        {
            if (sved.Rows.Count != 1)
            {
                issues.Add(new ValidationIssue
                {
                    Layer = ValidationLayer.BusinessRules,
                    Severity = IssueSeverity.Error,
                    Code = "BUILTIN-SVED-ROWS",
                    File = sved.Uri,
                    Table = "sr_sved_purcb",
                    Message = $"Таблица сопроводительной информации должна содержать ровно одну строку данных, найдено {sved.Rows.Count}",
                });
            }

            foreach (var t in mapping.Tables)
            {
                var m = Regex.Match(t.TableName, @"^sr_(R\d+)$");
                if (!m.Success) continue;
                var enumCol = $"purcb_dic_{m.Groups[1].Value}Enumerator_dimGrp_1_periodGrp_1";
                var row = sved.Rows.FirstOrDefault();
                var flag = row is null ? null : sved.Cell(row, enumCol);
                if (flag is null) continue;

                var hasData = tables.TryGetValue(t.TableName, out var tbl) && tbl.Rows.Count > 0;

                if (flag == Peredany && !hasData)
                {
                    issues.Add(new ValidationIssue
                    {
                        Layer = ValidationLayer.BusinessRules,
                        Severity = IssueSeverity.Error,
                        Code = "BUILTIN-PRESENCE",
                        File = sved.Uri,
                        Table = "sr_sved_purcb",
                        Column = enumCol,
                        ColumnLabel = _taxonomy.Label(enumCol),
                        Message = $"В сопроводительной информации указано «данные переданы» ({m.Groups[1].Value}), но таблица {t.Uri} не содержит строк данных",
                    });
                }
                else if (flag == Otsutstvuyut && hasData)
                {
                    issues.Add(new ValidationIssue
                    {
                        Layer = ValidationLayer.BusinessRules,
                        Severity = IssueSeverity.Error,
                        Code = "BUILTIN-PRESENCE",
                        File = sved.Uri,
                        Table = "sr_sved_purcb",
                        Column = enumCol,
                        ColumnLabel = _taxonomy.Label(enumCol),
                        Message = $"В сопроводительной информации указано «данные отсутствуют» ({m.Groups[1].Value}), но таблица {t.Uri} содержит строки данных",
                    });
                }
            }
        }
        else
        {
            issues.Add(new ValidationIssue
            {
                Layer = ValidationLayer.BusinessRules,
                Severity = IssueSeverity.Error,
                Code = "BUILTIN-SVED-MISSING",
                Message = "В пакете отсутствует таблица сопроводительной информации sr_sved_purcb",
            });
        }
    }

    private static bool IsFilled(CsvTableData table, CsvRow row, string column)
        => CellValue(table, row, column) is not null;

    /// <summary>
    /// Значение ячейки для правил: пустая строка и маркер отсутствия «-»
    /// (формат УОД) приравниваются к отсутствию значения.
    /// </summary>
    private static string? CellValue(CsvTableData table, CsvRow row, string column)
    {
        var v = table.Cell(row, column);
        return string.IsNullOrEmpty(v) || v == "-" ? null : v;
    }

    internal static DateOnly? TryParseDate(string? value)
        => DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;
}
