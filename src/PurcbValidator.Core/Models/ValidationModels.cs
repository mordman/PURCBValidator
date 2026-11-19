namespace PurcbValidator.Core.Models;

/// <summary>Критичность замечания валидации.</summary>
public enum IssueSeverity
{
    /// <summary>Ошибка — пакет не пройдёт приёмку.</summary>
    Error,

    /// <summary>Предупреждение (в т.ч. уровень WARNING контрольных соотношений ЦБ).</summary>
    Warning,

    /// <summary>Информационное замечание.</summary>
    Info,
}

/// <summary>Слой (этап) валидации.</summary>
public static class ValidationLayer
{
    public const string Package = "Пакет";
    public const string MappingStructure = "Структура mapping.json";
    public const string TaxonomySchema = "Схема таксономии ЦБ";
    public const string CsvData = "CSV-данные";
    public const string BusinessRules = "Бизнес-правила";
    public const string TaxonomyChecks = "Проверки таксономии";
}

/// <summary>Одно замечание валидации.</summary>
public sealed class ValidationIssue
{
    /// <summary>Слой валидации (см. <see cref="ValidationLayer"/>).</summary>
    public string Layer { get; init; } = "";

    public IssueSeverity Severity { get; init; } = IssueSeverity.Error;

    /// <summary>Код замечания (MAP-001, CSV-014, RULE-valueAssertion_R1_05, ...).</summary>
    public string Code { get; init; } = "";

    /// <summary>Файл, к которому относится замечание.</summary>
    public string? File { get; init; }

    /// <summary>Таблица (sr_R1, sr_sved_purcb, ...).</summary>
    public string? Table { get; init; }

    /// <summary>Номер строки данных (1-based, без строки заголовка); null для файловых замечаний.</summary>
    public long? Row { get; init; }

    /// <summary>Идентификатор колонки.</summary>
    public string? Column { get; init; }

    /// <summary>Человекочитаемое название колонки (метка из таксономии).</summary>
    public string? ColumnLabel { get; init; }

    /// <summary>Текст замечания на русском языке.</summary>
    public string Message { get; init; } = "";

    /// <summary>Значение ячейки (для контекста).</summary>
    public string? Value { get; init; }

    public override string ToString() =>
        $"[{Severity}] {Layer} | {Code} | {File ?? ""} | строка {Row?.ToString() ?? "-"} | {Column ?? ""} | {Message}";
}

/// <summary>Сводная информация о пакете.</summary>
public sealed class PackageInfo
{
    public string MappingPath { get; init; } = "";
    public string? ReportDate { get; init; }
    public string? Identifier { get; init; }
    public string? RequestNumberDifp { get; init; }
    public string? DocumentVersion { get; init; }
    public string? DtsHref { get; init; }
    public string? Delimiter { get; init; }
    public string? TextQualifier { get; init; }
    public string? Encoding { get; init; }
    public long? TextValueLengthLimit { get; init; }
    public List<PackageTableInfo> Tables { get; init; } = new();
    public string MappingForm { get; init; } = "";   // "полная" | "краткая" | ""
}

/// <summary>Сводка по одной таблице пакета.</summary>
public sealed class PackageTableInfo
{
    public string Uri { get; init; } = "";
    public string Table { get; init; } = "";
    public string Title { get; init; } = "";
    public long? DeclaredRows { get; init; }
    public long? ActualRows { get; init; }
    public int ColumnCount { get; init; }
    public bool TypedFiltering { get; init; }
}

/// <summary>Итоговый отчёт валидации.</summary>
public sealed class ValidationReport
{
    public PackageInfo Package { get; init; } = new();
    public List<ValidationIssue> Issues { get; init; } = new();
    public DateTime StartedUtc { get; init; } = DateTime.UtcNow;
    public DateTime FinishedUtc { get; init; } = DateTime.UtcNow;

    public int ErrorCount => Issues.Count(i => i.Severity == IssueSeverity.Error);
    public int WarningCount => Issues.Count(i => i.Severity == IssueSeverity.Warning);
    public int InfoCount => Issues.Count(i => i.Severity == IssueSeverity.Info);

    /// <summary>Правило считается пройденным только при отсутствии ошибок.</summary>
    public bool Passed => ErrorCount == 0;
}
