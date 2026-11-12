using System.Text.Json;
using System.Text.RegularExpressions;
using TestEngineer.Core.Models;

namespace TestEngineer.Adb;

/// <summary>调试命令与控制台快捷指令的完整清单。</summary>
public sealed record DebugCatalog(IReadOnlyList<DebugCommandDefinition> Commands, IReadOnlyList<ConsoleChipDefinition> ConsoleChips);

/// <summary>调试命令清单：优先读取 Commands/debug-commands.json（数据驱动），缺失时使用此处的内置清单。</summary>
public static partial class DebugCommandCatalog
{
    private static readonly DebugCommandParameter PackageParameter = new("package", "包名", null, @"^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z0-9_]+)*$");

    /// <summary>JSON 参数模板里的 <c>{name}</c> 会自动绑到这里。未知名字用安全字符集并在界面上提示。</summary>
    private static readonly Dictionary<string, DebugCommandParameter> KnownParameters = new(StringComparer.Ordinal)
    {
        ["package"] = PackageParameter,
        ["size"] = new("size", "分辨率（宽x高）", "720x1440", @"^\d{3,5}x\d{3,5}$"),
        ["density"] = new("density", "DPI", "320", @"^\d{2,4}$"),
        ["level"] = new("level", "电量（1-100）", "15", @"^(100|[1-9][0-9]?)$"),
        ["proxy"] = new("proxy", "代理 host:port", "127.0.0.1:8888", @"^[A-Za-z0-9._-]+:\d{2,5}$"),
        ["rotation"] = new("rotation", "方向：0 竖屏，1 向左，2 倒置，3 向右", "1", @"^[0-3]$"),
    };

    public static IReadOnlyList<DebugCommandDefinition> Default { get; } =
    [
        new("key.home", "返回桌面", "按键", "发送 HOME 键。", ["shell", "input", "keyevent", "KEYCODE_HOME"]),
        new("key.back", "返回键", "按键", "发送 BACK 键。", ["shell", "input", "keyevent", "KEYCODE_BACK"]),
        new("key.wakeup", "唤醒屏幕", "按键", "发送 WAKEUP 键。", ["shell", "input", "keyevent", "KEYCODE_WAKEUP"]),
        new("screen.size.reset", "重置分辨率", "显示", "恢复物理分辨率。", ["shell", "wm", "size", "reset"]),
        new("screen.density.reset", "重置密度", "显示", "恢复物理 DPI。", ["shell", "wm", "density", "reset"]),
        new(
            "screen.size.set",
            "设置分辨率",
            "显示",
            "临时覆盖分辨率，如 720x1440。测试后请重置。",
            ["shell", "wm", "size", "{size}"],
            [new DebugCommandParameter("size", "分辨率 (宽x高)", "720x1440", @"^\d{3,5}x\d{3,5}$")],
            IsDangerous: true),
        new(
            "prop.get",
            "读取系统属性",
            "信息",
            "getprop <key>。",
            ["shell", "getprop", "{key}"],
            [new DebugCommandParameter("key", "属性名", "ro.build.fingerprint", @"^[A-Za-z0-9._\-]+$")]),
        new("ps.all", "进程列表", "信息", "ps -A。", ["shell", "ps", "-A"]),
        new("dumpsys.activity.top", "前台 Activity", "信息", "dumpsys activity top。", ["shell", "dumpsys", "activity", "top"], Timeout: TimeSpan.FromSeconds(30)),
        new("dumpsys.focus", "当前焦点窗口", "信息", "dumpsys window | grep mCurrentFocus。", ["shell", "dumpsys window | grep -E 'mCurrentFocus|mFocusedApp'"]),
        new("dumpsys.meminfo", "应用内存", "信息", "dumpsys meminfo <package>。", ["shell", "dumpsys", "meminfo", "{package}"], [PackageParameter]),
        new("logcat.clear", "清空 logcat 缓冲", "日志", "logcat -c。", ["logcat", "-c"]),
        new("reboot", "重启设备", "电源", "adb reboot。", ["reboot"], IsDangerous: true),
        new("reboot.recovery", "重启到 Recovery", "电源", "adb reboot recovery。", ["reboot", "recovery"], IsDangerous: true),
        new(
            "oem.mtk-loggerui",
            "打开 MTK Logger",
            "日志管理",
            "com.debug.loggerui 主界面。",
            ["shell", "am", "start", "-n", "com.debug.loggerui/com.debug.loggerui.MainActivity"],
            WhenPackageExists: "com.debug.loggerui"),
        new(
            "oem.sprd-logmanager",
            "打开展讯 Logger",
            "日志管理",
            "com.sprd.logmanager 默认主界面。",
            ["shell", "am", "start", "-n", "com.sprd.logmanager/com.sprd.logmanager.logui.LogMainActivity"],
            WhenPackageExists: "com.sprd.logmanager"),
        new(
            "oem.qcom-logsettings",
            "打开高通 Logger",
            "日志管理",
            "com.android.logsettings 主界面。",
            ["shell", "am", "start", "-n", "com.android.logsettings/com.android.logsettings.MainActivity"],
            WhenPackageExists: "com.android.logsettings"),
        new(
            "oem.engineer-mode",
            "打开 EngineerMode",
            "日志管理",
            "展锐工程模式。",
            ["shell", "am", "start", "-n", "com.sprd.engineermode/.EngineerModeActivity"],
            WhenPackageExists: "com.sprd.engineermode"),
        new(
            "oem.hisense-factory-log",
            "打开 Hisense 工厂日志",
            "日志管理",
            "Hisense 工厂日志上传。",
            ["shell", "monkey", "-p", "com.hmct.factorylogupload", "-c", "android.intent.category.LAUNCHER", "1"],
            WhenPackageExists: "com.hmct.factorylogupload"),
        new("diag.users", "列出用户账户", "诊断查询", "pm list users。", ["shell", "pm", "list", "users"]),
        new(
            "settings.developer",
            "打开开发者选项",
            "设置菜单",
            "android.settings.APPLICATION_DEVELOPMENT_SETTINGS。",
            ["shell", "am", "start", "-a", "android.settings.APPLICATION_DEVELOPMENT_SETTINGS"]),
        new(
            "settings.about",
            "打开关于手机",
            "设置菜单",
            "android.settings.DEVICE_INFO_SETTINGS。",
            ["shell", "am", "start", "-a", "android.settings.DEVICE_INFO_SETTINGS"]),
        new(
            "settings.home",
            "打开系统设置",
            "设置菜单",
            "android.settings.SETTINGS。",
            ["shell", "am", "start", "-a", "android.settings.SETTINGS"]),
        new(
            "settings.display",
            "打开显示设置",
            "设置菜单",
            "android.settings.DISPLAY_SETTINGS。",
            ["shell", "am", "start", "-a", "android.settings.DISPLAY_SETTINGS"]),
        new(
            "settings.locale",
            "打开语言设置",
            "设置菜单",
            "android.settings.LOCALE_SETTINGS。",
            ["shell", "am", "start", "-a", "android.settings.LOCALE_SETTINGS"]),
        new(
            "settings.datetime",
            "打开日期和时间",
            "设置菜单",
            "android.settings.DATE_SETTINGS。",
            ["shell", "am", "start", "-a", "android.settings.DATE_SETTINGS"]),
    ];

    public static IReadOnlyList<ConsoleChipDefinition> DefaultConsoleChips { get; } =
    [
        new("selinux-get", "查看 SELinux", "shell getenforce"),
        new("selinux-off", "关闭 SELinux", "shell setenforce 0", RequiresRoot: true, IsDangerous: true),
        new("selinux-on", "恢复 SELinux", "shell setenforce 1", RequiresRoot: true, IsDangerous: true),
        new("logcat-keyword", "logcat 关键字", Kind: "logcat-filter"),
        new("logcat-error", "logcat *:E", "logcat *:E"),
        new("logcat-clear", "logcat -c", "logcat -c"),
        new("adb-root", "adb root", "root", RequiresUserdebug: true, IsDangerous: true),
        new("adb-remount", "adb remount", "remount", RequiresUserdebug: true, IsDangerous: true),
    ];

    /// <summary>内置清单（无 JSON 文件时使用）。</summary>
    public static DebugCatalog BuiltIn { get; } = new(Default, DefaultConsoleChips);

    /// <summary>JSON 清单相对程序目录的位置。</summary>
    public const string JsonRelativePath = "Commands/debug-commands.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>读取程序目录下的 JSON 清单；文件不存在或损坏时回落内置清单。</summary>
    public static DebugCatalog LoadDefault(string? baseDirectory = null)
    {
        var path = Path.Combine(baseDirectory ?? AppContext.BaseDirectory, JsonRelativePath.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            return File.Exists(path) ? LoadFromJson(File.ReadAllText(path)) : BuiltIn;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return BuiltIn;
        }
    }

    /// <summary>解析 JSON 清单。args 为空且 kind 为 am-force-stop / pm-path / pm-clear 时补全带 {package} 的模板；参数模板中的 {name} 自动绑定。</summary>
    public static DebugCatalog LoadFromJson(string json)
    {
        var file = JsonSerializer.Deserialize<CatalogFile>(json, JsonOptions) ?? throw new JsonException("清单为空。");
        var commands = new List<DebugCommandDefinition>();
        foreach (var item in file.Commands ?? [])
        {
            if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Title))
            {
                continue;
            }

            var args = item.Args ?? [];
            var parameters = new List<DebugCommandParameter>();
            if (item.Parameters is { Count: > 0 })
            {
                foreach (var parameter in item.Parameters)
                {
                    if (string.IsNullOrWhiteSpace(parameter.Name))
                    {
                        continue;
                    }

                    parameters.Add(new DebugCommandParameter(
                        parameter.Name,
                        string.IsNullOrWhiteSpace(parameter.Label) ? parameter.Name : parameter.Label,
                        parameter.Default,
                        string.IsNullOrWhiteSpace(parameter.Pattern) ? DebugCommandParameter.SafeTokenPattern : parameter.Pattern));
                }
            }

            if (args.Count == 0)
            {
                switch (item.Kind)
                {
                    case "am-force-stop":
                        args = ["shell", "am", "force-stop", "{package}"];
                        break;
                    case "pm-path":
                        args = ["shell", "pm", "path", "{package}"];
                        break;
                    case "pm-clear":
                        args = ["shell", "pm", "clear", "{package}"];
                        break;
                }
            }

            BindPlaceholders(args, parameters);

            commands.Add(new DebugCommandDefinition(
                item.Id,
                item.Title,
                item.Category ?? "其他",
                item.Description ?? string.Empty,
                args,
                parameters,
                item.Dangerous,
                item.TimeoutSeconds is > 0 ? TimeSpan.FromSeconds(item.TimeoutSeconds.Value) : null,
                item.Kind,
                item.WhenPackageExists,
                item.Silent));
        }

        var chips = (file.ConsoleChips ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c.Id))
            .Select(c => new ConsoleChipDefinition(c.Id!, c.Title ?? c.Id!, c.Line, c.Kind, c.RequiresRoot, c.RequiresUserdebug, c.Dangerous))
            .ToList();
        return new DebugCatalog(commands, chips);
    }

    private sealed class CatalogFile
    {
        public List<CommandItem>? Commands { get; set; }

        public List<ChipItem>? ConsoleChips { get; set; }
    }

    private sealed class CommandItem
    {
        public string? Id { get; set; }

        public string? Title { get; set; }

        public string? Category { get; set; }

        public string? Description { get; set; }

        public List<string>? Args { get; set; }

        public List<ParameterItem>? Parameters { get; set; }

        public string? Kind { get; set; }

        public string? WhenPackageExists { get; set; }

        public bool Dangerous { get; set; }

        public bool Silent { get; set; }

        public int? TimeoutSeconds { get; set; }
    }

    private sealed class ParameterItem
    {
        public string? Name { get; set; }

        public string? Label { get; set; }

        public string? Default { get; set; }

        public string? Pattern { get; set; }
    }

    private sealed class ChipItem
    {
        public string? Id { get; set; }

        public string? Title { get; set; }

        public string? Line { get; set; }

        public string? Kind { get; set; }

        public bool RequiresRoot { get; set; }

        public bool RequiresUserdebug { get; set; }

        public bool Dangerous { get; set; }
    }

    [GeneratedRegex(@"\{(?<name>[A-Za-z0-9_]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderRegex();

    /// <summary>把参数模板里尚未声明的占位符绑成可提示、可校验的参数。</summary>
    private static void BindPlaceholders(List<string> args, List<DebugCommandParameter> parameters)
    {
        var seen = new HashSet<string>(parameters.Select(p => p.Name), StringComparer.Ordinal);
        foreach (Match match in PlaceholderRegex().Matches(string.Join('\n', args)))
        {
            var name = match.Groups["name"].Value;
            if (!seen.Add(name))
            {
                continue;
            }

            parameters.Add(KnownParameters.TryGetValue(name, out var known)
                ? known
                : new DebugCommandParameter(name, name));
        }
    }

    /// <summary>用参数值展开命令模板；值必须通过参数定义的校验正则，缺省使用默认值。</summary>
    public static IReadOnlyList<string> Expand(DebugCommandDefinition definition, IReadOnlyDictionary<string, string>? values)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var parameter in definition.ParameterList)
        {
            string? value = null;
            if (values is not null && values.TryGetValue(parameter.Name, out var supplied))
            {
                value = supplied;
            }

            value = string.IsNullOrWhiteSpace(value) ? parameter.DefaultValue : value.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException($"缺少参数：{parameter.Label}（{parameter.Name}）。");
            }

            if (!Regex.IsMatch(value, parameter.ValidationPattern, RegexOptions.CultureInvariant))
            {
                throw new ArgumentException($"参数 {parameter.Label} 的值不合法：\"{value}\"。");
            }

            resolved[parameter.Name] = value;
        }

        return
        [
            .. definition.AdbArgs.Select(arg => PlaceholderRegex().Replace(arg, match =>
                resolved.TryGetValue(match.Groups["name"].Value, out var v) ? v : match.Value)),
        ];
    }
}

