using System.Globalization;
using System.Text;
using TestEngineer.Core.Abstractions;
using TestEngineer.Core.Gnss;
using TestEngineer.Infrastructure.Gnss;

static string Pct(int n, int d) =>
    d <= 0 ? "0.00%" : (100.0 * n / d).ToString("0.00", CultureInfo.InvariantCulture) + "%";

var root = args.Length > 0
    ? args[0]
    : @"C:\Users\Tuly\Desktop\工作文件\工作工具\Nmea2Kmz";
var outDir = args.Length > 1
    ? args[1]
    : Path.Combine("E:", "CCode", "TestEngineer", "docs", "selftest", "nmea-result-20261112");
Directory.CreateDirectory(outDir);
var kmlDir = Path.Combine(outDir, "kml");
Directory.CreateDirectory(kmlDir);

var analyzer = new NmeaLogAnalyzer();
var result = await analyzer.ScanAsync([root], new NmeaScanOptions { IncludeSubdirectories = true });
var resultLog = await analyzer.ScanAsync(
    [root],
    new NmeaScanOptions { IncludeSubdirectories = true, ExtraExtensions = [".log", ".nmea"] });

var agg = result.Aggregate.Counts;
var valid = result.Files.Where(f => f.IsIncludedInAggregate).ToList();
var skip = result.Files.Where(f => !f.IsIncludedInAggregate).ToList();
var anom = valid.Where(f => f.Verdict == NmeaFileVerdict.ValidWithAnomalies).ToList();
var fixRate = agg.TotalGga == 0 ? 0 : 100.0 * (agg.TotalGga - agg.Invalid) / agg.TotalGga;

var md = new StringBuilder();
md.AppendLine("# NMEA 分析输出 — Nmea2Kmz 真实数据");
md.AppendLine();
md.AppendLine($"- 源目录: `{root}`");
md.AppendLine($"- 时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
md.AppendLine("- 规则: 默认 `.txt`；非法标准行剔除计入异常；厂商私有行不算非法；KML 输出到本报告目录，**未写入采集目录**");
md.AppendLine();
md.AppendLine("## 汇总（仅 .txt）");
md.AppendLine();
md.AppendLine("| 项 | 值 |");
md.AppendLine("| --- | --- |");
md.AppendLine($"| 扫描文件 | {result.Files.Count} |");
md.AppendLine($"| 计入统计 | {valid.Count} |");
md.AppendLine($"| 跳过 | {skip.Count} |");
md.AppendLine($"| 含异常行 | {anom.Count} |");
md.AppendLine($"| 合法 GGA 合计 | {agg.TotalGga} |");
md.AppendLine($"| 固定解 | {agg.Fixed} ({Pct(agg.Fixed, agg.TotalGga)}) |");
md.AppendLine($"| 浮点解 | {agg.Float} ({Pct(agg.Float, agg.TotalGga)}) |");
md.AppendLine($"| 差分 | {agg.Dgps} ({Pct(agg.Dgps, agg.TotalGga)}) |");
md.AppendLine($"| 单点 | {agg.Single} ({Pct(agg.Single, agg.TotalGga)}) |");
md.AppendLine($"| 未定位 | {agg.Invalid} ({Pct(agg.Invalid, agg.TotalGga)}) |");
md.AppendLine($"| 其他 | {agg.Other} ({Pct(agg.Other, agg.TotalGga)}) |");
md.AppendLine($"| 定位率 | {fixRate.ToString("0.00", CultureInfo.InvariantCulture)}% |");
md.AppendLine();
var a2 = resultLog.Aggregate.Counts;
md.AppendLine("### 含 .log / .nmea");
md.AppendLine($"- 扫描文件: {resultLog.Files.Count}；合法 GGA: {a2.TotalGga}；固定 {a2.Fixed} / 浮点 {a2.Float} / 差分 {a2.Dgps} / 单点 {a2.Single} / 未定位 {a2.Invalid}");
md.AppendLine();
md.AppendLine("## 分文件明细（.txt）");
md.AppendLine();
md.AppendLine("| 相对路径 | 判定 | GGA | 固定% | 浮点% | 差分% | 单点% | 未定位% | 异常行 |");
md.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
foreach (var f in result.Files.OrderBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase))
{
    var c = f.Counts;
    var t = c.TotalGga;
    if (f.IsIncludedInAggregate)
    {
        md.AppendLine($"| `{f.RelativePath}` | {f.Verdict} | {t} | {Pct(c.Fixed, t)} | {Pct(c.Float, t)} | {Pct(c.Dgps, t)} | {Pct(c.Single, t)} | {Pct(c.Invalid, t)} | {c.AnomalyLines} |");
    }
    else
    {
        var reason = string.IsNullOrWhiteSpace(f.ErrorMessage) ? f.Verdict.ToString() : f.ErrorMessage;
        md.AppendLine($"| `{f.RelativePath}` | 跳过:{reason} | — | — | — | — | — | — | {c.AnomalyLines} |");
    }
}

var csvPath = Path.Combine(outDir, "nmea-summary.csv");
var csv = new StringBuilder();
csv.Append('\uFEFF');
csv.AppendLine("RelativePath,Verdict,TotalGga,Fixed,Float,Dgps,Single,Invalid,Other,AnomalyLines,PrivateLines");
foreach (var f in result.Files.OrderBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase))
{
    var c = f.Counts;
    csv.AppendLine(string.Join(',',
        Escape(f.RelativePath), f.Verdict, c.TotalGga, c.Fixed, c.Float, c.Dgps, c.Single, c.Invalid, c.Other, c.AnomalyLines, c.PrivateLines));
}
await File.WriteAllTextAsync(csvPath, csv.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
var tracks = valid.Select(f =>
{
    var color = KmlColorPalette.RandomUnused(used);
    used.Add(color);
    return new KmlTrackRequest
    {
        SourcePath = f.Path,
        DisplayName = f.RelativePath,
        KmlColorAabbggrr = color,
    };
}).ToList();

var writer = new KmlTrackWriter();
var written = await writer.WriteAsync(tracks, new KmlWriteOptions
{
    OutputDirectory = kmlDir,
    Kmz = false,
    AbsoluteAltitude = false,
    IncludePointPlacemarks = false,
});

md.AppendLine();
md.AppendLine("## KML 输出");
md.AppendLine();
md.AppendLine($"- 目录: `{kmlDir}`");
md.AppendLine($"- 生成文件数: {written.Count}（随机不重复颜色）");
md.AppendLine();
md.AppendLine("## 跳过文件");
md.AppendLine();
if (skip.Count == 0)
{
    md.AppendLine("（无）");
}
else
{
    foreach (var f in skip)
    {
        md.AppendLine($"- `{f.RelativePath}` — {f.Verdict}" + (string.IsNullOrWhiteSpace(f.ErrorMessage) ? "" : $": {f.ErrorMessage}"));
    }
}

var reportPath = Path.Combine(outDir, "REPORT.md");
await File.WriteAllTextAsync(reportPath, md.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
Console.WriteLine($"REPORT={reportPath}");
Console.WriteLine($"CSV={csvPath}");
Console.WriteLine($"KML={kmlDir} count={written.Count}");
Console.WriteLine($"SUMMARY files={result.Files.Count} valid={valid.Count} gga={agg.TotalGga} fixed={agg.Fixed} float={agg.Float} dgps={agg.Dgps} single={agg.Single} invalid={agg.Invalid}");

static string Escape(string s) =>
    s.Contains(',') || s.Contains('"') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
