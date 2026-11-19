using PurcbValidator.Core;
using PurcbValidator.Core.Models;

namespace PurcbValidator.Core.Tests;

public class SmokeExamplePackageTests
{
    private static string SampleRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.Name != "tests") dir = dir.Parent;
        return Path.Combine(dir!.Parent!.FullName, "samples");
    }

    [Fact]
    public void ExamplePackage_HasNoStructuralOrCsvOrSchemaErrors()
    {
        var runner = new ValidatorRunner();
        var report = runner.Run(new ValidationOptions
        {
            Path = Path.Combine(SampleRoot(), "example_package"),
            UseCbrSchema = true,
        });

        using var w = new StringWriter();
        foreach (var i in report.Issues)
            w.WriteLine(i);

        // структурные слои должны быть чистыми (ошибок нет;
        // предупреждение об идентификаторе-заполнителе допустимо)
        var structural = report.Issues
            .Where(i => i.Severity == IssueSeverity.Error &&
                        i.Layer is ValidationLayer.MappingStructure
                                 or ValidationLayer.CsvData
                                 or ValidationLayer.Package)
            .ToList();
        Assert.True(structural.Count == 0,
            "Неожиданные структурные ошибки:\n" + w.ToString());

        // в официальном примере ЦБ идентификатор — заполнитель из нулей,
        // поэтому схема таксономии правомерно требует его исправления;
        // иных ошибок схемы быть не должно
        var schemaErrors = report.Issues
            .Where(i => i.Layer == ValidationLayer.TaxonomySchema && i.Severity == IssueSeverity.Error)
            .ToList();
        Assert.True(schemaErrors.All(i => (i.Column ?? "").Contains("identifier")),
            "Лишние ошибки схемы таксономии:\n" + w.ToString());
    }

    [Fact]
    public void ExamplePackage_RuleWarnings_AreProduced()
    {
        // эталонный пример ЦБ содержит случайные заполнители-значения,
        // поэтому часть контрольных соотношений обязана сработать
        var runner = new ValidatorRunner();
        var report = runner.Run(new ValidationOptions
        {
            Path = Path.Combine(SampleRoot(), "example_package"),
        });

        Assert.Contains(report.Issues, i => i.Layer == ValidationLayer.BusinessRules);
    }
}
