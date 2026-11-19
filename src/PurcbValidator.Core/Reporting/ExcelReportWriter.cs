using System.IO.Compression;
using System.Text;
using PurcbValidator.Core.Models;

namespace PurcbValidator.Core.Reporting;

/// <summary>
/// Формирование отчёта валидации в формате .xlsx без внешних зависимостей
/// (минимальный корректный OOXML-пакет: сводные листы, стили, автофильтр,
/// закрепление заголовков, подсветка критичности).
/// </summary>
public sealed class ExcelReportWriter
{
    public void Write(ValidationReport report, string outputPath)
    {
        // FileMode.Create — перезапись существующего файла
        using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        AddEntry(archive, "[Content_Types].xml", ContentTypes());
        AddEntry(archive, "_rels/.rels", RootRels());
        AddEntry(archive, "xl/workbook.xml", Workbook());
        AddEntry(archive, "xl/_rels/workbook.xml.rels", WorkbookRels());
        AddEntry(archive, "xl/styles.xml", Styles());
        AddEntry(archive, "xl/worksheets/sheet1.xml", SummarySheet(report));
        AddEntry(archive, "xl/worksheets/sheet2.xml", IssuesSheet(report));
        AddEntry(archive, "xl/worksheets/sheet3.xml", TablesSheet(report));
    }

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    // -------------------------------------------------------------- части пакета

    private static string ContentTypes() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
          <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
          <Override PartName="/xl/worksheets/sheet2.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
          <Override PartName="/xl/worksheets/sheet3.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
          <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
        </Types>
        """;

    private static string RootRels() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
        </Relationships>
        """;

    private static string Workbook() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <sheets>
            <sheet name="Сводка" sheetId="1" r:id="rId1"/>
            <sheet name="Замечания" sheetId="2" r:id="rId2"/>
            <sheet name="Таблицы пакета" sheetId="3" r:id="rId3"/>
          </sheets>
        </workbook>
        """;

    private static string WorkbookRels() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
          <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet2.xml"/>
          <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet3.xml"/>
          <Relationship Id="rId4" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
        </Relationships>
        """;

    /// <summary>Стили: 0 — обычный, 1 — заголовок, 2 — ошибка, 3 — предупреждение, 4 — информация, 5 — итог, 6 — метка.</summary>
    private static string Styles() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <fonts count="5">
            <font><sz val="10"/><name val="Calibri"/></font>
            <font><b/><sz val="10"/><color rgb="FFFFFFFF"/><name val="Calibri"/></font>
            <font><b/><sz val="10"/><color rgb="FF9C0006"/><name val="Calibri"/></font>
            <font><b/><sz val="10"/><color rgb="FF9C6500"/><name val="Calibri"/></font>
            <font><sz val="10"/><color rgb="FF1F4E79"/><name val="Calibri"/></font>
          </fonts>
          <fills count="7">
            <fill><patternFill patternType="none"/></fill>
            <fill><patternFill patternType="gray125"/></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FF1F4E79"/><bgColor indexed="64"/></patternFill></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FFFFC7CE"/><bgColor indexed="64"/></patternFill></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FFFFEB9C"/><bgColor indexed="64"/></patternFill></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FFDDEBF7"/><bgColor indexed="64"/></patternFill></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FFF2F2F2"/><bgColor indexed="64"/></patternFill></fill>
          </fills>
          <borders count="2">
            <border><left/><right/><top/><bottom/><diagonal/></border>
            <border><left style="thin"><color rgb="FFB0B0B0"/></left><right style="thin"><color rgb="FFB0B0B0"/></right><top style="thin"><color rgb="FFB0B0B0"/></top><bottom style="thin"><color rgb="FFB0B0B0"/></bottom><diagonal/></border>
          </borders>
          <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
          <cellXfs count="7">
            <xf numFmtId="0" fontId="0" fillId="0" borderId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment vertical="top" wrapText="1"/></xf>
            <xf numFmtId="0" fontId="1" fillId="2" borderId="1" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment vertical="center" wrapText="1"/></xf>
            <xf numFmtId="0" fontId="2" fillId="3" borderId="1" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment vertical="top" wrapText="1"/></xf>
            <xf numFmtId="0" fontId="3" fillId="4" borderId="1" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment vertical="top" wrapText="1"/></xf>
            <xf numFmtId="0" fontId="4" fillId="5" borderId="1" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment vertical="top" wrapText="1"/></xf>
            <xf numFmtId="0" fontId="0" fillId="6" borderId="1" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment vertical="top"/></xf>
            <xf numFmtId="0" fontId="4" fillId="0" borderId="0" applyFont="1" applyAlignment="1"><alignment vertical="top"/></xf>
          </cellXfs>
          <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
        </styleSheet>
        """;

    // -------------------------------------------------------------- листы

    private string SummarySheet(ValidationReport report)
    {
        var p = report.Package;
        var sb = new StringBuilder(4096);
        sb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""").Append('\n');
        sb.Append("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><cols>""");
        sb.Append("""<col min="1" max="1" width="38" customWidth="1"/><col min="2" max="2" width="70" customWidth="1"/>""");
        sb.Append("</cols><sheetData>");

        Row(sb, 1, 1, Cell("Параметр", 1), Cell("Значение", 1));
        var r = 2;
        void Pair(string name, string? value)
        {
            Row(sb, r++, 1, Cell(name, 6), Cell(value ?? "—"));
        }

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

        r++;
        Row(sb, r++, 1, Cell("Результат проверки", 1), Cell(report.Passed ? "ПАКЕТ ПРОШЁЛ ПРОВЕРКУ (ошибок нет)" : "ПАКЕТ НЕ ПРОШЁЛ ПРОВЕРКУ", report.Passed ? 4 : 2));
        Pair("Ошибок", report.ErrorCount.ToString());
        Pair("Предупреждений", report.WarningCount.ToString());
        Pair("Информационных замечаний", report.InfoCount.ToString());

        r++;
        Row(sb, r++, 1, Cell("Слой проверки", 1), Cell("Ошибки / Предупреждения / Инфо", 1));
        foreach (var g in report.Issues.GroupBy(i => i.Layer).OrderBy(g => g.Key))
        {
            var e = g.Count(i => i.Severity == IssueSeverity.Error);
            var w = g.Count(i => i.Severity == IssueSeverity.Warning);
            var n = g.Count(i => i.Severity == IssueSeverity.Info);
            Row(sb, r++, 1, Cell(g.Key, 6), Cell($"{e} / {w} / {n}"));
        }

        sb.Append("</sheetData></worksheet>");
        return sb.ToString();
    }

    private string IssuesSheet(ValidationReport report)
    {
        var sb = new StringBuilder(1 << 20);
        sb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""").Append('\n');
        sb.Append("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""");
        sb.Append("""<cols><col min="1" max="1" width="6" customWidth="1"/><col min="2" max="2" width="14" customWidth="1"/><col min="3" max="3" width="24" customWidth="1"/><col min="4" max="4" width="32" customWidth="1"/><col min="5" max="5" width="16" customWidth="1"/><col min="6" max="6" width="14" customWidth="1"/><col min="7" max="7" width="7" customWidth="1"/><col min="8" max="8" width="46" customWidth="1"/><col min="9" max="9" width="34" customWidth="1"/><col min="10" max="10" width="26" customWidth="1"/><col min="11" max="11" width="90" customWidth="1"/></cols>""");
        sb.Append("""<sheetViews><sheetView workbookViewId="0"><pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews>""");
        sb.Append("""<autoFilter ref="A1:K1"/>""");
        sb.Append("<sheetData>");

        Row(sb, 1, 1,
            Cell("№", 1), Cell("Критичность", 1), Cell("Слой", 1), Cell("Код", 1),
            Cell("Файл", 1), Cell("Таблица", 1), Cell("Строка", 1), Cell("Колонка", 1),
            Cell("Название показателя", 1), Cell("Значение", 1), Cell("Сообщение", 1));

        var n = 0;
        var order = new Dictionary<IssueSeverity, int> { [IssueSeverity.Error] = 0, [IssueSeverity.Warning] = 1, [IssueSeverity.Info] = 2 };
        foreach (var i in report.Issues.OrderBy(i => order[i.Severity]).ThenBy(i => i.Layer).ThenBy(i => i.File).ThenBy(i => i.Row ?? 0))
        {
            n++;
            var style = i.Severity switch
            {
                IssueSeverity.Error => 2,
                IssueSeverity.Warning => 3,
                _ => 4,
            };
            Row(sb, n + 1, 1,
                Cell(n.ToString()),
                Cell(SevName(i.Severity), style),
                Cell(i.Layer),
                Cell(i.Code),
                Cell(i.File ?? ""),
                Cell(i.Table ?? ""),
                i.Row is null ? Cell("") : CellNum(i.Row.Value),
                Cell(i.Column ?? ""),
                Cell(i.ColumnLabel ?? ""),
                Cell(i.Value ?? ""),
                Cell(i.Message, style));
        }

        sb.Append("</sheetData></worksheet>");
        return sb.ToString();
    }

    private string TablesSheet(ValidationReport report)
    {
        var sb = new StringBuilder(8192);
        sb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""").Append('\n');
        sb.Append("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""");
        sb.Append("""<cols><col min="1" max="1" width="16" customWidth="1"/><col min="2" max="2" width="50" customWidth="1"/><col min="3" max="3" width="18" customWidth="1"/><col min="4" max="4" width="14" customWidth="1"/><col min="5" max="5" width="16" customWidth="1"/><col min="6" max="6" width="10" customWidth="1"/><col min="7" max="7" width="16" customWidth="1"/></cols>""");
        sb.Append("""<sheetViews><sheetView workbookViewId="0"><pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews>""");
        sb.Append("""<autoFilter ref="A1:G1"/>""");
        sb.Append("<sheetData>");

        Row(sb, 1, 1,
            Cell("Таблица", 1), Cell("Название раздела", 1), Cell("Файл", 1),
            Cell("Строк заявлено", 1), Cell("Строк фактически", 1), Cell("Колонок", 1),
            Cell("typedFiltering", 1));

        var r = 2;
        foreach (var t in report.Package.Tables)
        {
            var mismatch = t.DeclaredRows.HasValue && t.ActualRows.HasValue && t.DeclaredRows != t.ActualRows;
            Row(sb, r++, 1,
                Cell(t.Table),
                Cell(t.Title),
                Cell(t.Uri),
                CellNum(t.DeclaredRows ?? 0),
                t.ActualRows is null ? Cell("файл не загружен", 2) : CellNum(t.ActualRows.Value, mismatch ? 2 : 0),
                CellNum(t.ColumnCount),
                Cell(t.TypedFiltering ? "да" : "нет"));
        }

        sb.Append("</sheetData></worksheet>");
        return sb.ToString();
    }

    // -------------------------------------------------------------- низкоуровневые построители

    private static void Row(StringBuilder sb, int index, int styleUnused, params string[] cells)
    {
        sb.Append("<row r=\"").Append(index).Append("\">");
        sb.Append(string.Concat(cells));
        sb.Append("</row>");
    }

    private static string Cell(string value, int style = 0)
    {
        var t = Esc(value);
        return $"""<c t="inlineStr" s="{style}"><is><t xml:space="preserve">{t}</t></is></c>""";
    }

    private static string CellNum(long value, int style = 0)
        => $"""<c s="{style}"><v>{value}</v></c>""";

    private static string SevName(IssueSeverity s) => s switch
    {
        IssueSeverity.Error => "ОШИБКА",
        IssueSeverity.Warning => "ПРЕДУПРЕЖДЕНИЕ",
        _ => "ИНФО",
    };

    private static string Esc(string s) => s
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;");
}
