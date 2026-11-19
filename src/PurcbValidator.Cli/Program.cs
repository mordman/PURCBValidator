using System.Text;
using PurcbValidator.Core;
using PurcbValidator.Core.Models;
using PurcbValidator.Core.Reporting;

namespace PurcbValidator.Cli;

/// <summary>
/// Консольный запуск валидатора ПУРЦБ (удобен для CI-конвейеров):
///   purcb-validator &lt;путь к mapping.json или папке пакета&gt; [параметры]
/// Параметры:
///   --no-schema      не проверять по официальным JSON-схемам ЦБ
///   --rules &lt;файл&gt;   альтернативный файл правил rules.json
///   --taxonomy &lt;версия&gt; версия таксономии: auto, 6.1.0.10 (УОД), 7.1 (10-РД)
///   --out &lt;файл&gt;     сохранить отчёт (.md — Markdown или .xlsx — Excel)
///   --quiet          не печатать полный список замечаний
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length == 0 || args[0] is "--help" or "-h" or "/?")
        {
            Console.WriteLine("Валидатор отчётности ПУРЦБ — УОД, XBRL-CSV 2.0 (таксономия ЦБ РФ 6.1.0.10)");
            Console.WriteLine();
            Console.WriteLine("Использование: purcb-validator <путь к mapping.json или папке пакета> [параметры]");
            Console.WriteLine("  --no-schema      не проверять по официальным JSON-схемам ЦБ");
            Console.WriteLine("  --rules <файл>   альтернативный файл правил rules.json");
            Console.WriteLine("  --taxonomy <вер> версия таксономии: auto (по умолчанию), 6.1.0.10, 7.1");
            Console.WriteLine("  --out <файл>     сохранить отчёт (.md — Markdown или .xlsx — Excel)");
            Console.WriteLine("  --quiet          печатать только сводку");
            return 0;
        }

        var path = args[0];
        var useSchema = true;
        string? rulesFile = null;
        string? outFile = null;
        string? taxonomy = null;
        var quiet = false;

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--no-schema": useSchema = false; break;
                case "--rules" when i + 1 < args.Length: rulesFile = args[++i]; break;
                case "--taxonomy" when i + 1 < args.Length: taxonomy = args[++i]; break;
                case "--out" when i + 1 < args.Length: outFile = args[++i]; break;
                case "--quiet": quiet = true; break;
                default:
                    Console.WriteLine($"Неизвестный параметр: {args[i]}");
                    return 2;
            }
        }

        var runner = new ValidatorRunner();
        var report = runner.Run(new ValidationOptions
        {
            Path = path,
            UseCbrSchema = useSchema,
            RulesFile = rulesFile,
        }, msg => Console.WriteLine("→ " + msg));

        Console.WriteLine();
        Console.WriteLine("══════════════ ИТОГИ ПРОВЕРКИ ══════════════");
        Console.WriteLine($"Файл пакета:        {report.Package.MappingPath}");
        Console.WriteLine($"Версия таксономии:  {report.Package.TaxonomyVersionName} ({report.Package.TaxonomyVersionSource})");
        Console.WriteLine($"Дата отчётности:    {report.Package.ReportDate ?? "—"}");
        Console.WriteLine($"Идентификатор:      {report.Package.Identifier ?? "—"}");
        Console.WriteLine($"Номер запроса ДИФП: {report.Package.RequestNumberDifp ?? "—"}");
        Console.WriteLine($"Таблиц в пакете:    {report.Package.Tables.Count}");
        Console.WriteLine($"Ошибок:             {report.ErrorCount}");
        Console.WriteLine($"Предупреждений:      {report.WarningCount}");
        Console.WriteLine($"Инфо:               {report.InfoCount}");
        Console.WriteLine(report.Passed
            ? "ВЕРДИКТ: ПАКЕТ ПРОШЁЛ ПРОВЕРКУ"
            : "ВЕРДИКТ: ПАКЕТ НЕ ПРОШЁЛ ПРОВЕРКУ");

        if (!quiet && report.Issues.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("══════════════ ЗАМЕЧАНИЯ ══════════════");
            foreach (var g in report.Issues.GroupBy(i => i.Layer))
            {
                Console.WriteLine();
                Console.WriteLine($"[{g.Key}] — {g.Count()}");
                foreach (var i in g.OrderBy(x => x.Severity).ThenBy(x => x.File).ThenBy(x => x.Row ?? 0))
                {
                    var where = new List<string>();
                    if (!string.IsNullOrEmpty(i.File)) where.Add(i.File);
                    if (!string.IsNullOrEmpty(i.Table)) where.Add(i.Table);
                    if (i.Row is not null) where.Add("строка " + i.Row);
                    if (!string.IsNullOrEmpty(i.Column)) where.Add(i.Column);
                    Console.WriteLine($"  [{Sev(i.Severity)}] {i.Code}: {i.Message}");
                    if (where.Count > 0)
                        Console.WriteLine($"      ↳ {string.Join(" | ", where)}" +
                            (i.Value is null ? "" : $" | значение: {Trunc(i.Value)}"));
                }
            }
        }

        if (outFile is not null)
        {
            try
            {
                // формат отчёта определяется расширением: .md — Markdown, иначе Excel
                if (outFile.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                    new MarkdownReportWriter().Write(report, outFile);
                else
                    new ExcelReportWriter().Write(report, outFile);
                Console.WriteLine();
                Console.WriteLine($"Отчёт сохранён: {outFile}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Не удалось сохранить отчёт: {ex.Message}");
                return 2;
            }
        }

        return report.Passed ? 0 : 1;
    }

    private static string Sev(IssueSeverity s) => s switch
    {
        IssueSeverity.Error => "ОШИБКА",
        IssueSeverity.Warning => "ПРЕДУПР",
        _ => "ИНФО",
    };

    private static string Trunc(string v) => v.Length <= 80 ? v : v[..80] + "…";
}
