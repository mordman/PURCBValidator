using System.Text;
using System.Text.Json;
using PurcbValidator.Core;
using PurcbValidator.Core.Models;

namespace PurcbValidator.Core.Tests;

/// <summary>
/// Юнит-тесты движка бизнес-правил на синтетическом пакете
/// с собственным файлом правил (изолированно от набора ЦБ).
/// </summary>
public class RuleEngineTests : IDisposable
{
    private readonly string _dir;

    public RuleEngineTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "purcb-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* временный каталог */ }
    }

    private string WritePackage(string mappingJson, string rulesJson, params (string name, string content)[] files)
    {
        File.WriteAllText(Path.Combine(_dir, "mapping.json"), mappingJson);
        foreach (var (name, content) in files)
            File.WriteAllText(Path.Combine(_dir, name), content);
        var rulesPath = Path.Combine(_dir, "rules.json");
        File.WriteAllText(rulesPath, rulesJson);
        return rulesPath;
    }

    private ValidationReport Run(string rulesPath, bool useSchema = false)
    {
        var runner = new ValidatorRunner();
        return runner.Run(new ValidationOptions
        {
            Path = Path.Combine(_dir, "mapping.json"),
            UseCbrSchema = useSchema,
            RulesFile = rulesPath,
        });
    }

    private const string Mapping = """
        {
          "@context": "www.cbr.ru/xbrl_csv2",
          "header": {
            "encoding": "UTF-8", "delimiter": "|", "reportDate": "2026-04-30",
            "textQualifier": "\"", "documentVersion": "test",
            "textValueLengthLimit": 8192,
            "identifier": "1234567890123", "requestNumberDifp": "12345_67"
          },
          "dtsReferences": { "type": "schema", "href": "test" },
          "tables": [
            {
              "uri": "sr_R1.csv", "roleUri": "r1", "csvRowsCount": 2, "typedFiltering": true,
              "columns": [
                { "columnId": "dim_int_C_CdTaxis", "xbrldi:typedMember": { "dimension": "dim-int_C_CdTaxis", "typedDomain": "dim-int_ID_FL_YUL_ReestrTypedName" } },
                { "columnId": "purcb_dic_Cntrct_BDt_dimGrp_1_periodGrp_1", "aspect": { "type": { "datatype": "date", "http://www.cbr.ru/xbrl-csv/model#columnType": "xbrli:dateItemType" }, "xbrl:concept": "purcb-dic_Cntrct_BDt" } },
                { "columnId": "purcb_dic_Cntrct_Optn_dimGrp_1_periodGrp_1", "aspect": { "type": { "datatype": "string", "http://www.cbr.ru/xbrl-csv/model#columnType": "xbrli:stringItemType" }, "xbrl:concept": "purcb-dic_Cntrct_Optn" } },
                { "columnId": "purcb_dic_Cntrct_Othr_dimGrp_1_periodGrp_1", "aspect": { "type": { "datatype": "string", "http://www.cbr.ru/xbrl-csv/model#columnType": "xbrli:stringItemType" }, "xbrl:concept": "purcb-dic_Cntrct_Othr" } },
                { "columnId": "purcb_dic_Actvts_dimGrp_1_periodGrp_1", "aspect": { "type": { "datatype": "string", "http://www.cbr.ru/xbrl-csv/model#columnType": "xbrli:stringItemType" }, "xbrl:concept": "purcb-dic_Actvts" } }
              ]
            },
            {
              "uri": "sr_sved_purcb.csv", "roleUri": "sved", "csvRowsCount": 1, "typedFiltering": false,
              "columns": [
                { "columnId": "purcb_dic_R1Enumerator_dimGrp_1_periodGrp_1", "aspect": { "type": { "datatype": "string" }, "xbrl:concept": "purcb-dic_R1Enumerator" } }
              ]
            }
          ]
        }
        """;

    private const string SvedCsv =
        "\"purcb_dic_R1Enumerator_dimGrp_1_periodGrp_1\"\n\"mem-int:DannyePeredanyMember\"\n";

    [Fact]
    public void RequiredIf_FiresWhenConditionMet()
    {
        var csv = "\"dim_int_C_CdTaxis\"|\"purcb_dic_Cntrct_BDt_dimGrp_1_periodGrp_1\"|\"purcb_dic_Cntrct_Optn_dimGrp_1_periodGrp_1\"|\"purcb_dic_Cntrct_Othr_dimGrp_1_periodGrp_1\"|\"purcb_dic_Actvts_dimGrp_1_periodGrp_1\"\n" +
                  "\"cl-1\"|\"2026-01-15\"|\"\"|\"\"|\"999\"\n" +
                  "\"cl-2\"|\"2026-01-15\"|\"1\"|\"\"|\"999\"\n";
        var rules = """
            { "rules": [ {
              "id": "TEST-REQ-1", "table": "sr_R1", "severity": "warning", "message": "тест: обязателен при наступившей дате",
              "check": "requiredIf", "column": "purcb_dic_Cntrct_Optn_dimGrp_1_periodGrp_1",
              "when": { "anyFilled": ["purcb_dic_Cntrct_BDt_dimGrp_1_periodGrp_1"],
                        "dateRules": [ { "left": "$reportDate", "right": "purcb_dic_Cntrct_BDt_dimGrp_1_periodGrp_1", "op": "ge" } ] } } ] }
            """;
        var rulesPath = WritePackage(Mapping, rules, ("sr_R1.csv", csv), ("sr_sved_purcb.csv", SvedCsv));

        var report = Run(rulesPath);
        var v = report.Issues.Where(i => i.Code == "RULE-TEST-REQ-1").ToList();
        Assert.Single(v);
        Assert.Equal(1, v[0].Row!.Value);
        Assert.Equal("purcb_dic_Cntrct_Optn_dimGrp_1_periodGrp_1", v[0].Column);
    }

    [Fact]
    public void RequiredIf_SkippedWhenDateInFuture()
    {
        // дата договора позже отчётной — правило неприменимо
        var csv = "\"dim_int_C_CdTaxis\"|\"purcb_dic_Cntrct_BDt_dimGrp_1_periodGrp_1\"|\"purcb_dic_Cntrct_Optn_dimGrp_1_periodGrp_1\"|\"purcb_dic_Cntrct_Othr_dimGrp_1_periodGrp_1\"|\"purcb_dic_Actvts_dimGrp_1_periodGrp_1\"\n" +
                  "\"cl-1\"|\"2026-12-31\"|\"\"|\"\"|\"999\"\n" +
                  "\"cl-2\"|\"2026-01-15\"|\"1\"|\"\"|\"999\"\n";
        var rules = """
            { "rules": [ {
              "id": "TEST-REQ-2", "table": "sr_R1", "severity": "warning", "message": "тест",
              "check": "requiredIf", "column": "purcb_dic_Cntrct_Optn_dimGrp_1_periodGrp_1",
              "when": { "anyFilled": ["purcb_dic_Cntrct_BDt_dimGrp_1_periodGrp_1"],
                        "dateRules": [ { "left": "$reportDate", "right": "purcb_dic_Cntrct_BDt_dimGrp_1_periodGrp_1", "op": "ge" } ] } } ] }
            """;
        var rulesPath = WritePackage(Mapping, rules, ("sr_R1.csv", csv), ("sr_sved_purcb.csv", SvedCsv));

        var report = Run(rulesPath);
        Assert.DoesNotContain(report.Issues, i => i.Code == "RULE-TEST-REQ-2");
    }

    [Fact]
    public void ForbiddenIf_FiresWhenFilledUnderCondition()
    {
        var csv = "\"dim_int_C_CdTaxis\"|\"purcb_dic_Cntrct_BDt_dimGrp_1_periodGrp_1\"|\"purcb_dic_Cntrct_Optn_dimGrp_1_periodGrp_1\"|\"purcb_dic_Cntrct_Othr_dimGrp_1_periodGrp_1\"|\"purcb_dic_Actvts_dimGrp_1_periodGrp_1\"\n" +
                  "\"cl-1\"|\"2026-01-15\"|\"1\"|\"иное описание\"|\"111\"\n" +
                  "\"cl-2\"|\"2026-01-15\"|\"1\"|\"\"|\"999\"\n";
        var rules = """
            { "rules": [ {
              "id": "TEST-FORBID-1", "table": "sr_R1", "severity": "warning", "message": "тест: не заполняется при не-999",
              "check": "forbiddenIf", "column": "purcb_dic_Cntrct_Othr_dimGrp_1_periodGrp_1",
              "when": { "anyFilled": ["purcb_dic_Cntrct_Othr_dimGrp_1_periodGrp_1"],
                        "valueNotIn": [ { "column": "purcb_dic_Actvts_dimGrp_1_periodGrp_1", "values": ["999"] } ] } } ] }
            """;
        var rulesPath = WritePackage(Mapping, rules, ("sr_R1.csv", csv), ("sr_sved_purcb.csv", SvedCsv));

        var report = Run(rulesPath);
        var v = report.Issues.Where(i => i.Code == "RULE-TEST-FORBID-1").ToList();
        Assert.Single(v);
        Assert.Equal(1, v[0].Row!.Value);
    }

    [Fact]
    public void AllowedValues_RequiresFilledAndValid()
    {
        var csv = "\"dim_int_C_CdTaxis\"|\"purcb_dic_Cntrct_BDt_dimGrp_1_periodGrp_1\"|\"purcb_dic_Cntrct_Optn_dimGrp_1_periodGrp_1\"|\"purcb_dic_Cntrct_Othr_dimGrp_1_periodGrp_1\"|\"purcb_dic_Actvts_dimGrp_1_periodGrp_1\"\n" +
                  "\"cl-1\"|\"2026-01-15\"|\"9\"|\"\"|\"999\"\n" +
                  "\"cl-2\"|\"2026-01-15\"|\"\"|\"\"|\"999\"\n";
        var rules = """
            { "rules": [ {
              "id": "TEST-VALS-1", "table": "sr_R1", "severity": "error", "message": "тест: только 1 или 2",
              "check": "allowedValuesIf", "column": "purcb_dic_Cntrct_Optn_dimGrp_1_periodGrp_1",
              "values": ["1", "2"],
              "when": { "anyFilled": ["purcb_dic_Cntrct_BDt_dimGrp_1_periodGrp_1"] } } ] }
            """;
        var rulesPath = WritePackage(Mapping, rules, ("sr_R1.csv", csv), ("sr_sved_purcb.csv", SvedCsv));

        var report = Run(rulesPath);
        var v = report.Issues.Where(i => i.Code == "RULE-TEST-VALS-1").ToList();
        // строка 1: значение 9 вне набора; строка 2: пусто — по семантике SQL тоже нарушение
        Assert.Equal(2, v.Count);
        Assert.All(v, i => Assert.Equal(IssueSeverity.Error, i.Severity));
    }

    [Fact]
    public void UniqueAxes_DuplicateIsReported()
    {
        var csv = "\"dim_int_C_CdTaxis\"|\"purcb_dic_Cntrct_BDt_dimGrp_1_periodGrp_1\"|\"purcb_dic_Cntrct_Optn_dimGrp_1_periodGrp_1\"|\"purcb_dic_Cntrct_Othr_dimGrp_1_periodGrp_1\"|\"purcb_dic_Actvts_dimGrp_1_periodGrp_1\"\n" +
                  "\"cl-1\"|\"2026-01-15\"|\"1\"|\"\"|\"999\"\n" +
                  "\"cl-1\"|\"2026-02-15\"|\"2\"|\"\"|\"999\"\n";
        var rulesPath = WritePackage(Mapping, "{ \"rules\": [] }",
            ("sr_R1.csv", csv), ("sr_sved_purcb.csv", SvedCsv));

        var report = Run(rulesPath);
        Assert.Contains(report.Issues, i => i.Code == "BUILTIN-UNIQUE" && i.Table == "sr_R1");
    }

    [Fact]
    public void PresenceFlag_MissingTableDataIsReported()
    {
        // заголовок без строк данных при флаге «данные переданы»
        var headerOnly = "\"dim_int_C_CdTaxis\"|\"purcb_dic_Cntrct_BDt_dimGrp_1_periodGrp_1\"|\"purcb_dic_Cntrct_Optn_dimGrp_1_periodGrp_1\"|\"purcb_dic_Cntrct_Othr_dimGrp_1_periodGrp_1\"|\"purcb_dic_Actvts_dimGrp_1_periodGrp_1\"\n";
        var rulesPath = WritePackage(Mapping.Replace("\"csvRowsCount\": 2", "\"csvRowsCount\": 0"),
            "{ \"rules\": [] }",
            ("sr_R1.csv", headerOnly), ("sr_sved_purcb.csv", SvedCsv));

        var report = Run(rulesPath);
        Assert.Contains(report.Issues, i => i.Code == "BUILTIN-PRESENCE");
    }

    [Fact]
    public void ValueConjunction_AllConditionsMustHold()
    {
        var csv = "\"dim_int_C_CdTaxis\"|\"purcb_dic_Cntrct_BDt_dimGrp_1_periodGrp_1\"|\"purcb_dic_Cntrct_Optn_dimGrp_1_periodGrp_1\"|\"purcb_dic_Cntrct_Othr_dimGrp_1_periodGrp_1\"|\"purcb_dic_Actvts_dimGrp_1_periodGrp_1\"\n" +
                  "\"cl-1\"|\"2026-01-15\"|\"1\"|\"\"|\"111\"\n" +
                  "\"cl-2\"|\"2026-01-15\"|\"1\"|\"\"|\"999\"\n";
        var rules = """
            { "rules": [ {
              "id": "TEST-CONJ-1", "table": "sr_R1", "severity": "warning", "message": "тест: 111 -> Optn=2",
              "check": "valueConjunction",
              "conditions": [ { "column": "purcb_dic_Actvts_dimGrp_1_periodGrp_1", "values": ["111"] },
                              { "column": "purcb_dic_Cntrct_Optn_dimGrp_1_periodGrp_1", "values": ["2"] } ],
              "when": { "anyFilled": ["purcb_dic_Actvts_dimGrp_1_periodGrp_1"],
                        "valueIn": [ { "column": "purcb_dic_Actvts_dimGrp_1_periodGrp_1", "values": ["111"] } ] } } ] }
            """;
        var rulesPath = WritePackage(Mapping, rules, ("sr_R1.csv", csv), ("sr_sved_purcb.csv", SvedCsv));

        var report = Run(rulesPath);
        // применимы только строки с Actvts=111; в них Optn обязан быть равен 2
        var v = report.Issues.Where(i => i.Code == "RULE-TEST-CONJ-1").ToList();
        Assert.Single(v);
    }
}
