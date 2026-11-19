using PurcbValidator.Core;
using PurcbValidator.Core.Models;

namespace PurcbValidator.Core.Tests;

/// <summary>Каталог версий таксономии и автоопределение по ссылке пакета.</summary>
public class TaxonomyVersionTests
{
    [Fact]
    public void Detect_UodHref_Returns61010()
    {
        var v = TaxonomyVersions.Detect(
            "http://www.cbr.ru/xbrl_csv/20260403/20260430/ep_nso_purcb_oper_nr_uod_reestr.def.json");
        Assert.NotNull(v);
        Assert.Equal("6.1.0.10", v.Id);
        Assert.True(v.HasRules);
    }

    [Fact]
    public void Detect_Purcb71Href_Returns71()
    {
        var v = TaxonomyVersions.Detect(
            "http://www.cbr.ru/xbrl_csv/2025-12-30/20251230/ep_nso_purcb_m_q_y_10rd_reestr_0420417.def.ep.json");
        Assert.NotNull(v);
        Assert.Equal("7.1", v.Id);
        Assert.False(v.HasRules);
    }

    [Fact]
    public void Detect_UnknownHref_ReturnsNull()
    {
        Assert.Null(TaxonomyVersions.Detect("http://example.com/unknown.def.json"));
        Assert.Null(TaxonomyVersions.Detect(null));
        Assert.Null(TaxonomyVersions.Detect(""));
    }

    [Fact]
    public void Find_ByIdentifier_IsCaseInsensitive()
    {
        Assert.Equal("7.1", TaxonomyVersions.Find("7.1")!.Id);
        Assert.Equal("6.1.0.10", TaxonomyVersions.Find("6.1.0.10")!.Id);
        Assert.Equal("7.1", TaxonomyVersions.Find(" 7.1 ")!.Id);
        Assert.Null(TaxonomyVersions.Find("9.9"));
        Assert.Null(TaxonomyVersions.Find(null));
    }

    [Fact]
    public void ResourcesDir_ExistsForAllVersions()
    {
        foreach (var v in TaxonomyVersions.All)
        {
            Assert.True(Directory.Exists(v.ResourcesDir), $"нет каталога ресурсов {v.Id}");
            Assert.True(File.Exists(Path.Combine(v.ResourcesDir, "taxonomy_dict.json")),
                $"нет словаря таксономии {v.Id}");
            Assert.True(Directory.Exists(Path.Combine(v.ResourcesDir, "cbr_schemas")),
                $"нет схем таксономии {v.Id}");
        }
    }
}

/// <summary>Проверка пакета 10-РД (таксономия 7.1) — официальный пример Банка России.</summary>
public class SmokeExamplePackage71Tests
{
    private static string SampleRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.Name != "tests") dir = dir.Parent;
        return Path.Combine(dir!.Parent!.FullName, "samples");
    }

    private static ValidationReport Run(string? taxonomyVersion = null)
    {
        var runner = new ValidatorRunner();
        return runner.Run(new ValidationOptions
        {
            Path = Path.Combine(SampleRoot(), "example_package_7_1"),
            TaxonomyVersion = taxonomyVersion,
        });
    }

    [Fact]
    public void Example71_AutoDetected_AndPasses()
    {
        var report = Run();

        // версия определена по dtsReferences.href пакета
        Assert.Equal("7.1", report.Package.TaxonomyVersion);
        Assert.Equal("автоопределение по пакету", report.Package.TaxonomyVersionSource);

        // официальный пример ЦБ проходит все слои без ошибок и предупреждений;
        // единственное замечание — информационное пояснение об отсутствии КС для 10-РД
        Assert.Equal(0, report.ErrorCount);
        Assert.Equal(0, report.WarningCount);
        Assert.Contains(report.Issues, i => i.Code == "RULE-001" && i.Severity == IssueSeverity.Info);

        // слой 2: все 7 таблиц опознаны схемами таксономии 7.1
        Assert.DoesNotContain(report.Issues, i => i.Code.StartsWith("SCH-", StringComparison.Ordinal));
        Assert.Equal(7, report.Package.Tables.Count);
        Assert.Contains(report.Package.Tables, t => t.Table == "SR_0420417" && t.ActualRows == 3);
        Assert.Contains(report.Package.Tables, t => t.Table == "SR_Sved_ObOtchOrg_OtvLicza" && t.ActualRows == 1);
    }

    [Fact]
    public void Example71_ExplicitSelection_IsApplied()
    {
        var report = Run("7.1");
        Assert.Equal("7.1", report.Package.TaxonomyVersion);
        Assert.Equal("выбрана вручную", report.Package.TaxonomyVersionSource);
        Assert.Equal(0, report.ErrorCount);
    }

    [Fact]
    public void Example71_ForcedUod61010_TablesNotRecognized()
    {
        // принудительная проверка пакета 10-РД схемами УОД 6.1.0.10:
        // таблицы 10-РД не опознаются — по каждой выдаётся SCH-100
        var report = Run("6.1.0.10");
        Assert.Equal("6.1.0.10", report.Package.TaxonomyVersion);
        Assert.Equal("выбрана вручную", report.Package.TaxonomyVersionSource);
        Assert.Contains(report.Issues, i => i.Code == "SCH-100");
    }

    [Fact]
    public void Example71_UnknownVersion_FallsBackWithWarning()
    {
        var report = Run("9.9");
        Assert.Contains(report.Issues, i => i.Code == "PKG-010");
        // после предупреждения применяется автоопределение
        Assert.Equal("7.1", report.Package.TaxonomyVersion);
        Assert.Equal("автоопределение по пакету", report.Package.TaxonomyVersionSource);
    }

    [Fact]
    public void Example71_EnumDomains_AreLoaded()
    {
        // словарь 7.1 содержит домены перечислений 10-РД с русскими метками
        var dict = Taxonomy.TaxonomyDictionary.Load(
            Path.Combine(TaxonomyVersions.Purcb71.ResourcesDir, "taxonomy_dict.json"));
        var domain = dict.EnumDomainFor("purcb-dic:Inform_povtor_predost_sved_oSdelkeEnumerator");
        Assert.NotNull(domain);
        Assert.Contains("mem-int:UPDATE_SvedeVInformSoobshhOSdelkeNeobxIzmenitMember", domain.Values);
        Assert.NotEmpty(dict.Label("purcb-dic:CZena_fin_instrumenta_Inf_o_vtor_chast_sdelki"));
    }
}
