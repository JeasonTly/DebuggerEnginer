using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TestEngineer.Core.Abstractions;
using TestEngineer.Core.Gnss;
using TestEngineer.Desktop.Gnss;
using TestEngineer.Desktop.Views;

namespace TestEngineer.Desktop.ViewModels;

public sealed partial class NmeaPageViewModel : ObservableObject
{
    private readonly INmeaLogAnalyzer _analyzer;
    private readonly IKmlTrackWriter _kmlWriter;
    private readonly List<string> _roots = [];
    private readonly List<NmeaFileRowItem> _allRows = [];
    private CancellationTokenSource? _scanCts;

    public NmeaPageViewModel(INmeaLogAnalyzer analyzer, IKmlTrackWriter kmlWriter)
    {
        _analyzer = analyzer;
        _kmlWriter = kmlWriter;
    }

    public string PageTitle => "定位日志";

    public string PageSubtitle => "拖入或选择含 NMEA 日志的目录";

    public string EmptyHint => "将日志目录或文件拖到此处，或点击下方按钮选择";

    public ObservableCollection<NmeaFileRowItem> FilteredRows { get; } = [];

    public ObservableCollection<NmeaShareSegment> SummaryLegend { get; } = [];

    public ObservableCollection<NmeaAnomalySample> SelectedAnomalies { get; } = [];

    [ObservableProperty] private string? _selectedPath;

    [ObservableProperty] private string _status = "等待选择目录";

    [ObservableProperty] private bool _includeSubdirectories = true;

    [ObservableProperty] private bool _includeLogAndNmea;

    [ObservableProperty] private bool _isScanning;

    [ObservableProperty] private double _scanProgress;

    [ObservableProperty] private string _scanProgressText = string.Empty;

    [ObservableProperty] private bool _hasResults;

    [ObservableProperty] private string _summaryGgaText = "GGA —";

    [ObservableProperty] private string _summaryFixRateText = "定位率 —";

    [ObservableProperty] private string _summaryFilesText = string.Empty;

    [ObservableProperty] private GridLength _summaryFixedStar = new(0);

    [ObservableProperty] private GridLength _summaryFloatStar = new(0);

    [ObservableProperty] private GridLength _summaryDgpsStar = new(0);

    [ObservableProperty] private GridLength _summarySingleStar = new(0);

    [ObservableProperty] private GridLength _summaryInvalidStar = new(0);

    [ObservableProperty] private GridLength _summaryOtherStar = new(0);

    [ObservableProperty] private bool _showDgpsColumn;

    [ObservableProperty] private NmeaFileFilterKind _filterKind = NmeaFileFilterKind.All;

    [ObservableProperty] private string _nameFilter = string.Empty;

    [ObservableProperty] private NmeaResultViewMode _viewMode = NmeaResultViewMode.Table;

    [ObservableProperty] private NmeaFileRowItem? _selectedRow;

    [ObservableProperty] private string _detailTimeText = "起止时间 —";

    [ObservableProperty] private bool _exportKmz;

    [ObservableProperty] private bool _exportAbsoluteAltitude;

    [ObservableProperty] private bool _exportIncludePoints = true;

    [ObservableProperty] private string? _exportOutputDirectory;

    [ObservableProperty] private bool _isExporting;

    public bool IsTableView => ViewMode == NmeaResultViewMode.Table;

    public bool IsChartView => ViewMode == NmeaResultViewMode.Chart;

    public bool CanCancelScan => IsScanning;

    public bool CanExport => HasResults && !IsScanning && !IsExporting && _allRows.Any(r => r.IsValid);

    partial void OnViewModeChanged(NmeaResultViewMode value)
    {
        OnPropertyChanged(nameof(IsTableView));
        OnPropertyChanged(nameof(IsChartView));
    }

    partial void OnIsScanningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCancelScan));
        CancelScanCommand.NotifyCanExecuteChanged();
        ExportCsvCommand.NotifyCanExecuteChanged();
        ExportKmlCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanExport));
    }

    partial void OnIsExportingChanged(bool value)
    {
        ExportCsvCommand.NotifyCanExecuteChanged();
        ExportKmlCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanExport));
    }

    partial void OnHasResultsChanged(bool value)
    {
        ExportCsvCommand.NotifyCanExecuteChanged();
        ExportKmlCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanExport));
    }

    partial void OnFilterKindChanged(NmeaFileFilterKind value) => ApplyFilter();

    partial void OnNameFilterChanged(string value) => ApplyFilter();

    partial void OnSelectedRowChanged(NmeaFileRowItem? value) => UpdateDetail(value);

    partial void OnIncludeSubdirectoriesChanged(bool value)
    {
        if (_roots.Count > 0 && !IsScanning)
        {
            _ = ScanAsync();
        }
    }

    partial void OnIncludeLogAndNmeaChanged(bool value)
    {
        if (_roots.Count > 0 && !IsScanning)
        {
            _ = ScanAsync();
        }
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        var dlg = new OpenFolderDialog { Title = "选择定位日志目录" };
        if (!string.IsNullOrWhiteSpace(SelectedPath) && Directory.Exists(SelectedPath))
        {
            dlg.InitialDirectory = SelectedPath;
        }

        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.FolderName))
        {
            return;
        }

        SetRoots([dlg.FolderName]);
        _ = ScanAsync();
    }

    [RelayCommand]
    private void Clear()
    {
        CancelScan();
        _roots.Clear();
        SelectedPath = null;
        ClearResults();
        Status = "等待选择目录";
    }

    [RelayCommand(CanExecute = nameof(CanCancelScan))]
    private void CancelScan()
    {
        try
        {
            _scanCts?.Cancel();
        }
        catch
        {
            // ignore
        }
    }

    [RelayCommand]
    private void SetFilterAll() => FilterKind = NmeaFileFilterKind.All;

    [RelayCommand]
    private void SetFilterValid() => FilterKind = NmeaFileFilterKind.Valid;

    [RelayCommand]
    private void SetFilterSkipped() => FilterKind = NmeaFileFilterKind.Skipped;

    [RelayCommand]
    private void SetFilterAnomalies() => FilterKind = NmeaFileFilterKind.Anomalies;

    [RelayCommand]
    private void ShowTable() => ViewMode = NmeaResultViewMode.Table;

    [RelayCommand]
    private void ShowChart() => ViewMode = NmeaResultViewMode.Chart;

    [RelayCommand]
    private void BrowseExportDirectory()
    {
        var dlg = new OpenFolderDialog { Title = "选择 KML/KMZ 输出目录（可留空旁路）" };
        if (!string.IsNullOrWhiteSpace(ExportOutputDirectory) && Directory.Exists(ExportOutputDirectory))
        {
            dlg.InitialDirectory = ExportOutputDirectory;
        }

        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FolderName))
        {
            ExportOutputDirectory = dlg.FolderName;
        }
    }

    [RelayCommand]
    private void ClearExportDirectory() => ExportOutputDirectory = null;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportCsvAsync()
    {
        var dlg = new SaveFileDialog
        {
            Title = "导出 CSV",
            Filter = "CSV (*.csv)|*.csv",
            FileName = "nmea-summary.csv",
            AddExtension = true,
            DefaultExt = ".csv",
        };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.FileName))
        {
            return;
        }

        try
        {
            IsExporting = true;
            await Task.Run(() => WriteCsv(dlg.FileName, _allRows));
            Status = $"已导出 CSV：{dlg.FileName}";
        }
        catch (Exception ex)
        {
            Status = $"导出 CSV 失败：{ex.Message}";
            MessageBox.Show(ex.Message, "导出 CSV", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsExporting = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportKmlAsync()
    {
        var valid = _allRows.Where(r => r.IsValid).ToList();
        if (valid.Count == 0)
        {
            Status = "没有可导出的有效文件";
            return;
        }

        // 单文件也打开对话框（预填默认色）；≥2 必须弹窗。
        var colors = PromptTrackColors(valid);
        if (colors is null)
        {
            Status = "已取消导出 KML/KMZ";
            return;
        }

        var tracks = valid.Select(r => new KmlTrackRequest
        {
            SourcePath = r.FullPath,
            DisplayName = r.DisplayName,
            KmlColorAabbggrr = colors.TryGetValue(r.FullPath, out var c)
                ? c
                : KmlColorUiPalette.DefaultKmlColor,
        }).ToList();

        var options = new KmlWriteOptions
        {
            OutputDirectory = string.IsNullOrWhiteSpace(ExportOutputDirectory) ? null : ExportOutputDirectory,
            Kmz = ExportKmz,
            AbsoluteAltitude = ExportAbsoluteAltitude,
            IncludePointPlacemarks = ExportIncludePoints,
        };

        try
        {
            IsExporting = true;
            Status = $"正在导出 {(ExportKmz ? "KMZ" : "KML")}…";
            var written = await _kmlWriter.WriteAsync(tracks, options);
            var where = options.OutputDirectory ?? "各源文件旁";
            Status = $"已导出 {(ExportKmz ? "KMZ" : "KML")} {written.Count}/{tracks.Count} → {where}";
        }
        catch (OperationCanceledException)
        {
            Status = "导出已取消";
        }
        catch (Exception ex)
        {
            Status = $"导出失败：{ex.Message}";
            MessageBox.Show(ex.Message, "导出 KML/KMZ", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsExporting = false;
        }
    }

    /// <summary>拖放多路径入口（目录或文件）。</summary>
    public void ApplyDroppedPaths(IEnumerable<string> paths)
    {
        var list = paths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Where(p => Directory.Exists(p) || File.Exists(p))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (list.Count == 0)
        {
            return;
        }

        SetRoots(list);
        _ = ScanAsync();
    }

    /// <summary>兼容旧单路径调用。</summary>
    public void ApplyDroppedPath(string path) => ApplyDroppedPaths([path]);

    private void SetRoots(IReadOnlyList<string> roots)
    {
        _roots.Clear();
        _roots.AddRange(roots);
        SelectedPath = roots.Count == 1 ? roots[0] : $"{roots.Count} 个路径";
    }

    private async Task ScanAsync()
    {
        if (_roots.Count == 0)
        {
            return;
        }

        CancelScan();
        _scanCts = new CancellationTokenSource();
        var ct = _scanCts.Token;

        var options = new NmeaScanOptions
        {
            IncludeSubdirectories = IncludeSubdirectories,
            ExtraExtensions = IncludeLogAndNmea ? [".log", ".nmea"] : [],
        };

        IsScanning = true;
        ScanProgress = 0;
        ScanProgressText = "正在扫描…";
        Status = "正在分析定位日志…";
        ClearResults(keepStatus: true);

        var progress = new Progress<NmeaScanProgress>(p =>
        {
            if (p.FilesDiscovered > 0)
            {
                ScanProgress = 100.0 * p.FilesProcessed / p.FilesDiscovered;
            }

            ScanProgressText = p.FilesDiscovered > 0
                ? $"{p.FilesProcessed}/{p.FilesDiscovered}"
                : "发现文件中…";
            if (!string.IsNullOrWhiteSpace(p.CurrentPath))
            {
                Status = $"分析中：{Path.GetFileName(p.CurrentPath)}";
            }
        });

        try
        {
            var result = await _analyzer.ScanAsync(_roots, options, progress, ct);
            ApplyScanResult(result);
            Status = $"完成：有效 {result.Aggregate.ValidFileCount}/{result.Aggregate.TotalFiles}，GGA {result.Aggregate.Counts.TotalGga}";
        }
        catch (OperationCanceledException)
        {
            Status = "已取消扫描";
            ClearResults(keepStatus: true);
        }
        catch (Exception ex)
        {
            Status = $"扫描失败：{ex.Message}";
            MessageBox.Show(ex.Message, "定位日志", MessageBoxButton.OK, MessageBoxImage.Error);
            ClearResults(keepStatus: true);
        }
        finally
        {
            IsScanning = false;
            ScanProgressText = string.Empty;
            CancelScanCommand.NotifyCanExecuteChanged();
        }
    }

    private void ApplyScanResult(NmeaScanResult result)
    {
        _allRows.Clear();
        foreach (var file in result.Files.OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            _allRows.Add(new NmeaFileRowItem(file));
        }

        ShowDgpsColumn = _allRows.Any(r => r.HasDgps) || result.Aggregate.Counts.Dgps > 0;
        UpdateSummary(result.Aggregate);
        HasResults = _allRows.Count > 0;
        ApplyFilter();
    }

    private void UpdateSummary(NmeaStatsAggregate aggregate)
    {
        SummaryLegend.Clear();
        var total = aggregate.Counts.TotalGga;
        SummaryGgaText = $"GGA {total}";
        var fixRate = total <= 0 ? 0 : 100.0 * (total - aggregate.Counts.Invalid) / total;
        SummaryFixRateText = total <= 0 ? "定位率 —" : $"定位率 {fixRate:0.0}%";
        SummaryFilesText = $"有效文件 {aggregate.ValidFileCount} / 共 {aggregate.TotalFiles}";

        SummaryFixedStar = StarOrZero(aggregate.Counts.Fixed, total);
        SummaryFloatStar = StarOrZero(aggregate.Counts.Float, total);
        SummaryDgpsStar = StarOrZero(aggregate.Counts.Dgps, total);
        SummarySingleStar = StarOrZero(aggregate.Counts.Single, total);
        SummaryInvalidStar = StarOrZero(aggregate.Counts.Invalid, total);
        SummaryOtherStar = StarOrZero(aggregate.Counts.Other, total);

        void Legend(string label, int count, string brushKey)
        {
            var pct = total <= 0 ? 0 : 100.0 * count / total;
            SummaryLegend.Add(new NmeaShareSegment
            {
                Label = label,
                PercentText = $"{label} {pct:0.0}%",
                Star = StarOrZero(count, total),
                Fill = ResolveBrush(brushKey),
            });
        }

        Legend("固定", aggregate.Counts.Fixed, "Brush.Success");
        Legend("浮点", aggregate.Counts.Float, "Brush.Accent");
        Legend("差分", aggregate.Counts.Dgps, "Brush.Warning");
        Legend("单点", aggregate.Counts.Single, "Brush.Warning");
        Legend("未定位", aggregate.Counts.Invalid, "Brush.Danger");
        Legend("其他", aggregate.Counts.Other, "Brush.TextSecondary");
    }

    private static GridLength StarOrZero(int count, int total) =>
        total > 0 && count > 0 ? new GridLength(count, GridUnitType.Star) : new GridLength(0);

    private static System.Windows.Media.Brush ResolveBrush(string key)
    {
        if (Application.Current?.TryFindResource(key) is System.Windows.Media.Brush brush)
        {
            return brush;
        }

        return System.Windows.Media.Brushes.Gray;
    }

    private void ApplyFilter()
    {
        FilteredRows.Clear();
        IEnumerable<NmeaFileRowItem> q = _allRows;
        q = FilterKind switch
        {
            NmeaFileFilterKind.Valid => q.Where(r => r.IsValid),
            NmeaFileFilterKind.Skipped => q.Where(r => r.IsSkipped),
            NmeaFileFilterKind.Anomalies => q.Where(r => r.HasAnomalies),
            _ => q,
        };

        if (!string.IsNullOrWhiteSpace(NameFilter))
        {
            var key = NameFilter.Trim();
            q = q.Where(r =>
                r.RelativePath.Contains(key, StringComparison.OrdinalIgnoreCase)
                || r.FullPath.Contains(key, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var row in q)
        {
            FilteredRows.Add(row);
        }

        if (SelectedRow is not null && !FilteredRows.Contains(SelectedRow))
        {
            SelectedRow = FilteredRows.FirstOrDefault();
        }
        else if (SelectedRow is null)
        {
            SelectedRow = FilteredRows.FirstOrDefault();
        }
    }

    private void UpdateDetail(NmeaFileRowItem? row)
    {
        SelectedAnomalies.Clear();
        if (row is null)
        {
            DetailTimeText = "起止时间 —";
            return;
        }

        DetailTimeText = $"起止时间 {row.TimeRangeText}";
        foreach (var sample in row.AnomalySamples)
        {
            SelectedAnomalies.Add(sample);
        }
    }

    private void ClearResults(bool keepStatus = false)
    {
        _allRows.Clear();
        FilteredRows.Clear();
        SummaryLegend.Clear();
        SelectedAnomalies.Clear();
        SelectedRow = null;
        HasResults = false;
        ShowDgpsColumn = false;
        SummaryGgaText = "GGA —";
        SummaryFixRateText = "定位率 —";
        SummaryFilesText = string.Empty;
        SummaryFixedStar = new GridLength(0);
        SummaryFloatStar = new GridLength(0);
        SummaryDgpsStar = new GridLength(0);
        SummarySingleStar = new GridLength(0);
        SummaryInvalidStar = new GridLength(0);
        SummaryOtherStar = new GridLength(0);
        DetailTimeText = "起止时间 —";
        if (!keepStatus)
        {
            Status = "等待选择目录";
        }
    }

    private static IReadOnlyDictionary<string, string>? PromptTrackColors(IReadOnlyList<NmeaFileRowItem> valid)
    {
        var files = valid.Select(r => (r.FullPath, r.DisplayName));
        var vm = new NmeaColorPickerViewModel(files);
        var window = new NmeaColorPickerWindow(vm)
        {
            Owner = Application.Current.MainWindow,
        };
        return window.ShowDialog() == true ? vm.BuildResult() : null;
    }

    private static void WriteCsv(string path, IReadOnlyList<NmeaFileRowItem> rows)
    {
        var showDgps = rows.Any(r => r.HasDgps);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var headers = new List<string>
        {
            "相对路径", "状态", "GGA", "固定%", "浮点%",
        };
        if (showDgps)
        {
            headers.Add("差分%");
        }

        headers.AddRange(["单点%", "未定位%", "其他%", "异常行数", "完整路径"]);
        writer.WriteLine(string.Join(",", headers.Select(CsvEscape)));

        foreach (var row in rows)
        {
            var cells = new List<string>
            {
                row.RelativePath,
                row.StatusText,
                row.GgaCount.ToString(CultureInfo.InvariantCulture),
                row.FixedPercent,
                row.FloatPercent,
            };
            if (showDgps)
            {
                cells.Add(row.DgpsPercent);
            }

            cells.Add(row.SinglePercent);
            cells.Add(row.InvalidPercent);
            cells.Add(row.OtherPercent);
            cells.Add(row.AnomalyCount.ToString(CultureInfo.InvariantCulture));
            cells.Add(row.FullPath);
            writer.WriteLine(string.Join(",", cells.Select(CsvEscape)));
        }
    }

    private static string CsvEscape(string? value)
    {
        value ??= string.Empty;
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }
}
