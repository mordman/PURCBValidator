using System.IO.Compression;
using System.Text;
using PurcbValidator.Core.Models;
using PurcbValidator.Core.Reporting;

namespace PurcbValidator.Core.Tests;

public class ExcelReportWriterTests
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
                    Message = "Отсутствует значение у элемента Вариант договора",
                },
            },
        };
        return report;
    }

    [Fact]
    public void Writer_ProducesValidZipWithSheets()
    {
        var path = Path.Combine(Path.GetTempPath(), "purcb-report-" + Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            new ExcelReportWriter().Write(SampleReport(), path);

            Assert.True(File.Exists(path));
            Assert.True(new FileInfo(path).Length > 1000);

            using var zip = ZipFile.OpenRead(path);
            var names = zip.Entries.Select(e => e.FullName).ToHashSet();
            Assert.Contains("[Content_Types].xml", names);
            Assert.Contains("xl/workbook.xml", names);
            Assert.Contains("xl/worksheets/sheet1.xml", names);
            Assert.Contains("xl/worksheets/sheet2.xml", names);
            Assert.Contains("xl/worksheets/sheet3.xml", names);
            Assert.Contains("xl/styles.xml", names);

            // каждая XML-часть обязана быть корректно сформированной
            foreach (var entry in zip.Entries)
            {
                if (!entry.FullName.EndsWith(".xml") && !entry.FullName.EndsWith(".rels"))
                    continue;
                using var es = entry.Open();
                var doc = System.Xml.Linq.XDocument.Load(es);
                Assert.NotNull(doc.Root);
            }

            // в листе замечаний должны быть коды и сообщения
            using var stream = zip.GetEntry("xl/worksheets/sheet2.xml")!.Open();
            var xml = new StreamReader(stream, Encoding.UTF8).ReadToEnd();
            Assert.Contains("CSV-020", xml);
            Assert.Contains("RULE-valueAssertion_R1_05", xml);
            Assert.Contains("не совпадает с заявленным", xml);
            Assert.DoesNotContain("&amp;lt;", xml); // нет двойного экранирования
        }
        finally
        {
            try { File.Delete(path); } catch { /* временный файл */ }
        }
    }

    [Fact]
    public void Report_Counts()
    {
        var r = SampleReport();
        Assert.Equal(1, r.ErrorCount);
        Assert.Equal(1, r.WarningCount);
        Assert.Equal(0, r.InfoCount);
        Assert.False(r.Passed);
    }
}
