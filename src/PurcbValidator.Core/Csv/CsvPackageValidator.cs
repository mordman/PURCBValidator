using System.Text;
using PurcbValidator.Core.Mapping;
using PurcbValidator.Core.Models;
using PurcbValidator.Core.Taxonomy;

namespace PurcbValidator.Core.Csv;

/// <summary>
/// Слой 3 — проверка CSV-файлов пакета: наличие файлов, кодировка,
/// заголовок против описания таблицы в mapping.json, число строк данных,
/// число полей в строках, типы значений (лексический формат XBRL-CSV),
/// ограничение длины текста, домены перечислений, фасеты таксономии
/// и полнота открытых осей в строках (typedFiltering).
/// </summary>
public sealed class CsvPackageValidator
{
    private readonly TaxonomyDictionary _taxonomy;

    public CsvPackageValidator(TaxonomyDictionary taxonomy)
    {
        _taxonomy = taxonomy;
    }

    /// <summary>Загружает и проверяет все таблицы пакета. Возвращает загруженные таблицы.</summary>
    public Dictionary<string, CsvTableData> Validate(
        MappingDocument mapping,
        List<ValidationIssue> issues,
        Action<string>? progress = null)
    {
        var result = new Dictionary<string, CsvTableData>(StringComparer.OrdinalIgnoreCase);
        if (mapping.Header is null) return result;

        var baseDir = Path.GetDirectoryName(mapping.SourcePath) ?? ".";
        var delimiter = string.IsNullOrEmpty(mapping.Header.Delimiter) ? "|" : mapping.Header.Delimiter!;
        var qualifier = string.IsNullOrEmpty(mapping.Header.TextQualifier) ? "\"" : mapping.Header.TextQualifier!;
        var limit = mapping.Header.TextValueLengthLimit ?? 8192;
        var declaredEncoding = mapping.Header.Encoding ?? "UTF-8";

        foreach (var t in mapping.Tables)
        {
            if (string.IsNullOrEmpty(t.Uri)) continue;
            var tableName = t.TableName;          // ключ таблицы (уникален в пакете)
            var displayTable = t.RoleKey;         // отображение: SR_0420417 / sr_R1
            var fullPath = Path.Combine(baseDir, t.Uri);
            progress?.Invoke($"Проверка таблицы {displayTable} ({t.Uri})");

            if (!File.Exists(fullPath))
            {
                issues.Add(new ValidationIssue
                {
                    Layer = ValidationLayer.CsvData,
                    Severity = IssueSeverity.Error,
                    Code = "CSV-001",
                    File = t.Uri,
                    Table = displayTable,
                    Message = "CSV-файл, указанный в служебном файле, отсутствует в пакете",
                });
                continue;
            }

            // --- чтение с жёсткой проверкой UTF-8 ---
            string text;
            var hasBom = false;
            try
            {
                var bytes = File.ReadAllBytes(fullPath);
                hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
                var strictUtf8 = new UTF8Encoding(false, true);
                try
                {
                    text = strictUtf8.GetString(bytes);
                }
                catch (DecoderFallbackException)
                {
                    issues.Add(new ValidationIssue
                    {
                        Layer = ValidationLayer.CsvData,
                        Severity = IssueSeverity.Error,
                        Code = "CSV-002",
                        File = t.Uri,
                        Table = displayTable,
                        Message = "Файл не является корректным UTF-8 (обнаружены недопустимые байты)",
                    });
                    text = new UTF8Encoding(false, false).GetString(bytes);
                }
            }
            catch (Exception ex)
            {
                issues.Add(new ValidationIssue
                {
                    Layer = ValidationLayer.CsvData,
                    Severity = IssueSeverity.Error,
                    Code = "CSV-003",
                    File = t.Uri,
                    Table = displayTable,
                    Message = $"Не удалось прочитать файл: {ex.Message}",
                });
                continue;
            }

            if (!string.Equals(declaredEncoding, "UTF-8", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new ValidationIssue
                {
                    Layer = ValidationLayer.CsvData,
                    Severity = IssueSeverity.Error,
                    Code = "CSV-004",
                    File = t.Uri,
                    Table = displayTable,
                    Message = $"Кодировка файла должна соответствовать заявленной в mapping.json ({declaredEncoding})",
                });
            }

            if (hasBom)
            {
                issues.Add(new ValidationIssue
                {
                    Layer = ValidationLayer.CsvData,
                    Severity = IssueSeverity.Info,
                    Code = "CSV-005",
                    File = t.Uri,
                    Table = displayTable,
                    Message = "Файл начинается с маркера BOM; требуется UTF-8 без BOM",
                });
            }

            // --- разбор CSV ---
            var fileErrors = new List<string>();
            var rowLines = new List<long>();
            var lexer = new CsvLexer(delimiter, qualifier);
            var rows = lexer.Parse(text, fileErrors, rowLines);

            if (rows.Count == 0)
            {
                issues.Add(new ValidationIssue
                {
                    Layer = ValidationLayer.CsvData,
                    Severity = IssueSeverity.Error,
                    Code = "CSV-006",
                    File = t.Uri,
                    Table = displayTable,
                    Message = "Файл пуст (отсутствует даже строка заголовка)",
                });
                continue;
            }

            var header = rows[0];
            var dataRows = rows.Skip(1).ToList();
            var dataLines = rowLines.Count == rows.Count
                ? rowLines.Skip(1).ToList()
                : new List<long>();

            var table = new CsvTableData
            {
                TableName = tableName,
                Uri = t.Uri!,
                Path = fullPath,
                HeaderColumns = header.Fields,
                Rows = dataRows,
                RowLines = dataLines,
                FileErrors = fileErrors,
            };
            result[tableName] = table;

            foreach (var fe in fileErrors)
                issues.Add(new ValidationIssue
                {
                    Layer = ValidationLayer.CsvData,
                    Severity = IssueSeverity.Error,
                    Code = "CSV-007",
                    File = t.Uri,
                    Table = displayTable,
                    Message = fe,
                });

            // --- заголовок против mapping ---
            var mapped = t.Columns.Select(c => c.ColumnId).ToList();
            var headerSet = new HashSet<string>(header.Fields, StringComparer.Ordinal);
            var mappedSet = new HashSet<string>(mapped, StringComparer.Ordinal);

            foreach (var missing in mapped.Where(c => !headerSet.Contains(c)))
                issues.Add(new ValidationIssue
                {
                    Layer = ValidationLayer.CsvData,
                    Severity = IssueSeverity.Error,
                    Code = "CSV-010",
                    File = t.Uri,
                    Table = displayTable,
                    Column = missing,
                    ColumnLabel = _taxonomy.Label(missing),
                    Message = $"Колонка \"{missing}\" описана в mapping.json, но отсутствует в заголовке CSV",
                });
            foreach (var extra in header.Fields.Where(c => !mappedSet.Contains(c)))
                issues.Add(new ValidationIssue
                {
                    Layer = ValidationLayer.CsvData,
                    Severity = IssueSeverity.Error,
                    Code = "CSV-011",
                    File = t.Uri,
                    Table = displayTable,
                    Column = extra,
                    ColumnLabel = _taxonomy.Label(extra),
                    Message = $"Колонка \"{extra}\" присутствует в заголовке CSV, но не описана в mapping.json",
                });

            var sameOrder = header.Fields.Count == mapped.Count && header.Fields.SequenceEqual(mapped);
            if (!sameOrder && headerSet.SetEquals(mappedSet))
                issues.Add(new ValidationIssue
                {
                    Layer = ValidationLayer.CsvData,
                    Severity = IssueSeverity.Info,
                    Code = "CSV-012",
                    File = t.Uri,
                    Table = displayTable,
                    Message = "Порядок колонок в заголовке CSV отличается от порядка описания в mapping.json (набор колонок совпадает)",
                });

            // --- число строк данных ---
            var declared = t.CsvRowsCount;
            if (declared.HasValue && declared.Value != dataRows.Count)
                issues.Add(new ValidationIssue
                {
                    Layer = ValidationLayer.CsvData,
                    Severity = IssueSeverity.Error,
                    Code = "CSV-020",
                    File = t.Uri,
                    Table = displayTable,
                    Row = dataRows.Count,
                    Message = $"Число строк данных ({dataRows.Count}) не совпадает с заявленным в mapping.json csvRowsCount ({declared.Value})",
                });

            // --- построчная проверка ---
            for (var r = 0; r < dataRows.Count; r++)
            {
                var row = dataRows[r];
                var lineNo = r < dataLines.Count ? dataLines[r] : header.Fields.Count + r + 2;

                if (row.HasErrors)
                {
                    foreach (var e in row.Errors)
                        issues.Add(new ValidationIssue
                        {
                            Layer = ValidationLayer.CsvData,
                            Severity = IssueSeverity.Error,
                            Code = "CSV-030",
                            File = t.Uri,
                            Table = displayTable,
                            Row = r + 1,
                            Message = e,
                        });
                }

                if (row.Fields.Count != header.Fields.Count)
                {
                    issues.Add(new ValidationIssue
                    {
                        Layer = ValidationLayer.CsvData,
                        Severity = IssueSeverity.Error,
                        Code = "CSV-031",
                        File = t.Uri,
                        Table = displayTable,
                        Row = r + 1,
                        Message = $"Число полей в строке ({row.Fields.Count}) не совпадает с числом колонок заголовка ({header.Fields.Count})",
                    });
                    continue; // типы проверять бессмысленно
                }

                ValidateRowValues(mapping, t, table, row, r + 1, limit, issues);
            }
        }

        // --- лишние CSV в папке ---
        var knownFiles = new HashSet<string>(mapping.Tables
            .Where(x => x.Uri is not null)
            .Select(x => x.Uri!), StringComparer.OrdinalIgnoreCase);
        foreach (var f in Directory.EnumerateFiles(baseDir, "*.csv"))
        {
            var name = Path.GetFileName(f);
            if (!knownFiles.Contains(name))
                issues.Add(new ValidationIssue
                {
                    Layer = ValidationLayer.CsvData,
                    Severity = IssueSeverity.Warning,
                    Code = "CSV-040",
                    File = name,
                    Message = "CSV-файл находится в папке пакета, но не описан в mapping.json (в состав пакета не войдёт)",
                });
        }

        return result;
    }

    private void ValidateRowValues(
        MappingDocument mapping,
        MappingTable t,
        CsvTableData table,
        CsvRow row,
        long rowNo,
        long limit,
        List<ValidationIssue> issues)
    {
        // открытые оси (typedFiltering): пропуск данных в оси отклоняется приёмкой
        // ЦБ — fControlLogicalCsv3 «Контроль открытых осей на пропуск данных»
        if (t.TypedFiltering == true)
        {
            for (var i = 0; i < table.HeaderColumns.Count && i < row.Fields.Count; i++)
            {
                var axCol = table.HeaderColumns[i];
                var axDef = t.Columns.FirstOrDefault(c => c.ColumnId == axCol);
                var isAxis = axCol.StartsWith("dim_int_", StringComparison.Ordinal) || axDef?.IsDimension == true;
                if (!isAxis || row.Fields[i].Length != 0) continue;
                issues.Add(new ValidationIssue
                {
                    Layer = ValidationLayer.CsvData,
                    Severity = IssueSeverity.Error,
                    Code = "CSV-070",
                    File = table.Uri,
                    Table = table.TableName,
                    Row = rowNo,
                    Column = axCol,
                    ColumnLabel = _taxonomy.Label(axCol),
                    Message = "Значение открытой оси отсутствует: в таблицах с типизированной фильтрацией " +
                              "каждая строка обязана содержать значения всех осей (контроль ЦБ fControlLogicalCsv3)",
                });
            }
        }

        for (var i = 0; i < table.HeaderColumns.Count && i < row.Fields.Count; i++)
        {
            var col = table.HeaderColumns[i];
            var value = row.Fields[i];
            if (value.Length == 0) continue;

            var colDef = t.Columns.FirstOrDefault(c => c.ColumnId == col);
            var columnType = colDef?.Aspect?.Type?.ColumnType;
            // концепт колонки: в 7.1 задаётся явно (xbrl:concept), в 6.1 выводится из columnId
            var concept = colDef?.Concept ?? _taxonomy.QNameFromColumnId(col);

            // краткая форма описания (6.1): тип из таксономии
            if (columnType is null && concept is not null)
                columnType = _taxonomy.ConceptType(concept);

            // «-» — признак отсутствия значения в формате УОД: проверки длины,
            // типов и перечислений к нему не применяются (правомерность отсутствия
            // значения у конкретного элемента проверяют exists-правила ЦБ).
            // Маски таксономии, по протоколу приёмки ЦБ (fControlLogicalCsv5
            // «Контроль 3Custom»), «-» не пропускают — маска проверяется ниже.
            if (value != "-")
            {
                // ограничение длины текстовых значений
                if (value.Length > limit)
                    issues.Add(new ValidationIssue
                    {
                        Layer = ValidationLayer.CsvData,
                        Severity = IssueSeverity.Error,
                        Code = "CSV-050",
                        File = table.Uri,
                        Table = table.TableName,
                        Row = rowNo,
                        Column = col,
                        ColumnLabel = _taxonomy.Label(col),
                        Value = TruncValue(value),
                        Message = $"Длина значения ({value.Length}) превышает textValueLengthLimit ({limit})",
                    });

                // пробелы по краям
                if (value != value.Trim())
                    issues.Add(new ValidationIssue
                    {
                        Layer = ValidationLayer.CsvData,
                        Severity = IssueSeverity.Info,
                        Code = "CSV-051",
                        File = table.Uri,
                        Table = table.TableName,
                        Row = rowNo,
                        Column = col,
                        ColumnLabel = _taxonomy.Label(col),
                        Value = TruncValue(value),
                        Message = "Значение содержит начальные или конечные пробелы",
                    });

                switch (columnType)
                {
                    case "xbrli:integerItemType":
                        if (!IsInteger(value))
                            issues.Add( TypeIssue(table, rowNo, col, value, "целое число (integer)") );
                        break;

                    case "xbrli:decimalItemType":
                    case "xbrli:monetaryItemType":
                    case "xbrli:pureItemType":
                        if (!IsDecimal(value))
                            issues.Add( TypeIssue(table, rowNo, col, value,
                                "десятичное число с обязательной целой частью (например, 0.5)") );
                        break;

                    case "xbrli:dateItemType":
                        if (!MappingLoader.IsIsoDate(value))
                            issues.Add( TypeIssue(table, rowNo, col, value, "дата в формате ГГГГ-ММ-ДД") );
                        break;

                    case "enum:enumerationItemType":
                    case "enum2:enumerationSetItemType":
                        ValidateEnum(table, rowNo, col, value, columnType, concept, issues);
                        break;
                }
            }

            // фасеты XSD (pattern и др.) для концепта колонки; проверяются по любым
            // непустым значениям, включая маркер «-» (fControlLogicalCsv5 ЦБ)
            if (concept is not null && _taxonomy.FacetsFor(concept) is { } facets)
            {
                if (facets.TryGetValue("pattern", out var pattern))
                {
                    try
                    {
                        if (!System.Text.RegularExpressions.Regex.IsMatch(value, pattern))
                            issues.Add(new ValidationIssue
                            {
                                Layer = ValidationLayer.TaxonomyChecks,
                                Severity = IssueSeverity.Error,
                                Code = "TAX-010",
                                File = table.Uri,
                                Table = table.TableName,
                                Row = rowNo,
                                Column = col,
                                ColumnLabel = _taxonomy.Label(col),
                                Value = TruncValue(value),
                                Message = $"Значение не соответствует маске таксономии «{pattern}»",
                            });
                    }
                    catch (System.Text.RegularExpressions.RegexParseException)
                    {
                        // некорректная маска в таксономии — не наш провал
                    }
                }
            }
        }
    }

    private void ValidateEnum(
        CsvTableData table, long rowNo, string col, string value, string columnType,
        string? concept, List<ValidationIssue> issues)
    {
        var qname = concept ?? _taxonomy.QNameFromColumnId(col);
        var domain = qname is null ? null : _taxonomy.EnumDomainFor(qname);
        if (domain is null) return; // домен неизвестен — не проверяем

        if (columnType == "enum2:enumerationSetItemType")
        {
            // Extensible Enumerations 2.0: значение — набор URI членов домена,
            // разделённых пробелами (http://…/mem-int#XxxMember)
            var uris = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (uris.Length == 0 || uris.Any(u => !UriMatchesDomain(u, domain.Values)))
            {
                issues.Add(new ValidationIssue
                {
                    Layer = ValidationLayer.TaxonomyChecks,
                    Severity = IssueSeverity.Error,
                    Code = "TAX-020",
                    File = table.Uri,
                    Table = table.TableName,
                    Row = rowNo,
                    Column = col,
                    ColumnLabel = _taxonomy.Label(col),
                    Value = TruncValue(value),
                    Message = $"Значение отсутствует в домене перечисления концепта {qname}: каждый URI набора должен иметь вид http://…#ЧленДомена",
                });
            }
            return;
        }

        if (!domain.Values.Contains(value))
        {
            issues.Add(new ValidationIssue
            {
                Layer = ValidationLayer.TaxonomyChecks,
                Severity = IssueSeverity.Error,
                Code = "TAX-020",
                File = table.Uri,
                Table = table.TableName,
                Row = rowNo,
                Column = col,
                ColumnLabel = _taxonomy.Label(col),
                Value = TruncValue(value),
                Message = $"Значение отсутствует в домене перечисления концепта {qname}: значение должно быть именем члена перечисления (например, mem-int:…Member)",
            });
        }
    }

    /// <summary>URI члена перечисления (…/mem-int#XxxMember) против домена (mem-int:XxxMember).</summary>
    private static bool UriMatchesDomain(string uri, HashSet<string> domainValues)
    {
        var hash = uri.LastIndexOf('#');
        if (hash < 0 || hash == uri.Length - 1) return false;
        var fragment = uri[(hash + 1)..];
        return domainValues.Contains(fragment)
            || domainValues.Any(v => v.EndsWith(":" + fragment, StringComparison.Ordinal));
    }

    private ValidationIssue TypeIssue(CsvTableData table, long rowNo, string col, string value, string expected) => new()
    {
        Layer = ValidationLayer.CsvData,
        Severity = IssueSeverity.Error,
        Code = "CSV-060",
        File = table.Uri,
        Table = table.TableName,
        Row = rowNo,
        Column = col,
        ColumnLabel = _taxonomy.Label(col),
        Value = TruncValue(value),
        Message = $"Значение не соответствует типу колонки: ожидается {expected}",
    };

    // Числовой лексический формат XBRL-CSV, принимаемый первичными контролями ЦБ
    // (fControlLogicalCsv2): целая часть обязательна, дробная — только цифры,
    // экспоненциальная запись запрещена. Значения вида «.5» отклоняются
    // приёмкой ЦБ, хотя decimal.TryParse их допускает; заодно отсекаются
    // «5.», «1E5» и переполнение long у IsInteger.
    private static readonly System.Text.RegularExpressions.Regex IntegerLexical =
        new("^[+-]?[0-9]+$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static readonly System.Text.RegularExpressions.Regex DecimalLexical =
        new("^[+-]?[0-9]+(\\.[0-9]+)?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    internal static bool IsInteger(string value) => IntegerLexical.IsMatch(value);

    internal static bool IsDecimal(string value) => DecimalLexical.IsMatch(value);

    private static string TruncValue(string v) => v.Length <= 120 ? v : v[..120] + "…";
}
