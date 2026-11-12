using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TestEngineer.Core.Abstractions;
using TestEngineer.Core.Apk;
using TestEngineer.Core.Media;
using TestEngineer.Core.Preset;
using TestEngineer.Desktop.Services;
using TestEngineer.Infrastructure.Tools;

namespace TestEngineer.Desktop.ViewModels;

public sealed partial class DeveloperPageViewModel : ObservableObject
{
    private readonly IAppSettings _settings;
    private readonly IToolLocator _tools;
    private readonly List<string> _bootFiles = [];

    public DeveloperPageViewModel(IAppSettings settings, IToolLocator tools)
    {
        _settings = settings;
        _tools = tools;
        _settings.Current.SigningProfiles ??= [];
        ReloadSigningProfiles();
    }

    public ObservableCollection<SigningProfileRow> SigningProfiles { get; } = [];

    public ObservableCollection<string> AnalyzeOverviewLines { get; } = [];

    public ObservableCollection<string> AnalyzeMainEntries { get; } = [];

    public ObservableCollection<string> AnalyzeActivities { get; } = [];

    public ObservableCollection<string> AnalyzeServices { get; } = [];

    public ObservableCollection<string> AnalyzeProviders { get; } = [];

    public ObservableCollection<string> AnalyzePermissions { get; } = [];

    [ObservableProperty] private string _section = "home";
    [ObservableProperty] private string _imageFormat = "bmp";
    [ObservableProperty] private string _convertSource = "";
    [ObservableProperty] private string _outputName = "logo.bmp";
    [ObservableProperty] private string _bootSourceText = "";
    [ObservableProperty] private string _fpsText = "30";
    [ObservableProperty] private string _convertStatus = "选择一张图片，另存为开机 Logo 或壁纸。";
    [ObservableProperty] private string _bootStatus = "按文件名中的数字排序。part0 播放一次，part1 停在最后一帧。";
    [ObservableProperty] private string _presetApk = "";
    [ObservableProperty] private string _moduleName = "";
    [ObservableProperty] private string _buildFile = "mk";
    [ObservableProperty] private string _certificate = "presigned";
    [ObservableProperty] private bool _dexPreopt;
    [ObservableProperty] private string _presetStatus = "选择 APK，填写模块名后生成到本机目录。";
    [ObservableProperty] private string _analyzeApk = "";
    [ObservableProperty] private string _analyzeStatus = "选择一个 APK。";
    [ObservableProperty] private bool _hasAnalyzeResult;
    [ObservableProperty] private bool _analyzeActivitiesExpanded = true;
    [ObservableProperty] private bool _analyzeServicesExpanded;
    [ObservableProperty] private bool _analyzeProvidersExpanded;
    [ObservableProperty] private bool _analyzePermissionsExpanded;
    [ObservableProperty] private string _analyzeActivitiesTitle = "界面";
    [ObservableProperty] private string _analyzeServicesTitle = "服务";
    [ObservableProperty] private string _analyzeProvidersTitle = "内容提供者";
    [ObservableProperty] private string _analyzePermissionsTitle = "权限";
    [ObservableProperty] private string _signApk = "";
    [ObservableProperty] private string _signKeystore = "";
    [ObservableProperty] private string _signAlias = "";
    [ObservableProperty] private string _storePassword = "";
    [ObservableProperty] private string _keyPassword = "";
    [ObservableProperty] private string _signStatus = "选择 APK 和签名文件。用过的签名会留在列表里。";
    [ObservableProperty] private bool _hasSigningProfiles;
    [ObservableProperty] private bool _hasSelectedProfile;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private double _bootProgress;
    [ObservableProperty] private bool _showBootProgress;

    public bool IsHome => Section == "home";

    public bool IsImages => Section == "images";

    public bool IsApps => Section == "apps";

    public bool IsPreset => Section == "preset";

    public bool IsAnalyze => Section == "analyze";

    public bool IsSign => Section == "sign";

    public string PageTitle => Section switch
    {
        "images" => "图片处理",
        "apps" => "应用处理",
        "preset" => "生成预置包",
        "analyze" => "应用分析",
        "sign" => "重新签名",
        _ => "开发者选项",
    };

    public string PageSubtitle => Section switch
    {
        "images" => "格式转换与开机动画。结果另存到本机。",
        "apps" => "预置包、应用分析、重新签名。",
        "preset" => "从 APK 生成 Android.mk 或 Android.bp 预置模块。",
        "analyze" => "统计界面、服务、内容提供者、权限，并标出主入口。",
        "sign" => "用签名文件重新签名。文件不在时会提示。",
        _ => "选择一项继续。",
    };

    partial void OnSectionChanged(string value)
    {
        OnPropertyChanged(nameof(IsHome));
        OnPropertyChanged(nameof(IsImages));
        OnPropertyChanged(nameof(IsApps));
        OnPropertyChanged(nameof(IsPreset));
        OnPropertyChanged(nameof(IsAnalyze));
        OnPropertyChanged(nameof(IsSign));
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageSubtitle));
    }

    partial void OnIsBusyChanged(bool value) => RefreshCommands();

    partial void OnImageFormatChanged(string value)
    {
        if (value == "bmp")
        {
            if (OutputName == "wallpaper_01.png")
            {
                OutputName = "logo.bmp";
            }
        }
        else if (OutputName == "logo.bmp")
        {
            OutputName = "wallpaper_01.png";
        }
    }

    private bool CanWork() => !IsBusy;

    private void RefreshCommands()
    {
        BrowseConvertSourceCommand.NotifyCanExecuteChanged();
        BuildConvertCommand.NotifyCanExecuteChanged();
        BrowseBootFilesCommand.NotifyCanExecuteChanged();
        BrowseBootFolderCommand.NotifyCanExecuteChanged();
        BuildBootCommand.NotifyCanExecuteChanged();
        BrowsePresetApkCommand.NotifyCanExecuteChanged();
        GeneratePresetCommand.NotifyCanExecuteChanged();
        BrowseAnalyzeApkCommand.NotifyCanExecuteChanged();
        AnalyzeCommand.NotifyCanExecuteChanged();
        BrowseSignApkCommand.NotifyCanExecuteChanged();
        BrowseSignKeystoreCommand.NotifyCanExecuteChanged();
        SignCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void OpenSection(string section)
    {
        if (section is "images" or "apps" or "preset" or "analyze" or "sign")
        {
            Section = section;
        }
    }

    [RelayCommand]
    private void Back() => Section = Section is "preset" or "analyze" or "sign" ? "apps" : "home";

    [RelayCommand]
    private void SetBuildFile(string kind)
    {
        if (kind is "mk" or "bp")
        {
            BuildFile = kind;
        }
    }

    [RelayCommand]
    private void SetCertificate(string certificate)
    {
        if (certificate is "presigned" or "platform" or "shared" or "media")
        {
            Certificate = certificate;
        }
    }

    [RelayCommand]
    private void SetDex(string enabled) =>
        DexPreopt = string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase);

    [RelayCommand(CanExecute = nameof(CanWork))]
    private void SetImageFormat(string format)
    {
        if (format is "bmp" or "png")
        {
            ImageFormat = format;
        }
    }

    [RelayCommand(CanExecute = nameof(CanWork))]
    private void BrowseConvertSource()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择图片",
            Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.gif",
        };
        if (ModalSession.Show(dialog) != true)
        {
            return;
        }

        ConvertSource = dialog.FileName;
        ConvertStatus = "已选择 " + dialog.FileName;
    }

    [RelayCommand(CanExecute = nameof(CanWork))]
    private async Task BuildConvertAsync()
    {
        if (string.IsNullOrWhiteSpace(ConvertSource))
        {
            ModalSession.Alert("先选择图片。", "格式转换", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var format = ImageFormat == "png" ? "png" : "bmp";
        var name = string.IsNullOrWhiteSpace(OutputName)
            ? (format == "bmp" ? "logo.bmp" : "wallpaper_01.png")
            : OutputName.Trim();
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            ModalSession.Alert("输出名不能包含路径或特殊字符。", "格式转换", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!name.EndsWith("." + format, StringComparison.OrdinalIgnoreCase))
        {
            name = Path.GetFileNameWithoutExtension(name) + "." + format;
        }

        IsBusy = true;
        ConvertStatus = "正在转换…";
        byte[]? bytes = null;
        string? error = null;
        try
        {
            var source = ConvertSource;
            bytes = await Task.Run(() => DeveloperMedia.Transcode(source, format));
        }
        catch (MediaBuildException ex)
        {
            error = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }

        if (error is not null || bytes is null)
        {
            ConvertStatus = "转换失败";
            ModalSession.Alert(error ?? "转换失败。", "格式转换", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var save = new SaveFileDialog
        {
            Title = format == "bmp" ? "保存开机 Logo" : "保存壁纸",
            FileName = name,
            Filter = format == "bmp" ? "BMP 图片|*.bmp" : "PNG 图片|*.png",
            DefaultExt = format,
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (ModalSession.Show(save) != true)
        {
            ConvertStatus = "未保存";
            return;
        }

        try
        {
            var saved = save.FileName;
            await Task.Run(() => File.WriteAllBytes(saved, bytes));
            var companionName = WallpaperNames.SmallCompanion(Path.GetFileName(saved));
            string? companionPath = null;
            if (!string.IsNullOrEmpty(companionName))
            {
                companionPath = Path.Combine(Path.GetDirectoryName(saved)!, companionName);
                if (!string.Equals(companionPath, saved, StringComparison.OrdinalIgnoreCase))
                {
                    await Task.Run(() => File.WriteAllBytes(companionPath, bytes));
                }
                else
                {
                    companionPath = null;
                }
            }

            var summary = companionPath is null ? saved : saved + "\n" + companionPath;
            ConvertStatus = "已生成 " + summary.Replace('\n', '；');
            ModalSession.Alert("生成成功。\n\n已写出：\n" + summary, "格式转换", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ConvertStatus = "写入失败";
            ModalSession.Alert("生成失败。\n原因：无法写入文件。\n" + ex.Message, "格式转换", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand(CanExecute = nameof(CanWork))]
    private void BrowseBootFiles()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择图片",
            Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.webp",
            Multiselect = true,
        };
        if (ModalSession.Show(dialog) != true || dialog.FileNames.Length == 0)
        {
            return;
        }

        _bootFiles.Clear();
        _bootFiles.AddRange(dialog.FileNames);
        BootSourceText = _bootFiles.Count == 1 ? _bootFiles[0] : $"已选 {_bootFiles.Count} 张图片";
        BootStatus = $"已选择 {_bootFiles.Count} 张图片";
    }

    [RelayCommand(CanExecute = nameof(CanWork))]
    private void BrowseBootFolder()
    {
        var dialog = new OpenFolderDialog { Title = "选择图片文件夹" };
        if (ModalSession.Show(dialog) != true || string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            return;
        }

        _bootFiles.Clear();
        _bootFiles.AddRange(Directory.EnumerateFiles(dialog.FolderName).Where(path => DeveloperMedia.IsImageName(path)));
        BootSourceText = dialog.FolderName;
        BootStatus = $"文件夹中有 {_bootFiles.Count} 张图片";
    }

    [RelayCommand(CanExecute = nameof(CanWork))]
    private async Task BuildBootAsync()
    {
        if (_bootFiles.Count == 0)
        {
            BootStatus = "生成失败：还没有选择图片";
            ModalSession.Alert("生成失败。\n原因：还没有选择图片或文件夹。", "生成开机动画", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var fps = int.TryParse(FpsText, out var parsed) ? parsed : 0;
        IsBusy = true;
        ShowBootProgress = true;
        BootProgress = 0;
        BootStatus = "正在准备图片…";
        var progress = new Progress<(int Done, int Total)>(pair =>
        {
            BootProgress = pair.Total <= 0 ? 0 : pair.Done * 100d / pair.Total;
            BootStatus = $"正在转换第 {pair.Done} / {pair.Total} 张";
        });
        byte[]? zip = null;
        string? summary = null;
        string? error = null;
        try
        {
            var files = _bootFiles.ToArray();
            (zip, summary) = await Task.Run(() => DeveloperMedia.BuildBoot(files, fps, progress, CancellationToken.None));
        }
        catch (MediaBuildException ex)
        {
            error = ex.Message;
        }
        finally
        {
            IsBusy = false;
            ShowBootProgress = false;
        }

        if (error is not null || zip is null || summary is null)
        {
            BootStatus = "生成失败：" + error;
            ModalSession.Alert("生成失败。\n原因：" + error, "生成开机动画", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var save = new SaveFileDialog
        {
            Title = "保存开机动画",
            FileName = "bootanimation.zip",
            Filter = "开机动画|*.zip",
            DefaultExt = "zip",
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (ModalSession.Show(save) != true)
        {
            BootStatus = "未保存";
            return;
        }

        try
        {
            var path = save.FileName;
            await Task.Run(() => File.WriteAllBytes(path, zip));
            BootStatus = "生成成功：" + path;
            ModalSession.Alert("生成成功。\n" + summary + "\n\n已写出：\n" + path, "生成开机动画", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            BootStatus = "生成失败：无法写入";
            ModalSession.Alert("生成失败。\n原因：无法写入文件。\n" + ex.Message, "生成开机动画", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand(CanExecute = nameof(CanWork))]
    private void BrowsePresetApk()
    {
        var picker = new OpenFileDialog
        {
            Filter = "Android Package|*.apk",
            Title = "选择要生成预置包的 APK",
        };
        if (ModalSession.Show(picker) != true)
        {
            return;
        }

        ApplyPresetApkPath(picker.FileName);
    }

    public void ApplyPresetApkPath(string fileName)
    {
        if (IsBusy || string.IsNullOrWhiteSpace(fileName) || !File.Exists(fileName))
        {
            return;
        }

        if (!fileName.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
        {
            PresetStatus = "请拖入 APK 文件";
            return;
        }

        var previousDefault = string.IsNullOrWhiteSpace(PresetApk) ? "" : PresetPackage.DefaultModuleName(PresetApk);
        PresetApk = fileName;
        var nextDefault = PresetPackage.DefaultModuleName(PresetApk);
        if (string.IsNullOrWhiteSpace(ModuleName) || ModuleName == previousDefault)
        {
            ModuleName = nextDefault;
        }

        PresetStatus = "已选择 " + fileName;
    }

    [RelayCommand(CanExecute = nameof(CanWork))]
    private async Task GeneratePresetAsync()
    {
        if (string.IsNullOrWhiteSpace(PresetApk) || !File.Exists(PresetApk))
        {
            ModalSession.Alert("先选择 APK。", "生成预置包", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var moduleName = ModuleName.Trim();
        if (!PresetPackage.IsValidModuleName(moduleName))
        {
            ModalSession.Alert("模块名只能包含字母、数字、下划线、点和短横线。", "生成预置包", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var buildFile = BuildFile == "bp" ? PresetBuildFile.Bp : PresetBuildFile.Mk;
        var certificate = Certificate switch
        {
            "platform" => PresetCertificate.Platform,
            "shared" => PresetCertificate.Shared,
            "media" => PresetCertificate.Media,
            _ => PresetCertificate.Presigned,
        };

        var folder = new OpenFolderDialog { Title = "选择预置包的上级目录" };
        if (ModalSession.Show(folder) != true || string.IsNullOrWhiteSpace(folder.FolderName))
        {
            return;
        }

        var target = Path.Combine(folder.FolderName, moduleName);
        if (Directory.Exists(target)
            && ModalSession.Alert(
                "目录已存在，是否覆盖？\n" + target,
                "生成预置包",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        var options = new PresetPackageOptions(
            PresetApk,
            moduleName,
            folder.FolderName,
            buildFile,
            certificate,
            DexPreopt);

        IsBusy = true;
        PresetStatus = "正在生成预置包…";
        PresetPackageResult? result = null;
        string? error = null;
        try
        {
            result = await Task.Run(() => PresetPackage.Generate(options));
        }
        catch (PresetPackageException ex)
        {
            error = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }

        if (error is not null || result is null)
        {
            PresetStatus = "生成预置包失败";
            ModalSession.Alert(error ?? "生成失败。", "生成预置包", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var summary = new StringBuilder();
        summary.Append("已生成 ").Append(result.BuildFileName).Append('\n').Append(result.OutputDir);
        if (options.BuildFile == PresetBuildFile.Mk)
        {
            summary.Append(string.IsNullOrEmpty(result.Abi)
                ? "\n未发现可抽离的 so。"
                : $"\n已抽离 {result.Abi}，共 {result.SoCount} 个 so。");
        }
        else
        {
            summary.Append("\nso 保留在 APK 内，由 Soong 处理。");
        }

        PresetStatus = "已生成预置包 " + options.ModuleName;
        ModalSession.Alert(summary.ToString(), "生成预置包", MessageBoxButton.OK, MessageBoxImage.Information);
        Process.Start(new ProcessStartInfo
        {
            FileName = result.OutputDir,
            UseShellExecute = true,
        });
    }

    [RelayCommand(CanExecute = nameof(CanWork))]
    private async Task BrowseAnalyzeApkAsync()
    {
        var picker = new OpenFileDialog
        {
            Title = "选择要分析的 APK",
            Filter = "Android Package|*.apk",
        };
        if (ModalSession.Show(picker) != true)
        {
            return;
        }

        await ApplyAnalyzeApkAsync(picker.FileName);
    }

    public async Task ApplyAnalyzeApkAsync(string fileName)
    {
        if (IsBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(fileName) || !File.Exists(fileName)
            || !fileName.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
        {
            AnalyzeStatus = "请拖入 APK 文件";
            return;
        }

        AnalyzeApk = fileName;
        await AnalyzeAsync();
    }

    [RelayCommand(CanExecute = nameof(CanWork))]
    private async Task AnalyzeAsync()
    {
        if (string.IsNullOrWhiteSpace(AnalyzeApk) || !File.Exists(AnalyzeApk))
        {
            ModalSession.Alert("先选择 APK。", "应用分析", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var path = AnalyzeApk;
        IsBusy = true;
        AnalyzeStatus = "正在读取清单…";
        try
        {
            var report = await Task.Run(() => ApkManifest.Read(path));
            ShowAnalyzeGroups(report);
            AnalyzeStatus = "分析完成";
        }
        catch (ApkManifestException ex)
        {
            ClearAnalyzeGroups();
            AnalyzeStatus = "分析失败";
            ModalSession.Alert(ex.Message, "应用分析", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ShowAnalyzeGroups(ApkManifestReport report)
    {
        ReplaceLines(AnalyzeOverviewLines,
        [
            "包名：" + (string.IsNullOrEmpty(report.PackageName) ? "（未读出）" : report.PackageName),
            $"界面：{report.Activities.Count} 个",
            $"服务：{report.Services.Count} 个",
            $"内容提供者：{report.Providers.Count} 个",
            $"权限：{report.Permissions.Count} 个",
            "android:sharedUserId=\"android.uid.system\"：" + (report.IsSystemSharedUser ? "是" : "否"),
        ]);
        if (!report.IsSystemSharedUser && !string.IsNullOrEmpty(report.SharedUserId))
        {
            AnalyzeOverviewLines.Add("当前 sharedUserId：" + report.SharedUserId);
        }

        var mains = report.Activities.Where(activity => activity.IsMainEntry).ToList();
        if (mains.Count == 0)
        {
            ReplaceLines(AnalyzeMainEntries, ["（没有 ACTION_MAIN + CATEGORY_LAUNCHER）"]);
        }
        else
        {
            ReplaceLines(AnalyzeMainEntries, mains.Select(activity => activity.IsAlias ? activity.Name + "（别名）" : activity.Name));
        }

        ReplaceLines(AnalyzeActivities, report.Activities.Count == 0
            ? ["（无）"]
            : report.Activities.Select(FormatActivity));
        ReplaceLines(AnalyzeServices, report.Services.Count == 0 ? ["（无）"] : report.Services);
        ReplaceLines(AnalyzeProviders, report.Providers.Count == 0 ? ["（无）"] : report.Providers);
        ReplaceLines(AnalyzePermissions, report.Permissions.Count == 0 ? ["（无）"] : report.Permissions);

        AnalyzeActivitiesTitle = $"界面 · {report.Activities.Count}";
        AnalyzeServicesTitle = $"服务 · {report.Services.Count}";
        AnalyzeProvidersTitle = $"内容提供者 · {report.Providers.Count}";
        AnalyzePermissionsTitle = $"权限 · {report.Permissions.Count}";
        AnalyzeActivitiesExpanded = true;
        AnalyzeServicesExpanded = false;
        AnalyzeProvidersExpanded = false;
        AnalyzePermissionsExpanded = false;
        HasAnalyzeResult = true;
    }

    private void ClearAnalyzeGroups()
    {
        AnalyzeOverviewLines.Clear();
        AnalyzeMainEntries.Clear();
        AnalyzeActivities.Clear();
        AnalyzeServices.Clear();
        AnalyzeProviders.Clear();
        AnalyzePermissions.Clear();
        HasAnalyzeResult = false;
    }

    private static void ReplaceLines(ObservableCollection<string> target, IEnumerable<string> lines)
    {
        target.Clear();
        foreach (var line in lines)
        {
            target.Add(line);
        }
    }

    private static string FormatActivity(ApkComponent activity)
    {
        var name = activity.IsAlias ? activity.Name + "（别名）" : activity.Name;
        return activity.IsMainEntry ? "主入口 " + name : name;
    }

    [RelayCommand(CanExecute = nameof(CanWork))]
    private void BrowseSignApk()
    {
        var picker = new OpenFileDialog
        {
            Title = "选择要重新签名的 APK",
            Filter = "Android Package|*.apk",
        };
        if (ModalSession.Show(picker) != true)
        {
            return;
        }

        ApplySignApkPath(picker.FileName);
    }

    public void ApplySignApkPath(string fileName)
    {
        if (IsBusy || string.IsNullOrWhiteSpace(fileName) || !File.Exists(fileName))
        {
            return;
        }

        if (!fileName.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
        {
            SignStatus = "请把 APK 拖到这里";
            return;
        }

        SignApk = fileName;
        SignStatus = "已选择 " + fileName;
    }

    [RelayCommand(CanExecute = nameof(CanWork))]
    private void BrowseSignKeystore()
    {
        var picker = new OpenFileDialog
        {
            Title = "选择签名文件",
            Filter = "签名文件|*.jks;*.keystore;*.p12;*.pfx|所有文件|*.*",
        };
        if (ModalSession.Show(picker) != true)
        {
            return;
        }

        ApplySignKeystorePath(picker.FileName);
    }

    public void ApplySignKeystorePath(string fileName)
    {
        if (IsBusy || string.IsNullOrWhiteSpace(fileName) || !File.Exists(fileName))
        {
            return;
        }

        if (fileName.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
        {
            SignStatus = "请把签名文件拖到这里";
            return;
        }

        SignKeystore = fileName;
        SigningProfile? existing = null;
        try
        {
            var fullPath = Path.GetFullPath(fileName);
            existing = _settings.Current.SigningProfiles.FirstOrDefault(item =>
                string.Equals(item.KeystorePath, fullPath, StringComparison.OrdinalIgnoreCase));
        }
        catch (ArgumentException)
        {
            existing = null;
        }

        if (existing is null)
        {
            SignAlias = "";
            StorePassword = "";
            KeyPassword = "";
            foreach (var row in SigningProfiles)
            {
                row.IsSelected = false;
            }

            HasSelectedProfile = false;
            SignStatus = "已选择 " + fileName;
            return;
        }

        LoadProfile(existing);
    }

    [RelayCommand]
    private void SelectSigningProfile(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        var profile = _settings.Current.SigningProfiles.FirstOrDefault(item => item.Id == id);
        if (profile is not null)
        {
            LoadProfile(profile);
        }
    }

    [RelayCommand]
    private void RemoveSigningProfile()
    {
        var selected = SigningProfiles.FirstOrDefault(row => row.IsSelected);
        if (selected is null)
        {
            return;
        }

        _settings.Current.SigningProfiles = _settings.Current.SigningProfiles
            .Where(item => item.Id != selected.Id)
            .ToList();
        _settings.Save();
        ReloadSigningProfiles();
        SignStatus = "已从历史中移除";
    }

    [RelayCommand(CanExecute = nameof(CanWork))]
    private async Task SignAsync()
    {
        if (string.IsNullOrWhiteSpace(SignApk) || !File.Exists(SignApk))
        {
            ModalSession.Alert("先选择 APK。", "重新签名", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(SignKeystore))
        {
            ModalSession.Alert("先选择签名文件。", "重新签名", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!RememberCurrentProfile())
        {
            return;
        }

        if (!File.Exists(SignKeystore))
        {
            SignStatus = "签名文件不存在";
            ModalSession.Alert("签名文件不存在：\n" + SignKeystore + "\n请重新选择。", "重新签名", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var apksigner = _tools.FindApksigner();
        if (string.IsNullOrWhiteSpace(apksigner))
        {
            SignStatus = "未找到 apksigner";
            ModalSession.Alert("未找到 apksigner。\n请安装 Android SDK Build-Tools，或设置 ANDROID_HOME。", "重新签名", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var alias = SignAlias.Trim();
        if (alias.Length == 0)
        {
            IsBusy = true;
            SignStatus = "正在读取密钥别名…";
            try
            {
                var aliases = await ApkSigning.ListAliasesAsync(SignKeystore, StorePassword);
                if (aliases.Count == 1)
                {
                    alias = aliases[0];
                    SignAlias = alias;
                    if (!RememberCurrentProfile())
                    {
                        return;
                    }
                }
                else if (aliases.Count == 0)
                {
                    SignStatus = "没有读出密钥别名";
                    ModalSession.Alert("没有读出密钥别名。请填写别名。", "重新签名", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                else
                {
                    SignStatus = "请填写密钥别名";
                    ModalSession.Alert("这个签名文件有多个别名，请填写其中一个：\n" + string.Join('\n', aliases), "重新签名", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }
            catch (ApkSigningException ex)
            {
                SignStatus = "无法读取签名文件";
                ModalSession.Alert(ex.Message, "重新签名", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            finally
            {
                IsBusy = false;
            }
        }

        var save = new SaveFileDialog
        {
            Title = "保存签名后的 APK",
            FileName = Path.GetFileNameWithoutExtension(SignApk) + "-signed.apk",
            Filter = "Android Package|*.apk",
            DefaultExt = "apk",
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = Path.GetDirectoryName(SignApk),
        };
        if (ModalSession.Show(save) != true)
        {
            SignStatus = "未签名";
            return;
        }

        var output = save.FileName;
        var replaceSource = string.Equals(Path.GetFullPath(output), Path.GetFullPath(SignApk), StringComparison.OrdinalIgnoreCase);
        var target = replaceSource ? output + ".signing.tmp" : output;
        IsBusy = true;
        SignStatus = "正在签名…";
        try
        {
            var request = new ApkSignRequest(
                SignApk,
                target,
                Path.GetFullPath(SignKeystore),
                alias,
                StorePassword,
                string.IsNullOrEmpty(KeyPassword) ? null : KeyPassword);
            await ApkSigning.SignAsync(apksigner, request);
            if (replaceSource)
            {
                File.Move(target, output, overwrite: true);
            }

            SignStatus = "已签名 " + output;
            ModalSession.Alert("签名完成。\n" + output, "重新签名", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (ApkSigningException ex)
        {
            DeleteSigningTemp(replaceSource, target);
            SignStatus = "签名失败";
            ModalSession.Alert(ex.Message, "重新签名", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DeleteSigningTemp(replaceSource, target);
            SignStatus = "签名失败";
            ModalSession.Alert("签名失败。\n无法写入文件。\n" + ex.Message, "重新签名", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool RememberCurrentProfile()
    {
        try
        {
            var profile = new SigningProfile
            {
                KeystorePath = Path.GetFullPath(SignKeystore),
                Alias = SignAlias.Trim(),
                StorePasswordProtected = SecretBox.Protect(StorePassword),
                KeyPasswordProtected = SecretBox.Protect(KeyPassword),
            };
            _settings.Current.SigningProfiles = SigningHistory.Upsert(_settings.Current.SigningProfiles, profile);
            _settings.Save();
            ReloadSigningProfiles(profile.Id);
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            SignStatus = "无法保存签名配置";
            ModalSession.Alert("无法把签名配置写入本机设置。\n" + ex.Message, "重新签名", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private void LoadProfile(SigningProfile profile)
    {
        SignKeystore = profile.KeystorePath;
        SignAlias = profile.Alias;
        try
        {
            StorePassword = SecretBox.Unprotect(profile.StorePasswordProtected);
            KeyPassword = SecretBox.Unprotect(profile.KeyPasswordProtected);
            SignStatus = File.Exists(profile.KeystorePath)
                ? "已选择 " + profile.KeystorePath
                : "已选择的签名文件不存在，签名时会提示。";
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            StorePassword = "";
            KeyPassword = "";
            SignStatus = "无法读取已保存的密码，请重新填写。";
        }

        foreach (var row in SigningProfiles)
        {
            row.IsSelected = row.Id == profile.Id;
        }

        HasSelectedProfile = SigningProfiles.Any(row => row.IsSelected);
    }

    private void ReloadSigningProfiles(string? selectedId = null)
    {
        SigningProfiles.Clear();
        foreach (var profile in _settings.Current.SigningProfiles)
        {
            var name = string.IsNullOrWhiteSpace(profile.KeystorePath) ? "（未命名）" : Path.GetFileName(profile.KeystorePath);
            var label = string.IsNullOrWhiteSpace(profile.Alias) ? name : name + "  ·  " + profile.Alias;
            if (!File.Exists(profile.KeystorePath))
            {
                label += "（文件不存在）";
            }

            SigningProfiles.Add(new SigningProfileRow
            {
                Id = profile.Id,
                Label = label,
                ToolTip = profile.KeystorePath,
                IsSelected = profile.Id == selectedId,
            });
        }

        HasSigningProfiles = SigningProfiles.Count > 0;
        HasSelectedProfile = selectedId is not null && SigningProfiles.Any(row => row.IsSelected);
    }

    private static void DeleteSigningTemp(bool replaceSource, string target)
    {
        if (!replaceSource || !File.Exists(target))
        {
            return;
        }

        try
        {
            File.Delete(target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

public sealed partial class SigningProfileRow : ObservableObject
{
    public required string Id { get; init; }

    public required string Label { get; init; }

    public required string ToolTip { get; init; }

    [ObservableProperty]
    private bool _isSelected;
}
