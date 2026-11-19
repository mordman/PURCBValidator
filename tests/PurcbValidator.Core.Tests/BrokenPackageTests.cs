using System.Text;
using PurcbValidator.Core;
using PurcbValidator.Core.Models;

namespace PurcbValidator.Core.Tests;

/// <summary>Негативные тесты: намеренно испорченный пакет обязан давать конкретные коды ошибок.</summary>
public class BrokenPackageTests
{
    private static string SampleRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.Name != "tests") dir = dir.Parent;
        return Path.Combine(dir!.Parent!.FullName, "samples");
    }

    private static ValidationReport Run(string package, bool useSchema = true)
    {
        var runner = new ValidatorRunner();
        return runner.Run(new ValidationOptions
        {
            Path = Path.Combine(SampleRoot(), package),
            UseCbrSchema = useSchema,
        });
    }

    [Fact]
    public void BrokenJsonSyntax_IsReported()
    {
        var report = Run("broken_json");
        Assert.Contains(report.Issues, i => i.Code == "MAP-004");
    }

    [Fact]
    public void RowCountMismatch_IsReported()
    {
        var report = Run("broken_package");
        Assert.Contains(report.Issues, i =>
            i.Code == "CSV-020" && i.Table == "sr_R1" && i.Message.Contains("3"));
    }

    [Fact]
    public void UnknownHeaderColumn_IsReported()
    {
        var report = Run("broken_package");
        Assert.Contains(report.Issues, i => i.Code == "CSV-011" && i.Table == "sr_R2");
        Assert.Contains(report.Issues, i => i.Code == "CSV-010" && i.Table == "sr_R2");
    }

    [Fact]
    public void BadDateType_IsReported()
    {
        var report = Run("broken_package");
        Assert.Contains(report.Issues, i => i.Code == "CSV-060" && i.Table == "sr_R3");
    }

    [Fact]
    public void UnknownEnumMember_IsReported()
    {
        var report = Run("broken_package");
        Assert.Contains(report.Issues, i => i.Code == "TAX-020" && i.Table == "sr_sved_purcb");
    }

    [Fact]
    public void FacetPatternViolation_IsReported()
    {
        var report = Run("broken_package");
        Assert.Contains(report.Issues, i => i.Code == "TAX-010" && i.Table == "sr_R1");
    }

    [Fact]
    public void DuplicateAxesCombination_IsReported()
    {
        var report = Run("broken_package");
        Assert.Contains(report.Issues, i => i.Code == "BUILTIN-UNIQUE" && i.Table == "sr_R4");
    }

    [Fact]
    public void PresenceFlagContradiction_IsReported()
    {
        var report = Run("broken_package");
        Assert.Contains(report.Issues, i => i.Code == "BUILTIN-PRESENCE" && i.Table == "sr_sved_purcb");
    }

    [Fact]
    public void BrokenPackage_Fails()
    {
        var report = Run("broken_package");
        Assert.False(report.Passed);
        Assert.True(report.ErrorCount > 0);
    }
}
