namespace PurcbValidator.Core.Csv;

/// <summary>Результат лексического разбора CSV: поля строки и ошибки экранирования.</summary>
public sealed class CsvRow
{
    /// <summary>Поля строки (без ограничителей текста).</summary>
    public List<string> Fields { get; } = new();

    /// <summary>Ошибки, зафиксированные при разборе строки.</summary>
    public List<string> Errors { get; } = new();

    /// <summary>Поля, которые были взяты в ограничители текста.</summary>
    public bool[]? Quoted { get; set; }

    public bool HasErrors => Errors.Count > 0;
}

/// <summary>
/// Лексический анализатор CSV с учётом разделителя и ограничителя текста
/// из заголовка mapping.json. Поддерживает вложенные разделители, переводы строк
/// и удвоенные кавычки внутри поля (RFC 4180), фиксирует некорректное экранирование.
/// </summary>
public sealed class CsvLexer
{
    private readonly string _delimiter;
    private readonly char _qualifier;

    public CsvLexer(string delimiter = "|", string textQualifier = "\"")
    {
        _delimiter = string.IsNullOrEmpty(delimiter) ? "|" : delimiter;
        _qualifier = string.IsNullOrEmpty(textQualifier) ? '"' : textQualifier[0];
    }

    /// <summary>
    /// Разбирает текст CSV в строки. Ошибки уровня файла (незакрытая кавычка)
    /// добавляются в последнюю строку и в fileErrors.
    /// </summary>
    /// <param name="text">Содержимое CSV-файла.</param>
    /// <param name="fileErrors">Список файловых ошибок.</param>
    /// <param name="lineNumbers">Номера физических строк начала каждой записи.</param>
    public List<CsvRow> Parse(string text, List<string> fileErrors, List<long>? lineNumbers = null)
    {
        var rows = new List<CsvRow>();
        var row = new CsvRow();
        var field = new System.Text.StringBuilder();
        var inQuotes = false;
        var fieldQuoted = false;
        var wasQuoted = false;
        long line = 1;
        long rowStartLine = 1;

        void EndField()
        {
            row.Fields.Add(field.ToString());
            row.Quoted = row.Quoted is { } q ? AppendFlag(q, wasQuoted) : new[] { wasQuoted };
            field.Clear();
            fieldQuoted = false;
            wasQuoted = false;
        }

        void EndRow(bool skipIfEmpty)
        {
            // последняя запись без разделителя в конце — поле уже добавлено вызовом EndField
            if (row.Fields.Count == 0 && skipIfEmpty)
                return;
            rows.Add(row);
            lineNumbers?.Add(rowStartLine);
            row = new CsvRow();
            rowStartLine = line;
        }

        var i = 0;
        while (i < text.Length)
        {
            var ch = text[i];

            if (inQuotes)
            {
                if (ch == _qualifier)
                {
                    if (i + 1 < text.Length && text[i + 1] == _qualifier)
                    {
                        field.Append(_qualifier);
                        i += 2;
                        continue;
                    }
                    inQuotes = false;
                    i++;
                    continue;
                }
                if (ch is '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n') { line++; i += 2; field.Append("\r\n"); continue; }
                    line++; i++; field.Append('\r'); continue;
                }
                if (ch == '\n') { line++; field.Append('\n'); i++; continue; }
                field.Append(ch);
                i++;
                continue;
            }

            if (ch == _qualifier && field.Length == 0 && !fieldQuoted)
            {
                inQuotes = true;
                fieldQuoted = true;
                wasQuoted = true;
                i++;
                continue;
            }

            if (ch == _qualifier)
            {
                // кавычка в середине поля — некорректное экранирование
                row.Errors.Add($"Строка {rowStartLine}: символ ограничителя текста {_qualifier} в середине поля");
                field.Append(ch);
                i++;
                continue;
            }

            if (StartsWith(text, i, _delimiter))
            {
                EndField();
                i += _delimiter.Length;
                continue;
            }

            if (ch == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                EndField();
                EndRow(true);
                line++;
                i++;
                rowStartLine = line;
                continue;
            }
            if (ch == '\n')
            {
                EndField();
                EndRow(true);
                line++;
                i++;
                rowStartLine = line;
                continue;
            }

            field.Append(ch);
            i++;
        }

        // хвост
        if (field.Length > 0 || row.Fields.Count > 0 || row.HasErrors)
        {
            EndField();
            EndRow(false);
        }

        if (inQuotes)
            fileErrors.Add("Незакрытый ограничитель текста в конце файла");

        return rows;
    }

    private static bool StartsWith(string text, int pos, string token)
    {
        if (pos + token.Length > text.Length) return false;
        for (var k = 0; k < token.Length; k++)
            if (text[pos + k] != token[k]) return false;
        return true;
    }

    private static bool[] AppendFlag(bool[] arr, bool v)
    {
        var res = new bool[arr.Length + 1];
        arr.CopyTo(res, 0);
        res[^1] = v;
        return res;
    }
}
