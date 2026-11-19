using System.Diagnostics;
using System.Text;
using PurcbValidator.Core;
using PurcbValidator.Core.Models;
using PurcbValidator.Core.Reporting;

namespace PurcbValidator.App;

/// <summary>Главная форма валидатора ПУРЦБ.</summary>
public sealed class MainForm : Form
{
    private readonly TextBox _pathBox = new();
    private readonly Button _browseButton = new();
    private readonly Button _validateButton = new();
    private readonly Button _exportButton = new();
    private readonly CheckBox _schemaCheck = new();
    private readonly ComboBox _severityFilter = new();
    private readonly ComboBox _layerFilter = new();
    private readonly DataGridView _grid = new();
    private readonly Label _summaryLabel = new();
    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _statusLabel = new();
    private readonly ToolStripProgressBar _progress = new();

    private ValidationReport? _report;
    private CancellationTokenSource? _cts;
    private volatile bool _running;

    public MainForm()
    {
        Text = "Валидатор отчётности ПУРЦБ — УОД, XBRL-CSV 2.0 (таксономия ЦБ РФ 6.1.0.10)";
        MinimumSize = new Size(1000, 640);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);

        BuildUi();
    }

    // ------------------------------------------------------------------ интерфейс

    private void BuildUi()
    {
        // верхняя панель выбора пакета
        var top = new Panel { Dock = DockStyle.Top, Height = 68, Padding = new Padding(10, 10, 10, 6) };

        var pathLabel = new Label { Text = "Папка пакета или файл &mapping.json:", AutoSize = true, Location = new Point(10, 12) };
        _pathBox.Location = new Point(220, 8);
        _pathBox.Width = 560;
        _pathBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _pathBox.PlaceholderText = "например, C:\\Отчётность\\УОД_2026-04-30";

        _browseButton.Text = "Обзор…";
        _browseButton.Location = new Point(790, 6);
        _browseButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _browseButton.Width = 90;
        _browseButton.Click += BrowseClick;

        _validateButton.Text = "Проверить";
        _validateButton.Location = new Point(890, 6);
        _validateButton.Width = 110;
        _validateButton.Height = 28;
        _validateButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _validateButton.BackColor = Color.FromArgb(0x1F, 0x4E, 0x79);
        _validateButton.ForeColor = Color.White;
        _validateButton.FlatStyle = FlatStyle.Flat;
        _validateButton.Click += async (_, _) => await RunValidationAsync();

        _exportButton.Text = "Экспорт в Excel…";
        _exportButton.Location = new Point(890, 38);
        _exportButton.Width = 110;
        _exportButton.Height = 26;
        _exportButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _exportButton.Enabled = false;
        _exportButton.Click += ExportClick;

        _schemaCheck.Text = "Проверять по официальным JSON-схемам ЦБ";
        _schemaCheck.Location = new Point(220, 38);
        _schemaCheck.AutoSize = true;
        _schemaCheck.Checked = true;

        top.Controls.AddRange(new Control[] { pathLabel, _pathBox, _browseButton, _validateButton, _exportButton, _schemaCheck });
        top.ResumeLayout(false);

        // панель сводки
        var summary = new Panel { Dock = DockStyle.Top, Height = 56, Padding = new Padding(10, 6, 10, 6), BackColor = Color.FromArgb(0xF2, 0xF6, 0xFA) };
        _summaryLabel.Text = "Укажите пакет и нажмите «Проверить».";
        _summaryLabel.Dock = DockStyle.Fill;
        _summaryLabel.TextAlign = ContentAlignment.MiddleLeft;
        _summaryLabel.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        summary.Controls.Add(_summaryLabel);

        // панель фильтров
        var filters = new Panel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(10, 4, 10, 4) };
        var sevLabel = new Label { Text = "Критичность:", AutoSize = true, Location = new Point(10, 8) };
        _severityFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        _severityFilter.Width = 160;
        _severityFilter.Location = new Point(95, 5);
        _severityFilter.Items.AddRange(new object[] { "Все", "ОШИБКА", "ПРЕДУПРЕЖДЕНИЕ", "ИНФО" });
        _severityFilter.SelectedIndex = 0;
        _severityFilter.SelectedIndexChanged += (_, _) => ApplyFilter();

        var layerLabel = new Label { Text = "Слой:", AutoSize = true, Location = new Point(270, 8) };
        _layerFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        _layerFilter.Width = 220;
        _layerFilter.Location = new Point(315, 5);
        _layerFilter.SelectedIndexChanged += (_, _) => ApplyFilter();

        filters.Controls.AddRange(new Control[] { sevLabel, _severityFilter, layerLabel, _layerFilter });

        // таблица замечаний
        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.ReadOnly = true;
        _grid.RowHeadersVisible = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.BackgroundColor = Color.White;
        _grid.BorderStyle = BorderStyle.None;
        _grid.CellDoubleClick += GridCellDoubleClick;

        // статус
        _statusLabel.Text = "Готово";
        _statusLabel.Spring = true;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _progress.Width = 200;
        _progress.Visible = false;
        _status.Items.Add(_statusLabel);
        _status.Items.Add(_progress);

        Controls.Add(_grid);
        Controls.Add(filters);
        Controls.Add(summary);
        Controls.Add(top);
        Controls.Add(_status);
        ResumeLayout(false);
    }

    // ------------------------------------------------------------------ обработчики

    private void BrowseClick(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Выберите mapping.json пакета",
            Filter = "Служебный файл mapping.json (mapping*.json)|mapping*.json|Все файлы (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _pathBox.Text = dialog.FileName;
            return;
        }
    }

    private async Task RunValidationAsync()
    {
        if (_running) return;
        var path = _pathBox.Text.Trim();
        if (path.Length == 0)
        {
            MessageBox.Show(this, "Укажите путь к папке пакета или файлу mapping.json.", "Валидатор ПУРЦБ",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!Directory.Exists(path) && !File.Exists(path))
        {
            MessageBox.Show(this, "Указанный путь не существует.", "Валидатор ПУРЦБ",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _running = true;
        _cts = new CancellationTokenSource();
        _validateButton.Enabled = false;
        _exportButton.Enabled = false;
        _progress.Visible = true;
        _progress.Style = ProgressBarStyle.Marquee;
        SetStatus("Выполняется проверка…");
        _grid.Rows.Clear();
        _summaryLabel.Text = "Выполняется проверка…";

        var options = new ValidationOptions
        {
            Path = path,
            UseCbrSchema = _schemaCheck.Checked,
        };

        var sw = Stopwatch.StartNew();
        ValidationReport report;
        try
        {
            var runner = new ValidatorRunner();
            var progress = new Progress<string>(msg => SetStatus(msg));
            report = await Task.Run(() => runner.Run(options, msg => ((IProgress<string>)progress).Report(msg)), _cts.Token);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Проверка отменена");
            _progress.Visible = false;
            _validateButton.Enabled = true;
            _running = false;
            return;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Ошибка выполнения проверки:\n" + ex.Message, "Валидатор ПУРЦБ",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus("Ошибка");
            _progress.Visible = false;
            _validateButton.Enabled = true;
            _running = false;
            return;
        }
        sw.Stop();

        _report = report;
        BindReport(sw.Elapsed);
        _progress.Visible = false;
        _validateButton.Enabled = true;
        _exportButton.Enabled = report.Issues.Count > 0 || report.Package.Tables.Count > 0;
        _running = false;
        SetStatus($"Проверка завершена за {sw.Elapsed.TotalSeconds:0.0} с");
    }

    private void BindReport(TimeSpan elapsed)
    {
        var r = _report!;
        var verdict = r.Passed ? "ПАКЕТ ПРОШЁЛ ПРОВЕРКУ" : "ПАКЕТ НЕ ПРОШЁЛ ПРОВЕРКУ";
        var color = r.Passed ? Color.FromArgb(0x1E, 0x7B, 0x34) : Color.FromArgb(0xB3, 0x1B, 0x1B);
        _summaryLabel.Text = $"{verdict}   •   Ошибок: {r.ErrorCount}   •   Предупреждений: {r.WarningCount}   •   Инфо: {r.InfoCount}   •   Время: {elapsed.TotalSeconds:0.0} с";
        _summaryLabel.ForeColor = color;

        // фильтры по слоям
        var layers = r.Issues.Select(i => i.Layer).Distinct().OrderBy(x => x).ToList();
        _layerFilter.Items.Clear();
        _layerFilter.Items.Add("Все");
        foreach (var l in layers) _layerFilter.Items.Add(l);
        _layerFilter.SelectedIndex = 0;
        _severityFilter.SelectedIndex = 0;

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (_report is null) return;
        _grid.SuspendLayout();
        _grid.Rows.Clear();

        var sev = _severityFilter.SelectedItem?.ToString() ?? "Все";
        var layer = _layerFilter.SelectedItem?.ToString() ?? "Все";

        foreach (var i in _report.Issues)
        {
            if (sev != "Все" && SevName(i.Severity) != sev) continue;
            if (layer != "Все" && i.Layer != layer) continue;
            var idx = _grid.Rows.Add(
                SevName(i.Severity), i.Layer, i.Code, Path.GetFileName(i.File ?? ""),
                i.Table ?? "", i.Row?.ToString() ?? "", i.Column ?? "",
                i.ColumnLabel ?? "", i.Value ?? "", i.Message);
            var row = _grid.Rows[idx];
            switch (i.Severity)
            {
                case IssueSeverity.Error:
                    row.DefaultCellStyle.ForeColor = Color.FromArgb(0xB3, 0x1B, 0x1B);
                    break;
                case IssueSeverity.Warning:
                    row.DefaultCellStyle.ForeColor = Color.FromArgb(0x9C, 0x65, 0x00);
                    break;
                default:
                    row.DefaultCellStyle.ForeColor = Color.FromArgb(0x1F, 0x4E, 0x79);
                    break;
            }
        }

        if (_grid.Columns.Count == 10)
        {
            _grid.Columns[0].Width = 110;
            _grid.Columns[1].Width = 150;
            _grid.Columns[2].Width = 210;
            _grid.Columns[3].Width = 120;
            _grid.Columns[4].Width = 90;
            _grid.Columns[5].Width = 50;
            _grid.Columns[6].Width = 240;
            _grid.Columns[7].Width = 220;
            _grid.Columns[8].Width = 140;
            _grid.Columns[9].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            _grid.Columns[0].HeaderText = "Критичность";
            _grid.Columns[1].HeaderText = "Слой";
            _grid.Columns[2].HeaderText = "Код";
            _grid.Columns[3].HeaderText = "Файл";
            _grid.Columns[4].HeaderText = "Таблица";
            _grid.Columns[5].HeaderText = "Строка";
            _grid.Columns[6].HeaderText = "Колонка";
            _grid.Columns[7].HeaderText = "Показатель";
            _grid.Columns[8].HeaderText = "Значение";
            _grid.Columns[9].HeaderText = "Сообщение";
        }
        _grid.ResumeLayout();
    }

    private void GridCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        var row = _grid.Rows[e.RowIndex];
        var text = new StringBuilder();
        text.AppendLine("Критичность: " + row.Cells[0].Value);
        text.AppendLine("Слой: " + row.Cells[1].Value);
        text.AppendLine("Код: " + row.Cells[2].Value);
        text.AppendLine("Файл: " + row.Cells[3].Value);
        text.AppendLine("Таблица: " + row.Cells[4].Value);
        text.AppendLine("Строка: " + row.Cells[5].Value);
        text.AppendLine("Колонка: " + row.Cells[6].Value);
        text.AppendLine("Показатель: " + row.Cells[7].Value);
        text.AppendLine("Значение: " + row.Cells[8].Value);
        text.AppendLine();
        text.AppendLine("Сообщение: " + row.Cells[9].Value);
        MessageBox.Show(this, text.ToString(), "Детали замечания", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ExportClick(object? sender, EventArgs e)
    {
        if (_report is null) return;
        using var dialog = new SaveFileDialog
        {
            Title = "Экспорт отчёта в Excel",
            Filter = "Книга Excel (*.xlsx)|*.xlsx",
            FileName = $"Отчёт валидации ПУРЦБ {DateTime.Now:yyyy-MM-dd HH-mm}.xlsx",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var writer = new ExcelReportWriter();
            writer.Write(_report, dialog.FileName);
            SetStatus("Отчёт сохранён: " + dialog.FileName);
            if (MessageBox.Show(this, "Отчёт сохранён.\nОткрыть файл?", "Валидатор ПУРЦБ",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Не удалось сохранить отчёт:\n" + ex.Message, "Валидатор ПУРЦБ",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SetStatus(string text) => _statusLabel.Text = text;

    private static string SevName(IssueSeverity s) => s switch
    {
        IssueSeverity.Error => "ОШИБКА",
        IssueSeverity.Warning => "ПРЕДУПРЕЖДЕНИЕ",
        _ => "ИНФО",
    };

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _cts?.Cancel();
        base.OnFormClosed(e);
    }
}
