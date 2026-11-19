using System.Text.Json;
using PurcbValidator.Core.Models;

namespace PurcbValidator.Core.Rules;

/// <summary>Загрузчик набора бизнес-правил rules.json и справочного каталога КС ЦБ.</summary>
public sealed class RuleSetLoader
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Загружает rules.json; при ошибке возвращает пустой набор с сообщением.</summary>
    public static (RuleSetFile? RuleSet, string? Error) Load(string? path = null)
    {
        path ??= Path.Combine(Taxonomy.TaxonomyDictionary.ResourcesRoot, "rules.json");
        try
        {
            var set = JsonSerializer.Deserialize<RuleSetFile>(File.ReadAllText(path), JsonOpts);
            return (set, null);
        }
        catch (Exception ex)
        {
            return (null, $"Не удалось загрузить набор правил {path}: {ex.Message}");
        }
    }

    public static RulesReferenceFile? LoadReference(string? path = null)
    {
        path ??= Path.Combine(Taxonomy.TaxonomyDictionary.ResourcesRoot, "rules_reference.json");
        try
        {
            return JsonSerializer.Deserialize<RulesReferenceFile>(File.ReadAllText(path), JsonOpts);
        }
        catch
        {
            return null;
        }
    }
}
