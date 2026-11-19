using System.Text;
using System.Text.Json;
using PurcbValidator.Core.Models;

namespace PurcbValidator.Core.Mapping;

/// <summary>
/// Слой 1 — загрузка и структурная валидация служебного файла mapping.json:
/// синтаксис JSON, обязательные поля, форматы дат и идентификаторов,
/// уникальность таблиц и колонок, согласованность с прилагаемыми CSV.
/// </summary>
public sealed class MappingLoader
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = false,
        AllowTrailingCommas = true,
    };

    /// <summary>Загружает и проверяет mapping.json. Ошибки добавляются в issues.</summary>
    /// <returns>Документ или null, если разбор невозможен.</returns>
    public MappingDocument? Load(string path, List<ValidationIssue> issues)
    {
        if (!File.Exists(path))
        {
            issues.Add(Error("MAP-000", path, "Файл mapping.json не найден"));
            return null;
        }

        string text;
        try
        {
            var bytes = File.ReadAllBytes(path);
            // строгая проверка UTF-8
            var strictUtf8 = new UTF8Encoding(false, true);
            try
            {
                text = strictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                issues.Add(Error("MAP-001", path,
                    "Файл не является корректным UTF-8 (обнаружены недопустимые байты)"));
                text = new UTF8Encoding(false, false).GetString(bytes);
            }
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                issues.Add(new ValidationIssue
                {
                    Layer = ValidationLayer.MappingStructure,
                    Severity = IssueSeverity.Info,
                    Code = "MAP-002",
                    File = path,
                    Message = "Файл начинается с маркера BOM; требуется UTF-8 без BOM",
                });
        }
        catch (Exception ex)
        {
            issues.Add(Error("MAP-003", path, $"Не удалось прочитать файл: {ex.Message}"));
            return null;
        }

        MappingDocument? doc;
        try
        {
            doc = JsonSerializer.Deserialize<MappingDocument>(text, JsonOpts);
        }
        catch (JsonException ex)
        {
            issues.Add(Error("MAP-004", path,
                $"Некорректный JSON: {ex.Message} (строка {ex.LineNumber + 1}, позиция {ex.BytePositionInLine + 1})"));
            return null;
        }

        if (doc is null)
        {
            issues.Add(Error("MAP-005", path, "Пустое содержимое mapping.json"));
            return null;
        }

        doc.SourcePath = path;
        Validate(doc, text, issues);
        return doc;
    }

    private void Validate(MappingDocument doc, string rawJson, List<ValidationIssue> issues)
    {
        var path = doc.SourcePath;

        if (doc.Context is null)
            issues.Add(Error("MAP-006", path, "Отсутствует обязательное поле \"@context\""));
        else if (doc.Context != "www.cbr.ru/xbrl_csv2")
            issues.Add(new ValidationIssue
            {
                Layer = ValidationLayer.MappingStructure,
                Severity = IssueSeverity.Warning,
                Code = "MAP-007",
                File = path,
                Message = $"Неожиданное значение \"@context\"=\"{doc.Context}\"; ожидается \"www.cbr.ru/xbrl_csv2\" (XBRL-CSV 2.0 Банка России)",
            });

        // --- header ---
        if (doc.Header is null)
        {
            issues.Add(Error("MAP-008", path, "Отсутствует обязательный блок \"header\""));
        }
        else
        {
            var h = doc.Header;
            if (!string.Equals(h.Encoding, "UTF-8", StringComparison.OrdinalIgnoreCase))
                issues.Add(Error("MAP-009", path,
                    $"Кодировка \"{h.Encoding ?? ""}\" не поддерживается; требуется UTF-8"));

            if (string.IsNullOrEmpty(h.Delimiter))
                issues.Add(Error("MAP-010", path, "Не задан разделитель полей \"delimiter\""));
            else if (h.Delimiter.Length != 1)
                issues.Add(Error("MAP-011", path,
                    $"Разделитель \"{h.Delimiter}\" должен быть одним символом"));

            if (string.IsNullOrEmpty(h.TextQualifier))
                issues.Add(Error("MAP-012", path, "Не задан ограничитель текста \"textQualifier\""));

            if (string.IsNullOrEmpty(h.ReportDate))
            {
                issues.Add(Error("MAP-013", path, "Не задана дата отчетного момента \"reportDate\""));
            }
            else if (!IsIsoDate(h.ReportDate))
            {
                issues.Add(Error("MAP-014", path,
                    $"Дата отчетного момента \"{h.ReportDate}\" должна быть в формате ГГГГ-ММ-ДД"));
            }

            if (string.IsNullOrEmpty(h.DocumentVersion))
                issues.Add(Error("MAP-015", path, "Не задана версия схемы \"documentVersion\""));

            if (h.TextValueLengthLimit is null or <= 0)
                issues.Add(Error("MAP-016", path,
                    "Не задано (или некорректно) ограничение длины текстовых значений \"textValueLengthLimit\""));

            if (string.IsNullOrEmpty(h.Identifier))
            {
                issues.Add(Error("MAP-017", path, "Не задан идентификатор организации \"identifier\""));
            }
            else if (!System.Text.RegularExpressions.Regex.IsMatch(
                         h.Identifier,
                         @"^(3\d{14}|[15]\d{12}|\d{12}|\d{10}|\d{10}(\d{2})?)$"))
            {
                issues.Add(new ValidationIssue
                {
                    Layer = ValidationLayer.MappingStructure,
                    Severity = IssueSeverity.Warning,
                    Code = "MAP-018",
                    File = path,
                    Message = $"Идентификатор \"{h.Identifier}\" не соответствует формату: 15 цифр с ведущей 3, 13 цифр с ведущей 1 или 5, либо 10–12 цифр (регистрационный номер записи по госреестру / ОГРН / ИНН)",
                });
            }

            if (string.IsNullOrEmpty(h.RequestNumberDifp))
            {
                issues.Add(new ValidationIssue
                {
                    Layer = ValidationLayer.MappingStructure,
                    Severity = IssueSeverity.Warning,
                    Code = "MAP-019",
                    File = path,
                    Message = "Не задан номер запроса ДИФП \"requestNumberDifp\" (обязателен для отчетности по запросам)",
                });
            }
            else if (!System.Text.RegularExpressions.Regex.IsMatch(h.RequestNumberDifp, @"^\d{5}_\d{2}$"))
            {
                issues.Add(new ValidationIssue
                {
                    Layer = ValidationLayer.MappingStructure,
                    Severity = IssueSeverity.Warning,
                    Code = "MAP-020",
                    File = path,
                    Message = $"Номер запроса \"{h.RequestNumberDifp}\" не соответствует формату ННННН_ПП (5 цифр, подчёркивание, 2 цифры)",
                });
            }
        }

        // --- dtsReferences ---
        if (doc.DtsReferences is null)
        {
            issues.Add(Error("MAP-021", path, "Отсутствует блок \"dtsReferences\""));
        }
        else
        {
            if (doc.DtsReferences.Type != "schema")
                issues.Add(Error("MAP-022", path,
                    $"Тип ссылки таксономии \"{doc.DtsReferences.Type}\" не поддерживается; ожидается \"schema\""));
            if (string.IsNullOrEmpty(doc.DtsReferences.Href))
                issues.Add(Error("MAP-023", path, "Не задана ссылка на точку входа таксономии \"href\""));
        }

        // --- tables ---
        if (doc.Tables.Count == 0)
        {
            issues.Add(Error("MAP-024", path, "В пакете не описано ни одной таблицы \"tables\""));
        }

        var seenUris = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in doc.Tables)
        {
            var tname = t.TableName;
            if (string.IsNullOrEmpty(t.Uri))
            {
                issues.Add(Error("MAP-025", path, "Таблица без имени файла \"uri\""));
                continue;
            }
            if (!seenUris.Add(t.Uri!))
                issues.Add(Error("MAP-026", path, $"Таблица \"{t.Uri}\" описана повторно"));

            if (string.IsNullOrEmpty(t.RoleUri))
                issues.Add(new ValidationIssue
                {
                    Layer = ValidationLayer.MappingStructure,
                    Severity = IssueSeverity.Warning,
                    Code = "MAP-027",
                    File = path,
                    Table = tname,
                    Message = "Не задан идентификатор роли \"roleUri\"",
                });

            if (t.CsvRowsCount is null || t.CsvRowsCount < 0)
                issues.Add(Error("MAP-028", path, $"Таблица \"{t.Uri}\": не задано (или отрицательно) число строк \"csvRowsCount\""));

            if (t.Columns.Count == 0)
            {
                issues.Add(Error("MAP-029", path, $"Таблица \"{t.Uri}\": не описано ни одной колонки"));
                continue;
            }

            var seenCols = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in t.Columns)
            {
                if (string.IsNullOrWhiteSpace(c.ColumnId))
                {
                    issues.Add(Error("MAP-030", path, $"Таблица \"{t.Uri}\": колонка без \"columnId\""));
                    continue;
                }
                if (!seenCols.Add(c.ColumnId))
                    issues.Add(Error("MAP-031", path, $"Таблица \"{t.Uri}\": колонка \"{c.ColumnId}\" описана повторно"));
            }
        }

        // форма описания колонок: полная (с аспектами) или краткая.
        // колонки-оси (dim_int_*) аспектов не имеют по определению формата
        var factColumns = doc.Tables.Sum(t => t.Columns.Count(c => !c.IsDimension));
        var fullColumns = doc.Tables.Sum(t => t.Columns.Count(c => !c.IsDimension && c.Aspect is not null));
        var form = factColumns == 0 ? "краткая" : fullColumns == factColumns ? "полная" : "смешанная";
        if (form == "смешанная")
            issues.Add(new ValidationIssue
            {
                Layer = ValidationLayer.MappingStructure,
                Severity = IssueSeverity.Warning,
                Code = "MAP-032",
                File = path,
                Message = "Часть колонок описана с аспектами, часть — только идентификаторами (смешанная форма); типы значений для кратких колонок будут взяты из схемы таксономии",
            });
        doc.MappingForm = form;
    }

    internal static bool IsIsoDate(string? value)
    {
        return value is not null
               && System.Text.RegularExpressions.Regex.IsMatch(value, @"^\d{4}-\d{2}-\d{2}$")
               && DateOnly.TryParse(value, out _);
    }

    private static ValidationIssue Error(string code, string file, string message) => new()
    {
        Layer = ValidationLayer.MappingStructure,
        Severity = IssueSeverity.Error,
        Code = code,
        File = file,
        Message = message,
    };
}
