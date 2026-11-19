using PurcbValidator.Core.Csv;
using PurcbValidator.Core.Mapping;
using PurcbValidator.Core.Models;
using PurcbValidator.Core.Rules;
using PurcbValidator.Core.Taxonomy;

namespace PurcbValidator.Core;

/// <summary>Параметры запуска валидации.</summary>
public sealed class ValidationOptions
{
    /// <summary>Путь к файлу mapping.json или к папке пакета (mapping.json будет найден внутри).</summary>
    public string Path { get; init; } = "";

    /// <summary>Проверять mapping.json по официальным JSON-схемам ЦБ (слой 2).</summary>
    public bool UseCbrSchema { get; init; } = true;

    /// <summary>Путь к альтернативному файлу правил (rules.json).</summary>
    public string? RulesFile { get; init; }
}

/// <summary>
/// Конвейер валидации пакета УОД ПУРЦБ (XBRL-CSV 2.0, таксономия 6.1.0.10):
/// слой 1 — структура mapping.json; слой 2 — JSON-схемы ЦБ; слой 3 — CSV-данные;
/// слой 4 — контрольные соотношения ЦБ и встроенные проверки пакета.
/// </summary>
public sealed class ValidatorRunner
{
    /// <summary>Запускает полную валидацию пакета.</summary>
    public ValidationReport Run(ValidationOptions options, Action<string>? progress = null)
    {
        var started = DateTime.UtcNow;
        var issues = new List<ValidationIssue>();

        progress?.Invoke("Поиск mapping.json…");
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

        // --- слой 1: структура mapping.json ---
        progress?.Invoke("Слой 1/4: структура mapping.json…");
        var loader = new MappingLoader();
        var mapping = loader.Load(mappingPath, issues);
        if (mapping is null)
        {
            return Finish(started, issues, null, null);
        }

        // --- слой 2: официальные JSON-схемы ЦБ ---
        if (options.UseCbrSchema)
        {
            progress?.Invoke("Слой 2/4: соответствие схемам таксономии ЦБ…");
            try
            {
                var schemaValidator = new CbrSchemaValidator();
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
        var taxonomy = TaxonomyDictionary.Load();
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
        var (ruleSet, ruleError) = RuleSetLoader.Load(options.RulesFile);
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
        if (ruleSet is not null)
        {
            try
            {
                var engine = new RuleEngine(taxonomy);
                engine.Execute(mapping, tables, ruleSet.Rules, issues);
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

        progress?.Invoke("Формирование отчёта…");
        var package = BuildPackageInfo(mapping, tables, taxonomy);
        return Finish(started, issues, package, taxonomy);
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
            var candidates = Directory.EnumerateFiles(path, "mapping.json").ToList();
            if (candidates.Count == 0)
                candidates = Directory.EnumerateFiles(path, "*.json")
                    .Where(f => string.Equals(Path.GetFileName(f), "mapping.json", StringComparison.OrdinalIgnoreCase))
                    .ToList();
            if (candidates.Count == 0)
            {
                issues.Add(new ValidationIssue
                {
                    Layer = ValidationLayer.Package,
                    Severity = IssueSeverity.Error,
                    Code = "PKG-001",
                    Message = $"В папке \"{path}\" не найден файл mapping.json",
                });
                return null;
            }
            return candidates[0];
        }

        if (File.Exists(path))
        {
            var name = Path.GetFileName(path);
            return string.Equals(name, "mapping.json", StringComparison.OrdinalIgnoreCase)
                ? path
                : path; // допускаем произвольное имя служебного файла
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
        TaxonomyDictionary taxonomy)
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
        };
        foreach (var t in mapping.Tables)
        {
            tables.TryGetValue(t.TableName, out var csv);
            info.Tables.Add(new PackageTableInfo
            {
                Uri = t.Uri ?? "",
                Table = t.TableName,
                Title = taxonomy.TableTitle(t.TableName),
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
        TaxonomyDictionary? taxonomy)
    {
        return new ValidationReport
        {
            StartedUtc = started,
            FinishedUtc = DateTime.UtcNow,
            Issues = issues,
            Package = package ?? new PackageInfo(),
        };
    }
}
