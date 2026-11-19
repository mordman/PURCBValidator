using System.Text.Json.Serialization;

namespace PurcbValidator.Core.Models;

/// <summary>
/// Модель набора бизнес-правил rules.json.
/// Формат сгенерирован из официальных контрольных соотношений ЦБ РФ
/// (valueAssertion/existenceAssertion, апрель 2026) и может пополняться вручную.
/// </summary>
public sealed class RuleSetFile
{
    [JsonPropertyName("ruleSet")]
    public string? RuleSetName { get; set; }

    [JsonPropertyName("taxonomyVersion")]
    public string? TaxonomyVersion { get; set; }

    [JsonPropertyName("taxonomyIdentifier")]
    public string? TaxonomyIdentifier { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("rules")]
    public List<BusinessRule> Rules { get; set; } = new();
}

public sealed class BusinessRule
{
    /// <summary>Код правила (совпадает с кодом формулы ЦБ).</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("table")]
    public string Table { get; set; } = "";

    /// <summary>error | warning.</summary>
    [JsonPropertyName("severity")]
    public string? Severity { get; set; } = "warning";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    /// <summary>
    /// Тип проверки: required, requiredIf, requiredAnyOf, requiredIn, requiredNotIn,
    /// forbiddenIf, forbiddenValuesIf, allowedValuesIf, matchesPatternIf, valueConjunction.
    /// </summary>
    [JsonPropertyName("check")]
    public string Check { get; set; } = "";

    [JsonPropertyName("column")]
    public string? Column { get; set; }

    [JsonPropertyName("columns")]
    public List<string>? Columns { get; set; }

    [JsonPropertyName("values")]
    public List<string>? Values { get; set; }

    [JsonPropertyName("pattern")]
    public string? Pattern { get; set; }

    [JsonPropertyName("conditions")]
    public List<ValueCondition>? Conditions { get; set; }

    [JsonPropertyName("when")]
    public RuleWhen? When { get; set; }
}

public sealed class ValueCondition
{
    [JsonPropertyName("column")]
    public string Column { get; set; } = "";

    [JsonPropertyName("values")]
    public List<string> Values { get; set; } = new();
}

/// <summary>Условия применимости правила к строке.</summary>
public sealed class RuleWhen
{
    /// <summary>Правило применяется, если хотя бы одна из колонок заполнена.</summary>
    [JsonPropertyName("anyFilled")]
    public List<string>? AnyFilled { get; set; }

    /// <summary>Правило применяется, если все перечисленные колонки заполнены.</summary>
    [JsonPropertyName("allFilled")]
    public List<string>? AllFilled { get; set; }

    /// <summary>Правило применяется, если колонка принимает одно из значений.</summary>
    [JsonPropertyName("valueIn")]
    public List<ValueCondition>? ValueIn { get; set; }

    /// <summary>Правило применяется, если колонка не принимает ни одного из значений.</summary>
    [JsonPropertyName("valueNotIn")]
    public List<ValueCondition>? ValueNotIn { get; set; }

    /// <summary>Правило применяется, если значение колонки соответствует регулярному выражению.</summary>
    [JsonPropertyName("valueMatches")]
    public List<PatternCondition>? ValueMatches { get; set; }

    /// <summary>Сравнение дат (left op right, возможны $reportDate и сдвиги в днях).</summary>
    [JsonPropertyName("dateRules")]
    public List<DateRule>? DateRules { get; set; }
}

public sealed class PatternCondition
{
    [JsonPropertyName("column")]
    public string Column { get; set; } = "";

    /// <summary>Шаблон в синтаксисе PostgreSQL regexp_match.</summary>
    [JsonPropertyName("pattern")]
    public string Pattern { get; set; } = "";
}

public sealed class DateRule
{
    /// <summary>Левая дата: колонка или "$reportDate".</summary>
    [JsonPropertyName("left")]
    public string Left { get; set; } = "";

    /// <summary>Правая дата: колонка или "$reportDate".</summary>
    [JsonPropertyName("right")]
    public string Right { get; set; } = "";

    /// <summary>ge | le | gt | lt | eq.</summary>
    [JsonPropertyName("op")]
    public string Op { get; set; } = "ge";

    /// <summary>Сдвиг в днях, применяемый к левой дате (по умолчанию 0).</summary>
    [JsonPropertyName("days")]
    public long? Days { get; set; }
}

/// <summary>Справочный каталог всех контрольных соотношений ЦБ (rules_reference.json).</summary>
public sealed class RulesReferenceFile
{
    [JsonPropertyName("formulas")]
    public List<ReferenceFormula> Formulas { get; set; } = new();
}

public sealed class ReferenceFormula
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("table")]
    public string Table { get; set; } = "";

    [JsonPropertyName("severity")]
    public string? Severity { get; set; }

    [JsonPropertyName("severityCbr")]
    public string? SeverityCbr { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("resultExpr")]
    public string? ResultExpr { get; set; }

    [JsonPropertyName("where")]
    public string? Where { get; set; }

    [JsonPropertyName("crossTable")]
    public bool CrossTable { get; set; }

    [JsonPropertyName("usedTables")]
    public List<string>? UsedTables { get; set; }
}
