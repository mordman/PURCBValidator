using System.Text.Json;
using System.Text.Json.Serialization;
using PurcbValidator.Core.Models;

namespace PurcbValidator.Core.Taxonomy;

/// <summary>Запись члена перечисления.</summary>
public sealed class EnumMember
{
    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    [JsonPropertyName("label")]
    public string? Label { get; set; }
}

/// <summary>Домен перечисления концепта.</summary>
public sealed class EnumDomain
{
    [JsonPropertyName("domain")]
    public string? Domain { get; set; }

    [JsonPropertyName("members")]
    public List<EnumMember> Members { get; set; } = new();

    [JsonIgnore]
    public HashSet<string> Values => new(Members.Select(m => m.Value), StringComparer.Ordinal);
}

/// <summary>Описание концепта таксономии.</summary>
public sealed class ConceptInfo
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("qname")]
    public string? QName { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("enumDomain")]
    public string? EnumDomain { get; set; }

    [JsonPropertyName("facets")]
    public Dictionary<string, string>? Facets { get; set; }
}

/// <summary>
/// Справочник таксономии ПУРЦБ (загружается для выбранной версии пакета —
/// см. <see cref="TaxonomyVersions"/>): русские метки концептов,
/// домены перечислений и фасеты XSD. Ресурсы разложены по версиям:
/// Resources/6.1.0.10/taxonomy_dict.json, Resources/7.1/taxonomy_dict.json;
/// словари построены по официальным таксономиям Банка России.
/// </summary>
public sealed class TaxonomyDictionary
{
    private readonly Dictionary<string, string> _labels;
    private readonly Dictionary<string, ConceptInfo> _concepts;
    private readonly Dictionary<string, EnumDomain> _enums;
    private readonly Dictionary<string, Dictionary<string, string>> _facets;
    private readonly Dictionary<string, string> _tableTitles;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private TaxonomyDictionary(
        Dictionary<string, string> labels,
        Dictionary<string, ConceptInfo> concepts,
        Dictionary<string, EnumDomain> enums,
        Dictionary<string, Dictionary<string, string>> facets,
        Dictionary<string, string> tableTitles)
    {
        _labels = labels;
        _concepts = concepts;
        _enums = enums;
        _facets = facets;
        _tableTitles = tableTitles;
    }

    /// <summary>
    /// Каталог ресурсов рядом со сборкой Core. Ресурсы разложены по версиям
    /// таксономии: Resources/6.1.0.10/…, Resources/7.1/…
    /// </summary>
    public static string ResourcesRoot
    {
        get
        {
            var asmDir = AppContext.BaseDirectory;
            for (var dir = new DirectoryInfo(asmDir); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "Resources");
                if (Directory.Exists(Path.Combine(candidate, "6.1.0.10")))
                    return candidate;
            }
            return Path.Combine(asmDir, "Resources");
        }
    }

    public static TaxonomyDictionary Load(string? filePath = null)
    {
        var path = filePath ?? Path.Combine(ResourcesRoot, "taxonomy_dict.json");
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var labels = DeserializeMap(root, "labels");
        var titles = DeserializeMap(root, "tableTitles");
        var facets = new Dictionary<string, Dictionary<string, string>>();
        if (root.TryGetProperty("facets", out var fe))
        {
            foreach (var p in fe.EnumerateObject())
            {
                var inner = new Dictionary<string, string>();
                if (p.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var q in p.Value.EnumerateObject())
                        if (q.Value.ValueKind == JsonValueKind.String)
                            inner[q.Name] = q.Value.GetString() ?? "";
                }
                facets[p.Name] = inner;
            }
        }

        var concepts = root.TryGetProperty("concepts", out var ce)
            ? ce.Deserialize<Dictionary<string, ConceptInfo>>(JsonOpts) ?? new()
            : new Dictionary<string, ConceptInfo>();

        var enums = root.TryGetProperty("enums", out var ee)
            ? ee.Deserialize<Dictionary<string, EnumDomain>>(JsonOpts) ?? new()
            : new Dictionary<string, EnumDomain>();

        return new TaxonomyDictionary(labels, concepts, enums, facets, titles);
    }

    private static Dictionary<string, string> DeserializeMap(JsonElement root, string prop)
    {
        var result = new Dictionary<string, string>();
        if (root.TryGetProperty(prop, out var el))
        {
            foreach (var p in el.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.String)
                    result[p.Name] = p.Value.GetString() ?? "";
        }
        return result;
    }

    /// <summary>Метка концепта по QName (purcb-dic:XXX) или домену измерения (dim-int:XXX).</summary>
    public string Label(string qnameOrName)
    {
        if (string.IsNullOrEmpty(qnameOrName)) return "";
        if (_labels.TryGetValue(qnameOrName, out var l)) return l;
        // columnId -> concept: purcb_dic_XXX_dimGrp_1_periodGrp_1
        var concept = ConceptFromColumnId(qnameOrName);
        if (concept is not null && _labels.TryGetValue("purcb-dic:" + concept, out var l2)) return l2;
        // формат 7.1: имя колонки «purcb-dic:Xxx_1» — суффикс экземпляра _N
        var cut = StripInstanceSuffix(qnameOrName);
        if (cut is not null && _labels.TryGetValue(cut, out var l3)) return l3;
        return "";
    }

    /// <summary>Отбрасывает суффикс экземпляра колонки «_N» (purcb-dic:Xxx_1 → purcb-dic:Xxx).</summary>
    private static string? StripInstanceSuffix(string name)
    {
        var cut = name.LastIndexOf('_');
        if (cut <= 0) return null;
        var tail = name[(cut + 1)..];
        return tail.Length > 0 && tail.All(char.IsDigit) ? name[..cut] : null;
    }

    /// <summary>Название концепта из идентификатора колонки маппинга.</summary>
    public static string? ConceptFromColumnId(string columnId)
    {
        if (!columnId.StartsWith("purcb_dic_")) return null;
        var rest = columnId["purcb_dic_".Length..];
        var idx = rest.IndexOf("_dimGrp_", StringComparison.Ordinal);
        return idx <= 0 ? null : rest[..idx];
    }

    /// <summary>QName концепта из идентификатора колонки маппинга.</summary>
    public string? QNameFromColumnId(string columnId)
    {
        // формат 6.1: purcb_dic_XXX_dimGrp_1_periodGrp_1
        var c = ConceptFromColumnId(columnId);
        if (c is not null) return "purcb-dic:" + c;
        // формат 7.1: имя колонки «purcb-dic:Xxx_1» (возможно, с суффиксом _N)
        if (columnId.Contains(':'))
        {
            var cut = StripInstanceSuffix(columnId);
            if (cut is not null && cut.Contains(':')) return cut;
        }
        return null;
    }

    /// <summary>Домен перечисления для концепта (или null).</summary>
    public EnumDomain? EnumDomainFor(string qname)
        => _enums.TryGetValue(qname, out var d) ? d : null;

    /// <summary>Фасеты XSD концепта (pattern, totalDigits, ...).</summary>
    public IReadOnlyDictionary<string, string>? FacetsFor(string qname)
        => _facets.TryGetValue(qname, out var f) ? f : null;

    /// <summary>Название таблицы по её имени (sr_R1 -> "Раздел 1 ...").</summary>
    public string TableTitle(string tableName)
        => _tableTitles.TryGetValue(tableName, out var t) ? t : "";

    /// <summary>Тип XBRL концепта (xbrli:stringItemType, ...).</summary>
    public string? ConceptType(string qname)
        => _concepts.TryGetValue(qname, out var c) ? c.Type : null;
}
