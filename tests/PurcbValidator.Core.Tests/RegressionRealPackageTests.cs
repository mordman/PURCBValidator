using System.Text;
using PurcbValidator.Core;
using PurcbValidator.Core.Models;

namespace PurcbValidator.Core.Tests;

/// <summary>
/// Регрессионные тесты на реальном пакете (краткая форма mapping.json,
/// 10 таблиц) с воспроизведёнными дефектами конвертера:
/// рассинхрон открытых осей sr_R7, текст в поле даты sr_R6, сырые коды
/// вместо членов перечисления, маркер отсутствия «-».
/// </summary>
public class RegressionRealPackageTests
{
    private static string SampleRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.Name != "tests") dir = dir.Parent;
        return Path.Combine(dir!.Parent!.FullName, "samples");
    }

    private static ValidationReport Run()
    {
        var runner = new ValidatorRunner();
        return runner.Run(new ValidationOptions
        {
            Path = Path.Combine(SampleRoot(), "regression_package"),
        });
    }

    /// <summary>
    /// Ошибки схемы таксономии обязаны приписываться именно той таблице, в которой
    /// дефект (sr_R7 с рассинхронированными открытыми осями), а не предыдущей
    /// записи массива tables (баг смещения путей в сквозной anyOf-оценке) —
    /// и не порождать лавину из сотен сообщений.
    /// </summary>
    [Fact]
    public void SchemaErrors_AreAttributedToActualTable()
    {
        var report = Run();
        using var w = new StringWriter();
        foreach (var i in report.Issues) w.WriteLine(i);

        var schemaErrors = report.Issues
            .Where(i => i.Layer == ValidationLayer.TaxonomySchema &&
                        i.Severity == IssueSeverity.Error &&
                        i.Code == "SCH-100")
            .ToList();

        Assert.True(schemaErrors.Count > 0, "Ожидались ошибки схемы для sr_R7:\n" + w);
        Assert.True(schemaErrors.Count < 50,
            $"Лавина ошибок схемы ({schemaErrors.Count}) вместо точечных замечаний:\n" + w);
        Assert.All(schemaErrors, i => Assert.Equal("sr_R7.csv", i.Table));
        Assert.All(schemaErrors, i =>
            Assert.True((i.Column ?? "").StartsWith("/tables/", StringComparison.Ordinal),
                $"Путь ошибки схемы должен указывать на запись таблицы: {i.Column}"));
    }

    /// <summary>
    /// Маркер отсутствия «-» (формат УОД) и первичные контроли ЦБ:
    /// контроль масок fControlLogicalCsv5 «-» не пропускает — AO_Tm = «-»
    /// даёт TAX-010 (подтверждено протоколом приёмки ЦБ: 5110 срабатываний);
    /// контроль типов по-прежнему ловит настоящий дефект (текст «Клиринговый
    /// отчет» в поле даты R_DcmDt), а правомерность отсутствия проверяют
    /// exists-правила ЦБ (см. Rules_TreatDashAsAbsence).
    /// </summary>
    [Fact]
    public void AbsenceMarker_IsCheckedAgainstTaxonomyMask()
    {
        var report = Run();

        Assert.Contains(report.Issues,
            i => i.Code == "TAX-010" && (i.Column ?? "").Contains("AO_Tm"));
        Assert.Contains(report.Issues, i =>
            i.Code == "CSV-060" &&
            i.Table == "sr_R6" &&
            (i.Column ?? "").Contains("R_DcmDt"));
    }

    /// <summary>
    /// Пустые открытые оси (fControlLogicalCsv3): в сэмпле 10 строк sr_R6
    /// без значений осей A_PrtflCdTaxis/A_SctnTaxis — по 10 замечаний
    /// на каждую ось.
    /// </summary>
    [Fact]
    public void EmptyOpenAxes_AreReported()
    {
        var report = Run();
        var v = report.Issues.Where(i => i.Code == "CSV-070").ToList();
        Assert.Equal(20, v.Count);
        Assert.All(v, i => Assert.Equal("sr_R6", i.Table));
        Assert.Equal(10, v.Count(i => (i.Column ?? "").EndsWith("A_PrtflCdTaxis")));
        Assert.Equal(10, v.Count(i => (i.Column ?? "").EndsWith("A_SctnTaxis")));
    }

    /// <summary>Сыровые коды вместо имён членов перечисления — реальная ошибка данных.</summary>
    [Fact]
    public void RawCodesInsteadOfEnumMembers_AreReported()
    {
        var report = Run();

        Assert.Contains(report.Issues, i =>
            i.Code == "TAX-020" &&
            i.Table == "sr_R6" &&
            (i.Column ?? "").Contains("Asst_CrncEnumerator"));
    }

    /// <summary>
    /// Контрольные соотношения ЦБ трактуют «-» как отсутствие значения:
    /// замена заполненного Rqst_TrdUsr на «-» при T_Bss из {1,2,3}
    /// обязана дать замечание по правилу R3_05 именно в этой строке.
    /// </summary>
    [Fact]
    public void Rules_TreatDashAsAbsence()
    {
        var report = Run();

        // ищем строку с маркером «-» в колонке Rqst_TrdUsr
        // (заголовок пишется без текстовых ограничителей, данные — с ними)
        var csv = File.ReadAllLines(
            Path.Combine(SampleRoot(), "regression_package", "sr_R3.csv"));
        var header = csv[0].Split('|').Select(c => c.Trim('"')).ToArray();
        var trdIdx = Array.IndexOf(header, "purcb_dic_Rqst_TrdUsr_dimGrp_1_periodGrp_1");
        Assert.True(trdIdx >= 0, "в сэмпле нет колонки Rqst_TrdUsr");
        var dashRow = -1;
        for (var i = 1; i < csv.Length; i++)
        {
            if (csv[i].Split('|')[trdIdx] == "\"-\"") { dashRow = i; break; }
        }
        Assert.True(dashRow > 0, "в сэмпле нет строки с маркером «-» в Rqst_TrdUsr");

        Assert.Contains(report.Issues, i =>
            i.Code == "RULE-valueAssertion_R3_05" && i.Row == dashRow);
    }
}
