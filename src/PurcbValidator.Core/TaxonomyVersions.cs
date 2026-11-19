using PurcbValidator.Core.Taxonomy;

namespace PurcbValidator.Core;

/// <summary>Описание одной поддерживаемой версии таксономии Банка России.</summary>
public sealed record TaxonomyVersionInfo
{
    /// <summary>Идентификатор версии (совпадает с именем каталога ресурсов).</summary>
    public required string Id { get; init; }

    /// <summary>Человекочитаемое название (приложение, набор).</summary>
    public required string DisplayName { get; init; }

    /// <summary>$id схемы точки входа (draftSchema).</summary>
    public required string EntrySchemaId { get; init; }

    /// <summary>Подстроки dtsReferences.href, по которым пакет относится к версии.</summary>
    public required string[] HrefMarkers { get; init; }

    /// <summary>Опубликован ли набор контрольных соотношений для версии.</summary>
    public required bool HasRules { get; init; }

    /// <summary>Каталог ресурсов версии (схемы, словарь, правила).</summary>
    public string ResourcesDir => Path.Combine(TaxonomyDictionary.ResourcesRoot, Id);
}

/// <summary>
/// Каталог поддерживаемых версий таксономии. На ноябрь 2026 года Банк России
/// публикует материалы XBRL-CSV ПУРЦБ двух поколений:
///  * УОД ПУРЦБ 6.1.0.10 — учетно-операционные данные (набор 20260403/20260430,
///    отчётность с апреля 2026 года) — 261 контрольное соотношение;
///  * таксономия XBRL Банка России 7.1 — точка входа 10-РД 0420417
///    (набор 2025-12-30/20251230): отчётность 10-РД больших объёмов в XBRL-CSV.
/// </summary>
public static class TaxonomyVersions
{
    /// <summary>УОД ПУРЦБ, таксономия 6.1.0.10 (набор 20260403/20260430).</summary>
    public static readonly TaxonomyVersionInfo Uod61010 = new()
    {
        Id = "6.1.0.10",
        DisplayName = "УОД ПУРЦБ 6.1.0.10 (набор 20260403/20260430)",
        EntrySchemaId = "http://www.cbr.ru/xbrl_csv/20260403/20260430/ep_nso_purcb_oper_nr_uod_reestr.def.json",
        HrefMarkers = new[]
        {
            "xbrl_csv/20260403/20260430",
            "xbrl_csv2/20260403/20260430",
            "ep_nso_purcb_oper_nr_uod_reestr",
        },
        HasRules = true,
    };

    /// <summary>Таксономия XBRL Банка России 7.1, точка входа 10-РД 0420417 (набор 2025-12-30/20251230).</summary>
    public static readonly TaxonomyVersionInfo Purcb71 = new()
    {
        Id = "7.1",
        DisplayName = "10-РД ПУРЦБ, таксономия 7.1 (набор 2025-12-30/20251230)",
        EntrySchemaId = "http://www.cbr.ru/xbrl_csv/2025-12-30/20251230/ep_nso_purcb_m_q_y_10rd_reestr_0420417.def.ep.json",
        HrefMarkers = new[]
        {
            "xbrl_csv/2025-12-30/20251230",
            "ep_nso_purcb_m_q_y_10rd_reestr",
        },
        HasRules = false,
    };

    /// <summary>Все поддерживаемые версии (от новых к старым).</summary>
    public static IReadOnlyList<TaxonomyVersionInfo> All { get; } = new[] { Uod61010, Purcb71 };

    /// <summary>Версия по идентификатору ("6.1.0.10", "7.1") или null.</summary>
    public static TaxonomyVersionInfo? Find(string? id)
        => All.FirstOrDefault(v => string.Equals(v.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Автоопределение версии по ссылке dtsReferences.href служебного файла пакета
    /// (ссылка содержит набор и имя точки входа таксономии). null — не опознана.
    /// </summary>
    public static TaxonomyVersionInfo? Detect(string? dtsHref)
    {
        if (string.IsNullOrEmpty(dtsHref)) return null;
        // сначала точное сравнение с точкой входа, затем поиск маркеров набора
        var exact = All.FirstOrDefault(v =>
            string.Equals(v.EntrySchemaId, dtsHref.Trim(), StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;
        return All.FirstOrDefault(v => v.HrefMarkers.Any(m => dtsHref.Contains(m, StringComparison.OrdinalIgnoreCase)));
    }
}
