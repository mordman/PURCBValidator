using System.Text.Json.Serialization;

namespace PurcbValidator.Core.Models;

/// <summary>
/// Типизированная модель служебного файла mapping.json (DRAFT) в формате
/// XBRL-CSV 2.0 Банка России (контекст "www.cbr.ru/xbrl_csv2", УОД ПУРЦБ 6.1.0.10).
/// </summary>
public sealed class MappingDocument
{
    [JsonPropertyName("@context")]
    public string? Context { get; set; }

    [JsonPropertyName("header")]
    public MappingHeader? Header { get; set; }

    [JsonPropertyName("dtsReferences")]
    public DtsReferences? DtsReferences { get; set; }

    [JsonPropertyName("tables")]
    public List<MappingTable> Tables { get; set; } = new();

    /// <summary>Форма описания колонок: "полная", "краткая" или "смешанная".</summary>
    [JsonIgnore]
    public string MappingForm { get; set; } = "";

    [JsonIgnore]
    public string SourcePath { get; set; } = "";
}

public sealed class MappingHeader
{
    [JsonPropertyName("encoding")]
    public string? Encoding { get; set; }

    [JsonPropertyName("delimiter")]
    public string? Delimiter { get; set; }

    [JsonPropertyName("reportDate")]
    public string? ReportDate { get; set; }

    [JsonPropertyName("textQualifier")]
    public string? TextQualifier { get; set; }

    [JsonPropertyName("documentVersion")]
    public string? DocumentVersion { get; set; }

    [JsonPropertyName("textValueLengthLimit")]
    public long? TextValueLengthLimit { get; set; }

    /// <summary>Регистрационный номер записи по государственному реестру (13 цифр).</summary>
    [JsonPropertyName("identifier")]
    public string? Identifier { get; set; }

    [JsonPropertyName("requestNumberDifp")]
    public string? RequestNumberDifp { get; set; }
}

public sealed class DtsReferences
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("href")]
    public string? Href { get; set; }
}

public sealed class MappingTable
{
    [JsonPropertyName("uri")]
    public string? Uri { get; set; }

    [JsonPropertyName("roleUri")]
    public string? RoleUri { get; set; }

    [JsonPropertyName("csvRowsCount")]
    public long? CsvRowsCount { get; set; }

    [JsonPropertyName("typedFiltering")]
    public bool? TypedFiltering { get; set; }

    [JsonPropertyName("columns")]
    public List<MappingColumn> Columns { get; set; } = new();

    /// <summary>Имя таблицы без расширения (sr_R1 и т.п.).</summary>
    [JsonIgnore]
    public string TableName => Path.GetFileNameWithoutExtension(Uri ?? "");
}

public sealed class MappingColumn
{
    [JsonPropertyName("columnId")]
    public string ColumnId { get; set; } = "";

    [JsonPropertyName("xbrldi:typedMember")]
    public TypedMemberSpec? TypedMember { get; set; }

    [JsonPropertyName("aspect")]
    public AspectSpec? Aspect { get; set; }

    [JsonPropertyName("xbrli:period")]
    public PeriodSpec? Period { get; set; }

    [JsonPropertyName("flatDimension")]
    public bool? FlatDimension { get; set; }

    /// <summary>Это колонка-ось (типизированное измерение dim_int_*).</summary>
    [JsonIgnore]
    public bool IsDimension => TypedMember is not null || ColumnId.StartsWith("dim_int_");
}

public sealed class TypedMemberSpec
{
    [JsonPropertyName("dimension")]
    public string? Dimension { get; set; }

    [JsonPropertyName("typedDomain")]
    public string? TypedDomain { get; set; }
}

public sealed class AspectSpec
{
    [JsonPropertyName("type")]
    public AspectTypeSpec? Type { get; set; }

    [JsonPropertyName("xbrl:concept")]
    public string? Concept { get; set; }
}

public sealed class AspectTypeSpec
{
    [JsonPropertyName("datatype")]
    public string? DataType { get; set; }

    [JsonPropertyName("http://www.cbr.ru/xbrl-csv/model#columnType")]
    public string? ColumnType { get; set; }

    [JsonPropertyName("xbrli:unit")]
    public UnitSpec? Unit { get; set; }
}

public sealed class UnitSpec
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("xbrli:measure")]
    public string? Measure { get; set; }
}

public sealed class PeriodSpec
{
    [JsonPropertyName("periodType")]
    public string? PeriodType { get; set; }

    [JsonPropertyName("xbrli:instant")]
    public string? Instant { get; set; }

    [JsonPropertyName("xbrli:periodStart")]
    public string? PeriodStart { get; set; }

    [JsonPropertyName("xbrli:periodEnd")]
    public string? PeriodEnd { get; set; }
}
