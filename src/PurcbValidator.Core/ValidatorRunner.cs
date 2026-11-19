using PurcbValidator.Core.Csv;
using PurcbValidator.Core.Mapping;
using PurcbValidator.Core.Models;
using PurcbValidator.Core.Rules;
using PurcbValidator.Core.Taxonomy;

namespace PurcbValidator.Core;

/// <summary>Параметры запуска валидации.</summary>
public sealed class ValidationOptions
{
    /// <summary>Путь к служебному файлу пакета (mapping.json / Service_*.json) или к папке пакета.</summary>
    public string Path { get; init; } = "";

    /// <summary>Проверять служебный файл по официальным JSON-схемам ЦБ (слой 2).</summary>
    public bool UseCbrSchema { get; init; } = true;

    /// <summary>
    /// Версия таксономии: идентификатор из <see cref="TaxonomyVersions"/>
    /// ("6.1.0.10", "7.1") либо null/"auto" — автоопределение по dtsReferences.href пакета.
    /// </summary>
    public string? TaxonomyVersion { get; init; }

    /// <summary>Путь к альтернативному файлу правил (rules.json).</summary>
    public string? RulesFile { get; init; }
}

/// <summary>
/// Конвейер валидации пакета ПУРЦБ в формате XBRL-CSV Банка России:
/// слой 1 — структура служебного файла; слой 2 — JSON-схемы ЦБ; слой 3 — CSV-данные;
/// слой 4 — контрольные соотношения ЦБ и встроенные проверки пакета.
/// Поддерживаемые таксономии: УОД ПУРЦБ 6.1.0.10 и 10-РД 7.1 (см. <see cref="TaxonomyVersions"/>);
/// версия выбирается вручную или определяется автоматически по ссылке пакета.
/// </summary>
public sealed class ValidatorRunner
{
    /// <summary>Запускает полную валидацию пакета.</summary>
    public ValidationReport Run(ValidationOptions options, Action<string>? progress = null)
    {
        var started = DateTime.UtcNow;
        var issues = new List<ValidationIssue>();

        progress?.Invoke("Поиск служебного файла пакета…");
        var mappingPath = ResolveMappingPath(options.Path, issues);
        if (mappingPath is null)
        {
            return new ValidationReport
            {
                StartedUtc = started,
                FinishedUtc = DateTime.UtcNow,
                Issues = issues,
                Package = new PackageInfo { MappingPath = options.Path },
            };
        }

        // --- слой 1: структура служебного файла ---
        progress?.Invoke("Слой 1/4: структура служебного файла…");
        var loader = new MappingLoader();
        var mapping = loader.Load(mappingPath, issues);
        if (mapping is null)
        {
            return Finish(started, issues, null, "", "");
        }

        // --- определение версии таксономии ---
        var (version, versionSource) = ResolveVersion(options, mapping, issues);
        progress?.Invoke($"Версия таксономии: {version.DisplayName} ({versionSource})");

        // --- слой 2: официальные JSON-схемы ЦБ ---
        if (options.UseCbrSchema)
        {
            progress?.Invoke("Слой 2/4: соответствие схемам таксономии ЦБ…");
            try
            {
                var schemaValidator = new CbrSchemaValidator(
                    Path.Combine(version.ResourcesDir, "cbr_schemas"),
                    version.EntrySchemaId);
                schemaValidator.Validate(File.ReadAllText(mappingPath), mappingPath, issues);
            }
            catch (Exception ex)
            {
                issues.Add(new ValidationIssue
                {
                    Layer = ValidationLayer.TaxonomySchema,
                    Severity = IssueSeverity.Warning,
                    Code = "SCH-500",
                    File = mappingPath,
                    Message = $"Проверка по схеме ЦБ не выполнена: {ex.Message}",
                });
            }
        }

        // --- слой 3: CSV ---
        progress?.Invoke("Слой 3/4: CSV-файлы…");
        var taxonomy = TaxonomyDictionary.Load(Path.Combine(version.ResourcesDir, "taxonomy_dict.json"));
        Dictionary<string, CsvTableData> tables;
        try
        {
            var csvValidator = new CsvPackageValidator(taxonomy);
            tables = csvValidator.Validate(mapping, issues, progress);
        }
        catch (Exception ex)
        {
            issues.Add(new ValidationIssue
            {
                Layer = ValidationLayer.CsvData,
                Severity = IssueSeverity.Error,
                Code = "CSV-900",
                File = mappingPath,
                Message = $"Ошибка при проверке CSV: {ex.Message}",
            });
            tables = new Dictionary<string, CsvTableData>();
        }

        // --- слой 4: бизнес-правила ---
        progress?.Invoke("Слой 4/4: контрольные соотношения ЦБ…");
        var rulesPath = options.RulesFile ?? Path.Combine(version.ResourcesDir, "rules.json");
        var (ruleSet, ruleError) = RuleSetLoader.Load(rulesPath);
        if (ruleError is not null)
        {
            issues.Add(new ValidationIssue
            {
                Layer = ValidationLayer.BusinessRules,
                Severity = IssueSeverity.Warning,
                Code = "RULE-000",
                File = mappingPath,
                Message = ruleError,
            });
        }
        // встроенные проверки пакета (BUILTIN-UNIQUE, BUILTIN-PRESENCE) выполняются
        // движком правил; для 7.1 (набор КС не публиковался) слой пропускается целиком,
        // если пользователь не передал собственный файл правил
        var engineApplies = ruleSet is not null && (version.HasRules || options.RulesFile is not null);
        if (engineApplies)
        {
            try
            {
                var engine = new RuleEngine(taxonomy);
                engine.Execute(mapping, tables, ruleSet!.Rules, issues);
            }
            catch (Exception ex)
            {
                issues.Add(new ValidationIssue
                {
                    Layer = ValidationLayer.BusinessRules,
                    Severity = IssueSeverity.Error,
                    Code = "RULE-900",
                    File = mappingPath,
                    Message = $"Ошибка при выполнении правил: {ex.Message}",
                });
            }
        }
        if (ruleSet is not null && ruleSet.Rules.Count == 0)
        {
            issues.Add(new ValidationIssue
            {
                Layer = ValidationLayer.BusinessRules,
                Severity = IssueSeverity.Info,
                Code = "RULE-001",
                File = mappingPath,
                Message = version.HasRules
                    ? "Набор контрольных соотношений пуст — выполнены только встроенные проверки пакета (BUILTIN-UNIQUE, BUILTIN-PRESENCE)"
                    : "Для таксономии 7.1 (10-РД) отдельный набор контрольных соотношений " +
                      "Банком России не публиковался: формулы включены в состав таксономии " +
                      "(SR_0420417-formula.xml и др.) — слой 4 не выполнялся",
            });
        }

        progress?.Invoke("Формирование отчёта…");
        var package = BuildPackageInfo(mapping, tables, taxonomy, version, versionSource);
        return Finish(started, issues, package, version.Id, versionSource);
    }

    /// <summary>
    /// Определяет версию таксономии: явный выбор из параметров либо
    /// автоопределение по dtsReferences.href; при неудаче — УОД 6.1.0.10.
    /// </summary>
    private static (TaxonomyVersionInfo Version, string Source) ResolveVersion(
        ValidationOptions options,
        MappingDocument mapping,
        List<ValidationIssue> issues)
    {
        var requested = options.TaxonomyVersion?.Trim();
        if (!string.IsNullOrEmpty(requested) &&
            !string.Equals(requested, "auto", StringComparison.OrdinalIgnoreCase))
        {
            var explicitVersion = TaxonomyVersions.Find(requested);
            if (explicitVersion is not null)
                return (explicitVersion, "выбрана вручную");

            issues.Add(new ValidationIssue
            {
                Layer = ValidationLayer.Package,
                Severity = IssueSeverity.Warning,
                Code = "PKG-010",
                File = mapping.SourcePath,
                Message = $"Неизвестная версия таксономии \"{requested}\"; поддерживаются: " +
                          string.Join(", ", TaxonomyVersions.All.Select(v => v.Id)) +
                          ". Применено автоопределение по пакету",
            });
        }

        var detected = TaxonomyVersions.Detect(mapping.DtsReferences?.Href);
        if (detected is not null)
            return (detected, "автоопределение по пакету");

        issues.Add(new ValidationIssue
        {
            Layer = ValidationLayer.Package,
            Severity = IssueSeverity.Warning,
            Code = "PKG-011",
            File = mapping.SourcePath,
            Message = $"Не удалось определить версию таксономии по ссылке dtsReferences.href " +
                      $"\"{mapping.DtsReferences?.Href ?? ""}\"; используется УОД ПУРЦБ 6.1.0.10",
        });
        return (TaxonomyVersions.Uod61010, "версия по умолчанию");
    }

    private static string? ResolveMappingPath(string path, List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            issues.Add(new ValidationIssue
            {
                Layer = ValidationLayer.Package,
                Severity = IssueSeverity.Error,
                Code = "PKG-000",
                Message = "Не указан путь к пакету",
            });
            return null;
        }

        if (Directory.Exists(path))
        {
            // 6.1: mapping.json; 7.1: Service_*.json
            var candidates = Directory.EnumerateFiles(path, "mapping.json").ToList();
            if (candidates.Count == 0)
                candidates = Directory.EnumerateFiles(path, "Service_*.json")
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            if (candidates.Count == 0)
            {
                issues.Add(new ValidationIssue
                {
                    Layer = ValidationLayer.Package,
                    Severity = IssueSeverity.Error,
                    Code = "PKG-001",
                    Message = $"В папке \"{path}\" не найден служебный файл пакета (mapping.json или Service_*.json)",
                });
                return null;
            }
            return candidates[0];
        }

        if (File.Exists(path))
        {
            return path; // допускаем произвольное имя служебного файла
        }

        issues.Add(new ValidationIssue
        {
            Layer = ValidationLayer.Package,
            Severity = IssueSeverity.Error,
            Code = "PKG-002",
            Message = $"Путь не найден: {path}",
        });
        return null;
    }

    private static PackageInfo BuildPackageInfo(
        MappingDocument mapping,
        IReadOnlyDictionary<string, CsvTableData> tables,
        TaxonomyDictionary taxonomy,
        TaxonomyVersionInfo version,
        string versionSource)
    {
        var h = mapping.Header;
        var info = new PackageInfo
        {
            MappingPath = mapping.SourcePath,
            ReportDate = h?.ReportDate,
            Identifier = h?.Identifier,
            RequestNumberDifp = h?.RequestNumberDifp,
            DocumentVersion = h?.DocumentVersion,
            DtsHref = mapping.DtsReferences?.Href,
            Delimiter = h?.Delimiter,
            TextQualifier = h?.TextQualifier,
            Encoding = h?.Encoding,
            TextValueLengthLimit = h?.TextValueLengthLimit,
            MappingForm = mapping.MappingForm,
            TaxonomyVersion = version.Id,
            TaxonomyVersionName = version.DisplayName,
            TaxonomyVersionSource = versionSource,
        };
        foreach (var t in mapping.Tables)
        {
            tables.TryGetValue(t.TableName, out var csv);
            // название раздела: по roleUri (SR_0420417), для 6.1 совпадает с именем таблицы
            var title = taxonomy.TableTitle(t.RoleKey);
            info.Tables.Add(new PackageTableInfo
            {
                Uri = t.Uri ?? "",
                Table = t.RoleKey,
                Title = title,
                DeclaredRows = t.CsvRowsCount,
                ActualRows = csv?.Rows.Count,
                ColumnCount = t.Columns.Count,
                TypedFiltering = t.TypedFiltering == true,
            });
        }
        return info;
    }

    private static ValidationReport Finish(
        DateTime started,
        List<ValidationIssue> issues,
        PackageInfo? package,
        string taxonomyVersion,
        string taxonomyVersionSource)
    {
        return new ValidationReport
        {
            StartedUtc = started,
            FinishedUtc = DateTime.UtcNow,
            Issues = issues,
            Package = package ?? new PackageInfo
            {
                TaxonomyVersion = taxonomyVersion,
                TaxonomyVersionSource = taxonomyVersionSource,
            },
        };
    }
}
