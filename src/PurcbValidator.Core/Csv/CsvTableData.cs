using PurcbValidator.Core.Csv;

namespace PurcbValidator.Core.Csv;

/// <summary>Загруженная CSV-таблица пакета.</summary>
public sealed class CsvTableData
{
    public string TableName { get; init; } = "";
    public string Uri { get; init; } = "";
    public string Path { get; init; } = "";
    public List<string> HeaderColumns { get; init; } = new();
    public List<CsvRow> Rows { get; init; } = new();
    /// <summary>Физические номера строк (в файле) для каждой записи данных.</summary>
    public List<long> RowLines { get; init; } = new();
    public List<string> FileErrors { get; init; } = new();

    /// <summary>Значение ячейки по имени колонки; null, если колонки нет.</summary>
    public string? Cell(CsvRow row, string column)
    {
        var idx = HeaderColumns.IndexOf(column);
        return idx < 0 || idx >= row.Fields.Count ? null : row.Fields[idx];
    }

    public int ColumnIndex(string column) => HeaderColumns.IndexOf(column);
}
