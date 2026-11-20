using PurcbValidator.Core;
using PurcbValidator.Core.Models;

namespace PurcbValidator.Core.Tests;

/// <summary>
/// Воспроизведение дефектов пакета, отклонённого приёмкой ЦБ (протокол
/// первичных контролей fControlLogicalCsv2/3/5/6): числа без целой части,
/// текст в колонке даты, маркер «-» против маски таксономии, сырые коды
/// вместо членов перечисления, пустые открытые оси.
/// Сэмпл анонимизирован: строки синтетические, персональных данных нет.
/// </summary>
public class ProtocolPackageTests
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
            Path = Path.Combine(SampleRoot(), "protocol_package"),
        });
    }

    /// <summary>
    /// fControlLogicalCsv2: значение «.5» в колонке типа xbrli:monetaryItemType
    /// отклоняется приёмкой ЦБ (28 срабатываний в протоколе), хотя decimal
    /// .TryParse такое значение допускает, — проверяется лексический формат.
    /// </summary>
    [Fact]
    public void MonetaryValueWithoutIntegerPart_IsReported()
    {
        var report = Run();
        var v = report.Issues
            .Where(i => i.Code == "CSV-060" && i.Table == "sr_R10" &&
                        (i.Column ?? "").Contains("Scrt_NmnlPrc"))
            .ToList();
        Assert.Equal(3, v.Count);
        Assert.Contains(v, i => i.Value == ".5");
        Assert.Contains(v, i => i.Message.Contains("целой частью"));
    }

    /// <summary>fControlLogicalCsv2: текст в колонке даты.</summary>
    [Fact]
    public void TextInDateColumn_IsReported()
    {
        var report = Run();
        Assert.Contains(report.Issues, i =>
            i.Code == "CSV-060" && i.Table == "sr_R6" &&
            (i.Column ?? "").Contains("R_DcmDt"));
    }

    /// <summary>
    /// fControlLogicalCsv5: контроль масок ЦБ не делает исключения для маркера
    /// отсутствия «-» — в протоколе приёмки 5110 значений AO_Tm="-" отклонены
    /// маской времени; типы и перечисления «-» по-прежнему пропускают
    /// (правомерность отсутствия проверяют exists-правила ЦБ).
    /// </summary>
    [Fact]
    public void AbsenceMarker_FailsTaxonomyMask()
    {
        var report = Run();
        var v = report.Issues
            .Where(i => i.Code == "TAX-010" && (i.Column ?? "").Contains("AO_Tm"))
            .ToList();
        Assert.Equal(3, v.Count);
        Assert.All(v, i => Assert.Equal("sr_R6", i.Table));

        // «-» не считается ошибкой типа или перечисления
        Assert.DoesNotContain(report.Issues, i =>
            i.Code == "CSV-060" && (i.Column ?? "").Contains("AO_Tm"));
    }

    /// <summary>fControlLogicalCsv6: сырой код валюты вместо члена перечисления.</summary>
    [Fact]
    public void RawCurrencyCode_IsReported()
    {
        var report = Run();
        Assert.Contains(report.Issues, i =>
            i.Code == "TAX-020" && i.Table == "sr_R6" &&
            (i.Column ?? "").Contains("Asst_CrncEnumerator"));
    }

    /// <summary>
    /// fControlLogicalCsv3: в таблицах с типизированной фильтрацией каждая строка
    /// обязана содержать значения всех открытых осей (в протоколе — 20 пропусков).
    /// </summary>
    [Fact]
    public void EmptyOpenAxes_AreReported()
    {
        var report = Run();
        var v = report.Issues.Where(i => i.Code == "CSV-070").ToList();
        Assert.Equal(6, v.Count);
        Assert.All(v, i => Assert.Equal("sr_R6", i.Table));
        Assert.Equal(3, v.Count(i => (i.Column ?? "").EndsWith("A_PrtflCdTaxis")));
        Assert.Equal(3, v.Count(i => (i.Column ?? "").EndsWith("A_SctnTaxis")));
        // прочие оси заполнены — замечаний по ним нет
        Assert.DoesNotContain(v, i => (i.Column ?? "").Contains("A_NTaxis"));
    }
}
