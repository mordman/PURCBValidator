using System.Text.Json;
using System.Text.Json.Serialization;

namespace PurcbValidator.Core.Models;

/// <summary>
/// Конвертер списка колонок: в формате 7.1 массив содержит строки-маркеры
/// ("without_typed_filter") вперемешку с описаниями колонок; маркеры
/// в заголовок CSV не входят и в список колонок не попадают.
/// </summary>
public sealed class MappingColumnListConverter : JsonConverter<List<MappingColumn>>
{
    public override List<MappingColumn>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var result = new List<MappingColumn>();
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Ожидается массив колонок");
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray) break;
            if (reader.TokenType == JsonTokenType.String)
            {
                reader.GetString(); // маркер (without_typed_filter) — пропускаем
                continue;
            }
            result.Add((MappingColumn)JsonSerializer.Deserialize(ref reader, typeof(MappingColumn), options)!);
        }
        return result;
    }

    public override void Write(Utf8JsonWriter writer, List<MappingColumn> value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value, options);
}

/// <summary>
/// Типизированная модель служебного файла пакета (DRAFT) в формате
/// XBRL-CSV Банка России. Поддерживаются два формата:
///  * УОД ПУРЦБ 6.1.0.10 — контекст "www.cbr.ru/xbrl_csv2", блок "header",
///    колонки с "columnId" (mapping.json);
///  * таксономия 7.1 (10-РД) — контекст "http://www.w3.org/ns/csvw", поля
///    кодировки и реквизитов на верхнем уровне, колонки с "name" и аспектами
///    в "http://www.cbr.ru/xbrl-csv/model#properties" (Service_*.json).
/// </summary>
public sealed class MappingDocument
{
    [JsonPropertyName("@context")]
    public string? Context { get; set; }

    [JsonPropertyName("header")]
    public MappingHeader? Header { get; set; }

    // --- 7.1 (10-РД): те же поля на верхнем уровне служебного файла ---
    [JsonPropertyName("encoding")]
    public string? TopEncoding { get; set; }

    [JsonPropertyName("delimiter")]
    public string? TopDelimiter { get; set; }

    [JsonPropertyName("reportDate")]
    public string? TopReportDate { get; set; }

    [JsonPropertyName("textQualifier")]
    public string? TopTextQualifier { get; set; }

    [JsonPropertyName("documentVersion")]
    public string? TopDocumentVersion { get; set; }

    [JsonPropertyName("textValueLengthLimit")]
    public long? TopTextValueLengthLimit { get; set; }

    [JsonPropertyName("identifier")]
    public string? TopIdentifier { get; set; }

    [JsonPropertyName("documentType")]
    public string? DocumentType { get; set; }

    [JsonPropertyName("dtsReferences")]
    public DtsReferences? DtsReferences { get; set; }

    [JsonPropertyName("tables")]
    public List<MappingTable> Tables { get; set; } = new();

    /// <summary>Форма описания колонок: "полная", "краткая" или "смешанная".</summary>
    [JsonIgnore]
    public string MappingForm { get; set; } = "";

    /// <summary>Служебный файл формата 7.1 (поля на верхнем уровне, без блока header).</summary>
    [JsonIgnore]
    public bool FlatHeaderFormat { get; set; }

    [JsonIgnore]
    public string SourcePath { get; set; } = "";

    /// <summary>Приводит оба формата к единому виду (блок header, идентификаторы колонок).</summary>
    public void Normalize()
    {
        if (Header is null &&
            (TopEncoding is not null || TopDelimiter is not null || TopIdentifier is not null))
        {
            FlatHeaderFormat = true;
            Header = new MappingHeader
            {
                Encoding = TopEncoding,
                Delimiter = TopDelimiter,
                ReportDate = TopReportDate,
                TextQualifier = TopTextQualifier,
                DocumentVersion = TopDocumentVersion,
                TextValueLengthLimit = TopTextValueLengthLimit,
                Identifier = TopIdentifier,
                // requestNumberDifp в формате 7.1 отсутствует: 10-РД — регулярная
                // отчётность, а не отчётность по запросам ДИФП
            };
        }

        foreach (var t in Tables)
            foreach (var c in t.Columns)
                c.Normalize();
    }
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
    [JsonConverter(typeof(MappingColumnListConverter))]
    public List<MappingColumn> Columns { get; set; } = new();

    /// <summary>Имя таблицы без расширения (sr_R1, XBRL_..._1 и т.п.).</summary>
    [JsonIgnore]
    public string TableName => Path.GetFileNameWithoutExtension(Uri ?? "");

    /// <summary>Ключ таблицы по roleUri (SR_0420417); для 6.1 совпадает с TableName.</summary>
    [JsonIgnore]
    public string RoleKey
    {
        get
        {
            var parts = RoleUri?.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return parts is { Length: > 0 } ? parts[^1] : TableName;
        }
    }
}

public sealed class MappingColumn
{
    [JsonPropertyName("columnId")]
    public string ColumnId { get; set; } = "";

    /// <summary>Имя колонки в формате 7.1 ("name" вместо "columnId").</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("xbrldi:typedMember")]
    public TypedMemberSpec? TypedMember { get; set; }

    [JsonPropertyName("aspect")]
    public AspectSpec? Aspect { get; set; }

    /// <summary>Аспекты колонки в формате 7.1 (ключ "http://www.cbr.ru/xbrl-csv/model#properties").</summary>
    [JsonPropertyName("http://www.cbr.ru/xbrl-csv/model#properties")]
    public AspectSpec? ModelProperties { get; set; }

    [JsonPropertyName("xbrli:period")]
    public PeriodSpec? Period { get; set; }

    [JsonPropertyName("flatDimension")]
    public bool? FlatDimension { get; set; }

    /// <summary>Это колонка-ось (типизированное измерение dim_int_*/dim-int:*).</summary>
    [JsonIgnore]
    public bool IsDimension => TypedMember is not null
        || (ModelProperties ?? Aspect)?.TypedMemberMap is { Count: > 0 }
        || ColumnId.StartsWith("dim_int_", StringComparison.Ordinal);

    /// <summary>Ключ колонки, единый для обоих форматов ("columnId" либо "name").</summary>
    [JsonIgnore]
    public string Key => ColumnId.Length > 0 ? ColumnId : Name ?? "";

    /// <summary>Концепт колонки (в 7.1 задаётся явно, в 6.1 выводится из columnId).</summary>
    [JsonIgnore]
    public string? Concept
    {
        get
        {
            var c = (ModelProperties ?? Aspect)?.Concept;
            if (c is null) return null;
            // 6.1 записывает QName с подчёркиванием (purcb-dic_Xxx),
            // 7.1 и словари таксономии — с двоеточием (purcb-dic:Xxx)
            if (c.Contains(':')) return c;
            var idx = c.IndexOf('_');
            return idx > 0 ? c[..idx] + ":" + c[(idx + 1)..] : c;
        }
    }

    /// <summary>Приводит колонку к единому виду: ColumnId из "name", аспекты из model#properties.</summary>
    public void Normalize()
    {
        if (ColumnId.Length == 0 && !string.IsNullOrEmpty(Name))
            ColumnId = Name!;
        // 7.1: измерение вида {"dim-int:Ось": "dim-int:Домен"} внутри model#properties
        var map = (ModelProperties ?? Aspect)?.TypedMemberMap;
        if (TypedMember is null && map is { Count: > 0 })
        {
            var kv = map.First();
            TypedMember = new TypedMemberSpec { Dimension = kv.Key, TypedDomain = kv.Value };
        }
        if (ModelProperties is not null && Aspect is null)
            Aspect = ModelProperties;
    }
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

    /// <summary>Период в формате 7.1: {"xbrli:instant": "2026-01-31"}.</summary>
    [JsonPropertyName("xbrli:periodType")]
    public PeriodSpec? PeriodType { get; set; }

    /// <summary>Типизированное измерение в формате 7.1: {"dim-int:Ось": "dim-int:Домен"}.</summary>
    [JsonPropertyName("xbrldi:typedMember")]
    public Dictionary<string, string>? TypedMemberMap { get; set; }

    /// <summary>Признак "домен не определён" у колонок-перечислений 7.1.</summary>
    [JsonPropertyName("not_specified_domain")]
    public bool? NotSpecifiedDomain { get; set; }
}

public sealed class AspectTypeSpec
{
    [JsonPropertyName("datatype")]
    public string? DataType { get; set; }

    [JsonPropertyName("http://www.cbr.ru/xbrl-csv/model#columnType")]
    public string? ColumnType { get; set; }

    /// <summary>Точность десятичного значения (decimals) в формате 7.1.</summary>
    [JsonPropertyName("decimals")]
    public long? Decimals { get; set; }

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
