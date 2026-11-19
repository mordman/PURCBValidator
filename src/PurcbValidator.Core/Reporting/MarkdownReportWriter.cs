using System.Text;
using PurcbValidator.Core.Models;

namespace PurcbValidator.Core.Reporting;

/// <summary>
/// Формирование отчёта валидации в формате Markdown: те же разделы, что и в
/// Excel-отчёте (сводка, распределение по слоям, замечания, таблицы пакета).
/// Удобно для вставки в задачи, wiki и merge-запросы.
/// </summary>
public sealed class MarkdownReportWriter
{
    public void Write(ValidationReport report, string outputPath)
    {
        // FileMode.Create — перезапись существующего файла; UTF-8 без BOM
        using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(Build(report));
    }

    // -------------------------------------------------------------- построение

    private static string Build(ValidationReport report)
    {
        var p = report.Package;
        var sb = new StringBuilder(1 << 16);

        sb.AppendLine("# Отчёт валидации ПУРЦБ");
        sb.AppendLine();
        sb.AppendLine(report.Passed
            ? "**Вердикт: ПАКЕТ ПРОШЁЛ ПРОВЕРКУ (ошибок нет).**"
            : "**Вердикт: ПАКЕТ НЕ ПРОШЁЛ ПРОВЕРКУ.**");
        sb.AppendLine();
        sb.AppendLine($"Ошибок: {report.ErrorCount} • Предупреждений: {report.WarningCount} • Инфо: {report.InfoCount}");
        sb.AppendLine();
        sb.AppendLine($"Сформирован {report.FinishedUtc:yyyy-MM-dd HH:mm:ss} UTC, длительность проверки {(report.FinishedUtc - report.StartedUtc).TotalSeconds:0.0} с.");
        sb.AppendLine();

        // ------------------------------------------------ сводка по пакету
        sb.AppendLine("## Сводка");
        sb.AppendLine();
        sb.AppendLine("| Параметр | Значение |");
        sb.AppendLine("| --- | --- |");
        void Pair(string name, string? value) => sb.AppendLine($"| {Esc(name)} | {Esc(value ?? "—")} |");

        Pair("Файл пакета", p.MappingPath);
        Pair("Дата отчетного момента (reportDate)", p.ReportDate);
        Pair("Идентификатор организации", p.Identifier);
        Pair("Номер запроса ДИФП", p.RequestNumberDifp);
        Pair("Версия схемы (documentVersion)", p.DocumentVersion);
        Pair("Точка входа таксономии (dtsReferences.href)", p.DtsHref);
        Pair("Разделитель / ограничитель текста", $"{p.Delimiter ?? ""} / {p.TextQualifier ?? ""}");
        Pair("Кодировка", p.Encoding);
        Pair("Ограничение длины текстовых значений", p.TextValueLengthLimit?.ToString());
        Pair("Таблиц в пакете", p.Tables.Count.ToString());
        Pair("Дата проверки (UTC)", report.FinishedUtc.ToString("yyyy-MM-dd HH:mm:ss"));
        Pair("Длительность проверки", $"{(report.FinishedUtc - report.StartedUtc).TotalSeconds:0.0} с");
        sb.AppendLine();

        // ------------------------------------------------ распределение по слоям
        sb.AppendLine("### По слоям проверки");
        sb.AppendLine();
        sb.AppendLine("| Слой проверки | Ошибки | Предупреждения | Инфо |");
        sb.AppendLine("| --- | ---: | ---: | ---: |");
        foreach (var g in report.Issues.GroupBy(i => i.Layer).OrderBy(g => g.Key))
        {
            var e = g.Count(i => i.Severity == IssueSeverity.Error);
            var w = g.Count(i => i.Severity == IssueSeverity.Warning);
            var n = g.Count(i => i.Severity == IssueSeverity.Info);
            sb.AppendLine($"| {Esc(g.Key)} | {e} | {w} | {n} |");
        }
        sb.AppendLine();

        // ------------------------------------------------ замечания
        sb.AppendLine("## Замечания");
        sb.AppendLine();
        if (report.Issues.Count == 0)
        {
            sb.AppendLine("Замечаний нет — пакет прошёл все проверки.");
        }
        else
        {
            sb.AppendLine("| № | Критичность | Слой | Код | Файл | Таблица | Строка | Колонка | Показатель | Значение | Сообщение |");
            sb.AppendLine("| ---: | --- | --- | --- | --- | --- | ---: | --- | --- | --- | --- |");
            var n = 0;
            var order = new Dictionary<IssueSeverity, int> { [IssueSeverity.Error] = 0, [IssueSeverity.Warning] = 1, [IssueSeverity.Info] = 2 };
            foreach (var i in report.Issues.OrderBy(i => order[i.Severity]).ThenBy(i => i.Layer).ThenBy(i => i.File).ThenBy(i => i.Row ?? 0))
            {
                n++;
                sb.AppendLine($"| {n} | {SevName(i.Severity)} | {Esc(i.Layer)} | {Esc(i.Code)} | {Esc(i.File)} | {Esc(i.Table)} | {i.Row?.ToString() ?? "—"} | {Esc(i.Column)} | {Esc(i.ColumnLabel)} | {Esc(i.Value)} | {Esc(i.Message)} |");
            }
        }
        sb.AppendLine();

        // ------------------------------------------------ таблицы пакета
        sb.AppendLine("## Таблицы пакета");
        sb.AppendLine();
        sb.AppendLine("| Таблица | Название раздела | Файл | Строк заявлено | Строк фактически | Колонок | typedFiltering |");
        sb.AppendLine("| --- | --- | --- | ---: | ---: | ---: | --- |");
        foreach (var t in report.Package.Tables)
        {
            var actual = t.ActualRows?.ToString() ?? "файл не загружен";
            sb.AppendLine($"| {Esc(t.Table)} | {Esc(t.Title)} | {Esc(t.Uri)} | {t.DeclaredRows ?? 0} | {actual} | {t.ColumnCount} | {(t.TypedFiltering ? "да" : "нет")} |");
        }
        sb.AppendLine();

        return sb.ToString();
    }

    private static string SevName(IssueSeverity s) => s switch
    {
        IssueSeverity.Error => "**ОШИБКА**",
        IssueSeverity.Warning => "**ПРЕДУПРЕЖДЕНИЕ**",
        _ => "ИНФО",
    };

    /// <summary>Экранирование для ячейки таблицы Markdown: без переносов строк и вертикальных черт.</summary>
    private static string Esc(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\r", " ").Replace("\n", " ").Replace("|", "\\|");
    }
}
