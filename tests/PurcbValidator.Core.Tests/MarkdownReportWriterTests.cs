using PurcbValidator.Core.Models;
using PurcbValidator.Core.Reporting;

namespace PurcbValidator.Core.Tests;

public class MarkdownReportWriterTests
{
    private static ValidationReport SampleReport()
    {
        var report = new ValidationReport
        {
            Package = new PackageInfo
            {
                MappingPath = "/packages/example/mapping.json",
                ReportDate = "2026-04-30",
                Identifier = "1234567890123",
                RequestNumberDifp = "12345_67",
                DocumentVersion = "http://www.cbr.ru/xbrl_csv2/20260403/20260430/difp",
                DtsHref = "http://www.cbr.ru/xbrl_csv/20260403/20260430/ep_nso_purcb_oper_nr_uod_reestr.def.json",
                Delimiter = "|",
                TextQualifier = "\"",
                Encoding = "UTF-8",
                TextValueLengthLimit = 8192,
                Tables =
                {
                    new PackageTableInfo
                    {
                        Uri = "sr_R1.csv", Table = "sr_R1", Title = "Раздел 1",
                        DeclaredRows = 5, ActualRows = 5, ColumnCount = 29, TypedFiltering = true,
                    },
                },
            },
            Issues =
            {
                new ValidationIssue
                {
                    Layer = ValidationLayer.CsvData, Severity = IssueSeverity.Error, Code = "CSV-020",
                    File = "sr_R1.csv", Table = "sr_R1", Row = 3,
                    Message = "Число строк данных (3) не совпадает с заявленным (5)",
                },
                new ValidationIssue
                {
                    Layer = ValidationLayer.BusinessRules, Severity = IssueSeverity.Warning,
                    Code = "RULE-valueAssertion_R1_05", File = "sr_R1.csv", Table = "sr_R1", Row = 2,
                    Column = "purcb_dic_Cntrct_Optn_dimGrp_1_periodGrp_1",
                    // значение с вертикальной чертой — обязано экранироваться в ячейке таблицы
                    Value = "010000-P | доп. раздел",
                    Message = "Отсутствует значение у элемента Вариант договора",
                },
            },
        };
        return report;
    }

    [Fact]
    public void Writer_ProducesMarkdownWithAllSections()
    {
        var path = Path.Combine(Path.GetTempPath(), "purcb-report-" + Guid.NewGuid().ToString("N") + ".md");
        try
        {
            new MarkdownReportWriter().Write(SampleReport(), path);

            Assert.True(File.Exists(path));
            var md = File.ReadAllText(path);

            // разделы отчёта
            Assert.Contains("# Отчёт валидации ПУРЦБ", md);
            Assert.Contains("## Сводка", md);
            Assert.Contains("### По слоям проверки", md);
            Assert.Contains("## Замечания", md);
            Assert.Contains("## Таблицы пакета", md);

            // вердикт и счётчики
            Assert.Contains("ПАКЕТ НЕ ПРОШЁЛ ПРОВЕРКУ", md);
            Assert.Contains("Ошибок: 1", md);

            // параметры пакета
            Assert.Contains("1234567890123", md);
            Assert.Contains("2026-04-30", md);

            // замечания: коды, сообщения, заголовок таблицы
            Assert.Contains("CSV-020", md);
            Assert.Contains("RULE-valueAssertion_R1_05", md);
            Assert.Contains("не совпадает с заявленным", md);
            Assert.Contains("| № | Критичность |", md);

            // таблица пакета
            Assert.Contains("sr_R1.csv", md);
            Assert.Contains("typedFiltering", md);

            // экранирование: значение с «|» не ломает таблицу,
            // разделитель в сводке тоже экранирован
            Assert.Contains("010000-P \\| доп. раздел", md);
            Assert.Contains("\\| / \"", md);
            Assert.DoesNotContain("010000-P | доп. раздел", md);
        }
        finally
        {
            try { File.Delete(path); } catch { /* временный файл */ }
        }
    }

    [Fact]
    public void Writer_PassedReport_HasNoIssuesTable()
    {
        var report = new ValidationReport
        {
            Package = new PackageInfo
            {
                MappingPath = "/packages/clean/mapping.json",
                Tables = { new PackageTableInfo { Uri = "sr_R1.csv", Table = "sr_R1", Title = "Раздел 1", DeclaredRows = 1, ActualRows = 1, ColumnCount = 10, TypedFiltering = true } },
            },
        };
        var path = Path.Combine(Path.GetTempPath(), "purcb-report-" + Guid.NewGuid().ToString("N") + ".md");
        try
        {
            new MarkdownReportWriter().Write(report, path);
            var md = File.ReadAllText(path);

            Assert.Contains("ПАКЕТ ПРОШЁЛ ПРОВЕРКУ", md);
            Assert.Contains("Замечаний нет", md);
            Assert.DoesNotContain("| № | Критичность |", md);
        }
        finally
        {
            try { File.Delete(path); } catch { /* временный файл */ }
        }
    }
}
