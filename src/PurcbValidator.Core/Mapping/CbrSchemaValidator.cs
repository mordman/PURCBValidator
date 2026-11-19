using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using PurcbValidator.Core.Models;
using PurcbValidator.Core.Taxonomy;

namespace PurcbValidator.Core.Mapping;

/// <summary>
/// Слой 2 — проверка служебного файла пакета на соответствие официальным
/// draft-схемам Банка России. Поддерживаемые наборы схем:
///  * УОД ПУРЦБ 6.1.0.10 (набор 20260403/20260430, файлы *.def.json);
///  * таксономия 7.1, точка входа 10-РД 0420417 (набор 2025-12-30/20251230,
///    файлы *.def.ep.json).
/// Схемы подключаются из ресурсов и регистрируются по их $id, поэтому
/// относительные ссылки $ref между файлами схем разрешаются офлайн.
/// Построенные наборы схем кэшируются на процесс для каждой пары
/// (каталог схем, точка входа) и оберегаются блокировкой:
/// библиотека содержит глобальные статические реестры.
/// </summary>
/// <remarks>
/// Таблицы проверяются по-таблицно: запись tables[i] сверяется напрямую со схемой
/// своей таблицы (опознаётся по roleUri, затем по uri). Сквозная оценка массива
/// tables через anyOf точки входа в JsonSchema.Net 9.7.0 ошибочно приписывает
/// ошибки i-й записи пути (i-1)-й (смещение указателя инстанса в items) и порождает
/// лавину сообщений всех ветвей anyOf, поэтому из результатов точки
/// входа собираются только ошибки вне массива таблиц и сводный уровень самого
/// массива (minItems/uniqueItems/перечень провалившихся индексов items).
/// </remarks>
public sealed class CbrSchemaValidator
{
    /// <summary>$id точки входа таксономии УОД ПУРЦБ 6.1.0.10 (по умолчанию).</summary>
    public const string UodEntryPointId =
        "http://www.cbr.ru/xbrl_csv/20260403/20260430/ep_nso_purcb_oper_nr_uod_reestr.def.json";

    private static readonly object SchemaLock = new();
    private static readonly ConcurrentDictionary<string, SchemaCacheEntry> Cache = new();

    private readonly string _schemasRoot;
    private readonly string _entryPointId;

    public CbrSchemaValidator(string? schemasRoot = null, string? entryPointId = null)
    {
        _schemasRoot = schemasRoot
            ?? Path.Combine(TaxonomyDictionary.ResourcesRoot, "6.1.0.10", "cbr_schemas");
        _entryPointId = entryPointId ?? UodEntryPointId;
    }

    private sealed record SchemaCacheEntry(
        JsonSchema? Entry,
        Dictionary<string, TableSchemaRef>? TableSchemas,
        string? Error,
        bool Attempted);

    /// <summary>Валидирует текст служебного файла против схем таксономии ЦБ.</summary>
    public void Validate(string mappingJson, string mappingPath, List<ValidationIssue> issues)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(mappingJson);
        }
        catch (Exception ex)
        {
            issues.Add(new ValidationIssue
            {
                Layer = ValidationLayer.TaxonomySchema,
                Severity = IssueSeverity.Error,
                Code = "SCH-000",
                File = mappingPath,
                Message = $"Не удалось разобрать JSON для проверки по схеме: {ex.Message}",
            });
            return;
        }
        if (node is null) return;

        var entry = GetOrBuildEntrySchema(issues, mappingPath);
        if (entry is null) return;

        var element = JsonSerializer.SerializeToElement(node);
        EvaluationResults result;
        try
        {
            lock (SchemaLock)
            {
                result = entry.Evaluate(element, new EvaluationOptions
                {
                    OutputFormat = OutputFormat.Hierarchical,
                    RequireFormatValidation = false,
                });
            }
        }
        catch (Exception ex)
        {
            issues.Add(new ValidationIssue
            {
                Layer = ValidationLayer.TaxonomySchema,
                Severity = IssueSeverity.Warning,
                Code = "SCH-002",
                File = mappingPath,
                Message = $"Проверка по схеме ЦБ прервана: {ex.Message}",
            });
            return;
        }

        if (!result.IsValid)
        {
            var reported = new HashSet<string>(StringComparer.Ordinal);
            CollectErrors(result, mappingPath, issues, reported);
        }

        ValidateTables(node, mappingPath, issues);
    }

    /// <summary>
    /// Строит (один раз на процесс для каждой версии) схему точки входа и карту
    /// схем таблиц с общим реестром всех def-файлов.
    /// </summary>
    private JsonSchema? GetOrBuildEntrySchema(List<ValidationIssue> issues, string mappingPath)
    {
        var cacheKey = _schemasRoot + "|" + _entryPointId;
        var cached = Cache.GetOrAdd(cacheKey, _ => new SchemaCacheEntry(null, null, null, false));

        lock (SchemaLock)
        {
            if (cached.Entry is not null) return cached.Entry;
            if (cached.Attempted)
            {
                // уже пытались и не нашли — не повторяем, чтобы не маскировать причину
                issues.Add(new ValidationIssue
                {
                    Layer = ValidationLayer.TaxonomySchema,
                    Severity = IssueSeverity.Error,
                    Code = "SCH-001",
                    File = mappingPath,
                    Message = "Схема точки входа таксономии ЦБ не найдена или не построена" +
                              (cached.Error is null ? "" : $": {cached.Error}"),
                });
                return null;
            }
            Cache[cacheKey] = cached with { Attempted = true };
        }

        // $id читаем прямо из текста схемы — независимо от состояния библиотеки
        var schemaTexts = new Dictionary<string, string>(StringComparer.Ordinal);
        var idlessTexts = new List<(string FileName, string Text)>();
        string? cacheError = null;
        try
        {
            if (Directory.Exists(_schemasRoot))
            {
                // 6.1.0.10 — *.def.json; 7.1 — *.def.ep.json (наборы не пересекаются)
                var files = Directory.EnumerateFiles(_schemasRoot, "*.def.json", SearchOption.AllDirectories)
                    .Concat(Directory.EnumerateFiles(_schemasRoot, "*.def.ep.json", SearchOption.AllDirectories))
                    .Concat(Directory.EnumerateFiles(_schemasRoot, "xbrl-csv.json", SearchOption.AllDirectories))
                    .Distinct(StringComparer.OrdinalIgnoreCase);
                foreach (var file in files)
                {
                    string text;
                    try { text = File.ReadAllText(file); }
                    catch { continue; }

                    try
                    {
                        using var doc = JsonDocument.Parse(text);
                        if (doc.RootElement.TryGetProperty("$id", out var idEl) &&
                            idEl.ValueKind == JsonValueKind.String)
                        {
                            schemaTexts[idEl.GetString()!] = text;
                        }
                        else
                        {
                            // файл без $id (например, общий xbrl-csv.json в наборе 7.1):
                            // ссылки на него из соседних схем разрешаются относительно
                            // их $id — синтезируем $id от базового адреса точки входа
                            idlessTexts.Add((Path.GetFileName(file), text));
                        }
                    }
                    catch (JsonException)
                    {
                        // битый файл схемы — пропускаем
                    }
                }
            }
        }
        catch (Exception ex)
        {
            cacheError = ex.Message;
        }

        // база $id точки входа: http://…/20251230/ep_xxx.json -> http://…/20251230/
        var entryUri = new Uri(_entryPointId);
        var entryBase = _entryPointId[..(_entryPointId.Length - entryUri.Segments[^1].Length)];
        foreach (var (fileName, text) in idlessTexts)
        {
            var node = JsonNode.Parse(text) as JsonObject;
            if (node is null) continue;
            node["$id"] = entryBase + fileName;
            schemaTexts[entryBase + fileName] = node.ToJsonString();
        }

        JsonSchema? entry = null;
        var tableSchemas = new Dictionary<string, TableSchemaRef>(StringComparer.Ordinal);
        lock (SchemaLock)
        {
            try
            {
                // все схемы таксономии строятся в одном реестре, поэтому относительные
                // ссылки $ref между def-файлами разрешаются без выхода в сеть
                var registry = new SchemaRegistry();
                var buildOptions = new BuildOptions { SchemaRegistry = registry };
                foreach (var (id, text) in schemaTexts)
                {
                    try
                    {
                        // Для точки входа строится модифицированная схема: массив tables
                        // заменяется каркасной заглушкой (type/minItems/uniqueItems).
                        // Полная оценка немодифицированной точки входа в JsonSchema.Net
                        // 9.7.0 разворачивает каскад anyOf по всем ветвям таблиц
                        // (~14 секунд и ~2 ГБ удерживаемой памяти на один mapping.json),
                        // а содержимое таблиц надёжнее и быстрее проверять по-таблицно.
                        var schema = id == _entryPointId
                            ? BuildPatchedEntry(text, buildOptions)
                            : JsonSchema.FromText(text, buildOptions);
                        registry.Register(schema);
                        if (id == _entryPointId) entry = schema;

                        CollectTableSchemas(registry, buildOptions, id, text, tableSchemas);
                    }
                    catch (Exception)
                    {
                        // отдельная неразобранная схема не должна рушить весь слой
                    }
                }
            }
            catch (Exception ex)
            {
                cacheError = ex.Message;
            }

            Cache[cacheKey] = new SchemaCacheEntry(entry, tableSchemas, cacheError, true);
        }

        if (entry is null)
        {
            issues.Add(new ValidationIssue
            {
                Layer = ValidationLayer.TaxonomySchema,
                Severity = IssueSeverity.Error,
                Code = "SCH-001",
                File = mappingPath,
                Message = $"Схема точки входа таксономии ЦБ ({_entryPointId}) не найдена в ресурсах приложения" +
                          (cacheError is null ? "" : $": {cacheError}"),
            });
        }
        return entry;
    }

    /// <summary>
    /// Строит точку входа с каркасной заглушкой вместо массива tables:
    /// сохраняются только type/minItems/uniqueItems самого массива.
    /// В 6.1.0.10 tables лежит в корневых properties, в 7.1 — внутри ветки allOf.
    /// Содержимое таблиц (roleUri, uri, csvRowsCount, columns) проверяется
    /// по-таблицно в <see cref="ValidateTables"/>.
    /// </summary>
    private static JsonSchema BuildPatchedEntry(string entryText, BuildOptions buildOptions)
    {
        var node = (JsonObject)JsonNode.Parse(entryText)!;
        if (!TryReplaceTables(node))
        {
            // формат без properties.tables (неожиданный) — оставляем как есть
        }
        return JsonSchema.FromText(node.ToJsonString(), buildOptions);
    }

    private static bool TryReplaceTables(JsonObject node)
    {
        var stub = JsonNode.Parse(
            """{"type":"array","minItems":1,"uniqueItems":true,"description":"Массив таблиц; состав записей проверяется по-таблицно"}""")!;
        if (node["properties"]?["tables"] is not null)
        {
            node["properties"]!["tables"] = stub;
            return true;
        }
        if (node["allOf"] is JsonArray allOf)
        {
            foreach (var branch in allOf)
            {
                if (branch?["properties"]?["tables"] is not null)
                {
                    ((JsonObject)branch!)["properties"]!["tables"] = stub;
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Для каждого определения table_* строит обёртку-схему для прямой проверки записи
    /// таблицы и добавляет её в карту по roleUri и по uri.
    /// </summary>
    private static void CollectTableSchemas(
        SchemaRegistry registry,
        BuildOptions buildOptions,
        string schemaId,
        string schemaText,
        Dictionary<string, TableSchemaRef> tableSchemas)
    {
        using var doc = JsonDocument.Parse(schemaText);
        if (!doc.RootElement.TryGetProperty("$defs", out var defsEl)) return;

        foreach (var def in defsEl.EnumerateObject())
        {
            if (!def.Name.StartsWith("table_", StringComparison.Ordinal)) continue;
            var roleUri = TryConst(def.Value, "roleUri");
            var tableUri = TryConst(def.Value, "uri");
            if (roleUri is null && tableUri is null) continue;

            var wrapper = JsonSchema.FromText(
                $"{{\"$ref\": \"{schemaId}#/$defs/{def.Name}\"}}", buildOptions);
            registry.Register(wrapper);
            var tsr = new TableSchemaRef(tableUri ?? roleUri!, wrapper);
            if (roleUri is not null) tableSchemas[roleUri] = tsr;
            if (tableUri is not null) tableSchemas[tableUri] = tsr;
        }
    }

    private static string? TryConst(JsonElement tableDef, string propertyName)
    {
        if (!tableDef.TryGetProperty("properties", out var props)) return null;
        if (!props.TryGetProperty(propertyName, out var prop)) return null;
        if (!prop.TryGetProperty("const", out var constEl)) return null;
        return constEl.ValueKind == JsonValueKind.String ? constEl.GetString() : null;
    }

    /// <summary>
    /// По-таблицная проверка: каждая запись tables[i] сверяется со схемой
    /// своей таблицы напрямую; ошибки получают корректные пути /tables/i/…
    /// </summary>
    private void ValidateTables(JsonNode mapping, string mappingPath, List<ValidationIssue> issues)
    {
        if (mapping["tables"] is not JsonArray tables) return;
        var map = Cache.TryGetValue(_schemasRoot + "|" + _entryPointId, out var c) ? c.TableSchemas : null;
        if (map is null || map.Count == 0) return;

        var options = new EvaluationOptions
        {
            OutputFormat = OutputFormat.Hierarchical,
            RequireFormatValidation = false,
        };

        for (var i = 0; i < tables.Count; i++)
        {
            if (tables[i] is not JsonObject table) continue;
            var roleUri = (string?)table["roleUri"];
            var uri = (string?)table["uri"];

            TableSchemaRef? ts = null;
            if (roleUri is not null) map.TryGetValue(roleUri, out ts);
            if (ts is null && uri is not null) map.TryGetValue(uri, out ts);

            if (ts is null)
            {
                issues.Add(new ValidationIssue
                {
                    Layer = ValidationLayer.TaxonomySchema,
                    Severity = IssueSeverity.Error,
                    Code = "SCH-100",
                    File = mappingPath,
                    Table = uri ?? roleUri,
                    Column = $"/tables/{i}",
                    Message = "Таблица не соответствует ни одной из схем таблиц таксономии " +
                              "ПУРЦБ (roleUri и uri не опознаны как таблицы таксономии)",
                });
                continue;
            }

            EvaluationResults result;
            try
            {
                lock (SchemaLock)
                {
                    result = ts.Schema.Evaluate(JsonSerializer.SerializeToElement(table), options);
                }
            }
            catch (Exception ex)
            {
                issues.Add(new ValidationIssue
                {
                    Layer = ValidationLayer.TaxonomySchema,
                    Severity = IssueSeverity.Warning,
                    Code = "SCH-002",
                    File = mappingPath,
                    Table = uri,
                    Message = $"Проверка таблицы по схеме ЦБ прервана: {ex.Message}",
                });
                continue;
            }

            if (result.IsValid) continue;

            var reported = new HashSet<string>(StringComparer.Ordinal);
            CollectTableErrors(result, mappingPath, uri, i, issues, reported);
        }
    }

    private static void CollectTableErrors(
        EvaluationResults node,
        string mappingPath,
        string? tableName,
        int tableIndex,
        List<ValidationIssue> issues,
        HashSet<string> reported)
    {
        // валидный узел (в т.ч. удачный anyOf) ошибок не даёт:
        // сообщения его неудачных ветвей отбрасываем
        if (node.IsValid) return;

        if (node.Details is { Count: > 0 })
        {
            foreach (var d in node.Details)
                CollectTableErrors(d, mappingPath, tableName, tableIndex, issues, reported);
            return;
        }

        if (node.Errors is null) return;
        var local = node.InstanceLocation.ToString();
        var instancePath = $"/tables/{tableIndex}{local}";
        foreach (var (key, message) in node.Errors)
        {
            var unique = $"{instancePath}|{key}|{message}";
            if (!reported.Add(unique)) continue;
            issues.Add(new ValidationIssue
            {
                Layer = ValidationLayer.TaxonomySchema,
                Severity = IssueSeverity.Error,
                Code = "SCH-100",
                File = mappingPath,
                Table = tableName,
                Column = instancePath,
                Message = $"Несоответствие схеме таксономии ЦБ: {message} (в \"{key}\", путь {instancePath})",
            });
        }
    }

    private static void CollectErrors(
        EvaluationResults node,
        string mappingPath,
        List<ValidationIssue> issues,
        HashSet<string> reported)
    {
        // валидный узел (в т.ч. удачный anyOf) ошибок не даёт:
        // сообщения его неудачных ветвей отбрасываем
        if (node.IsValid) return;

        var instancePath = node.InstanceLocation.ToString();

        // содержимое записей таблиц проверяется по-таблицно (см. ValidateTables):
        // из сквозной оценки берём только сводный уровень самого массива /tables
        if (instancePath.StartsWith("/tables/", StringComparison.Ordinal)) return;
        var isTablesNode = instancePath == "/tables";
        var isLeaf = node.Details is not { Count: > 0 };

        // собственные ошибки узла собираем у листьев; единственное исключение —
        // сам массив /tables: его сводка (uniqueItems, перечень провалившихся
        // индексов items) полезна даже при наличии дочерних узлов
        if (node.Errors is not null && (isLeaf || isTablesNode))
        {
            foreach (var (key, message) in node.Errors)
            {
                var unique = $"{instancePath}|{key}|{message}";
                if (!reported.Add(unique)) continue;
                issues.Add(new ValidationIssue
                {
                    Layer = ValidationLayer.TaxonomySchema,
                    Severity = IssueSeverity.Error,
                    Code = "SCH-100",
                    File = mappingPath,
                    Column = instancePath.Length > 0 ? instancePath : null,
                    Message = $"Несоответствие схеме таксономии ЦБ: {message} (в \"{key}\", путь {instancePath})",
                });
            }
        }

        if (isTablesNode) return; // ниже /tables — по-таблицная проверка
        if (node.Details is { Count: > 0 })
        {
            foreach (var d in node.Details)
                CollectErrors(d, mappingPath, issues, reported);
        }
    }

    /// <summary>Ссылка на схему одной таблицы таксономии (для по-таблицной проверки).</summary>
    private sealed record TableSchemaRef(string TableName, JsonSchema Schema);
}
