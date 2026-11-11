using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TestEngineer.Core.Abstractions;
using TestEngineer.Core.Settings;

namespace TestEngineer.Infrastructure.Settings;

/// <summary>
/// 以 settings.json 持久化设置：带 version 字段，写入走临时文件替换，文件损坏时备份后回落默认值。
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _filePath;
    private readonly ILogger _logger;

    public JsonSettingsStore(string filePath, ILogger<JsonSettingsStore>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
        _logger = logger ?? NullLogger<JsonSettingsStore>.Instance;
    }

    public AppSettings Load()
    {
        if (!File.Exists(_filePath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions)
                ?? throw new JsonException("settings.json 内容为空。");
            return Migrate(settings);
        }
        catch (JsonException ex)
        {
            var backup = $"{_filePath}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}";
            _logger.LogWarning(ex, "设置文件损坏，已备份为 {Backup} 并使用默认设置", backup);
            File.Move(_filePath, backup, overwrite: true);
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Version = AppSettings.CurrentVersion;

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, SerializerOptions));
            if (File.Exists(_filePath))
            {
                File.Replace(tempPath, _filePath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, _filePath);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private AppSettings Migrate(AppSettings settings)
    {
        if (settings.Version > AppSettings.CurrentVersion)
        {
            _logger.LogWarning(
                "设置文件版本 {Found} 高于当前支持的 {Supported}，按已知字段读取",
                settings.Version,
                AppSettings.CurrentVersion);
            return settings;
        }

        // 版本迁移链：每个旧版本在此升级一级。当前仅 v1，缺省或 0 视为 v1。
        if (settings.Version < 1)
        {
            settings.Version = 1;
        }

        // JSON 中显式写 null 时补默认对象，调用方无需判空。
        settings.ToolPaths ??= new ToolPathSettings();
        return settings;
    }
}
